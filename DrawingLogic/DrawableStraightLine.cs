// In AetherDraw/DrawingLogic/DrawableStraightLine.cs
using System.Numerics;
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawableStraightLine : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }

        public DrawableStraightLine(Vector2 startPointRelative, Vector4 color, float thickness)
        {
            this.ObjectDrawMode = DrawMode.StraightLine;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative; // End point is same as start initially
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = false; // Lines are not "filled"
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            // Called when dragging to draw/extend the line
            this.EndPointRelative = newPointRelative;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var displayThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            Vector2 screenStart = this.StartPointRelative + canvasOriginScreen;
            Vector2 screenEnd = this.EndPointRelative + canvasOriginScreen;

            drawList.AddLine(screenStart, screenEnd, displayColor, displayThickness);
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            // Use the static HitDetection class
            return HitDetection.DistancePointToLineSegment(queryPointRelative, this.StartPointRelative, this.EndPointRelative) <= hitThreshold + (this.Thickness / 2f);
        }

        public override BaseDrawable Clone()
        {
            var newLine = new DrawableStraightLine(this.StartPointRelative, this.Color, this.Thickness)
            {
                EndPointRelative = this.EndPointRelative // Ensure the end point is also cloned
            };
            CopyBasePropertiesTo(newLine);
            return newLine;
        }

        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }
    }
}