// AetherDraw/DrawingLogic/EmojiRenderer.cs
using SkiaSharp;
using System;
using System.IO;
using System.Threading.Tasks;

public static class EmojiRenderer
{
    public static async Task<byte[]> RenderEmojiToPngAsync(string emoji, int size = 128)
    {
        using var typeface = SKTypeface.FromFamilyName("Segoe UI Emoji", SKFontStyle.Bold);

        if (typeface == null)
            throw new Exception("Segoe UI Emoji font not found.");

        using var font = new SKFont
        {
            Typeface = typeface,
            Size = size,
            Edging = SKFontEdging.SubpixelAntialias,
            Hinting = SKFontHinting.Full,
            Subpixel = true
        };

        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };

        var bounds = new SKRect();
        font.MeasureText(emoji, out bounds);

        int width = (int)Math.Ceiling(bounds.Width);
        int height = (int)Math.Ceiling(bounds.Height);

        if (width <= 0 || height <= 0)
            throw new Exception($"Invalid emoji dimensions for character: {emoji}");

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(emoji, -bounds.Left, -bounds.Top, font, paint);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        await using var ms = new MemoryStream();
        data.SaveTo(ms);
        return ms.ToArray();
    }
}
