using System;
using System.Collections.Generic;
using System.Linq;
using Framelet;

internal static class OverlayVisibilityChecks
{
    private sealed class Card(string name, bool pinned = false)
    {
        internal readonly string Name = name;
        internal bool Visible, Pinned = pinned;
        internal int Left = 42, Top = 128, Width = 240, Height = 166;
        internal double Opacity = .65;
        internal int Shows, Hides;
    }
    private static OverlayVisibility<Card> Model() => new((card, show) => { card.Visible = show; if (show) card.Shows++; else card.Hides++; });
    private static void Require(bool value, string reason = "Overlay visibility assertion failed") { if (!value) throw new Exception(reason); }
    internal static void Run(Action<string, Action> test)
    {
        test("Temporary hiding restores the same cards without changing pin state, bounds, order or opacity", () =>
        {
            var model = Model(); Card[] cards = [new("preview"), new("pin-one", true), new("pin-two", true)];
            foreach (var card in cards) model.Track(card);
            model.Hide(); Require(model.Hidden && model.HiddenCount == 3 && cards.All(c => !c.Visible));
            model.Restore(); Require(!model.Hidden && model.HiddenCount == 0 && model.Desired.SequenceEqual(cards));
            foreach (var card in cards) Require(card.Visible && card.Left == 42 && card.Top == 128 && card.Width == 240 && card.Height == 166 && card.Opacity == .65 && card.Shows == 2 && card.Hides == 1);
            Require(!cards[0].Pinned && cards[1].Pinned && cards[2].Pinned);
        });
        test("Temporary hide and restore are idempotent and do nothing for an empty group", () =>
        {
            var model = Model(); int events = 0; model.Changed += () => events++;
            model.Hide(); model.Restore(); Require(!model.Hidden && events == 0);
            var card = new Card("one"); model.Track(card); model.Track(card); model.Hide(); model.Hide(); model.Restore(); model.Restore();
            Require(card.Shows == 2 && card.Hides == 1 && model.Desired.Length == 1 && events == 3);
        });
        test("Temporary hiding does not resurrect a preview already hidden for editing", () =>
        {
            var model = Model(); var preview = new Card("editing preview"); var pin = new Card("pin", true);
            model.Track(preview); model.Track(pin); model.SetDesired(preview, false); model.Hide();
            Require(model.HiddenCount == 1); model.Restore(); Require(!preview.Visible && pin.Visible && preview.Shows == 1);
        });
        test("New overlays join a temporarily hidden group until explicit restoration", () =>
        {
            var model = Model(); var pin = new Card("pin", true); var next = new Card("new capture");
            model.Track(pin); model.Hide(); model.Track(next);
            Require(model.Hidden && model.HiddenCount == 2 && !next.Visible && next.Shows == 0);
            model.Restore(); Require(pin.Visible && next.Visible && next.Shows == 1);
        });
        test("Capture suspension restores visible intent without reviving an editor-hidden preview", () =>
        {
            var model = Model(); var preview = new Card("editing preview"); var pin = new Card("pin", true);
            model.Track(preview); model.Track(pin); model.SetDesired(preview, false);
            using (model.Suspend()) Require(model.Suspended && !preview.Visible && !pin.Visible);
            Require(!model.Suspended && !preview.Visible && pin.Visible && !model.Hidden);
        });
        test("Capture replacement preserves temporary hiding even when the previous preview was the only card", () =>
        {
            var model = Model(); var old = new Card("old capture"); var next = new Card("new capture");
            model.Track(old); model.Hide();
            using (model.Suspend()) { model.Forget(old); Require(model.Hidden); model.Track(next); Require(!next.Visible); }
            Require(model.Hidden && model.HiddenCount == 1 && !next.Visible);
            model.Restore(); Require(next.Visible && !old.Visible && old.Shows == 1);
        });
        test("Capture cancellation and failure leave a temporarily hidden group hidden", () =>
        {
            var model = Model(); var card = new Card("pin", true); model.Track(card); model.Hide();
            try { using var capture = model.Suspend(); throw new InvalidOperationException("Synthetic capture failure"); }
            catch (InvalidOperationException) { }
            Require(model.Hidden && model.HiddenCount == 1 && !model.Suspended && !card.Visible && card.Shows == 1);
            using (model.Suspend()) { }
            Require(model.Hidden && !card.Visible); model.Restore(); Require(card.Visible);
        });
        test("Restoring during a capture never presents cards before capture suspension ends", () =>
        {
            var model = Model(); var card = new Card("one"); model.Track(card); model.Hide();
            using (model.Suspend()) { model.Restore(); Require(!model.Hidden && !card.Visible); }
            Require(card.Visible);
            using (model.Suspend()) { model.Hide(); Require(!card.Visible); }
            Require(model.Hidden && !card.Visible);
        });
        test("Closing hidden cards updates the count and never recreates a closed card", () =>
        {
            var model = Model(); var first = new Card("one"); var second = new Card("two"); model.Track(first); model.Track(second); model.Hide();
            model.Forget(first); Require(model.HiddenCount == 1); model.Restore(); Require(first.Shows == 1 && second.Visible);
            model.Hide(); model.Forget(second); Require(!model.Hidden && model.HiddenCount == 0);
            var next = new Card("future capture"); model.Track(next); Require(next.Visible && !model.Hidden);
        });
        test("Replacing hidden history previews preserves hiding without flickering other cards", () =>
        {
            var model = Model(); var old = new Card("old"); var next = new Card("next"); model.Track(old); model.Hide();
            using (model.BeginUpdate()) { model.Forget(old); model.Track(next); }
            Require(model.Hidden && !next.Visible && model.HiddenCount == 1);
            model.Restore(); var pin = new Card("pin", true); model.Track(pin); int shows = pin.Shows, hides = pin.Hides;
            using (model.BeginUpdate()) { model.Forget(next); model.Track(new Card("replacement")); }
            Require(pin.Visible && pin.Shows == shows && pin.Hides == hides);
        });
        test("Nested capture suspensions release once and preserve hiding until the outer scope finishes", () =>
        {
            var model = Model(); var card = new Card("one"); model.Track(card);
            var first = model.Suspend(); var second = model.Suspend(); first.Dispose(); first.Dispose();
            Require(model.Suspended && !card.Visible); second.Dispose(); second.Dispose(); Require(!model.Suspended && card.Visible && card.Shows == 2 && card.Hides == 1);
        });
        test("Closing a card during its presentation callback does not leave a ghost overlay", () =>
        {
            OverlayVisibility<Card>? model = null;
            model = new OverlayVisibility<Card>((card, show) => { card.Visible = show; if (show) { card.Visible = false; model!.Forget(card); } });
            var card = new Card("closed during show"); model.Track(card); model.Hide(); model.Restore();
            Require(model.Desired.Length == 0 && !model.Hidden && !card.Visible);
        });
        test("A failed capture suspension does not leave future overlays permanently suppressed", () =>
        {
            bool fail = true;
            var model = new OverlayVisibility<Card>((card, show) => { if (!show && fail) { fail = false; throw new InvalidOperationException("Synthetic presentation failure"); } card.Visible = show; });
            var card = new Card("one"); model.Track(card);
            try { using var capture = model.Suspend(); throw new Exception("Expected the presentation failure"); } catch (InvalidOperationException) { }
            Require(!model.Suspended && card.Visible); model.Hide(); model.Restore(); Require(card.Visible);
        });
    }
}
