using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Framelet;

public sealed record UpdatePreferences(bool AutomaticChecks = true, bool IncludePreviews = true,
    DateTimeOffset? LastChecked = null, string? NotifiedVersion = null, DateTimeOffset? LastAttempt = null,
    UpdateOffer? AvailableUpdate = null, string? CheckedVersion = null);
public sealed record UpdateOffer(string Version, string Source, string Url, bool IsPreview, string Key);
public sealed record UpdateState(bool Checking = false, UpdateOffer? Offer = null,
    string Message = "Not checked yet.", DateTimeOffset? CheckedAt = null);
public sealed record StartupStatus(bool Enabled, bool CanChange, string Message, bool ManageInWindows = false);

internal interface IUpdateSource
{
    string Name { get; }
    Task<UpdateOffer?> CheckAsync(string installedVersion, bool includePreviews, CancellationToken cancellation);
}
internal interface IStartupRegistration
{
    Task<StartupStatus> ReadAsync();
    Task<StartupStatus> SetAsync(bool enabled);
}

internal sealed class AppPreferencesFile(string root)
{
    internal string PathName => Path.Combine(root, "app-preferences.json");
    internal UpdatePreferences Load()
    {
        if (!File.Exists(PathName)) return new();
        if (new FileInfo(PathName).Length > 8192) throw new InvalidDataException("App preferences are too large.");
        var preferences = JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(PathName)) ?? new();
        if (preferences.NotifiedVersion?.Length > 128) throw new InvalidDataException("Invalid update notification preference.");
        if (preferences.AvailableUpdate is { } offer &&
            (string.IsNullOrEmpty(offer.Version) || offer.Version.Length > 80 || string.IsNullOrEmpty(offer.Url) || offer.Url.Length > 512 ||
             string.IsNullOrEmpty(offer.Key) || offer.Key.Length > 128 || string.IsNullOrEmpty(offer.Source) || offer.Source.Length > 80))
            throw new InvalidDataException("Invalid cached update.");
        return preferences;
    }
    internal void Save(UpdatePreferences preferences)
    {
        Directory.CreateDirectory(root);
        string temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, PathName, true);
    }
}
