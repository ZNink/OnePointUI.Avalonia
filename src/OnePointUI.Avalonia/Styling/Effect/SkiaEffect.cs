using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using OnePointUI.Avalonia.Style.Core;
using OnePointUI.Avalonia.Styling.Colors;
using SkiaSharp;

namespace OnePointUI.Avalonia.Styling.Effect;

public class SkiaEffect
{
    // Basic uniforms passed into the shader from the CPU.
    private static readonly string[] Uniforms =
    [
        "uniform float iTime;",
        "uniform float iDark;",
        "uniform float iAlpha;",
        "uniform vec3 iResolution;",
        "uniform vec3 iPrimary;",
        "uniform vec3 iAccent;",
        "uniform vec3 iBase;"
    ];

    private static bool _disposed;
    private static readonly float[] White = [0.95f, 0.95f, 0.95f];
    private static readonly List<SkiaEffect> LoadedEffects = [];
    private static float[] _backgroundAlloc = new[] { 0.08f, 0.08f, 0.12f };
    private static float[] _backgroundAccentAlloc = new[] { 1.00f, 0.27f, 0.00f };
    private static float[] _backgroundPrimaryAlloc = new[] { 0.00f, 0.82f, 0.82f };
    private readonly float[] _boundsAlloc = new float[3];
    private readonly string _rawShaderString;

    private readonly string _shaderString;

    // 该 effect 实际声明过的 uniform 名字。SKRuntimeEffect.Uniforms 每次读取都会
    // 从原生侧封送一个新数组，所以只在第一次使用时缓存下来。
    private HashSet<string>? _declaredUniforms;

    // 宿主追加的 uniform（键为 shader 里声明过的名字）。这里只存引用不拷贝，
    // 调用方可以逐帧改值而不必重建字典；每帧只注入该 effect 真正声明过的那些，
    // 未声明的名字直接跳过，避免 SKRuntimeEffectUniforms 抛异常。
    private IReadOnlyDictionary<string, object>? _extraUniforms;

    static SkiaEffect()
    {
        if (Application.Current?.ApplicationLifetime is IControlledApplicationLifetime controlled)
            controlled.Exit += (_, _) => EnsureDisposed();
    }

    //error: 85: unknown identifier 'smoothstep'\n1 error\n
    private SkiaEffect(string shaderString, string rawShaderString)
    {
        _shaderString = shaderString;
        _rawShaderString = rawShaderString;
        var compiledEffect = SKRuntimeEffect.CreateShader(_shaderString, out var errors);
        Effect = compiledEffect ?? throw new ShaderCompilationException(errors);
    }

    /// <summary>
    ///     The compiled <see cref="SKRuntimeEffect" /> that will actually be used in draw calls.
    /// </summary>
    public SKRuntimeEffect Effect { get; }

    public static void UpdateColor()
    {
        if (ThemeManager.CurrentThemeVariant == ThemeVariant.Dark)
        {
            var color = ThemeManager.AccentColor;
            _backgroundPrimaryAlloc = new[] { color.R / 255f, color.G / 255f, color.B / 255f };
            _backgroundAlloc = new[]
                { color.Darken(0.5).R / 255f, color.Darken(0.5).G / 255f, color.Darken(0.5).B / 255f };
            _backgroundAccentAlloc = new[]
                { color.Darken(1).R / 255f, color.Darken(1).G / 255f, color.Darken(1).B / 255f };
        }
        else if (ThemeManager.CurrentThemeVariant == ThemeVariant.Light)
        {
            var color = ThemeManager.AccentColor;
            _backgroundPrimaryAlloc = new[] { global::Avalonia.Media.Colors.White.R / 255f, global::Avalonia.Media.Colors.White.G / 255f, global::Avalonia.Media.Colors.White.B / 255f };
            _backgroundAlloc = new[]
                { color.Lighten(0.3).R / 255f, color.Lighten(0.3).G / 255f, color.Lighten(0.3).B / 255f };
            _backgroundAccentAlloc = new[]
                { color.Lighten(0.1).R / 255f, color.Lighten(0.1).G / 255f, color.Lighten(0.1).B / 255f };
        }
    }

    /// <summary>
    ///     Attempts to load and compile a ".sksl" shader file from the assembly.
    ///     You don't need to provide the extension.
    ///     The shader will be pre-compiled
    ///     REMEMBER: For files to be discoverable in the assembly they should be marked as an embedded resource.
    /// </summary>
    /// <param name="shaderName">Name of the shader to load, with or without extension. - MUST BE .sksl</param>
    public static SkiaEffect FromEmbeddedResource(string shaderName)
    {
        shaderName = shaderName.ToLowerInvariant();
        if (!shaderName.EndsWith(".sksl"))
            shaderName += ".sksl";

        var assembly = Assembly.GetEntryAssembly();
        var resName = assembly?.GetManifestResourceNames()
            .FirstOrDefault(x => x.Contains(shaderName, StringComparison.InvariantCultureIgnoreCase));

        if (resName is null)
        {
            assembly = Assembly.GetExecutingAssembly();
            resName = assembly?.GetManifestResourceNames()
                .FirstOrDefault(x => x.Contains(shaderName, StringComparison.InvariantCultureIgnoreCase));
        }

        if (resName is null)
        {
            assembly = typeof(SkiaEffect).Assembly;
            resName = assembly?.GetManifestResourceNames()
                .FirstOrDefault(x => x.Contains(shaderName, StringComparison.InvariantCultureIgnoreCase));
        }

        if (resName is null)
            throw new FileNotFoundException(
                $"Unable to find a file with the name \"{shaderName}\" anywhere in the assembly.");

        var resourceStream = assembly?.GetManifestResourceStream(resName)
                             ?? throw new FileNotFoundException(
                                 $"Unable to open the embedded resource \"{resName}\".");
        using var tr = new StreamReader(resourceStream);
        return FromString(tr.ReadToEnd());
    }

    /// <summary>
    ///     Attempts to compile an sksl shader from a string.
    ///     The shader will be pre-compiled and any errors will be thrown as an exception.
    ///     REMEMBER: For files to be discoverable in the assembly they should be marked as an embedded resource.
    /// </summary>
    /// <param name="shaderString">The shader code to be compiled.</param>
    /// <returns>An instance of a SukiBackgroundShader with the loaded shader</returns>
    public static SkiaEffect FromString(string shaderString)
    {
        var sb = new StringBuilder();
        foreach (var uniform in Uniforms)
            sb.AppendLine(uniform);

        sb.Append(shaderString);
        var withUniforms = sb.ToString();
        return new SkiaEffect(withUniforms, shaderString);
    }


    /// <summary>
    ///     Necessary to make sure all the unmanaged effects are disposed.
    /// </summary>
    internal static void EnsureDisposed()
    {
        if (_disposed)
            throw new InvalidOperationException(
                "Effects should only be disposed once at the app lifecycle end.");

        _disposed = true;
        foreach (var loaded in LoadedEffects)
            loaded.Effect.Dispose();

        LoadedEffects.Clear();
    }

    public override bool Equals(object? obj)
    {
        if (obj is not SkiaEffect effect) return false;
        return effect._shaderString == _shaderString;
    }

    /// <summary>
    ///     给这个 effect 追加一组每帧注入的 uniform。
    /// </summary>
    /// <param name="uniforms">
    ///     键是 shader 里声明过的 uniform 名。值按声明类型给：标量用 <see cref="float" />，
    ///     vec2/vec3 用长度等于分量数的 <see cref="float" />[]。只保存引用，宿主之后原地
    ///     改数组里的值就会立刻生效；当前 effect 没有声明的键会被忽略。
    /// </param>
    /// <remarks>
    ///     标量必须用 <see cref="float" /> 而不是单元素数组：SkiaSharp 会校验
    ///     Float 与 FloatArray 的类型，给错会抛
    ///     <c>ArgumentOutOfRangeException: Unable to write a 'FloatArray' value to a 'Float' uniform</c>。
    /// </remarks>
    public void SetExtraUniforms(IReadOnlyDictionary<string, object>? uniforms)
    {
        _extraUniforms = uniforms;
    }

    private bool DeclaresUniform(string name)
    {
        _declaredUniforms ??= new HashSet<string>(Effect.Uniforms, StringComparer.Ordinal);
        return _declaredUniforms.Contains(name);
    }

    internal SKShader ToShaderWithUniforms(float timeSeconds, ThemeVariant activeVariant, Rect bounds,
        float animationScale, float alpha = 1f)
    {
        _boundsAlloc[0] = (float)bounds.Width;
        _boundsAlloc[1] = (float)bounds.Height;

        var inputs = new SKRuntimeEffectUniforms(Effect)
        {
            { "iResolution", _boundsAlloc },
            { "iTime", timeSeconds * animationScale },
            { "iBase", _backgroundAlloc },
            { "iAccent", _backgroundAccentAlloc },
            { "iPrimary", _backgroundPrimaryAlloc },
            { "iDark", activeVariant == ThemeVariant.Dark ? 1f : 0f },
            { "iAlpha", alpha }
        };

        // iTime 会被 AnimationSpeedScale 缩放，带物理单位（DIP/s）的动画没法直接用它。
        // 约定：需要「真实秒」的 shader 自行声明 iFlowTime，这里补上未缩放的原始值。
        if (DeclaresUniform("iFlowTime"))
            inputs.Add("iFlowTime", timeSeconds);

        if (_extraUniforms is { Count: > 0 } extras)
        {
            foreach (var (name, value) in extras)
            {
                if (!DeclaresUniform(name)) continue;

                // 标量与向量走不同的重载：FloatArray 塞不进 Float uniform。
                switch (value)
                {
                    case float scalar:
                        inputs.Add(name, scalar);
                        break;
                    case float[] array:
                        inputs.Add(name, array);
                        break;
                }
            }
        }

        return Effect.ToShader(inputs);
    }

    internal SKShader ToShaderWithCustomUniforms(Func<SKRuntimeEffect, SKRuntimeEffectUniforms> uniformFactory,
        float timeSeconds, Rect bounds,
        float animationScale, float alpha = 1f)
    {
        var uniforms = uniformFactory(Effect);
        uniforms.Add("iResolution", new[] { (float)bounds.Width, (float)bounds.Height, 0f });
        uniforms.Add("iTime", timeSeconds * animationScale);
        uniforms.Add("iAlpha", alpha);
        return Effect.ToShader(uniforms);
    }

    /// <summary>
    ///     Returns the pure shader string without uniforms.
    /// </summary>
    public override string ToString()
    {
        return _rawShaderString;
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(_shaderString);
    }

    private class ShaderCompilationException(string message) : Exception(message);
}
