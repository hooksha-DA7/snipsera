using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static Framelet.CaptureBarVisuals;

namespace Framelet;

// One compact floating bar holding every screenshot mode. Choosing a mode closes the bar first, then hands exactly one
// CaptureKind to the owner; Esc, Close or clicking elsewhere dismisses it without capturing. Number keys 1–5 pick a mode and
// the arrow keys move between the controls. It opens at the bottom centre of the pointer's work area, just above the taskbar,
// and can be dragged; a work area too narrow for the single row gets a wrapped grid of the same full-size controls instead.
public sealed class CaptureBarWindow : Window
{
    // Logical gap between the bottom of the work area (the top of the taskbar) and the window, whose transparent margin adds
    // the shadow space.
    internal const double BottomGap = 12;
    internal static readonly CaptureKind[] Kinds = [CaptureKind.Area, CaptureKind.Window, CaptureKind.Screen, CaptureKind.Scrolling, CaptureKind.PreviousArea];
    private static readonly string[] Symbols = ["Area", "Window", "Screen", "Scrolling", "History"];
    private static readonly string[] Labels = ["Area", "Window", "Display", "Scroll", "Last area"];
    private static readonly string[] Names = ["Capture area", "Capture window", "Capture display", "Scrolling capture", "Repeat last area"];
    private static readonly string[] Helps = ["Drag a rectangle across your screen", "Choose a visible window", "Capture the display where this bar opened",
        "Select content, then scroll", "Capture the same area again"];
    private const string RepeatUnavailable = "Select an area first. Snipsera then remembers it, so you can capture it again from here.";
    private readonly Action<CaptureKind> capture;
    private readonly bool canRepeat;
    // Grid widths tried, widest first, when the single row does not fit.
    private static readonly int[] GridColumns = [3, 2, 1];
    private readonly Button[] modes;
    private readonly Button close;
    // The five modes then Close: visible order in every layout, and the arrow keys' order.
    private readonly Button[] items;
    private readonly Border panel;
    private System.Drawing.Rectangle work;
    // ended: a mode was chosen or the bar is closing, so nothing else may dispatch. moved: dragged, so never re-placed.
    private bool ended, moved;

    // The current layout: 0 for the single row, otherwise the grid's column count. Scrolls: even the chosen layout exceeds the
    // limit, so the panel scrolls (focus brings each control into view).
    internal int Columns { get; private set; }
    internal bool Scrolls { get; private set; }

    public CaptureBarWindow(Action<CaptureKind> capture, bool canRepeat)
    {
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture)); this.canRepeat = canRepeat; Ui.Setup(this);
        Title = "Snipsera — capture";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight; Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.Manual;
        FontSize = 12; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        AutomationProperties.SetAutomationId(this, "CaptureBar");

        modes = new Button[Kinds.Length];
        for (int i = 0; i < Kinds.Length; i++)
        {
            var kind = Kinds[i]; string key = (i + 1).ToString();
            var label = new TextBlock { Text = Labels[i], FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new UiIcon(Symbols[i], ModeIcon) { HorizontalAlignment = HorizontalAlignment.Center }); content.Children.Add(label);
            var button = modes[i] = new Button { Content = content, Width = ModeWidth, Height = ModeHeight, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            bool available = kind != CaptureKind.PreviousArea || canRepeat;
            button.IsEnabled = available; button.ToolTip = available ? $"{Helps[i]}  ·  {key}" : RepeatUnavailable;
            ToolTipService.SetShowOnDisabled(button, true);
            AutomationProperties.SetAutomationId(button, "CaptureBar" + kind); AutomationProperties.SetName(button, Names[i]);
            AutomationProperties.SetHelpText(button, available ? Helps[i] : RepeatUnavailable); AutomationProperties.SetAcceleratorKey(button, key);
            button.Click += (_, _) => Choose(kind);
        }
        close = new Button { Content = new UiIcon("Close", 12, 2), Width = CloseSize, Height = CloseSize, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Close  ·  Esc" };
        AutomationProperties.SetAutomationId(close, "CaptureBarClose"); AutomationProperties.SetName(close, "Close capture bar");
        close.Click += (_, _) => Dismiss();
        items = [.. modes, close];

        panel = CaptureBarVisuals.Panel(Arrangement(0));
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        Content = new Grid { Margin = new Thickness(Shadow), Children = { panel } };
        Restyle();

        // Drag from the grip or any panel background; buttons keep their own input.
        panel.MouseLeftButtonDown += (_, e) =>
        {
            if (Inside<ButtonBase>(e.OriginalSource as DependencyObject) || e.ButtonState != MouseButtonState.Pressed) return;
            DragMove(); moved = true;
        };
        PreviewKeyDown += (_, e) => { if (HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) e.Handled = true; };
        // Clicking anywhere else dismisses the bar, as a flyout would.
        Deactivated += (_, _) => Dismiss();
        Loaded += (_, _) => { Place(); modes[0].Focus(); };
        // Moving onto a display with another scale resizes the window; centre it again at the new scale unless it was dragged.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Place));
        // The HWND takes its final size after Place; keep the bottom edge just above the taskbar as it does, unless dragged.
        SizeChanged += (_, e) => { if (IsLoaded) Position(e.NewSize); };
        Appearance.Current.Changed += Restyle;
        Closing += (_, _) => ended = true;
        Closed += (_, _) => Appearance.Current.Changed -= Restyle;
    }

    // Closes the bar, then dispatches the mode once. False for an unknown mode, an unavailable Last area, or a bar that has
    // already chosen or closed.
    internal bool Choose(CaptureKind kind)
    {
        if (ended || Array.IndexOf(Kinds, kind) < 0 || kind == CaptureKind.PreviousArea && !canRepeat) return false;
        ended = true; Close();
        capture(kind);
        return true;
    }

    // Esc dismisses; 1–5 (top row or keypad) choose a mode. Left and Right step through the modes and Close in visible order,
    // wrapping and skipping an unavailable Last area; Home and End go to the first and last. Up and Down do the same in the
    // single row, and move a whole grid row, without wrapping, in the wrapped grid. Tab and Enter keep their usual behaviour.
    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.None) return false;
        if (key == Key.Escape) { Dismiss(); return true; }
        int digit = key >= Key.D1 && key <= Key.D5 ? key - Key.D1 : key >= Key.NumPad1 && key <= Key.NumPad5 ? key - Key.NumPad1 : -1;
        if (digit >= 0) return Choose(Kinds[digit]);
        if (key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)) return false;
        int at = Array.FindIndex(items, b => b.IsKeyboardFocusWithin), stride = Math.Max(1, Columns);
        int next = key switch
        {
            Key.Home => Seek(-1, 1, false),
            Key.End => Seek(items.Length, -1, false),
            _ when at < 0 => Seek(-1, 1, false),
            Key.Left => Seek(at, -1, true),
            Key.Right => Seek(at, 1, true),
            Key.Up => Seek(at, -stride, stride == 1),
            _ => Seek(at, stride, stride == 1)
        };
        if (next >= 0) items[next].Focus();
        return true;

        // The first enabled control after `from` in steps of `step`; -1 if a non-wrapping walk leaves the controls.
        int Seek(int from, int step, bool wrap)
        {
            for (int i = 1; i <= items.Length; i++)
            {
                int index = from + step * i;
                if (wrap) index = (index % items.Length + items.Length) % items.Length;
                else if (index < 0 || index >= items.Length) return -1;
                if (items[index].IsEnabled) return index;
            }
            return -1;
        }
    }

    // Chooses and applies the layout for a logical limit (the window, shadow margin included; non-positive or NaN means
    // unlimited) and returns the window's resulting logical size. The single row is kept whenever it fits the width; otherwise
    // the widest 3-, 2- or 1-column grid of the same full-size controls that fits. Narrower grids are only taller, so the first
    // to fit the width is also the shortest. If even that exceeds the limit, the panel scrolls. The limit also becomes the
    // window's MaxWidth/MaxHeight, so SizeToContent never grows the HWND beyond it and the scroller receives the clamp; the
    // HWND itself resizes on the next layout pass and Position keeps it bottom-centred. Detached tests can call it, then detach and
    // measure Content with the same limit (Content already includes the shadow margin).
    internal Size Layout(Size limit)
    {
        static double Limit(double value) => double.IsFinite(value) && value > 0 ? value : double.PositiveInfinity;
        double width = Limit(limit.Width), height = Limit(limit.Height);
        var focused = items.FirstOrDefault(b => b.IsKeyboardFocusWithin);
        int columns = 0; var size = Apply(0, false);
        if (size.Width > width)
            foreach (int candidate in GridColumns)
            {
                columns = candidate; size = Apply(candidate, false);
                if (size.Width <= width) break;
            }
        bool scrolls = size.Width > width || size.Height > height;
        if (scrolls) Apply(columns, true);
        Columns = columns; Scrolls = scrolls; MaxWidth = width; MaxHeight = height;
        focused?.Focus();
        return new Size(Math.Min(size.Width, width), Math.Min(size.Height, height));
    }

    // Puts a layout in the panel and returns its unconstrained logical window size.
    private Size Apply(int columns, bool scroll)
    {
        var child = Arrangement(columns);
        panel.Child = scroll ? new ScrollViewer { Content = child, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, IsTabStop = false } : child;
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(panel.DesiredSize.Width + 2 * Shadow, panel.DesiredSize.Height + 2 * Shadow);
    }

    // 0: grip, divider, the five modes, divider, Close in one row. Otherwise a grip strip above a grid of that many columns,
    // filled in reading order with the modes and then Close, so arrow order matches what is seen.
    private UIElement Arrangement(int columns)
    {
        foreach (var item in items) if (item.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(item);
        if (columns <= 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Grip()); row.Children.Add(Divider());
            for (int i = 0; i < modes.Length; i++) { modes[i].Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0); row.Children.Add(modes[i]); }
            row.Children.Add(Divider());
            close.Margin = new Thickness(0); row.Children.Add(close);
            return row;
        }
        var grid = new Grid();
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r <= (items.Length + columns - 1) / columns; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var grip = Grip(true); Grid.SetColumnSpan(grip, columns); grid.Children.Add(grip);
        for (int i = 0; i < items.Length; i++)
        {
            int r = i / columns, c = i % columns;
            Grid.SetRow(items[i], r + 1); Grid.SetColumn(items[i], c);
            items[i].Margin = new Thickness(c == 0 ? 0 : 2, r == 0 ? 0 : 2, 0, 0);
            grid.Children.Add(items[i]);
        }
        return grid;
    }

    private void Dismiss()
    {
        if (ended) return;
        ended = true; Close();
    }

    // Fits the layout to the logical size of the work area under the pointer when the bar opened, then places it at that
    // area's bottom centre, in physical pixels at this window's scale. Runs again after a DPI change unless the bar was dragged.
    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (moved || handle == IntPtr.Zero || !IsVisible) return;
        if (work.IsEmpty) work = Native.MonitorAtCursor(true);
        double scale = Scale(handle);
        Position(Layout(new Size(Math.Floor(Math.Max(0, work.Width) / scale), Math.Floor(Math.Max(0, work.Height) / scale))));
    }

    // Moves the window, without resizing it, to the placement for a logical size. Does nothing once dragged.
    private void Position(Size size)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (moved || ended || handle == IntPtr.Zero || !IsVisible || work.IsEmpty) return;
        var bounds = Placement(work, size, Scale(handle));
        // HWND_TOPMOST, SWP_NOSIZE | SWP_NOACTIVATE.
        Native.SetWindowPos(handle, new IntPtr(-1), bounds.X, bounds.Y, 0, 0, 0x0011);
    }

    private static double Scale(IntPtr handle) { uint dpi = Native.GetDpiForWindow(handle); return dpi > 0 ? dpi / 96d : 1; }

    // Window bounds for a logical size at a scale, horizontally centred BottomGap above the bottom of the work area (so above
    // the taskbar, wherever it is docked) and kept inside it.
    // Pure, so it holds for any monitor origin (including negative) and scale. Place passes a size Layout already fitted to the
    // work area, so the clamp here is only a rounding safety net; it never resizes the HWND (SWP_NOSIZE).
    internal static System.Drawing.Rectangle Placement(System.Drawing.Rectangle work, Size logical, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        static double Valid(double value) => double.IsFinite(value) && value > 0 ? value : 0;
        int width = Math.Min(Math.Max(0, work.Width), ScrollCaptureLayout.Px(Valid(logical.Width), scale));
        int height = Math.Min(Math.Max(0, work.Height), ScrollCaptureLayout.Px(Valid(logical.Height), scale));
        int x = work.Left + (Math.Max(0, work.Width) - width) / 2;
        int y = work.Top + Math.Max(0, Math.Max(0, work.Height) - height - (int)Math.Round(BottomGap * scale));
        return new System.Drawing.Rectangle(x, y, width, height);
    }

    private void Restyle()
    {
        foreach (var button in modes) RecordingSetupVisuals.Apply(button, "Tool");
        RecordingSetupVisuals.Apply(close, "Tool");
        CaptureBarVisuals.Restyle(panel);
    }

    private static bool Inside<T>(DependencyObject? node) where T : DependencyObject
    {
        for (; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is T) return true;
        return false;
    }
}
