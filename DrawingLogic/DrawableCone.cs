// In AetherDraw/DrawingLogic/DrawableCone.cs
using System.Numerics;
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawableCone : BaseDrawable
    {
        public Vector2 ApexRelative { get; set; }
        // BaseCenterRelative is the point the user drags to define the cone's direction and length from the apex.
        public Vector2 BaseCenterRelative { get; set; }

        public DrawableCone(Vector2 apexRelative, Vector4 color, float thickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Cone;
            this.ApexRelative = apexRelative;
            this.BaseCenterRelative = apexRelative; // Initially, base center is at the apex
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newBaseCenterRelative)
        {
            // This point is typically the current mouse position while drawing
            this.BaseCenterRelative = newBaseCenterRelative;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var displayThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            Vector2 screenApex = this.ApexRelative + canvasOriginScreen;
            Vector2 screenBaseCenter = this.BaseCenterRelative + canvasOriginScreen;

            // If the cone is too small to draw during preview, just return
            if (Vector2.DistanceSquared(screenApex, screenBaseCenter) < 1.0f && this.IsPreview)
            {
                // Optionally, draw a small dot at the apex for immediate feedback during preview
                // drawList.AddCircleFilled(screenApex, displayThickness / 2f + 1f, displayColor);
                return;
            }

            // Direction from apex towards the center of the base line
            Vector2 direction = (screenBaseCenter == screenApex) ? new Vector2(0, 1) : Vector2.Normalize(screenBaseCenter - screenApex); // Default downwards
            float height = Vector2.Distance(screenApex, screenBaseCenter); // Distance from apex to base center

            // The half-width of the cone's base is proportional to its height (defines a fixed cone angle)
            float baseHalfWidth = height * 0.3f; // Original ratio

            // Calculate the two points forming the base of the cone.
            // These points are perpendicular to the 'direction' vector and centered around 'screenBaseCenter'.
            Vector2 basePoint1 = screenBaseCenter + new Vector2(direction.Y, -direction.X) * baseHalfWidth;
            Vector2 basePoint2 = screenBaseCenter + new Vector2(-direction.Y, direction.X) * baseHalfWidth;

            if (this.IsFilled)
            {
                // The cone is drawn as a triangle: (Apex, BasePoint1, BasePoint2)
                drawList.AddTriangleFilled(screenApex, basePoint1, basePoint2, displayColor);
            }
            else
            {
                drawList.AddLine(screenApex, basePoint1, displayColor, displayThickness);
                drawList.AddLine(screenApex, basePoint2, displayColor, displayThickness);
                drawList.AddLine(basePoint1, basePoint2, displayColor, displayThickness); // Draw the base line itself
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThresholdOrEraserRadius = 5.0f)
        {
            // Calculate cone geometry in relative coordinates for hit testing
            Vector2 direction = (this.BaseCenterRelative == this.ApexRelative) ? new Vector2(0, 1) : Vector2.Normalize(this.BaseCenterRelative - this.ApexRelative);
            float height = Vector2.Distance(this.ApexRelative, this.BaseCenterRelative);

            // If the cone is essentially a point (very small height)
            if (height < 0.1f)
            {
                return Vector2.DistanceSquared(queryPointRelative, this.ApexRelative) < hitThresholdOrEraserRadius * hitThresholdOrEraserRadius;
            }

            float baseHalfWidth = height * 0.3f; // Using the same ratio as in Draw
            Vector2 relativeApex = this.ApexRelative;
            // Relative base points calculated from BaseCenterRelative
            Vector2 relativeBasePoint1 = this.BaseCenterRelative + new Vector2(direction.Y, -direction.X) * baseHalfWidth;
            Vector2 relativeBasePoint2 = this.BaseCenterRelative + new Vector2(-direction.Y, direction.X) * baseHalfWidth;

            // If hitThreshold is large (e.g., eraser), use circle-triangle intersection against the cone's triangle
            if (hitThresholdOrEraserRadius > (this.Thickness / 2f + 2.1f)) // Condition from original code
            {
                return HitDetection.IntersectCircleTriangle(queryPointRelative, hitThresholdOrEraserRadius, relativeApex, relativeBasePoint1, relativeBasePoint2);
            }

            // For point selection or small eraser
            if (this.IsFilled)
            {
                // Check if the point is inside the cone triangle
                if (HitDetection.PointInTriangle(queryPointRelative, relativeApex, relativeBasePoint1, relativeBasePoint2))
                {
                    return true;
                }
            }
            else // If not filled, check proximity to the outline segments
            {
                float effectiveHitRange = hitThresholdOrEraserRadius + (this.Thickness / 2f);
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, relativeApex, relativeBasePoint1) <= effectiveHitRange) return true;
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, relativeApex, relativeBasePoint2) <= effectiveHitRange) return true;
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, relativeBasePoint1, relativeBasePoint2) <= effectiveHitRange) return true; // Hit on the base line
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newCone = new DrawableCone(this.ApexRelative, this.Color, this.Thickness, this.IsFilled)
            {
                BaseCenterRelative = this.BaseCenterRelative
            };
            CopyBasePropertiesTo(newCone);
            return newCone;
        }

        public override void Translate(Vector2 delta)
        {
            this.ApexRelative += delta;
            this.BaseCenterRelative += delta;
        }
    }
}