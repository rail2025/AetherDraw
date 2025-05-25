using System;
using System.Numerics;
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawableArrow : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; } // Defines the end of the shaft, before rotation
        public float RotationAngle { get; set; } = 0f; // Rotation in radians around StartPointRelative

        // Made public static readonly so ShapeInteractionHandler can access them
        public static readonly float ArrowheadLengthFactor = 2.5f;
        public static readonly float ArrowheadWidthFactor = 1.5f;
        public static readonly float MinArrowheadDim = 5f;


        public DrawableArrow(Vector2 startPointRelative, Vector4 color, float thickness)
        {
            this.ObjectDrawMode = DrawMode.Arrow;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative;
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = true;
            this.IsPreview = true;
            this.RotationAngle = 0f;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        /// <summary>
        /// Calculates the three vertices of the arrowhead triangle.
        /// </summary>
        /// <param name="arrowShaftEndPosition">The point where the arrow's shaft ends (this becomes the center of the arrowhead's base).</param>
        /// <param name="directionFromStartToTip">Normalized direction vector of the arrow's shaft.</param>
        /// <param name="effectiveThickness">Current thickness of the arrow, used to scale the arrowhead.</param>
        /// <returns>A tuple containing the visual tip and the two base vertices of the arrowhead.</returns>
        public (Vector2 visualTip, Vector2 base1, Vector2 base2) GetArrowheadGeometricPoints(Vector2 arrowShaftEndPosition, Vector2 directionFromStartToTip, float effectiveThickness)
        {
            float arrowheadVisualLength = MathF.Max(MinArrowheadDim, effectiveThickness * ArrowheadLengthFactor);
            float arrowheadHalfWidth = MathF.Max(MinArrowheadDim / 2f, effectiveThickness * ArrowheadWidthFactor);

            Vector2 visualTip = arrowShaftEndPosition + directionFromStartToTip * arrowheadVisualLength;
            Vector2 basePoint1 = arrowShaftEndPosition + new Vector2(directionFromStartToTip.Y, -directionFromStartToTip.X) * arrowheadHalfWidth;
            Vector2 basePoint2 = arrowShaftEndPosition + new Vector2(-directionFromStartToTip.Y, directionFromStartToTip.X) * arrowheadHalfWidth;

            return (visualTip, basePoint1, basePoint2);
        }


        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var lineThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            Vector2 unrotatedShaftVector = this.EndPointRelative - this.StartPointRelative;

            float cosA = MathF.Cos(this.RotationAngle);
            float sinA = MathF.Sin(this.RotationAngle);
            Vector2 rotatedShaftVector = HitDetection.ImRotate(unrotatedShaftVector, cosA, sinA);

            Vector2 rotatedShaftEndRelative = this.StartPointRelative + rotatedShaftVector;

            Vector2 screenStart = this.StartPointRelative + canvasOriginScreen;
            Vector2 screenRotatedShaftEnd = rotatedShaftEndRelative + canvasOriginScreen;

            if (Vector2.DistanceSquared(screenStart, screenRotatedShaftEnd) < 1.0f && this.IsPreview)
            {
                drawList.AddCircleFilled(screenStart, lineThickness / 2f + 1f, displayColor);
                return;
            }

            drawList.AddLine(screenStart, screenRotatedShaftEnd, displayColor, lineThickness);

            Vector2 rotatedDirection = Vector2.Zero;
            if (rotatedShaftVector.LengthSquared() > 0.001f)
            {
                rotatedDirection = Vector2.Normalize(rotatedShaftVector);
            }
            else // Fallback for zero-length shaft (e.g., during preview at start point)
            {
                // Create a default direction based on rotation if shaft is zero length
                Vector2 defaultUnrotatedDir = new Vector2(1, 0); // Or (0,-1) for upwards
                rotatedDirection = HitDetection.ImRotate(defaultUnrotatedDir, cosA, sinA);
            }

            // Arrowhead geometry is based on the shaft's end point and its thickness
            var (tip, basePoint1, basePoint2) = GetArrowheadGeometricPoints(screenRotatedShaftEnd, rotatedDirection, this.Thickness);
            drawList.AddTriangleFilled(tip, basePoint1, basePoint2, displayColor);
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            Vector2 queryPointRelativeToStart = queryPointRelative - this.StartPointRelative;
            float cosNegA = MathF.Cos(-this.RotationAngle);
            float sinNegA = MathF.Sin(-this.RotationAngle);
            Vector2 unrotatedQueryPointRelativeToStart = HitDetection.ImRotate(queryPointRelativeToStart, cosNegA, sinNegA);
            Vector2 unrotatedQueryPoint = this.StartPointRelative + unrotatedQueryPointRelativeToStart;

            // 1. Check hit on the unrotated line segment (shaft)
            bool hitShaft = HitDetection.DistancePointToLineSegment(unrotatedQueryPoint, this.StartPointRelative, this.EndPointRelative) <= hitThreshold + (this.Thickness / 2f);
            if (hitShaft) return true;

            // 2. Check hit on the unrotated arrowhead triangle
            Vector2 unrotatedShaft = this.EndPointRelative - this.StartPointRelative;
            Vector2 unrotatedDirection = Vector2.Zero;

            if (unrotatedShaft.LengthSquared() > 0.001f)
            {
                unrotatedDirection = Vector2.Normalize(unrotatedShaft);
            }
            else // For zero-length arrow, arrowhead effectively originates from StartPointRelative
            {
                // A small circle hit test around the start point might be appropriate here
                // Or consider the arrowhead based on a default minimal length/direction for hit testing
                return Vector2.DistanceSquared(unrotatedQueryPoint, this.StartPointRelative) < (hitThreshold + this.Thickness) * (hitThreshold + this.Thickness);
            }

            // Calculate unrotated arrowhead points relative to canvas for hit test
            // GetArrowheadGeometricPoints expects the shaft end and direction.
            // For hit testing, we use the unrotated EndPointRelative as the shaft end.
            var (localVisualTip, localBase1, localBase2) = GetArrowheadGeometricPoints(this.EndPointRelative, unrotatedDirection, this.Thickness);

            if (hitThreshold > (this.Thickness / 2f + 2.1f)) // Eraser-like larger threshold
            {
                return HitDetection.IntersectCircleTriangle(unrotatedQueryPoint, hitThreshold, localVisualTip, localBase1, localBase2);
            }
            else // Point selection
            {
                if (HitDetection.PointInTriangle(unrotatedQueryPoint, localVisualTip, localBase1, localBase2))
                {
                    return true;
                }
                // Check proximity to arrowhead edges
                float edgeHitProximity = hitThreshold + 1.0f;
                if (HitDetection.DistancePointToLineSegment(unrotatedQueryPoint, localVisualTip, localBase1) <= edgeHitProximity) return true;
                if (HitDetection.DistancePointToLineSegment(unrotatedQueryPoint, localVisualTip, localBase2) <= edgeHitProximity) return true;
                if (HitDetection.DistancePointToLineSegment(unrotatedQueryPoint, localBase1, localBase2) <= edgeHitProximity) return true;
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newArrow = new DrawableArrow(this.StartPointRelative, this.Color, this.Thickness)
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
            this.EndPointRelative += diff;
        }

        public void SetEndPoint(Vector2 newEnd)
        {
            this.EndPointRelative = newEnd;
        }
    }
}
