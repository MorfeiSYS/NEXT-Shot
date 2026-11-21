using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NEXTShot.Helpers;
using NEXTShot.Native;
using NEXTShot.Services;
using WpfPoint = System.Windows.Point;

namespace NEXTShot.Windows;

public partial class VideoControlPanelWindow : Window
{
    private bool _sessionStarted;
    private bool _paused;
    private bool _useIcons;
    private WpfPoint _dragStart;
    private bool _dragging;
    private Storyboard? _pauseBlinkStoryboard;
    private readonly DispatcherTimer _durationTimer;

    public event Action? StartRequested;
    public event Action? PauseToggleRequested;
    public event Action? StopRequested;
    public event Action? Cancelled;
    public Func<TimeSpan>? GetRecordedDuration { get; set; }

    public VideoControlPanelWindow()
    {
        InitializeComponent();
        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _durationTimer.Tick += (_, _) => RefreshDuration();
        Loaded += OnLoaded;
        ContentRendered += (_, _) => ReapplyCaptureExclusion();
        SourceInitialized += (_, _) => ReapplyCaptureExclusion();
        IsVisibleChanged += (_, _) => { if (IsVisible) ReapplyCaptureExclusion(); };
        MouseLeftButtonDown += OnDragMouseDown;
        MouseLeftButtonUp += OnDragMouseUp;
        MouseMove += OnDragMouseMove;
        BrushContrast.ApplyButtonForeground(BtnStart);
        BrushContrast.ApplyButtonForeground(BtnStop);
    }

    public void ApplySettings(AppSettings settings)
    {
        _useIcons = settings.VideoControlPanelUseIcons;
        BtnStart.MinWidth = _useIcons ? 40 : 72;
        BtnStop.MinWidth = _useIcons ? 40 : 72;
        RefreshButtons();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterAtScreenBottom();
        ReapplyCaptureExclusion();
    }

    public void ReapplyCaptureExclusion()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
            helper.EnsureHandle();
        ExcludeFromScreenCapture();
    }

    private void CenterAtScreenBottom()
    {
        UpdateLayout();
        var w = ActualWidth > 10 ? ActualWidth : 220;
        var h = ActualHeight > 10 ? ActualHeight : 48;
        var vsLeft = SystemParameters.VirtualScreenLeft;
        var vsTop = SystemParameters.VirtualScreenTop;
        var vsW = SystemParameters.VirtualScreenWidth;
        var vsH = SystemParameters.VirtualScreenHeight;
        Left = vsLeft + (vsW - w) / 2;
        Top = vsTop + vsH - h - 24;
    }

    /// <summary>Requires Windows 10 version 1903+ for capture exclusion.</summary>
    private void ExcludeFromScreenCapture()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludeFromCapture);
    }

    private void OnDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        _dragging = true;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnDragMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        Left += p.X - _dragStart.X;
        Top += p.Y - _dragStart.Y;
        ReapplyCaptureExclusion();
    }

    private void OnDragMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    public void SetSessionState(bool sessionActive, bool recording, bool paused)
    {
        _sessionStarted = sessionActive;
        _paused = paused;
        BtnStart.IsEnabled = !sessionActive || recording || paused;
        BtnStop.IsEnabled = sessionActive;

        if (sessionActive)
        {
            TxtDuration.Visibility = Visibility.Visible;
            RecDot.Fill = new SolidColorBrush(
                paused ? Color.FromRgb(0x88, 0x88, 0x88) :
                recording ? Color.FromRgb(0xE7, 0x4C, 0x3C) : Color.FromRgb(0xE7, 0x4C, 0x3C));
            if (recording && !paused)
                _durationTimer.Start();
            else if (paused)
                _durationTimer.Stop();
            else
                _durationTimer.Start();
        }
        else
        {
            TxtDuration.Visibility = Visibility.Collapsed;
            TxtDuration.Text = "00:00";
            _durationTimer.Stop();
            RecDot.Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        }

        RefreshButtons();
        if (paused)
            StartPauseBlink();
        else
            StopPauseBlink();
        RefreshDuration();
        if (sessionActive)
            ReapplyCaptureExclusion();
    }

    private void RefreshButtons()
    {
        var sessionActive = _sessionStarted && BtnStop.IsEnabled;
        VideoControlContent.ApplyStartButton(BtnStart, _useIcons, sessionActive, _paused);
        VideoControlContent.ApplyStopButton(BtnStop, _useIcons, BtnStop.IsEnabled);
    }

    private void RefreshDuration()
    {
        if (TxtDuration.Visibility != Visibility.Visible || GetRecordedDuration is null) return;
        TxtDuration.Text = VideoControlContent.FormatDuration(GetRecordedDuration());
    }

    private void Start_OnClick(object sender, RoutedEventArgs e)
    {
        if (_sessionStarted && BtnStop.IsEnabled)
            PauseToggleRequested?.Invoke();
        else
            StartRequested?.Invoke();
    }

    private void Stop_OnClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

    private void StartPauseBlink()
    {
        StopPauseBlink();
        _pauseBlinkStoryboard = PauseButtonBlink.Begin(BtnStart);
    }

    private void StopPauseBlink()
    {
        PauseButtonBlink.Stop(_pauseBlinkStoryboard, BtnStart, clearBackground: false);
        _pauseBlinkStoryboard = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _durationTimer.Stop();
        StopPauseBlink();
        if (!_sessionStarted)
            Cancelled?.Invoke();
        base.OnClosed(e);
    }
}