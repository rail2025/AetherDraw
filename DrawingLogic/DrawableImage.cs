using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility; // Added for ImGuiHelpers

namespace AetherDraw.DrawingLogic
{
    public class DrawableImage : BaseDrawable
    {
        public string ImageResourcePath { get; private set; }
        public Vector2 PositionRelative { get; set; } // Logical, unscaled position
        public Vector2 DrawSize { get; set; }         // Logical, unscaled size
        public float RotationAngle { get; set; } = 0f;

        // Store unscaled base values for handles. Scale them when used.
        public static readonly float UnscaledRotationHandleDistance = 20f;
        public static readonly float UnscaledRotationHandleRadius = 5f;
        public static readonly float UnscaledResizeHandleRadius = 4f;

        private IDalamudTextureWrap? textureWrap;

        // Constructor: Initializes a new drawable image.
        // positionRelative and drawSize are expected to be logical, unscaled values.
        // Scaling for drawing is applied in the Draw() method.
        public DrawableImage(DrawMode drawMode, string imageResourcePath, Vector2 positionRelative, Vector2 unscaledDrawSize, Vector4 tint, float rotation = 0f)
        {
            this.ObjectDrawMode = drawMode;
            this.ImageResourcePath = imageResourcePath;
            this.PositionRelative = positionRelative;
            this.DrawSize = unscaledDrawSize; // Store unscaled size
            this.Color = tint;
            this.RotationAngle = rotation;
            this.Thickness = 0; // Not applicable to images
            this.IsFilled = true; // Images are considered "filled"
            this.IsPreview = false; // Typically images are placed, not preview-dragged like shapes
        }

        // Calculates the screen position of the rotation handle.
        public Vector2 GetRotationHandleScreenPosition(Vector2 canvasOriginScreen)
        {
            // PositionRelative and DrawSize are logical. Scale them for screen calculations.
            Vector2 scaledPositionRelative = this.PositionRelative * ImGuiHelpers.GlobalScale;
            Vector2 scaledDrawSize = this.DrawSize * ImGuiHelpers.GlobalScale;
            float scaledRotationHandleDistance = UnscaledRotationHandleDistance * ImGuiHelpers.GlobalScale;

            Vector2 screenCenter = scaledPositionRelative + canvasOriginScreen;
            Vector2 handleOffset = new Vector2(0, -(scaledDrawSize.Y / 2f + scaledRotationHandleDistance));

            float cosA = MathF.Cos(this.RotationAngle);
            float sinA = MathF.Sin(this.RotationAngle);
            Vector2 rotatedHandleOffset = HitDetection.ImRotate(handleOffset, cosA, sinA);
            return screenCenter + rotatedHandleOffset;
        }

        // Retrieves the texture, loading it if necessary.
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

            // Scale logical position and size for drawing.
            Vector2 scaledPositionRelative = this.PositionRelative * ImGuiHelpers.GlobalScale;
            Vector2 scaledDrawSize = this.DrawSize * ImGuiHelpers.GlobalScale;

            Vector2 screenPosCenter = scaledPositionRelative + canvasOriginScreen;

            var displayTintVec = this.IsSelected ? new Vector4(1, 1, 0, 0.7f) :
                                (this.IsHovered && !this.IsSelected ? new Vector4(0.9f, 0.9f, 0.9f, 0.9f) : this.Color);
            uint tintColorU32 = ImGui.GetColorU32(displayTintVec);

            if (tex == null || tex.ImGuiHandle == IntPtr.Zero) // Texture not loaded or invalid.
            {
                // Draw a placeholder if texture is missing.
                drawList.AddRectFilled(screenPosCenter - scaledDrawSize / 2f, screenPosCenter + scaledDrawSize / 2f, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
                Vector2 textSize = ImGui.CalcTextSize("IMG?");
                drawList.AddText(screenPosCenter - textSize / 2f, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), "IMG?");
                return;
            }

            Vector2 scaledHalfSize = scaledDrawSize / 2.0f;
            // GetRotatedQuadVertices expects center and halfSize in screen-space units for drawing.
            // Here, screenPosCenter is already scaled, and scaledHalfSize is also scaled.
            Vector2[] quadVerticesScreen = HitDetection.GetRotatedQuadVertices(screenPosCenter, scaledHalfSize, this.RotationAngle);

            Vector2 uv0 = Vector2.Zero;
            Vector2 uv1 = new Vector2(1, 0);
            Vector2 uv2 = Vector2.One;
            Vector2 uv3 = new Vector2(0, 1);
            drawList.AddImageQuad(tex.ImGuiHandle, quadVerticesScreen[0], quadVerticesScreen[1], quadVerticesScreen[2], quadVerticesScreen[3], uv0, uv1, uv2, uv3, tintColorU32);

            // Draw selection highlights and handles if selected.
            if (this.IsSelected)
            {
                uint highlightColor = ImGui.GetColorU32(new Vector4(1, 1, 0, 1));
                float highlightThickness = 2f * ImGuiHelpers.GlobalScale; // Scale highlight thickness.
                for (int i = 0; i < 4; i++)
                {
                    drawList.AddLine(quadVerticesScreen[i], quadVerticesScreen[(i + 1) % 4], highlightColor, highlightThickness);
                }

                Vector2 rotationHandleScreenPos = GetRotationHandleScreenPosition(canvasOriginScreen); // Already returns scaled pos.
                float scaledRotationHandleRadius = UnscaledRotationHandleRadius * ImGuiHelpers.GlobalScale;
                drawList.AddCircleFilled(rotationHandleScreenPos, scaledRotationHandleRadius, highlightColor);
                drawList.AddCircle(rotationHandleScreenPos, scaledRotationHandleRadius + (1f * ImGuiHelpers.GlobalScale), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), 12, 1.5f * ImGuiHelpers.GlobalScale);

                uint resizeHandleColor = ImGui.GetColorU32(new Vector4(0, 0.8f, 1f, 1f));
                float scaledResizeHandleRadius = UnscaledResizeHandleRadius * ImGuiHelpers.GlobalScale;
                foreach (var corner in quadVerticesScreen) // quadVerticesScreen are already scaled screen positions.
                {
                    drawList.AddCircleFilled(corner, scaledResizeHandleRadius, resizeHandleColor);
                    drawList.AddCircle(corner, scaledResizeHandleRadius + (1f * ImGuiHelpers.GlobalScale), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), 8, 1f * ImGuiHelpers.GlobalScale);
                }
            }
            else if (this.IsHovered) // Draw hover highlight.
            {
                uint hoverColor = ImGui.GetColorU32(new Vector4(0f, 1f, 1f, 0.7f));
                float hoverThickness = 1.5f * ImGuiHelpers.GlobalScale; // Scale hover thickness.
                for (int i = 0; i < 4; i++)
                {
                    drawList.AddLine(quadVerticesScreen[i], quadVerticesScreen[(i + 1) % 4], hoverColor, hoverThickness);
                }
            }
        }

        // queryPointOrEraserCenterRelative is a logical, unscaled coordinate.
        // hitThresholdOrEraserRadius is a logical, unscaled radius.
        public override bool IsHit(Vector2 queryPointOrEraserCenterRelative, float unscaledHitThresholdOrEraserRadius = 5.0f)
        {
            // Perform hit detection in logical, unscaled space.
            Vector2 logicalHalfSize = this.DrawSize / 2f;
            Vector2 imageRectMinRelative = this.PositionRelative - logicalHalfSize;
            Vector2 imageRectMaxRelative = this.PositionRelative + logicalHalfSize;

            // HitDetection.IntersectCircleAABB should work with logical units.
            // The query "circle" (eraser) radius is unscaledHitThresholdOrEraserRadius.
            // The AABB is defined by logical imageRectMinRelative and imageRectMaxRelative.
            return HitDetection.IntersectCircleAABB(queryPointOrEraserCenterRelative, unscaledHitThresholdOrEraserRadius, imageRectMinRelative, imageRectMaxRelative);
        }

        public override BaseDrawable Clone()
        {
            // Pass unscaled DrawSize.
            var newImg = new DrawableImage(this.ObjectDrawMode, this.ImageResourcePath, this.PositionRelative, this.DrawSize, this.Color, this.RotationAngle);
            CopyBasePropertiesTo(newImg);
            return newImg;
        }

        public override void Translate(Vector2 delta) // Delta is a logical, unscaled vector.
        {
            this.PositionRelative += delta;
        }
    }
}
