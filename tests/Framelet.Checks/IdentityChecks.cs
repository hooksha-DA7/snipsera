using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class IdentityChecks
{
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Use a fresh identity-check folder.");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        int failed = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; results.Add(new { name, status = "failed", error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        void Require(bool value) { if (!value) throw new Exception("Compatibility check failed."); }
        string local = Path.Combine(output, "local");
        Directory.CreateDirectory(local);
        Check("Fresh installations select the Snipsera data folder without creating it", () =>
        { Require(AppIdentity.DataRoot(local) == Path.Combine(local, "Snipsera")); Require(!Directory.Exists(Path.Combine(local, "Snipsera"))); });
        string legacy = Path.Combine(local, "Framelet");
        Directory.CreateDirectory(legacy);
        var image = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 8 * 4], 8 * 4);
        image.Freeze();
        var history = new HistoryStore(legacy);
        var entry = history.Add(image, CaptureKind.Area);
        history.SaveSettings(new AppSettings(Area: "Ctrl+Alt+F1"));
        var recordings = new RecordingStore(legacy);
        recordings.SaveOptions(new RecordingOptions());
        var originals = Directory.GetFiles(legacy, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        Check("Existing Framelet history, shortcuts and recording settings are reused byte for byte", () =>
        {
            string selected = AppIdentity.DataRoot(local);
            Require(selected == legacy && !Directory.Exists(Path.Combine(local, "Snipsera")));
            Require(new HistoryStore(selected).Entries.Single().Id == entry.Id);
            Require(new HistoryStore(selected).LoadSettings().Area == "Ctrl+Alt+F1");
            _ = new RecordingStore(selected).LoadOptions();
            foreach (var file in originals) Require(file.Value.SequenceEqual(File.ReadAllBytes(file.Key)));
        });
        Check("An existing Snipsera folder takes precedence without merging two histories", () =>
        { Directory.CreateDirectory(Path.Combine(local, "Snipsera")); Require(AppIdentity.DataRoot(local) == Path.Combine(local, "Snipsera")); });
        Check("Both editable-project extensions are recognized case-insensitively", () =>
        { Require(AppIdentity.IsProject("old.FRAMELET") && AppIdentity.IsProject("new.SNIPSERA") && !AppIdentity.IsProject("image.png")); });
        Check("Old and new project filenames preserve identical document data", () =>
        {
            foreach (string extension in new[] { ".framelet", ".snipsera" })
            {
                string path = Path.Combine(output, "project" + extension);
                EditFiles.SaveProject(new EditDocument(image), path);
                var document = EditFiles.LoadProject(path);
                Require(document.Original.PixelWidth == 8 && document.Original.PixelHeight == 8 && !document.IsDirty);
            }
        });
        Check("Single-instance lock remains compatible with the original Framelet algorithm", () =>
        {
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacy.ToUpperInvariant())))[..20];
            Require(AppIdentity.InstanceName(legacy) == "Local\\Framelet-" + hash);
            Require(AppIdentity.InstanceName(legacy) == AppIdentity.InstanceName(legacy.ToLowerInvariant()));
            Require(AppIdentity.InstanceName(legacy) != AppIdentity.InstanceName(Path.Combine(local, "Snipsera")));
        });
        Check("Runtime assembly uses the Snipsera executable identity", () => Require(typeof(CaptureApp).Assembly.GetName().Name == "Snipsera"));
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = failed == 0 ? "passed" : "failed", results }, new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }
}
