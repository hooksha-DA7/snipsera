using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Framelet;

public enum ToolbarMaterial { Tinted, Frosted }
public sealed record ToolbarAppearanceOptions(ToolbarMaterial Material = ToolbarMaterial.Tinted, int Opacity = 80);

public sealed class ToolbarAppearanceSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter<ToolbarMaterial>() } };
    private readonly string path;
    private bool? lastNativeSuccess;
    public ToolbarAppearanceOptions Options { get; private set; } = new();
    public string? Notice { get; private set; }
    public event Action? Changed;
    internal static bool NativeFrostingSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
    public string CapabilityMessage => !Appearance.Current.Policy.Frosting
        ? "Windows transparency is off or a contrast theme is active. The bars use a solid surface."
        : Options.Opacity == 100
            ? "At 100% opacity the background is solid. Lower the opacity to see the selected material."
        : !NativeFrostingSupported
            ? "Tinted surfaces are available. Frosted glass needs Windows 11 version 22H2 or later; a solid surface is used on this version."
            : Options.Material == ToolbarMaterial.Frosted && lastNativeSuccess == false
                ? "Windows could not initialize the frosted surface. The affected bar uses a solid surface. Reopen it to try again. Your saved choice is unchanged."
                : "Frosted glass requests Windows Acrylic. Windows may use a solid surface for power, graphics or accessibility settings. Background opacity controls the tint; icons and text stay sharp.";

    internal void ReportNativeResult(bool success)
    {
        if (lastNativeSuccess == success) return;
        lastNativeSuccess = success; Changed?.Invoke();
    }

    public ToolbarAppearanceSettings(string dataRoot)
    {
        path = Path.Combine(Path.GetFullPath(dataRoot), "toolbar-appearance.json");
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 4096) throw new InvalidDataException();
            var loaded = JsonSerializer.Deserialize<ToolbarAppearanceOptions>(File.ReadAllText(path), Json) ?? throw new InvalidDataException();
            Validate(loaded); Options = loaded;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Notice = "Appearance settings could not be read. Tinted surface at 80% is active; use an opacity preset below to save new settings."; }
    }

    public void Update(ToolbarMaterial material, int opacity)
    {
        var next = new ToolbarAppearanceOptions(material, opacity); Validate(next);
        if (next == Options && Notice is null) return;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, Json));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new IOException("Appearance could not be saved. Check that Snipsera's settings folder is writable, then try again.", ex); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        Options = next; Notice = null; Changed?.Invoke();
    }

    private static void Validate(ToolbarAppearanceOptions value)
    {
        if (!Enum.IsDefined(value.Material)) throw new ArgumentOutOfRangeException(nameof(value), "Choose Tinted surface or Frosted glass.");
        if (value.Opacity is < 70 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Background opacity must be between 70% and 100%.");
    }
}

internal readonly record struct ToolbarSurfacePlan(bool Frosted, byte Alpha)
{
    internal static ToolbarSurfacePlan Resolve(ToolbarAppearanceOptions options, bool transparency, bool supported, bool nativeFailed = false)
    {
        bool frost = options.Material == ToolbarMaterial.Frosted;
        if (!transparency || frost && (!supported || nativeFailed)) return new(false, 255);
        return new(frost && options.Opacity < 100, (byte)Math.Round(options.Opacity * 255d / 100, MidpointRounding.AwayFromZero));
    }
}
