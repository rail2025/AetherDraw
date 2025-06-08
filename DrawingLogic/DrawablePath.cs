// AetherDraw/DrawingLogic/DrawablePath.cs
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Dalamud.Interface.Utility;
using System;
using System.Drawing; // Required for RectangleF

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace AetherDraw.DrawingLogic
{
    public class DrawablePath : BaseDrawable
    {
        public List<Vector2> PointsRelative { get; set; } = new List<Vector2>();

        public DrawablePath(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Pen;
            if (startPointRelative != default || !PointsRelative.Any())
            {
                PointsRelative.Add(startPointRelative);
            }
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = false;
            this.IsPreview = true;
        }

        public void AddPoint(Vector2 pointRelative)
        {
            if (!PointsRelative.Any() || Vector2.DistanceSquared(PointsRelative.Last(), pointRelative) > 0.01f)
            {
                PointsRelative.Add(pointRelative);
            }
            else if (PointsRelative.Any() && this.IsPreview)
            {
                PointsRelative[PointsRelative.Count - 1] = pointRelative;
            }
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (PointsRelative.Count < 2) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = Math.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness);

            Vector2[] screenPoints = PointsRelative.Select(p => p * ImGuiHelpers.GlobalScale + canvasOriginScreen).ToArray();

            if (screenPoints.Length > 1)
            {
                drawList.AddPolyline(ref screenPoints[0], screenPoints.Length, displayColor, ImDrawFlags.None, displayScaledThickness);
            }
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            if (PointsRelative.Count < 2) return;

            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255),
                (byte)(Color.Y * 255),
                (byte)(Color.Z * 255),
                (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            // Convert our list of points into the format the graphics library needs.
            var imageSharpPoints = this.PointsRelative.Select(p => new SixLabors.ImageSharp.PointF(
                (p.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                (p.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
                )).ToArray();

            // Build a single "Path" object from the list of points.
            var path = new PathBuilder().AddLines(imageSharpPoints).Build();

            // Draw the generated path onto the image.
            context.Draw(imageSharpColor, scaledThickness, path);
        }

        /// <summary>
        /// Calculates the axis-aligned bounding box for this path.
        /// </summary>
        /// <returns>A RectangleF representing the bounding box.</returns>
        public override System.Drawing.RectangleF GetBoundingBox()
        {
            if (this.PointsRelative.Count == 0) return System.Drawing.RectangleF.Empty;

            float minX = this.PointsRelative.Min(p => p.X);
            float minY = this.PointsRelative.Min(p => p.Y);
            float maxX = this.PointsRelative.Max(p => p.X);
            float maxY = this.PointsRelative.Max(p => p.Y);

            return new System.Drawing.RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            if (PointsRelative.Count < 2) return false;
            float effectiveHitRange = unscaledHitThreshold + (this.Thickness / 2f);
            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, PointsRelative[i], PointsRelative[i + 1]) <= effectiveHitRange)
                {
                    return true;
                }
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newPath = new DrawablePath(PointsRelative.FirstOrDefault(), this.Color, this.Thickness)
            {
                PointsRelative = new List<Vector2>(this.PointsRelative)
            };
            CopyBasePropertiesTo(newPath);
            return newPath;
        }

        public override void Translate(Vector2 delta)
        {
            for (int i = 0; i < PointsRelative.Count; i++)
            {
                PointsRelative[i] += delta;
            }
        }
    }
}
