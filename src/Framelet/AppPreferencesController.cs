using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Framelet;

public sealed class AppPreferencesController : IDisposable
{
    private readonly AppPreferencesFile file;
    private readonly IUpdateSource updates;
    private readonly IStartupRegistration startup;
    private readonly Func<DateTimeOffset> now;
    private readonly Action<string> open;
    private readonly CancellationTokenSource lifetime = new();
    private readonly IDisposable? ownedClient;
    private bool startupBusy, disposed;
    public string InstalledVersion { get; }
    public string UpdateSourceName => updates.Name;
    public bool IsStoreInstall { get; }
    public UpdatePreferences Preferences { get; private set; }
    public UpdateState Update { get; private set; } = new();
    public StartupStatus Startup { get; private set; } = new(false, false, "Startup status has not been checked yet.");
    public bool StartupBusy => startupBusy;
    public event Action? Changed;

    internal AppPreferencesController(AppPreferencesFile file, IUpdateSource updates, IStartupRegistration startup,
        string installedVersion, bool store, Func<DateTimeOffset>? now = null, Action<string>? open = null, IDisposable? ownedClient = null)
    {
        this.file = file; this.updates = updates; this.startup = startup; InstalledVersion = installedVersion; IsStoreInstall = store;
        this.now = now ?? (() => DateTimeOffset.UtcNow); this.open = open ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })); this.ownedClient = ownedClient;
        try
        {
            Preferences = file.Load();
            if (Preferences.CheckedVersion == InstalledVersion && Preferences.LastChecked is { } checkedAt)
            {
                var cached = Preferences.AvailableUpdate;
                bool eligible = cached is not null && (!cached.IsPreview || Preferences.IncludePreviews) &&
                    (store ? cached.Url == StoreUpdateSource.StoreUrl && cached.Key == "store:" + InstalledVersion
                        : updates.Name == "GitHub releases" && cached.Url == GitHubUpdateSource.ReleasePrefix + Uri.EscapeDataString("v" + cached.Version) &&
                          cached.Key == "github:v" + cached.Version && ReleaseVersion.Parse(cached.Version) is { } version && version.CompareTo(ReleaseVersion.Parse(InstalledVersion)) > 0);
                // Preserve a known notice across restarts during the daily check interval. A changed installed version invalidates it.
                Update = new(false, eligible ? cached : null, eligible ? "An update is available. Check again for the latest availability." : "Not checked in this session.", checkedAt);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { Preferences = new(AutomaticChecks: false); Update = new(Message: "App preferences could not be read. Automatic checks are off until you review your update settings."); }
    }
    internal static AppPreferencesController Create(string dataRoot)
    {
        var kind = Installation.Kind();
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        string normal = AppIdentity.DataRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        bool isolated = !Path.GetFullPath(dataRoot).Equals(Path.GetFullPath(normal), StringComparison.OrdinalIgnoreCase);
        IUpdateSource source = isolated ? new UnsupportedUpdateSource("Update checks are disabled for an isolated test profile.", "Isolated test profile")
            : kind switch { InstallKind.Store => new StoreUpdateSource(), InstallKind.OtherPackage => new UnsupportedUpdateSource(), _ => new GitHubUpdateSource(client) };
        IStartupRegistration registration = isolated ? new UnavailableStartupRegistration() : kind == InstallKind.GitHub
            ? new UnpackagedStartupRegistration(new StartupRegistryValues(), Environment.ProcessPath) : new PackagedStartupRegistration();
        string version = typeof(AppPreferencesController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
        return new(new(dataRoot), source, registration, version, kind == InstallKind.Store, ownedClient: client);
    }
    public void SetUpdatePreferences(bool automatic, bool includePreviews)
    {
        ThrowIfDisposed();
        var preferences = Preferences with { AutomaticChecks = automatic, IncludePreviews = includePreviews,
            AvailableUpdate = !includePreviews && Preferences.AvailableUpdate?.IsPreview == true ? null : Preferences.AvailableUpdate };
        file.Save(preferences); Preferences = preferences;
        if (!includePreviews && Update.Offer?.IsPreview == true) Update = Update with { Offer = null, Message = "Preview updates are off. Check again for an official release." };
        Changed?.Invoke();
    }
    internal Task CheckAutomaticallyAsync()
    {
        if (disposed || !Preferences.AutomaticChecks || updates is UnsupportedUpdateSource) return Task.CompletedTask;
        bool succeeded = Preferences.LastChecked is { } success && (Preferences.LastAttempt is null || success >= Preferences.LastAttempt);
        var interval = succeeded ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
        if ((Preferences.LastAttempt ?? Preferences.LastChecked) is DateTimeOffset last && last <= now() && now() - last < interval) return Task.CompletedTask;
        return CheckUpdatesAsync();
    }
    internal void SetOwnerWindow(IntPtr handle) { if (updates is StoreUpdateSource store) store.OwnerWindow = handle; }
    public async Task CheckUpdatesAsync()
    {
        if (disposed || Update.Checking) return;
        Preferences = Preferences with { LastAttempt = now() };
        try { file.Save(Preferences); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A read-only data folder must not prevent a manual check. */ }
        Update = Update with { Checking = true, Message = "Checking for updates…" }; Changed?.Invoke();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            bool include = Preferences.IncludePreviews;
            var offer = await updates.CheckAsync(InstalledVersion, include, timeout.Token);
            if (disposed) return;
            if (include != Preferences.IncludePreviews)
            { Update = Update with { Checking = false, Message = "Update settings changed. Check again for the selected releases." }; return; }
            var checkedAt = now();
            Preferences = Preferences with { LastChecked = checkedAt, AvailableUpdate = offer, CheckedVersion = InstalledVersion };
            string message = offer is null ? "You're up to date." : IsStoreInstall ? "An update is available from Microsoft Store." : $"{offer.Version}{(offer.IsPreview ? " preview" : "")} is available from {offer.Source}.";
            Update = new(false, offer, message, checkedAt);
            try { file.Save(Preferences); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Update = Update with { Message = message + " Update preferences could not be saved." }; }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (disposed) return;
            Update = Update with { Checking = false, Message = ex is UpdateCheckUnavailableException ? ex.Message : "Couldn't check for updates. Try again later or check your connection." };
        }
        finally { if (!disposed) Changed?.Invoke(); }
    }
    public async Task RefreshStartupAsync()
    {
        if (disposed || startupBusy) return;
        startupBusy = true;
        Changed?.Invoke();
        try { Startup = await startup.ReadAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Startup = new(false, false, "Windows startup status could not be read. Try Windows Startup apps.", true); }
        finally { startupBusy = false; if (!disposed) Changed?.Invoke(); }
    }
    public async Task SetStartupAsync(bool enabled)
    {
        if (disposed || startupBusy) return;
        startupBusy = true;
        try { Startup = await startup.SetAsync(enabled); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Read back the real OS state; do not show the requested value after a rejected write.
            try { Startup = (await startup.ReadAsync()) with { Message = "Startup couldn't be changed. Review Windows Startup apps.", ManageInWindows = true }; }
            catch (Exception readError) when (readError is not OutOfMemoryException) { Startup = new(false, false, "Startup couldn't be changed. Review Windows Startup apps.", true); }
        }
        finally { startupBusy = false; if (!disposed) Changed?.Invoke(); }
    }
    public void OpenUpdate()
    {
        ThrowIfDisposed();
        if (Update.Offer is not { } offer) return;
        if (offer.Url != StoreUpdateSource.StoreUrl && !(offer.Url.StartsWith(GitHubUpdateSource.ReleasePrefix, StringComparison.Ordinal) &&
            Uri.TryCreate(offer.Url, UriKind.Absolute, out var uri) && uri.Host == "github.com" && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0))
            throw new InvalidOperationException("The update destination is invalid.");
        open(offer.Url);
    }
    public void OpenStartupSettings() { ThrowIfDisposed(); open("ms-settings:startupapps"); }
    internal string? TakeNotification()
    {
        if (disposed || !Preferences.AutomaticChecks || Update.Offer is not { } offer || Preferences.NotifiedVersion == offer.Key) return null;
        var preferences = Preferences with { NotifiedVersion = offer.Key };
        try { file.Save(preferences); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        Preferences = preferences;
        return IsStoreInstall ? "An update for Snipsera is available in Microsoft Store. Open Settings to view it."
            : $"Snipsera {offer.Version}{(offer.IsPreview ? " preview" : "")} is available. Open Settings to view the update.";
    }
    private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(AppPreferencesController)); }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); lifetime.Dispose(); ownedClient?.Dispose(); }
}
