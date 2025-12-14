using System;
using System.Drawing; // Required for RectangleF
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace AetherDraw.DrawingLogic
{
    public class DrawablePie : BaseDrawable
    {
        // Renamed to match Serializer expectations
        public Vector2 CenterRelative { get; set; }
        public float Radius { get; set; } = 50f;
        public float RotationAngle { get; set; } = 0f; // Radians
        public float SweepAngle { get; set; } = 1.0f;  // Radians

        public DrawablePie(Vector2 center, Vector4 color, float thickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Pie;
            this.CenterRelative = center;
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 currentPointRelative)
        {
            Vector2 diff = currentPointRelative - CenterRelative;

            // 1. Update Radius based on distance from center
            this.Radius = Math.Max(1f, diff.Length());

            // 2. Update Rotation so the wedge centers on the cursor
            float angleToCursor = MathF.Atan2(diff.Y, diff.X);

            // Offset the rotation so the cursor is in the middle of the sweep
            this.RotationAngle = angleToCursor - (this.SweepAngle / 2f);
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);
            float scale = ImGuiHelpers.GlobalScale;
            float scaledThickness = Math.Max(1f, Thickness * scale);
            if (IsSelected || IsHovered) scaledThickness += 2f * scale;

            Vector2 screenCenter = (CenterRelative * scale) + canvasOriginScreen;
            float scaledRadius = Radius * scale;

            drawList.PathLineTo(screenCenter);
            drawList.PathArcTo(screenCenter, scaledRadius, RotationAngle, RotationAngle + SweepAngle);
            drawList.PathLineTo(screenCenter);

            if (IsFilled)
                drawList.PathFillConvex(displayColor);
            else
                drawList.PathStroke(displayColor, ImDrawFlags.None, scaledThickness);
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            // ImageSharp logic to be implemented
        }

        // Explicitly use System.Drawing.RectangleF to fix ambiguity
        public override System.Drawing.RectangleF GetBoundingBox()
        {
            return new System.Drawing.RectangleF(CenterRelative.X - Radius, CenterRelative.Y - Radius, Radius * 2, Radius * 2);
        }

        public override bool IsHit(Vector2 queryPoint, float threshold = 5.0f)
        {
            return HitDetection.IsPointInCircularSector(queryPoint, CenterRelative, Radius, RotationAngle, SweepAngle);
        }

        public override BaseDrawable Clone()
        {
            var newPie = new DrawablePie(this.CenterRelative, this.Color, this.Thickness, this.IsFilled)
            {
                Radius = this.Radius,
                RotationAngle = this.RotationAngle,
                SweepAngle = this.SweepAngle
            };
            CopyBasePropertiesTo(newPie);
            return newPie;
        }

        public override void Translate(Vector2 delta)
        {
            CenterRelative += delta;
        }
    }
}
