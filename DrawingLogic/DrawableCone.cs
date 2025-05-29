using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility;

namespace AetherDraw.DrawingLogic
{
    public class DrawableCone : BaseDrawable
    {
        public Vector2 ApexRelative { get; set; } // Logical, unscaled
        // BaseCenterRelative defines the length and unrotated direction of the cone's axis FROM the Apex.
        public Vector2 BaseCenterRelative { get; set; } // Logical, unscaled
        public float RotationAngle { get; set; } = 0f;

        public static readonly float ConeWidthFactor = 0.3f; // Proportional, so no scaling needed itself

        public DrawableCone(Vector2 apexRelative, Vector4 color, float unscaledThickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Cone;
            this.ApexRelative = apexRelative;
            this.BaseCenterRelative = apexRelative; // Initially, base is at apex
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newBaseCenterRelativeWhileDrawing)
        {
            this.BaseCenterRelative = newBaseCenterRelativeWhileDrawing;
        }

        // Gets vertices in local space (Apex at 0,0), unrotated, logical units
        private (Vector2 localApex, Vector2 localBaseVert1, Vector2 localBaseVert2, Vector2 localBaseCenterFromApex) GetLocalUnrotatedVertices()
        {
            Vector2 localBaseCenterFromApexVec = BaseCenterRelative - ApexRelative;
            float height = localBaseCenterFromApexVec.Length();
            if (height < 0.1f) return (Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero);

            Vector2 direction = Vector2.Normalize(localBaseCenterFromApexVec);
            float baseHalfWidth = height * ConeWidthFactor;

            Vector2 perp = new Vector2(direction.Y, -direction.X);
            Vector2 localBaseV1 = localBaseCenterFromApexVec + perp * baseHalfWidth;
            Vector2 localBaseV2 = localBaseCenterFromApexVec - perp * baseHalfWidth;

            return (Vector2.Zero, localBaseV1, localBaseV2, localBaseCenterFromApexVec);
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);
            float scaledThickness = Math.Max(1f, Thickness * ImGuiHelpers.GlobalScale);
            if (IsSelected || IsHovered) scaledThickness += 2f * ImGuiHelpers.GlobalScale;

            var (la, lb1, lb2, _) = GetLocalUnrotatedVertices(); // These are offsets from Apex
            if (lb1 == Vector2.Zero && lb2 == Vector2.Zero && IsPreview)
            {
                drawList.AddCircleFilled(ApexRelative * ImGuiHelpers.GlobalScale + canvasOriginScreen, scaledThickness / 2f + (1f * ImGuiHelpers.GlobalScale), displayColor);
                return;
            }

            Matrix3x2 transform = Matrix3x2.CreateRotation(RotationAngle) *
                                  Matrix3x2.CreateTranslation(ApexRelative) *
                                  Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                                  Matrix3x2.CreateTranslation(canvasOriginScreen);

            Vector2 screenApex = Vector2.Transform(la, transform); // la is (0,0)
            Vector2 screenB1 = Vector2.Transform(lb1, transform);
            Vector2 screenB2 = Vector2.Transform(lb2, transform);

            if (IsFilled)
            {
                drawList.AddTriangleFilled(screenApex, screenB1, screenB2, displayColor);
            }
            else
            {
                drawList.AddLine(screenApex, screenB1, displayColor, scaledThickness);
                drawList.AddLine(screenApex, screenB2, displayColor, scaledThickness);
                drawList.AddLine(screenB1, screenB2, displayColor, scaledThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            // Transform query point to cone's local unrotated space (Apex at origin)
            Vector2 localQueryPoint = Vector2.Transform(queryPointCanvasRelative - ApexRelative, Matrix3x2.CreateRotation(-RotationAngle));
            var (_, localB1, localB2, localBaseCenter) = GetLocalUnrotatedVertices();

            if (localBaseCenter.LengthSquared() < 0.01f) // Degenerate cone (point)
            {
                return Vector2.DistanceSquared(localQueryPoint, Vector2.Zero) < (unscaledHitThreshold * unscaledHitThreshold);
            }

            if (IsFilled)
            {
                return HitDetection.PointInTriangle(localQueryPoint, Vector2.Zero, localB1, localB2);
            }
            else // Outline
            {
                float effectiveDist = unscaledHitThreshold + Thickness / 2f;
                if (HitDetection.DistancePointToLineSegment(localQueryPoint, Vector2.Zero, localB1) <= effectiveDist) return true;
                if (HitDetection.DistancePointToLineSegment(localQueryPoint, Vector2.Zero, localB2) <= effectiveDist) return true;
                if (HitDetection.DistancePointToLineSegment(localQueryPoint, localB1, localB2) <= effectiveDist) return true;
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newCone = new DrawableCone(ApexRelative, Color, Thickness, IsFilled)
            {
                BaseCenterRelative = BaseCenterRelative,
                RotationAngle = RotationAngle
            };
            CopyBasePropertiesTo(newCone);
            return newCone;
        }

        public override void Translate(Vector2 delta) // logical
        {
            ApexRelative += delta;
            BaseCenterRelative += delta;
        }

        public void SetApex(Vector2 newApexLogical)
        {
            Vector2 diff = newApexLogical - ApexRelative;
            ApexRelative = newApexLogical;
            BaseCenterRelative += diff; // Keep base relative to apex
        }

        public void SetBaseCenter(Vector2 newBaseCenterLogical)
        {
            BaseCenterRelative = newBaseCenterLogical;
        }
    }
}
