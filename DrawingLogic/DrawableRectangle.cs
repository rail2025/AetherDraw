// In AetherDraw/DrawingLogic/DrawableRectangle.cs
using System; // For Math
using System.Numerics;
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawableRectangle : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }

        public DrawableRectangle(Vector2 startPointRelative, Vector4 color, float thickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Rectangle;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative; // End point is same as start initially
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            // Called when dragging to draw/resize the rectangle
            this.EndPointRelative = newPointRelative;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var displayThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            Vector2 screenStart = this.StartPointRelative + canvasOriginScreen;
            Vector2 screenEnd = this.EndPointRelative + canvasOriginScreen;

            // Determine the top-left and bottom-right corners for ImGui drawing
            Vector2 minRectPoint = new Vector2(Math.Min(screenStart.X, screenEnd.X), Math.Min(screenStart.Y, screenEnd.Y));
            Vector2 maxRectPoint = new Vector2(Math.Max(screenStart.X, screenEnd.X), Math.Max(screenStart.Y, screenEnd.Y));

            if (this.IsFilled)
            {
                drawList.AddRectFilled(minRectPoint, maxRectPoint, displayColor, 0f, ImDrawFlags.None);
            }
            else
            {
                drawList.AddRect(minRectPoint, maxRectPoint, displayColor, 0f, ImDrawFlags.None, displayThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            Vector2 minRelative = new Vector2(Math.Min(this.StartPointRelative.X, this.EndPointRelative.X), Math.Min(this.StartPointRelative.Y, this.EndPointRelative.Y));
            Vector2 maxRelative = new Vector2(Math.Max(this.StartPointRelative.X, this.EndPointRelative.X), Math.Max(this.StartPointRelative.Y, this.EndPointRelative.Y));

            // If hitThreshold is large (like an eraser radius), check if the query area intersects the rectangle's AABB
            if (hitThreshold > (this.Thickness / 2f + 2.1f)) // This condition was in the original code
            {
                return HitDetection.IntersectCircleAABB(queryPointRelative, hitThreshold, minRelative, maxRelative);
            }

            // For point selection or small eraser
            if (this.IsFilled)
            {
                // Point is inside the rectangle
                return queryPointRelative.X >= minRelative.X && queryPointRelative.X <= maxRelative.X &&
                       queryPointRelative.Y >= minRelative.Y && queryPointRelative.Y <= maxRelative.Y;
            }
            else
            {
                // Point is on one of the borders
                float effectiveHitRange = hitThreshold + (this.Thickness / 2f);
                // Check proximity to each of the four lines forming the rectangle border
                bool onTopEdge = queryPointRelative.X >= minRelative.X - hitThreshold && queryPointRelative.X <= maxRelative.X + hitThreshold && Math.Abs(queryPointRelative.Y - minRelative.Y) < effectiveHitRange;
                bool onBottomEdge = queryPointRelative.X >= minRelative.X - hitThreshold && queryPointRelative.X <= maxRelative.X + hitThreshold && Math.Abs(queryPointRelative.Y - maxRelative.Y) < effectiveHitRange;
                bool onLeftEdge = queryPointRelative.Y >= minRelative.Y - hitThreshold && queryPointRelative.Y <= maxRelative.Y + hitThreshold && Math.Abs(queryPointRelative.X - minRelative.X) < effectiveHitRange;
                bool onRightEdge = queryPointRelative.Y >= minRelative.Y - hitThreshold && queryPointRelative.Y <= maxRelative.Y + hitThreshold && Math.Abs(queryPointRelative.X - maxRelative.X) < effectiveHitRange;

                return onTopEdge || onBottomEdge || onLeftEdge || onRightEdge;
            }
        }

        public override BaseDrawable Clone()
        {
            var newRect = new DrawableRectangle(this.StartPointRelative, this.Color, this.Thickness, this.IsFilled)
            {
                EndPointRelative = this.EndPointRelative // Ensure the end point is also cloned
            };
            CopyBasePropertiesTo(newRect);
            return newRect;
        }

        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }
    }
}