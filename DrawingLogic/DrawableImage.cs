using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using ImGuiNET;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;

namespace AetherDraw.DrawingLogic
{
    public class DrawableImage : BaseDrawable
    {
        public string ImageResourcePath { get; private set; }
        public Vector2 PositionRelative { get; set; }
        public Vector2 DrawSize { get; set; }
        public float RotationAngle { get; set; } = 0f;

        public static readonly float UnscaledRotationHandleDistance = 20f;
        public static readonly float UnscaledRotationHandleRadius = 5f;
        public static readonly float UnscaledResizeHandleRadius = 4f;

        private IDalamudTextureWrap? textureWrapCache;

        public DrawableImage(DrawMode drawMode, string imageResourcePath, Vector2 positionRelative, Vector2 unscaledDrawSize, Vector4 tint, float rotation = 0f)
        {
            this.ObjectDrawMode = drawMode;
            this.ImageResourcePath = imageResourcePath;
            this.PositionRelative = positionRelative;
            this.DrawSize = unscaledDrawSize;
            this.Color = tint;
            this.RotationAngle = rotation;
            this.IsFilled = true;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            textureWrapCache ??= TextureManager.GetTexture(this.ImageResourcePath);

            if (textureWrapCache == null || textureWrapCache.ImGuiHandle == IntPtr.Zero)
            {
                Vector2 screenPosCenter = (this.PositionRelative * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
                Vector2 scaledDrawSize = this.DrawSize * ImGuiHelpers.GlobalScale;
                drawList.AddRectFilled(screenPosCenter - scaledDrawSize / 2f, screenPosCenter + scaledDrawSize / 2f, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
                Vector2 textSize = ImGui.CalcTextSize("IMG?");
                drawList.AddText(screenPosCenter - textSize / 2f, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), "IMG?");
                return;
            }

            var displayTintVec = this.IsSelected ? new Vector4(1, 1, 0, 0.7f) : (this.IsHovered && !this.IsSelected ? new Vector4(0.9f, 0.9f, 0.9f, 0.9f) : this.Color);
            uint tintColorU32 = ImGui.GetColorU32(displayTintVec);

            Vector2[] quadVertices = GetRotatedCorners();
            for (int i = 0; i < quadVertices.Length; i++)
            {
                quadVertices[i] = (quadVertices[i] * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
            }

            drawList.AddImageQuad(textureWrapCache.ImGuiHandle, quadVertices[0], quadVertices[1], quadVertices[2], quadVertices[3],
                                  Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, tintColorU32);
        }

        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            // Placeholder - i dont remember why this is here
        }

        public override System.Drawing.RectangleF GetBoundingBox()
        {
            Vector2[] corners = GetRotatedCorners();
            float minX = corners.Min(c => c.X);
            float minY = corners.Min(c => c.Y);
            float maxX = corners.Max(c => c.X);
            float maxY = corners.Max(c => c.Y);
            return new System.Drawing.RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            Vector2 localQueryPoint = Vector2.Transform(queryPointRelative - this.PositionRelative, Matrix3x2.CreateRotation(-this.RotationAngle));
            Vector2 logicalHalfSize = this.DrawSize / 2f;
            return Math.Abs(localQueryPoint.X) <= logicalHalfSize.X + hitThreshold &&
                   Math.Abs(localQueryPoint.Y) <= logicalHalfSize.Y + hitThreshold;
        }

        public override BaseDrawable Clone()
        {
            var newImg = new DrawableImage(this.ObjectDrawMode, this.ImageResourcePath, this.PositionRelative, this.DrawSize, this.Color, this.RotationAngle);
            CopyBasePropertiesTo(newImg);
            return newImg;
        }

        public override void Translate(Vector2 delta)
        {
            this.PositionRelative += delta;
        }

        private Vector2[] GetRotatedCorners()
        {
            return HitDetection.GetRotatedQuadVertices(this.PositionRelative, this.DrawSize / 2f, this.RotationAngle);
        }
    }
}
