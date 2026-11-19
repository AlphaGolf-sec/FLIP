using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Flip;

/// <summary>
/// Aura luminosa arancione FLIP lungo i bordi interni del monitor attivo (stile "AI agent / computer use").
/// Overlay full-screen trasparente, click-through al 100% e mai attivabile: i clic passano sempre alle finestre sottostanti.
/// </summary>
internal sealed class ScreenEdgeAuraWindow : Window
{
    const double EdgeThickness = 28;
    const int FadeInMilliseconds = 180;
    const int FadeOutMilliseconds = 280;
    const int GWL_EXSTYLE = -20;
    const nint WS_EX_TRANSPARENT = 0x00000020;
    const nint WS_EX_TOOLWINDOW = 0x00000080;
    const nint WS_EX_LAYERED = 0x00080000;
    const nint WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_SHOWWINDOW = 0x0040;
    static readonly nint HWND_TOPMOST = new(-1);

    static readonly Color Accent = Color.FromRgb(0xFF, 0x7A, 0x18);
    static readonly Color SuccessPulse = Color.FromRgb(0x2E, 0xCC, 0x71);
    static readonly Color WarningPulse = Color.FromRgb(0xFF, 0xB0, 0x20);

    static ScreenEdgeAuraWindow? current;

    /// <summary>Quando true (modalità test / anteprime) l'aura non viene mai creata.</summary>
    internal static bool Suppressed { get; set; }

    readonly Grid glowLayer = new()
    {
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };
    readonly List<(GradientStop Stop, byte Alpha)> tintedStops = [];
    readonly DispatcherTimer monitorTimer;
    nint handle;
    nint currentMonitor;
    nint currentKeepBelow;
    bool _isActive;
    bool fadingOut;

    ScreenEdgeAuraWindow()
    {
        Title = "FLIP Aura";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Opacity = 0;

        glowLayer.Children.Add(CreateEdge(VerticalAlignment.Top, HorizontalAlignment.Stretch, new Point(0, 0), new Point(0, 1)));
        glowLayer.Children.Add(CreateEdge(VerticalAlignment.Bottom, HorizontalAlignment.Stretch, new Point(0, 1), new Point(0, 0)));
        glowLayer.Children.Add(CreateEdge(VerticalAlignment.Stretch, HorizontalAlignment.Left, new Point(0, 0), new Point(1, 0)));
        glowLayer.Children.Add(CreateEdge(VerticalAlignment.Stretch, HorizontalAlignment.Right, new Point(1, 0), new Point(0, 0)));
        glowLayer.Opacity = 0.55;
        Content = glowLayer;

        // Segue il cursore / la penna della tavoletta grafica tra i monitor: attivo solo mentre l'aura è visibile.
        monitorTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        monitorTimer.Tick += (_, _) =>
        {
            if (_isActive && !fadingOut && IsVisible)
                PositionOnCursorMonitor(currentKeepBelow, force: false, fromTimer: true);
        };
        Closed += (_, _) => { _isActive = false; monitorTimer.Stop(); };
    }

    /// <summary>
    /// Dopo un cambio DPI (monitor secondario / tavoletta con scala diversa) WPF applica il rettangolo suggerito
    /// scalato dal monitor precedente: riapplichiamo subito il rettangolo fisico esatto del monitor attivo.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (!_isActive || fadingOut) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_isActive && !fadingOut && IsVisible)
                PositionOnCursorMonitor(currentKeepBelow, force: true, fromTimer: true);
        }, DispatcherPriority.Render);
    }

    Rectangle CreateEdge(VerticalAlignment vertical, HorizontalAlignment horizontal, Point start, Point end)
    {
        var outer = new GradientStop(Color.FromArgb(0xE6, Accent.R, Accent.G, Accent.B), 0);
        var middle = new GradientStop(Color.FromArgb(0x5C, Accent.R, Accent.G, Accent.B), 0.42);
        var inner = new GradientStop(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B), 1);
        tintedStops.Add((outer, 0xE6));
        tintedStops.Add((middle, 0x5C));
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        brush.GradientStops.Add(outer);
        brush.GradientStops.Add(middle);
        brush.GradientStops.Add(inner);
        bool horizontalEdge = vertical != VerticalAlignment.Stretch;
        return new Rectangle
        {
            Fill = brush,
            VerticalAlignment = vertical,
            HorizontalAlignment = horizontal,
            Height = horizontalEdge ? EdgeThickness : double.NaN,
            Width = horizontalEdge ? double.NaN : EdgeThickness,
            IsHitTestVisible = false
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        handle = new WindowInteropHelper(this).Handle;
        nint ex = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Accende o spegne l'aura. <paramref name="keepBelow"/> (opzionale) è una finestra che deve restare sopra l'aura (la ruota).
    /// </summary>
    internal static void SetActive(bool active, nint keepBelow = 0)
    {
        if (Suppressed) active = false;
        try
        {
            if (active)
            {
                current ??= new ScreenEdgeAuraWindow();
                current.Ignite(keepBelow);
            }
            else
            {
                current?.FadeOut();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            Evidence.Log("aura.error", ex.Message);
            try { current?.Close(); } catch (InvalidOperationException) { }
            current = null;
        }
    }

    /// <summary>
    /// Riallinea immediatamente l'aura sul monitor su cui si trova il cursore/penna (multi-monitor e tavolette grafiche).
    /// </summary>
    internal static void SyncCursorMonitor(nint keepBelow = 0)
    {
        if (Suppressed || current is not { _isActive: true, fadingOut: false } aura) return;
        nint effectiveBelow = keepBelow != 0 ? keepBelow : aura.currentKeepBelow;
        aura.PositionOnCursorMonitor(effectiveBelow, force: false, fromTimer: false);
    }

    /// <summary>Impulso rapido al termine della conversione: verde se riuscita, ambra se fallita.</summary>
    internal static void Pulse(bool success)
    {
        if (Suppressed || current is not { _isActive: true, fadingOut: false } aura) return;
        aura.RunPulse(success ? SuccessPulse : WarningPulse);
    }

    void Ignite(nint keepBelow)
    {
        bool firstShow = !IsVisible;
        bool wasFading = fadingOut;
        _isActive = true;
        fadingOut = false;
        currentKeepBelow = keepBelow;
        if (firstShow) Show();
        PositionOnCursorMonitor(keepBelow, force: true, fromTimer: false);
        if (!monitorTimer.IsEnabled) monitorTimer.Start();
        if (firstShow || wasFading)
        {
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(FadeInMilliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            BeginAnimation(OpacityProperty, fadeIn);
        }
        StartBreathing();
    }

    /// <summary>
    /// Copre il monitor sotto il cursore/penna (anche con coordinate negative o DPI diverso dal primario):
    /// aggiorna le proprietà WPF in DIP via TransformFromDevice e poi applica il rettangolo fisico esatto sull'HWND.
    /// </summary>
    void PositionOnCursorMonitor(nint keepBelow, bool force, bool fromTimer = false)
    {
        if (!_isActive || fadingOut || handle == 0) return;
        if (!Native.GetCursorPos(out var cursor)) return;
        nint monitor = Native.MonitorFromPoint(cursor, 2);
        if (monitor == 0) return;
        if (!force && monitor == currentMonitor && (keepBelow == 0 || keepBelow == currentKeepBelow))
            return;

        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return;
        currentMonitor = monitor;
        if (keepBelow != 0) currentKeepBelow = keepBelow;
        if (currentKeepBelow != 0 && !IsWindow(currentKeepBelow)) currentKeepBelow = 0;

        var rc = info.Monitor;
        int widthPx = Math.Max(1, rc.Right - rc.Left);
        int heightPx = Math.Max(1, rc.Bottom - rc.Top);

        if (PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice is { } fromDevice)
        {
            var topLeft = fromDevice.Transform(new Point(rc.Left, rc.Top));
            var size = fromDevice.Transform(new Vector(widthPx, heightPx));
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = size.X;
            Height = size.Y;
        }

        // Il posizionamento fisico diretto è quello autorevole: WPF da solo sbaglia su monitor con DPI diverso.
        nint after = fromTimer ? 0 : (currentKeepBelow != 0 ? currentKeepBelow : HWND_TOPMOST);
        uint flags = SWP_NOACTIVATE | (fromTimer ? SWP_NOZORDER : 0u) | (_isActive && !fadingOut ? SWP_SHOWWINDOW : 0u);
        SetWindowPos(handle, after, rc.Left, rc.Top, widthPx, heightPx, flags);
    }

    void StartBreathing()
    {
        if (glowLayer.HasAnimatedProperties) return;
        var breathe = new DoubleAnimation(0.35, 0.75, TimeSpan.FromMilliseconds(1500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        glowLayer.BeginAnimation(OpacityProperty, breathe);
    }

    void RunPulse(Color pulse)
    {
        foreach (var (stop, alpha) in tintedStops)
        {
            var from = Color.FromArgb(alpha, Accent.R, Accent.G, Accent.B);
            var to = Color.FromArgb((byte)Math.Min(255, alpha + 25), pulse.R, pulse.G, pulse.B);
            var keys = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(620) };
            keys.KeyFrames.Add(new EasingColorKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            keys.KeyFrames.Add(new DiscreteColorKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
            keys.KeyFrames.Add(new EasingColorKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            stop.BeginAnimation(GradientStop.ColorProperty, keys);
        }
        var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(620) };
        flash.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))));
        flash.KeyFrames.Add(new EasingDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        flash.Completed += (_, _) =>
        {
            glowLayer.BeginAnimation(OpacityProperty, null);
            if (!fadingOut) StartBreathing();
        };
        glowLayer.BeginAnimation(OpacityProperty, flash);
    }

    void Dismiss() => FadeOut();

    void FadeOut()
    {
        _isActive = false;
        monitorTimer.Stop();
        if (fadingOut || !IsVisible) return;
        fadingOut = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(FadeOutMilliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            if (!fadingOut) return;
            glowLayer.BeginAnimation(OpacityProperty, null);
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            if (ReferenceEquals(current, this)) current = null;
            Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd, int index, nint newLong);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindow(nint hwnd);
}
