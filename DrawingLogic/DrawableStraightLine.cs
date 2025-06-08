// AetherDraw/DrawingLogic/DrawableStraightLine.cs
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility;
using System;
using System.Drawing; // Required for RectangleF
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;

namespace AetherDraw.DrawingLogic
{
    public class DrawableStraightLine : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }

        public DrawableStraightLine(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.StraightLine;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative;
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = false;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);
            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness);

            Vector2 screenStart = this.StartPointRelative * ImGuiHelpers.GlobalScale + canvasOriginScreen; // Apply scale
            Vector2 screenEnd = this.EndPointRelative * ImGuiHelpers.GlobalScale + canvasOriginScreen;   // Apply scale

            drawList.AddLine(screenStart, screenEnd, displayColor, displayScaledThickness);
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba((byte)(Color.X * 255), (byte)(Color.Y * 255), (byte)(Color.Z * 255), (byte)(Color.W * 255));
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            var p1 = new SixLabors.ImageSharp.PointF(
                (this.StartPointRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                (this.StartPointRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
            );
            var p2 = new SixLabors.ImageSharp.PointF(
                (this.EndPointRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                (this.EndPointRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
            );

            context.DrawLine(imageSharpColor, scaledThickness, p1, p2);
        }

        /// <summary>
        /// Calculates the axis-aligned bounding box for this line.
        /// </summary>
        /// <returns>A RectangleF representing the bounding box.</returns>
        public override System.Drawing.RectangleF GetBoundingBox()
        {
            float minX = MathF.Min(this.StartPointRelative.X, this.EndPointRelative.X);
            float minY = MathF.Min(this.StartPointRelative.Y, this.EndPointRelative.Y);
            float maxX = MathF.Max(this.StartPointRelative.X, this.EndPointRelative.X);
            float maxY = MathF.Max(this.StartPointRelative.Y, this.EndPointRelative.Y);

            return new System.Drawing.RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            // Hit detection uses logical coordinates
            float effectiveHitRange = unscaledHitThreshold + (this.Thickness / 2f);
            return HitDetection.DistancePointToLineSegment(queryPointRelative, this.StartPointRelative, this.EndPointRelative) <= effectiveHitRange;
        }

        public override BaseDrawable Clone()
        {
            var newLine = new DrawableStraightLine(this.StartPointRelative, this.Color, this.Thickness)
            {
                EndPointRelative = this.EndPointRelative
            };
            CopyBasePropertiesTo(newLine);
            return newLine;
        }

        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }
    }
}
