// AetherDraw/DrawingLogic/DrawableText.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using ImGuiNET;
using Dalamud.Interface.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using System.Linq;
using System.Drawing; // Required for RectangleF

namespace AetherDraw.DrawingLogic
{
    public class DrawableText : BaseDrawable
    {
        private string rawText_ = string.Empty;
        public string RawText
        {
            get => rawText_;
            set
            {
                var sanitizedValue = InputSanitizer.Sanitize(value ?? string.Empty);
                if (rawText_ != sanitizedValue) { rawText_ = sanitizedValue; PerformLayout(); }
            }
        }

        private Vector2 positionRelative_ = Vector2.Zero;
        public Vector2 PositionRelative
        {
            get => positionRelative_;
            set { if (positionRelative_ != value) positionRelative_ = value; }
        }

        private float fontSize_ = 16f;
        public float FontSize
        {
            get => fontSize_;
            set
            {
                var newSize = Math.Max(1f, value);
                if (Math.Abs(fontSize_ - newSize) > 0.001f) { fontSize_ = newSize; PerformLayout(); }
            }
        }

        private float wrappingWidth_ = 0f;
        public float WrappingWidth
        {
            get => wrappingWidth_;
            set
            {
                if (Math.Abs(wrappingWidth_ - value) > 0.001f) { wrappingWidth_ = value; PerformLayout(); }
            }
        }

        public Vector2 CurrentBoundingBoxSize { get; private set; } = Vector2.Zero;
        private List<string> laidOutLines_ = new List<string>();

        public DrawableText(Vector2 positionRelative, string rawText, Vector4 color, float fontSize, float wrappingWidth = 0f)
        {
            this.ObjectDrawMode = DrawMode.TextTool;
            this.positionRelative_ = positionRelative;
            this.Color = color;
            this.Thickness = 1f; this.IsFilled = true; this.IsPreview = false;
            fontSize_ = Math.Max(1f, fontSize);
            wrappingWidth_ = wrappingWidth;
            this.RawText = rawText;
        }

        public void PerformLayout()
        {
            laidOutLines_.Clear();
            if (string.IsNullOrEmpty(this.RawText) || ImGui.GetCurrentContext() == IntPtr.Zero || !ImGui.GetFont().IsLoaded())
            {
                CurrentBoundingBoxSize = Vector2.Zero;
                return;
            }

            var imFont = ImGui.GetFont();
            float originalFontScale = imFont.Scale;
            float baseImGuiFontSize = (imFont.ConfigDataCount > 0 && imFont.ConfigData.SizePixels > 0) ? imFont.ConfigData.SizePixels : imFont.FontSize;
            if (baseImGuiFontSize <= 0) baseImGuiFontSize = 16f;

            float targetScaledImGuiFontSize = this.FontSize * ImGuiHelpers.GlobalScale;
            imFont.Scale = targetScaledImGuiFontSize / baseImGuiFontSize;

            float wrapPxWidth = (WrappingWidth > 0.01f) ? (WrappingWidth * ImGuiHelpers.GlobalScale) : float.MaxValue;

            var lines = this.RawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (wrapPxWidth == float.MaxValue || string.IsNullOrEmpty(line))
                {
                    laidOutLines_.Add(line);
                    continue;
                }

                string[] words = line.Split(' ');
                var currentLine = new StringBuilder();
                foreach (var word in words)
                {
                    if (string.IsNullOrEmpty(word)) continue;
                    var testLine = currentLine.Length > 0 ? currentLine + " " + word : word;
                    if (ImGui.CalcTextSize(testLine).X > wrapPxWidth && currentLine.Length > 0)
                    {
                        laidOutLines_.Add(currentLine.ToString());
                        currentLine.Clear().Append(word);
                    }
                    else
                    {
                        if (currentLine.Length > 0) currentLine.Append(" ");
                        currentLine.Append(word);
                    }
                }
                if (currentLine.Length > 0) laidOutLines_.Add(currentLine.ToString());
            }

            float maxWidthPx = 0;
            foreach (string laidOutLine in laidOutLines_)
            {
                float lineWidth = ImGui.CalcTextSize(laidOutLine).X;
                if (lineWidth > maxWidthPx)
                {
                    maxWidthPx = lineWidth;
                }
            }

            float totalHeightPx = laidOutLines_.Count * ImGui.GetTextLineHeightWithSpacing();
            CurrentBoundingBoxSize = new Vector2(maxWidthPx, totalHeightPx) / ImGuiHelpers.GlobalScale;
            imFont.Scale = originalFontScale;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (laidOutLines_.Count == 0) return;

            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0.7f, 0.7f, 1f, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);
            float targetScaledFontSize = Math.Max(1f, FontSize * ImGuiHelpers.GlobalScale);

            if (ImGui.GetCurrentContext() != IntPtr.Zero && ImGui.GetFont().IsLoaded())
            {
                var imFont = ImGui.GetFont();
                float originalFontScale = imFont.Scale;
                float baseImGuiFontSize = (imFont.ConfigDataCount > 0 && imFont.ConfigData.SizePixels > 0) ? imFont.ConfigData.SizePixels : imFont.FontSize;
                if (baseImGuiFontSize <= 0) baseImGuiFontSize = 16f;
                imFont.Scale = targetScaledFontSize / baseImGuiFontSize;

                Vector2 currentLineScreenPos = (PositionRelative * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
                float lineHeight = ImGui.GetTextLineHeightWithSpacing();

                foreach (string line in laidOutLines_)
                {
                    drawList.AddText(imFont, imFont.FontSize * imFont.Scale, currentLineScreenPos, displayColor, line);
                    currentLineScreenPos.Y += lineHeight;
                }

                imFont.Scale = originalFontScale;
            }
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            if (string.IsNullOrEmpty(RawText?.Trim())) return;
            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba((byte)(Color.X * 255), (byte)(Color.Y * 255), (byte)(Color.Z * 255), (byte)(Color.W * 255));
            float scaledFontSizePoints = this.FontSize * currentGlobalScale;
            if (scaledFontSizePoints < 1f) scaledFontSizePoints = 1f;
            SixLabors.Fonts.Font? font = null;
            try
            {
                FontFamily? fontFamily = SystemFonts.Families.FirstOrDefault(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase));
                if (fontFamily == null && SystemFonts.Families.Any()) { fontFamily = SystemFonts.Families.First(); }
                if (fontFamily != null) { FontFamily actualNonNullFontFamily = (FontFamily)fontFamily; font = actualNonNullFontFamily.CreateFont(scaledFontSizePoints, SixLabors.Fonts.FontStyle.Regular); }
            }
            catch (Exception ex) { AetherDraw.Plugin.Log?.Warning(ex, "[DrawableText.DrawToImage] Error loading system font."); }
            if (font == null)
            {
                AetherDraw.Plugin.Log?.Error("[DrawableText.DrawToImage] Font not loaded, cannot draw text to image.");
                float fallbackWidth = (RawText.Length * scaledFontSizePoints * 0.6f); float fallbackHeight = scaledFontSizePoints;
                var fallbackPos = new SixLabors.ImageSharp.PointF((this.PositionRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X, (this.PositionRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y);
                var fallbackRect = new RectangularPolygon(fallbackPos.X, fallbackPos.Y, fallbackWidth, fallbackHeight);
                context.Fill(SixLabors.ImageSharp.Color.DarkRed, fallbackRect);
                return;
            }
            var textOrigin = new SixLabors.ImageSharp.PointF((this.PositionRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X, (this.PositionRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y);
            var textOptions = new RichTextOptions(font) { Origin = textOrigin, WrappingLength = (this.WrappingWidth > 0.01f) ? (this.WrappingWidth * currentGlobalScale) : 0, HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment.Left, VerticalAlignment = SixLabors.Fonts.VerticalAlignment.Top, };
            context.DrawText(textOptions, RawText, imageSharpColor);
        }

        public override System.Drawing.RectangleF GetBoundingBox()
        {
            // The bounding box for a text object is its top-left position and its calculated size.
            return new System.Drawing.RectangleF(this.PositionRelative.X, this.PositionRelative.Y, this.CurrentBoundingBoxSize.X, this.CurrentBoundingBoxSize.Y);
        }

        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            if (string.IsNullOrEmpty(RawText?.Trim())) return false;
            return (queryPointCanvasRelative.X >= PositionRelative.X - unscaledHitThreshold) &&
                   (queryPointCanvasRelative.X <= PositionRelative.X + CurrentBoundingBoxSize.X + unscaledHitThreshold) &&
                   (queryPointCanvasRelative.Y >= PositionRelative.Y - unscaledHitThreshold) &&
                   (queryPointCanvasRelative.Y <= PositionRelative.Y + CurrentBoundingBoxSize.Y + unscaledHitThreshold);
        }

        public override BaseDrawable Clone()
        {
            var newTextObject = new DrawableText(this.PositionRelative, this.RawText, this.Color, this.FontSize, this.WrappingWidth);
            CopyBasePropertiesTo(newTextObject);
            return newTextObject;
        }

        public override void Translate(Vector2 deltaCanvasRelative) { PositionRelative += deltaCanvasRelative; }
        public override void UpdatePreview(Vector2 currentPointRelative) { /* No standard preview update for text */ }
        public string GetHashCodeShort() => Math.Abs(this.GetHashCode()).ToString().Substring(0, Math.Min(10, Math.Abs(this.GetHashCode()).ToString().Length));

    }
}
