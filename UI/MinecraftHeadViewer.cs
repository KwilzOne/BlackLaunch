using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

public class MinecraftHeadViewer : Control
{
    public static readonly StyledProperty<IImage?> SkinProperty = AvaloniaProperty.Register<MinecraftHeadViewer, IImage?>(nameof(Skin));

    public IImage? Skin { get => GetValue(SkinProperty); set => SetValue(SkinProperty, value); }

    public MinecraftHeadViewer() { RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None); }

    public override void Render(DrawingContext context)
    {
        if (Skin == null) return;
        var destRect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var baseRect = new Rect(8, 8, 8, 8);
        context.DrawImage(Skin, baseRect, destRect);
        if (Skin.Size.Width >= 64 && Skin.Size.Height >= 32) {
            var outerRect = new Rect(40, 8, 8, 8);
            context.DrawImage(Skin, outerRect, destRect);
        }
    }
}
