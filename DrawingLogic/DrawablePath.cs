using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Dalamud.Interface.Utility;
using System;

namespace AetherDraw.DrawingLogic
{
    public class DrawablePath : BaseDrawable
    {
        public List<Vector2> PointsRelative { get; set; } = new List<Vector2>();

        // Constructor: Initializes a new path.
        public DrawablePath(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Pen;
            // Ensure the first point is added.
            if (startPointRelative != default || !PointsRelative.Any()) // More robust check for empty list
            {
                PointsRelative.Add(startPointRelative);
            }
            this.Color = color;
            this.Thickness = unscaledThickness; // Store unscaled thickness.
            this.IsFilled = false; // Paths are not filled.
            this.IsPreview = true; // Starts as a preview.
        }

        // Adds a point to the path, ensuring a minimum distance from the previous point if not a preview.
        public void AddPoint(Vector2 pointRelative)
        {
            // Add if list is empty or if new point is sufficiently far from the last.
            // 0.01f is a small logical squared distance, doesn't need scaling.
            if (!PointsRelative.Any() || Vector2.DistanceSquared(PointsRelative.Last(), pointRelative) > 0.01f)
            {
                PointsRelative.Add(pointRelative);
            }
            // If it's a preview and points exist, update the last point to reflect current mouse position.
            else if (PointsRelative.Any() && this.IsPreview)
            {
                PointsRelative[PointsRelative.Count - 1] = pointRelative;
            }
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (PointsRelative.Count < 2) return; // Need at least two points to draw a line segment.

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            // Scale base thickness and selection highlight thickness.
            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = Math.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness); // Ensure minimum visible thickness.


            // Convert relative points to screen points.
            Vector2[] screenPoints = PointsRelative.Select(p => p + canvasOriginScreen).ToArray();

            if (screenPoints.Length > 1)
            {
                drawList.AddPolyline(ref screenPoints[0], screenPoints.Length, displayColor, ImDrawFlags.None, displayScaledThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            if (PointsRelative.Count < 2) return false;

            float scaledHitThreshold = unscaledHitThreshold * ImGuiHelpers.GlobalScale;
            float scaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float effectiveHitRange = scaledHitThreshold + (scaledThickness / 2f);

            // Check distance from the query point to each line segment in the path.
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
            var newPath = new DrawablePath(PointsRelative.FirstOrDefault(), this.Color, this.Thickness) // Pass unscaled thickness
            {
                PointsRelative = new List<Vector2>(this.PointsRelative) // Deep copy the list of points.
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
