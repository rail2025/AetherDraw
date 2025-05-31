// AetherDraw/DrawingLogic/DrawableCircle.cs
using System;
using System.Numerics;
using ImGuiNET; // For ImDrawListPtr in existing Draw method
using Dalamud.Interface.Utility; // For ImGuiHelpers

// ImageSharp using statements
using SixLabors.ImageSharp; // For PointF, Color
using SixLabors.ImageSharp.PixelFormats; // For Rgba32 if needed directly
using SixLabors.ImageSharp.Processing; // For IImageProcessingContext
using SixLabors.ImageSharp.Drawing; // For PathBuilder, Pens, IPath, EllipsePolygon
using SixLabors.ImageSharp.Drawing.Processing; // For Fill, Draw extension methods

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
            this.Radius = 0f; // Radius is typically determined during UpdatePreview.
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
            // Avoid drawing if the radius is very small during preview.
            if (this.Radius < 0.5f && this.IsPreview) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness);

            // Scale center and radius for ImGui drawing
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
            // Do not draw if radius is negligible for the export.
            if (this.Radius * currentGlobalScale < 0.5f) return;

            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);
            float finalScaledRadius = this.Radius * currentGlobalScale;

            // Calculate the center point on the output image.
            PointF centerPoint = new PointF(
                (this.CenterRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                (this.CenterRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
            );

            // Create an EllipsePolygon representing the circle.
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

        // Performs hit detection for the circle in logical (unscaled) coordinates.
        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            // Hit detection uses logical (unscaled) values.
            float distanceToCenter = Vector2.Distance(queryPointRelative, this.CenterRelative);

            if (this.IsFilled)
            {
                // For filled circles, check if the point is within the radius (plus hit threshold).
                return distanceToCenter <= this.Radius + unscaledHitThreshold;
            }
            else
            {
                // For outlined circles, check if the point is close to the circumference.
                // The thickness component is for the line's own width.
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
