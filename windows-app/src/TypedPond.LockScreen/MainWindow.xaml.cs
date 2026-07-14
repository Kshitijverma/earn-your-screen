using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TypedPond.LockScreen;

/// <summary>
/// The full-screen lock window. It draws a circular progress ring toward the
/// step goal, shows the live step count, a motivational message, and a clock.
///
/// While shown it:
///   - installs a system-wide keyboard hook that swallows common escape
///     shortcuts (Alt+Tab, Alt+F4, Win, Ctrl+Esc, Ctrl+Shift+Esc);
///   - covers every non-primary monitor with a plain black window so the
///     desktop is fully hidden;
///   - refuses to close except via <see cref="HideLock"/> (driven by the
///     UNLOCK pipe command).
/// </summary>
public partial class MainWindow : Window
{
    // Ring geometry (must match the Ellipse/Path sizing in XAML).
    private const double RingDiameter = 340.0;
    private const double RingStrokeThickness = 20.0;

    private readonly DispatcherTimer _clockTimer;
    private readonly List<Window> _coverWindows = new();

    private KeyboardHook? _keyboardHook;
    private bool _allowClose;
    private int _lastSteps;
    private int _lastGoal = 10_000;

    public MainWindow()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _clockTimer.Tick += (_, _) => UpdateClock();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateClock();
        _clockTimer.Start();
        RedrawProgressArc();
    }

    /// <summary>Shows the lock: covers all screens and blocks escape keys.</summary>
    public void ShowLock()
    {
        Show();
        WindowState = WindowState.Maximized;
        Topmost = true;
        Activate();

        InstallKeyboardHook();
        CoverSecondaryMonitors();
        RedrawProgressArc();
    }

    /// <summary>Hides the lock: removes covers and unhooks the keyboard.</summary>
    public void HideLock()
    {
        RemoveKeyboardHook();
        RemoveMonitorCovers();
        Hide();
    }

    /// <summary>
    /// Permits the window to close for genuine application shutdown. Until this
    /// is called, <see cref="OnClosing"/> cancels every close attempt so the
    /// lock cannot be dismissed by closing the window.
    /// </summary>
    public void AllowClose()
    {
        _allowClose = true;
    }

    /// <summary>
    /// Updates the ring and the "N / goal steps" text. Safe to call from any
    /// thread.
    /// </summary>
    public void UpdateProgress(int steps, int goal)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateProgress(steps, goal));
            return;
        }

        _lastSteps = Math.Max(0, steps);
        _lastGoal = goal > 0 ? goal : 1;

        double fraction = Math.Clamp((double)_lastSteps / _lastGoal, 0.0, 1.0);
        int percent = (int)Math.Round(fraction * 100);

        PercentText.Text = $"{percent}%";
        StepsText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:N0} / {1:N0} steps",
            _lastSteps,
            _lastGoal);

        RedrawProgressArc();
    }

    /// <summary>Sets the motivational message. Safe to call from any thread.</summary>
    public void SetMotivationalMessage(string msg)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetMotivationalMessage(msg));
            return;
        }

        MessageText.Text = msg ?? string.Empty;
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Rebuilds the arc <see cref="Path"/> geometry to reflect the current
    /// progress fraction, sweeping clockwise from the 12 o'clock position.
    /// </summary>
    private void RedrawProgressArc()
    {
        if (ProgressArc is null)
        {
            return;
        }

        double fraction = Math.Clamp((double)_lastSteps / (_lastGoal > 0 ? _lastGoal : 1), 0.0, 1.0);

        if (fraction <= 0.0)
        {
            ProgressArc.Data = null;
            return;
        }

        double radius = (RingDiameter - RingStrokeThickness) / 2.0;
        double center = RingDiameter / 2.0;

        // A single ArcSegment cannot represent a full 360-degree sweep
        // (start point would equal the end point), so cap just short of it.
        double sweepDegrees = Math.Min(fraction * 360.0, 359.999);

        Point start = PointOnCircle(center, radius, 0.0);
        Point end = PointOnCircle(center, radius, sweepDegrees);

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDegrees > 180.0,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        ProgressArc.Data = geometry;
    }

    /// <summary>
    /// Returns the point on the ring at <paramref name="angleDegrees"/> measured
    /// clockwise from the top (12 o'clock).
    /// </summary>
    private static Point PointOnCircle(double center, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        double x = center + radius * Math.Sin(radians);
        double y = center - radius * Math.Cos(radians);
        return new Point(x, y);
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook is not null)
        {
            return;
        }

        _keyboardHook = new KeyboardHook();
        _keyboardHook.Install();
    }

    private void RemoveKeyboardHook()
    {
        _keyboardHook?.Dispose();
        _keyboardHook = null;
    }

    /// <summary>
    /// Creates a plain black, borderless, topmost window over every monitor
    /// except the primary one (which shows this UI).
    /// </summary>
    private void CoverSecondaryMonitors()
    {
        RemoveMonitorCovers();

        Matrix deviceToLogical = GetDeviceToLogicalTransform();

        foreach (MonitorBounds monitor in EnumerateMonitors())
        {
            if (monitor.IsPrimary)
            {
                continue;
            }

            // Convert device-pixel bounds to WPF DIPs.
            Point topLeft = deviceToLogical.Transform(new Point(monitor.Left, monitor.Top));
            Point bottomRight = deviceToLogical.Transform(new Point(monitor.Right, monitor.Bottom));

            var cover = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                AllowsTransparency = false,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = topLeft.X,
                Top = topLeft.Y,
                Width = Math.Max(1, bottomRight.X - topLeft.X),
                Height = Math.Max(1, bottomRight.Y - topLeft.Y),
                Title = "TypedPond",
            };

            // Cover windows also refuse to close except during teardown.
            cover.Closing += CoverClosing;
            cover.Show();
            _coverWindows.Add(cover);
        }
    }

    private void CoverClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
        }
    }

    private void RemoveMonitorCovers()
    {
        foreach (Window cover in _coverWindows)
        {
            cover.Closing -= CoverClosing;
            cover.Close();
        }

        _coverWindows.Clear();
    }

    private Matrix GetDeviceToLogicalTransform()
    {
        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            return source.CompositionTarget.TransformFromDevice;
        }

        // Fall back to identity (assumes 96 DPI) if the source is not ready.
        return Matrix.Identity;
    }

    /// <summary>
    /// Prevents the window from closing. The lock is only removed via
    /// <see cref="HideLock"/>; genuine app shutdown calls <see cref="AllowClose"/>
    /// first (from App on SHUTDOWN / OS session end).
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        _clockTimer.Stop();
        RemoveKeyboardHook();
        RemoveMonitorCovers();
        base.OnClosing(e);
    }

    // ---- Monitor enumeration via Win32 ------------------------------------

    private readonly record struct MonitorBounds(
        double Left, double Top, double Right, double Bottom, bool IsPrimary);

    private static IEnumerable<MonitorBounds> EnumerateMonitors()
    {
        var results = new List<MonitorBounds>();

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                bool isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
                results.Add(new MonitorBounds(
                    info.rcMonitor.left,
                    info.rcMonitor.top,
                    info.rcMonitor.right,
                    info.rcMonitor.bottom,
                    isPrimary));
            }

            return true; // continue enumeration
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return results;
    }

    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
