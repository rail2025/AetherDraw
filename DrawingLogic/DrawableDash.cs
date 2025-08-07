// AetherDraw/DrawingLogic/DrawableDash.cs
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Drawing; // Required for RectangleF

// ImageSharp using statements
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace AetherDraw.DrawingLogic
{
    public class DrawableDash : BaseDrawable
    {
        // List of logical, unscaled points defining the path of the dashed line.
        public List<Vector2> PointsRelative { get; set; } = new List<Vector2>();
        // Logical, unscaled length of each dash segment.
        public float DashLength { get; set; }
        // Logical, unscaled length of each gap between dashes.
        public float GapLength { get; set; }

        public DrawableDash(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Dash;
            if (startPointRelative != default || !PointsRelative.Any())
            {
                PointsRelative.Add(startPointRelative);
            }
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = false;
            this.IsPreview = true;

            this.DashLength = MathF.Max(5f, unscaledThickness * 2.5f);
            this.GapLength = MathF.Max(3f, unscaledThickness * 1.25f);
        }

        // Adds a point to the dashed line path during preview or finalization.
        public void AddPoint(Vector2 pointRelative)
        {
            // Add if list is empty or if new point is sufficiently far from the last.
            if (!PointsRelative.Any() || Vector2.DistanceSquared(PointsRelative.Last(), pointRelative) > 4.0f) // 4.0f is a logical squared distance
            {
                PointsRelative.Add(pointRelative);
            }
            // If it's a preview and points exist, update the last point.
            else if (PointsRelative.Any() && this.IsPreview)
            {
                PointsRelative[PointsRelative.Count - 1] = pointRelative;
            }
        }

        // Draws the dashed line on the ImGui canvas.
        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (!PointsRelative.Any()) return;

            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = Math.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness);

            if (PointsRelative.Count == 1 && this.IsPreview)
            {
                var previewColorVec = this.IsSelected || this.IsHovered ? new Vector4(1, 1, 0, 1) : this.Color;
                uint previewColor = ImGui.GetColorU32(previewColorVec);
                Vector2 screenPoint = PointsRelative[0] * ImGuiHelpers.GlobalScale + canvasOriginScreen; // Apply scale
                drawList.AddCircleFilled(screenPoint, displayScaledThickness / 2f + (1f * ImGuiHelpers.GlobalScale), previewColor);
                return;
            }

            if (PointsRelative.Count < 2) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float scaledDashLength = this.DashLength * ImGuiHelpers.GlobalScale;
            float scaledGapLength = this.GapLength * ImGuiHelpers.GlobalScale;

            if (scaledDashLength <= 0) scaledDashLength = 1f * ImGuiHelpers.GlobalScale;
            if (scaledGapLength <= 0) scaledGapLength = 1f * ImGuiHelpers.GlobalScale;

            float currentDistanceIntoPatternElement = 0f;
            bool isCurrentlyDrawingDash = true;

            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                // Scale points for ImGui drawing
                Vector2 screenP1 = PointsRelative[i] * ImGuiHelpers.GlobalScale + canvasOriginScreen;
                Vector2 screenP2 = PointsRelative[i + 1] * ImGuiHelpers.GlobalScale + canvasOriginScreen;

                Vector2 segmentVector = screenP2 - screenP1;
                float segmentLength = segmentVector.Length();

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

                        if (currentDistanceIntoPatternElement >= scaledDashLength - (0.01f * ImGuiHelpers.GlobalScale))
                        {
                            isCurrentlyDrawingDash = false;
                            currentDistanceIntoPatternElement = 0;
                        }
                    }
                    else
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

        // Draws the dashed line to an ImageSharp context for image export.
        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            if (PointsRelative.Count < 2) return;

            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            // Scale dash and gap lengths for ImageSharp rendering
            float currentScaledDashLength = this.DashLength * currentGlobalScale;
            float currentScaledGapLength = this.GapLength * currentGlobalScale;

            // Ensure minimum positive lengths for scaled dashes/gaps
            if (currentScaledDashLength <= 0.1f) currentScaledDashLength = 0.1f; // Use a small positive value
            if (currentScaledGapLength <= 0.1f) currentScaledGapLength = 0.1f;

            float currentDistanceIntoPatternElement = 0f;
            bool isCurrentlyDrawingDash = true;

            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                // Transform logical points to image coordinates
                var p1 = new SixLabors.ImageSharp.PointF(
                    (PointsRelative[i].X * currentGlobalScale) + canvasOriginInOutputImage.X,
                    (PointsRelative[i].Y * currentGlobalScale) + canvasOriginInOutputImage.Y
                );
                var p2 = new SixLabors.ImageSharp.PointF(
                    (PointsRelative[i + 1].X * currentGlobalScale) + canvasOriginInOutputImage.X,
                    (PointsRelative[i + 1].Y * currentGlobalScale) + canvasOriginInOutputImage.Y
                );

                Vector2 segmentVector = new Vector2(p2.X - p1.X, p2.Y - p1.Y);
                float segmentLength = segmentVector.Length();

                if (segmentLength < 0.01f) continue; // Skip very short segments

                Vector2 segmentDirection = Vector2.Normalize(segmentVector);
                float distanceCoveredOnSegment = 0f;

                while (distanceCoveredOnSegment < segmentLength)
                {
                    if (isCurrentlyDrawingDash)
                    {
                        float remainingLengthInCurrentDash = currentScaledDashLength - currentDistanceIntoPatternElement;
                        float lengthToDrawForThisDashPart = MathF.Min(remainingLengthInCurrentDash, segmentLength - distanceCoveredOnSegment);

                        var dashPartStart = new SixLabors.ImageSharp.PointF(
                            p1.X + segmentDirection.X * distanceCoveredOnSegment,
                            p1.Y + segmentDirection.Y * distanceCoveredOnSegment
                        );
                        var dashPartEnd = new SixLabors.ImageSharp.PointF(
                            dashPartStart.X + segmentDirection.X * lengthToDrawForThisDashPart,
                            dashPartStart.Y + segmentDirection.Y * lengthToDrawForThisDashPart
                        );

                        var pathBuilder = new PathBuilder();
                        pathBuilder.AddLine(dashPartStart, dashPartEnd);
                        context.Draw(Pens.Solid(imageSharpColor, scaledThickness), pathBuilder.Build());

                        currentDistanceIntoPatternElement += lengthToDrawForThisDashPart;
                        distanceCoveredOnSegment += lengthToDrawForThisDashPart;

                        // Use a small epsilon for float comparison
                        if (currentDistanceIntoPatternElement >= currentScaledDashLength - 0.01f)
                        {
                            isCurrentlyDrawingDash = false;
                            currentDistanceIntoPatternElement = 0;
                        }
                    }
                    else // Currently in a gap
                    {
                        float remainingLengthInCurrentGap = currentScaledGapLength - currentDistanceIntoPatternElement;
                        float lengthToAdvanceForThisGapPart = MathF.Min(remainingLengthInCurrentGap, segmentLength - distanceCoveredOnSegment);

                        currentDistanceIntoPatternElement += lengthToAdvanceForThisGapPart;
                        distanceCoveredOnSegment += lengthToAdvanceForThisGapPart;

                        if (currentDistanceIntoPatternElement >= currentScaledGapLength - 0.01f)
                        {
                            isCurrentlyDrawingDash = true;
                            currentDistanceIntoPatternElement = 0;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calculates the axis-aligned bounding box for this dashed path.
        /// </summary>
        /// <returns>A RectangleF representing the bounding box.</returns>
        public override System.Drawing.RectangleF GetBoundingBox()
        {
            if (this.PointsRelative.Count == 0) return System.Drawing.RectangleF.Empty;

            float minX = this.PointsRelative.Min(p => p.X);
            float minY = this.PointsRelative.Min(p => p.Y);
            float maxX = this.PointsRelative.Max(p => p.X);
            float maxY = this.PointsRelative.Max(p => p.Y);

            return new System.Drawing.RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        // Performs hit detection for the dashed line in logical (unscaled) coordinates.
        public override bool IsHit(Vector2 queryPointRelative, float unscaledHitThreshold = 5.0f)
        {
            if (PointsRelative.Count < 2) return false;

            float effectiveHitRange = unscaledHitThreshold + (this.Thickness / 2f);

            for (int i = 0; i < PointsRelative.Count - 1; i++)
            {
                if (HitDetection.DistancePointToLineSegment(queryPointRelative, PointsRelative[i], PointsRelative[i + 1]) <= effectiveHitRange)
                {
                    return true;
                }
            }
            return false;
        }

        // Creates a clone of this drawable dashed line.
        public override BaseDrawable Clone()
        {
            var newDash = new DrawableDash(PointsRelative.FirstOrDefault(), this.Color, this.Thickness)
            {
                PointsRelative = new List<Vector2>(this.PointsRelative),
                DashLength = this.DashLength,
                GapLength = this.GapLength
            };
            CopyBasePropertiesTo(newDash);
            return newDash;
        }

        // Translates the dashed line by a given delta in logical coordinates.
        public override void Translate(Vector2 delta)
        {
            for (int i = 0; i < PointsRelative.Count; i++)
            {
                PointsRelative[i] += delta;
            }
        }
    }
}
