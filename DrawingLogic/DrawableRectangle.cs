using System;
using System.Numerics;
using ImGuiNET;
using System.Linq; // Required for ToArray on IEnumerable
using Dalamud.Interface.Utility; // Added for ImGuiHelpers

namespace AetherDraw.DrawingLogic
{
    public class DrawableRectangle : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }
        public float RotationAngle { get; set; } = 0f; // Rotation in radians

        // Constructor: Initializes a new rectangle.
        public DrawableRectangle(Vector2 startPointRelative, Vector4 color, float unscaledThickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Rectangle;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative; // End point is same as start initially.
            this.Color = color;
            this.Thickness = unscaledThickness; // Store unscaled thickness.
            this.IsFilled = isFilled;
            this.IsPreview = true;
            this.RotationAngle = 0f;
        }

        // Updates the end point of the rectangle during preview (e.g., while dragging to define size).
        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        // Helper to get the rectangle's unrotated corners and center based on StartPointRelative and EndPointRelative.
        public (Vector2[] unrotatedCorners, Vector2 center) GetGeometry()
        {
            Vector2 p1 = this.StartPointRelative;
            Vector2 p2 = this.EndPointRelative;

            // Define rectangle boundaries.
            Vector2 min = new Vector2(MathF.Min(p1.X, p2.X), MathF.Min(p1.Y, p2.Y));
            Vector2 max = new Vector2(MathF.Max(p1.X, p2.X), MathF.Max(p1.Y, p2.Y));

            Vector2[] unrotatedCorners = new Vector2[] {
                min,                             // Top-Left
                new Vector2(max.X, min.Y),       // Top-Right
                max,                             // Bottom-Right
                new Vector2(min.X, max.Y)        // Bottom-Left
            };
            Vector2 center = (min + max) / 2f; // Center of the unrotated rectangle.
            return (unrotatedCorners, center);
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            // Scale base thickness and selection/hover highlight.
            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness); // Ensure minimum visible thickness.


            var (unrotatedCorners, center) = GetGeometry();

            // Avoid drawing if the rectangle is too small (e.g., during initial click before drag).
            // 0.1f is a small logical squared distance, likely doesn't need scaling.
            if (Vector2.DistanceSquared(unrotatedCorners[0], unrotatedCorners[2]) < 0.1f && this.IsPreview) return;

            Vector2[] screenCorners = new Vector2[4];
            float cosA = MathF.Cos(this.RotationAngle);
            float sinA = MathF.Sin(this.RotationAngle);

            // Calculate screen positions of rotated corners.
            for (int i = 0; i < 4; i++)
            {
                Vector2 pLocal = unrotatedCorners[i] - center; // Vector from center to corner.
                Vector2 pRotatedLocal = HitDetection.ImRotate(pLocal, cosA, sinA); // Rotate corner around center.
                screenCorners[i] = center + pRotatedLocal + canvasOriginScreen; // Translate to world then screen space.
            }

            if (this.IsFilled)
            {
                drawList.AddConvexPolyFilled(ref screenCorners[0], 4, displayColor);
            }
            else
            {
                drawList.AddPolyline(ref screenCorners[0], 4, displayColor, ImDrawFlags.Closed, displayScaledThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            var (unrotatedCornersLocal, center) = GetGeometry(); // Using local variable name to avoid conflict

            // Transform queryPointRelative into the rectangle's local, unrotated space.
            Vector2 queryPointRelativeToCenter = queryPointRelative - center;
            float cosNegA = MathF.Cos(-this.RotationAngle); // Cosine of negative angle for inverse rotation.
            float sinNegA = MathF.Sin(-this.RotationAngle); // Sine of negative angle.
            Vector2 unrotatedQueryPointRelativeToCenter = HitDetection.ImRotate(queryPointRelativeToCenter, cosNegA, sinNegA);
            Vector2 unrotatedQueryPoint = unrotatedQueryPointRelativeToCenter + center; // Point in world-relative, but unrotated frame.

            // Original unrotated min/max points for AABB check.
            Vector2 minOriginal = new Vector2(MathF.Min(this.StartPointRelative.X, this.EndPointRelative.X), MathF.Min(this.StartPointRelative.Y, this.EndPointRelative.Y));
            Vector2 maxOriginal = new Vector2(MathF.Max(this.StartPointRelative.X, this.EndPointRelative.X), MathF.Max(this.StartPointRelative.Y, this.EndPointRelative.Y));

            float scaledHitThreshold = unscaledHitThreshold * ImGuiHelpers.GlobalScale;
            float scaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            // The 2.1f constant should also be scaled if it represents a visual margin.
            float scaledEraserProximityFactor = 2.1f * ImGuiHelpers.GlobalScale;


            // For eraser or large hit thresholds, check if the query circle intersects the unrotated AABB.
            if (scaledHitThreshold > (scaledThickness / 2f + scaledEraserProximityFactor))
            {
                return HitDetection.IntersectCircleAABB(unrotatedQueryPoint, scaledHitThreshold, minOriginal, maxOriginal);
            }

            if (this.IsFilled)
            {
                // Point-in-unrotated-rectangle check.
                return unrotatedQueryPoint.X >= minOriginal.X && unrotatedQueryPoint.X <= maxOriginal.X &&
                       unrotatedQueryPoint.Y >= minOriginal.Y && unrotatedQueryPoint.Y <= maxOriginal.Y;
            }
            else // Outline hit detection.
            {
                float effectiveHitRange = scaledHitThreshold + (scaledThickness / 2f);
                // Check proximity to each edge of the unrotated rectangle.
                bool onTopEdge = unrotatedQueryPoint.X >= minOriginal.X - scaledHitThreshold && unrotatedQueryPoint.X <= maxOriginal.X + scaledHitThreshold && MathF.Abs(unrotatedQueryPoint.Y - minOriginal.Y) < effectiveHitRange;
                bool onBottomEdge = unrotatedQueryPoint.X >= minOriginal.X - scaledHitThreshold && unrotatedQueryPoint.X <= maxOriginal.X + scaledHitThreshold && MathF.Abs(unrotatedQueryPoint.Y - maxOriginal.Y) < effectiveHitRange;
                bool onLeftEdge = unrotatedQueryPoint.Y >= minOriginal.Y - scaledHitThreshold && unrotatedQueryPoint.Y <= maxOriginal.Y + scaledHitThreshold && MathF.Abs(unrotatedQueryPoint.X - minOriginal.X) < effectiveHitRange;
                bool onRightEdge = unrotatedQueryPoint.Y >= minOriginal.Y - scaledHitThreshold && unrotatedQueryPoint.Y <= maxOriginal.Y + scaledHitThreshold && MathF.Abs(unrotatedQueryPoint.X - maxOriginal.X) < effectiveHitRange;

                return onTopEdge || onBottomEdge || onLeftEdge || onRightEdge;
            }
        }

        public override BaseDrawable Clone()
        {
            var newRect = new DrawableRectangle(this.StartPointRelative, this.Color, this.Thickness, this.IsFilled) // Pass unscaled thickness.
            {
                EndPointRelative = this.EndPointRelative,
                RotationAngle = this.RotationAngle
            };
            CopyBasePropertiesTo(newRect);
            return newRect;
        }

        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }

        // Rotates the rectangle by a given angle delta around its center.
        public void RotateBy(float angleDeltaInRadians)
        {
            this.RotationAngle += angleDeltaInRadians;
        }

        // Sets the start point of the rectangle, typically used for resizing.
        public void SetStartPointRelative(Vector2 newStartPointRelative) { this.StartPointRelative = newStartPointRelative; }
        // Sets the end point of the rectangle, typically used for resizing.
        public void SetEndPointRelative(Vector2 newEndPointRelative) { this.EndPointRelative = newEndPointRelative; }

        // Helper to get the current screen positions of the rectangle's (potentially rotated) corners.
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
                rotatedCorners[i] = center + pRotatedLocal; // These are relative to canvas origin
            }
            return rotatedCorners;
        }
    }
}
