using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility;
using System;

namespace AetherDraw.DrawingLogic
{
    public class DrawableStraightLine : BaseDrawable
    {
        public Vector2 StartPointRelative { get; set; }
        public Vector2 EndPointRelative { get; set; }

        // Constructor: Initializes a new straight line.
        public DrawableStraightLine(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.StraightLine;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative; // End point is same as start initially.
            this.Color = color;
            this.Thickness = unscaledThickness; // Store unscaled thickness.
            this.IsFilled = false; // Lines are not "filled".
            this.IsPreview = true;
        }

        // Updates the end point of the line during preview (e.g., while dragging).
        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            // Determine display color based on selection/hover state.
            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            // Scale base thickness and selection/hover highlight.
            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness); // Ensure minimum visible thickness.

            Vector2 screenStart = this.StartPointRelative + canvasOriginScreen;
            Vector2 screenEnd = this.EndPointRelative + canvasOriginScreen;

            drawList.AddLine(screenStart, screenEnd, displayColor, displayScaledThickness);
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            float scaledHitThreshold = unscaledHitThreshold * ImGuiHelpers.GlobalScale;
            float scaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float effectiveHitRange = scaledHitThreshold + (scaledThickness / 2f);

            return HitDetection.DistancePointToLineSegment(queryPointRelative, this.StartPointRelative, this.EndPointRelative) <= effectiveHitRange;
        }

        public override BaseDrawable Clone()
        {
            var newLine = new DrawableStraightLine(this.StartPointRelative, this.Color, this.Thickness) // Pass unscaled thickness.
            {
                EndPointRelative = this.EndPointRelative
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
