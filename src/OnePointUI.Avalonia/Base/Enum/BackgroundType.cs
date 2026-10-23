namespace OnePointUI.Avalonia.Base.Enum;

public enum BackgroundType
{
    Voronoi,
    Bubble,

    /// <summary>
    /// 侧边栏专用的泰森多边形：细胞更大、边界经过解析模糊、漂移更慢，
    /// 供 72px 宽的图标栏这类窄条使用（<c>voronoi-sidebar.sksl</c>）。
    /// </summary>
    VoronoiSidebar,

    /// <summary>
    ///     噪点跑马灯：一条匀速流动的细粉尘带，密度沿流动轴从 0 递增到
    ///     <c>NoiseAmount</c>，颗粒亮度呈幂律尾部（多数若隐若现、少数清晰）。
    ///     只落在绑定矩形内的圆角区域，区域外完全透明（<c>noise-marquee.sksl</c>）。
    /// </summary>
    NoiseMarquee
}