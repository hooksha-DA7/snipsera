using System;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;
using Rect = System.Drawing.Rectangle;

internal static class ScrollingChecks
{
    internal static BitmapSource Page(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            uint hash = unchecked((uint)y * 2246822519u ^ (uint)(x / 5) * 2654435761u); hash ^= hash >> 13;
            int i = (y * width + x) * 4; bytes[i] = (byte)hash; bytes[i + 1] = (byte)(hash >> 8); bytes[i + 2] = (byte)(hash >> 16); bytes[i + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4); image.Freeze(); return image;
    }
    internal static BitmapSource View(BitmapSource page, int top, int height) => CaptureService.Crop(page, new Rect(0, top, page.PixelWidth, height), new Rect(0, 0, page.PixelWidth, page.PixelHeight));
    private static byte[] Pixels(BitmapSource image) { var data = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(data, image.PixelWidth * 4, 0); return data; }
    private static void Require(bool value, string message = "Scrolling capture assertion failed") { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test, Action<int> pump)
    {
        test("Scrolling capture control and preview bounds avoid the selected pixels across scales and negative origins", () =>
        {
            foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                var area = new Rect(-1500, 160, 1050, 600); Rect[] monitors = [new(-1920, 0, 1920, 1080), new(0, 0, 2560, 1440)];
                var placed = ScrollCaptureLayout.PlaceBar(area, monitors, ScrollCaptureLayout.NominalWide, ScrollCaptureLayout.NominalCompact, scale);
                Require(placed is not null, "No controls fit at scale " + scale);
                var bar = placed!.Value; Require(!bar.Outer.IntersectsWith(area) && monitors.Any(m => m.Contains(bar.Outer)));
                foreach (var ring in ScrollCaptureLayout.Ring(area, scale)) Require(!ring.IntersectsWith(area));
                var slot = ScrollCaptureLayout.PreviewSlot(area, monitors, bar.Outer, scale);
                if (slot is not null)
                {
                    var body = ScrollCaptureLayout.FitPreview(slot.Value, new System.Drawing.Size(180, 320), scale);
                    Require(!slot.Value.Outer.IntersectsWith(area) && !slot.Value.Outer.IntersectsWith(bar.Outer) && slot.Value.Outer.Contains(body));
                }
            }
            var full = new Rect(0, 0, 1920, 1080);
            Require(ScrollCaptureLayout.PlaceBar(full, new[] { full }, ScrollCaptureLayout.NominalWide, ScrollCaptureLayout.NominalCompact, 1) is null, "Fullscreen area must not receive controls inside its pixels");
        });
        test("Scrolling capture joins irregular downward offsets without lost or duplicated pixels", () =>
        {
            var page = Page(160, 1000); var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 240)));
            foreach (int top in new[] { 33, 112, 187, 300, 450 }) Require(stitcher.Add(new ScrollFrame(View(page, top, 240))) == ScrollAppendStatus.Added, "Failed at offset " + top);
            Require(Pixels(stitcher.Image()).SequenceEqual(Pixels(View(page, 0, 690))), "Joined output differs from exact original page pixels");
            var thumb = stitcher.Thumbnail(); Require(thumb.IsFrozen && thumb.PixelWidth <= 180 && thumb.PixelHeight <= 320);
        });
        test("Scrolling capture ignores duplicate views and backward motion then resumes", () =>
        {
            var page = Page(160, 800); var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 240)));
            Require(stitcher.Add(new ScrollFrame(View(page, 90, 240))) == ScrollAppendStatus.Added);
            Require(stitcher.Add(new ScrollFrame(View(page, 40, 240))) == ScrollAppendStatus.Backwards);
            Require(stitcher.Add(new ScrollFrame(View(page, 90, 240))) == ScrollAppendStatus.Unchanged);
            Require(stitcher.Add(new ScrollFrame(View(page, 140, 240))) == ScrollAppendStatus.Added);
            Require(Pixels(stitcher.Image()).SequenceEqual(Pixels(View(page, 0, 380))));
        });
        test("Scrolling capture refuses gaps and ambiguous blank content without altering good pixels", () =>
        {
            var page = Page(160, 1200); var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 240))); var before = Pixels(stitcher.Image());
            Require(stitcher.Add(new ScrollFrame(View(page, 700, 240))) == ScrollAppendStatus.NeedsOverlap);
            Require(before.SequenceEqual(Pixels(stitcher.Image())));
            var blank = BitmapSource.Create(160, 240, 96, 96, PixelFormats.Bgra32, null, new byte[160 * 240 * 4], 640); blank.Freeze();
            var flat = new ScrollStitcher(new ScrollFrame(blank)); Require(flat.Add(new ScrollFrame(blank)) == ScrollAppendStatus.Unchanged && flat.Height == 240);
        });
        test("Scrolling capture bounds output and rejects changed viewport dimensions", () =>
        {
            var page = Page(80, ScrollStitcher.MaxSide + 1);
            var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, ScrollStitcher.MaxSide)));
            Require(stitcher.Add(new ScrollFrame(View(page, 1, ScrollStitcher.MaxSide))) == ScrollAppendStatus.LimitReached);
            bool refused = false;
            try { stitcher.Add(new ScrollFrame(Page(81, 100))); } catch (InvalidOperationException) { refused = true; }
            Require(refused && stitcher.Height == ScrollStitcher.MaxSide);
        });
        test("Scrolling session can finish a single view or cancel without committing an image", () =>
        {
            using (var cancelled = new ScrollCaptureSession(new Rect(-120, 50, 160, 240), _ => Page(160, 240), false))
            { cancelled.Cancel(); Require(cancelled.Completion.IsCompletedSuccessfully && cancelled.Completion.Result is null && cancelled.Phase == ScrollCapturePhase.Cancelled); }
            var image = Page(160, 240);
            using var session = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => image, false);
            session.Start(); session.Finish();
            for (int i = 0; i < 30 && !session.Completion.IsCompleted; i++) pump(100);
            Require(session.Completion.IsCompletedSuccessfully && session.Phase == ScrollCapturePhase.Completed);
            Require(Pixels(session.Completion.Result!).SequenceEqual(Pixels(image)));
        });
        test("Scrolling session collects stable views and finishes with the exact combined source", () =>
        {
            var page = Page(160, 800); int read = 0;
            using var session = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => View(page, read++ == 0 ? 0 : read <= 3 ? 60 : 120, 240), false);
            session.Start(); for (int i = 0; i < 40 && session.Frames < 3; i++) pump(100);
            Require(session.Frames == 3, "Stable views were not collected"); session.Finish();
            for (int i = 0; i < 30 && !session.Completion.IsCompleted; i++) pump(100);
            Require(session.Completion.IsCompletedSuccessfully && Pixels(session.Completion.Result!).SequenceEqual(Pixels(View(page, 0, 360))));
        });
        test("Scrolling session cancellation and read failure release the workflow without a result", () =>
        {
            using (var failed = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => throw new System.IO.IOException("Test display read failed"), false))
            {
                failed.Start(); for (int i = 0; i < 20 && !failed.Completion.IsCompleted; i++) pump(50);
                Require(failed.Completion.IsFaulted && failed.Completion.Exception!.GetBaseException() is System.IO.IOException && failed.Phase == ScrollCapturePhase.Failed);
            }
            using var cancelled = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => Page(160, 240), false);
            cancelled.SetAutoScroll(true); Require(!cancelled.AutoScroll && !cancelled.CanAutoScroll);
            cancelled.Start(); pump(50); cancelled.Cancel(); pump(400);
            Require(cancelled.Completion.IsCompletedSuccessfully && cancelled.Completion.Result is null && cancelled.Phase == ScrollCapturePhase.Cancelled);
        });
    }
}
