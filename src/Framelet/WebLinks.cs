using System;
using System.Diagnostics;

namespace Framelet;

// Public pages opened from About, the tray menu and update notices. Only these fixed addresses are ever opened.
public static class WebLinks
{
    public const string Website = "https://snipsera.com/";
    public const string ReleaseNotes = "https://snipsera.com/releases/";
    public const string Support = "https://snipsera.com/support/";
    public const string Privacy = "https://snipsera.com/privacy/";
    public const string Source = "https://github.com/hooksha-DA7/snipsera";
    public const string License = "https://github.com/hooksha-DA7/snipsera/blob/main/LICENSE";

    private static readonly string[] Allowed = [Website, ReleaseNotes, Support, Privacy, Source, License];

    public static bool IsKnown(string url) => Array.IndexOf(Allowed, url) >= 0;

    // Opens the page in the default browser. Throws with a plain message when Windows cannot open it.
    public static void Open(string url)
    {
        if (!IsKnown(url)) throw new ArgumentException("Unknown link.", nameof(url));
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { throw new InvalidOperationException("Your browser couldn't be opened. Visit " + url + " instead.", ex); }
    }
}
