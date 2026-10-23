using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OnePointUI.Avalonia.Base.Enum;
using OnePointUI.Avalonia.Styling.Effect;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.View;

public partial class AnimationBackground : UserControl
{
    /// <summary>
    ///     深浅色交叉系数：0 = 浅色主题（深砂），1 = 深色主题（亮尘）。
    /// </summary>
    /// <remarks>
    ///     声明成 AvaloniaProperty 只是为了能用 <see cref="Transitions" /> 做补间：
    ///     主题切换时 220ms 内把系数推过去，颗粒颜色随之平滑过渡，不会整个底色硬跳。
    /// </remarks>
    public static readonly StyledProperty<double> DarkMixProperty =
        AvaloniaProperty.Register<AnimationBackground, double>(nameof(DarkMix));

    // 交给 SkiaEffect 的是这个字典的引用：标量换值、向量数组就地改，下一帧就生效，
    // 不需要重建字典、也不需要重新编译着色器。
    // 标量必须是 float（不是单元素数组），vec2/vec3 必须是等长 float[]，否则
    // SkiaSharp 会以 'FloatArray' vs 'Float' 类型不符抛异常。
    private readonly float[] _noiseColor = new float[3];
    private readonly float[] _noiseColorAlt = new float[3];
    private readonly float[] _noiseDirVector = new float[2];

    private readonly Dictionary<string, object> _noiseUniforms = new(StringComparer.Ordinal);

    private BackgroundType _backgroundType = BackgroundType.Bubble;
    private double _noiseDirection;
    private float _noiseAmount = 0.03f;
    private double _noiseSpeed = 20d;
    private double _noiseGrain = 1.1d;
    private double _noiseSoftness = 0.55d;
    private double _noiseCornerRadius;
    private bool _motionEnabled = true;
    private float _noiseSeed = Random.Shared.NextSingle() * 1000f;

    public AnimationBackground()
    {
        InitializeComponent();

        WriteNoiseUniforms();
        RefreshNoiseColors();

        Loaded += (sender, args) => Update();
        ActualThemeVariantChanged += (sender, args) => SyncTheme();
    }

    public BackgroundType BackgroundType
    {
        get => _backgroundType;
        set
        {
            _backgroundType = value;
            Update();
        }
    }

    /// <summary>
    ///     高密度端的颗粒峰值不透明度（对应设计稿里的 maxNoiseAmount，默认 3%）。
    /// </summary>
    /// <remarks>
    ///     着色器内部再乘一个固定增益：0.03 直接当 alpha 会低到几乎看不见，
    ///     乘完落在 0.2 左右，才既有存在感又不抢正文。
    /// </remarks>
    public float NoiseAmount
    {
        get => _noiseAmount;
        set
        {
            _noiseAmount = Math.Clamp(value, 0f, 1f);
            _noiseUniforms["iNoiseAmount"] = _noiseAmount;
        }
    }

    /// <summary>
    ///     流动与密度梯度方向（角度，0° = 向左，正值在屏幕坐标系里顺时针旋转）。
    /// </summary>
    public double NoiseDirection
    {
        get => _noiseDirection;
        set
        {
            _noiseDirection = value;
            var rad = value * Math.PI / 180d;
            // 0° 指向左：(-1, 0)。y 轴向下，所以正角度看起来是顺时针。
            _noiseDirVector[0] = (float)-Math.Cos(rad);
            _noiseDirVector[1] = (float)-Math.Sin(rad);
        }
    }

    /// <summary>
    ///     恒定滚动速度，单位是 DIP/s。
    /// </summary>
    public double NoiseSpeed
    {
        get => _noiseSpeed;
        set
        {
            _noiseSpeed = Math.Max(value, 0d);
            WriteNoiseSpeedUniform();
        }
    }

    /// <summary>
    ///     颗粒基准半径（DIP）。小于 1 就是亚像素颗粒。
    /// </summary>
    public double NoiseGrain
    {
        get => _noiseGrain;
        set
        {
            _noiseGrain = Math.Max(value, 0.25d);
            _noiseUniforms["iNoiseGrain"] = (float)_noiseGrain;
        }
    }

    /// <summary>
    ///     颗粒边缘柔和度：0 = 锐利，1 = 羽化。
    /// </summary>
    public double NoiseSoftness
    {
        get => _noiseSoftness;
        set
        {
            _noiseSoftness = Math.Clamp(value, 0d, 1d);
            _noiseUniforms["iNoiseSoft"] = (float)_noiseSoftness;
        }
    }

    /// <summary>
    ///     目标区域圆角半径（DIP）。噪点只出现在这个圆角矩形里，边缘自然淡出。
    /// </summary>
    public double NoiseCornerRadius
    {
        get => _noiseCornerRadius;
        set
        {
            _noiseCornerRadius = Math.Max(value, 0d);
            _noiseUniforms["iNoiseRadius"] = (float)_noiseCornerRadius;
        }
    }

    /// <summary>
    ///     单色颗粒颜色覆盖。为 <c>null</c> 时跟随主题前景色（深色亮尘、浅色暗砂）。
    /// </summary>
    public Color? NoiseColor { get; set; }

    /// <summary>
    ///     每实例随机种子：同一个种子每次启动的颗粒分布完全一致。
    /// </summary>
    public float NoiseSeed
    {
        get => _noiseSeed;
        set
        {
            _noiseSeed = value;
            _noiseUniforms["iNoiseSeed"] = _noiseSeed;
        }
    }

    /// <summary>
    ///     是否允许动画。关闭时把速度归零，画面停在当前颗粒分布上（静态浮尘），
    ///     用于「减少动态效果」这类无障碍设置。
    /// </summary>
    /// <remarks>
    ///     关闭的是**速度**而不是渲染循环：已画出的颗粒不能消失，也不能闪一下。
    ///     速度归零后共动坐标不再推进，画面就定格成一张静态浮尘图。
    /// </remarks>
    public bool MotionEnabled
    {
        get => _motionEnabled;
        set
        {
            if (_motionEnabled == value) return;

            _motionEnabled = value;
            WriteNoiseSpeedUniform();
        }
    }

    /// <summary>
    ///     当前深浅色系数（0..1）。主题切换时由 <see cref="Transitions" /> 补间。
    /// </summary>
    public double DarkMix
    {
        get => GetValue(DarkMixProperty);
        private set => SetValue(DarkMixProperty, value);
    }

    public void Update()
    {
        SkiaEffect.UpdateColor();

        SkiaShaderRenderer.Stop();
        if (_backgroundType == BackgroundType.Bubble)
        {
            SkiaShaderRenderer.SetEffect(SkiaEffect.FromEmbeddedResource("bubble.sksl"));
            SkiaShaderRenderer.Start();
        }
        else if (_backgroundType == BackgroundType.Voronoi)
        {
            SkiaShaderRenderer.SetEffect(SkiaEffect.FromEmbeddedResource("voronoi.sksl"));
            SkiaShaderRenderer.Start();
        }
        else if (_backgroundType == BackgroundType.VoronoiSidebar)
        {
            SkiaShaderRenderer.SetEffect(SkiaEffect.FromEmbeddedResource("voronoi-sidebar.sksl"));
            SkiaShaderRenderer.Start();
        }
        else if (_backgroundType == BackgroundType.NoiseMarquee)
        {
            var effect = SkiaEffect.FromEmbeddedResource("noise-marquee.sksl");
            effect.SetExtraUniforms(_noiseUniforms);
            SkiaShaderRenderer.SetEffect(effect);
            SkiaShaderRenderer.Start();
        }
    }

    /// <summary>
    /// 停掉渲染循环（不再逐帧重绘着色器）。
    /// </summary>
    /// <remarks>
    /// 宿主切到别的材质时调用：光把 <c>IsVisible</c> 置 false 只是不画，
    /// 合成器的动画回调仍在跑，白白占 GPU。
    /// </remarks>
    public void Stop() => SkiaShaderRenderer.Stop();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncTheme();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DarkMixProperty)
            _noiseUniforms["iDarkMix"] = (float)change.GetNewValue<double>();
    }

    /// <summary>
    ///     把所有噪点参数写成 uniform 的初始值。之后各 setter 只改自己那一项。
    /// </summary>
    private void WriteNoiseUniforms()
    {
        _noiseUniforms["iNoiseAmount"] = _noiseAmount;
        _noiseUniforms["iNoiseDir"] = _noiseDirVector;
        _noiseUniforms["iNoiseGrain"] = (float)_noiseGrain;
        _noiseUniforms["iNoiseSoft"] = (float)_noiseSoftness;
        _noiseUniforms["iNoiseRadius"] = (float)_noiseCornerRadius;
        _noiseUniforms["iNoiseColor"] = _noiseColor;
        _noiseUniforms["iNoiseColorAlt"] = _noiseColorAlt;
        _noiseUniforms["iDarkMix"] = (float)DarkMix;
        _noiseUniforms["iNoiseSeed"] = _noiseSeed;

        WriteNoiseSpeedUniform();

        var rad = _noiseDirection * Math.PI / 180d;
        _noiseDirVector[0] = (float)-Math.Cos(rad);
        _noiseDirVector[1] = (float)-Math.Sin(rad);
    }

    private void WriteNoiseSpeedUniform()
    {
        _noiseUniforms["iNoiseSpeed"] = MotionEnabled ? (float)_noiseSpeed : 0f;
    }

    /// <summary>
    ///     深浅两套颗粒色都是「主题前景色」：深色下是亮尘、浅色下是暗砂。
    ///     两套色同时交给着色器，由 <c>iDarkMix</c> 插值，所以切换主题时颜色是渐变的。
    /// </summary>
    private void RefreshNoiseColors()
    {
        var dark = NoiseColor ?? ResolveForeground(ThemeVariant.Dark);
        var light = NoiseColor ?? ResolveForeground(ThemeVariant.Light);

        _noiseColor[0] = dark.R / 255f;
        _noiseColor[1] = dark.G / 255f;
        _noiseColor[2] = dark.B / 255f;
        _noiseColorAlt[0] = light.R / 255f;
        _noiseColorAlt[1] = light.G / 255f;
        _noiseColorAlt[2] = light.B / 255f;
    }

    private static Color ResolveForeground(ThemeVariant variant)
    {
        // 主题字典里 PrimaryForegroundBrush 就是白/黑（见 ThemeManager.LoadThemeColors）；
        // 主题尚未就绪时（设计期、构造早期）退回同一个中性值即可。
        if (Application.Current is { } app
            && app.TryGetResource("PrimaryForegroundBrush", variant, out var resource)
            && resource is ISolidColorBrush brush)
            return brush.Color;

        // 注意：本文件命名空间里已经有 OnePointUI.Avalonia.Styling.Colors 这个命名空间，
        // 直接写 Colors.White 会先绑定到它，所以这里走 global:: 限定。
        return variant == ThemeVariant.Dark
            ? global::Avalonia.Media.Colors.White
            : global::Avalonia.Media.Colors.Black;
    }

    /// <summary>
    ///     把 <see cref="DarkMix" /> 推到当前主题的目标值。
    /// </summary>
    /// <remarks>
    ///     第一次调用时还没有 <see cref="Transitions" />，所以直接落值，不会在启动
    ///     时看到一段多余的淡变；之后（真正切换主题时）才走 220ms 补间。
    /// </remarks>
    private void SyncTheme()
    {
        RefreshNoiseColors();
        DarkMix = ActualThemeVariant == ThemeVariant.Dark ? 1d : 0d;

        if (Transitions is not { Count: > 0 })
        {
            Transitions =
            [
                new DoubleTransition
                {
                    Property = DarkMixProperty,
                    Duration = TimeSpan.FromMilliseconds(220)
                }
            ];
        }
    }
}
