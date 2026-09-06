using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using OnePointUI.Avalonia.Styling.Controls.OnePointControls.Dialog;
using OnePointUI.Avalonia.Styling.Controls.OnePointControls.Notice.Info;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.WindowFrame;

public partial class OnePointWindow : Window
{
    public static readonly StyledProperty<bool> IsMainWindowProperty =
        AvaloniaProperty.Register<OnePointWindow, bool>(nameof(IsMainWindow));

    public static readonly StyledProperty<object?> MainContentProperty =
        AvaloniaProperty.Register<OnePointWindow, object?>(nameof(MainContent));

    public static readonly StyledProperty<object?> TitleBarContentProperty =
        AvaloniaProperty.Register<OnePointWindow, object?>(nameof(TitleBarContent));

    public static readonly StyledProperty<object?> TitleBarContentContentProperty =
        AvaloniaProperty.Register<OnePointWindow, object?>(nameof(TitleBarContentContent));

    public static readonly StyledProperty<object?> TitleBarBackgroundContentProperty =
        AvaloniaProperty.Register<OnePointWindow, object?>(nameof(TitleBarBackgroundContent));

    public static readonly StyledProperty<object?> OverlayContentProperty =
        AvaloniaProperty.Register<OnePointWindow, object?>(nameof(OverlayContent));

    public static readonly StyledProperty<IBrush?> TitleBarBrushProperty =
        AvaloniaProperty.Register<OnePointWindow, IBrush?>(nameof(TitleBarBrush));

    public static readonly StyledProperty<bool> IsMaxBtnProperty =
        AvaloniaProperty.Register<OnePointWindow, bool>(nameof(IsMaxBtn), true);

    public static readonly StyledProperty<bool> IsMinBtnProperty =
        AvaloniaProperty.Register<OnePointWindow, bool>(nameof(IsMinBtn), true);

    public static readonly StyledProperty<bool> IsTitleVisibleProperty =
        AvaloniaProperty.Register<OnePointWindow, bool>(nameof(IsTitleVisible), true);

    public static readonly StyledProperty<double> TitleBarHeightProperty =
        AvaloniaProperty.Register<OnePointWindow, double>(nameof(TitleBarHeight), 42);

    public static readonly StyledProperty<Thickness> TitleBarMarginProperty =
        AvaloniaProperty.Register<OnePointWindow, Thickness>(nameof(TitleBarMargin));

    public static readonly StyledProperty<Thickness> TitleBarLeftMarginProperty =
        AvaloniaProperty.Register<OnePointWindow, Thickness>(nameof(TitleBarLeftMargin), new Thickness(15, 0, 0, 0));

    public static readonly StyledProperty<Thickness> MainContentMarginProperty =
        AvaloniaProperty.Register<OnePointWindow, Thickness>(nameof(MainContentMargin), new Thickness(0, 42, 0, 0));

    public static readonly StyledProperty<Thickness> TitleBarContentMarginProperty =
        AvaloniaProperty.Register<OnePointWindow, Thickness>(nameof(TitleBarContentMargin), new Thickness(8, 0, 0, 0));

    public static readonly StyledProperty<double> WindowControlButtonSizeProperty =
        AvaloniaProperty.Register<OnePointWindow, double>(nameof(WindowControlButtonSize), 22);

    public static readonly StyledProperty<double> WindowControlIconSizeProperty =
        AvaloniaProperty.Register<OnePointWindow, double>(nameof(WindowControlIconSize), 8);

    public static readonly StyledProperty<double> WindowControlSpacingProperty =
        AvaloniaProperty.Register<OnePointWindow, double>(nameof(WindowControlSpacing), 5);

    public static readonly StyledProperty<Thickness> WindowControlsMarginProperty =
        AvaloniaProperty.Register<OnePointWindow, Thickness>(nameof(WindowControlsMargin), new Thickness(0, 0, 15, 0));

    public static readonly StyledProperty<double> TitleBarBackgroundOpacityProperty =
        AvaloniaProperty.Register<OnePointWindow, double>(nameof(TitleBarBackgroundOpacity), 0.85);

    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<OnePointWindow, double>(nameof(TitleFontSize), 16);

    public static readonly StyledProperty<FontWeight> TitleFontWeightProperty =
        AvaloniaProperty.Register<OnePointWindow, FontWeight>(nameof(TitleFontWeight), FontWeight.Normal);

    public static readonly StyledProperty<IBrush?> WindowFrameBorderBrushProperty =
        AvaloniaProperty.Register<OnePointWindow, IBrush?>(nameof(WindowFrameBorderBrush), Brushes.Transparent);

    public static readonly StyledProperty<Thickness> WindowFrameBorderThicknessProperty =
        AvaloniaProperty.Register<OnePointWindow, Thickness>(nameof(WindowFrameBorderThickness));

    public static readonly StyledProperty<CornerRadius> WindowFrameCornerRadiusProperty =
        AvaloniaProperty.Register<OnePointWindow, CornerRadius>(nameof(WindowFrameCornerRadius));

    private readonly Timer _stateTimer;
    private int _isClosed;

    public int DrawMarginLR = 10;

    public OnePointWindow()
    {
        InitializeComponent();

        Frame.NavigateTo("");
        _stateTimer = new Timer(_ =>
        {
            if (Volatile.Read(ref _isClosed) != 0)
            {
                return;
            }

            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (Volatile.Read(ref _isClosed) != 0)
                    {
                        return;
                    }

                if (OperatingSystem.IsWindows())
                {
                    if (WindowState == WindowState.Maximized) Padding = new Thickness(8);
                    else Padding = new Thickness(0);
                }

                if (WindowState == WindowState.Maximized) MaxBtnIcon.Glyph = "\uE923";
                else MaxBtnIcon.Glyph = "\uE922";
                });
            }
            catch (InvalidOperationException) when (Volatile.Read(ref _isClosed) != 0)
            {
                // The dispatcher can shut down concurrently with window closure.
            }
        });
        Closed += OnePointWindow_OnClosed;
        _stateTimer.Change(TimeSpan.FromMilliseconds(0), TimeSpan.FromMilliseconds(100));
        BottomBorder.Margin = new Thickness(DrawMarginLR, 0, DrawMarginLR, 0);
    }

    public bool IsMainWindow { get => GetValue(IsMainWindowProperty); set => SetValue(IsMainWindowProperty, value); }
    public object? MainContent { get => GetValue(MainContentProperty); set => SetValue(MainContentProperty, value); }
    public object? TitleBarContent { get => GetValue(TitleBarContentProperty); set => SetValue(TitleBarContentProperty, value); }
    public object? TitleBarContentContent { get => GetValue(TitleBarContentContentProperty); set => SetValue(TitleBarContentContentProperty, value); }
    public object? TitleBarBackgroundContent { get => GetValue(TitleBarBackgroundContentProperty); set => SetValue(TitleBarBackgroundContentProperty, value); }
    public object? OverlayContent { get => GetValue(OverlayContentProperty); set => SetValue(OverlayContentProperty, value); }
    public IBrush? TitleBarBrush { get => GetValue(TitleBarBrushProperty); set => SetValue(TitleBarBrushProperty, value); }
    public bool IsMaxBtn { get => GetValue(IsMaxBtnProperty); set => SetValue(IsMaxBtnProperty, value); }
    public bool IsMinBtn { get => GetValue(IsMinBtnProperty); set => SetValue(IsMinBtnProperty, value); }
    public bool IsTitleVisible { get => GetValue(IsTitleVisibleProperty); set => SetValue(IsTitleVisibleProperty, value); }
    public double TitleBarHeight { get => GetValue(TitleBarHeightProperty); set => SetValue(TitleBarHeightProperty, value); }
    public Thickness TitleBarMargin { get => GetValue(TitleBarMarginProperty); set => SetValue(TitleBarMarginProperty, value); }
    public Thickness TitleBarLeftMargin { get => GetValue(TitleBarLeftMarginProperty); set => SetValue(TitleBarLeftMarginProperty, value); }
    public Thickness MainContentMargin { get => GetValue(MainContentMarginProperty); set => SetValue(MainContentMarginProperty, value); }
    public Thickness TitleBarContentMargin { get => GetValue(TitleBarContentMarginProperty); set => SetValue(TitleBarContentMarginProperty, value); }
    public double WindowControlButtonSize { get => GetValue(WindowControlButtonSizeProperty); set => SetValue(WindowControlButtonSizeProperty, value); }
    public double WindowControlIconSize { get => GetValue(WindowControlIconSizeProperty); set => SetValue(WindowControlIconSizeProperty, value); }
    public double WindowControlSpacing { get => GetValue(WindowControlSpacingProperty); set => SetValue(WindowControlSpacingProperty, value); }
    public Thickness WindowControlsMargin { get => GetValue(WindowControlsMarginProperty); set => SetValue(WindowControlsMarginProperty, value); }
    public double TitleBarBackgroundOpacity { get => GetValue(TitleBarBackgroundOpacityProperty); set => SetValue(TitleBarBackgroundOpacityProperty, value); }
    public double TitleFontSize { get => GetValue(TitleFontSizeProperty); set => SetValue(TitleFontSizeProperty, value); }
    public FontWeight TitleFontWeight { get => GetValue(TitleFontWeightProperty); set => SetValue(TitleFontWeightProperty, value); }
    public IBrush? WindowFrameBorderBrush { get => GetValue(WindowFrameBorderBrushProperty); set => SetValue(WindowFrameBorderBrushProperty, value); }
    public Thickness WindowFrameBorderThickness { get => GetValue(WindowFrameBorderThicknessProperty); set => SetValue(WindowFrameBorderThicknessProperty, value); }
    public CornerRadius WindowFrameCornerRadius { get => GetValue(WindowFrameCornerRadiusProperty); set => SetValue(WindowFrameCornerRadiusProperty, value); }

    public NoticePanel Notice => NoticePanel;

    private void OnePointWindow_OnClosed(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
        {
            return;
        }

        Closed -= OnePointWindow_OnClosed;
        _stateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _stateTimer.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsMainWindowProperty && IsMainWindow)
        {
            DialogHost.SetHost(DialogHost);
        }
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void MinBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaxBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();

        if (IsMainWindow) Environment.Exit(0);
    }

    public void CloseDraw()
    {
        SetBorderState(false);
    }

    public async void OpenDraw(object? page, string title)
    {
        BorderTitle.Text = title;
        await SetBorderState(true);

        Frame.NavigateTo(page);
    }

    private async Task SetBorderState(bool state)
    {
        if (state)
        {
            BottomBorder.Margin = new Thickness(DrawMarginLR, Height, DrawMarginLR, -Height);
            await Task.Delay(100);
            BorderGrid.IsVisible = true;
            BottomBorder.Margin = new Thickness(DrawMarginLR, 100, DrawMarginLR, 0);
            BorderBackground.Opacity = 0.3;
            await Task.Delay(200);
        }
        else
        {
            BottomBorder.Margin = new Thickness(DrawMarginLR, Height, DrawMarginLR, -Height);
            BorderBackground.Opacity = 0;
            await Task.Delay(800);
            BorderGrid.IsVisible = false;
            Frame.NavigateTo("");
        }
    }

    private void CloseBorderBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        SetBorderState(false);
    }
}
