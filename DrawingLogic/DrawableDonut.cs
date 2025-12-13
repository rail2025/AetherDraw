// AetherDraw/DrawingLogic/DrawableDonut.cs
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
    public class DrawableDonut : BaseDrawable
    {
        public Vector2 CenterRelative { get; set; }
        public float Radius { get; set; }
        public float InnerRadius { get; set; }

        // Fix: Use optional parameters to support both 4-arg (Tool creation) and 6-arg (Deserializer) calls
        public DrawableDonut(Vector2 centerRelative, Vector4 color, float unscaledThickness, bool isFilled, float radius = 50f, float innerRadius = 25f)
        {
            this.ObjectDrawMode = DrawMode.Donut;
            this.CenterRelative = centerRelative;
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = isFilled;
            this.Radius = radius;
            this.InnerRadius = innerRadius;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newPoint)
        {
            this.Radius = Vector2.Distance(this.CenterRelative, newPoint);
            // Parity: Default inner radius to 50% of outer radius during creation
            this.InnerRadius = this.Radius * 0.5f;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (this.Radius < 0.5f && this.IsPreview) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, baseScaledThickness + highlightThicknessAddition);

            Vector2 screenCenter = (this.CenterRelative * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
            float scaledRadius = this.Radius * ImGuiHelpers.GlobalScale;
            float scaledInnerRadius = this.InnerRadius * ImGuiHelpers.GlobalScale;

            int numSegments = (int)(scaledRadius / 2f);
            numSegments = Math.Clamp(numSegments, 12, 128);

            if (this.IsFilled)
            {
                // To fill a ring in ImGui, we use a thick circle stroke centered between the inner and outer radii
                float ringThickness = scaledRadius - scaledInnerRadius;
                float midRadius = scaledInnerRadius + (ringThickness / 2f);

                // If ring thickness is valid, draw it
                if (ringThickness > 0)
                {
                    drawList.AddCircle(screenCenter, midRadius, displayColor, numSegments, ringThickness);
                }
            }
            else
            {
                // Draw outline: Outer circle and Inner circle
                drawList.AddCircle(screenCenter, scaledRadius, displayColor, numSegments, displayScaledThickness);
                drawList.AddCircle(screenCenter, scaledInnerRadius, displayColor, numSegments, displayScaledThickness);
            }
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            if (this.Radius * currentGlobalScale < 0.5f) return;

            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            float finalScaledRadius = this.Radius * currentGlobalScale;
            float finalScaledInnerRadius = this.InnerRadius * currentGlobalScale;

            var centerPoint = new SixLabors.ImageSharp.PointF(
                (this.CenterRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                (this.CenterRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
            );

            // Create geometry
            var outerEllipse = new EllipsePolygon(centerPoint, finalScaledRadius);
            var innerEllipse = new EllipsePolygon(centerPoint, finalScaledInnerRadius);

            if (IsFilled)
            {
                // Use Clip to cut the inner hole from the outer circle
                var donutPath = outerEllipse.Clip(innerEllipse);
                context.Fill(imageSharpColor, donutPath);
            }
            else
            {
                // Draw two outlines
                context.Draw(Pens.Solid(imageSharpColor, scaledThickness), outerEllipse);
                context.Draw(Pens.Solid(imageSharpColor, scaledThickness), innerEllipse);
            }
        }

        public override System.Drawing.RectangleF GetBoundingBox()
        {
            float diameter = this.Radius * 2;
            return new System.Drawing.RectangleF(this.CenterRelative.X - this.Radius, this.CenterRelative.Y - this.Radius, diameter, diameter);
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            float dist = Vector2.Distance(queryPointRelative, this.CenterRelative);

            if (IsFilled)
            {
                // Parity with JS: hit if >= InnerRadius AND <= Radius + threshold
                return dist >= this.InnerRadius && dist <= (this.Radius + unscaledHitThreshold);
            }
            else
            {
                // Parity with JS: hit if near outer rim OR near inner rim
                float hitRange = unscaledHitThreshold + (this.Thickness / 2f);
                bool hitOuter = Math.Abs(dist - this.Radius) <= hitRange;
                bool hitInner = Math.Abs(dist - this.InnerRadius) <= hitRange;
                return hitOuter || hitInner;
            }
        }

        public override BaseDrawable Clone()
        {
            // Uses the constructor to copy basic props, then sets specific ones
            var newDonut = new DrawableDonut(this.CenterRelative, this.Color, this.Thickness, this.IsFilled, this.Radius, this.InnerRadius);
            CopyBasePropertiesTo(newDonut);
            return newDonut;
        }

        public override void Translate(Vector2 delta)
        {
            this.CenterRelative += delta;
        }
    }
}
