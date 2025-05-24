// In AetherDraw/DrawingLogic/DrawablePath.cs
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawablePath : BaseDrawable
    {
        public List<Vector2> PointsRelative { get; set; } = new List<Vector2>();

        public DrawablePath(Vector2 startPointRelative, Vector4 color, float thickness)
        {
            this.ObjectDrawMode = DrawMode.Pen;
            // Ensure the first point is added, especially if it's the default Vector2,
            // or if the list is somehow empty when the constructor is called.
            if (startPointRelative != default || PointsRelative.Count == 0)
            {
                PointsRelative.Add(startPointRelative);
            }
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = false; // Paths are generally not considered "filled"
            this.IsPreview = true; // Starts as a preview
        }

        public void AddPoint(Vector2 pointRelative)
        {
            // Add if list is empty or if new point is sufficiently far from the last
            if (PointsRelative.Count == 0 || Vector2.DistanceSquared(PointsRelative.Last(), pointRelative) > 0.01f)
            {
                PointsRelative.Add(pointRelative);
            }
            // If it's a preview and points exist, update the last point to reflect current mouse position
            else if (PointsRelative.Count > 0 && this.IsPreview)
            {
                PointsRelative[PointsRelative.Count - 1] = pointRelative;
            }
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (PointsRelative.Count < 2) return; // Need at least two points to draw a line segment

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var displayThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            // Convert relative points to screen points
            Vector2[] screenPoints = PointsRelative.Select(p => p + canvasOriginScreen).ToArray();

            if (screenPoints.Length > 1)
            {
                // AddPolyline connects the points in sequence
                drawList.AddPolyline(ref screenPoints[0], screenPoints.Length, displayColor, ImDrawFlags.None, displayThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            if (PointsRelative.Count < 2) return false;

            // Check distance from the query point to each line segment in the path
            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                // Use the static HitDetection class (which is now in the same AetherDraw.DrawingLogic namespace)
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, PointsRelative[i], PointsRelative[i + 1]) <= hitThreshold + (this.Thickness / 2f))
                {
                    return true;
                }
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            // Create a new DrawablePath, starting with the first point if available
            var newPath = new DrawablePath(PointsRelative.FirstOrDefault(), this.Color, this.Thickness)
            {
                // Deep copy the list of points
                PointsRelative = new List<Vector2>(this.PointsRelative)
            };
            CopyBasePropertiesTo(newPath); // This also sets IsPreview to false, IsSelected to false etc.
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