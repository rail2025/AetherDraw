// AetherDraw/DrawingLogic/DrawableStarburst.cs
using System;
using System.Drawing; // Required for RectangleF
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace AetherDraw.DrawingLogic
{
    public class DrawableStarburst : BaseDrawable
    {
        public Vector2 Center { get; set; }
        public float Radius { get; set; } = 50f;
        public float Width { get; set; } = 20f;
        public float RotationAngle { get; set; } = 0f;

        public DrawableStarburst(Vector2 center, Vector4 color, float unscaledThickness, bool isFilled, float radius = 50f, float width = 20f)
        {
            this.ObjectDrawMode = DrawMode.Starburst;
            this.Center = center;
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = isFilled;
            this.Radius = radius;
            this.Width = width;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            // Dragging defines the radius
            this.Radius = Vector2.Distance(this.Center, newPointRelative);
        }

        // Explicitly use System.Drawing.RectangleF to avoid ambiguity with ImageSharp
        public override System.Drawing.RectangleF GetBoundingBox()
        {
            // Use enclosing circle for faster bounding box calculation
            // { x: center.x - radius, y: center.y - radius, ... }
            return new System.Drawing.RectangleF(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = IsSelected || IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = Math.Max(1f * ImGuiHelpers.GlobalScale, baseScaledThickness + highlightThicknessAddition);

            // A starburst is 4 rectangles rotated by 45 degrees (PI/4) increments
            for (int i = 0; i < 4; i++)
            {
                float angle = RotationAngle + (i * MathF.PI / 4f);
                DrawSingleBar(drawList, canvasOriginScreen, angle, displayColor, displayScaledThickness);
            }
        }

        private void DrawSingleBar(ImDrawListPtr drawList, Vector2 canvasOriginScreen, float angle, uint color, float thickness)
        {
            // Define a local rectangle centered at (0,0) with length = 2*Radius and height = Width
            Vector2 halfSize = new Vector2(Radius, Width / 2f);

            Vector2[] localCorners = {
                new Vector2(-halfSize.X, -halfSize.Y), new Vector2(halfSize.X, -halfSize.Y),
                new Vector2(halfSize.X, halfSize.Y), new Vector2(-halfSize.X, halfSize.Y)
            };
            Vector2[] screenCorners = new Vector2[4];

            Matrix3x2 transform = Matrix3x2.CreateRotation(angle) *
                                  Matrix3x2.CreateTranslation(Center) *
                                  Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                                  Matrix3x2.CreateTranslation(canvasOriginScreen);

            for (int k = 0; k < 4; k++)
            {
                screenCorners[k] = Vector2.Transform(localCorners[k], transform);
            }

            if (IsFilled)
            {
                drawList.AddConvexPolyFilled(ref screenCorners[0], 4, color);
            }
            else
            {
                drawList.AddPolyline(ref screenCorners[0], 4, color, ImDrawFlags.Closed, thickness);
            }
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            for (int i = 0; i < 4; i++)
            {
                float angle = RotationAngle + (i * MathF.PI / 4f);

                Vector2 halfSize = new Vector2(Radius, Width / 2f);
                var localCorners = new SixLabors.ImageSharp.PointF[] {
                    new (-halfSize.X, -halfSize.Y), new (halfSize.X, -halfSize.Y),
                    new (halfSize.X, halfSize.Y), new (-halfSize.X, halfSize.Y)
                };

                Matrix3x2 transformMatrix =
                    Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(Center) * Matrix3x2.CreateScale(currentGlobalScale) * Matrix3x2.CreateTranslation(canvasOriginInOutputImage);

                var transformedCorners = new SixLabors.ImageSharp.PointF[4];
                for (int k = 0; k < 4; k++)
                {
                    var transformedVec = Vector2.Transform(new Vector2(localCorners[k].X, localCorners[k].Y), transformMatrix);
                    transformedCorners[k] = new SixLabors.ImageSharp.PointF(transformedVec.X, transformedVec.Y);
                }

                var path = new PathBuilder().AddLines(transformedCorners).CloseFigure().Build();

                if (IsFilled)
                {
                    context.Fill(imageSharpColor, path);
                }
                else
                {
                    context.Draw(Pens.Solid(imageSharpColor, scaledThickness), path);
                }
            }
        }

        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5f)
        {
            // Check if point hits any of the 4 individual bars
            for (int i = 0; i < 4; i++)
            {
                float angle = RotationAngle + (i * MathF.PI / 4f);
                if (IsPointInRotatedRect(queryPointCanvasRelative, angle, unscaledHitThreshold))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsPointInRotatedRect(Vector2 queryPoint, float angle, float threshold)
        {
            // Transform the query point into the bar's local, unrotated coordinate system
            Vector2 localQueryPoint = Vector2.Transform(queryPoint - Center, Matrix3x2.CreateRotation(-angle));

            float halfLen = Radius;
            float halfWidth = Width / 2f;

            if (IsFilled)
            {
                return Math.Abs(localQueryPoint.X) <= halfLen + threshold &&
                       Math.Abs(localQueryPoint.Y) <= halfWidth + threshold;
            }
            else
            {
                float effectiveEdgeDist = threshold + Thickness / 2f;
                bool withinOuter = Math.Abs(localQueryPoint.X) <= halfLen + effectiveEdgeDist &&
                                   Math.Abs(localQueryPoint.Y) <= halfWidth + effectiveEdgeDist;

                bool outsideInner = Math.Abs(localQueryPoint.X) >= halfLen - effectiveEdgeDist ||
                                    Math.Abs(localQueryPoint.Y) >= halfWidth - effectiveEdgeDist;

                return withinOuter && outsideInner;
            }
        }

        public override BaseDrawable Clone()
        {
            var newObj = new DrawableStarburst(this.Center, this.Color, this.Thickness, this.IsFilled, this.Radius, this.Width)
            {
                RotationAngle = this.RotationAngle
            };
            CopyBasePropertiesTo(newObj);
            return newObj;
        }

        public override void Translate(Vector2 delta)
        {
            this.Center += delta;
        }
    }
}
