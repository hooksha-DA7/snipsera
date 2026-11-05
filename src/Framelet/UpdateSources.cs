using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace Framelet;

internal sealed class GitHubUpdateSource(HttpClient client) : IUpdateSource
{
    internal const string ReleasesApi = "https://api.github.com/repos/hooksha-DA7/snipsera/releases?per_page=100";
    internal const string ReleasePrefix = "https://github.com/hooksha-DA7/snipsera/releases/tag/";
    public string Name => "GitHub releases";
    public async Task<UpdateOffer?> CheckAsync(string installedVersion, bool includePreviews, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Snipsera", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2_000_000) throw new InvalidDataException("Update response is too large.");
        using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var data = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        {
            if (data.Length + count > 2_000_000) throw new InvalidDataException("Update response is too large.");
            data.Write(buffer, 0, count);
        }
        return Select(data.ToArray(), installedVersion, includePreviews);
    }
    internal static UpdateOffer? Select(byte[] content, string installedVersion, bool includePreviews)
    {
        var installed = ReleaseVersion.Parse(installedVersion) ?? throw new InvalidDataException("Installed version could not be read.");
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid update response.");
        UpdateOffer? offer = null; ReleaseVersion best = installed;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("prerelease", out var preview) || preview.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !release.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String) continue;
            bool isPreview = preview.GetBoolean(); if (isPreview && !includePreviews) continue;
            string tag = tagValue.GetString()!;
            // A fixed repository URL is constructed from the validated tag. Never launch a URL supplied in the feed.
            if (!tag.StartsWith('v') || tag.Length > 80 || ReleaseVersion.Parse(tag) is not { } version || version.CompareTo(best) <= 0) continue;
            string build = tag[1..];
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array ||
                !assets.EnumerateArray().Any(asset => asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == $"Snipsera-{build}-setup-win-x64.exe")) continue;
            best = version;
            offer = new(build, "GitHub", ReleasePrefix + Uri.EscapeDataString(tag), isPreview, "github:" + tag);
        }
        return offer;
    }
}

internal sealed class StoreUpdateSource : IUpdateSource
{
    private readonly Func<CancellationToken, Task<(string Name, string Publisher)[]>> query;
    internal StoreUpdateSource(Func<CancellationToken, Task<(string Name, string Publisher)[]>>? query = null) => this.query = query ?? QueryAsync;
    internal IntPtr OwnerWindow { get; set; }
    internal const string ProductId = "9NFL9VHL8GKV";
    internal const string StoreUrl = "ms-windows-store://pdp/?ProductId=" + ProductId;
    public string Name => "Microsoft Store";
    public async Task<UpdateOffer?> CheckAsync(string installedVersion, bool includePreviews, CancellationToken cancellation)
    {
        var packages = await query(cancellation);
        bool available = packages.Any(package => package.Name == AppIdentity.StoreName && package.Publisher == AppIdentity.StorePublisher);
        return !available ? null : new("Store update", "Microsoft Store", StoreUrl, false, "store:" + installedVersion);
    }
    private async Task<(string Name, string Publisher)[]> QueryAsync(CancellationToken cancellation)
    {
        var context = StoreContext.GetDefault();
        if (OwnerWindow == IntPtr.Zero) throw new InvalidOperationException("Open Snipsera before checking Microsoft Store updates.");
        WinRT.Interop.InitializeWithWindow.Initialize(context, OwnerWindow);
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellation);
        // Optional framework packages are not a newer Snipsera application build.
        // Package identifies the installed app with an update, not a documented target-version field.
        return updates.Select(update => (update.Package.Id.Name, update.Package.Id.Publisher)).ToArray();
    }
}

internal sealed class UpdateCheckUnavailableException(string message) : Exception(message);

internal sealed class UnsupportedUpdateSource(string message = "Update checks are available in Microsoft Store or GitHub downloads.", string name = "Development package") : IUpdateSource
{
    public string Name => name;
    public Task<UpdateOffer?> CheckAsync(string installedVersion, bool includePreviews, CancellationToken cancellation) =>
        throw new UpdateCheckUnavailableException(message);
}
