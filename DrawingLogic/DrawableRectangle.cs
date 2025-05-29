using System;
using System.Numerics;
using ImGuiNET;
using System.Linq;
using Dalamud.Interface.Utility;

namespace AetherDraw.DrawingLogic
{
    public class DrawableRectangle : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }
        public float RotationAngle { get; set; } = 0f;

        // Increased offset to ensure handle is outside
        public static readonly float UnscaledRotationHandleExtraOffset = 25f;

        public DrawableRectangle(Vector2 startPointRelative, Vector4 color, float unscaledThickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Rectangle;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative;
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        public (Vector2 center, Vector2 halfSize) GetGeometry()
        {
            Vector2 min = new Vector2(MathF.Min(StartPointRelative.X, EndPointRelative.X), MathF.Min(StartPointRelative.Y, EndPointRelative.Y));
            Vector2 max = new Vector2(MathF.Max(StartPointRelative.X, EndPointRelative.X), MathF.Max(StartPointRelative.Y, EndPointRelative.Y));
            Vector2 center = (min + max) / 2f;
            Vector2 halfSize = (max - min) / 2f;
            return (center, halfSize);
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = IsSelected || IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = Math.Max(1f * ImGuiHelpers.GlobalScale, baseScaledThickness + highlightThicknessAddition);

            var (center, halfSize) = GetGeometry();
            if (halfSize.X < 0.1f && halfSize.Y < 0.1f && IsPreview) return;

            Vector2[] localCorners = {
                new Vector2(-halfSize.X, -halfSize.Y), new Vector2(halfSize.X, -halfSize.Y),
                new Vector2(halfSize.X, halfSize.Y), new Vector2(-halfSize.X, halfSize.Y)
            };
            Vector2[] screenCorners = new Vector2[4];

            Matrix3x2 transform = Matrix3x2.CreateRotation(RotationAngle) *
                                  Matrix3x2.CreateTranslation(center) *
                                  Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                                  Matrix3x2.CreateTranslation(canvasOriginScreen);

            for (int i = 0; i < 4; i++)
            {
                screenCorners[i] = Vector2.Transform(localCorners[i], transform);
            }

            if (IsFilled)
            {
                drawList.AddConvexPolyFilled(ref screenCorners[0], 4, displayColor);
            }
            else
            {
                drawList.AddPolyline(ref screenCorners[0], 4, displayColor, ImDrawFlags.Closed, displayScaledThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            var (center, halfSize) = GetGeometry();
            Vector2 localQueryPoint = Vector2.Transform(queryPointCanvasRelative - center, Matrix3x2.CreateRotation(-RotationAngle));

            if (IsFilled)
            {
                return Math.Abs(localQueryPoint.X) <= halfSize.X + unscaledHitThreshold &&
                       Math.Abs(localQueryPoint.Y) <= halfSize.Y + unscaledHitThreshold;
            }
            else
            {
                float effectiveEdgeDist = unscaledHitThreshold + Thickness / 2f;
                bool withinOuter = Math.Abs(localQueryPoint.X) <= halfSize.X + effectiveEdgeDist &&
                                   Math.Abs(localQueryPoint.Y) <= halfSize.Y + effectiveEdgeDist;
                bool outsideInner = Math.Abs(localQueryPoint.X) >= halfSize.X - effectiveEdgeDist ||
                                    Math.Abs(localQueryPoint.Y) >= halfSize.Y - effectiveEdgeDist;
                return withinOuter && outsideInner;
            }
        }

        public override BaseDrawable Clone()
        {
            var newRect = new DrawableRectangle(StartPointRelative, Color, Thickness, IsFilled)
            {
                EndPointRelative = EndPointRelative,
                RotationAngle = RotationAngle
            };
            CopyBasePropertiesTo(newRect);
            return newRect;
        }

        public override void Translate(Vector2 delta)
        {
            StartPointRelative += delta;
            EndPointRelative += delta;
        }

        public Vector2[] GetRotatedCorners()
        {
            var (center, halfSize) = GetGeometry();
            Vector2[] localCorners = {
                new Vector2(-halfSize.X, -halfSize.Y), new Vector2(halfSize.X, -halfSize.Y),
                new Vector2(halfSize.X, halfSize.Y), new Vector2(-halfSize.X, halfSize.Y)
            };
            Vector2[] rotatedCorners = new Vector2[4];
            Matrix3x2 rotMatrix = Matrix3x2.CreateRotation(RotationAngle);
            for (int i = 0; i < 4; i++)
            {
                rotatedCorners[i] = Vector2.Transform(localCorners[i], rotMatrix) + center;
            }
            return rotatedCorners;
        }
    }
}
