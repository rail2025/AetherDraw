using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility; // Added for ImGuiHelpers

namespace AetherDraw.DrawingLogic
{
    public class DrawableArrow : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; } // Defines the end of the shaft, before rotation and scaling.
        public float RotationAngle { get; set; } = 0f; // Rotation in radians around StartPointRelative.

        // Arrowhead geometry factors; these are proportions and typically don't scale with GlobalScale.
        public static readonly float ArrowheadLengthFactor = 2.5f;
        public static readonly float ArrowheadWidthFactor = 1.5f;
        // MinArrowheadDim is a logical minimum size, will be scaled at draw/hit-test time.
        public static readonly float MinArrowheadDim = 5f;


        // Constructor: Initializes a new arrow.
        public DrawableArrow(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Arrow;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative; // End point same as start initially.
            this.Color = color;
            this.Thickness = unscaledThickness; // Store unscaled thickness.
            this.IsFilled = true; // Arrows are typically filled (shaft and head).
            this.IsPreview = true;
            this.RotationAngle = 0f;
        }

        // Updates the end point of the arrow's shaft during preview.
        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        /// <summary>
        /// Calculates the three vertices of the arrowhead triangle in world-relative (canvas-relative) coordinates.
        /// </summary>
        /// <param name="worldShaftEndPosition">The world-relative point where the arrow's shaft ends.</param>
        /// <param name="worldDirectionFromStartToTip">Normalized world-relative direction vector of the arrow's shaft.</param>
        /// <param name="scaledEffectiveThickness">Current SCALED thickness of the arrow, used to scale the arrowhead.</param>
        /// <returns>A tuple containing the visual tip and the two base vertices of the arrowhead in world-relative coordinates.</returns>
        public (Vector2 visualTip, Vector2 base1, Vector2 base2) GetArrowheadGeometricPoints(Vector2 worldShaftEndPosition, Vector2 worldDirectionFromStartToTip, float scaledEffectiveThickness)
        {
            float scaledMinArrowheadDim = MinArrowheadDim * ImGuiHelpers.GlobalScale;
            float arrowheadVisualLength = MathF.Max(scaledMinArrowheadDim, scaledEffectiveThickness * ArrowheadLengthFactor);
            float arrowheadHalfWidth = MathF.Max(scaledMinArrowheadDim / 2f, scaledEffectiveThickness * ArrowheadWidthFactor);

            Vector2 visualTip = worldShaftEndPosition + worldDirectionFromStartToTip * arrowheadVisualLength;
            // Perpendicular vectors for arrowhead base points.
            Vector2 perpendicularOffset = new Vector2(worldDirectionFromStartToTip.Y, -worldDirectionFromStartToTip.X) * arrowheadHalfWidth;
            Vector2 basePoint1 = worldShaftEndPosition + perpendicularOffset;
            Vector2 basePoint2 = worldShaftEndPosition - perpendicularOffset; // Use -perpendicularOffset for the other side

            return (visualTip, basePoint1, basePoint2);
        }


        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            // Calculate scaled thickness for drawing.
            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledShaftThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledShaftThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledShaftThickness);


            // Calculate shaft start and end points in screen space, considering rotation.
            Vector2 unrotatedShaftVector = this.EndPointRelative - this.StartPointRelative;
            float cosA = MathF.Cos(this.RotationAngle);
            float sinA = MathF.Sin(this.RotationAngle);
            Vector2 rotatedShaftVector = HitDetection.ImRotate(unrotatedShaftVector, cosA, sinA);
            Vector2 rotatedShaftEndRelative = this.StartPointRelative + rotatedShaftVector; // Shaft end relative to canvas origin.

            Vector2 screenStart = this.StartPointRelative + canvasOriginScreen;
            Vector2 screenRotatedShaftEnd = rotatedShaftEndRelative + canvasOriginScreen;

            // Avoid drawing if arrow is too small during preview.
            // 1.0f is a small logical squared distance.
            if (Vector2.DistanceSquared(screenStart, screenRotatedShaftEnd) < (1.0f * ImGuiHelpers.GlobalScale * ImGuiHelpers.GlobalScale) && this.IsPreview)
            {
                drawList.AddCircleFilled(screenStart, displayScaledShaftThickness / 2f + (1f * ImGuiHelpers.GlobalScale), displayColor);
                return;
            }

            // Draw the arrow shaft.
            drawList.AddLine(screenStart, screenRotatedShaftEnd, displayColor, displayScaledShaftThickness);

            // Calculate arrowhead geometry.
            Vector2 rotatedDirection = Vector2.Zero;
            if (rotatedShaftVector.LengthSquared() > 0.001f) // Check if shaft has a discernible length.
            {
                rotatedDirection = Vector2.Normalize(rotatedShaftVector);
            }
            else // Fallback for zero-length shaft (e.g., during preview at start point).
            {
                Vector2 defaultUnrotatedDir = new Vector2(0, -1); // Default upwards if shaft is zero. Could be (1,0) for right.
                rotatedDirection = HitDetection.ImRotate(defaultUnrotatedDir, cosA, sinA);
            }

            // Arrowhead geometry is based on the shaft's end point and its scaled thickness.
            // Note: GetArrowheadGeometricPoints now expects scaled thickness.
            var (tip, basePoint1, basePoint2) = GetArrowheadGeometricPoints(screenRotatedShaftEnd, rotatedDirection, baseScaledThickness); // Use baseScaledThickness for arrowhead size consistency.
            drawList.AddTriangleFilled(tip, basePoint1, basePoint2, displayColor);
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            // Transform query point into the arrow's local unrotated space (where StartPointRelative is origin, shaft along X or Y axis).
            Vector2 queryPointRelativeToStart = queryPointRelative - this.StartPointRelative;
            float cosNegA = MathF.Cos(-this.RotationAngle);
            float sinNegA = MathF.Sin(-this.RotationAngle);
            Vector2 unrotatedQueryPointLocalToStart = HitDetection.ImRotate(queryPointRelativeToStart, cosNegA, sinNegA);
            Vector2 unrotatedQueryPointWorldRelative = this.StartPointRelative + unrotatedQueryPointLocalToStart; // Query point if arrow had no rotation.

            float scaledHitThreshold = unscaledHitThreshold * ImGuiHelpers.GlobalScale;
            float scaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float effectiveShaftHitRange = scaledHitThreshold + (scaledThickness / 2f);

            // 1. Check hit on the unrotated line segment (shaft).
            bool hitShaft = HitDetection.DistancePointToLineSegment(unrotatedQueryPointWorldRelative, this.StartPointRelative, this.EndPointRelative) <= effectiveShaftHitRange;
            if (hitShaft) return true;

            // 2. Check hit on the unrotated arrowhead triangle.
            Vector2 unrotatedShaft = this.EndPointRelative - this.StartPointRelative;
            Vector2 unrotatedDirection = Vector2.Zero;

            if (unrotatedShaft.LengthSquared() > 0.001f)
            {
                unrotatedDirection = Vector2.Normalize(unrotatedShaft);
            }
            else // For zero-length arrow, arrowhead effectively originates from StartPointRelative.
            {
                // Hit test as a small circle around the start point if arrow has no length.
                return Vector2.DistanceSquared(queryPointRelative, this.StartPointRelative) < (scaledHitThreshold + scaledThickness) * (scaledHitThreshold + scaledThickness);
            }

            // Calculate unrotated arrowhead points relative to canvas for hit test.
            // GetArrowheadGeometricPoints expects world-relative shaft end and SCALED thickness.
            var (localVisualTip, localBase1, localBase2) = GetArrowheadGeometricPoints(this.EndPointRelative, unrotatedDirection, scaledThickness);

            // The 2.1f factor is a logical margin for eraser-like interactions.
            float scaledEraserProximityFactor = 2.1f * ImGuiHelpers.GlobalScale;

            // For eraser or large hit thresholds (passed in scaledHitThreshold).
            if (scaledHitThreshold > (scaledThickness / 2f + scaledEraserProximityFactor))
            {
                // IntersectCircleTriangle expects world-relative points and a world-space radius.
                return HitDetection.IntersectCircleTriangle(queryPointRelative, scaledHitThreshold, localVisualTip, localBase1, localBase2);
            }
            else // Point selection for arrowhead.
            {
                // PointInTriangle expects points in the same coordinate space.
                // We need to check unrotatedQueryPointWorldRelative against unrotated arrowhead points (localVisualTip, localBase1, localBase2).
                if (HitDetection.PointInTriangle(unrotatedQueryPointWorldRelative, localVisualTip, localBase1, localBase2))
                {
                    return true;
                }
                // Check proximity to arrowhead edges with a small scaled margin.
                float edgeHitProximity = scaledHitThreshold + (1.0f * ImGuiHelpers.GlobalScale);
                if (HitDetection.DistancePointToLineSegment(unrotatedQueryPointWorldRelative, localVisualTip, localBase1) <= edgeHitProximity) return true;
                if (HitDetection.DistancePointToLineSegment(unrotatedQueryPointWorldRelative, localVisualTip, localBase2) <= edgeHitProximity) return true;
                if (HitDetection.DistancePointToLineSegment(unrotatedQueryPointWorldRelative, localBase1, localBase2) <= edgeHitProximity) return true;
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newArrow = new DrawableArrow(this.StartPointRelative, this.Color, this.Thickness) // Pass unscaled thickness.
            {
                EndPointRelative = this.EndPointRelative,
                RotationAngle = this.RotationAngle
            };
            CopyBasePropertiesTo(newArrow);
            return newArrow;
        }

        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }

        public void RotateBy(float angleDeltaInRadians)
        {
            this.RotationAngle += angleDeltaInRadians;
        }

        public void SetStartPoint(Vector2 newStart)
        {
            Vector2 diff = newStart - this.StartPointRelative;
            this.StartPointRelative = newStart;
            this.EndPointRelative += diff; // Maintain shaft vector relative to start.
        }

        public void SetEndPoint(Vector2 newEnd)
        {
            this.EndPointRelative = newEnd;
        }
    }
}
