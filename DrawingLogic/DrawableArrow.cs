// AetherDraw/DrawingLogic/DrawableArrow.cs
using System;
using System.Numerics;
using ImGuiNET; // For ImDrawListPtr in existing Draw method
using Dalamud.Interface.Utility; // For ImGuiHelpers

// ImageSharp using statements
using SixLabors.ImageSharp; // For PointF, Color, Matrix3x2 (from System.Numerics)
using SixLabors.ImageSharp.PixelFormats; // For Rgba32 if needed directly
using SixLabors.ImageSharp.Processing; // For IImageProcessingContext
using SixLabors.ImageSharp.Drawing; // For PathBuilder, Pens, IPath
using SixLabors.ImageSharp.Drawing.Processing; // For Fill, Draw extension methods

namespace AetherDraw.DrawingLogic
{
    public class DrawableArrow : BaseDrawable
    {
        // Logical, unscaled start point of the arrow shaft. Also the pivot for rotation.
        public Vector2 StartPointRelative { get; set; }
        // Logical, unscaled end point of the arrow shaft (before arrowhead extension).
        public Vector2 EndPointRelative { get; set; }
        // Rotation angle in radians around StartPointRelative.
        public float RotationAngle { get; set; } = 0f;

        // Offset determining arrowhead length from the shaft's thickness.
        public float ArrowheadLengthOffset { get; set; }
        // Scale factor determining arrowhead width from the shaft's thickness.
        public float ArrowheadWidthScale { get; set; }

        public static readonly float DefaultArrowheadLengthFactorFromThickness = 2.5f;
        public static readonly float DefaultArrowheadWidthFactorFromThickness = 1.5f;
        // Minimum absolute dimension for arrowhead components to ensure visibility.
        public static readonly float MinArrowheadAbsoluteDim = 5f;


        public DrawableArrow(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Arrow;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative;
            this.Color = color;
            this.Thickness = Math.Max(1f, unscaledThickness); // Ensure minimum thickness
            this.IsFilled = true; // Arrowhead is filled.
            this.IsPreview = true;
            this.RotationAngle = 0f;

            // Initialize arrowhead dimensions based on thickness
            this.ArrowheadLengthOffset = Math.Max(MinArrowheadAbsoluteDim, this.Thickness * DefaultArrowheadLengthFactorFromThickness);
            this.ArrowheadWidthScale = DefaultArrowheadWidthFactorFromThickness;
        }

        // Updates the arrow's end point during preview drawing.
        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        // Gets the vector representing the arrow's shaft in logical, unrotated space (from StartPoint to EndPoint).
        public Vector2 GetShaftVectorLogical() => EndPointRelative - StartPointRelative;

        // Calculates the geometric points of the arrowhead in local space relative to the shaft's end point.
        // shaftEndPointLocal: The end point of the shaft, treated as (0,0) for this calculation if shaftDirectionLocal is relative to it.
        //                     More accurately, these points are offsets from shaftEndPointLocal.
        // shaftDirectionLogical: Normalized direction of the shaft.
        // currentThicknessLogical: The logical thickness of the shaft.
        // Returns points of the arrowhead: (visualTip, baseVertex1, baseVertex2), all relative to shaftEndPointLogical.
        public (Vector2 visualTip, Vector2 base1, Vector2 base2) GetArrowheadGeometricPoints(
            Vector2 shaftEndPointLocal, // This is actually the shaft vector (end - start) in local unrotated space
            Vector2 shaftDirectionLogical,
            float currentThicknessLogical
            )
        {
            // Arrowhead dimensions are based on properties and shaft thickness
            float actualArrowheadLength = Math.Max(MinArrowheadAbsoluteDim, this.ArrowheadLengthOffset);
            float actualArrowheadHalfWidth = Math.Max(MinArrowheadAbsoluteDim / 2f, (currentThicknessLogical * this.ArrowheadWidthScale) / 2f);

            // Tip of the arrowhead extends beyond the shaft's end point
            Vector2 visualTipPoint = shaftEndPointLocal + shaftDirectionLogical * actualArrowheadLength;

            // Base vertices of the arrowhead are perpendicular to the shaft direction at the shaft's end point
            Vector2 perpendicularOffset = new Vector2(shaftDirectionLogical.Y, -shaftDirectionLogical.X) * actualArrowheadHalfWidth;
            Vector2 basePoint1 = shaftEndPointLocal + perpendicularOffset;
            Vector2 basePoint2 = shaftEndPointLocal - perpendicularOffset;

            return (visualTipPoint, basePoint1, basePoint2);
        }

        // Draws the arrow on the ImGui canvas.
        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float scaledShaftThickness = Math.Max(1f, Thickness * ImGuiHelpers.GlobalScale);
            if (IsSelected || IsHovered) scaledShaftThickness += 2f * ImGuiHelpers.GlobalScale;

            Vector2 shaftStartLogical = StartPointRelative;
            Vector2 shaftEndLogical = EndPointRelative; // End of shaft line segment
            Vector2 shaftVectorLogicalUnrotated = shaftEndLogical - shaftStartLogical; // Shaft vector before rotation

            // Transformation matrix: Rotates around StartPointRelative, then translates by StartPointRelative, scales, then translates to screen.
            Matrix3x2 transform =
                Matrix3x2.CreateRotation(RotationAngle) * Matrix3x2.CreateTranslation(shaftStartLogical) * Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                Matrix3x2.CreateTranslation(canvasOriginScreen);

            // Transform shaft points. Local start is (0,0), local end is shaftVectorLogicalUnrotated.
            Vector2 screenShaftStart = Vector2.Transform(Vector2.Zero, transform);
            Vector2 screenShaftEnd = Vector2.Transform(shaftVectorLogicalUnrotated, transform);

            if (IsPreview && shaftVectorLogicalUnrotated.LengthSquared() < (0.5f * 0.5f))
            {
                drawList.AddCircleFilled(screenShaftStart, scaledShaftThickness / 2f + (2f * ImGuiHelpers.GlobalScale), displayColor);
                return;
            }

            drawList.AddLine(screenShaftStart, screenShaftEnd, displayColor, scaledShaftThickness);

            Vector2 shaftDirLogicalUnrotated = shaftVectorLogicalUnrotated.LengthSquared() > 0.001f ? Vector2.Normalize(shaftVectorLogicalUnrotated) : new Vector2(0, -1);

            // Get arrowhead points in local space (relative to StartPointRelative, unrotated)
            var (ahTipLocal, ahBase1Local, ahBase2Local) = GetArrowheadGeometricPoints(
                shaftVectorLogicalUnrotated, // This is the shaft's end in local unrotated space relative to start
                shaftDirLogicalUnrotated,
                Thickness // Use logical thickness for geometry
            );

            // Transform arrowhead points using the same matrix
            Vector2 screenAhTip = Vector2.Transform(ahTipLocal, transform);
            Vector2 screenAhBase1 = Vector2.Transform(ahBase1Local, transform);
            Vector2 screenAhBase2 = Vector2.Transform(ahBase2Local, transform);

            drawList.AddTriangleFilled(screenAhTip, screenAhBase1, screenAhBase2, displayColor);
        }

        // Draws the arrow to an ImageSharp context for image export.
        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledShaftThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            Vector2 shaftStartLogical = this.StartPointRelative;
            Vector2 shaftEndLogical = this.EndPointRelative;
            Vector2 shaftVectorLogicalUnrotated = shaftEndLogical - shaftStartLogical;

            // If the arrow is degenerate (e.g., during preview before dragging), don't draw to image.
            if (shaftVectorLogicalUnrotated.LengthSquared() < (0.1f * 0.1f) / (currentGlobalScale * currentGlobalScale) && this.IsPreview)
            {
                // Optionally draw a small dot for extremely small arrows if not a preview
                return;
            }

            // Transformation matrix:
            // 1. Rotate around local origin (0,0) by RotationAngle.
            // 2. Translate by StartPointRelative to position the rotated shape in logical space.
            // 3. Scale by currentGlobalScale.
            // 4. Translate by canvasOriginInOutputImage to place on the final image.
            Matrix3x2 transformMatrix =
                Matrix3x2.CreateRotation(this.RotationAngle) * Matrix3x2.CreateTranslation(shaftStartLogical) * Matrix3x2.CreateScale(currentGlobalScale) * Matrix3x2.CreateTranslation(canvasOriginInOutputImage);

            // Transform shaft points. Shaft starts at local (0,0) and ends at shaftVectorLogicalUnrotated.
            PointF screenShaftStart = PointF.Transform(Vector2.Zero, transformMatrix);
            PointF screenShaftEnd = PointF.Transform(shaftVectorLogicalUnrotated, transformMatrix);

            // Draw shaft line
            var shaftPathBuilder = new PathBuilder();
            shaftPathBuilder.AddLine(screenShaftStart, screenShaftEnd);
            context.Draw(Pens.Solid(imageSharpColor, scaledShaftThickness), shaftPathBuilder.Build());

            // Calculate and transform arrowhead points
            Vector2 shaftDirLogicalUnrotated = shaftVectorLogicalUnrotated.LengthSquared() > 0.001f ? Vector2.Normalize(shaftVectorLogicalUnrotated) : new Vector2(0, -1); // Default up if zero length

            // Get arrowhead points in local space (relative to StartPointRelative, unrotated)
            var (ahTipLocal, ahBase1Local, ahBase2Local) = GetArrowheadGeometricPoints(
                shaftVectorLogicalUnrotated, // End of shaft in local unrotated space (relative to StartPointRelative)
                shaftDirLogicalUnrotated,
                this.Thickness // Use logical thickness for geometry calculation
            );

            // Transform arrowhead points using the same matrix
            PointF screenAhTip = PointF.Transform(ahTipLocal, transformMatrix);
            PointF screenAhBase1 = PointF.Transform(ahBase1Local, transformMatrix);
            PointF screenAhBase2 = PointF.Transform(ahBase2Local, transformMatrix);

            var arrowheadPathBuilder = new PathBuilder();
            arrowheadPathBuilder.AddLines(new PointF[] { screenAhTip, screenAhBase1, screenAhBase2 });
            arrowheadPathBuilder.CloseFigure(); // Make it a triangle
            context.Fill(imageSharpColor, arrowheadPathBuilder.Build()); // Arrowhead is always filled
        }

        // Performs hit detection for the arrow in logical (unscaled) coordinates.
        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            // Transform query point into the arrow's local unrotated space (StartPointRelative is origin)
            Vector2 localQueryPoint = Vector2.Transform(queryPointCanvasRelative - this.StartPointRelative, Matrix3x2.CreateRotation(-this.RotationAngle));

            float effectiveHitRangeShaft = unscaledHitThreshold + (this.Thickness / 2f);
            Vector2 localShaftStart = Vector2.Zero; // StartPointRelative is the local origin
            Vector2 localShaftEnd = this.EndPointRelative - this.StartPointRelative; // EndPoint in local unrotated space

            // Check hit on shaft
            if (HitDetection.DistancePointToLineSegment(localQueryPoint, localShaftStart, localShaftEnd) <= effectiveHitRangeShaft) return true;

            // Check hit on arrowhead (which is filled)
            Vector2 shaftDirLocal = localShaftEnd.LengthSquared() > 0.001f ? Vector2.Normalize(localShaftEnd) : new Vector2(0, -1);
            var (ahTip, ahB1, ahB2) = GetArrowheadGeometricPoints(localShaftEnd, shaftDirLocal, this.Thickness);

            // Check if point is inside the arrowhead triangle (ahTip, ahB1, ahB2 are relative to localShaftStart i.e. Vector2.Zero here)
            if (HitDetection.PointInTriangle(localQueryPoint, ahTip, ahB1, ahB2)) return true;

            // Optional: Add proximity check to arrowhead edges if more generous hit is needed for outline-like selection
            // float edgeProximity = unscaledHitThreshold + Thickness * 0.25f; 
            // if (HitDetection.DistancePointToLineSegment(localQueryPoint, ahTip, ahB1) <= edgeProximity) return true;
            // if (HitDetection.DistancePointToLineSegment(localQueryPoint, ahTip, ahB2) <= edgeProximity) return true;
            // if (HitDetection.DistancePointToLineSegment(localQueryPoint, ahB1, ahB2) <= edgeProximity) return true;

            return false;
        }

        // Creates a clone of this drawable arrow.
        public override BaseDrawable Clone()
        {
            var newArrow = new DrawableArrow(this.StartPointRelative, this.Color, this.Thickness)
            {
                EndPointRelative = this.EndPointRelative,
                RotationAngle = this.RotationAngle,
                ArrowheadLengthOffset = this.ArrowheadLengthOffset,
                ArrowheadWidthScale = this.ArrowheadWidthScale
            };
            CopyBasePropertiesTo(newArrow);
            return newArrow;
        }

        // Translates the arrow by a given delta in logical coordinates.
        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }

        // Sets the start point and adjusts the end point to maintain the shaft vector.
        public void SetStartPoint(Vector2 newStartLogical)
        {
            Vector2 diff = newStartLogical - this.StartPointRelative;
            this.StartPointRelative = newStartLogical;
            this.EndPointRelative += diff;
        }

        // Sets the end point directly (relative to the unrotated start point).
        public void SetEndPoint(Vector2 newEndLogical)
        {
            this.EndPointRelative = newEndLogical;
        }
    }
}
