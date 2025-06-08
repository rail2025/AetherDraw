// AetherDraw/DrawingLogic/DrawableImage.cs
using System;
using System.Drawing; // Required for RectangleF
using System.IO;
using System.Numerics;
using System.Reflection;
using AetherDraw.DrawingLogic; // For TextureManager
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using ImGuiNET;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;

namespace AetherDraw.DrawingLogic
{
    /// <summary>
    /// Represents a drawable image object on the canvas.
    /// Handles loading, transformation, tinting, and rendering for both ImGui and image export.
    /// </summary>
    public class DrawableImage : BaseDrawable
    {
        /// <summary>
        /// The path to the image resource, relative to the plugin's embedded resources.
        /// </summary>
        public string ImageResourcePath { get; private set; }
        /// <summary>
        /// The logical, unscaled center position of the image on the canvas.
        /// </summary>
        public Vector2 PositionRelative { get; set; }
        /// <summary>
        /// The logical, unscaled dimensions (width, height) of the image.
        /// </summary>
        public Vector2 DrawSize { get; set; }
        /// <summary>
        /// The rotation angle in radians around the image's center.
        /// </summary>
        public float RotationAngle { get; set; } = 0f;

        // Constants for the visual appearance and interaction of handles.
        public static readonly float UnscaledRotationHandleDistance = 20f;
        public static readonly float UnscaledRotationHandleRadius = 5f;
        public static readonly float UnscaledResizeHandleRadius = 4f;

        // A cache for the loaded texture to avoid reloading from disk every frame.
        private IDalamudTextureWrap? textureWrapCache;

        public DrawableImage(DrawMode drawMode, string imageResourcePath, Vector2 positionRelative, Vector2 unscaledDrawSize, Vector4 tint, float rotation = 0f)
        {
            this.ObjectDrawMode = drawMode;
            this.ImageResourcePath = imageResourcePath;
            this.PositionRelative = positionRelative;
            this.DrawSize = unscaledDrawSize;
            this.Color = tint; // The Color property from BaseDrawable is used as the Tint for images.
            this.RotationAngle = rotation;
            this.Thickness = 0;
            this.IsFilled = true;
            this.IsPreview = false;
        }

        /// <summary>
        /// Draws the image on the ImGui canvas.
        /// </summary>
        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var tex = GetDalamudTextureWrap();
            var displayTintVec = this.IsSelected ? new Vector4(1, 1, 0, 0.7f) : (this.IsHovered && !this.IsSelected ? new Vector4(0.9f, 0.9f, 0.9f, 0.9f) : this.Color);
            uint tintColorU32 = ImGui.GetColorU32(displayTintVec);

            if (tex == null || tex.ImGuiHandle == IntPtr.Zero)
            {
                // Draw a placeholder if the texture fails to load.
                Vector2 screenPosCenter = (this.PositionRelative * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
                Vector2 scaledDrawSize = this.DrawSize * ImGuiHelpers.GlobalScale;
                drawList.AddRectFilled(screenPosCenter - scaledDrawSize / 2f, screenPosCenter + scaledDrawSize / 2f, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
                Vector2 textSize = ImGui.CalcTextSize("IMG?");
                drawList.AddText(screenPosCenter - textSize / 2f, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), "IMG?");
                return;
            }

            // Get the corner positions and scale them for drawing on the screen.
            Vector2[] quadVertices = GetRotatedCorners();
            for (int i = 0; i < quadVertices.Length; i++)
            {
                quadVertices[i] = (quadVertices[i] * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
            }

            // Draw the image quad with the specified corners and tint.
            drawList.AddImageQuad(tex.ImGuiHandle, quadVertices[0], quadVertices[1], quadVertices[2], quadVertices[3],
                                  Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, tintColorU32);
        }

        /// <summary>
        /// Draws the image to an ImageSharp context for image export.
        /// </summary>
        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            byte[]? initialImageBytes = null;
            bool isSvg = ImageResourcePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

            int renderTargetWidth = (int)Math.Max(1, this.DrawSize.X * currentGlobalScale);
            int renderTargetHeight = (int)Math.Max(1, this.DrawSize.Y * currentGlobalScale);

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string fullResourcePath = $"{assembly.GetName().Name}.{ImageResourcePath.Replace("\\", ".").Replace("/", ".")}";

                using (Stream? resourceStream = assembly.GetManifestResourceStream(fullResourcePath))
                {
                    if (resourceStream == null) return;

                    if (isSvg)
                    {
                        using (var svg = new SKSvg())
                        {
                            if (svg.Load(resourceStream) == null || svg.Picture == null) return;
                            using (var skBitmap = new SKBitmap(renderTargetWidth, renderTargetHeight))
                            using (var skCanvas = new SKCanvas(skBitmap))
                            {
                                skCanvas.Clear(SKColors.Transparent);
                                SKMatrix skiaTransformMatrix = SKMatrix.CreateScale((float)renderTargetWidth / svg.Picture.CullRect.Width, (float)renderTargetHeight / svg.Picture.CullRect.Height);
                                skCanvas.DrawPicture(svg.Picture, in skiaTransformMatrix);
                                skCanvas.Flush();

                                using (var skImage = SKImage.FromBitmap(skBitmap))
                                using (var skData = skImage.Encode(SKEncodedImageFormat.Png, 100))
                                {
                                    if (skData == null) return;
                                    initialImageBytes = skData.ToArray();
                                }
                            }
                        }
                    }
                    else
                    {
                        using (var ms = new MemoryStream())
                        {
                            resourceStream.CopyTo(ms);
                            initialImageBytes = ms.ToArray();
                        }
                    }
                }
            }
            catch { return; }

            if (initialImageBytes == null || initialImageBytes.Length == 0) return;

            try
            {
                using (var sourceImage = SixLabors.ImageSharp.Image.Load(initialImageBytes))
                {
                    Image<Rgba32> imageToRenderOnContext = sourceImage.CloneAs<Rgba32>();

                    if (!isSvg && (imageToRenderOnContext.Width != renderTargetWidth || imageToRenderOnContext.Height != renderTargetHeight))
                        imageToRenderOnContext.Mutate(op => op.Resize(renderTargetWidth, renderTargetHeight));

                    if (Math.Abs(this.RotationAngle) > 0.001f)
                        imageToRenderOnContext.Mutate(op => op.Rotate(this.RotationAngle * (180f / MathF.PI)));

                    var targetCenterOnImage = new SixLabors.ImageSharp.PointF(
                        (this.PositionRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                        (this.PositionRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
                    );

                    var drawLocation = new SixLabors.ImageSharp.Point(
                        (int)Math.Round(targetCenterOnImage.X - imageToRenderOnContext.Width / 2f),
                        (int)Math.Round(targetCenterOnImage.Y - imageToRenderOnContext.Height / 2f)
                    );

                    context.DrawImage(imageToRenderOnContext, drawLocation, this.Color.W);

                    var tintColorForBrush = SixLabors.ImageSharp.Color.FromRgba(
                        (byte)(this.Color.X * 255), (byte)(this.Color.Y * 255),
                        (byte)(this.Color.Z * 255), (byte)(this.Color.W * 255));

                    if (this.Color.X < 0.99f || this.Color.Y < 0.99f || this.Color.Z < 0.99f)
                    {
                        var imageBoundsRect = new SixLabors.ImageSharp.RectangleF(drawLocation.X, drawLocation.Y, imageToRenderOnContext.Width, imageToRenderOnContext.Height);
                        var tintDrawingOptions = new DrawingOptions { GraphicsOptions = new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Multiply } };
                        context.Fill(tintDrawingOptions, new SolidBrush(tintColorForBrush), imageBoundsRect);
                    }

                    if (imageToRenderOnContext != sourceImage) imageToRenderOnContext.Dispose();
                }
            }
            catch { /* Log error if necessary */ }
        }

        /// <summary>
        /// Calculates the axis-aligned bounding box that encloses the (potentially rotated) image.
        /// </summary>
        /// <returns>A RectangleF representing the bounding box in logical coordinates.</returns>
        public override System.Drawing.RectangleF GetBoundingBox()
        {
            Vector2[] corners = GetRotatedCorners();

            float minX = corners[0].X, minY = corners[0].Y, maxX = corners[0].X, maxY = corners[0].Y;

            for (int i = 1; i < 4; i++)
            {
                minX = MathF.Min(minX, corners[i].X);
                minY = MathF.Min(minY, corners[i].Y);
                maxX = MathF.Max(maxX, corners[i].X);
                maxY = MathF.Max(maxY, corners[i].Y);
            }

            return new System.Drawing.RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Checks if a point is inside the image's boundaries.
        /// </summary>
        public override bool IsHit(Vector2 queryPointRelative, float hitThreshold = 5.0f)
        {
            // Transform the query point into the image's local, unrotated coordinate space to simplify the check.
            Vector2 localQueryPoint = Vector2.Transform(queryPointRelative - this.PositionRelative, Matrix3x2.CreateRotation(-this.RotationAngle));
            Vector2 logicalHalfSize = this.DrawSize / 2f;
            return Math.Abs(localQueryPoint.X) <= logicalHalfSize.X + hitThreshold &&
                   Math.Abs(localQueryPoint.Y) <= logicalHalfSize.Y + hitThreshold;
        }

        /// <summary>
        /// Creates a deep copy of this DrawableImage.
        /// </summary>
        public override BaseDrawable Clone()
        {
            var newImg = new DrawableImage(this.ObjectDrawMode, this.ImageResourcePath, this.PositionRelative, this.DrawSize, this.Color, this.RotationAngle);
            CopyBasePropertiesTo(newImg);
            return newImg;
        }

        /// <summary>
        /// Moves the image by a given delta.
        /// </summary>
        public override void Translate(Vector2 delta)
        {
            this.PositionRelative += delta;
        }

        /// <summary>
        /// Calculates the four corners of the image in logical space, applying rotation.
        /// This is a helper used by both Draw and GetBoundingBox to avoid duplicate code.
        /// </summary>
        private Vector2[] GetRotatedCorners()
        {
            return HitDetection.GetRotatedQuadVertices(this.PositionRelative, this.DrawSize / 2f, this.RotationAngle);
        }

        /// <summary>
        /// Retrieves the Dalamud texture wrap from the TextureManager, using a local cache.
        /// </summary>
        private IDalamudTextureWrap? GetDalamudTextureWrap()
        {
            if (textureWrapCache == null || textureWrapCache.ImGuiHandle == IntPtr.Zero)
            {
                textureWrapCache = TextureManager.GetTexture(this.ImageResourcePath);
            }
            return textureWrapCache;
        }
    }
}
