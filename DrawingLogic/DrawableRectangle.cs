// In AetherDraw/DrawingLogic/DrawableRectangle.cs
using System;
using System.Numerics;
using ImGuiNET;
using System.Linq; // Required for ToArray on IEnumerable

namespace AetherDraw.DrawingLogic
{
    public class DrawableRectangle : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }
        public float RotationAngle { get; set; } = 0f; // Rotation in radians

        public DrawableRectangle(Vector2 startPointRelative, Vector4 color, float thickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Rectangle;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative;
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
            this.RotationAngle = 0f;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        // Helper to get the rectangle's unrotated corners and center
        public (Vector2[] unrotatedCorners, Vector2 center) GetGeometry()
        {
            Vector2 p1 = this.StartPointRelative;
            Vector2 p2 = this.EndPointRelative;

            Vector2 min = new Vector2(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y));
            Vector2 max = new Vector2(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));

            Vector2[] unrotatedCorners = new Vector2[] {
                min,                             // Top-Left
                new Vector2(max.X, min.Y),       // Top-Right
                max,                             // Bottom-Right
                new Vector2(min.X, max.Y)        // Bottom-Left
            };
            Vector2 center = (min + max) / 2f;
            return (unrotatedCorners, center);
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var displayThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            var (unrotatedCorners, center) = GetGeometry();

            if (Vector2.DistanceSquared(unrotatedCorners[0], unrotatedCorners[2]) < 0.1f && this.IsPreview) return; // Too small to draw

            Vector2[] screenCorners = new Vector2[4];
            float cosA = MathF.Cos(this.RotationAngle);
            float sinA = MathF.Sin(this.RotationAngle);

            for (int i = 0; i < 4; i++)
            {
                // Translate corner to be relative to pivot (center), rotate, then translate back and to screen space
                Vector2 pLocal = unrotatedCorners[i] - center;
                Vector2 pRotatedLocal = HitDetection.ImRotate(pLocal, cosA, sinA);
                screenCorners[i] = center + pRotatedLocal + canvasOriginScreen;
            }

            if (this.IsFilled)
            {
                drawList.AddConvexPolyFilled(ref screenCorners[0], 4, displayColor);
            }
            else
            {
                // AddPolyline needs a closed flag or draw the 4th segment manually
                drawList.AddPolyline(ref screenCorners[0], 4, displayColor, ImDrawFlags.Closed, displayThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            var (unrotatedCorners, center) = GetGeometry();

            // Transform queryPointRelative into the rectangle's local, unrotated space
            Vector2 queryPointRelativeToCenter = queryPointRelative - center;
            float cosNegA = MathF.Cos(-this.RotationAngle);
            float sinNegA = MathF.Sin(-this.RotationAngle);
            Vector2 unrotatedQueryPointRelativeToCenter = HitDetection.ImRotate(queryPointRelativeToCenter, cosNegA, sinNegA);
            Vector2 unrotatedQueryPoint = unrotatedQueryPointRelativeToCenter + center; // Back to world-relative, but unrotated

            // Original unrotated min/max points for AABB check
            Vector2 minOriginal = new Vector2(Math.Min(this.StartPointRelative.X, this.EndPointRelative.X), Math.Min(this.StartPointRelative.Y, this.EndPointRelative.Y));
            Vector2 maxOriginal = new Vector2(Math.Max(this.StartPointRelative.X, this.EndPointRelative.X), Math.Max(this.StartPointRelative.Y, this.EndPointRelative.Y));

            // For eraser or large hit thresholds, check if the query circle intersects the unrotated AABB
            if (hitThreshold > (this.Thickness / 2f + 2.1f))
            {
                // We use the unrotated query point and unrotated AABB
                return HitDetection.IntersectCircleAABB(unrotatedQueryPoint, hitThreshold, minOriginal, maxOriginal);
            }

            if (this.IsFilled)
            {
                // Point-in-unrotated-rectangle check
                return unrotatedQueryPoint.X >= minOriginal.X && unrotatedQueryPoint.X <= maxOriginal.X &&
                       unrotatedQueryPoint.Y >= minOriginal.Y && unrotatedQueryPoint.Y <= maxOriginal.Y;
            }
            else
            {
                // Check proximity to the borders of the unrotated rectangle
                float effectiveHitRange = hitThreshold + (this.Thickness / 2f);
                bool onTopEdge = unrotatedQueryPoint.X >= minOriginal.X - hitThreshold && unrotatedQueryPoint.X <= maxOriginal.X + hitThreshold && Math.Abs(unrotatedQueryPoint.Y - minOriginal.Y) < effectiveHitRange;
                bool onBottomEdge = unrotatedQueryPoint.X >= minOriginal.X - hitThreshold && unrotatedQueryPoint.X <= maxOriginal.X + hitThreshold && Math.Abs(unrotatedQueryPoint.Y - maxOriginal.Y) < effectiveHitRange;
                bool onLeftEdge = unrotatedQueryPoint.Y >= minOriginal.Y - hitThreshold && unrotatedQueryPoint.Y <= maxOriginal.Y + hitThreshold && Math.Abs(unrotatedQueryPoint.X - minOriginal.X) < effectiveHitRange;
                bool onRightEdge = unrotatedQueryPoint.Y >= minOriginal.Y - hitThreshold && unrotatedQueryPoint.Y <= maxOriginal.Y + hitThreshold && Math.Abs(unrotatedQueryPoint.X - maxOriginal.X) < effectiveHitRange;

                return onTopEdge || onBottomEdge || onLeftEdge || onRightEdge;
            }
        }

        public override BaseDrawable Clone()
        {
            var newRect = new DrawableRectangle(this.StartPointRelative, this.Color, this.Thickness, this.IsFilled)
            {
                EndPointRelative = this.EndPointRelative,
                RotationAngle = this.RotationAngle // Clone rotation angle
            };
            CopyBasePropertiesTo(newRect);
            return newRect;
        }

        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }

        // --- New methods for direct manipulation ---
        public void RotateBy(float angleDeltaInRadians)
        {
            this.RotationAngle += angleDeltaInRadians;
        }

        // For resizing, manipulate StartPointRelative and EndPointRelative.
        // If resizing a corner of a rotated rectangle,
        // 1. User drags a visual handle (which is a rotated corner).
        // 2. Convert the new mouse position from screen/world space to the rectangle's local (unrotated) space.
        // 3. Update the corresponding local corner (e.g., if top-left handle, update StartPointRelative).
        // 4. This implicitly updates EndPointRelative if they define opposite corners.
        // For example, to set a new top-left corner (StartPointRelative) after un-rotating the mouse input:
        public void SetStartPointRelative(Vector2 newStartPointRelative) { this.StartPointRelative = newStartPointRelative; }
        public void SetEndPointRelative(Vector2 newEndPointRelative) { this.EndPointRelative = newEndPointRelative; }

        // Helper to get corners for manipulation in MainWindow
        public Vector2[] GetRotatedCorners()
        {
            var (unrotatedCorners, center) = GetGeometry();
            Vector2[] rotatedCorners = new Vector2[4];
            float cosA = MathF.Cos(this.RotationAngle);
            float sinA = MathF.Sin(this.RotationAngle);
            for (int i = 0; i < 4; i++)
            {
                Vector2 pLocal = unrotatedCorners[i] - center;
                Vector2 pRotatedLocal = HitDetection.ImRotate(pLocal, cosA, sinA);
                rotatedCorners[i] = center + pRotatedLocal;
            }
            return rotatedCorners;
        }

    }
}
