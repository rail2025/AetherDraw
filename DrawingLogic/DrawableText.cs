using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text; // For StringBuilder
using ImGuiNET;
using Dalamud.Interface.Utility;

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
                if (rawText_ != sanitizedValue)
                {
                    rawText_ = sanitizedValue;
                    PerformLayout();
                }
            }
        }

        private Vector2 positionRelative_ = Vector2.Zero; // Top-left of the bounding box, logical units
        public Vector2 PositionRelative
        {
            get => positionRelative_;
            set { if (positionRelative_ != value) positionRelative_ = value; }
        }

        // RotationAngle property and its usage are removed.
        // private float rotationAngle_ = 0f;

        private float fontSize_ = 16f; // Unscaled, logical font size
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

        private float wrappingWidth_ = 0f; // Unscaled, logical width. 0 or negative for auto-width.
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

        public Vector2 CurrentBoundingBoxSize { get; private set; } = Vector2.Zero; // Unscaled
        public Vector2 LocalCenter => CurrentBoundingBoxSize / 2f; // Center relative to top-left of bounding box
        public Vector2 CanvasRelativeBoundingBoxCenter => PositionRelative + LocalCenter; // Center relative to canvas origin

        private List<string> laidOutLines_ = new List<string>();

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
            this.RawText = rawText; // Setter calls PerformLayout
        }

        private void PerformLayout()
        {
            laidOutLines_.Clear();
            if (string.IsNullOrEmpty(this.RawText))
            {
                CurrentBoundingBoxSize = Vector2.Zero;
                return;
            }

            ImFontPtr currentFont = ImGui.GetFont();
            if (!(ImGui.GetCurrentContext() != IntPtr.Zero && currentFont.IsLoaded()))
            {
                AetherDraw.Plugin.Log?.Warning($"[DrawableText] PerformLayout: ImGui context or Font not fully ready.");
                CurrentBoundingBoxSize = ImGui.CalcTextSize(this.RawText); // Fallback
                laidOutLines_.Add(this.RawText);
                return;
            }

            float originalFontScaleProperty = currentFont.Scale;
            bool fontScaleWasAdjusted = false;

            if (currentFont.ConfigDataCount > 0 && currentFont.ConfigData.SizePixels > 0)
            {
                currentFont.Scale = this.FontSize / currentFont.ConfigData.SizePixels;
                fontScaleWasAdjusted = true;
            }
            else
            {
                AetherDraw.Plugin.Log?.Warning($"[DrawableText] PerformLayout: Font has no ConfigData or SizePixels is 0. Measurement based on current font scale.");
            }

            float maxWidthLayout = 0f;
            float totalHeightLayout = 0f;
            float unscaledLineHeight = ImGui.GetTextLineHeight(); // Based on temporarily scaled font

            if (this.WrappingWidth > 0.01f)
            {
                string[] paragraphs = this.RawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (string paragraph in paragraphs)
                {
                    if (string.IsNullOrEmpty(paragraph) && laidOutLines_.Count > 0)
                    {
                        laidOutLines_.Add(string.Empty);
                        totalHeightLayout += unscaledLineHeight;
                        continue;
                    }
                    string[] words = paragraph.Split(' ');
                    StringBuilder currentLineSb = new StringBuilder();
                    float currentLineWidthUnscaled = 0f;
                    for (int i = 0; i < words.Length; i++)
                    {
                        string word = words[i];
                        string wordSegment = (currentLineSb.Length > 0 ? " " : "") + word;
                        Vector2 wordSegmentSize = ImGui.CalcTextSize(wordSegment);
                        if (currentLineSb.Length > 0 && (currentLineWidthUnscaled + wordSegmentSize.X) > this.WrappingWidth && currentLineWidthUnscaled > 0)
                        {
                            laidOutLines_.Add(currentLineSb.ToString());
                            totalHeightLayout += unscaledLineHeight;
                            maxWidthLayout = Math.Max(maxWidthLayout, currentLineWidthUnscaled);
                            currentLineSb.Clear();
                            currentLineSb.Append(word);
                            currentLineWidthUnscaled = ImGui.CalcTextSize(word).X;
                        }
                        else
                        {
                            if (currentLineSb.Length > 0) currentLineSb.Append(" ");
                            currentLineSb.Append(word);
                            currentLineWidthUnscaled = ImGui.CalcTextSize(currentLineSb.ToString()).X;
                        }
                    }
                    if (currentLineSb.Length > 0)
                    {
                        laidOutLines_.Add(currentLineSb.ToString());
                        totalHeightLayout += unscaledLineHeight;
                        maxWidthLayout = Math.Max(maxWidthLayout, currentLineWidthUnscaled);
                    }
                }
                CurrentBoundingBoxSize = new Vector2(this.WrappingWidth, totalHeightLayout);
            }
            else
            {
                string[] lines = this.RawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (string line in lines)
                {
                    laidOutLines_.Add(line);
                    Vector2 lineSizeUnscaled = ImGui.CalcTextSize(line);
                    maxWidthLayout = Math.Max(maxWidthLayout, lineSizeUnscaled.X);
                    totalHeightLayout += unscaledLineHeight;
                }
                CurrentBoundingBoxSize = new Vector2(maxWidthLayout, totalHeightLayout);
            }

            if (laidOutLines_.Count == 0 && !string.IsNullOrEmpty(this.RawText))
            {
                Vector2 sizeOfAllText = ImGui.CalcTextSize(this.RawText);
                laidOutLines_.Add(this.RawText);
                CurrentBoundingBoxSize = sizeOfAllText;
            }
            if (laidOutLines_.Count > 0 && totalHeightLayout < 0.01f && unscaledLineHeight > 0.01f)
            {
                totalHeightLayout = laidOutLines_.Count * unscaledLineHeight;
                CurrentBoundingBoxSize = new Vector2(CurrentBoundingBoxSize.X, totalHeightLayout);
            }

            if (fontScaleWasAdjusted)
            {
                currentFont.Scale = originalFontScaleProperty;
            }
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (laidOutLines_.Count == 0 && string.IsNullOrEmpty(RawText.Trim())) return;

            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0.7f, 0.7f, 1f, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float finalScaledFontSizeForRender = Math.Max(1f, FontSize * ImGuiHelpers.GlobalScale);
            ImFontPtr font = ImGui.GetFont();
            float originalFontScaleProperty = font.Scale;
            bool fontScaleAdjustedForRender = false;

            if (font.ConfigDataCount > 0 && font.ConfigData.SizePixels > 0)
            {
                font.Scale = finalScaledFontSizeForRender / font.ConfigData.SizePixels;
                fontScaleAdjustedForRender = true;
            }

            float scaledLineHeightRender = ImGui.GetTextLineHeight(); // Line height at the render scale

            // Transformation: PositionRelative is top-left. Global scale and canvas origin are applied.
            // No rotation.
            Matrix3x2 transformMatrix =
                Matrix3x2.CreateTranslation(PositionRelative)    // Move to final logical position
                * Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale)  // Apply global scaling
                * Matrix3x2.CreateTranslation(canvasOriginScreen); // Move to screen space

            Vector2 currentLineLocalTopLeft = Vector2.Zero; // Start drawing lines from local 0,0 relative to PositionRelative
            foreach (string line in laidOutLines_)
            {
                if (!string.IsNullOrEmpty(line))
                {
                    Vector2 screenPos = Vector2.Transform(currentLineLocalTopLeft, transformMatrix);
                    drawList.AddText(font, finalScaledFontSizeForRender, screenPos, displayColor, line);
                }
                // Advance by logical line height (unscaled by GlobalScale here, because transformMatrix applies it to the positions)
                currentLineLocalTopLeft.Y += (scaledLineHeightRender / ImGuiHelpers.GlobalScale);
            }

            if (fontScaleAdjustedForRender)
            {
                font.Scale = originalFontScaleProperty;
            }

            // Bounding Box and Handles (No rotation handle now)
            if (IsSelected || IsHovered)
            {
                uint highlightRectColor = ImGui.GetColorU32(IsSelected ? new Vector4(1, 1, 0, 0.6f) : new Vector4(0, 1, 1, 0.4f));
                float handleRadiusScaled = 4f * ImGuiHelpers.GlobalScale;
                float highlightThicknessScaled = 1.5f * ImGuiHelpers.GlobalScale;

                // Bounding box is simply at PositionRelative, scaled, and moved to screen.
                Vector2 screenTopLeft = Vector2.Transform(PositionRelative, Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) * Matrix3x2.CreateTranslation(canvasOriginScreen));
                Vector2 screenBottomRight = Vector2.Transform(PositionRelative + CurrentBoundingBoxSize, Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) * Matrix3x2.CreateTranslation(canvasOriginScreen));

                drawList.AddRect(screenTopLeft, screenBottomRight, highlightRectColor, 0f, ImDrawFlags.None, highlightThicknessScaled);

                if (IsSelected) // Only show resize handles (for font size)
                {
                    uint handleColor = ImGui.GetColorU32(new Vector4(1, 0, 1, 0.8f));
                    Vector2[] screenBoxCorners = {
                        screenTopLeft,
                        new Vector2(screenBottomRight.X, screenTopLeft.Y),
                        screenBottomRight,
                        new Vector2(screenTopLeft.X, screenBottomRight.Y)
                    };
                    foreach (var corner in screenBoxCorners)
                    {
                        drawList.AddCircleFilled(corner, handleRadiusScaled, handleColor);
                    }
                }
            }
        }

        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            // queryPointCanvasRelative is logical (unscaled)
            if (CurrentBoundingBoxSize.X < 0.01f && CurrentBoundingBoxSize.Y < 0.01f && laidOutLines_.Count == 0) return false;

            // Since there's no rotation, hit detection is a simple AABB check.
            // queryPoint is relative to canvas origin. PositionRelative is top-left of text box, relative to canvas origin.
            // CurrentBoundingBoxSize is the logical size of the text box.
            return (queryPointCanvasRelative.X >= PositionRelative.X - unscaledHitThreshold) &&
                   (queryPointCanvasRelative.X <= PositionRelative.X + CurrentBoundingBoxSize.X + unscaledHitThreshold) &&
                   (queryPointCanvasRelative.Y >= PositionRelative.Y - unscaledHitThreshold) &&
                   (queryPointCanvasRelative.Y <= PositionRelative.Y + CurrentBoundingBoxSize.Y + unscaledHitThreshold);
        }

        public override BaseDrawable Clone()
        {
            var newTextObject = new DrawableText(
                this.PositionRelative, this.RawText, this.Color, this.FontSize, this.WrappingWidth);
            // RotationAngle is removed.
            CopyBasePropertiesTo(newTextObject);
            newTextObject.IsPreview = false;
            return newTextObject;
        }

        public override void Translate(Vector2 deltaCanvasRelative) // delta is logical
        {
            PositionRelative += deltaCanvasRelative;
        }

        // SetRotationAngleAroundCenter is removed.

        public override void UpdatePreview(Vector2 currentPointRelative) { }

        // GetLocalToCanvasTransformMatrix is simplified as there's no rotation.
        public Matrix3x2 GetLocalToCanvasTransformMatrix()
        {
            // Only translation by PositionRelative, as local 0,0 is already top-left.
            return Matrix3x2.CreateTranslation(PositionRelative);
        }

        public Matrix3x2 GetBasePositionToScreenTransformMatrixForEditor(Vector2 canvasOriginScreen)
        {
            // This transforms the logical PositionRelative (top-left of text)
            // to a screen coordinate for the editor window.
            return Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                   Matrix3x2.CreateTranslation(PositionRelative * ImGuiHelpers.GlobalScale + canvasOriginScreen);
        }

        public string GetHashCodeShort() => Math.Abs(this.GetHashCode()).ToString().Substring(0, Math.Min(10, Math.Abs(this.GetHashCode()).ToString().Length));
        private string LimitString(string s, int maxLength) => (s.Length <= maxLength) ? s : s.Substring(0, maxLength) + "...";
    }
}
