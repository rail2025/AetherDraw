using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility; // Added for ImGuiHelpers

namespace AetherDraw.DrawingLogic
{
    public class DrawableCone : BaseDrawable
    {
        public Vector2 ApexRelative { get; set; }
        // BaseCenterRelative defines the length and unrotated direction of the cone's axis from the Apex.
        // These are logical, unscaled coordinates.
        public Vector2 BaseCenterRelative { get; set; }
        public float RotationAngle { get; set; } = 0f; // Rotation in radians around ApexRelative.

        // Factor determining the cone's width relative to its height. This is a proportion, so no scaling.
        public static readonly float ConeWidthFactor = 0.3f;

        // Constructor: Initializes a new cone.
        public DrawableCone(Vector2 apexRelative, Vector4 color, float unscaledThickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Cone;
            this.ApexRelative = apexRelative;
            this.BaseCenterRelative = apexRelative; // Initially, base center is at the apex.
            this.Color = color;
            this.Thickness = unscaledThickness; // Store unscaled thickness.
            this.IsFilled = isFilled;
            this.IsPreview = true;
            this.RotationAngle = 0f;
        }

        // Updates the base center of the cone during preview.
        public override void UpdatePreview(Vector2 newBaseCenterRelativeWhileDrawing)
        {
            this.BaseCenterRelative = newBaseCenterRelativeWhileDrawing;
        }

        // Helper to get the cone's vertices in their local, unrotated space (Apex at 0,0).
        // Returns logical, unscaled local coordinates.
        private (Vector2 localApex, Vector2 localBaseVert1, Vector2 localBaseVert2, Vector2 localBaseCenter) GetLocalUnrotatedVertices()
        {
            Vector2 localApex = Vector2.Zero;
            Vector2 localBaseCenterFromApex = this.BaseCenterRelative - this.ApexRelative;

            float height = localBaseCenterFromApex.Length();
            // 0.1f is a small logical threshold for height, doesn't need scaling here.
            if (height < 0.1f)
            {
                return (localApex, localApex, localApex, localApex); // Degenerate cone.
            }

            Vector2 direction = Vector2.Normalize(localBaseCenterFromApex);
            float baseHalfWidth = height * ConeWidthFactor; // Logical half-width.

            Vector2 localBaseVert1 = localBaseCenterFromApex + new Vector2(direction.Y, -direction.X) * baseHalfWidth;
            Vector2 localBaseVert2 = localBaseCenterFromApex + new Vector2(-direction.Y, direction.X) * baseHalfWidth;

            return (localApex, localBaseVert1, localBaseVert2, localBaseCenterFromApex);
        }


        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness);


            var (localApexUnscaled, localBaseVert1Unscaled, localBaseVert2Unscaled, localActualBaseCenterUnscaled) = GetLocalUnrotatedVertices();

            // Check with scaled minimum distance for preview drawing.
            // 1.0f is a small logical squared distance.
            if (localActualBaseCenterUnscaled == localApexUnscaled && this.IsPreview &&
                Vector2.DistanceSquared(this.ApexRelative, this.BaseCenterRelative) < (1.0f * ImGuiHelpers.GlobalScale * ImGuiHelpers.GlobalScale))
            {
                // drawList.AddCircleFilled(this.ApexRelative + canvasOriginScreen, displayScaledThickness / 2f + (1f * ImGuiHelpers.GlobalScale), displayColor); // Optional: draw a dot for tiny preview.
                return;
            }

            float cosA = MathF.Cos(this.RotationAngle);
            float sinA = MathF.Sin(this.RotationAngle);

            // Scale the local unscaled vertices for drawing.
            // Apex remains localApex (0,0) before scaling translation component.
            Vector2 rotatedScaledLocalBaseVert1 = HitDetection.ImRotate(localBaseVert1Unscaled * ImGuiHelpers.GlobalScale, cosA, sinA);
            Vector2 rotatedScaledLocalBaseVert2 = HitDetection.ImRotate(localBaseVert2Unscaled * ImGuiHelpers.GlobalScale, cosA, sinA);
            // Scaled Apex (still at logical origin for rotation, but if it had components, they'd scale here before adding world pos)
            // Vector2 scaledLocalApex = localApexUnscaled * ImGuiHelpers.GlobalScale; // This is (0,0) so still (0,0)

            // Translate rotated and scaled local vertices to screen space.
            // ApexRelative is logical, so scale it for screen positioning.
            Vector2 screenScaledApex = (this.ApexRelative * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
            Vector2 screenBaseVert1 = screenScaledApex + rotatedScaledLocalBaseVert1;
            Vector2 screenBaseVert2 = screenScaledApex + rotatedScaledLocalBaseVert2;

            if (this.IsFilled)
            {
                drawList.AddTriangleFilled(screenScaledApex, screenBaseVert1, screenBaseVert2, displayColor);
            }
            else
            {
                drawList.AddLine(screenScaledApex, screenBaseVert1, displayColor, displayScaledThickness);
                drawList.AddLine(screenScaledApex, screenBaseVert2, displayColor, displayScaledThickness);
                drawList.AddLine(screenBaseVert1, screenBaseVert2, displayColor, displayScaledThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            // Transform queryPointRelative into the cone's local, unrotated, unscaled space.
            Vector2 localQueryPointUnscaled = queryPointRelative - this.ApexRelative;
            float cosNegA = MathF.Cos(-this.RotationAngle);
            float sinNegA = MathF.Sin(-this.RotationAngle);
            Vector2 unrotatedLocalQueryPointUnscaled = HitDetection.ImRotate(localQueryPointUnscaled, cosNegA, sinNegA);

            var (localApex, localBaseVert1, localBaseVert2, localBaseCenter) = GetLocalUnrotatedVertices(); // These are unscaled.

            if (localBaseCenter == localApex) // Degenerate cone (unscaled check).
            {
                // Compare squared logical distance with squared scaled threshold.
                return Vector2.DistanceSquared(unrotatedLocalQueryPointUnscaled, localApex) < (unscaledHitThreshold * ImGuiHelpers.GlobalScale) * (unscaledHitThreshold * ImGuiHelpers.GlobalScale);
            }

            float scaledHitThreshold = unscaledHitThreshold * ImGuiHelpers.GlobalScale;
            float scaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float scaledEraserProximityFactor = 2.1f * ImGuiHelpers.GlobalScale;


            // For eraser-like interactions, compare scaled query circle with scaled triangle.
            if (scaledHitThreshold > (scaledThickness / 2f + scaledEraserProximityFactor))
            {
                // Scale the triangle vertices for intersection test with scaled query circle.
                Vector2 scaledTriangleApex = (this.ApexRelative + localApex) * ImGuiHelpers.GlobalScale; // localApex is (0,0)
                Vector2 scaledTriangleBase1 = (this.ApexRelative + localBaseVert1) * ImGuiHelpers.GlobalScale;
                Vector2 scaledTriangleBase2 = (this.ApexRelative + localBaseVert2) * ImGuiHelpers.GlobalScale;
                // queryPointRelative is logical, scale it for this comparison if IntersectCircleTriangle expects scaled points.
                // Or, scale the triangle and use a scaled queryPoint and scaledHitThreshold.
                // Let's assume queryPointRelative is logical and scale the triangle for the test.
                // This needs HitDetection.IntersectCircleTriangle to be clear about coordinate spaces.
                // For simplicity, assuming queryPointRelative is the logical point to check.
                // The unrotatedLocalQueryPointUnscaled is the point in the cone's unscaled, unrotated local frame.
                // We need to compare this with the unscaled local triangle and use unscaledHitThreshold.
                return HitDetection.IntersectCircleTriangle(unrotatedLocalQueryPointUnscaled, unscaledHitThreshold, localApex, localBaseVert1, localBaseVert2);
            }

            if (this.IsFilled)
            {
                // PointInTriangle with unscaled local coordinates.
                return HitDetection.PointInTriangle(unrotatedLocalQueryPointUnscaled, localApex, localBaseVert1, localBaseVert2);
            }
            else // Outline hit detection.
            {
                float effectiveLogicalHitRange = unscaledHitThreshold + (this.Thickness / 2f); // Logical range.
                if (HitDetection.DistancePointToLineSegment(unrotatedLocalQueryPointUnscaled, localApex, localBaseVert1) <= effectiveLogicalHitRange) return true;
                if (HitDetection.DistancePointToLineSegment(unrotatedLocalQueryPointUnscaled, localApex, localBaseVert2) <= effectiveLogicalHitRange) return true;
                if (HitDetection.DistancePointToLineSegment(unrotatedLocalQueryPointUnscaled, localBaseVert1, localBaseVert2) <= effectiveLogicalHitRange) return true;
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newCone = new DrawableCone(this.ApexRelative, this.Color, this.Thickness, this.IsFilled) // Pass unscaled thickness.
            {
                BaseCenterRelative = this.BaseCenterRelative,
                RotationAngle = this.RotationAngle
            };
            CopyBasePropertiesTo(newCone);
            return newCone;
        }

        public override void Translate(Vector2 delta)
        {
            this.ApexRelative += delta;
            this.BaseCenterRelative += delta;
        }

        public void RotateBy(float angleDeltaInRadians)
        {
            this.RotationAngle += angleDeltaInRadians;
        }

        public void SetApex(Vector2 newApex)
        {
            Vector2 diff = newApex - this.ApexRelative;
            this.ApexRelative = newApex;
            this.BaseCenterRelative += diff;
        }

        public void SetBaseCenter(Vector2 newBaseCenter)
        {
            this.BaseCenterRelative = newBaseCenter;
        }

        public void SetLength(float newLength) // newLength is a logical, unscaled length.
        {
            if (newLength < 0) newLength = 0;
            Vector2 axis = this.BaseCenterRelative - this.ApexRelative;
            if (axis.LengthSquared() < 0.001f)
            {
                axis = new Vector2(0, 1); // Default direction if zero length.
            }
            this.BaseCenterRelative = this.ApexRelative + Vector2.Normalize(axis) * newLength;
        }
    }
}
