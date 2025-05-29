using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility;

namespace AetherDraw.DrawingLogic
{
    public class DrawableArrow : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }
        public float RotationAngle { get; set; } = 0f;

        public float ArrowheadLengthOffset { get; set; }
        public float ArrowheadWidthScale { get; set; }

        public static readonly float DefaultArrowheadLengthFactorFromThickness = 2.5f;
        public static readonly float DefaultArrowheadWidthFactorFromThickness = 1.5f;
        public static readonly float MinArrowheadAbsoluteDim = 5f;


        public DrawableArrow(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Arrow;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative; // Initialize EndPoint to StartPoint
            this.Color = color;
            this.Thickness = Math.Max(1f, unscaledThickness);
            this.IsFilled = true;
            this.IsPreview = true;
            this.RotationAngle = 0f;

            this.ArrowheadLengthOffset = Math.Max(MinArrowheadAbsoluteDim, this.Thickness * DefaultArrowheadLengthFactorFromThickness);
            this.ArrowheadWidthScale = DefaultArrowheadWidthFactorFromThickness;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative; // Called during initial drag
        }


        public Vector2 GetShaftVectorLogical() => EndPointRelative - StartPointRelative;

        public (Vector2 visualTip, Vector2 base1, Vector2 base2) GetArrowheadGeometricPoints(
            Vector2 shaftEndPointLogical,
            Vector2 shaftDirectionLogical,
            float currentThicknessLogical
            )
        {
            float actualArrowheadLength = Math.Max(MinArrowheadAbsoluteDim, ArrowheadLengthOffset);
            float actualArrowheadHalfWidth = Math.Max(MinArrowheadAbsoluteDim / 2f, (currentThicknessLogical * ArrowheadWidthScale) / 2f);

            Vector2 visualTipPoint = shaftEndPointLogical + shaftDirectionLogical * actualArrowheadLength;

            Vector2 perpendicularOffset = new Vector2(shaftDirectionLogical.Y, -shaftDirectionLogical.X) * actualArrowheadHalfWidth;
            Vector2 basePoint1 = shaftEndPointLogical + perpendicularOffset;
            Vector2 basePoint2 = shaftEndPointLogical - perpendicularOffset;

            return (visualTipPoint, basePoint1, basePoint2);
        }


        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float scaledShaftThickness = Math.Max(1f, Thickness * ImGuiHelpers.GlobalScale);
            if (IsSelected || IsHovered) scaledShaftThickness += 2f * ImGuiHelpers.GlobalScale;


            Vector2 shaftStartLogical = StartPointRelative;
            Vector2 shaftEndLogical = EndPointRelative;
            Vector2 shaftVectorLogical = shaftEndLogical - shaftStartLogical;

            // Transformation matrix applies rotation around StartPointRelative, then translates by StartPointRelative, then scales, then moves to screen space
            Matrix3x2 transform = Matrix3x2.CreateRotation(RotationAngle) *
                                  Matrix3x2.CreateTranslation(shaftStartLogical) *
                                  Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                                  Matrix3x2.CreateTranslation(canvasOriginScreen);

            Vector2 screenShaftStart = Vector2.Transform(Vector2.Zero, transform); // StartPoint is the origin for the local part of transform
            Vector2 screenShaftEnd = Vector2.Transform(shaftVectorLogical, transform); // shaftVectorLogical is relative to StartPoint


            // If it's a preview and effectively zero length (e.g., initial click before drag), draw a small dot.
            // Otherwise, IsHit and FinalizeDrawing might cull it too soon.
            if (IsPreview && shaftVectorLogical.LengthSquared() < (0.5f * 0.5f)) // Tiny logical length
            {
                drawList.AddCircleFilled(screenShaftStart, scaledShaftThickness / 2f + (2f * ImGuiHelpers.GlobalScale), displayColor);
                return;
            }

            drawList.AddLine(screenShaftStart, screenShaftEnd, displayColor, scaledShaftThickness);

            Vector2 shaftDirLogical = shaftVectorLogical.LengthSquared() > 0.001f ? Vector2.Normalize(shaftVectorLogical) : new Vector2(0, -1); // Default up if zero length

            // GetArrowheadGeometricPoints expects shaftEnd and shaftDir relative to the shaft's own local space (where start is 0,0)
            // So, shaftVectorLogical is the end point in that local space, and shaftDirLogical is its direction.
            var (ahTipLocalToShaftEnd, ahBase1LocalToShaftEnd, ahBase2LocalToShaftEnd) = GetArrowheadGeometricPoints(
                shaftVectorLogical,
                shaftDirLogical,
                Thickness
            );

            // These points are already relative to shaft start (which is the origin for the transform matrix)
            Vector2 screenAhTip = Vector2.Transform(ahTipLocalToShaftEnd, transform);
            Vector2 screenAhBase1 = Vector2.Transform(ahBase1LocalToShaftEnd, transform);
            Vector2 screenAhBase2 = Vector2.Transform(ahBase2LocalToShaftEnd, transform);

            drawList.AddTriangleFilled(screenAhTip, screenAhBase1, screenAhBase2, displayColor);
        }

        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            Vector2 localQueryPoint = Vector2.Transform(queryPointCanvasRelative - StartPointRelative, Matrix3x2.CreateRotation(-RotationAngle));
            float effectiveHitRange = unscaledHitThreshold + (Thickness / 2f);
            Vector2 localShaftEnd = EndPointRelative - StartPointRelative;

            if (localShaftEnd.LengthSquared() < (0.5f * 0.5f)) // If effectively a point
            {
                return Vector2.DistanceSquared(localQueryPoint, Vector2.Zero) < (effectiveHitRange * effectiveHitRange);
            }

            if (HitDetection.DistancePointToLineSegment(localQueryPoint, Vector2.Zero, localShaftEnd) <= effectiveHitRange) return true;
            Vector2 shaftDirLocal = localShaftEnd.LengthSquared() > 0.001f ? Vector2.Normalize(localShaftEnd) : new Vector2(0, -1);
            var (ahTip, ahB1, ahB2) = GetArrowheadGeometricPoints(localShaftEnd, shaftDirLocal, Thickness);
            if (HitDetection.PointInTriangle(localQueryPoint, ahTip, ahB1, ahB2)) return true;

            float edgeProximity = unscaledHitThreshold + Thickness * 0.25f; // Smaller proximity for edges
            if (HitDetection.DistancePointToLineSegment(localQueryPoint, ahTip, ahB1) <= edgeProximity) return true;
            if (HitDetection.DistancePointToLineSegment(localQueryPoint, ahTip, ahB2) <= edgeProximity) return true;
            if (HitDetection.DistancePointToLineSegment(localQueryPoint, ahB1, ahB2) <= edgeProximity) return true;

            return false;
        }


        public override BaseDrawable Clone() { /* ... unchanged ... */ return new DrawableArrow(StartPointRelative, Color, Thickness); }
        public override void Translate(Vector2 delta) { StartPointRelative += delta; EndPointRelative += delta; }
        public void SetStartPoint(Vector2 newStartLogical) { Vector2 diff = newStartLogical - StartPointRelative; StartPointRelative = newStartLogical; EndPointRelative += diff; }
        public void SetEndPoint(Vector2 newEndLogical) { EndPointRelative = newEndLogical; }
    }
}
