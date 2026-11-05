using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Input;
using System.Text.Json.Serialization;

namespace Framelet;

public enum CaptureKind { Area, Window, Screen, PreviousArea, Scrolling }
public sealed record CaptureEntry(string Id, DateTimeOffset Created, string Mode, int Width, int Height)
{
    [JsonIgnore] public string Label => $"{(Mode == "Screen" ? "Display" : Mode == "PreviousArea" ? "Repeat area" : Mode)} · {Width:N0} × {Height:N0}";
    [JsonIgnore]
    public string TimeLabel => Created.LocalDateTime.ToString("MMM d · h:mm tt", CultureInfo.CurrentCulture);
}

public sealed record AppSettings(string Area = "Ctrl+Alt+Shift+1", string Window = "Ctrl+Alt+Shift+2",
                                 string Screen = "Ctrl+Alt+Shift+3", string Previous = "Ctrl+Alt+Shift+4",
                                 string AllInOne = "Ctrl+Alt+Shift+5")
{
    public string[] Bindings => [Area, Window, Screen, Previous, AllInOne];
}

public readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public static Hotkey Parse(string input)
    {
        uint modifiers = 0;
        Key key = Key.None;
        foreach (string raw in input.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                default:
                    if (key != Key.None) throw new FormatException("Choose one key with Ctrl and/or Alt, optionally Shift.");
                    string name = raw.ToUpperInvariant() is "PRINTSCREEN" or "PRINTSCRN" or "PRTSC" or "PRTSCN" or "SNAPSHOT" ? "PrintScreen"
                        : raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse(name, true, out key) || !Enum.IsDefined(key)) throw new FormatException("That shortcut key is not supported.");
                    break;
            }
        }
        bool allowedKey = key >= Key.A && key <= Key.Z || key >= Key.D0 && key <= Key.D9 || key >= Key.F1 && key <= Key.F11;
        if (key != Key.PrintScreen && ((modifiers & 3) == 0 || !allowedKey)) throw new FormatException("Use Print Screen, or Ctrl and/or Alt with a letter, number or F1–F11. Shift is optional.");
        return new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    public static string? FromKey(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0) return null;
        string name = key == Key.PrintScreen ? "PrintScreen"
            : key >= Key.D0 && key <= Key.D9 ? ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture) : key.ToString();
        string binding = (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "")
            + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "")
            + (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + name;
        try { _ = Parse(binding); return binding; }
        catch (FormatException) { return null; }
    }
}

public static class CaptureGeometry
{
    public static Rectangle FromDrag(double x1, double y1, double x2, double y2, double viewWidth, double viewHeight, Rectangle screen)
    {
        if (viewWidth <= 0 || viewHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewWidth));
        int left = (int)Math.Floor(Math.Clamp(Math.Min(x1, x2) / viewWidth, 0, 1) * screen.Width);
        int top = (int)Math.Floor(Math.Clamp(Math.Min(y1, y2) / viewHeight, 0, 1) * screen.Height);
        int right = (int)Math.Ceiling(Math.Clamp(Math.Max(x1, x2) / viewWidth, 0, 1) * screen.Width);
        int bottom = (int)Math.Ceiling(Math.Clamp(Math.Max(y1, y2) / viewHeight, 0, 1) * screen.Height);
        return Rectangle.Intersect(screen, Rectangle.FromLTRB(screen.Left + left, screen.Top + top, screen.Left + right, screen.Top + bottom));
    }

    public static Rectangle Clip(Rectangle requested, Rectangle screen)
    {
        var clipped = Rectangle.Intersect(requested, screen);
        if (clipped.Width < 2 || clipped.Height < 2) throw new InvalidOperationException("Select an area at least 2 × 2 pixels inside a display.");
        if ((long)clipped.Width * clipped.Height > 100_000_000) throw new InvalidOperationException("This capture is too large. Choose a smaller area.");
        return clipped;
    }
}
