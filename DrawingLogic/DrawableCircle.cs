// In AetherDraw/DrawingLogic/DrawableCircle.cs
using System; // For Math.Abs
using System.Numerics;
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawableCircle : BaseDrawable
    {
        public Vector2 CenterRelative { get; set; }
        public float Radius { get; set; }

        public DrawableCircle(Vector2 centerRelative, Vector4 color, float thickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Circle;
            this.CenterRelative = centerRelative;
            this.Radius = 0f; // Radius will be determined during UpdatePreview
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 pointRelative)
        {
            // Radius is the distance from the fixed center to the current mouse point
            this.Radius = Vector2.Distance(this.CenterRelative, pointRelative);
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            // Avoid drawing a tiny dot if the radius is very small during preview,
            // unless it's no longer a preview (meaning it was intentionally drawn small).
            if (this.Radius < 0.5f && this.IsPreview) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var displayThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            Vector2 screenCenter = this.CenterRelative + canvasOriginScreen;
            int numSegments = 0; // Use 0 for ImGui to auto-determine the number of segments for smoothness

            if (this.IsFilled)
            {
                drawList.AddCircleFilled(screenCenter, this.Radius, displayColor, numSegments);
            }
            else
            {
                drawList.AddCircle(screenCenter, this.Radius, displayColor, numSegments, displayThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            // If hitThreshold is large (like an eraser radius), perform a circle-circle intersection check
            // This condition (2.1f) was in the original code.
            if (hitThreshold > (this.Thickness / 2f + 2.1f))
            {
                return HitDetection.IntersectCircleCircle(this.CenterRelative, this.Radius, queryPointRelative, hitThreshold);
            }

            float distanceToCenter = Vector2.Distance(queryPointRelative, this.CenterRelative);

            if (this.IsFilled)
            {
                // For filled circles, check if the point is within the radius.
                // The original code added 'hitThreshold' here, making filled circles easier to hit.
                return distanceToCenter <= this.Radius + hitThreshold;
            }
            else
            {
                // For outlined circles, check if the point is close to the circumference.
                // The distance from the point to the circumference must be within the hitThreshold + half thickness.
                return Math.Abs(distanceToCenter - this.Radius) <= hitThreshold + (this.Thickness / 2f);
            }
        }

        public override BaseDrawable Clone()
        {
            var newCircle = new DrawableCircle(this.CenterRelative, this.Color, this.Thickness, this.IsFilled)
            {
                Radius = this.Radius // Ensure the radius is also cloned
            };
            CopyBasePropertiesTo(newCircle);
            return newCircle;
        }

        public override void Translate(Vector2 delta)
        {
            this.CenterRelative += delta;
        }
    }
}