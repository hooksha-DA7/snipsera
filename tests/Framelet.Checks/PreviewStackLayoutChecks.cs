using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Framelet;
using Rect = System.Drawing.Rectangle;

// Pure geometry only: no application, window, display queries or native input.
internal static class PreviewStackLayoutChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Rect Body(Rect window, double dpi)
    {
        int margin = (int)Math.Round(FloatingLayout.Margin * dpi);
        return new(window.X + margin, window.Y + margin, window.Width - 2 * margin, window.Height - 2 * margin);
    }
    private static void CheckArrangement(Rect work, int previews, int pins, bool overflow, double dpi, bool recording = false)
    {
        var placed = FloatingLayout.Stack(work, previews, pins, overflow, dpi, recording);
        Require(placed.Previews.Count == previews && placed.Pins.Count == pins && placed.Overflow.HasValue == overflow, "An available card or chip was omitted");
        var images = placed.Previews.Concat(placed.Pins).ToArray();
        var windows = images.Concat(placed.Overflow is Rect chip ? new[] { chip } : []).ToArray();
        var bodies = windows.Select(window => Body(window, dpi)).ToArray();
        foreach (var window in windows) Require(work.Contains(window), $"Window escapes {work} at {dpi}: {window}");
        foreach (var body in bodies) Require(body.Width > 0 && body.Height > 0 && work.Contains(body), "Invalid visible bounds");
        int frontIndex = recording ? 1 : 0;
        bool InDeck(int index) => index >= frontIndex && index < previews;
        for (int i = 0; i < bodies.Length; i++)
        for (int j = i + 1; j < bodies.Length; j++)
            if (!(InDeck(i) && InDeck(j))) Require(!bodies[i].IntersectsWith(bodies[j]), "Deck overlaps a separate card or overflow");
        if (images.Length > 0)
        {
            var first = bodies[0];
            for (int i = 0; i < images.Length; i++)
                if (!InDeck(i) || i == frontIndex) Require(bodies[i].Size == first.Size, "Front, recording and pin sizes differ");
        }
        for (int i = frontIndex + 1; i < placed.Previews.Count; i++)
        {
            var nearer = bodies[i - 1]; var rear = bodies[i];
            Require(rear.Top < nearer.Top && rear.Bottom > nearer.Top, "A rear card has no exposed strip or no intentional overlap");
            Require(rear.Left >= nearer.Left && rear.Right <= nearer.Right && rear.Height == nearer.Height, "Rear card inset or height is inconsistent");
        }
        if (placed.Overflow is Rect overflowWindow && placed.Previews.Count > 0)
            Require(overflowWindow.Left == placed.Previews[0].Left && Body(overflowWindow, dpi).Bottom < Body(placed.Previews[^1], dpi).Top, "Overflow is not above the pending group");
        if (recording && previews > 1)
            Require(bodies[0].Top - bodies[1].Bottom == (int)Math.Round(FloatingLayout.Gap * dpi), "Recording is not separated below the screenshot deck");
    }

    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); int failures = 0; var outcomes = new List<object>();
        void Check(string name, Action check)
        {
            try { check(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        Check("Screenshot decks stay bounded and overlap only their own rear cards across monitors and DPI", () =>
        {
            Rect[] areas = [new(0, 0, 1920, 1040), new(-1920, 0, 1920, 1040), new(-1600, -1000, 1600, 950), new(64, 48, 1216, 672)];
            foreach (var work in areas) foreach (double dpi in new[] { 1d, 1.25, 1.5, 2 })
            foreach (int count in new[] { 0, 1, 2, 3 }) for (int pins = 0; pins <= 8; pins++)
            foreach (bool overflow in new[] { false, true }) CheckArrangement(work, count, pins, overflow, dpi);
        });
        Check("Full-size deck uses centered eight-DIP insets, twenty-DIP peeks and a compact 190-DIP footprint", () =>
        {
            foreach (double dpi in new[] { 1d, 1.25, 1.5, 2 })
            {
                var placed = FloatingLayout.Stack(new Rect(0, 0, 3840, 2160), 3, 1, true, dpi);
                var images = placed.Previews.Concat(placed.Pins).Select(rect => Body(rect, dpi)).ToArray();
                Require(images[0].Width == (int)(FloatingLayout.CardWidth * dpi) && images[0].Height == (int)(FloatingLayout.CardHeight * dpi) && images[3].Size == images[0].Size, "Front or reference needlessly shrinks");
                int gap = (int)Math.Round(FloatingLayout.Gap * dpi);
                for (int depth = 1; depth <= 2; depth++)
                {
                    Require(images[depth].Top == images[0].Top - (int)(FloatingLayout.DeckPeek * dpi) * depth, "Deck peek spacing changed");
                    Require(images[depth].Left == images[0].Left + (int)(FloatingLayout.DeckInset * dpi) * depth && images[depth].Right == images[0].Right - (int)(FloatingLayout.DeckInset * dpi) * depth, "Rear card is not inset symmetrically");
                    Require(images[depth].Height == images[0].Height, "Rear card height changed");
                }
                Require(images[0].Bottom - images[2].Top == (int)(190 * dpi), "Deck footprint is not compact");
                var chip = Body(placed.Overflow!.Value, dpi);
                Require(chip.Width == images[0].Width && chip.Height == (int)(FloatingLayout.OverflowHeight * dpi) && images[2].Top - chip.Bottom == gap, "Overflow spacing/size changed");
                Require(chip.Top - images[3].Bottom == gap, "Pins are not placed above overflow when space is available");
            }
        });
        Check("References use neighbouring columns before shrinking full-size pending cards", () =>
        {
            var placed = FloatingLayout.Stack(new Rect(0, 0, 1920, 1040), 3, 8, true, 1);
            Require(Body(placed.Previews[0], 1).Width == 200 && placed.Pins.All(window => Body(window, 1).Width == 200), "Available column space was ignored");
            Require(placed.Pins.Any(window => window.Left > placed.Previews[0].Left), "References did not form adjacent columns");
            CheckArrangement(new(0, 0, 1920, 1040), 3, 8, true, 1);
        });
        Check("Pins preserve a vacant capture slot when no pending preview exists", () =>
        {
            var work = new Rect(0, 0, 1920, 1040);
            var empty = FloatingLayout.Stack(work, 0, 3, false, 1);
            var next = FloatingLayout.Stack(work, 1, 3, false, 1);
            Require(empty.Previews.Count == 0 && empty.Pins.SequenceEqual(next.Pins), "A new first preview moved existing references");
        });
        Check("A recording remains a full card below the screenshot deck across scales and pin counts", () =>
        {
            foreach (var work in new[] { new Rect(0, 0, 1920, 1040), new Rect(-1600, -1000, 1600, 950), new Rect(64, 48, 1216, 672) })
            foreach (double dpi in new[] { 1d, 1.25, 1.5, 2 })
            foreach (int count in new[] { 1, 2, 3 }) for (int pins = 0; pins <= 8; pins++)
            foreach (bool overflow in new[] { false, true }) CheckArrangement(work, count, pins, overflow, dpi, recording: true);
        });
        Check("Small usable work areas scale the deck and references without invalid coordinates", () =>
        {
            foreach (var work in new[] { new Rect(-80, -60, 160, 180), new Rect(-400, 50, 260, 300), new Rect(0, 0, 480, 320) })
            foreach (double dpi in new[] { 1d, 1.25, 1.5, 2 })
            foreach (int count in new[] { 0, 1, 3 }) for (int pins = 0; pins <= 8; pins++)
            foreach (bool overflow in new[] { false, true })
            {
                CheckArrangement(work, count, pins, overflow, dpi);
                if (count > 0) CheckArrangement(work, count, pins, overflow, dpi, recording: true);
            }
        });
        Check("Impossible work areas return empty geometry instead of overlapping or negative rectangles", () =>
        {
            foreach (int size in new[] { 0, 1, 8, 20, 32 }) foreach (double dpi in new[] { 1d, 1.25, 2 })
            {
                var placed = FloatingLayout.Stack(new Rect(-50, -70, size, size), 3, 8, true, dpi);
                Require(placed.Previews.Count == 0 && placed.Pins.Count == 0 && placed.Overflow is null, "An impossible layout returned unsafe rectangles");
            }
        });
        Check("Stack inputs reject invalid counts and scales", () =>
        {
            void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid input accepted"); }
            foreach (double dpi in new[] { 0d, -1, double.NaN, double.PositiveInfinity }) Reject(() => FloatingLayout.Stack(new(0, 0, 1000, 800), 1, 1, false, dpi));
            Reject(() => FloatingLayout.Stack(new(0, 0, 1000, 800), -1, 0, false, 1));
            Reject(() => FloatingLayout.Stack(new(0, 0, 1000, 800), 4, 0, false, 1));
            Reject(() => FloatingLayout.Stack(new(0, 0, 1000, 800), 1, -1, false, 1));
            Reject(() => FloatingLayout.Stack(new(0, 0, 1000, 800), 1, 9, false, 1));
            Reject(() => FloatingLayout.Stack(new(0, 0, 1000, 800), 0, 0, false, 1, separateRecording: true));
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
