// In AetherDraw/DrawingLogic/DrawableArrow.cs
using System; // For MathF
using System.Numerics;
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawableArrow : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }

        public DrawableArrow(Vector2 startPointRelative, Vector4 color, float thickness)
        {
            this.ObjectDrawMode = DrawMode.Arrow;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative; // End point is same as start initially
            this.Color = color;
            this.Thickness = thickness; // This is for the shaft of the arrow
            this.IsFilled = true;       // The arrowhead is considered filled
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            // 'lineThickness' applies to the shaft and influences arrowhead size
            var lineThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            Vector2 screenStart = this.StartPointRelative + canvasOriginScreen;
            Vector2 screenEnd = this.EndPointRelative + canvasOriginScreen;

            // If start and end points are too close, draw a dot as a preview
            if (Vector2.DistanceSquared(screenStart, screenEnd) < 1.0f)
            {
                if (this.IsPreview)
                {
                    // Draw a small circle if the arrow is too short to be meaningful during preview
                    drawList.AddCircleFilled(screenStart, lineThickness / 2f + 1f, displayColor);
                }
                return;
            }

            // Draw the line (shaft) of the arrow
            drawList.AddLine(screenStart, screenEnd, displayColor, lineThickness);

            // Calculate arrowhead geometry
            Vector2 direction = (screenEnd == screenStart) ? new Vector2(0, -1) : Vector2.Normalize(screenEnd - screenStart);

            // Arrowhead size is based on the line's current display thickness
            float arrowheadLength = MathF.Max(8f, lineThickness * 2.5f);
            float arrowheadHalfWidth = MathF.Max(5f, lineThickness * 1.5f);

            // Arrowhead points: The original code's geometry places screenEnd as the center of the arrowhead's base,
            // and the tip (aT) extends forward from there.
            Vector2 tip = screenEnd + direction * arrowheadLength;
            Vector2 basePoint1 = screenEnd + new Vector2(direction.Y, -direction.X) * arrowheadHalfWidth; // Perpendicular to 'direction'
            Vector2 basePoint2 = screenEnd + new Vector2(-direction.Y, direction.X) * arrowheadHalfWidth; // Other side

            drawList.AddTriangleFilled(tip, basePoint1, basePoint2, displayColor);
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            // 1. Check hit on the line segment (shaft)
            if (HitDetection.DistancePointToLineSegment(queryPointRelative, this.StartPointRelative, this.EndPointRelative) <= hitThreshold + (this.Thickness / 2f))
            {
                return true;
            }

            // 2. Check hit on the arrowhead triangle
            // Calculate arrowhead geometry in relative coordinates using the base thickness
            Vector2 direction = (this.EndPointRelative == this.StartPointRelative) ? Vector2.Zero : Vector2.Normalize(this.EndPointRelative - this.StartPointRelative);
            if (direction == Vector2.Zero && this.StartPointRelative == this.EndPointRelative) // Handle case of zero-length arrow for hit test robustness
            { 
                return Vector2.DistanceSquared(queryPointRelative, this.StartPointRelative) < hitThreshold * hitThreshold;
            }

            // Arrowhead size for hit detection uses the base 'this.Thickness', not the potentially larger display thickness.
            float arrowheadLength = MathF.Max(8f, this.Thickness * 2.5f);
            float arrowheadHalfWidth = MathF.Max(5f, this.Thickness * 1.5f);

            Vector2 endPointRelative = this.EndPointRelative; // Reference for arrowhead base
            Vector2 tipRelative = endPointRelative + direction * arrowheadLength;
            Vector2 basePoint1Relative = endPointRelative + new Vector2(direction.Y, -direction.X) * arrowheadHalfWidth;
            Vector2 basePoint2Relative = endPointRelative + new Vector2(-direction.Y, direction.X) * arrowheadHalfWidth;

            // If hitThreshold is large (like an eraser), use circle-triangle intersection
            if (hitThreshold > (this.Thickness / 2f + 2.1f)) // Condition from original code
            {
                return HitDetection.IntersectCircleTriangle(queryPointRelative, hitThreshold, tipRelative, basePoint1Relative, basePoint2Relative);
            }
            else // For point selection
            {
                // Check if the query point is inside the arrowhead triangle
                if (HitDetection.PointInTriangle(queryPointRelative, tipRelative, basePoint1Relative, basePoint2Relative))
                {
                    return true;
                }
                // Original code also checked proximity to the triangle's edges. This makes the outline of the arrowhead clickable.
                float edgeHitProximity = hitThreshold + 1.0f; // As per original logic
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, tipRelative, basePoint1Relative) <= edgeHitProximity) return true;
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, tipRelative, basePoint2Relative) <= edgeHitProximity) return true;
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, basePoint1Relative, basePoint2Relative) <= edgeHitProximity) return true;
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newArrow = new DrawableArrow(this.StartPointRelative, this.Color, this.Thickness)
            {
                EndPointRelative = this.EndPointRelative
            };
            CopyBasePropertiesTo(newArrow);
            return newArrow;
        }

        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }
    }
}