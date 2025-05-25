using System; // For MathF
using System.Numerics;
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawableCone : BaseDrawable
    {
        public Vector2 ApexRelative { get; set; }
        // BaseCenterRelative defines the length and unrotated direction of the cone's axis from the Apex.
        public Vector2 BaseCenterRelative { get; set; }
        public float RotationAngle { get; set; } = 0f; // Rotation in radians around ApexRelative

        // Factor determining the cone's width relative to its height.
        public static readonly float ConeWidthFactor = 0.3f;


        public DrawableCone(Vector2 apexRelative, Vector4 color, float thickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Cone;
            this.ApexRelative = apexRelative;
            this.BaseCenterRelative = apexRelative; // Initially, base center is at the apex
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
            this.RotationAngle = 0f;
        }

        public override void UpdatePreview(Vector2 newBaseCenterRelativeWhileDrawing)
        {
            // This point is typically the current mouse position while initially drawing the cone.
            // It defines the unrotated length and direction of the cone.
            this.BaseCenterRelative = newBaseCenterRelativeWhileDrawing;
        }

        // Helper to get the cone's vertices in their local, unrotated space (Apex at 0,0)
        private (Vector2 localApex, Vector2 localBaseVert1, Vector2 localBaseVert2, Vector2 localBaseCenter) GetLocalUnrotatedVertices()
        {
            Vector2 localApex = Vector2.Zero;
            Vector2 localBaseCenter = this.BaseCenterRelative - this.ApexRelative; // Vector from apex to base-center in local space

            float height = localBaseCenter.Length();
            if (height < 0.1f) // If very small, treat as a point for geometry calculation
            {
                return (localApex, localApex, localApex, localApex);
            }

            Vector2 direction = Vector2.Normalize(localBaseCenter);
            float baseHalfWidth = height * ConeWidthFactor;

            Vector2 localBaseVert1 = localBaseCenter + new Vector2(direction.Y, -direction.X) * baseHalfWidth;
            Vector2 localBaseVert2 = localBaseCenter + new Vector2(-direction.Y, direction.X) * baseHalfWidth;

            return (localApex, localBaseVert1, localBaseVert2, localBaseCenter);
        }


        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var displayThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            var (localApex, localBaseVert1, localBaseVert2, localActualBaseCenter) = GetLocalUnrotatedVertices();

            // If the cone is too small (height essentially zero)
            if (localActualBaseCenter == localApex && this.IsPreview && Vector2.DistanceSquared(this.ApexRelative, this.BaseCenterRelative) < 1.0f)
            {
                // drawList.AddCircleFilled(this.ApexRelative + canvasOriginScreen, displayThickness / 2f + 1f, displayColor);
                return;
            }

            float cosA = MathF.Cos(this.RotationAngle);
            float sinA = MathF.Sin(this.RotationAngle);

            // Rotate local vertices around localApex (0,0)
            // Apex remains localApex (0,0) after rotation around itself
            Vector2 rotatedLocalBaseVert1 = HitDetection.ImRotate(localBaseVert1, cosA, sinA);
            Vector2 rotatedLocalBaseVert2 = HitDetection.ImRotate(localBaseVert2, cosA, sinA);

            // Translate rotated local vertices to world (canvas-relative) space by adding ApexRelative
            Vector2 screenApex = this.ApexRelative + canvasOriginScreen;
            Vector2 screenBaseVert1 = this.ApexRelative + rotatedLocalBaseVert1 + canvasOriginScreen;
            Vector2 screenBaseVert2 = this.ApexRelative + rotatedLocalBaseVert2 + canvasOriginScreen;

            if (this.IsFilled)
            {
                drawList.AddTriangleFilled(screenApex, screenBaseVert1, screenBaseVert2, displayColor);
            }
            else
            {
                drawList.AddLine(screenApex, screenBaseVert1, displayColor, displayThickness);
                drawList.AddLine(screenApex, screenBaseVert2, displayColor, displayThickness);
                drawList.AddLine(screenBaseVert1, screenBaseVert2, displayColor, displayThickness); // Draw the base line
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThresholdOrEraserRadius = 5.0f)
        {
            // Transform queryPointRelative into the cone's local, unrotated space (where Apex is at origin)
            Vector2 localQueryPoint = queryPointRelative - this.ApexRelative; // Translate query point so cone's apex is at origin
            float cosNegA = MathF.Cos(-this.RotationAngle);
            float sinNegA = MathF.Sin(-this.RotationAngle);
            Vector2 unrotatedLocalQueryPoint = HitDetection.ImRotate(localQueryPoint, cosNegA, sinNegA); // Inverse rotate query point

            var (localApex, localBaseVert1, localBaseVert2, localBaseCenter) = GetLocalUnrotatedVertices();

            // If cone has no real area (e.g. height is near zero)
            if (localBaseCenter == localApex)
            {
                return Vector2.DistanceSquared(unrotatedLocalQueryPoint, localApex) < hitThresholdOrEraserRadius * hitThresholdOrEraserRadius;
            }

            // Perform hit detection in local, unrotated space
            if (hitThresholdOrEraserRadius > (this.Thickness / 2f + 2.1f))
            {
                return HitDetection.IntersectCircleTriangle(unrotatedLocalQueryPoint, hitThresholdOrEraserRadius, localApex, localBaseVert1, localBaseVert2);
            }

            if (this.IsFilled)
            {
                return HitDetection.PointInTriangle(unrotatedLocalQueryPoint, localApex, localBaseVert1, localBaseVert2);
            }
            else
            {
                float effectiveHitRange = hitThresholdOrEraserRadius + (this.Thickness / 2f);
                if (HitDetection.DistancePointToLineSegment(unrotatedLocalQueryPoint, localApex, localBaseVert1) <= effectiveHitRange) return true;
                if (HitDetection.DistancePointToLineSegment(unrotatedLocalQueryPoint, localApex, localBaseVert2) <= effectiveHitRange) return true;
                if (HitDetection.DistancePointToLineSegment(unrotatedLocalQueryPoint, localBaseVert1, localBaseVert2) <= effectiveHitRange) return true;
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newCone = new DrawableCone(this.ApexRelative, this.Color, this.Thickness, this.IsFilled)
            {
                BaseCenterRelative = this.BaseCenterRelative,
                RotationAngle = this.RotationAngle // Clone rotation angle
            };
            CopyBasePropertiesTo(newCone); // This sets IsPreview to false, IsSelected to false etc.
            return newCone;
        }

        public override void Translate(Vector2 delta)
        {
            // Translating the cone means moving its defining points. Rotation is maintained relative to these.
            this.ApexRelative += delta;
            this.BaseCenterRelative += delta;
        }

        // --- New methods for direct manipulation (to be called from MainWindow interaction logic) ---

        public void RotateBy(float angleDeltaInRadians)
        {
            this.RotationAngle += angleDeltaInRadians;
            // Normalize angle if desired, e.g., to keep it within 0 to 2*PI
            // this.RotationAngle = this.RotationAngle % (2 * MathF.PI);
        }

        public void SetApex(Vector2 newApex)
        {
            Vector2 diff = newApex - this.ApexRelative;
            this.ApexRelative = newApex;
            this.BaseCenterRelative += diff; // Keep BaseCenter relative to Apex unless explicitly moved otherwise
        }

        public void SetBaseCenter(Vector2 newBaseCenter)
        {
            this.BaseCenterRelative = newBaseCenter;
        }

        // For resizing, you'd typically manipulate ApexRelative or BaseCenterRelative directly,
        // or define specific resize handles.
        // Example: To change length while keeping apex fixed and direction fixed:
        public void SetLength(float newLength)
        {
            if (newLength < 0) newLength = 0;
            Vector2 axis = this.BaseCenterRelative - this.ApexRelative;
            if (axis.LengthSquared() < 0.001f)
            { // if zero length, define a default direction e.g. downwards
                axis = new Vector2(0, 1);
            }
            this.BaseCenterRelative = this.ApexRelative + Vector2.Normalize(axis) * newLength;
        }
    }
}
