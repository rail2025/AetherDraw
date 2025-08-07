// AetherDraw/DrawingLogic/DrawableArrow.cs
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
    public class DrawableArrow : BaseDrawable
    {
        /// <summary>
        /// The logical, unscaled start point of the arrow shaft. Also the pivot for rotation.
        /// </summary>
        public Vector2 StartPointRelative { get; set; }
        /// <summary>
        /// The logical, unscaled end point of the arrow shaft.
        /// </summary>
        public Vector2 EndPointRelative { get; set; }
        /// <summary>
        /// The rotation angle in radians around the StartPointRelative.
        /// </summary>
        public float RotationAngle { get; set; } = 0f;

        /// <summary>
        /// The offset determining the arrowhead's length based on the shaft's thickness.
        /// </summary>
        public float ArrowheadLengthOffset { get; set; }
        /// <summary>
        /// The scale factor determining the arrowhead's width based on the shaft's thickness.
        /// </summary>
        public float ArrowheadWidthScale { get; set; }

        public static readonly float DefaultArrowheadLengthFactorFromThickness = 5.0f;
        public static readonly float DefaultArrowheadWidthFactorFromThickness = 3.0f;
        public static readonly float MinArrowheadAbsoluteDim = 5f;

        public DrawableArrow(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Arrow;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative;
            this.Color = color;
            this.Thickness = Math.Max(1f, unscaledThickness);
            this.IsFilled = true; // Arrowhead is always filled.
            this.IsPreview = true;

            this.ArrowheadLengthOffset = Math.Max(MinArrowheadAbsoluteDim, this.Thickness * DefaultArrowheadLengthFactorFromThickness);
            this.ArrowheadWidthScale = DefaultArrowheadWidthFactorFromThickness;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float scaledShaftThickness = Math.Max(1f, Thickness * ImGuiHelpers.GlobalScale);
            if (IsSelected || IsHovered) scaledShaftThickness += 2f * ImGuiHelpers.GlobalScale;

            Vector2 shaftStartLogical = StartPointRelative;
            Vector2 shaftEndLogical = EndPointRelative;
            Vector2 shaftVectorLogicalUnrotated = shaftEndLogical - shaftStartLogical;

            // If the arrow is too small to see, just draw a dot.
            if (IsPreview && shaftVectorLogicalUnrotated.LengthSquared() < (0.5f * 0.5f))
            {
                drawList.AddCircleFilled((shaftStartLogical * ImGuiHelpers.GlobalScale) + canvasOriginScreen, scaledShaftThickness / 2f + (2f * ImGuiHelpers.GlobalScale), displayColor);
                return;
            }

            // Get all vertices of the arrow polygon.
            var vertices = GetTransformedVertices();

            // Scale and translate the vertices to screen coordinates.
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = (vertices[i] * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
            }

            // Draw the arrow components.
            drawList.AddLine(vertices[0], vertices[1], displayColor, scaledShaftThickness); // Shaft
            drawList.AddTriangleFilled(vertices[2], vertices[3], vertices[4], displayColor); // Arrowhead
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledShaftThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            if ((this.EndPointRelative - this.StartPointRelative).LengthSquared() < 0.01f && this.IsPreview)
                return;

            // Get the final vertex positions for drawing.
            var vertices = GetTransformedVertices();

            // Convert to ImageSharp points and apply canvas origin offset.
            var imageSharpPoints = new SixLabors.ImageSharp.PointF[vertices.Length];
            var transform = Matrix3x2.CreateScale(currentGlobalScale) * Matrix3x2.CreateTranslation(canvasOriginInOutputImage);
            for (int i = 0; i < vertices.Length; i++)
            {
                var transformedVec = Vector2.Transform(vertices[i], transform);
                imageSharpPoints[i] = new SixLabors.ImageSharp.PointF(transformedVec.X, transformedVec.Y);
            }

            // Draw shaft line.
            context.DrawLine(imageSharpColor, scaledShaftThickness, imageSharpPoints[0], imageSharpPoints[1]);

            // Draw arrowhead triangle.
            var arrowheadPath = new PathBuilder().AddLines(new[] { imageSharpPoints[2], imageSharpPoints[3], imageSharpPoints[4] }).CloseFigure().Build();
            context.Fill(imageSharpColor, arrowheadPath);
        }

        public override System.Drawing.RectangleF GetBoundingBox()
        {
            // Get the final absolute positions of all defining vertices.
            var vertices = GetTransformedVertices();

            float minX = vertices[0].X, minY = vertices[0].Y, maxX = vertices[0].X, maxY = vertices[0].Y;

            // The shaft start (vertices[0]) is already included. We check the other 4 points.
            for (int i = 1; i < vertices.Length; i++)
            {
                minX = MathF.Min(minX, vertices[i].X);
                minY = MathF.Min(minY, vertices[i].Y);
                maxX = MathF.Max(maxX, vertices[i].X);
                maxY = MathF.Max(maxY, vertices[i].Y);
            }

            return new System.Drawing.RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            // Transform query point into the arrow's local unrotated space.
            Vector2 localQueryPoint = Vector2.Transform(queryPointCanvasRelative - this.StartPointRelative, Matrix3x2.CreateRotation(-this.RotationAngle));

            float effectiveHitRangeShaft = unscaledHitThreshold + (this.Thickness / 2f);
            Vector2 localShaftStart = Vector2.Zero;
            Vector2 localShaftEnd = this.EndPointRelative - this.StartPointRelative;

            // Check hit on shaft.
            if (HitDetection.DistancePointToLineSegment(localQueryPoint, localShaftStart, localShaftEnd) <= effectiveHitRangeShaft) return true;

            // Check hit on arrowhead triangle.
            Vector2 shaftDirLocal = localShaftEnd.LengthSquared() > 0.001f ? Vector2.Normalize(localShaftEnd) : new Vector2(0, -1);
            var (ahTip, ahB1, ahB2) = GetArrowheadGeometricPoints(localShaftEnd, shaftDirLocal);

            return HitDetection.PointInTriangle(localQueryPoint, ahTip, ahB1, ahB2);
        }

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

        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }

        public void SetStartPoint(Vector2 newStartLogical)
        {
            Vector2 diff = newStartLogical - this.StartPointRelative;
            this.StartPointRelative = newStartLogical;
            this.EndPointRelative += diff;
        }

        public void SetEndPoint(Vector2 newEndLogical)
        {
            this.EndPointRelative = newEndLogical;
        }

        private (Vector2 visualTip, Vector2 base1, Vector2 base2) GetArrowheadGeometricPoints(Vector2 shaftEnd, Vector2 shaftDir)
        {
            float actualArrowheadLength = Math.Max(MinArrowheadAbsoluteDim, this.ArrowheadLengthOffset);
            float actualArrowheadHalfWidth = Math.Max(MinArrowheadAbsoluteDim / 2f, (this.Thickness * this.ArrowheadWidthScale) / 2f);

            Vector2 visualTipPoint = shaftEnd + shaftDir * actualArrowheadLength;

            Vector2 perpendicularOffset = new Vector2(shaftDir.Y, -shaftDir.X) * actualArrowheadHalfWidth;
            Vector2 basePoint1 = shaftEnd + perpendicularOffset;
            Vector2 basePoint2 = shaftEnd - perpendicularOffset;

            return (visualTipPoint, basePoint1, basePoint2);
        }

        private Vector2[] GetTransformedVertices()
        {
            Vector2 shaftStart = this.StartPointRelative;
            Vector2 shaftEnd = this.EndPointRelative;
            Vector2 shaftVector = shaftEnd - shaftStart;

            if (shaftVector.LengthSquared() < 0.01f)
                return new Vector2[] { shaftStart, shaftStart, shaftStart, shaftStart, shaftStart };

            Vector2 shaftDir = Vector2.Normalize(shaftVector);

            // Get arrowhead points relative to the shaft end
            var (ahTip, ahB1, ahB2) = GetArrowheadGeometricPoints(shaftVector, shaftDir);

            // Create the transformation matrix for rotation and positioning
            Matrix3x2 transform = Matrix3x2.CreateRotation(this.RotationAngle) * Matrix3x2.CreateTranslation(shaftStart);

            return new Vector2[] {
                Vector2.Transform(Vector2.Zero, transform), // Final Shaft Start
                Vector2.Transform(shaftVector, transform), // Final Shaft End
                Vector2.Transform(ahTip, transform), // Final Arrowhead Tip
                Vector2.Transform(ahB1, transform), // Final Arrowhead Base 1
                Vector2.Transform(ahB2, transform)  // Final Arrowhead Base 2
            };
        }
    }
}
