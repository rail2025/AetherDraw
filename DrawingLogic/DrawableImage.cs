using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Textures.TextureWraps;

namespace AetherDraw.DrawingLogic
{
    public class DrawableImage : BaseDrawable
    {
        public string ImageResourcePath { get; private set; }
        public Vector2 PositionRelative { get; set; }
        public Vector2 DrawSize { get; set; }
        public float RotationAngle { get; set; } = 0f;

        public static readonly float RotationHandleDistance = 20f;
        public static readonly float RotationHandleRadius = 5f;
        public static readonly float ResizeHandleRadius = 4f;

        private IDalamudTextureWrap? textureWrap; // Renamed from _textureWrap

        public DrawableImage(DrawMode drawMode, string imageResourcePath, Vector2 positionRelative, Vector2 drawSize, Vector4 tint, float rotation = 0f)
        {
            this.ObjectDrawMode = drawMode; this.ImageResourcePath = imageResourcePath;
            this.PositionRelative = positionRelative; this.DrawSize = drawSize;
            this.Color = tint; this.RotationAngle = rotation;
            this.Thickness = 0; this.IsFilled = true; this.IsPreview = false;
        }

        public Vector2 GetRotationHandleScreenPosition(Vector2 canvasOriginScreen)
        {
            Vector2 screenCenter = this.PositionRelative + canvasOriginScreen;
            Vector2 handleOffset = new Vector2(0, -(this.DrawSize.Y / 2f + RotationHandleDistance));
            float cosA = MathF.Cos(this.RotationAngle); float sinA = MathF.Sin(this.RotationAngle);
            Vector2 rotatedHandleOffset = HitDetection.ImRotate(handleOffset, cosA, sinA);
            return screenCenter + rotatedHandleOffset;
        }

        private IDalamudTextureWrap? GetTextureWrap()
        {
            if (textureWrap == null || textureWrap.ImGuiHandle == IntPtr.Zero)
            {
                textureWrap = TextureManager.GetTexture(this.ImageResourcePath);
            }
            return textureWrap;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var tex = GetTextureWrap();
            Vector2 screenPosCenter = this.PositionRelative + canvasOriginScreen;
            var displayTintVec = this.IsSelected ? new Vector4(1, 1, 0, 0.7f) :
                                (this.IsHovered && !this.IsSelected ? new Vector4(0.9f, 0.9f, 0.9f, 0.9f) : this.Color);
            uint tintColorU32 = ImGui.GetColorU32(displayTintVec);

            if (tex == null || tex.ImGuiHandle == IntPtr.Zero)
            {
                drawList.AddRectFilled(screenPosCenter - this.DrawSize / 2f, screenPosCenter + this.DrawSize / 2f, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
                drawList.AddText(screenPosCenter - new Vector2(10, 5), ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), "IMG?");
                return;
            }

            Vector2 halfSize = this.DrawSize / 2.0f;
            Vector2[] quadVerticesScreen = HitDetection.GetRotatedQuadVertices(screenPosCenter, halfSize, this.RotationAngle);

            Vector2 uv0 = Vector2.Zero; Vector2 uv1 = new Vector2(1, 0); Vector2 uv2 = Vector2.One; Vector2 uv3 = new Vector2(0, 1);
            drawList.AddImageQuad(tex.ImGuiHandle, quadVerticesScreen[0], quadVerticesScreen[1], quadVerticesScreen[2], quadVerticesScreen[3], uv0, uv1, uv2, uv3, tintColorU32);

            if (this.IsSelected)
            {
                uint highlightColor = ImGui.GetColorU32(new Vector4(1, 1, 0, 1));
                float highlightThickness = 2f;
                for (int i = 0; i < 4; i++)
                {
                    drawList.AddLine(quadVerticesScreen[i], quadVerticesScreen[(i + 1) % 4], highlightColor, highlightThickness);
                }

                Vector2 rotationHandleScreenPos = GetRotationHandleScreenPosition(canvasOriginScreen);
                drawList.AddCircleFilled(rotationHandleScreenPos, RotationHandleRadius, highlightColor);
                drawList.AddCircle(rotationHandleScreenPos, RotationHandleRadius + 1f, ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), 12, 1.5f);

                uint resizeHandleColor = ImGui.GetColorU32(new Vector4(0, 0.8f, 1f, 1f));
                foreach (var corner in quadVerticesScreen)
                {
                    drawList.AddCircleFilled(corner, ResizeHandleRadius, resizeHandleColor);
                    drawList.AddCircle(corner, ResizeHandleRadius + 1f, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), 8, 1f);
                }
            }
            else if (this.IsHovered)
            {
                uint hoverColor = ImGui.GetColorU32(new Vector4(0f, 1f, 1f, 0.7f));
                float hoverThickness = 1.5f;
                for (int i = 0; i < 4; i++)
                {
                    drawList.AddLine(quadVerticesScreen[i], quadVerticesScreen[(i + 1) % 4], hoverColor, hoverThickness);
                }
            }
        }

        public override bool IsHit(Vector2 queryPointOrEraserCenterRelative, float hitThresholdOrEraserRadius = 5.0f)
        {
            Vector2 halfSize = this.DrawSize / 2f;
            Vector2 imageRectMinRelative = this.PositionRelative - halfSize;
            Vector2 imageRectMaxRelative = this.PositionRelative + halfSize;
            return HitDetection.IntersectCircleAABB(queryPointOrEraserCenterRelative, hitThresholdOrEraserRadius, imageRectMinRelative, imageRectMaxRelative);
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
    }
}
