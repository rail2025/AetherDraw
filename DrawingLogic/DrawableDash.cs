using System; // For MathF
using System.Numerics;
using System.Collections.Generic;
using System.Linq; // For FirstOrDefault, Last
using ImGuiNET;

namespace AetherDraw.DrawingLogic
{
    public class DrawableDash : BaseDrawable
    {
        public List<Vector2> PointsRelative { get; set; } = new List<Vector2>();
        public float DashLength { get; set; }
        public float GapLength { get; set; }

        // Constructor: The original 'bool f' (isFilled) parameter was unused, so it's removed.
        public DrawableDash(Vector2 startPointRelative, Vector4 color, float thickness)
        {
            this.ObjectDrawMode = DrawMode.Dash;
            if (startPointRelative != default || PointsRelative.Count == 0)
            {
                PointsRelative.Add(startPointRelative);
            }
            this.Color = color;
            this.Thickness = thickness;
            this.IsFilled = false; // Dashes are inherently not "filled" in the typical sense
            this.IsPreview = true;

            // Initialize DashLength and GapLength based on thickness
            this.DashLength = MathF.Max(5f, thickness * 2.5f);
            this.GapLength = MathF.Max(3f, thickness * 1.25f);
        }

        public void AddPoint(Vector2 pointRelative)
        {
            // Add if list is empty or if new point is sufficiently far from the last
            // Original code used DistanceSquared with a threshold of 4.0f
            if (PointsRelative.Count == 0 || Vector2.DistanceSquared(PointsRelative.Last(), pointRelative) > 4.0f)
            {
                PointsRelative.Add(pointRelative);
            }
            // If it's a preview and points exist, update the last point
            else if (PointsRelative.Count > 0 && this.IsPreview)
            {
                PointsRelative[PointsRelative.Count - 1] = pointRelative;
            }
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (PointsRelative.Count == 0) return;

            // If only one point and in preview, draw a small circle for feedback
            if (PointsRelative.Count == 1 && this.IsPreview)
            {
                var previewColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
                var previewThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
                uint previewColor = ImGui.GetColorU32(previewColorVec);
                drawList.AddCircleFilled(PointsRelative[0] + canvasOriginScreen, previewThickness / 2f + 1f, previewColor);
                return;
            }

            if (PointsRelative.Count < 2) return; // Need at least two points to form segments

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            var displayThickness = this.IsSelected || this.IsHovered ? this.Thickness + 2f : this.Thickness;
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            // State for iterating through the dash-gap pattern along the polyline
            float currentDistanceIntoPatternElement = 0f; // How far into the current dash or gap we are
            bool isCurrentlyDrawingDash = true;      // Start by drawing a dash

            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                Vector2 screenP1 = PointsRelative[i] + canvasOriginScreen;
                Vector2 screenP2 = PointsRelative[i + 1] + canvasOriginScreen;

                Vector2 segmentVector = screenP2 - screenP1;
                float segmentLength = segmentVector.Length();

                if (segmentLength < 0.01f) continue; // Skip zero-length segments

                Vector2 segmentDirection = segmentVector / segmentLength;
                float distanceCoveredOnSegment = 0f;

                // Iterate along the current segment, drawing dashes and gaps
                while (distanceCoveredOnSegment < segmentLength)
                {
                    if (isCurrentlyDrawingDash)
                    {
                        float remainingLengthInCurrentDash = this.DashLength - currentDistanceIntoPatternElement;
                        float lengthToDrawForThisDashPart = MathF.Min(remainingLengthInCurrentDash, segmentLength - distanceCoveredOnSegment);

                        Vector2 dashPartStart = screenP1 + segmentDirection * distanceCoveredOnSegment;
                        Vector2 dashPartEnd = dashPartStart + segmentDirection * lengthToDrawForThisDashPart;
                        drawList.AddLine(dashPartStart, dashPartEnd, displayColor, displayThickness);

                        currentDistanceIntoPatternElement += lengthToDrawForThisDashPart;
                        distanceCoveredOnSegment += lengthToDrawForThisDashPart;

                        if (currentDistanceIntoPatternElement >= this.DashLength - 0.01f) // Tolerance for float comparison
                        {
                            isCurrentlyDrawingDash = false; // Switch to gap
                            currentDistanceIntoPatternElement = 0; // Reset for gap
                        }
                    }
                    else // Currently in a gap
                    {
                        float remainingLengthInCurrentGap = this.GapLength - currentDistanceIntoPatternElement;
                        float lengthToAdvanceForThisGapPart = MathF.Min(remainingLengthInCurrentGap, segmentLength - distanceCoveredOnSegment);

                        currentDistanceIntoPatternElement += lengthToAdvanceForThisGapPart;
                        distanceCoveredOnSegment += lengthToAdvanceForThisGapPart;

                        if (currentDistanceIntoPatternElement >= this.GapLength - 0.01f) // Tolerance
                        {
                            isCurrentlyDrawingDash = true; // Switch to dash
                            currentDistanceIntoPatternElement = 0; // Reset for dash
                        }
                    }
                }
            }
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            // For dashed lines, hit detection typically simplifies to checking against the underlying polyline path.
            // Checking against individual dash segments would be more complex and might not be desired for user interaction.
            if (PointsRelative.Count < 2) return false;

            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, PointsRelative[i], PointsRelative[i + 1]) <= hitThreshold + (this.Thickness / 2f))
                {
                    return true;
                }
            }
            return false;
        }

        public override BaseDrawable Clone()
        {
            var newDash = new DrawableDash(PointsRelative.FirstOrDefault(), this.Color, this.Thickness)
            {
                PointsRelative = new List<Vector2>(this.PointsRelative),
                // Explicitly copy DashLength and GapLength in case they could be modified
                // after construction (though the current class doesn't allow this, it's safer for cloning).
                DashLength = this.DashLength,
                GapLength = this.GapLength
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
