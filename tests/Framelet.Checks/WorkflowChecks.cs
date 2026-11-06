using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Framelet;

// Offscreen only: no windows shown, capture, physical input, clipboard writes, installation or real startup/update providers.
internal static class WorkflowChecks
{
    private sealed class PendingEngine : IRecordingEngine
    {
        internal readonly TaskCompletionSource Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Stops, Disposals;
        public Task Completion => Stopped.Task;
        public Task StartAsync(RecordingTarget target, RecordingOptions options, string path) => Task.CompletedTask;
        public Task StopAsync() { Stops++; return Stopped.Task; }
        public void Pause() { }
        public void Resume() { }
        public void Dispose() => Disposals++;
    }
    private sealed class Restoration : IDisposable { internal int Calls; public void Dispose() => Calls++; }
    internal static IEnumerable<DependencyObject> Children(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        int failures = 0; var outcomes = new List<object>();
        void Check(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
        string Folder() { string folder = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
        void Refuses(Action action) { try { action(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid operation was accepted"); }
        var app = new CaptureApp(Path.Combine(output, "profile"));
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
        Check("Image/video save folders and screenshot auto-save survive restart independently", () =>
        {
            string root = Folder(), pictures = Folder(), images = Folder(), videos = Folder(); var storage = new StorageLocations(root, pictures);
            Require(storage.ImageFolder == Path.Combine(pictures, AppIdentity.Name) && storage.VideoFolder == Path.Combine(root, "recordings") && storage.AutomaticImageSaving);
            storage.SetImageFolder(images); storage.SetVideoFolder(videos); storage.SetAutomaticImageSaving(true);
            var again = new StorageLocations(root, pictures); Require(again.ImageFolder == images && again.VideoFolder == videos && again.AutomaticImageSaving);
            again.SetImageFolder(null); Require(again.ImageFolder == Path.Combine(pictures, AppIdentity.Name) && again.VideoFolder == videos);
        });
        Check("Folder changes do not move old recordings or images", () =>
        {
            string root = Folder(), first = Folder(), second = Folder(); var storage = new StorageLocations(root, first);
            File.WriteAllText(Path.Combine(first, "keep.mp4"), "original"); File.WriteAllText(Path.Combine(first, "keep.png"), "original");
            storage.SetImageFolder(second); storage.SetVideoFolder(second);
            Require(Directory.GetFiles(first).Length == 2 && Directory.GetFiles(second).Length == 0);
        });
        Check("Relative/file destinations and failed config writes preserve previous preferences", () =>
        {
            string root = Folder(), pictures = Folder(); var storage = new StorageLocations(root, pictures);
            Refuses(() => storage.SetImageFolder("relative")); string file = Path.Combine(root, "file"); File.WriteAllText(file, "keep");
            Refuses(() => storage.SetVideoFolder(file)); Require(storage.ImageFolder == Path.Combine(pictures, AppIdentity.Name) && storage.VideoFolder == Path.Combine(root, "recordings"));
            storage.SetVideoFolder(Folder()); string previous = storage.VideoFolder;
            using (File.Open(Path.Combine(root, "storage.json"), FileMode.Open, FileAccess.Read, FileShare.None)) Refuses(() => storage.SetVideoFolder(Folder()));
            Require(storage.VideoFolder == previous && !Directory.GetFiles(root, "*.tmp").Any());
        });
        Check("Malformed save-location preferences are preserved and default auto-save is off", () =>
        {
            string root = Folder(), file = Path.Combine(root, "storage.json"); File.WriteAllText(file, "{broken");
            var storage = new StorageLocations(root, Folder()); Require(storage.Notice is not null && !storage.AutomaticImageSaving && File.ReadAllText(file) == "{broken");
        });
        Check("Automatic screenshots keep edits at one stable export path; disabling stops writes", () =>
        {
            string root = Folder(), images = Folder(); var storage = new StorageLocations(root, images);
            storage.SetImageFolder(images); storage.SetAutomaticImageSaving(false);
            var entry = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "Area", 32, 24);
            storage.SaveCapture(UiChecks.White(), entry); Require(!File.Exists(storage.ImagePath(entry)));
            storage.SetAutomaticImageSaving(true); storage.SaveCapture(UiChecks.White(), entry); byte[] original = File.ReadAllBytes(storage.ImagePath(entry));
            storage.SaveCapture(RecordingPreview.Placeholder(), entry); Require(!original.SequenceEqual(File.ReadAllBytes(storage.ImagePath(entry))) && Directory.GetFiles(images, "*.png").Length == 1);
            storage.SetAutomaticImageSaving(false); byte[] last = File.ReadAllBytes(storage.ImagePath(entry)); storage.SaveCapture(UiChecks.White(), entry);
            Require(last.SequenceEqual(File.ReadAllBytes(storage.ImagePath(entry))));
        });
        Check("Video and GIF choose the selected output folder; managed work files remain separate", () =>
        {
            string root = Folder(), videos = Folder(); var storage = new StorageLocations(root, Folder()); storage.SetVideoFolder(videos);
            Require(storage.NewRecordingPath("test", RecordingFormat.Mp4) == Path.Combine(videos, "Snipsera-test.mp4"));
            Require(storage.NewRecordingPath("test", RecordingFormat.Gif) == Path.Combine(videos, "Snipsera-test.gif"));
            Require(!Directory.GetFiles(videos).Any());
        });
        Check("Recording history retains previous selected folders and refuses unrelated paths", () =>
        {
            string root = Folder(), first = Folder(), second = Folder(); var store = new RecordingStore(root);
            RecordingResult Result(string folder, string name) => new(Path.Combine(folder, name), RecordingFormat.Mp4, 320, 180, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow);
            var video = Result(first, "Snipsera-first.mp4"); File.WriteAllText(video.FilePath, "video"); store.Add(video, first);
            var other = Result(second, "Snipsera-second.mp4"); File.WriteAllText(other.FilePath, "video"); store.Add(other, second);
            var again = new RecordingStore(root); Require(again.Entries.Count == 2 && again.IsRecording(video) && again.IsRecording(other));
            Refuses(() => again.Add(Result(Folder(), "unrelated.mp4"))); Require(!again.Owns(video.FilePath));
        });
        Check("Recording publication never overwrites an existing output or loses the recovery file", () =>
        {
            string folder = Folder(), source = Path.Combine(folder, "work.mp4"), target = Path.Combine(folder, "video.mp4");
            File.WriteAllText(source, "new"); File.WriteAllText(target, "original"); Refuses(() => RecordingStore.Publish(source, target));
            Require(File.ReadAllText(source) == "new" && File.ReadAllText(target) == "original");
            File.Delete(target); RecordingStore.Publish(source, target); Require(File.ReadAllText(target) == "new" && !File.Exists(source));
            File.WriteAllText(source, "recovery"); Refuses(() => RecordingStore.Publish(source, Path.Combine(folder, "missing", "video.mp4")));
            Require(File.ReadAllText(source) == "recovery" && !Directory.GetFiles(folder, "*.tmp").Any());
        });
        Check("Recording Copy and Drag offer the actual media file, not a thumbnail PNG", () =>
        {
            string file = Path.Combine(Folder(), "video.mp4"); File.WriteAllText(file, "synthetic"); var data = DragExport.CreateFileData(file);
            Require(data.GetFileDropList().Cast<string>().Single() == file && !data.GetDataPresent(DataFormats.Bitmap));
            Refuses(() => DragExport.CreateFileData(Path.Combine(Folder(), "missing.mp4")));
        });
        Check("Real synthetic MP4 and GIF produce frozen thumbnails without changing source bytes", () =>
        {
            foreach (string extension in new[] { ".mp4", ".gif" })
            {
                string folder = Folder(), path = Path.Combine(folder, "synthetic clip" + extension);
                var start = new ProcessStartInfo(GifConverter.EncoderPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "color=c=0x2869a2:s=320x180:r=10", "-frames:v", "1", "-an", path }) start.ArgumentList.Add(argument);
                using var encoder = Process.Start(start)!;
                var error = encoder.StandardError.ReadToEndAsync(); Require(encoder.WaitForExit(10000), "Synthetic encoder timed out");
                Require(encoder.ExitCode == 0, error.GetAwaiter().GetResult()); byte[] original = File.ReadAllBytes(path);
                var task = RecordingPreview.ThumbnailAsync(path, folder); PreferencesChecks.Wait(task); var thumbnail = task.GetAwaiter().GetResult();
                Require(thumbnail.IsFrozen && thumbnail.PixelWidth == 480 && thumbnail.PixelHeight == 270 && original.SequenceEqual(File.ReadAllBytes(path)));
                Require(!Directory.GetFiles(Path.Combine(folder, "recording-thumbnails")).Any(), "Temporary thumbnails were not cleaned");
            }
        });
        Check("Stop restoration excludes recording setup while keeping other app windows eligible", () =>
        {
            var setup = new RecordingWindow(app); var ordinary = new Window();
            Require(!CaptureApp.RestoreAfterRecording(setup) && CaptureApp.RestoreAfterRecording(ordinary)); setup.Close(); ordinary.Close();
        });
        Check("Repeated Stop saves once, restores presentation once and emits one completed result", () =>
        {
            string root = Folder(), temp = Path.Combine(root, "working.mp4"), destination = Path.Combine(Folder(), "Snipsera-result.mp4");
            File.WriteAllText(temp, "synthetic recording"); var engine = new PendingEngine(); var restoration = new Restoration();
            using var controller = new RecordingController(app, root);
            controller.UseTarget(new(RecordingTargetKind.Area, new System.Drawing.Rectangle(0, 0, 320, 180), IntPtr.Zero, "Synthetic target"));
            void Field(string name, object value) => typeof(RecordingController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, value);
            Field("engine", engine); Field("temporary", temp); Field("destination", destination); Field("presentation", restoration);
            typeof(RecordingController).GetProperty(nameof(RecordingController.State))!.SetValue(controller, RecordingState.Recording);
            int completed = 0; controller.Completed += _ => completed++;
            var stop = controller.StopAsync(); Require(ReferenceEquals(stop, controller.StopAsync()) && engine.Stops == 1 && completed == 0);
            engine.Stopped.SetResult(); PreferencesChecks.Wait(stop); PreferencesChecks.Wait(app.Dispatcher.InvokeAsync(() => { }).Task);
            Require(controller.State == RecordingState.Completed && controller.LastResult?.FilePath == destination && File.Exists(destination) && !File.Exists(temp));
            Require(completed == 1 && restoration.Calls == 1 && engine.Disposals == 1 && controller.RecentRecordings.Count == 1);
            PreferencesChecks.Wait(controller.StopAsync()); PreferencesChecks.Wait(app.Dispatcher.InvokeAsync(() => { }).Task); Require(completed == 1);
        });
        Check("Recording side card keeps on-image controls and plays instead of annotating", () =>
        {
            var result = new RecordingResult(Path.Combine(Folder(), "saved.mp4"), RecordingFormat.Mp4, 320, 180, TimeSpan.FromSeconds(6), DateTimeOffset.UtcNow);
            var card = new ImageWindow(app, RecordingPreview.Placeholder(), null, true, recording: result);
            var root = (FrameworkElement)card.Content;
            root.Measure(new Size(288, 180)); root.Arrange(new Rect(0, 0, 288, 180)); root.UpdateLayout();
            var buttons = Children(root).OfType<Button>().ToArray();
            Require(card.IsPinned && card.Recording == result && buttons.Any(b => AutomationProperties.GetName(b) == "Play recording"));
            Require(buttons.Any(b => AutomationProperties.GetAutomationId(b) == "CopyCapture") && buttons.Any(b => AutomationProperties.GetAutomationId(b) == "PinCapture"));
            card.UpdateRecordingThumbnail(UiChecks.White()); Require(card.CaptureImage.PixelWidth == UiChecks.White().PixelWidth); card.Close();
        });
        Check("Settings has separate pages and Home no longer owns shortcut recorders", () =>
        {
            var settings = new PreferencesWindow(app.Preferences, app); var root = (FrameworkElement)settings.Content;
            root.Measure(new Size(780, 620)); root.Arrange(new Rect(0, 0, 780, 620)); root.UpdateLayout();
            foreach (var page in Enum.GetValues<SettingsPage>()) { settings.ShowPage(page); Require(settings.Current == page); }
            Require(Children(root).OfType<ShortcutRecorder>().Count() == 5);
            var home = new HomeWindow(app); var homeRoot = (FrameworkElement)home.Content;
            homeRoot.Measure(new Size(1100, 720)); homeRoot.Arrange(new Rect(0, 0, 1100, 720)); homeRoot.UpdateLayout();
            Require(!Children(homeRoot).OfType<ShortcutRecorder>().Any()); home.Close(); settings.Close();
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown();
        return failures == 0 ? 0 : 1;
    }
}
