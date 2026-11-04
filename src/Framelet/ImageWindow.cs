using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingRect = System.Drawing.Rectangle;

namespace Framelet;

public sealed class ImageWindow : Window
{
    // Logical-pixel metrics for the on-image controls.
    private const double Inset = 6, Unit = 28, Gap = 4, Cluster = 6, PillWidth = 68, Spill = 6;
    public BitmapSource CaptureImage { get; }
    public bool IsPinned { get; private set; }
    public bool IsDocked { get; private set; } = true;
    public DrawingRect DockWorkArea { get; private set; }
    public double DockScale { get; private set; } = 1;
    public double ImageOpacity { get => thumbnail.Opacity; private set { thumbnail.Opacity = Math.Clamp(value, .2, 1); edge.Opacity = thumbnail.Opacity; } }
    private readonly Image thumbnail;
    private readonly Border edge = new(), scrim, message;
    private readonly Grid controls;
    private readonly ContextMenu menu, overflow;
    private readonly Button pinButton, editButton, copyButton, saveButton, dragButton, closeButton, moreButton;
    private readonly List<Button> folded = [];
    private readonly MenuItem pinMenu, opacityLabel, opacityItem;
    private readonly Slider opacity;
    private readonly Thumb grip;
    private readonly TextBlock status = Ui.Text("", 12, Ui.Ink);
    private readonly DispatcherTimer toast = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer dismiss = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer copied = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private bool keyboardActions, revealed, labels = true;
    private double unit = Unit;
    private readonly OverlayHideGesture hideGesture = new();
    // Temporary-hide motion animates only the content root: a short vertical drift plus opacity. The native window never moves or resizes.
    private const double Drift = 18;
    private static readonly TimeSpan Leave = TimeSpan.FromMilliseconds(150), Return = TimeSpan.FromMilliseconds(180);
    private readonly Grid presented;
    private readonly TranslateTransform drift = new();
    private int presentation;
    private bool closed, placedOnce;

    public ImageWindow(CaptureApp app, BitmapSource image, CaptureEntry entry, bool pinned, string? warning = null)
    {
        CaptureImage = image;
        Ui.Setup(this); Width = FloatingLayout.MaxWidth; Height = FloatingLayout.MaxHeight; MinWidth = 18; MinHeight = 18;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true; Topmost = true; ShowActivated = false;
        var root = new Grid { Margin = new Thickness(FloatingLayout.Margin), RenderTransform = drift }; presented = root;
        var card = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(FloatingVisuals.Radius),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = .18 } };
        var surface = new Grid { Background = Brushes.Transparent };
        surface.SizeChanged += (_, e) => { FloatingVisuals.Round(surface, e.NewSize); LayoutControls(e.NewSize); };
        // Docked cards share one size, so the thumbnail fills the card and crops evenly around the centre. A resized pin follows the
        // capture's aspect ratio and shows it whole. Copy, Save, Annotate and Drag always use the full bitmap.
        thumbnail = new Image { Source = image, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
        surface.Children.Add(new Border { ClipToBounds = true, Child = thumbnail });
        // A soft top/bottom shade lifts the corner controls without blurring or tinting the capture's centre.
        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 0, 0, 0), 0)); shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0, 0, 0), .5)); shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 0, 0, 0), 1)); shade.Freeze();
        scrim = new Border { Background = shade, Opacity = 0, IsHitTestVisible = false }; surface.Children.Add(scrim);
        edge.BorderBrush = Ui.Line; edge.BorderThickness = new Thickness(.7); edge.CornerRadius = new CornerRadius(FloatingVisuals.Radius); edge.IsHitTestVisible = false; surface.Children.Add(edge);
        card.Child = surface; root.Children.Add(card);
        message = new Border { Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
            Child = status, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

        // Controls live on the image, outside its rounded mask, so shallow captures may use the window's shadow margin instead of clipping.
        var layer = new Canvas(); var spare = new Canvas();
        var bar = new Border { Name = "CaptureActionBar", Child = layer };
        controls = new Grid { Opacity = 0, Visibility = Visibility.Hidden }; controls.Children.Add(bar); controls.Children.Add(spare);
        AutomationProperties.SetName(controls, "Capture actions");
        KeyboardNavigation.SetTabNavigation(controls, KeyboardNavigationMode.Cycle);
        root.Children.Add(controls);
        Button AddControl(Panel host, string symbol, string label, string id, Action action, string tip)
        {
            var button = Ui.IconButton(symbol, label, id, action); button.Tag = symbol; button.ToolTip = tip; button.Content = new UiIcon(symbol, 15, 2);
            button.Margin = button.Padding = new Thickness(0); button.MinWidth = button.MinHeight = 0; button.Width = button.Height = Unit;
            // Colours come from the scoped floating style so its pointer-over/pressed/focus states can apply.
            button.ClearValue(BackgroundProperty); button.ClearValue(BorderBrushProperty); button.ClearValue(ForegroundProperty); FloatingButtons.Apply(button);
            button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
            host.Children.Add(button); return button;
        }
        void Toggle() { RevealActions(false); app.TogglePin(this); }
        async void Copy() { try { await CaptureService.Copy(image); Copied(); } catch (Exception ex) { app.Report(ex); } }
        void Save() { RevealActions(false); app.SaveImage(image, this); }
        pinButton = AddControl(layer, "Pin", "Pin", "PinCapture", Toggle, "Pin");
        editButton = AddControl(layer, "Pen", "Annotate", "EditCapture", () => { RevealActions(false); app.EditImage(image, entry); }, "Annotate");
        copyButton = AddControl(layer, "Copy", "Copy", "CopyCapture", Copy, "Copy · Ctrl+C");
        saveButton = AddControl(layer, "Save", "Save…", "SaveCapture", Save, "Save… · Ctrl+S");
        dragButton = AddControl(layer, "Drag", "Drag image to another app", "DragCapture", () => Notice("Drag this button into another app"), "Drag to another app");
        DragExport.Attach(dragButton, app.History.Root, () => image, app.Report);
        closeButton = AddControl(layer, "Close", "Close", "CloseCapture", Close, "Close · Esc");
        foreach (var pill in new[] { copyButton, saveButton }) { pill.FontSize = 12; pill.FontWeight = FontWeights.SemiBold; }
        overflow = new ContextMenu { Placement = PlacementMode.Bottom };
        moreButton = AddControl(spare, "More", "More actions", "MoreCapture", OpenOverflow, "More actions"); moreButton.Visibility = Visibility.Collapsed;
        overflow.PlacementTarget = moreButton;
        overflow.Closed += (_, _) => { if (keyboardActions && revealed) moreButton.Focus(); ScheduleDismiss(); };
        void OpenOverflow()
        {
            overflow.Items.Clear();
            foreach (var button in new[] { pinButton, editButton, copyButton, saveButton, closeButton }.Where(folded.Contains))
            {
                var item = new MenuItem { Header = AutomationProperties.GetName(button), Icon = new UiIcon((string)button.Tag, 16) { Ink = Ui.Ink }, InputGestureText = button == copyButton ? "Ctrl+C" : button == saveButton ? "Ctrl+S" : "" };
                item.Click += (_, _) => { overflow.IsOpen = false; Dispatcher.BeginInvoke(() => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))); };
                overflow.Items.Add(item);
            }
            overflow.IsOpen = true;
        }
        copied.Tick += (_, _) => { copied.Stop(); copyButton.Content = labels ? "Copy" : ActionIcon("Copy"); };
        root.Children.Add(message);

        menu = new ContextMenu(); root.ContextMenu = menu;
        MenuItem Menu(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => { menu.IsOpen = false; RevealActions(false); Dispatcher.BeginInvoke(action); }; menu.Items.Add(item); return item; }
        Menu("Capture area", () => _ = app.CaptureAsync(CaptureKind.Area)); Menu("Capture window", () => _ = app.CaptureAsync(CaptureKind.Window)); Menu("Capture display", () => _ = app.CaptureAsync(CaptureKind.Screen));
        Menu("Scrolling capture", () => _ = app.CaptureAsync(CaptureKind.Scrolling)); menu.Items.Add(new Separator());
        pinMenu = Menu("Pin", Toggle); Menu("Copy", Copy).InputGestureText = "Ctrl+C"; Menu("Save…", Save).InputGestureText = "Ctrl+S"; Menu("History", app.ShowHome); Menu("Annotate", () => app.EditImage(image, entry));
        // Keyboard-discoverable alternative to the wheel-down gesture.
        Menu("Temporarily hide overlays", () => app.HideOverlays(this)).InputGestureText = "Scroll down";
        opacity = new Slider { Minimum = .2, Maximum = 1, Value = 1, Width = 150, TickFrequency = .05, Margin = new Thickness(8, 5, 8, 8), ToolTip = "Pinned image opacity" };
        AutomationProperties.SetAutomationId(opacity, "PinOpacity"); opacity.ValueChanged += (_, _) => ImageOpacity = opacity.Value;
        opacityLabel = new MenuItem { Header = Ui.Text("Image opacity · Ctrl+wheel", 12, Ui.Muted), Focusable = false, IsHitTestVisible = false };
        opacityItem = new MenuItem { Header = opacity, StaysOpenOnClick = true }; menu.Items.Add(new Separator()); menu.Items.Add(opacityLabel); menu.Items.Add(opacityItem); Menu("Close", Close);
        menu.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { menu.IsOpen = false; e.Handled = true; } };
        menu.Opened += (_, _) => RevealActions(false); menu.Closed += (_, _) => ScheduleDismiss();

        // The resize grip sits on the outer corner so it never covers the Drag control.
        grip = new Thumb { Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -7, -7), Cursor = Cursors.SizeNWSE, Opacity = 0, ToolTip = "Resize pinned image", Background = Ui.Chrome };
        grip.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'><Path Stroke='Gray' StrokeThickness='1.5' Data='M 4,12 L 12,4 M 9,12 L 12,9' /></Border></ControlTemplate>");
        grip.DragStarted += (_, _) => RevealActions(false); grip.DragDelta += (_, e) => ResizeCard(Math.Max(32, Width + e.HorizontalChange)); root.Children.Add(grip);
        Content = root; SetPinned(pinned);
        void Hover() { dismiss.Stop(); if (!menu.IsOpen && !app.TrayMenuOpen) RevealActions(true); }
        root.MouseEnter += (_, _) => { Hover(); grip.Opacity = 1; }; root.MouseLeave += (_, _) => { grip.Opacity = 0; ScheduleDismiss(); };
        dismiss.Tick += (_, _) => { dismiss.Stop(); if (Mouse.LeftButton == MouseButtonState.Pressed) { dismiss.Start(); return; } if (!root.IsMouseOver && !overflow.IsOpen && !(keyboardActions && controls.IsKeyboardFocusWithin)) RevealActions(false); };
        controls.PreviewMouseDown += (_, _) => keyboardActions = false;
        controls.GotKeyboardFocus += (_, _) => Hover(); controls.LostKeyboardFocus += (_, _) => ScheduleDismiss();
        // Any hide (ours, capture's or the editor's) cancels a pending presentation and leaves a fully opaque, unshifted card for the next show.
        IsVisibleChanged += (_, _) => { if (!IsVisible) { menu.IsOpen = false; RevealActions(false); presentation++; StopPresentation(true); } };
        LocationChanged += (_, _) => RevealActions(false); Deactivated += (_, _) => { keyboardActions = false; ScheduleDismiss(); };
        DragExport.Attach(surface, app.History.Root, () => image, app.Report, true, () => !IsPinned);
        surface.MouseLeftButtonDown += (_, e) =>
        {
            if (DragExport.IsControl(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) { if (IsPinned) ResizeCard(Width < 350 ? Math.Max(360, Math.Min(600, image.PixelWidth / DockScale + 16)) : FloatingLayout.Preview(DockWorkArea, new Size(image.PixelWidth, image.PixelHeight), DockScale).Width / DockScale); else app.EditImage(image, entry); e.Handled = true; return; }
            if (IsPinned && e.LeftButton == MouseButtonState.Pressed)
            {
                RevealActions(false); var handle = new WindowInteropHelper(this).Handle; Native.GetWindowRect(handle, out var before);
                DragMove(); Native.GetWindowRect(handle, out var after);
                if (before.Left != after.Left || before.Top != after.Top) IsDocked = false;
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) { keyboardActions = true; if (!revealed) { RevealActions(true); controls.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); e.Handled = true; } }
            else if (e.Key == Key.Escape) { if (revealed) { bool focused = controls.IsKeyboardFocusWithin; RevealActions(false); if (focused) Focus(); } else Close(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) { Copy(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Save(); e.Handled = true; }
            else if (IsPinned && e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.FocusedElement is not Slider) { Nudge(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1); e.Handled = true; }
        };
        MouseWheel += (_, e) =>
        {
            if (IsPinned && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { hideGesture.Reset(); opacity.Value = ImageOpacity + Math.Sign(e.Delta) * .05; e.Handled = true; return; }
            // Plain wheel-down over the image slides every floating capture away. A full notch (120) is required, accumulated briefly,
            // so a precision-touchpad graze or a stray upward scroll never hides anything.
            if (e.Delta >= 0 || Keyboard.Modifiers != ModifierKeys.None || Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed
                || Mouse.MiddleButton == MouseButtonState.Pressed || Mouse.Captured is not null || menu.IsOpen || overflow.IsOpen
                || e.OriginalSource is not Visual source || DragExport.IsControl(source) || controls.IsAncestorOf(source) || grip.IsAncestorOf(source))
            { hideGesture.Reset(); return; }
            bool hide = hideGesture.Push(e.Delta, Environment.TickCount64, true); e.Handled = true;
            if (hide) { RevealActions(false); app.HideOverlays(this); }
        };
        Loaded += (_, _) =>
        {
            // Place once. A later Loaded (re-show, theme or visual-tree changes) must not move a pin the user moved or resized.
            if (placedOnce) return; placedOnce = true;
            DockWorkArea = Native.MonitorAtCursor(true); var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(handle, new IntPtr(-1), DockWorkArea.Left + 12, DockWorkArea.Top + 12, 0, 0, 0x0011); DockScale = Native.GetDpiForWindow(handle) / 96d;
            if (IsPinned) app.ReflowPins(); else Place(FloatingLayout.Preview(DockWorkArea, new Size(image.PixelWidth, image.PixelHeight), DockScale), DockScale);
            if (warning is not null) Notice(warning);
        };
        toast.Tick += (_, _) => { toast.Stop(); message.Visibility = Visibility.Collapsed; };
        // High contrast switches the floating template; theme colours already follow the bound tokens.
        void Restyle() { foreach (var button in Actions.Append(moreButton)) FloatingButtons.Apply(button, button == pinButton && IsPinned); }
        Appearance.Current.Changed += Restyle;
        Closed += (_, _) => { closed = true; presentation++; StopPresentation(true); toast.Stop(); dismiss.Stop(); copied.Stop(); overflow.IsOpen = false; menu.IsOpen = false; Appearance.Current.Changed -= Restyle; };
    }

    private void Notice(string value) { status.Text = value; message.Visibility = Visibility.Visible; toast.Stop(); toast.Start(); }
    private void Copied()
    {
        if (!revealed || copyButton.Visibility != Visibility.Visible) { Notice("Copied"); return; }
        copied.Stop(); copyButton.Content = labels ? "Copied" : ActionIcon("Check"); copied.Start();
    }

    internal void SetPinned(bool pinned)
    {
        IsPinned = pinned; Title = pinned ? "Snipsera — pinned capture" : "Snipsera — preview";
        // Pin is a direct toggle: the accent fill shows the pinned state; it never opens a menu.
        string label = pinned ? "Unpin" : "Pin"; pinMenu.Header = label; AutomationProperties.SetName(pinButton, label);
        pinButton.ToolTip = pinned ? "Unpin · return to preview" : "Pin · keep on screen"; AutomationProperties.SetItemStatus(pinButton, pinned ? "Pinned" : "Not pinned");
        FloatingButtons.Apply(pinButton, pinned);
        opacityLabel.Visibility = opacityItem.Visibility = grip.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(thumbnail, pinned ? "Drag to move · double-click to enlarge · Ctrl+wheel for opacity · scroll down to hide temporarily" : "Drag the image to another app · double-click to annotate · scroll down to hide temporarily");
        if (!pinned)
        {
            opacity.Value = 1;
            if (IsLoaded)
            {
                var bounds = Native.Bounds(new WindowInteropHelper(this).Handle); int margin = (int)Math.Round(FloatingLayout.Edge * DockScale);
                Place(new DrawingRect(DockWorkArea.Left + margin, DockWorkArea.Bottom - margin - bounds.Height, bounds.Width, bounds.Height), DockScale);
            }
        }
    }
    private void ScheduleDismiss() { dismiss.Stop(); dismiss.Start(); }
    internal void DismissActions() { menu.IsOpen = false; RevealActions(false); }

    // Temporary hide/restore presentation, called by CaptureApp's visibility controller. Hide drifts the card down and fades it,
    // then hides the window; show reverses it. animate=false (captures) cancels everything and hides/shows synchronously.
    // A later call always supersedes an earlier one: completions check the generation and never re-show a hidden window.
    internal void PresentOverlay(bool show, bool animate)
    {
        int generation = ++presentation;
        if (closed) return;
        bool motion = animate && Appearance.Current.Policy.Animate;
        if (!show)
        {
            menu.IsOpen = false; RevealActions(false);
            if (!motion || !IsVisible) { StopPresentation(true); Hide(); return; }
            StopPresentation(false); presented.IsHitTestVisible = false;
            var fade = new DoubleAnimation(0, Leave) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            // Hide() raises IsVisibleChanged, which resets the content for the next show.
            fade.Completed += (_, _) => { if (generation == presentation && !closed) Hide(); };
            presented.BeginAnimation(OpacityProperty, fade);
            drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Drift, Leave) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            return;
        }
        if (!motion) { StopPresentation(true); if (!IsVisible) Show(); return; }
        // Continue from wherever an interrupted hide left the content; a hidden window starts from the drifted, transparent state.
        StopPresentation(false);
        if (!IsVisible) { presented.Opacity = 0; drift.Y = Drift; Show(); }
        presented.IsHitTestVisible = true;
        var appear = new DoubleAnimation(1, Return) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        appear.Completed += (_, _) => { if (generation == presentation) StopPresentation(true); };
        presented.BeginAnimation(OpacityProperty, appear);
        drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Return) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    // Stops presentation animations, either resetting to the resting state or holding the current animated values.
    private void StopPresentation(bool reset)
    {
        double opacity = presented.Opacity, offset = drift.Y;
        presented.BeginAnimation(OpacityProperty, null); drift.BeginAnimation(TranslateTransform.YProperty, null);
        presented.Opacity = reset ? 1 : opacity; drift.Y = reset ? 0 : offset;
        if (reset) presented.IsHitTestVisible = true;
    }
    public void RevealActions(bool reveal)
    {
        if (!reveal) { keyboardActions = false; overflow.IsOpen = false; }
        dismiss.Stop(); revealed = reveal && IsVisible;
        double shade = revealed && !SystemParameters.HighContrast ? ImageOpacity : 0;
        var duration = Appearance.Current.Policy.Animate ? TimeSpan.FromMilliseconds(revealed ? 120 : 90) : TimeSpan.Zero;
        if (revealed) { controls.Visibility = Visibility.Visible; controls.Effect = SystemParameters.HighContrast ? null : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = .22 }; }
        if (duration == TimeSpan.Zero || !IsVisible)
        {
            controls.BeginAnimation(OpacityProperty, null); scrim.BeginAnimation(OpacityProperty, null);
            controls.Opacity = revealed ? 1 : 0; scrim.Opacity = shade; controls.Visibility = revealed ? Visibility.Visible : Visibility.Hidden; return;
        }
        var fade = new DoubleAnimation(revealed ? 1 : 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) => { if (!revealed) controls.Visibility = Visibility.Hidden; };
        controls.BeginAnimation(OpacityProperty, fade); scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(shade, duration));
    }
    public void Place(DrawingRect bounds, double scale)
    {
        RevealActions(false); Width = bounds.Width / scale; Height = bounds.Height / scale;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
    }

    private Button[] Actions => [pinButton, editButton, copyButton, saveButton, dragButton, closeButton];
    private double IconSize => Math.Round(unit * .54);
    private UiIcon ActionIcon(string symbol) => new(symbol, IconSize, 2);
    private void LayoutControls(Size area)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        folded.Clear(); moreButton.Visibility = Visibility.Collapsed; overflow.IsOpen = false;
        foreach (var button in Actions) button.Visibility = Visibility.Visible;
        if (!LayoutCorners(area.Width, area.Height)) LayoutStrip(area.Width, area.Height);
    }
    // CleanShot arrangement: Pin/Close/Annotate/Drag in the corners, Copy and Save centred. Labeled pills first, icon pills when narrower.
    private bool LayoutCorners(double w, double h)
    {
        double corners = 2 * (Inset + Unit) + Gap, rail = Inset + Unit + Gap;
        if (w < corners || h < corners) return false;
        foreach (bool label in new[] { true, false })
            foreach (bool stacked in new[] { true, false })
            {
                double pill = label ? PillWidth : Unit, cw = stacked ? pill : pill * 2 + Cluster, ch = stacked ? Unit * 2 + Cluster : Unit;
                bool beside = w - 2 * rail >= cw && h >= ch + 2 * Gap, between = h - 2 * rail >= ch && w >= cw + 2 * Gap;
                if (!beside && !between) continue;
                Shape(label, Unit);
                Put(pinButton, Inset, Inset); Put(closeButton, w - Inset - Unit, Inset);
                Put(editButton, Inset, h - Inset - Unit); Put(dragButton, w - Inset - Unit, h - Inset - Unit);
                double x = (w - cw) / 2, y = (h - ch) / 2;
                Put(copyButton, x, y, pill); Put(saveButton, stacked ? x : x + pill + Cluster, stacked ? y + Unit + Cluster : y, pill);
                Order(pinButton, closeButton, copyButton, saveButton, editButton, dragButton);
                return true;
            }
        return false;
    }
    // Shallow or slender captures: one centred strip of the same circular controls over the image. Menu-capable actions fold into More;
    // Drag is a gesture that no menu can carry, so it never folds. More itself goes last, since the right-click menu repeats every folded action.
    private void LayoutStrip(double w, double h)
    {
        bool row = w >= h; double span = row ? w : h, cross = row ? h : w, size = Math.Clamp(cross + 2 * Spill, 16, Unit);
        Shape(false, size);
        Button[][] groups = [[pinButton, editButton], [copyButton, saveButton], [dragButton, moreButton, closeButton]];
        var shown = new HashSet<Button>(Actions);
        List<Button[]> Visible() => groups.Select(g => g.Where(shown.Contains).ToArray()).Where(g => g.Length > 0).ToList();
        double Need() { var visible = Visible(); return visible.Sum(g => g.Length * size + (g.Length - 1) * Gap) + (visible.Count - 1) * Cluster; }
        foreach (var button in new[] { editButton, saveButton, copyButton, pinButton, closeButton })
        {
            if (Need() <= span + 2 * Spill) break;
            shown.Remove(button); shown.Add(moreButton); folded.Add(button); button.Visibility = Visibility.Collapsed;
        }
        if (Need() > span + 2 * Spill) shown.Remove(moreButton);
        moreButton.Visibility = shown.Contains(moreButton) ? Visibility.Visible : Visibility.Collapsed;
        double along = (span - Need()) / 2, across = (cross - size) / 2; var order = new List<Button>();
        foreach (var group in Visible())
        {
            foreach (var button in group) { if (row) Put(button, along, across, size, size); else Put(button, across, along, size, size); along += size + Gap; order.Add(button); }
            along += Cluster - Gap;
        }
        Order([.. order]);
    }
    private void Shape(bool label, double size)
    {
        labels = label; unit = size; copied.Stop();
        // Corner radii follow each button's height in the floating template, so circles and pills stay round at any size.
        foreach (var button in Actions.Append(moreButton)) if (button.Content is UiIcon icon) icon.Width = icon.Height = IconSize;
        copyButton.Content = label ? "Copy" : ActionIcon("Copy"); saveButton.Content = label ? "Save" : ActionIcon("Save");
        copyButton.Padding = saveButton.Padding = label ? new Thickness(10, 0, 10, 0) : new Thickness(0);
    }
    private static void Put(Button button, double x, double y, double width = Unit, double height = Unit)
    {
        Canvas.SetLeft(button, Math.Round(x)); Canvas.SetTop(button, Math.Round(y)); button.Width = width; button.Height = height;
    }
    private static void Order(params Button[] buttons) { for (int i = 0; i < buttons.Length; i++) buttons[i].TabIndex = i; }

    private void Nudge(Key key, int step)
    {
        IsDocked = false; RevealActions(false); var handle = new WindowInteropHelper(this).Handle; Native.GetWindowRect(handle, out var current);
        double scale = Native.GetDpiForWindow(handle) / 96d; int amount = (int)Math.Round(step * scale);
        int x = current.Left + (key == Key.Left ? -amount : key == Key.Right ? amount : 0), y = current.Top + (key == Key.Up ? -amount : key == Key.Down ? amount : 0);
        var work = Native.WorkArea(handle);
        Native.SetWindowPos(handle, new IntPtr(-1), Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - (current.Right - current.Left))), Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - (current.Bottom - current.Top))), 0, 0, 0x0011);
    }
    private void ResizeCard(double width)
    {
        IsDocked = false; var handle = new WindowInteropHelper(this).Handle; var work = Native.WorkArea(handle);
        double scale = Native.GetDpiForWindow(handle) / 96d, ratio = (double)CaptureImage.PixelHeight / CaptureImage.PixelWidth;
        width = Math.Clamp(width, 18, Math.Max(18, work.Width / scale - 24)); double height = (width - 16) * ratio + 16;
        if (height > work.Height / scale - 24) { height = work.Height / scale - 24; width = (height - 16) / ratio + 16; }
        Native.GetWindowRect(handle, out var current); int w = (int)Math.Round(width * scale), h = (int)Math.Round(height * scale);
        Place(new DrawingRect(Math.Clamp(current.Left, work.Left, work.Right - w), Math.Clamp(current.Top, work.Top, work.Bottom - h), w, h), scale);
    }
}
