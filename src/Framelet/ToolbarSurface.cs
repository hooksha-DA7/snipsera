using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Framelet;

// The existing layered WPF windows retain their controls, rounded panels and shadows. Acrylic belongs to separate native
// plates immediately behind those panels: applying DWM's whole-window backdrop to a layered WPF HWND is not supported.
// Native plates are not Application.Windows, so capture/recording restoration cannot resurrect them after their host hides.
internal sealed class ToolbarSurface : IDisposable
{
    private readonly Window host;
    private readonly ToolbarAppearanceSettings settings;
    private readonly Border[] panels;
    private readonly List<AcrylicPlate> plates = [];
    private HwndSource? hostSource;
    private ToolbarAppearanceOptions? previousOptions;
    private bool disposed, nativeFailed, positioning;
    internal ToolbarSurface(Window host, ToolbarAppearanceSettings settings, params Border[] panels)
    {
        this.host = host; this.settings = settings; this.panels = panels;
        settings.Changed += Refresh; Appearance.Current.Changed += Refresh;
        host.SourceInitialized += Initialized; host.IsVisibleChanged += VisibilityChanged;
        host.LocationChanged += GeometryChanged; host.SizeChanged += SizeChanged;
        host.LayoutUpdated += LayoutUpdated; host.Closed += Closed;
        Refresh();
    }

    private void Initialized(object? sender, EventArgs e)
    {
        hostSource = HwndSource.FromHwnd(new WindowInteropHelper(host).Handle);
        hostSource?.AddHook(HostMessage); Refresh();
    }
    private IntPtr HostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Activation can reorder a topmost host without changing its bounds. Reinsert its backplates after that completes.
        if (message == 0x47 && !positioning && !disposed)
        {
            positioning = true;
            host.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { positioning = false; Position(true); }));
        }
        return IntPtr.Zero;
    }
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh();
    private void GeometryChanged(object? sender, EventArgs e) => Position();
    private void SizeChanged(object sender, SizeChangedEventArgs e) => Position();
    private void LayoutUpdated(object? sender, EventArgs e) => Position();
    private void Closed(object? sender, EventArgs e) => Dispose();
    private void Refresh()
    {
        if (disposed) return;
        if (previousOptions != settings.Options) { previousOptions = settings.Options; nativeFailed = false; }
        var appearance = Appearance.Current;
        bool? nativeResult = null;
        var plan = ToolbarSurfacePlan.Resolve(settings.Options, appearance.Policy.Frosting, ToolbarAppearanceSettings.NativeFrostingSupported, nativeFailed);
        if (!plan.Frosted || !host.IsVisible) ClearPlates();
        else if (plates.Count == 0)
        {
            try
            {
                foreach (var panel in panels) plates.Add(new AcrylicPlate(new WindowInteropHelper(host).Handle));
                nativeResult = true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or ArgumentException)
            {
                nativeFailed = true; ClearPlates();
                plan = ToolbarSurfacePlan.Resolve(settings.Options, appearance.Policy.Frosting, false);
                nativeResult = false;
            }
        }
        var color = appearance.Surface; color.A = plan.Alpha;
        var brush = new SolidColorBrush(color); brush.Freeze();
        foreach (var panel in panels) panel.Background = brush;
        try { foreach (var plate in plates) plate.Theme(appearance.Dark); }
        catch (Exception ex) when (NativeFailure(ex)) { FallBack(); return; }
        Position();
        if (nativeResult.HasValue) settings.ReportNativeResult(nativeResult.Value && !nativeFailed);
    }

    private void Position(bool force = false)
    {
        if (disposed || plates.Count == 0) return;
        if (!host.IsVisible) { ClearPlates(); return; }
        try
        {
            for (int i = 0; i < plates.Count; i++)
            {
                var panel = panels[i];
                if (!panel.IsVisible || panel.ActualWidth < 1 || panel.ActualHeight < 1 || PresentationSource.FromVisual(panel) is not HwndSource) { plates[i].Hide(); continue; }
                // Keep the entire native rectangle inside the WPF round, even if Windows suppresses its own rounding.
                double inset = BackplateInset(panel.CornerRadius.TopLeft);
                if (panel.ActualWidth <= inset * 2 || panel.ActualHeight <= inset * 2) { plates[i].Hide(); continue; }
                var a = panel.PointToScreen(new Point(inset, inset)); var b = panel.PointToScreen(new Point(panel.ActualWidth - inset, panel.ActualHeight - inset));
                plates[i].Place(a, b, force);
            }
        }
        catch (Exception ex) when (NativeFailure(ex)) { FallBack(); }
    }
    private static bool NativeFailure(Exception ex) => ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or ArgumentException;
    internal static double BackplateInset(double radius) => Math.Ceiling(radius * (1 - Math.Sqrt(.5)));
    private void FallBack() { nativeFailed = true; ClearPlates(); settings.ReportNativeResult(false); Refresh(); }
    private void ClearPlates() { foreach (var plate in plates) plate.Dispose(); plates.Clear(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; ClearPlates();
        settings.Changed -= Refresh; Appearance.Current.Changed -= Refresh;
        host.SourceInitialized -= Initialized; host.IsVisibleChanged -= VisibilityChanged;
        host.LocationChanged -= GeometryChanged; host.SizeChanged -= SizeChanged;
        host.LayoutUpdated -= LayoutUpdated; host.Closed -= Closed;
        if (hostSource is { IsDisposed: false }) hostSource.RemoveHook(HostMessage);
    }
}
