using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Behaviors;

/// <summary>
/// 右键菜单（ContextMenu）的视觉与交互增强：开/关非线性缓动动画、
/// 悬停高亮在菜单项之间平滑滑动、全部选项都没有图标时收起图标列。
/// </summary>
/// <remarks>
/// <para><b>动画为什么由行为驱动类而不是伪类。</b>ContextMenu 没有 <c>:open</c>
/// 伪类，而模板在菜单实例存活期间只应用一次——靠"模板挂载即播放"的自动动画
/// 只有第一次打开有效。行为在每次 <c>Opened</c> 时给根 Border 挂 <c>opening</c>
/// 类、动画播完摘掉，保证每次展开都重播；关闭方向同理，见下。</para>
/// <para><b>关闭动画为什么要接管 Popup.Closing。</b>Popup 的关闭是一次性直落的，
/// 没有留出播放退场动画的时机。行为订阅宿主 Popup 的 <c>Closing</c>（通过
/// <c>PopupRoot.Host</c> 反射取得，与 <see cref="PopupDismissBehavior"/> 同一路径），
/// 第一次触发时取消关闭、挂 <c>closing</c> 类播放反向关键帧，播完再真正关闭；
/// 框架或调用方主动取消关闭（如消费方在 ContextMenu.Closing 里否决）时
/// <c>CancelEventArgs.Cancel</c> 已为真，行为不插手。找不到 Popup 的宿主环境
/// 静默跳过，菜单照常立即关闭。</para>
        /// <para><b>悬停高亮为什么不用 MenuItem 的 :pointerover 背景。</b>那是每一项独立的
        /// 底色，没有"在选项之间移动"的连贯感。模板里放一个共享的 <c>PART_Highlight</c>
        /// （选中底色）+ 其左缘的 <c>PART_Indicator</c> 指示条，样式与侧边栏导航项的
        /// 选中态一致：指示条为 3px 纵向渐变，出现时从 scale(0,1) 生长到 scale(1,1)
        /// （"动态进度条"，0.18s QuarticEaseOut）。行为按悬停项相对项区的几何驱动
        /// 高亮滑动（160ms CubicEaseOut），位置用 <c>TranslateTransform.Y</c>、
        /// 高度用 <c>Height</c>，两者的 <c>DoubleTransition</c> 在样式里声明；
        /// 指示条用 <c>TransformOperationsTransition</c> 做生长动画。键盘导航
        /// （方向键改选）同样驱动高亮。</para>
/// </remarks>
public static class MenuEffects
{
    /// <summary>正在播放关闭动画时挂在根 Border 上的类。</summary>
    public const string ClosingClass = "closing";

    /// <summary>正在播放展开动画时挂在根 Border 上的类。</summary>
    public const string OpeningClass = "opening";

    /// <summary>菜单里所有选项都没有图标时挂在菜单上的类（收起图标列）。</summary>
    public const string NoIconsClass = "no-icons";

    /// <summary>关闭动画时长，与 ContextMenu.axaml 里 <c>closing</c> 关键帧一致。</summary>
    public static readonly TimeSpan CloseAnimationDuration = TimeSpan.FromMilliseconds(130);

    /// <summary>展开动画时长，与 ContextMenu.axaml 里 <c>opening</c> 关键帧一致。</summary>
    public static readonly TimeSpan OpenAnimationDuration = TimeSpan.FromMilliseconds(180);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ContextMenu, bool>("IsEnabled", typeof(MenuEffects));

    /// <summary>
    /// Popup.Closing 是 internal 事件：EventInfo.AddEventHandler 只认公开访问器，
    /// 这里直接拿 <c>add_Closing</c>/<c>remove_Closing</c> 内部方法反射调用。
    /// </summary>
    private static readonly MethodInfo? PopupClosingAdd =
        typeof(Popup).GetMethod("add_Closing", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? PopupClosingRemove =
        typeof(Popup).GetMethod("remove_Closing", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// ContextMenu 的宿主 Popup 存在私有字段 <c>_popup</c> 里。不用 PopupRoot.Host——
    /// Opened 时 Host 可能还没就绪；直接读字段在任何时机都拿得到。
    /// </summary>
    private static readonly FieldInfo? ContextMenuPopupField =
        typeof(ContextMenu).GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly ConditionalWeakTable<ContextMenu, MenuState> States = new();

    static MenuEffects()
    {
        IsEnabledProperty.Changed.AddClassHandler<ContextMenu>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ContextMenu menu) => menu.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ContextMenu menu, bool value) =>
        menu.SetValue(IsEnabledProperty, value);

    /// <summary>兜底外部按下判定；测试直接驱动（不依赖兜底是否真的订阅了顶层窗口）。</summary>
    internal static bool TryHandleOutsidePress(ContextMenu menu, object? source) =>
        States.TryGetValue(menu, out var state) && state.HandleOutsidePress(source);

    /// <summary>关闭动画播完后的真正关闭调度；测试可替换成立即执行或捕获。</summary>
    internal static Action<Action, TimeSpan> ScheduleClose { get; set; } = DefaultScheduleClose;

    internal static void DefaultScheduleClose(Action action, TimeSpan delay) =>
        DispatcherTimer.RunOnce(action, delay);

    private static void OnIsEnabledChanged(ContextMenu menu, AvaloniaPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue!)
        {
            menu.AddHandler(MenuBase.OpenedEvent, OnMenuOpened);
            menu.AddHandler(MenuBase.ClosedEvent, OnMenuClosed);
        }
        else
        {
            menu.RemoveHandler(MenuBase.OpenedEvent, OnMenuOpened);
            menu.RemoveHandler(MenuBase.ClosedEvent, OnMenuClosed);
            if (States.TryGetValue(menu, out var state))
            {
                state.Detach();
                States.Remove(menu);
            }
        }
    }

    private static void OnMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        var state = States.GetOrCreateValue(menu);
        state.Menu = menu;

        // 模板在 Opened 事件时已随 Popup 装进可视树；仍推一拍确保命名件齐备。
        Dispatcher.UIThread.Post(state.Attach);
    }

    private static void OnMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && States.TryGetValue(menu, out var state))
        {
            state.OnClosed();
        }
    }

    private sealed class MenuState
    {
        /// <summary>玻璃层模糊半径（DIP）。与 Windows 11 acrylic 观感接近。</summary>
        private const double BackdropBlurRadius = 30;

        /// <summary>挂上模糊背景后给色调层加的类（切换到更透明的 MenuGlassBackdropBrush）。</summary>
        private const string GlassClass = "glass";

        private readonly List<IDisposable> _popupSubscriptions = [];
        private readonly List<(Popup Popup, EventHandler Handler)> _submenuHooks = [];
        private readonly List<(Border Layer, Border Tint)> _glassLayers = [];
        private Popup? _popup;
        private TopLevel? _dismissRoot;
        private EventHandler<PointerPressedEventArgs>? _dismissHandler;

        /// <summary>宿主窗口（菜单背后的应用窗口），玻璃层从它取快照。</summary>
        private TopLevel? _host;

        /// <summary>宿主窗口的位图快照；同一次打开期间主菜单与子菜单共用。</summary>
        private RenderTargetBitmap? _backdrop;

        /// <summary>退场动画已安排播放：之后的关闭请求直接放行，不再重复取消。</summary>
        private bool _exitAnimationScheduled;

        public ContextMenu? Menu { get; set; }
        public Border? Root { get; private set; }
        public Border? Highlight { get; private set; }
        public Border? Indicator { get; private set; }

        /// <summary>附加模板部件、应用无图标模式并接管关闭流程。菜单每次打开时调用。</summary>
        public void Attach()
        {
            if (Menu is not { } menu)
            {
                return;
            }

            Root = menu.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Name == "PART_MenuRoot");
            Highlight = menu.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Name == "PART_Highlight");
            Indicator = Highlight?.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Name == "PART_Indicator");

            // 无图标模式：整个菜单没有任何带图标的选项时收起图标列，
            // 避免一整列空白；混合菜单保持固定图标列宽，文字自然对齐。
            // 类挂在菜单上，与 ContextMenu.axaml 的 ContextMenu.no-icons 选择器对应。
            var hasIcon = menu.Items.OfType<MenuItem>().Any(item => item.Icon is not null);
            ((IPseudoClasses)menu.Classes).Set(NoIconsClass, !hasIcon);

            HookPopup(menu);
            AttachGlassBackdrop(menu);

            // Height 初始是 NaN，DoubleTransition 从 NaN 插值会全程产出 NaN
            // （第一次悬停高亮不可见），先归零；位置走 TranslateTransform.Y，无需初始化。
            if (Highlight is { } highlight)
            {
                highlight.Height = 0;
            }

            HookOutsideDismiss(menu);
            HookHighlight(menu);
            PlayOpenAnimation();
        }

        /// <summary>菜单关闭：还原视觉状态并解除对 Popup 的接管。</summary>
        public void OnClosed()
        {
            _exitAnimationScheduled = false;
            foreach (var subscription in _popupSubscriptions)
            {
                subscription.Dispose();
            }

            _popupSubscriptions.Clear();
            _popup = null;

            if (Root is { } root)
            {
                root.Classes.Remove(OpeningClass);
                root.Classes.Remove(ClosingClass);
            }

            if (_dismissRoot is not null && _dismissHandler is not null)
            {
                _dismissRoot.RemoveHandler(InputElement.PointerPressedEvent, _dismissHandler);
            }

            _dismissRoot = null;
            _dismissHandler = null;

            foreach (var (popup, handler) in _submenuHooks)
            {
                popup.Opened -= handler;
            }

            _submenuHooks.Clear();
            ReleaseBackdrop();

            if (Menu is { } menu)
            {
                menu.PointerEntered -= OnMenuPointerEntered;
                menu.PointerExited -= OnMenuPointerExited;
                menu.SelectionChanged -= OnMenuSelectionChanged;
                ((IPseudoClasses)menu.Classes).Set(NoIconsClass, false);
            }

            if (Highlight is { } highlight)
            {
                highlight.Opacity = 0;
            }

            Root = null;
            Highlight = null;
        }

        /// <summary>行为整体停用（附加属性改回 false）时调用。</summary>
        public void Detach() => OnClosed();

        /// <summary>
        /// 取得宿主 Popup 并接管 Closing——先取消、播动画、再真正关闭。
        /// </summary>
        private void HookPopup(ContextMenu menu)
        {
            if (PopupClosingAdd is null
                || PopupClosingRemove is null
                || ContextMenuPopupField?.GetValue(menu) is not Popup popup)
            {
                return;
            }

            _popup = popup;
            var closingHandler = new EventHandler<CancelEventArgs>(OnPopupClosing);
            PopupClosingAdd.Invoke(popup, [closingHandler]);
            popup.Closed += OnPopupClosed;
            _popupSubscriptions.Add(new DisposeAction(() =>
            {
                PopupClosingRemove.Invoke(popup, [closingHandler]);
                popup.Closed -= OnPopupClosed;
            }));
        }

        /// <summary>
        /// 毛玻璃背景：给宿主窗口拍一张位图快照，按菜单所在区域裁剪、挂
        /// <see cref="BlurEffect"/>，垫在色调层下面。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么不用 DWM。</b>弹层是独立顶层窗口，<c>AcrylicBlur</c>
        /// 在 <c>PopupImpl</c> 上走 host-backdrop 路径，而弹窗没有扩展 DWM 边框
        /// 区域——真机拿到的是一块<b>不透明灰板</b>（把色调层 alpha 设成 0 仍是
        /// 灰板，逐像素与带色调时一致），色调层叠在灰板上，菜单永远是不透明
        /// 卡片；<c>ACCENT_ENABLE_BLURBEHIND</c> 在 Win11 22H2+ 也已退化为纯色。
        /// 所以弹层只请求 <c>Transparent</c>（逐像素 alpha，实测透底正常），
        /// 模糊改用 Avalonia 原生 <see cref="BlurEffect"/>。</para>
        /// <para><b>为什么快照是对的。</b>弹层是独立顶层窗口，宿主窗口的可视树
        /// 里不含菜单，快照恰好就是"菜单背后的内容"；菜单存活时间短、背后内容
        /// 基本静止，冻结一帧与实时模糊在观感上没有区别。</para>
        /// <para><b>失败回退。</b>无头测试、窗口尺寸为零、菜单完全在窗口外时
        /// 捕获失败：玻璃层保持隐藏、不挂 <c>glass</c> 类，色调层回落到不透明的
        /// <c>MenuBackdropBrush</c>，菜单仍是可用的实心卡片。</para>
        /// </remarks>
        private void AttachGlassBackdrop(ContextMenu menu)
        {
            var host = TopLevel.GetTopLevel(menu.PlacementTarget ?? menu);
            if (host is null || Root?.Child is not Panel panel)
            {
                return;
            }

            var layer = panel.Children.OfType<Border>()
                .FirstOrDefault(border => border.Name == "PART_BackdropBlur");
            var tint = panel.Children.OfType<Border>()
                .FirstOrDefault(border => border.Name == "PART_Backdrop");
            if (layer is null || tint is null || !TryCaptureBackdrop(host))
            {
                return;
            }

            _host = host;
            ApplyGlass(Root, layer, tint);
            HookSubmenus(menu);
        }

        /// <summary>把宿主窗口渲染成快照；同一次打开期间主菜单与子菜单共用。</summary>
        private bool TryCaptureBackdrop(TopLevel host)
        {
            if (_backdrop is not null)
            {
                return true;
            }

            RenderTargetBitmap? bitmap = null;
            try
            {
                var scaling = host.RenderScaling;
                var width = host.Bounds.Width;
                var height = host.Bounds.Height;
                if (scaling <= 0 || width < 1 || height < 1)
                {
                    return false;
                }

                // 快照只喂给 BlurEffect：长边压到不超过 1024 像素再渲染。
                // 低分辨率 + 放大显示本身带一层平滑，叠加 BlurEffect 后能明显
                // 加强"磨砂"观感（Pulsar 背景色差小，常规半径几乎看不出）；
                // 同时把 4K / 高 DPI 窗口的截图开销和位图内存压到四分之一以下。
                var scale = Math.Min(scaling, 1024 / Math.Max(width, height));
                if (scale <= 0)
                {
                    return false;
                }

                bitmap = new RenderTargetBitmap(
                    new PixelSize(
                        Math.Max(1, (int)Math.Ceiling(width * scale)),
                        Math.Max(1, (int)Math.Ceiling(height * scale))),
                    new Vector(96 * scale, 96 * scale));
                bitmap.Render(host);
                _backdrop = bitmap;
                return true;
            }
            catch (Exception)
            {
                // 无头平台没有渲染接口，或宿主尚未完成首帧：回退实心卡片。
                bitmap?.Dispose();
                return false;
            }
        }

        /// <summary>
        /// 用共享快照铺设一层玻璃：裁剪区域按弹层在宿主窗口里的位置计算，
        /// 向外扩一个模糊半径，让 <see cref="BlurEffect"/> 在菜单边缘有真实
        /// 像素可采样（软边落在菜单外，被根 Border 裁掉）。
        /// </summary>
        private void ApplyGlass(Control anchor, Border layer, Border tint)
        {
            if (_host is not { } host
                || _backdrop is not { } backdrop
                || !TryGetHostRect(anchor, host, out var rect))
            {
                return;
            }

            var hostRect = new Rect(0, 0, host.Bounds.Width, host.Bounds.Height);
            var expanded = rect.Inflate(BackdropBlurRadius).Intersect(hostRect);
            if (expanded.Width < 1 || expanded.Height < 1)
            {
                return;
            }

            layer.HorizontalAlignment = HorizontalAlignment.Left;
            layer.VerticalAlignment = VerticalAlignment.Top;
            layer.Margin = new Thickness(expanded.X - rect.X, expanded.Y - rect.Y, 0, 0);
            layer.Width = expanded.Width;
            layer.Height = expanded.Height;
            layer.Background = new ImageBrush(backdrop)
            {
                Stretch = Stretch.Fill,
                SourceRect = new RelativeRect(
                    expanded.X / hostRect.Width,
                    expanded.Y / hostRect.Height,
                    expanded.Width / hostRect.Width,
                    expanded.Height / hostRect.Height,
                    RelativeUnit.Relative),
            };
            layer.Effect = new BlurEffect { Radius = BackdropBlurRadius };
            layer.IsVisible = true;

            tint.Classes.Add(GlassClass);
            if (!_glassLayers.Any(entry => ReferenceEquals(entry.Layer, layer)))
            {
                _glassLayers.Add((layer, tint));
            }
        }

        /// <summary>弹层在宿主窗口客户区里的位置（DIP）：两者都是独立顶层窗口，用屏幕坐标换算。</summary>
        private static bool TryGetHostRect(Control anchor, TopLevel host, out Rect rect)
        {
            rect = default;
            var width = anchor.Bounds.Width;
            var height = anchor.Bounds.Height;
            var hostWidth = host.Bounds.Width;
            var hostHeight = host.Bounds.Height;
            var scaling = host.RenderScaling;
            if (width < 1 || height < 1 || hostWidth < 1 || hostHeight < 1 || scaling <= 0)
            {
                return false;
            }

            var anchorScreen = anchor.PointToScreen(new Point(0, 0));
            var hostScreen = host.PointToScreen(new Point(0, 0));
            rect = new Rect(
                (anchorScreen.X - hostScreen.X) / scaling,
                (anchorScreen.Y - hostScreen.Y) / scaling,
                width,
                height);
            return true;
        }

        /// <summary>
        /// 子菜单是 MenuItem 模板里的独立 Popup，宿主同样是独立顶层窗口。
        /// 订阅已实例化 Popup 的 Opened，复用同一张宿主快照铺玻璃层——不重复
        /// 截图，嵌套菜单不会逐层变慢。
        /// </summary>
        private void HookSubmenus(ContextMenu menu)
        {
            foreach (var popup in menu.GetVisualDescendants().OfType<Popup>())
            {
                if (_submenuHooks.Any(hook => ReferenceEquals(hook.Popup, popup)))
                {
                    continue;
                }

                var handler = new EventHandler((_, _) => ApplySubmenuGlass(popup));
                popup.Opened += handler;
                _submenuHooks.Add((popup, handler));
            }
        }

        private void ApplySubmenuGlass(Popup popup)
        {
            if (popup.Child is not Border border)
            {
                return;
            }

            var layers = border.GetVisualDescendants().OfType<Border>().ToList();
            var layer = layers.FirstOrDefault(candidate => candidate.Name == "PART_SubMenuBackdropBlur");
            var tint = layers.FirstOrDefault(candidate => candidate.Name == "PART_SubMenuTint");
            if (layer is null || tint is null)
            {
                return;
            }

            ApplyGlass(border, layer, tint);
        }

        /// <summary>
        /// 收尾：先摘掉画刷/效果引用再释放位图，避免模板复用时下一帧访问已释放的位图。
        /// </summary>
        private void ReleaseBackdrop()
        {
            foreach (var (layer, tint) in _glassLayers)
            {
                layer.Effect = null;
                layer.Background = null;
                layer.IsVisible = false;
                tint.Classes.Remove(GlassClass);
            }

            _glassLayers.Clear();

            var bitmap = _backdrop;
            _backdrop = null;
            _host = null;
            bitmap?.Dispose();
        }

        /// <summary>
        /// "点击菜单以外任意区域自动关闭"依赖 Avalonia 的光灭覆盖层，而覆盖层
        /// 注册在窗口的 VisualLayerManager 上——OnePointWindow 的手写模板没有
        /// 这一层（模板已补，但第三方宿主窗口仍可能没有）。探测不到光灭层时
        /// 由行为兜底：在顶层窗口隧道阶段监听按下，点击目标不在菜单体系内
        /// （主菜单或任何子菜单弹层）就关闭。
        /// </summary>
        private void HookOutsideDismiss(ContextMenu menu)
        {
            if (_popup is not { } popup
                || popup.PlacementTarget is not { } target
                || FindLightDismissLayer(target) is not null
                || TopLevel.GetTopLevel(target) is not { } topLevel)
            {
                return;
            }

            _dismissHandler = (_, e) => HandleOutsidePress(e.Source);
            _dismissRoot = topLevel;
            topLevel.AddHandler(InputElement.PointerPressedEvent, _dismissHandler, RoutingStrategies.Tunnel);
        }

        /// <summary>光灭覆盖层是 internal 类型，经程序集反射取得；没有则返回 null。</summary>
        private static object? FindLightDismissLayer(Visual visual)
        {
            var layerType = typeof(Popup).Assembly.GetType(
                "Avalonia.Controls.Primitives.LightDismissOverlayLayer");
            var method = layerType?.GetMethod(
                "GetLightDismissOverlayLayer",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return method?.Invoke(null, [visual]);
        }

        /// <summary>
        /// 外部按下判定与关闭（兜底路径，测试直接驱动）。返回是否执行了关闭。
        /// </summary>
        internal bool HandleOutsidePress(object? source)
        {
            if (Menu is not { } menu || !menu.IsOpen)
            {
                return false;
            }

            // 点击目标在菜单体系内（主菜单或任何子菜单弹层，其可视祖先链上都挂着
            // MenuBase 派生实例）不放行；menu.Close 会经 Closing 接管播退场动画。
            if (source is Visual visual && visual.GetSelfAndVisualAncestors().Any(a => a is MenuBase))
            {
                return false;
            }

            menu.Close();
            return true;
        }

        private void OnOutsidePointerPressed(object? sender, PointerPressedEventArgs e) =>
            HandleOutsidePress(e.Source);

        private void OnPopupClosing(object? sender, CancelEventArgs e)
        {
            // Cancel 已经为真说明框架/消费方主动取消了本次关闭（保持打开），不要抢戏；
            // 退场动画已经安排过，则放行本次关闭（动画播完的那次真正的 Close）。
            if (e.Cancel || _exitAnimationScheduled || Root is not { } root || Menu is not { } menu)
            {
                return;
            }

            e.Cancel = true;
            _exitAnimationScheduled = true;
            root.Classes.Add(ClosingClass);

            // 取消后 Popup.IsOpen 已被置回 false（CloseCore 在事件取消时不动它），
            // 此后再走 IsOpen=false 的关闭路径全是 no-op，光灭也会失效——
            // 必须立刻把 IsOpen 翻回 true 保持状态一致（Open 见 _openState 已存在会直接返回）。
            if (_popup is { } popup)
            {
                popup.IsOpen = true;
            }

            ScheduleClose(menu.Close, CloseAnimationDuration);
        }

        private void OnPopupClosed(object? sender, EventArgs e)
        {
            _exitAnimationScheduled = false;
            _popup = null;
        }

        private void HookHighlight(ContextMenu menu)
        {
            menu.PointerEntered += OnMenuPointerEntered;
            menu.PointerExited += OnMenuPointerExited;
            menu.SelectionChanged += OnMenuSelectionChanged;
        }

        private void OnMenuPointerEntered(object? sender, PointerEventArgs e)
        {
            if (TryGetHoveredMenuItem(e.Source as Visual, out var item))
            {
                MoveHighlight(item);
            }
            else
            {
                HideHighlight();
            }
        }

        private void OnMenuPointerExited(object? sender, PointerEventArgs e)
        {
            // 离开的若是菜单项，随后多半是进入相邻项（Entered 会重新驱动高亮），
            // 这里不动；离开的是非菜单项区域（内边距、菜单外）才隐藏。
            if (!TryGetHoveredMenuItem(e.Source as Visual, out _))
            {
                HideHighlight();
            }
        }

        private void OnMenuSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // 键盘导航（方向键）也要让高亮跟着走。
            if (Menu is { } menu
                && menu.SelectedIndex >= 0
                && menu.ContainerFromIndex(menu.SelectedIndex) is MenuItem item)
            {
                MoveHighlight(item);
            }
        }

        private static bool TryGetHoveredMenuItem(Visual? source, out MenuItem item)
        {
            if (source is not null)
            {
                foreach (var ancestor in source.GetSelfAndVisualAncestors())
                {
                    if (ancestor is MenuItem menuItem)
                    {
                        item = menuItem;
                        return true;
                    }
                }
            }

            item = null!;
            return false;
        }

        private void MoveHighlight(MenuItem item)
        {
            if (Highlight is not { } highlight)
            {
                return;
            }

            var host = highlight.Parent as Canvas;
            // host 为 null 时 TranslatePoint 会抛，先挡掉；顺带消掉 consequent 里的
            // `host is not null` 复制判断。
            if (host is null || item.TranslatePoint(default, host) is not { } origin)
            {
                return;
            }

            highlight.Width = host.Bounds.Width;

            if (highlight.RenderTransform is TranslateTransform translate)
            {
                translate.Y = origin.Y;
            }

            highlight.Height = item.Bounds.Height;
            highlight.Opacity = 1;

            // 指示条（侧边栏选中样式同款）：随高亮出现，从左缘生长到位。
            if (Indicator is { } indicator)
            {
                indicator.RenderTransform = TransformOperations.Parse("translate(0px,0px) scale(1,1)");
                indicator.Opacity = 1;
            }
        }

        private void HideHighlight()
        {
            if (Highlight is { } highlight)
            {
                highlight.Opacity = 0;
            }

            if (Indicator is { } indicator)
            {
                indicator.RenderTransform = TransformOperations.Parse("translate(-4px,0px) scale(0,1)");
                indicator.Opacity = 0;
            }
        }

        private void PlayOpenAnimation()
        {
            if (Root is not { } root)
            {
                return;
            }

            // 摘掉再挂上，保证重复打开时动画重播。
            root.Classes.Remove(OpeningClass);
            root.Classes.Add(OpeningClass);
            DispatcherTimer.RunOnce(
                () => root.Classes.Remove(OpeningClass),
                OpenAnimationDuration);
        }
    }

    /// <summary>把事件订阅与配对的退订动作打包成一个释放句柄。</summary>
    private sealed class DisposeAction(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
