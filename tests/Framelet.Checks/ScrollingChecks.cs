using System;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
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
    internal static BitmapSource Article(int width = 640, int height = 2400)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, width, height));
            for (int y = 30, section = 0; y < height; y += 83, section++)
            {
                var text = new FormattedText($"Section {section + 1}: {section * 7919} - capture this paragraph.", CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.Black, 1);
                drawing.DrawText(text, new System.Windows.Point(45, y));
            }
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
    internal static BitmapSource WithWidget(BitmapSource image, int variation)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(image, new System.Windows.Rect(0, 0, image.PixelWidth, image.PixelHeight));
            drawing.DrawRectangle(variation % 2 == 0 ? Brushes.RoyalBlue : Brushes.Orange, null, new System.Windows.Rect(image.PixelWidth - 160, 50, 120, 90));
        }
        var result = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
    private static BitmapSource FractionalView(BitmapSource page, double top, int height)
    {
        var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Linear);
        using (var drawing = visual.RenderOpen()) drawing.DrawImage(page, new System.Windows.Rect(0, -top, page.PixelWidth, page.PixelHeight));
        var result = new RenderTargetBitmap(page.PixelWidth, height, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
    private static BitmapSource SmoothPage()
    {
        const int width = 320, height = 1800; var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4;
            pixels[p] = (byte)(120 + 65 * Math.Sin(y / 37d) + 30 * Math.Cos(x / 70d));
            pixels[p + 1] = (byte)(120 + 80 * Math.Cos(y / 45d + x / 160d));
            pixels[p + 2] = (byte)(120 + 80 * Math.Sin((x + y) / 40d)); pixels[p + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static BitmapSource Noisy(BitmapSource image, int seed)
    {
        var pixels = Pixels(image);
        for (int p = 0; p < pixels.Length; p += 4) for (int c = 0; c < 3; c++)
        { int noise = (int)(unchecked((uint)(p + c) * 2654435761u + (uint)seed * 2246822519u) % 5) - 2; pixels[p + c] = (byte)Math.Clamp(pixels[p + c] + noise, 0, 255); }
        var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, image.PixelWidth * 4); result.Freeze(); return result;
    }
    private static BitmapSource BlankTail(BitmapSource image)
    {
        var data = Pixels(image); int width = image.PixelWidth, height = image.PixelHeight;
        for (int y = height * 3 / 4; y < height; y++) for (int x = 0; x < width; x++)
        { int p = (y * width + x) * 4; data[p] = data[p + 1] = data[p + 2] = 255; }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, width * 4); result.Freeze(); return result;
    }
    public static void Run(Action<string, Action> test, Action<int> pump)
    {
        test("Scrolling sparse article text matches exact offsets despite a small animated widget", () =>
        {
            var page = Article(); var stitcher = new ScrollStitcher(new ScrollFrame(WithWidget(View(page, 0, 480), 0)));
            foreach (int top in new[] { 47, 135, 251, 410, 700 })
                Require(stitcher.Add(new ScrollFrame(WithWidget(View(page, top, 480), top))) == ScrollAppendStatus.Added, "Article failed at " + top);
            Require(stitcher.Height == 1180, "Article offset was misidentified");
        });
        test("Scrolling sparse article accepts a uniquely identifiable short overlap", () =>
        {
            var page = Article(); var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 480)));
            Require(stitcher.Add(new ScrollFrame(View(page, 395, 480))) == ScrollAppendStatus.Added);
            Require(Pixels(stitcher.Image()).SequenceEqual(Pixels(View(page, 0, 875))));
        });
        test("Scrolling similarly spaced paragraphs retain their unique section offsets", () =>
        {
            var page = Article(); var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 480)));
            foreach (int top in new[] { 83, 166, 332, 581 })
                Require(stitcher.Add(new ScrollFrame(View(page, top, 480))) == ScrollAppendStatus.Added && stitcher.Height == 480 + top, "Repeated paragraph offset " + top + " was lost");
            Require(Pixels(stitcher.Image()).SequenceEqual(Pixels(View(page, 0, 1061))));
        });
        test("Scrolling article tolerates fractional text rasterization during smooth scrolling", () =>
        {
            var page = Article(); var stitcher = new ScrollStitcher(new ScrollFrame(FractionalView(page, 0, 480)));
            foreach (double top in new[] { 60.4, 120.8, 181.2 })
            {
                var result = stitcher.Add(new ScrollFrame(FractionalView(page, top, 480)));
                Require(result == ScrollAppendStatus.Added && Math.Abs(stitcher.Height - 480 - top) <= 1, "Fractional article failed at " + top + ": " + result + " height " + stitcher.Height);
            }
        });
        test("Scrolling smooth gradient content retains exact integer joins", () =>
        {
            var page = SmoothPage(); var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 480)));
            foreach (int top in new[] { 37, 90, 160, 270 }) Require(stitcher.Add(new ScrollFrame(View(page, top, 480))) == ScrollAppendStatus.Added, "Gradient failed at " + top);
            Require(Pixels(stitcher.Image()).SequenceEqual(Pixels(View(page, 0, 750))));
        });
        test("Scrolling integer article offsets do not drift with small pixel noise", () =>
        {
            var page = Article(); var stitcher = new ScrollStitcher(new ScrollFrame(Noisy(View(page, 0, 480), 0)));
            for (int i = 1; i <= 8; i++) Require(stitcher.Add(new ScrollFrame(Noisy(View(page, i * 83, 480), i))) == ScrollAppendStatus.Added && stitcher.Height == 480 + i * 83, "Noisy integer offset drifted at " + i);
        });
        test("Scrolling capture refreshes a changed stationary view and resumes without scrolling back", () =>
        {
            var page = Article(); var first = WithWidget(View(page, 0, 480), 0);
            var stitcher = new ScrollStitcher(new ScrollFrame(first));
            Require(stitcher.Add(new ScrollFrame(WithWidget(View(page, 0, 480), 1))) == ScrollAppendStatus.Unchanged, "Widget update was mistaken for lost overlap");
            Require(stitcher.Height == 480 && Pixels(stitcher.Image()).SequenceEqual(Pixels(first)));
            Require(stitcher.Add(new ScrollFrame(WithWidget(View(page, 90, 480), 1))) == ScrollAppendStatus.Added && stitcher.Height == 570);
        });
        test("Scrolling capture processes representative viewport detail within the sampling budget", () =>
        {
            var page = Article(1200, 2400); var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 900)));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Require(stitcher.Add(new ScrollFrame(View(page, 240, 900))) == ScrollAppendStatus.Added);
            clock.Stop(); Console.WriteLine("Alignment 1200x900: " + clock.ElapsedMilliseconds + " ms");
            Require(clock.ElapsedMilliseconds < 800, "Matcher may lose viewports while scrolling");
        });
        test("Scrolling input uses the correct native INPUT structure size", () =>
            Require(System.Runtime.InteropServices.Marshal.SizeOf<ScrollCaptureInput.WheelInput>() == (IntPtr.Size == 8 ? 40 : 28)));
        test("Scrolling matcher rejects repeated line ambiguity and unrelated pages", () =>
        {
            var repeated = new byte[320 * 480 * 4];
            for (int y = 0; y < 480; y++) for (int x = 0; x < 320; x++)
            { int p = (y * 320 + x) * 4; byte c = y % 40 < 10 ? (byte)20 : (byte)255; repeated[p] = repeated[p + 1] = repeated[p + 2] = c; repeated[p + 3] = 255; }
            var image = BitmapSource.Create(320, 480, 96, 96, PixelFormats.Bgra32, null, repeated, 1280); image.Freeze();
            var stitcher = new ScrollStitcher(new ScrollFrame(View(image, 0, 240)));
            Require(stitcher.Add(new ScrollFrame(View(image, 17, 240))) == ScrollAppendStatus.NeedsOverlap);
            Require(stitcher.Add(new ScrollFrame(Page(320, 240))) == ScrollAppendStatus.NeedsOverlap && stitcher.Height == 240);
        });
        test("Scrolling later views repair placeholder rows still visible in the overlap", () =>
        {
            var page = Page(160, 900); var placeholder = BlankTail(View(page, 120, 240));
            var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 240)));
            Require(stitcher.Add(new ScrollFrame(placeholder)) == ScrollAppendStatus.Added);
            Require(stitcher.Add(new ScrollFrame(View(page, 150, 240))) == ScrollAppendStatus.Added);
            Require(Pixels(stitcher.Image()).SequenceEqual(Pixels(View(page, 0, 390))), "Placeholder rows were permanently committed");
        });
        test("Scrolling Done refreshes the final viewport after placeholder content finishes painting", () =>
        {
            var page = Page(160, 900); int reads = 0;
            using var session = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => reads++ == 0 ? View(page, 0, 240) : reads == 2 ? BlankTail(View(page, 120, 240)) : View(page, 120, 240), false);
            session.Start(); for (int i = 0; i < 40 && session.Frames < 2; i++) pump(30);
            Require(session.Frames == 2); session.Finish(); for (int i = 0; i < 50 && !session.Completion.IsCompleted; i++) pump(30);
            Require(session.Completion.IsCompletedSuccessfully && Pixels(session.Completion.Result!).SequenceEqual(Pixels(View(page, 0, 360))));
        });
        test("Scrolling persistent overlap loss warns even when a large region keeps changing", () =>
        {
            var page = Page(640, 1600); int reads = 0;
            BitmapSource Read(Rect _)
            {
                var view = View(page, reads++ == 0 ? 0 : 800, 480); var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                { drawing.DrawImage(view, new System.Windows.Rect(0, 0, 640, 480)); drawing.DrawRectangle(reads % 2 == 0 ? Brushes.Blue : Brushes.Yellow, null, new System.Windows.Rect(300, 20, 300, 280)); }
                var result = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
            }
            using var session = new ScrollCaptureSession(new Rect(0, 0, 640, 480), Read, false);
            session.Start(); for (int i = 0; i < 100 && session.Warning is null; i++) pump(30);
            Require(session.Warning is not null && session.Height == 480, "Persistent moving mismatch did not report a capture limit"); session.Cancel();
        });
        test("Scrolling ambiguous low-contrast motion is not reported as unchanged", () =>
        {
            var data = new byte[320 * 800 * 4];
            for (int y = 0; y < 800; y++) for (int x = 0; x < 320; x++)
            { int p = (y * 320 + x) * 4; byte value = y % 40 == 0 ? (byte)245 : (byte)255; data[p] = data[p + 1] = data[p + 2] = value; data[p + 3] = 255; }
            var page = BitmapSource.Create(320, 800, 96, 96, PixelFormats.Bgra32, null, data, 1280); page.Freeze();
            var stitcher = new ScrollStitcher(new ScrollFrame(View(page, 0, 240)));
            Require(stitcher.Add(new ScrollFrame(View(page, 17, 240))) == ScrollAppendStatus.NeedsOverlap);
        });
        test("Scrolling session collects continuous movement instead of waiting until overlap is lost", () =>
        {
            var page = Page(160, 1200); int top = -32;
            using var session = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => View(page, top = Math.Min(640, top + 32), 240), false);
            session.Start(); for (int i = 0; i < 80 && session.Height < 880; i++) pump(50);
            Require(session.Height == 880 && session.Warning is null, "Continuous scroll lost the last accepted viewport");
            session.Finish(); for (int i = 0; i < 40 && !session.Completion.IsCompleted; i++) pump(50);
            Require(session.Completion.IsCompletedSuccessfully && Pixels(session.Completion.Result!).SequenceEqual(Pixels(View(page, 0, 880))));
        });
        test("Scrolling session retries a transient mismatch but warns for a genuine gap", () =>
        {
            var page = Page(160, 1600); int reads = 0;
            using (var recovered = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => View(page, reads++ == 0 ? 0 : reads < 4 ? 900 : 60, 240), false))
            {
                recovered.Start(); for (int i = 0; i < 40 && recovered.Frames < 2; i++) pump(50);
                Require(recovered.Frames == 2 && recovered.Warning is null); recovered.Finish();
                for (int i = 0; i < 40 && !recovered.Completion.IsCompleted; i++) pump(50);
                Require(Pixels(recovered.Completion.Result!).SequenceEqual(Pixels(View(page, 0, 300))));
            }
            reads = 0;
            using var gap = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => View(page, reads++ == 0 ? 0 : 900, 240), false);
            gap.Start(); for (int i = 0; i < 50 && gap.Warning is null; i++) pump(50);
            Require(gap.Warning is not null && gap.Height == 240); gap.Finish();
            for (int i = 0; i < 50 && !gap.Completion.IsCompleted; i++) pump(50);
            Require(gap.Completion.IsCompletedSuccessfully && gap.Warning is not null && Pixels(gap.Completion.Result!).SequenceEqual(Pixels(View(page, 0, 240))));
        });
        test("Scrolling session ignores changing stationary widgets without repeated overlap warnings", () =>
        {
            var page = View(Article(), 0, 480); int reads = 0;
            using var session = new ScrollCaptureSession(new Rect(0, 0, 640, 480), _ => WithWidget(page, reads++), false);
            session.Start(); for (int i = 0; i < 30 && reads < 6; i++) pump(50);
            Require(reads >= 6 && session.Warning is null && session.Height == 480);
            session.Finish(); for (int i = 0; i < 40 && !session.Completion.IsCompleted; i++) pump(50);
            Require(session.Completion.IsCompletedSuccessfully && session.Warning is null);
        });
        test("Auto-scroll waits for each animated step to settle before sending another", () =>
        {
            var page = Page(160, 1200); int top = 0, target = 0; bool overshot = false;
            using var input = new FakeInput((_, _) => { if (top != target) overshot = true; target += 120; return Task.FromResult(true); });
            using var session = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => View(page, top = Math.Min(target, top + 30), 240), input);
            session.Changed += () => { if (input.Calls.Count == 2 && session.Height == 480) session.Finish(); };
            session.SetAutoScroll(true); session.Start(); for (int i = 0; i < 160 && !session.Completion.IsCompleted; i++) pump(50);
            Require(!overshot && input.Calls.Count == 2 && session.Completion.IsCompletedSuccessfully, "Auto steps overlapped or did not complete");
            Require(Pixels(session.Completion.Result!).SequenceEqual(Pixels(View(page, 0, 480))));
        });
        test("Auto-scroll retries the wheel route when a provider reports success without pixel movement", () =>
        {
            var page = Page(160, 800); int top = 0;
            using var input = new FakeInput((_, wheel) => { if (wheel) top += 60; return Task.FromResult(true); });
            using var session = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => View(page, top, 240), input);
            session.Changed += () => { if (session.Height == 300) session.Finish(); };
            session.SetAutoScroll(true); session.Start(); for (int i = 0; i < 140 && !session.Completion.IsCompleted; i++) pump(50);
            Require(input.Calls.Count == 2 && !input.Calls[0] && input.Calls[1] && session.Completion.IsCompletedSuccessfully);
            Require(Pixels(session.Completion.Result!).SequenceEqual(Pixels(View(page, 0, 300))));
        });
        test("Auto-scroll stops after unchanged pixels and Finish cancels pending input", () =>
        {
            var page = Page(160, 240);
            using (var input = new FakeInput((_, _) => Task.FromResult(true)))
            using (var session = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => page, input))
            {
                session.SetAutoScroll(true); session.Start(); for (int i = 0; i < 160 && session.AutoScroll; i++) pump(50);
                Require(!session.AutoScroll && input.Calls.Count == 4 && session.Frames == 1 && session.Warning is null);
                session.Cancel(); Require(session.Completion.Result is null);
            }
            bool cancelled = false;
            using var waiting = new FakeInput(async (token, _) => { try { await Task.Delay(10000, token); } finally { cancelled = token.IsCancellationRequested; } return false; });
            using var finishing = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => page, waiting);
            finishing.SetAutoScroll(true); finishing.Start(); for (int i = 0; i < 20 && waiting.Calls.Count == 0; i++) pump(20);
            finishing.Finish(); for (int i = 0; i < 40 && !finishing.Completion.IsCompleted; i++) pump(50);
            Require(cancelled && waiting.Calls.Count == 1 && finishing.Completion.IsCompletedSuccessfully);
        });
        test("Auto-scroll can be turned off and on while a previous input request is cancelling", () =>
        {
            var page = Page(160, 800); int calls = 0, top = 0;
            using var input = new FakeInput(async (token, _) => { if (++calls == 1) await Task.Delay(10000, token); else top = 60; return true; });
            using var session = new ScrollCaptureSession(new Rect(0, 0, 160, 240), _ => View(page, top, 240), input);
            session.Changed += () => { if (session.Height == 300) session.Finish(); };
            session.SetAutoScroll(true); session.Start(); for (int i = 0; i < 30 && calls == 0; i++) pump(20);
            session.SetAutoScroll(false); session.SetAutoScroll(true);
            for (int i = 0; i < 80 && !session.Completion.IsCompleted; i++) pump(50);
            Require(calls == 2 && session.Completion.IsCompletedSuccessfully && session.Completion.Result!.PixelHeight == 300);
        });
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
    private sealed class FakeInput(Func<CancellationToken, bool, Task<bool>> scroll) : IScrollCaptureInput
    {
        public List<bool> Calls { get; } = [];
        public bool Available => true;
        public bool TargetMoved => false;
        public Task<bool> ScrollAsync(CancellationToken token, bool useWheel) { Calls.Add(useWheel); return scroll(token, useWheel); }
        public void StopAutoScroll() { }
        public void Dispose() { }
    }
}
