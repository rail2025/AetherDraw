// AetherDraw/DrawingLogic/DrawableCircle.cs
using System;
using System.Drawing; // Required for RectangleF
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace AetherDraw.DrawingLogic
{
    public class DrawableCircle : BaseDrawable
    {
        // Logical, unscaled center of the circle.
        public Vector2 CenterRelative { get; set; }
        // Logical, unscaled radius of the circle.
        public float Radius { get; set; }

        public DrawableCircle(Vector2 centerRelative, Vector4 color, float unscaledThickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Circle;
            this.CenterRelative = centerRelative;
            this.Radius = 0f;
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        // Updates the radius of the circle during preview drawing.
        public override void UpdatePreview(Vector2 pointRelative)
        {
            this.Radius = Vector2.Distance(this.CenterRelative, pointRelative);
        }

        // Draws the circle on the ImGui canvas.
        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (this.Radius < 0.5f && this.IsPreview) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness);

            Vector2 screenCenter = (this.CenterRelative * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
            float scaledRadiusForImGui = this.Radius * ImGuiHelpers.GlobalScale;

            int numSegments = (int)(scaledRadiusForImGui / 2f);
            numSegments = Math.Clamp(numSegments, 12, 128);

            if (this.IsFilled)
            {
                drawList.AddCircleFilled(screenCenter, scaledRadiusForImGui, displayColor, numSegments);
            }
            else
            {
                drawList.AddCircle(screenCenter, scaledRadiusForImGui, displayColor, numSegments, displayScaledThickness);
            }
        }

        // Draws the circle to an ImageSharp context for image export.
        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            if (this.Radius * currentGlobalScale < 0.5f) return;

            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);
            float finalScaledRadius = this.Radius * currentGlobalScale;

            var centerPoint = new SixLabors.ImageSharp.PointF(
                (this.CenterRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                (this.CenterRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
            );

            var ellipse = new EllipsePolygon(centerPoint, finalScaledRadius);

            if (IsFilled)
            {
                context.Fill(imageSharpColor, ellipse);
            }
            else
            {
                context.Draw(Pens.Solid(imageSharpColor, scaledThickness), ellipse);
            }
        }

        /// <summary>
        /// Calculates the axis-aligned bounding box for this circle.
        /// </summary>
        /// <returns>A RectangleF representing the bounding box.</returns>
        public override System.Drawing.RectangleF GetBoundingBox()
        {
            float diameter = this.Radius * 2;
            // We explicitly use System.Drawing.RectangleF to resolve ambiguity with the ImageSharp library's RectangleF.
            return new System.Drawing.RectangleF(this.CenterRelative.X - this.Radius, this.CenterRelative.Y - this.Radius, diameter, diameter);
        }

        // Performs hit detection for the circle in logical (unscaled) coordinates.
        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            float distanceToCenter = Vector2.Distance(queryPointRelative, this.CenterRelative);

            if (this.IsFilled)
            {
                return distanceToCenter <= this.Radius + unscaledHitThreshold;
            }
            else
            {
                return MathF.Abs(distanceToCenter - this.Radius) <= unscaledHitThreshold + (this.Thickness / 2f);
            }
        }

        // Creates a clone of this drawable circle.
        public override BaseDrawable Clone()
        {
            var newCircle = new DrawableCircle(this.CenterRelative, this.Color, this.Thickness, this.IsFilled)
            {
                Radius = this.Radius
            };
            CopyBasePropertiesTo(newCircle);
            return newCircle;
        }

        // Translates the circle by a given delta in logical coordinates.
        public override void Translate(Vector2 delta)
        {
            this.CenterRelative += delta;
        }
    }
}
