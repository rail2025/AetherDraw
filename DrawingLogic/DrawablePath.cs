using System.Numerics;
using System.Collections.Generic; // For List
using System.Linq; // For FirstOrDefault
using ImGuiNET;
using Dalamud.Interface.Utility; // For ImGuiHelpers
using System; // For MathF

// ImageSharp using statements
using SixLabors.ImageSharp; // For PointF, Color
using SixLabors.ImageSharp.PixelFormats; // If Rgba32 is used directly
using SixLabors.ImageSharp.Processing; // For IImageProcessingContext
using SixLabors.ImageSharp.Drawing; // For PathBuilder, Pens
using SixLabors.ImageSharp.Drawing.Processing; // For Draw extension method

namespace AetherDraw.DrawingLogic
{
    public class DrawablePath : BaseDrawable
    {
        public List<Vector2> PointsRelative { get; set; } = new List<Vector2>();

        public DrawablePath(Vector2 startPointRelative, Vector4 color, float unscaledThickness)
        {
            this.ObjectDrawMode = DrawMode.Pen;
            if (startPointRelative != default || !PointsRelative.Any())
            {
                PointsRelative.Add(startPointRelative);
            }
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = false;
            this.IsPreview = true;
        }

        public void AddPoint(Vector2 pointRelative)
        {
            if (!PointsRelative.Any() || Vector2.DistanceSquared(PointsRelative.Last(), pointRelative) > 0.01f)
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
            if (PointsRelative.Count < 2) return;

            var displayColorVec = this.IsSelected ? new Vector4(1, 1, 0, 1) : (this.IsHovered ? new Vector4(0, 1, 1, 1) : this.Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            float baseScaledThickness = this.Thickness * ImGuiHelpers.GlobalScale;
            float highlightThicknessAddition = this.IsSelected || this.IsHovered ? (2f * ImGuiHelpers.GlobalScale) : 0f;
            float displayScaledThickness = baseScaledThickness + highlightThicknessAddition;
            displayScaledThickness = Math.Max(1f * ImGuiHelpers.GlobalScale, displayScaledThickness);

            Vector2[] screenPoints = PointsRelative.Select(p => p * ImGuiHelpers.GlobalScale + canvasOriginScreen).ToArray(); // Apply scale

            if (screenPoints.Length > 1)
            {
                drawList.AddPolyline(ref screenPoints[0], screenPoints.Length, displayColor, ImDrawFlags.None, displayScaledThickness);
            }
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            if (PointsRelative.Count < 2) return;

            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255),
                (byte)(Color.Y * 255),
                (byte)(Color.Z * 255),
                (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            var pathBuilder = new PathBuilder();
            var firstPoint = PointsRelative[0];

            
            pathBuilder.MoveTo(new PointF(
                (firstPoint.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                (firstPoint.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
            ));

            for (int i = 1; i < PointsRelative.Count; i++)
            {
                var currentPoint = PointsRelative[i];
               
                pathBuilder.LineTo(new PointF(
                    (currentPoint.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                    (currentPoint.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
                ));
            }

            IPath path = pathBuilder.Build();
            
            context.Draw(Pens.Solid(imageSharpColor, scaledThickness), path);
        }

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

        public override BaseDrawable Clone()
        {
            var newPath = new DrawablePath(PointsRelative.FirstOrDefault(), this.Color, this.Thickness)
            {
                PointsRelative = new List<Vector2>(this.PointsRelative)
            };
            CopyBasePropertiesTo(newPath);
            return newPath;
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
