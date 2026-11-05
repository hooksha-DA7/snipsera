using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Windows.ApplicationModel;
using Framelet;

internal static class PreferencesChecks
{
    internal sealed class UpdateFake : IUpdateSource
    {
        public string Name { get; set; } = "GitHub releases";
        public int Calls;
        public UpdateOffer? Offer;
        public Exception? Error;
        public TaskCompletionSource<UpdateOffer?>? Pending;
        public async Task<UpdateOffer?> CheckAsync(string version, bool previews, CancellationToken token)
        { Calls++; if (Error is not null) throw Error; return Pending is null ? Offer : await Pending.Task.WaitAsync(token); }
    }
    internal sealed class StartupFake : IStartupRegistration
    {
        public StartupStatus Status = new(false, true, "Off");
        public int Writes;
        public bool FailWrite;
        public TaskCompletionSource<StartupStatus>? PendingRead;
        public Task<StartupStatus> ReadAsync() => PendingRead?.Task ?? Task.FromResult(Status);
        public Task<StartupStatus> SetAsync(bool enabled)
        { Writes++; if (FailWrite) throw new UnauthorizedAccessException(); Status = Status with { Enabled = enabled }; return ReadAsync(); }
    }
    internal sealed class Values : IStartupValues
    {
        public string? Command { get; set; }
        public byte[]? Approval { get; set; }
        public int Writes;
        public void SetCommand(string command) { Writes++; Command = command; }
        public void RemoveCommand() { Writes++; Command = null; }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(handle(request)); }
    internal static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Wait(Task task)
    {
        var frame = new DispatcherFrame(); var start = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow - start > TimeSpan.FromSeconds(15)) { timer.Stop(); frame.Continue = false; } };
        if (!task.IsCompleted) { timer.Start(); Dispatcher.PushFrame(frame); }
        Require(task.IsCompleted, "Asynchronous check timed out"); task.GetAwaiter().GetResult();
    }
    internal static UpdateOffer GitHub(string version = "1.0.6", bool preview = false) => new(version, "GitHub", GitHubUpdateSource.ReleasePrefix + "v" + version, preview, "github:v" + version);
    internal static byte[] Feed(params (string Tag, bool Preview, bool Draft, bool Exe)[] rows) => JsonSerializer.SerializeToUtf8Bytes(rows.Select(r => new
    { tag_name = r.Tag, prerelease = r.Preview, draft = r.Draft, html_url = "https://untrusted.example/install.exe", assets = r.Exe ? new[] { new { name = "Snipsera-" + r.Tag[1..] + "-setup-win-x64.exe" } } : Array.Empty<object>() }));
    // Explicit opt-in smoke check. Keep external availability out of the offline release gate.
    internal static int Network(string output)
    {
        Directory.CreateDirectory(output);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        var source = new GitHubUpdateSource(client);
        var preview = source.CheckAsync("0.0.0", true, CancellationToken.None).GetAwaiter().GetResult();
        var official = source.CheckAsync("0.0.0", false, CancellationToken.None).GetAwaiter().GetResult();
        Require(preview is not null && official is { IsPreview: false }, "Public release feed did not provide both supported channels");
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { source = GitHubUpdateSource.ReleasesApi, verified_at = DateTimeOffset.UtcNow, preview, official, scope = "Anonymous metadata reads only; no startup writes, download or installation" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS Production GitHub source reads current preview and official releases anonymously");
        return 0;
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.Install(app);
        var outcomes = new List<object>(); int failures = 0;
        void Test(string name, Action check)
        {
            try { check(); outcomes.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; outcomes.Add(new { name, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        AppPreferencesFile FileStore() => new(Path.Combine(output, Guid.NewGuid().ToString("N")));
        Test("Update preferences default on while Windows startup has no automatic opt-in", () =>
        {
            var updates = new UpdateFake(); var startup = new StartupFake();
            using var controller = new AppPreferencesController(FileStore(), updates, startup, "1.0.5", false);
            Require(controller.Preferences.AutomaticChecks && controller.Preferences.IncludePreviews && startup.Writes == 0 && updates.Calls == 0, "Constructor changed Windows or contacted a service");
        });
        Test("Version ordering handles preview revisions and ignores build metadata", () =>
        {
            Require(ReleaseVersion.Parse("1.0.5-preview.10")!.CompareTo(ReleaseVersion.Parse("1.0.5-preview.2")) > 0, "Preview order is lexical");
            Require(ReleaseVersion.Parse("1.0.5")!.CompareTo(ReleaseVersion.Parse("1.0.5-preview.10")) > 0, "Official order wrong");
            Require(ReleaseVersion.Parse("1.0.5+abc")!.CompareTo(ReleaseVersion.Parse("1.0.5")) == 0, "Build metadata changed precedence");
            Require(ReleaseVersion.Parse("1.0.10")!.CompareTo(ReleaseVersion.Parse("1.0.9")) > 0 && ReleaseVersion.Parse("../../1.0.8") is null, "Invalid version handling");
        });
        Test("GitHub selects the highest newer eligible version rather than publication order", () =>
        {
            var data = Feed(("v1.0.6", false, false, true), ("v1.0.9", true, false, true), ("v1.0.7", false, false, true));
            Require(GitHubUpdateSource.Select(data, "1.0.5", true)?.Version == "1.0.9", "Newest preview not selected");
            Require(GitHubUpdateSource.Select(data, "1.0.5", false)?.Version == "1.0.7", "Stable selection wrong");
        });
        Test("Legacy preview-suffixed approved tags use GitHub's actual release classification", () =>
        {
            var offer = GitHubUpdateSource.Select(Feed(("v1.0.2-preview.2", false, false, true)), "1.0.1", false);
            Require(offer is { IsPreview: false } && offer.Version == "1.0.2-preview.2", "Approved historical build excluded");
        });
        Test("Drafts, missing installers and equal or older builds never offer an update", () =>
        {
            var data = Feed(("v1.0.9", true, true, true), ("v1.0.8", false, false, false), ("v1.0.5", false, false, true), ("v1.0.4", false, false, true));
            Require(GitHubUpdateSource.Select(data, "1.0.5", true) is null, "Draft or downgrade offered");
        });
        Test("Release destinations are constructed from the owned repository, not feed URLs", () =>
        {
            var offer = GitHubUpdateSource.Select(Feed(("v1.0.6", false, false, true)), "1.0.5", true);
            Require(offer?.Url == "https://github.com/hooksha-DA7/snipsera/releases/tag/v1.0.6", "Feed URL was trusted");
        });
        Test("Malformed release data is an error, never an up-to-date result", () =>
        {
            bool rejected = false; try { GitHubUpdateSource.Select(Encoding.UTF8.GetBytes("{}"), "1.0.5", true); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Invalid feed accepted");
        });
        Test("GitHub checks send only an anonymous fixed-repository GET", () =>
        {
            using var client = new HttpClient(new Handler(request =>
            {
                Require(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == GitHubUpdateSource.ReleasesApi && request.Content is null && request.Headers.Authorization is null, "Unexpected network payload or destination");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Feed(("v1.0.6", true, false, true))) };
            }));
            var task = new GitHubUpdateSource(client).CheckAsync("1.0.5", true, CancellationToken.None); Wait(task);
            Require(task.Result?.Version == "1.0.6", "Release response not parsed");
        });
        Test("Oversized update responses are rejected before parsing", () =>
        {
            using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2_000_001]) }));
            bool rejected = false; try { Wait(new GitHubUpdateSource(client).CheckAsync("1.0.5", true, CancellationToken.None)); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Unbounded response accepted");
        });
        Test("Store checks require this app identity and do not mistake framework updates for Snipsera", () =>
        {
            var packages = new[] { ("Microsoft.VCLibs.140.00.UWPDesktop", AppIdentity.StorePublisher), (AppIdentity.StoreName, "CN=Other") };
            var source = new StoreUpdateSource(_ => Task.FromResult(packages));
            var task = source.CheckAsync("1.0.5", true, CancellationToken.None); Wait(task); Require(task.Result is null, "Framework update was advertised");
            var appSource = new StoreUpdateSource(_ => Task.FromResult(new[] { (AppIdentity.StoreName, AppIdentity.StorePublisher) }));
            task = appSource.CheckAsync("1.0.5", true, CancellationToken.None); Wait(task);
            Require(task.Result is { IsPreview: false, Version: "Store update" } && task.Result.Url == StoreUpdateSource.StoreUrl, "Store update not routed to Store");
        });
        Test("An empty Store response means no app update without any GitHub fallback", () =>
        {
            var source = new StoreUpdateSource(_ => Task.FromResult(Array.Empty<(string, string)>()));
            var task = source.CheckAsync("1.0.5", false, CancellationToken.None); Wait(task); Require(task.Result is null, "Empty Store response misreported");
        });
        Test("Successful automatic checks run daily and failed checks retry after an hour", () =>
        {
            var time = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero); var source = new UpdateFake { Error = new HttpRequestException() }; var file = FileStore();
            using var controller = new AppPreferencesController(file, source, new StartupFake(), "1.0.5", false, () => time);
            Wait(controller.CheckAutomaticallyAsync()); Wait(controller.CheckAutomaticallyAsync()); Require(source.Calls == 1 && controller.Update.CheckedAt is null, "Failure bypassed daily limit");
            Require(!controller.Update.Message.Contains("up to date", StringComparison.OrdinalIgnoreCase), "Offline was reported as up to date");
            time = time.AddHours(1); Wait(controller.CheckAutomaticallyAsync()); Require(source.Calls == 2, "Hourly retry missing");
            Require(file.Load().LastAttempt == time, "Attempt cache not persisted");
            source.Error = null; time = time.AddHours(1); Wait(controller.CheckAutomaticallyAsync());
            time = time.AddHours(2); Wait(controller.CheckAutomaticallyAsync()); Require(source.Calls == 3, "Successful check retried too soon");
            time = time.AddHours(22); Wait(controller.CheckAutomaticallyAsync()); Require(source.Calls == 4, "Next-day check missing");
        });
        Test("Turning automatic checks off persists, while Check now still works", () =>
        {
            var file = FileStore(); var source = new UpdateFake();
            using var controller = new AppPreferencesController(file, source, new StartupFake(), "1.0.5", false);
            controller.SetUpdatePreferences(false, false); Wait(controller.CheckAutomaticallyAsync()); Require(source.Calls == 0, "Opt-out still contacted service");
            Wait(controller.CheckUpdatesAsync()); Require(source.Calls == 1 && !file.Load().AutomaticChecks && !file.Load().IncludePreviews, "Manual check or persistence failed");
        });
        Test("Concurrent checks coalesce and settings changed mid-request are not reported as up to date", () =>
        {
            var source = new UpdateFake { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            using var controller = new AppPreferencesController(FileStore(), source, new StartupFake(), "1.0.5", false);
            var task = controller.CheckUpdatesAsync(); Wait(controller.CheckUpdatesAsync()); Require(source.Calls == 1, "Concurrent network check");
            controller.SetUpdatePreferences(true, false); source.Pending.SetResult(GitHub("1.0.9", true)); Wait(task);
            Require(controller.Update.Offer is null && controller.Update.Message.Contains("settings changed"), "Stale preview result became up to date");
        });
        Test("Disposing cancels pending checks without a late UI state update", () =>
        {
            var source = new UpdateFake { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            using var controller = new AppPreferencesController(FileStore(), source, new StartupFake(), "1.0.5", false); int events = 0; controller.Changed += () => events++;
            var task = controller.CheckUpdatesAsync(); int before = events; controller.Dispose(); Wait(task); Require(events == before && controller.Update.Offer is null, "Late callback after exit");
        });
        Test("Update notices occur once per offered build and survive restarting preferences", () =>
        {
            var file = FileStore(); var source = new UpdateFake { Offer = GitHub("1.0.6", true) };
            using var controller = new AppPreferencesController(file, source, new StartupFake(), "1.0.5", false);
            Wait(controller.CheckUpdatesAsync()); Require(controller.TakeNotification() is not null && controller.TakeNotification() is null, "Repeated update alert");
            using var again = new AppPreferencesController(file, source, new StartupFake(), "1.0.5", false);
            Require(again.TakeNotification() is null && again.Update.Offer is not null, "Restart repeated notice or lost durable offer before another check");
            Wait(again.CheckAutomaticallyAsync()); Require(source.Calls == 1, "Restart bypassed the daily interval");
        });
        Test("Cached offers disappear after upgrading and cannot cross installation sources", () =>
        {
            var file = FileStore(); file.Save(new(LastChecked: DateTimeOffset.UtcNow, AvailableUpdate: GitHub("1.0.6", true), CheckedVersion: "1.0.5"));
            using var upgraded = new AppPreferencesController(file, new UpdateFake(), new StartupFake(), "1.0.6", false);
            using var store = new AppPreferencesController(file, new UpdateFake { Name = "Microsoft Store" }, new StartupFake(), "1.0.5", true);
            Require(upgraded.Update.Offer is null && store.Update.Offer is null, "Stale or cross-source cached offer restored");
        });
        Test("Update actions only open validated release or Store pages and never execute an installer", () =>
        {
            var opened = new List<string>(); var source = new UpdateFake { Offer = GitHub() };
            using var controller = new AppPreferencesController(FileStore(), source, new StartupFake(), "1.0.5", false, open: opened.Add);
            Wait(controller.CheckUpdatesAsync()); controller.OpenUpdate(); controller.OpenStartupSettings();
            Require(opened.SequenceEqual(new[] { GitHub().Url, "ms-settings:startupapps" }), "Unexpected shell action");
            source.Offer = GitHub() with { Url = "file:///C:/unsafe.exe" }; Wait(controller.CheckUpdatesAsync());
            bool rejected = false; try { controller.OpenUpdate(); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && opened.Count == 2, "Unsafe feed destination opened");
        });
        Test("Corrupt update preferences do not damage screenshot shortcut settings", () =>
        {
            var file = FileStore(); Directory.CreateDirectory(Path.GetDirectoryName(file.PathName)!); File.WriteAllText(file.PathName, "{broken");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(file.PathName)!, "settings.json"), "keep");
            using var controller = new AppPreferencesController(file, new UpdateFake(), new StartupFake(), "1.0.5", false);
            Require(!controller.Preferences.AutomaticChecks && controller.Update.Message.Contains("could not be read") && File.ReadAllText(Path.Combine(Path.GetDirectoryName(file.PathName)!, "settings.json")) == "keep", "Shortcut settings touched or checks re-enabled");
        });
        Test("Oversized and invalid cached preferences never block launch or re-enable network checks", () =>
        {
            foreach (string content in new[] { new string('x', 8193), JsonSerializer.Serialize(new UpdatePreferences(NotifiedVersion: new string('x', 129))), "{\"AvailableUpdate\":{\"Version\":null}}" })
            {
                var file = FileStore(); Directory.CreateDirectory(Path.GetDirectoryName(file.PathName)!); File.WriteAllText(file.PathName, content);
                var source = new UpdateFake(); using var controller = new AppPreferencesController(file, source, new StartupFake(), "1.0.5", false);
                Wait(controller.CheckAutomaticallyAsync());
                Require(!controller.Preferences.AutomaticChecks && source.Calls == 0 && File.ReadAllText(file.PathName) == content, "Bad preferences blocked recovery or were silently replaced");
            }
        });
        Test("Isolated app profiles never contact the production update service or change startup", () =>
        {
            using var controller = AppPreferencesController.Create(Path.Combine(output, "isolated-profile"));
            Wait(controller.CheckAutomaticallyAsync()); Require(controller.Update.CheckedAt is null, "Automatic isolated check ran");
            Wait(controller.CheckUpdatesAsync()); Wait(controller.RefreshStartupAsync());
            Require(controller.Update.Message.Contains("isolated test profile") && !controller.Startup.CanChange, "Isolated services were available or misleading");
        });
        Test("Startup commands quote EXE paths and reject shell injection and unsupported hosts", () =>
        {
            Require(UnpackagedStartupRegistration.CommandLine(@"C:\Apps with spaces\Snipsera.exe") == "\"C:\\Apps with spaces\\Snipsera.exe\" --startup", "Path quoting incorrect");
            foreach (string path in new[] { "Snipsera.exe", @"C:\Apps\dotnet.exe", "C:\\Apps\\evil\"\\Snipsera.exe", "C:\\Apps\\bad\n\\Snipsera.exe" })
            { bool rejected = false; try { UnpackagedStartupRegistration.CommandLine(path); } catch (InvalidOperationException) { rejected = true; } Require(rejected, "Unsafe command accepted"); }
        });
        Test("Unpackaged startup can be toggled on and off without touching Windows approval", () =>
        {
            var values = new Values(); var startup = new UnpackagedStartupRegistration(values, @"C:\Apps\Snipsera.exe");
            Wait(startup.SetAsync(true)); var state = startup.ReadAsync(); Wait(state); Require(state.Result.Enabled && values.Command!.EndsWith(" --startup"), "On failed");
            Wait(startup.SetAsync(false)); state = startup.ReadAsync(); Wait(state); Require(!state.Result.Enabled && values.Command is null && values.Approval is null, "Off changed Windows approval");
        });
        Test("Windows-disabled startup stays off and leads to Windows settings instead of overriding it", () =>
        {
            var values = new Values { Command = UnpackagedStartupRegistration.CommandLine(@"C:\Apps\Snipsera.exe"), Approval = new byte[] { 3, 0, 0, 0 } };
            var startup = new UnpackagedStartupRegistration(values, @"C:\Apps\Snipsera.exe"); var state = startup.SetAsync(true); Wait(state);
            Require(!state.Result.Enabled && !state.Result.CanChange && state.Result.ManageInWindows && values.Writes == 0 && values.Approval[0] == 3, "Windows disablement overridden");
        });
        Test("A reinstall can restore the Windows startup listing without bypassing prior disablement", () =>
        {
            var values = new Values { Approval = new byte[] { 3, 0, 0, 0 } };
            var startup = new UnpackagedStartupRegistration(values, @"C:\Apps\Snipsera.exe"); var before = startup.ReadAsync(); Wait(before);
            Require(!before.Result.Enabled && before.Result.CanChange, "Re-registration unavailable");
            var after = startup.SetAsync(true); Wait(after);
            Require(values.Command is not null && !after.Result.Enabled && after.Result.ManageInWindows && values.Approval[0] == 3, "Previous Windows disablement bypassed");
        });
        Test("Disabling one EXE copy does not erase another copy's startup entry", () =>
        {
            var values = new Values { Command = UnpackagedStartupRegistration.CommandLine(@"D:\Other\Snipsera.exe") };
            var startup = new UnpackagedStartupRegistration(values, @"C:\Apps\Snipsera.exe"); Wait(startup.SetAsync(false)); Require(values.Writes == 0 && values.Command!.Contains("Other"), "Another copy's registration deleted");
        });
        Test("Packaged startup state respects Windows user and policy controls", () =>
        {
            foreach (var state in new[] { StartupTaskState.DisabledByUser, StartupTaskState.DisabledByPolicy, StartupTaskState.EnabledByPolicy })
            { var status = PackagedStartupRegistration.Status(state); Require(!status.CanChange && status.ManageInWindows, "OS policy override offered"); }
            Require(!PackagedStartupRegistration.Status(StartupTaskState.Disabled).Enabled && PackagedStartupRegistration.Status(StartupTaskState.Enabled).Enabled, "Wrong native state mapping");
        });
        Test("A denied startup write reads back the real state rather than leaving the switch on", () =>
        {
            using var controller = new AppPreferencesController(FileStore(), new UpdateFake(), new StartupFake { FailWrite = true }, "1.0.5", false);
            Wait(controller.SetStartupAsync(true)); Require(!controller.Startup.Enabled && controller.Startup.ManageInWindows, "Rejected write displayed as enabled");
        });
        app.Shutdown();
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes, environment = "No real startup registry writes, Store calls or desktop input" }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
