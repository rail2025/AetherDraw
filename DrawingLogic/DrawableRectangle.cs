// AetherDraw/DrawingLogic/DrawableRectangle.cs
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
    public class DrawableRectangle : BaseDrawable
    {
        // Top-left point used to define the rectangle before rotation.
        public Vector2 StartPointRelative { get; set; }
        // Bottom-right point used to define the rectangle before rotation.
        public Vector2 EndPointRelative { get; set; }
        // Rotation angle in radians around the rectangle's center.
        public float RotationAngle { get; set; } = 0f;

        // Offset for the rotation handle when drawn in ImGui.
        public static readonly float UnscaledRotationHandleExtraOffset = 25f;

        public DrawableRectangle(Vector2 startPointRelative, Vector4 color, float unscaledThickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Rectangle;
            this.StartPointRelative = startPointRelative;
            this.EndPointRelative = startPointRelative; // Initially a point, expands during preview.
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        // Updates the rectangle's second defining point during preview drawing.
        public override void UpdatePreview(Vector2 newPointRelative)
        {
            this.EndPointRelative = newPointRelative;
        }

        // Calculates the center and half-size of the unrotated rectangle from its defining points.
        public (Vector2 center, Vector2 halfSize) GetGeometry()
        {
            Vector2 min = new Vector2(MathF.Min(StartPointRelative.X, EndPointRelative.X), MathF.Min(StartPointRelative.Y, EndPointRelative.Y));
            Vector2 max = new Vector2(MathF.Max(StartPointRelative.X, EndPointRelative.X), MathF.Max(StartPointRelative.Y, EndPointRelative.Y));
            Vector2 center = (min + max) / 2f;
            Vector2 halfSize = (max - min) / 2f;
            return (center, halfSize);
        }

        // Draws the rectangle on the ImGui canvas.
        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = IsSelected || IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = Math.Max(1f * ImGuiHelpers.GlobalScale, baseScaledThickness + highlightThicknessAddition);

            var (center, halfSize) = GetGeometry();
            // Do not draw if it's a preview and effectively has no area.
            if (halfSize.X < 0.1f && halfSize.Y < 0.1f && IsPreview) return;

            // Define corners relative to a local (0,0) center for easier rotation.
            Vector2[] localCorners = {
                new Vector2(-halfSize.X, -halfSize.Y), new Vector2(halfSize.X, -halfSize.Y),
                new Vector2(halfSize.X, halfSize.Y), new Vector2(-halfSize.X, halfSize.Y)
            };
            Vector2[] screenCorners = new Vector2[4];

            // Transformation: Rotate around local (0,0), translate by center, scale, then translate to screen.
            Matrix3x2 transform = Matrix3x2.CreateRotation(RotationAngle) *
                                  Matrix3x2.CreateTranslation(center) *
                                  Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                                  Matrix3x2.CreateTranslation(canvasOriginScreen);

            for (int i = 0; i < 4; i++)
            {
                screenCorners[i] = Vector2.Transform(localCorners[i], transform);
            }

            if (IsFilled)
            {
                drawList.AddConvexPolyFilled(ref screenCorners[0], 4, displayColor);
            }
            else
            {
                drawList.AddPolyline(ref screenCorners[0], 4, displayColor, ImDrawFlags.Closed, displayScaledThickness);
            }
        }

        // Draws the rectangle to an ImageSharp context for image export.
        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            var (center, halfSize) = GetGeometry();

            // Do not draw if it has no area.
            if (halfSize.X < 0.01f || halfSize.Y < 0.01f) return;

            // Define corners relative to a local (0,0) center.
            PointF[] localCorners = {
                new PointF(-halfSize.X, -halfSize.Y), new PointF(halfSize.X, -halfSize.Y),
                new PointF(halfSize.X, halfSize.Y), new PointF(-halfSize.X, halfSize.Y)
            };

            PointF[] transformedCorners = new PointF[4];

            // Transformation:
            // 1. Rotate around local origin (0,0).
            // 2. Translate by logical center to position the rotated shape.
            // 3. Scale by currentGlobalScale.
            // 4. Translate by canvasOriginInOutputImage to place on the final image.
            Matrix3x2 transformMatrix =
                Matrix3x2.CreateRotation(this.RotationAngle) * Matrix3x2.CreateTranslation(center) * Matrix3x2.CreateScale(currentGlobalScale) * Matrix3x2.CreateTranslation(canvasOriginInOutputImage);

            for (int i = 0; i < 4; i++)
            {
                // Convert local PointF to Vector2 for System.Numerics.Matrix3x2.Transform, then back to PointF
                var transformedVec = Vector2.Transform(new Vector2(localCorners[i].X, localCorners[i].Y), transformMatrix);
                transformedCorners[i] = new PointF(transformedVec.X, transformedVec.Y);
            }

            var pathBuilder = new PathBuilder();
            pathBuilder.AddLines(transformedCorners);
            pathBuilder.CloseFigure(); // Ensure the rectangle path is closed.
            IPath path = pathBuilder.Build();

            if (IsFilled)
            {
                context.Fill(imageSharpColor, path);
            }
            else
            {
                context.Draw(Pens.Solid(imageSharpColor, scaledThickness), path);
            }
        }

        // Performs hit detection for the rectangle in logical (unscaled) coordinates.
        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            var (center, halfSize) = GetGeometry();
            // Transform the query point into the rectangle's local, unrotated coordinate system.
            Vector2 localQueryPoint = Vector2.Transform(queryPointCanvasRelative - center, Matrix3x2.CreateRotation(-RotationAngle));

            if (IsFilled)
            {
                // For a filled rectangle, check if the local query point is within the halfSize bounds (plus threshold).
                return Math.Abs(localQueryPoint.X) <= halfSize.X + unscaledHitThreshold &&
                       Math.Abs(localQueryPoint.Y) <= halfSize.Y + unscaledHitThreshold;
            }
            else
            {
                // For an outlined rectangle, check if the point is near the border.
                float effectiveEdgeDist = unscaledHitThreshold + Thickness / 2f;
                bool withinOuterBounds = Math.Abs(localQueryPoint.X) <= halfSize.X + effectiveEdgeDist &&
                                         Math.Abs(localQueryPoint.Y) <= halfSize.Y + effectiveEdgeDist;
                bool outsideInnerBounds = Math.Abs(localQueryPoint.X) >= halfSize.X - effectiveEdgeDist ||
                                          Math.Abs(localQueryPoint.Y) >= halfSize.Y - effectiveEdgeDist;
                return withinOuterBounds && outsideInnerBounds;
            }
        }

        // Creates a clone of this drawable rectangle.
        public override BaseDrawable Clone()
        {
            var newRect = new DrawableRectangle(this.StartPointRelative, this.Color, this.Thickness, this.IsFilled)
            {
                EndPointRelative = this.EndPointRelative,
                RotationAngle = this.RotationAngle
            };
            CopyBasePropertiesTo(newRect);
            return newRect;
        }

        // Translates the rectangle by a given delta in logical coordinates.
        public override void Translate(Vector2 delta)
        {
            this.StartPointRelative += delta;
            this.EndPointRelative += delta;
        }

        // Helper to get corners in logical, rotated space (not scaled, no canvas origin offset).
        // Used by ShapeInteractionHandler.
        public Vector2[] GetRotatedCorners()
        {
            var (center, halfSize) = GetGeometry();
            Vector2[] localCornersUnrotated = {
                new Vector2(-halfSize.X, -halfSize.Y), new Vector2(halfSize.X, -halfSize.Y),
                new Vector2(halfSize.X, halfSize.Y), new Vector2(-halfSize.X, halfSize.Y)
            };
            Vector2[] rotatedCorners = new Vector2[4];
            Matrix3x2 rotMatrix = Matrix3x2.CreateRotation(RotationAngle);
            for (int i = 0; i < 4; i++)
            {
                rotatedCorners[i] = Vector2.Transform(localCornersUnrotated[i], rotMatrix) + center;
            }
            return rotatedCorners;
        }
    }
}
