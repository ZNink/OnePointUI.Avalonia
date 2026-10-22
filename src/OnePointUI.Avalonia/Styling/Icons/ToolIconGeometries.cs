using System.Collections.Generic;
using Avalonia.Media;

namespace OnePointUI.Avalonia.Styling.Icons;

/// <summary>
/// OnePointUI 内置工具图标的种类。每种对应一套 24x24 几何线稿，
/// 由 <c>ToolIcon</c> 控件渲染（描边 1.8，圆头圆角，安全区 20x20）。
/// </summary>
public enum ToolIconKind
{
    KillEn,
    Crosshair,
    CacheCleanup,
    LapPackage,
    ForceSprint,
    McpackRepair,
    RenderMode,
    DynamicTexture,
    Skybox,
    ChatFix,
    UnicodeObfuscation,
    JsonObfuscation,
    JavaTexture,
    McpManager,
    McpUnpack,
    FeverBypass,
    VideoCape,
    McpInjection,
}

/// <summary>单个图标的几何定义：<see cref="Stroke" /> 为线稿，<see cref="Fill" /> 为点缀实心部分。</summary>
public readonly record struct ToolIconGeometry(Geometry Stroke, Geometry? Fill);

/// <summary>
/// 工具图标几何表。由 <c>tools/tool-icon-preview/gen_geometries.py</c> 从
/// <c>Assets/Icons/Tools/*.svg</c> 生成 —— 改图形请改 SVG 后重跑脚本，不要手改这里的路径。
/// </summary>
internal static class ToolIconGeometries
{
    private static readonly Dictionary<ToolIconKind, ToolIconGeometry> Map = new()
    {
        [ToolIconKind.KillEn] = new(
            StreamGeometry.Parse("M16.45 6.85A7 7 0 1 1 7.55 6.85M12 4.1V11.3"),
            null),
        [ToolIconKind.Crosshair] = new(
            StreamGeometry.Parse("M12 2.4V5.6M12 18.4V21.6M2.4 12H5.6M18.4 12H21.6M5.8 12A6.2 6.2 0 1 1 18.2 12A6.2 6.2 0 1 1 5.8 12Z"),
            StreamGeometry.Parse("M11.38 12A0.62 0.62 0 1 1 12.62 12A0.62 0.62 0 1 1 11.38 12Z")),
        [ToolIconKind.CacheCleanup] = new(
            StreamGeometry.Parse("M4.2 7.2H16.4M8.6 7.2V5.6A1.6 1.6 0 0 1 10.2 4H12.2A1.6 1.6 0 0 1 13.8 5.6V7.2M6.2 7.2L7.05 19A2 2 0 0 0 9.05 20.9H13.35A2 2 0 0 0 15.35 19L16.2 7.2M9.9 10.6V17.4M12.5 10.6V17.4M20.7 3.1L21.5 4.5L22.9 5.3L21.5 6.1L20.7 7.5L19.9 6.1L18.5 5.3L19.9 4.5Z"),
            null),
        [ToolIconKind.LapPackage] = new(
            StreamGeometry.Parse("M6.8 7H17.2A2.4 2.4 0 0 1 19.6 9.4V18A2.4 2.4 0 0 1 17.2 20.4H6.8A2.4 2.4 0 0 1 4.4 18V9.4A2.4 2.4 0 0 1 6.8 7ZM4.4 10.2H19.6M12 7V20.4"),
            null),
        [ToolIconKind.ForceSprint] = new(
            StreamGeometry.Parse("M13.6 2.8L6.6 13H11.4L10.4 21.2L17.4 11H12.6Z"),
            null),
        [ToolIconKind.McpackRepair] = new(
            StreamGeometry.Parse("M12 2.9L19.3 5.5V12.1C19.3 16.9 16.2 20.1 12 21.4C7.8 20.1 4.7 16.9 4.7 12.1V5.5ZM8.9 11.9L11.2 14.3L15.5 9.6"),
            null),
        [ToolIconKind.RenderMode] = new(
            StreamGeometry.Parse("M5.1 4.6H18.9A2.2 2.2 0 0 1 21.1 6.8V14.9A2.2 2.2 0 0 1 18.9 17.1H5.1A2.2 2.2 0 0 1 2.9 14.9V6.8A2.2 2.2 0 0 1 5.1 4.6ZM12 17.1V20.3M8.4 20.3H15.6M7.2 8.9H16.2M14.3 7.3L16.4 8.9L14.3 10.5M16.8 12.9H7.8M9.7 11.3L7.6 12.9L9.7 14.5"),
            null),
        [ToolIconKind.DynamicTexture] = new(
            StreamGeometry.Parse("M5.1 3H11.1A2.1 2.1 0 0 1 13.2 5.1V11.1A2.1 2.1 0 0 1 11.1 13.2H5.1A2.1 2.1 0 0 1 3 11.1V5.1A2.1 2.1 0 0 1 5.1 3ZM14.2 7.4H21.3M18.9 5.6L21.3 7.4L18.9 9.2M21 16.4H13.9M16.3 14.6L13.9 16.4L16.3 18.2"),
            StreamGeometry.Parse("M4.5 3.9H7.5A0.6 0.6 0 0 1 8.1 4.5V7.5A0.6 0.6 0 0 1 7.5 8.1H4.5A0.6 0.6 0 0 1 3.9 7.5V4.5A0.6 0.6 0 0 1 4.5 3.9ZM8.7 8.1H11.7A0.6 0.6 0 0 1 12.3 8.7V11.7A0.6 0.6 0 0 1 11.7 12.3H8.7A0.6 0.6 0 0 1 8.1 11.7V8.7A0.6 0.6 0 0 1 8.7 8.1Z")),
        [ToolIconKind.Skybox] = new(
            StreamGeometry.Parse("M12 3.6L19.2 7.4L12 11.2L4.8 7.4ZM4.8 7.4V15.6L12 19.4V11.2M19.2 7.4V15.6L12 19.4"),
            StreamGeometry.Parse("M10.65 7.4A1.35 1.35 0 1 1 13.35 7.4A1.35 1.35 0 1 1 10.65 7.4Z")),
        [ToolIconKind.ChatFix] = new(
            StreamGeometry.Parse("M6.4 4.6H17.6A3 3 0 0 1 20.6 7.6V13.2A3 3 0 0 1 17.6 16.2H11.4L6 20.2V16A3 3 0 0 1 3.4 13V7.6A3 3 0 0 1 6.4 4.6ZM8.6 10.4L11 12.8L15.4 8.2"),
            null),
        [ToolIconKind.UnicodeObfuscation] = new(
            StreamGeometry.Parse("M6.4 10.6H17.6A2.4 2.4 0 0 1 20 13V18.2A2.4 2.4 0 0 1 17.6 20.6H6.4A2.4 2.4 0 0 1 4 18.2V13A2.4 2.4 0 0 1 6.4 10.6ZM8.6 9.2A3.4 3.4 0 0 1 15.4 9.2M12 14.1A1.1 1.1 0 1 0 12 16.3A1.1 1.1 0 1 0 12 14.1ZM12 16.3V18.2"),
            null),
        [ToolIconKind.JsonObfuscation] = new(
            StreamGeometry.Parse("M8.8 3.8C7 3.8 6.6 4.8 6.6 6.4V9C6.6 10.6 6 11.2 4.8 12C6 12.8 6.6 13.4 6.6 15V17.6C6.6 19.2 7 20.2 8.8 20.2M15.2 3.8C17 3.8 17.4 4.8 17.4 6.4V9C17.4 10.6 18 11.2 19.2 12C18 12.8 17.4 13.4 17.4 15V17.6C17.4 19.2 17 20.2 15.2 20.2M9.8 8.8H14.2M9.8 12C10.6 10.9 11.4 13.1 12.2 12C13 10.9 13.4 13.1 14.2 12M9.8 15.2H14.2"),
            null),
        [ToolIconKind.JavaTexture] = new(
            StreamGeometry.Parse("M5.4 9.2H16.2V13.4A4.4 4.4 0 0 1 11.8 17.8H9.8A4.4 4.4 0 0 1 5.4 13.4ZM16.2 10.6H17.8A2.4 2.4 0 0 1 17.8 15.4H16.2M4.6 20.4H17.2M9 5.6C9.8 4.9 8.2 4.2 9 3.5M12.6 5.6C13.4 4.9 11.8 4.2 12.6 3.5"),
            null),
        [ToolIconKind.McpManager] = new(
            StreamGeometry.Parse("M10.9 10.6L7.3 15.2M13.1 10.6L16.7 15.2M9.4 8.6A2.6 2.6 0 1 1 14.6 8.6A2.6 2.6 0 1 1 9.4 8.6ZM3.8 17.4A2.4 2.4 0 1 1 8.6 17.4A2.4 2.4 0 1 1 3.8 17.4ZM15.4 17.4A2.4 2.4 0 1 1 20.2 17.4A2.4 2.4 0 1 1 15.4 17.4Z"),
            null),
        [ToolIconKind.McpUnpack] = new(
            StreamGeometry.Parse("M3.8 12.6V18.6A2 2 0 0 0 5.8 20.6H18.2A2 2 0 0 0 20.2 18.6V12.6M3.8 12.6L7.4 9.6M20.2 12.6L16.6 9.6M12 9.8V2.9M9.6 5.3L12 2.9L14.4 5.3"),
            null),
        [ToolIconKind.FeverBypass] = new(
            StreamGeometry.Parse("M3.6 18.2V14A8.3 8.3 0 0 1 20.2 14V15M17.6 14.2L20.2 17.4L22.2 14.2M11.9 11.6V19.6M9.9 19.8H13.9"),
            null),
        [ToolIconKind.VideoCape] = new(
            StreamGeometry.Parse("M8.4 4.6C9.7 6.3 14.3 6.3 15.6 4.6L19.4 19.4C17.2 18.2 14.8 19.8 12 18.8C9.2 19.8 6.8 18.2 4.6 19.4ZM10.3 9L14.6 11.6L10.3 14.2Z"),
            null),
        [ToolIconKind.McpInjection] = new(
            StreamGeometry.Parse("M8.59 12.39L11.41 15.21L16.61 10.01L13.79 7.19ZM15.2 8.6L16.95 6.85M15.93 5.33L18.47 7.87M10 13.8L6.1 17.7M11.6 9.66L14.14 12.2"),
            null),
    };

    public static ToolIconGeometry? Get(ToolIconKind kind) =>
        Map.TryGetValue(kind, out var geometry) ? geometry : null;
}
