using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Dalamud.Interface.Utility;

namespace AetherDraw.DrawingLogic
{
    public class DrawableDash : BaseDrawable
    {
        public List<Vector2> PointsRelative { get; set; } = new List<Vector2>();
        // Store DashLength and GapLength as logical, unscaled values.
        public float DashLength { get; set; }
        public float GapLength { get; set; }

        // Constructor: Initializes a new dashed line.
        public DrawableDash(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Dash;
            if (startPointRelative != default || !PointsRelative.Any())
            {
                PointsRelative.Add(startPointRelative);
            }
            this.Color = color;
            this.Thickness = unscaledThickness; // Store unscaled thickness.
            this.IsFilled = false; // Dashes are not "filled".
            this.IsPreview = true;

            // Initialize logical DashLength and GapLength based on unscaled thickness.
            // These factors determine the appearance relative to the thickness.
            this.DashLength = MathF.Max(5f, unscaledThickness * 2.5f);
            this.GapLength = MathF.Max(3f, unscaledThickness * 1.25f);
        }

        // Adds a point to the dashed line path.
        public void AddPoint(Vector2 pointRelative)
        {
            // 4.0f is a logical squared distance threshold, doesn't need scaling.
            if (!PointsRelative.Any() || Vector2.DistanceSquared(PointsRelative.Last(), pointRelative) > 4.0f)
            {
                PointsRelative.Add(pointRelative);
            }
            else if (PointsRelative.Any() && this.IsPreview)
            {
                PointsRelative[PointsRelative.Count - 1] = pointRelative;
            }
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (!PointsRelative.Any()) return;

            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = MathF.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness);

            // If only one point and in preview, draw a small circle for feedback.
            if (PointsRelative.Count == 1 && this.IsPreview)
            {
                var previewColorVec = this.IsSelected || this.IsHovered ? new Vector4(1, 1, 0, 1) : this.Color;
                uint previewColor = ImGui.GetColorU32(previewColorVec);
                drawList.AddCircleFilled(PointsRelative[0] + canvasOriginScreen, displayScaledThickness / 2f + (1f * ImGuiHelpers.GlobalScale), previewColor);
                return;
            }

            if (PointsRelative.Count < 2) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            // Scale dash and gap lengths for drawing.
            float scaledDashLength = this.DashLength * ImGuiHelpers.GlobalScale;
            float scaledGapLength = this.GapLength * ImGuiHelpers.GlobalScale;

            // Ensure minimum positive lengths for scaled dashes/gaps to avoid issues.
            if (scaledDashLength <= 0) scaledDashLength = 1f * ImGuiHelpers.GlobalScale;
            if (scaledGapLength <= 0) scaledGapLength = 1f * ImGuiHelpers.GlobalScale;


            float currentDistanceIntoPatternElement = 0f;
            bool isCurrentlyDrawingDash = true;

            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                Vector2 screenP1 = PointsRelative[i] + canvasOriginScreen;
                Vector2 screenP2 = PointsRelative[i + 1] + canvasOriginScreen;

                Vector2 segmentVector = screenP2 - screenP1;
                float segmentLength = segmentVector.Length();

                // 0.01f is a very small logical length, scaling it ensures it's not too small visually.
                if (segmentLength < 0.01f * ImGuiHelpers.GlobalScale) continue;

                Vector2 segmentDirection = segmentVector / segmentLength;
                float distanceCoveredOnSegment = 0f;

                while (distanceCoveredOnSegment < segmentLength)
                {
                    if (isCurrentlyDrawingDash)
                    {
                        float remainingLengthInCurrentDash = scaledDashLength - currentDistanceIntoPatternElement;
                        float lengthToDrawForThisDashPart = MathF.Min(remainingLengthInCurrentDash, segmentLength - distanceCoveredOnSegment);

                        Vector2 dashPartStart = screenP1 + segmentDirection * distanceCoveredOnSegment;
                        Vector2 dashPartEnd = dashPartStart + segmentDirection * lengthToDrawForThisDashPart;
                        drawList.AddLine(dashPartStart, dashPartEnd, displayColor, displayScaledThickness);

                        currentDistanceIntoPatternElement += lengthToDrawForThisDashPart;
                        distanceCoveredOnSegment += lengthToDrawForThisDashPart;

                        // Use a small epsilon for float comparison, scaled if needed.
                        if (currentDistanceIntoPatternElement >= scaledDashLength - (0.01f * ImGuiHelpers.GlobalScale))
                        {
                            isCurrentlyDrawingDash = false;
                            currentDistanceIntoPatternElement = 0;
                        }
                    }
                    else // Currently in a gap.
                    {
                        float remainingLengthInCurrentGap = scaledGapLength - currentDistanceIntoPatternElement;
                        float lengthToAdvanceForThisGapPart = MathF.Min(remainingLengthInCurrentGap, segmentLength - distanceCoveredOnSegment);

                        currentDistanceIntoPatternElement += lengthToAdvanceForThisGapPart;
                        distanceCoveredOnSegment += lengthToAdvanceForThisGapPart;

                        if (currentDistanceIntoPatternElement >= scaledGapLength - (0.01f * ImGuiHelpers.GlobalScale))
                        {
                            isCurrentlyDrawingDash = true;
                            currentDistanceIntoPatternElement = 0;
                        }
                    }
                }
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            if (PointsRelative.Count < 2) return false;

            float scaledHitThreshold = unscaledHitThreshold * ImGuiHelpers.GlobalScale;
            float scaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float effectiveHitRange = scaledHitThreshold + (scaledThickness / 2f);

            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, PointsRelative[i], PointsRelative[i + 1]) <= effectiveHitRange)
                {
                    return true;
                }
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            // Pass unscaled thickness. DashLength and GapLength are recalculated in constructor based on it.
            var newDash = new DrawableDash(PointsRelative.FirstOrDefault(), this.Color, this.Thickness)
            {
                PointsRelative = new List<Vector2>(this.PointsRelative),
                // DashLength and GapLength are set in constructor based on unscaled thickness.
            };
            CopyBasePropertiesTo(newDash);
            return newDash;
        }

        public override void Translate(Vector2 delta)
        {
            for (int i = 0; i < PointsRelative.Count; i++)
            {
                PointsRelative[i] += delta;
            }
        }
    }
}
