using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility; // Added for ImGuiHelpers

namespace AetherDraw.DrawingLogic
{
    public class DrawableCircle : BaseDrawable
    {
        public Vector2 CenterRelative { get; set; }
        public float Radius { get; set; }

        // Constructor: Initializes a new circle.
        public DrawableCircle(Vector2 centerRelative, Vector4 color, float unscaledThickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Circle;
            this.CenterRelative = centerRelative;
            this.Radius = 0f; // Radius will be determined during UpdatePreview.
            this.Color = color;
            this.Thickness = unscaledThickness; // Store unscaled thickness.
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        // Updates the radius of the circle during preview (e.g., while dragging).
        public override void UpdatePreview(Vector2 pointRelative)
        {
            this.Radius = Vector2.Distance(this.CenterRelative, pointRelative);
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            // Avoid drawing if the radius is very small during preview unless intentionally drawn small.
            // 0.5f is a small logical threshold, scaling it ensures it's visually consistent.
            if (this.Radius < 0.5f && this.IsPreview) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            // Scale base thickness and selection/hover highlight.
            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness); // Ensure minimum visible thickness.

            Vector2 screenCenter = this.CenterRelative + canvasOriginScreen;
            // ImGui determines segment count automatically if 0.
            int numSegments = (int)(this.Radius * ImGuiHelpers.GlobalScale / 2f); // Proportional segments, clamped
            numSegments = Math.Clamp(numSegments, 12, 128);


            if (this.IsFilled)
            {
                drawList.AddCircleFilled(screenCenter, this.Radius * ImGuiHelpers.GlobalScale, displayColor, numSegments);
            }
            else
            {
                drawList.AddCircle(screenCenter, this.Radius * ImGuiHelpers.GlobalScale, displayColor, numSegments, displayScaledThickness);
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            float scaledRadius = this.Radius * ImGuiHelpers.GlobalScale;
            float scaledHitThreshold = unscaledHitThreshold * ImGuiHelpers.GlobalScale;
            float scaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            // The 2.1f constant, representing an additional proximity margin for eraser-like interactions, should also be scaled.
            float scaledEraserProximityFactor = 2.1f * ImGuiHelpers.GlobalScale;

            // For eraser or large hit thresholds, perform a circle-circle intersection check.
            if (scaledHitThreshold > (scaledThickness / 2f + scaledEraserProximityFactor))
            {
                // HitDetection.IntersectCircleCircle expects world-space radii.
                return HitDetection.IntersectCircleCircle(this.CenterRelative, this.Radius, queryPointRelative, unscaledHitThreshold);
            }

            float distanceToCenter = Vector2.Distance(queryPointRelative, this.CenterRelative);

            if (this.IsFilled)
            {
                // For filled circles, check if the point is within the radius (plus hit threshold for easier selection).
                return distanceToCenter <= this.Radius + unscaledHitThreshold;
            }
            else
            {
                // For outlined circles, check if the point is close to the circumference.
                // Compare distance to the logical radius, and use scaled thresholds.
                return MathF.Abs(distanceToCenter - this.Radius) <= unscaledHitThreshold + (this.Thickness / 2f);
            }
        }


        public override BaseDrawable Clone()
        {
            var newCircle = new DrawableCircle(this.CenterRelative, this.Color, this.Thickness, this.IsFilled) // Pass unscaled thickness.
            {
                Radius = this.Radius // Radius is a logical value, scaling applied at draw time.
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
