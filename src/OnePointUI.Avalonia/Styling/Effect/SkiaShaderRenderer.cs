using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using SkiaSharp;

namespace OnePointUI.Avalonia.Styling.Effect;

public sealed class SkiaShaderRenderer : Control
{
    private CompositionCustomVisual? _customVisual;
    private SkiaEffect? _sukiEffect;
    private bool _isRunning;
    private bool _isPaused;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var comp = ElementComposition.GetElementVisual(this)?.Compositor;
        if (comp == null || _customVisual?.Compositor == comp)
            return;

        var visualHandler = new ShaderDraw();
        _customVisual = comp.CreateCustomVisual(visualHandler);
        ElementComposition.SetElementChildVisual(this, _customVisual);
        // 仅在 IsVisible 且未被主动暂停时才启动动画，避免后台/不可见时持续占用 GPU；
        // 暂停中的渲染器重新挂载时也要保持暂停，不能因为 IsVisible 还是 true 就自己跑起来。
        if (IsVisible && !_isPaused) SendStart();
        if (_sukiEffect != null)
            _customVisual.SendHandlerMessage(_sukiEffect);

        Update();
    }

    private void Update()
    {
        if (_customVisual == null) return;
        _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
    }

    /// <summary>
    /// 暂停动画：画面冻结在当前帧，<b>不</b>隐藏渲染器。
    /// </summary>
    /// <remarks>
    /// <para>隐藏（<c>IsVisible = false</c>）会把整层着色器从合成结果里摘掉，露出下面
    /// 的窗口材质。宿主在窗口失去焦点时调用本方法只是为了省掉逐帧重绘，
    /// 窗口仍然可见——一旦顺手把渲染器藏掉，背景就会整块消失、回退成亚克力。</para>
    /// <para><see cref="EffectDrawBase.StopAnimations"/> 同时停掉动画计时器，因此
    /// 最后一帧会原样保留，重新 <see cref="Start"/> 后从暂停处继续，不会跳帧。</para>
    /// </remarks>
    public void Stop()
    {
        _isPaused = true;
        SendStop();
    }

    public void Start()
    {
        _isPaused = false;
        IsVisible = true;
        SendStart();
    }

    private void SendStart()
    {
        if (_isRunning || _customVisual == null) return;
        _customVisual.SendHandlerMessage(EffectDrawBase.StartAnimations);
        _isRunning = true;
    }

    private void SendStop()
    {
        if (!_isRunning || _customVisual == null) return;
        _customVisual.SendHandlerMessage(EffectDrawBase.StopAnimations);
        _isRunning = false;
    }

    public void SetEffect(SkiaEffect effect)
    {
        _sukiEffect = effect;
        _customVisual?.SendHandlerMessage(effect);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            Update();
        }
        else if (change.Property == IsVisibleProperty)
        {
            // 跟随 IsVisible 自动启停动画，避免后台 60fps 空转。
            // 重新显示时若宿主已经主动暂停过，就不要自行恢复动画。
            if (!IsVisible) SendStop();
            else if (!_isPaused) SendStart();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // 控件从视觉树卸载时强制停止动画，防止后台帧回调持续触发
        SendStop();
        base.OnDetachedFromVisualTree(e);
    }

    private class ShaderDraw : EffectDrawBase
    {
        public ShaderDraw()
        {
            AnimationEnabled = true;
            AnimationSpeedScale = 2f;
        }

        protected override void Render(SKCanvas canvas, SKRect rect)
        {
            using var mainShaderPaint = new SKPaint();

            if (Effect is not null)
            {
                using var shader = EffectWithUniforms();
                mainShaderPaint.Shader = shader;
                canvas.DrawRect(rect, mainShaderPaint);
            }
        }

        protected override void RenderSoftware(SKCanvas canvas, SKRect rect)
        {
            throw new NotImplementedException();
        }
    }
}
