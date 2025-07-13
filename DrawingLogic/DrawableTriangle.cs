// AetherDraw/DrawingLogic/DrawableTriangle.cs
using System;
using System.Drawing;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using System.Linq;

namespace AetherDraw.DrawingLogic
{
    public class DrawableTriangle : BaseDrawable
    {
        public Vector2[] Vertices { get; set; } = new Vector2[3];

        public DrawableTriangle(Vector2 v1, Vector2 v2, Vector2 v3, Vector4 color)
        {
            this.ObjectDrawMode = DrawMode.Triangle;
            this.Vertices[0] = v1;
            this.Vertices[1] = v2;
            this.Vertices[2] = v3;
            this.Color = color;
            this.IsFilled = true; // Triangles from this source are always filled
            this.Thickness = 1f;
            this.IsPreview = false;
        }

        // Overloaded constructor for manual drawing
        public DrawableTriangle(Vector2 startPoint, Vector4 color)
        {
            this.ObjectDrawMode = DrawMode.Triangle;
            this.Vertices[0] = startPoint;
            this.Vertices[1] = startPoint;
            this.Vertices[2] = startPoint;
            this.Color = color;
            this.IsFilled = true;
            this.Thickness = 1f;
            this.IsPreview = true;
        }

        public override void UpdatePreview(Vector2 newPointRelative)
        {
            // Simple preview: first point is fixed, second follows X, third follows Y
            this.Vertices[1] = new Vector2(newPointRelative.X, Vertices[0].Y);
            this.Vertices[2] = newPointRelative;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            if (Vertices.Length < 3) return;

            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);

            var screenVertices = Vertices.Select(v => (v * ImGuiHelpers.GlobalScale) + canvasOriginScreen).ToArray();

            if (IsFilled)
            {
                drawList.AddTriangleFilled(screenVertices[0], screenVertices[1], screenVertices[2], displayColor);
            }
            else
            {
                drawList.AddTriangle(screenVertices[0], screenVertices[1], screenVertices[2], displayColor, Thickness * ImGuiHelpers.GlobalScale);
            }
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            if (Vertices.Length < 3) return;

            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba((byte)(Color.X * 255), (byte)(Color.Y * 255), (byte)(Color.Z * 255), (byte)(Color.W * 255));
            var imageSharpPoints = Vertices.Select(v => new SixLabors.ImageSharp.PointF((v.X * currentGlobalScale) + canvasOriginInOutputImage.X, (v.Y * currentGlobalScale) + canvasOriginInOutputImage.Y)).ToArray();

            var path = new PathBuilder().AddLines(imageSharpPoints).CloseFigure().Build();

            if (IsFilled)
            {
                context.Fill(imageSharpColor, path);
            }
            else
            {
                context.Draw(Pens.Solid(imageSharpColor, Thickness * currentGlobalScale), path);
            }
        }

        public override System.Drawing.RectangleF GetBoundingBox()
        {
            if (Vertices.Length < 3) return System.Drawing.RectangleF.Empty;
            float minX = Vertices.Min(v => v.X);
            float minY = Vertices.Min(v => v.Y);
            float maxX = Vertices.Max(v => v.X);
            float maxY = Vertices.Max(v => v.Y);
            return new System.Drawing.RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5)
        {
            if (Vertices.Length < 3) return false;
            return HitDetection.PointInTriangle(queryPointCanvasRelative, Vertices[0], Vertices[1], Vertices[2]);
        }

        public override BaseDrawable Clone()
        {
            return new DrawableTriangle(this.Vertices[0], this.Vertices[1], this.Vertices[2], this.Color);
        }

        public override void Translate(Vector2 delta)
        {
            for (int i = 0; i < Vertices.Length; i++)
            {
                Vertices[i] += delta;
            }
        }
    }
}
