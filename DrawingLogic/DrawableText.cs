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

namespace AetherDraw.DrawingLogic
{
    /// <summary>
    /// Represents a drawable text object on the canvas.
    /// Handles both ImGui rendering and export-to-image rendering.
    /// </summary>
    public class DrawableText : BaseDrawable
    {
        private string rawText_ = string.Empty;
        /// <summary>
        /// Gets or sets the raw text content.
        /// The text is sanitized upon being set, and layout is recalculated.
        /// </summary>
        public string RawText
        {
            get => rawText_;
            set
            {
                var sanitizedValue = InputSanitizer.Sanitize(value ?? string.Empty);
                if (rawText_ != sanitizedValue)
                {
                    rawText_ = sanitizedValue;
                    PerformLayout();
                }
            }
        }

        private Vector2 positionRelative_ = Vector2.Zero;
        /// <summary>
        /// Gets or sets the logical, unscaled top-left position of the text block.
        /// </summary>
        public Vector2 PositionRelative
        {
            get => positionRelative_;
            set { if (positionRelative_ != value) positionRelative_ = value; }
        }

        private float fontSize_ = 16f;
        /// <summary>
        /// Gets or sets the logical, unscaled font size.
        /// Minimum size is 1f. Triggers layout recalculation.
        /// </summary>
        public float FontSize
        {
            get => fontSize_;
            set
            {
                var newSize = Math.Max(1f, value);
                if (Math.Abs(fontSize_ - newSize) > 0.001f)
                {
                    fontSize_ = newSize;
                    PerformLayout();
                }
            }
        }

        private float wrappingWidth_ = 0f;
        /// <summary>
        /// Gets or sets the logical, unscaled wrapping width for the text.
        /// If 0 or less, text will not wrap for ImageSharp rendering.
        /// ImGui rendering relies on PerformLayout's interpretation.
        /// Triggers layout recalculation.
        /// </summary>
        public float WrappingWidth
        {
            get => wrappingWidth_;
            set
            {
                if (Math.Abs(wrappingWidth_ - value) > 0.001f)
                {
                    wrappingWidth_ = value;
                    PerformLayout();
                }
            }
        }

        /// <summary>
        /// Gets the unscaled bounding box size calculated for ImGui rendering.
        /// </summary>
        public Vector2 CurrentBoundingBoxSize { get; private set; } = Vector2.Zero;
        /// <summary>
        /// Gets the local center of the ImGui-calculated bounding box.
        /// </summary>
        public Vector2 LocalCenter => CurrentBoundingBoxSize / 2f;
        /// <summary>
        /// Gets the canvas-relative center of the ImGui-calculated bounding box.
        /// </summary>
        public Vector2 CanvasRelativeBoundingBoxCenter => PositionRelative + LocalCenter;

        // Stores lines of text after layout for ImGui rendering.
        private List<string> laidOutLines_ = new List<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="DrawableText"/> class.
        /// </summary>
        public DrawableText(Vector2 positionRelative, string rawText, Vector4 color, float fontSize, float wrappingWidth = 0f)
        {
            this.ObjectDrawMode = DrawMode.TextTool;
            this.positionRelative_ = positionRelative;
            this.Color = color;
            this.Thickness = 1f;
            this.IsFilled = true;
            this.IsPreview = false;

            fontSize_ = Math.Max(1f, fontSize);
            wrappingWidth_ = wrappingWidth;
            this.RawText = rawText;
        }

        /// <summary>
        /// Recalculates the text layout for ImGui display.
        /// Populates CurrentBoundingBoxSize and laidOutLines_ for the Draw method.
        /// Note: The line splitting logic for laidOutLines_ is a simplified stub
        /// and may not perfectly match ImGui's complex word wrapping for all cases.
        /// </summary>
        private void PerformLayout()
        {
            laidOutLines_.Clear();
            if (string.IsNullOrEmpty(this.RawText))
            {
                CurrentBoundingBoxSize = Vector2.Zero;
                return;
            }

            if (ImGui.GetCurrentContext() != IntPtr.Zero && ImGui.GetFont().IsLoaded())
            {
                var imFont = ImGui.GetFont();
                float originalFontScale = imFont.Scale;
                float baseImGuiFontSize = (imFont.ConfigDataCount > 0 && imFont.ConfigData.SizePixels > 0) ? imFont.ConfigData.SizePixels : imFont.FontSize;

                if (baseImGuiFontSize <= 0) baseImGuiFontSize = 16f;

                float targetScaledImGuiFontSize = this.FontSize * ImGuiHelpers.GlobalScale;
                imFont.Scale = targetScaledImGuiFontSize / baseImGuiFontSize;

                float wrapPxWidth = (WrappingWidth > 0.01f) ? (WrappingWidth * ImGuiHelpers.GlobalScale) : float.MaxValue;
                CurrentBoundingBoxSize = ImGui.CalcTextSize(RawText, false, wrapPxWidth == float.MaxValue ? 0 : wrapPxWidth) / ImGuiHelpers.GlobalScale;

                // Simplified line splitting logic for laidOutLines_.
                if (WrappingWidth > 0.01f && wrapPxWidth > 0 && wrapPxWidth != float.MaxValue)
                {
                    string[] words = RawText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.None);
                    StringBuilder currentLineSb = new StringBuilder();

                    for (int i = 0; i < words.Length; i++)
                    {
                        string word = words[i];
                        if (word == "" && i > 0 && (RawText.Contains("\n") || RawText.Contains("\r")))
                        {
                            if (currentLineSb.Length > 0) laidOutLines_.Add(currentLineSb.ToString());
                            laidOutLines_.Add("");
                            currentLineSb.Clear();
                            continue;
                        }
                        if (string.IsNullOrEmpty(word) && currentLineSb.Length == 0) continue;


                        string lineSoFar = currentLineSb.ToString();
                        string testWordWithSpace = (lineSoFar.Length > 0 ? " " : "") + word;
                        Vector2 currentLineVisualSize = ImGui.CalcTextSize(lineSoFar + testWordWithSpace);

                        if (currentLineVisualSize.X > wrapPxWidth && lineSoFar.Length > 0)
                        {
                            laidOutLines_.Add(lineSoFar);
                            currentLineSb.Clear().Append(word);
                        }
                        else
                        {
                            if (currentLineSb.Length > 0 && !string.IsNullOrEmpty(word)) currentLineSb.Append(" ");
                            currentLineSb.Append(word);
                        }
                    }
                    if (currentLineSb.Length > 0) laidOutLines_.Add(currentLineSb.ToString());
                }

                if (!laidOutLines_.Any() && !string.IsNullOrEmpty(RawText))
                {
                    laidOutLines_.AddRange(RawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
                }

                imFont.Scale = originalFontScale;
            }
            else
            {
                CurrentBoundingBoxSize = new Vector2(RawText.Length * FontSize * 0.6f, FontSize);
                laidOutLines_.Add(RawText);
            }
            if (!laidOutLines_.Any() && !string.IsNullOrEmpty(RawText)) laidOutLines_.Add(RawText);
        }

        /// <summary>
        /// Draws the text on the ImGui canvas using the pre-calculated layout.
        /// </summary>
        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if ((laidOutLines_ == null || laidOutLines_.Count == 0) && string.IsNullOrEmpty(RawText.Trim())) return;

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

                var linesToRender = (laidOutLines_ != null && laidOutLines_.Any()) ? laidOutLines_ : new List<string> { RawText };

                foreach (string line in linesToRender)
                {
                    drawList.AddText(imFont, imFont.FontSize * imFont.Scale, currentLineScreenPos, displayColor, line);
                    currentLineScreenPos.Y += lineHeight;
                }

                imFont.Scale = originalFontScale;
            }
        }

        /// <summary>
        /// Draws the text to an ImageSharp context for image export.
        /// </summary>
        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            if (string.IsNullOrEmpty(RawText?.Trim())) return;

            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );

            float scaledFontSizePoints = this.FontSize * currentGlobalScale;
            if (scaledFontSizePoints < 1f) scaledFontSizePoints = 1f;

            SixLabors.Fonts.Font? font = null;
            try
            {
                FontFamily? fontFamily = SystemFonts.Families.FirstOrDefault(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase));
                if (fontFamily == null && SystemFonts.Families.Any())
                {
                    fontFamily = SystemFonts.Families.First();
                }

                if (fontFamily != null)
                {
                    // Corrected: Explicitly cast fontFamily to FontFamily after null check.
                    // This addresses CS0266 if the null-forgiving operator wasn't sufficient.
                    FontFamily actualNonNullFontFamily = (FontFamily)fontFamily;
                    font = actualNonNullFontFamily.CreateFont(scaledFontSizePoints, SixLabors.Fonts.FontStyle.Regular);
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Warning(ex, "[DrawableText.DrawToImage] Error loading system font.");
            }

            if (font == null)
            {
                AetherDraw.Plugin.Log?.Error("[DrawableText.DrawToImage] Font not loaded, cannot draw text to image. Ensure fonts are available on the system or bundle one with the plugin.");
                float fallbackWidth = (RawText.Length * scaledFontSizePoints * 0.6f);
                float fallbackHeight = scaledFontSizePoints;
                PointF fallbackPos = new PointF(
                    (this.PositionRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                    (this.PositionRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
                );
                var fallbackRect = new RectangularPolygon(fallbackPos.X, fallbackPos.Y, fallbackWidth, fallbackHeight);
                context.Fill(SixLabors.ImageSharp.Color.DarkRed, fallbackRect);
                return;
            }

            PointF textOrigin = new PointF(
                (this.PositionRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                (this.PositionRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
            );

            var textOptions = new RichTextOptions(font)
            {
                Origin = textOrigin,
                WrappingLength = (this.WrappingWidth > 0.01f) ? (this.WrappingWidth * currentGlobalScale) : 0,
                HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment.Left,
                VerticalAlignment = SixLabors.Fonts.VerticalAlignment.Top,
            };

            context.DrawText(textOptions, RawText, imageSharpColor);
        }

        /// <summary>
        /// Performs hit detection based on the ImGui-calculated bounding box.
        /// </summary>
        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            if (CurrentBoundingBoxSize.X < 0.01f && CurrentBoundingBoxSize.Y < 0.01f && (laidOutLines_ == null || laidOutLines_.Count == 0 || string.IsNullOrEmpty(laidOutLines_.FirstOrDefault()))) return false;

            return (queryPointCanvasRelative.X >= PositionRelative.X - unscaledHitThreshold) &&
                   (queryPointCanvasRelative.X <= PositionRelative.X + CurrentBoundingBoxSize.X + unscaledHitThreshold) &&
                   (queryPointCanvasRelative.Y >= PositionRelative.Y - unscaledHitThreshold) &&
                   (queryPointCanvasRelative.Y <= PositionRelative.Y + CurrentBoundingBoxSize.Y + unscaledHitThreshold);
        }

        /// <summary>
        /// Creates a clone of this drawable text object.
        /// </summary>
        public override BaseDrawable Clone()
        {
            var newTextObject = new DrawableText(
                this.PositionRelative, this.RawText, this.Color, this.FontSize, this.WrappingWidth);
            CopyBasePropertiesTo(newTextObject);
            return newTextObject;
        }

        /// <summary>
        /// Translates the text object by a given delta in logical coordinates.
        /// </summary>
        public override void Translate(Vector2 deltaCanvasRelative)
        {
            PositionRelative += deltaCanvasRelative;
        }

        /// <summary>
        /// Updates the preview of the text object (not typically used for direct text placement).
        /// </summary>
        public override void UpdatePreview(Vector2 currentPointRelative) { /* No standard preview update */ }

        /// <summary>
        /// Gets a short string representation of the object's hash code, for unique ImGui IDs.
        /// </summary>
        public string GetHashCodeShort() => Math.Abs(this.GetHashCode()).ToString().Substring(0, Math.Min(10, Math.Abs(this.GetHashCode()).ToString().Length));

        /// <summary>
        /// Gets the transformation matrix from the text's base position to screen coordinates for the InPlaceTextEditor.
        /// </summary>
        public Matrix3x2 GetBasePositionToScreenTransformMatrixForEditor(Vector2 canvasOriginScreen)
        {
            return Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                   Matrix3x2.CreateTranslation(PositionRelative * ImGuiHelpers.GlobalScale + canvasOriginScreen);
        }
    }
}
