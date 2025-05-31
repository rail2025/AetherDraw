// AetherDraw/DrawingLogic/DrawableImage.cs
using System;
using System.Numerics;
using System.IO;
using System.Reflection;
using ImGuiNET;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;

// ImageSharp using statements
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

// SkiaSharp and Svg.Skia for SVG rendering
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
        /// Path to the image resource, relative to the plugin's embedded resources.
        /// </summary>
        public string ImageResourcePath { get; private set; }
        /// <summary>
        /// Logical, unscaled center position of the image on the canvas.
        /// </summary>
        public Vector2 PositionRelative { get; set; }
        /// <summary>
        /// Logical, unscaled dimensions (width, height) of the image. This defines the bounding box
        /// the image (including SVGs) should fill, potentially altering aspect ratio for SVGs.
        /// </summary>
        public Vector2 DrawSize { get; set; }
        /// <summary>
        /// Rotation angle in radians around the image's center.
        /// </summary>
        public float RotationAngle { get; set; } = 0f;

        public static readonly float UnscaledRotationHandleDistance = 20f;
        public static readonly float UnscaledRotationHandleRadius = 5f;
        public static readonly float UnscaledResizeHandleRadius = 4f;

        private IDalamudTextureWrap? textureWrapCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="DrawableImage"/> class.
        /// </summary>
        public DrawableImage(DrawMode drawMode, string imageResourcePath, Vector2 positionRelative, Vector2 unscaledDrawSize, Vector4 tint, float rotation = 0f)
        {
            this.ObjectDrawMode = drawMode;
            this.ImageResourcePath = imageResourcePath;
            this.PositionRelative = positionRelative;
            this.DrawSize = unscaledDrawSize;
            this.Color = tint;
            this.RotationAngle = rotation;
            this.Thickness = 0;
            this.IsFilled = true;
            this.IsPreview = false;
        }

        /// <summary>
        /// Calculates the screen position of the rotation handle for ImGui.
        /// </summary>
        public Vector2 GetRotationHandleScreenPosition(Vector2 canvasOriginScreen)
        {
            Vector2 scaledPositionRelative = this.PositionRelative * ImGuiHelpers.GlobalScale;
            Vector2 scaledDrawSize = this.DrawSize * ImGuiHelpers.GlobalScale;
            float scaledRotationHandleDistance = UnscaledRotationHandleDistance * ImGuiHelpers.GlobalScale;
            Vector2 screenCenter = scaledPositionRelative + canvasOriginScreen;
            Vector2 handleOffset = new Vector2(0, -(scaledDrawSize.Y / 2f + scaledRotationHandleDistance));
            Vector2 rotatedHandleOffset = HitDetection.ImRotate(handleOffset, MathF.Cos(this.RotationAngle), MathF.Sin(this.RotationAngle));
            return screenCenter + rotatedHandleOffset;
        }

        /// <summary>
        /// Retrieves the Dalamud texture wrap, loading from TextureManager if not cached or invalid.
        /// </summary>
        private IDalamudTextureWrap? GetDalamudTextureWrap()
        {
            if (textureWrapCache == null || textureWrapCache.ImGuiHandle == IntPtr.Zero)
            {
                textureWrapCache = TextureManager.GetTexture(this.ImageResourcePath);
            }
            return textureWrapCache;
        }

        /// <summary>
        /// Draws the image on the ImGui canvas. ImGui's AddImageQuad will stretch/scale the texture
        /// to the quad defined by scaledDrawSize and rotation.
        /// </summary>
        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var tex = GetDalamudTextureWrap();
            Vector2 scaledPositionRelative = this.PositionRelative * ImGuiHelpers.GlobalScale;
            Vector2 scaledDrawSize = this.DrawSize * ImGuiHelpers.GlobalScale;
            Vector2 screenPosCenter = scaledPositionRelative + canvasOriginScreen;
            var displayTintVec = this.IsSelected ? new Vector4(1, 1, 0, 0.7f) : (this.IsHovered && !this.IsSelected ? new Vector4(0.9f, 0.9f, 0.9f, 0.9f) : this.Color);
            uint tintColorU32 = ImGui.GetColorU32(displayTintVec);

            if (tex == null || tex.ImGuiHandle == IntPtr.Zero)
            {
                drawList.AddRectFilled(screenPosCenter - scaledDrawSize / 2f, screenPosCenter + scaledDrawSize / 2f, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
                Vector2 textSize = ImGui.CalcTextSize("IMG?");
                drawList.AddText(screenPosCenter - textSize / 2f, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), "IMG?");
                return;
            }
            Vector2 scaledHalfSize = scaledDrawSize / 2.0f;
            Vector2[] quadVerticesScreen = HitDetection.GetRotatedQuadVertices(screenPosCenter, scaledHalfSize, this.RotationAngle);
            drawList.AddImageQuad(tex.ImGuiHandle, quadVerticesScreen[0], quadVerticesScreen[1], quadVerticesScreen[2], quadVerticesScreen[3],
                                  Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, tintColorU32);
        }

        /// <summary>
        /// Draws the image to an ImageSharp context for image export.
        /// For SVGs, this method renders the SVG to match the potentially non-uniform DrawSize,
        /// effectively stretching/squashing the SVG content to fit.
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
                    if (resourceStream == null)
                    {
                        AetherDraw.Plugin.Log?.Error($"[DrawableImage.DrawToImage] Resource stream is null for {fullResourcePath}.");
                        return;
                    }

                    if (isSvg)
                    {
                        using (var svg = new SKSvg())
                        {
                            if (svg.Load(resourceStream) == null || svg.Picture == null)
                            {
                                AetherDraw.Plugin.Log?.Error($"[DrawableImage.DrawToImage] Failed to load SVG picture from stream for {ImageResourcePath}.");
                                return;
                            }

                            using (var skBitmap = new SKBitmap(renderTargetWidth, renderTargetHeight, SKColorType.Rgba8888, SKAlphaType.Premul))
                            using (var skCanvas = new SKCanvas(skBitmap))
                            {
                                skCanvas.Clear(SKColors.Transparent);

                                SKRect svgBounds = svg.Picture.CullRect;
                                SKRect destBounds = new SKRect(0, 0, renderTargetWidth, renderTargetHeight);

                                // Manually construct the transformation matrix to achieve SKMatrixScaleToFit.Fill behavior
                                SKMatrix skiaTransformMatrix = SKMatrix.Identity;
                                if (svgBounds.Width > 0 && svgBounds.Height > 0) // Ensure source bounds are valid
                                {
                                    float scaleX = destBounds.Width / svgBounds.Width;
                                    float scaleY = destBounds.Height / svgBounds.Height;

                                    // Translate SVG origin to 0,0
                                    skiaTransformMatrix = SKMatrix.CreateTranslation(-svgBounds.Left, -svgBounds.Top);
                                    // Scale non-uniformly
                                    SKMatrix scaleMatrix = SKMatrix.CreateScale(scaleX, scaleY);
                                    skiaTransformMatrix = SKMatrix.Concat(skiaTransformMatrix, scaleMatrix);
                                    // Translate to destination origin (which is 0,0 for destBounds)
                                    // No additional translation to destBounds.Left/Top as destBounds starts at 0,0
                                }
                                else
                                {
                                    // Fallback if svgBounds are invalid, prevent division by zero or NaNs
                                    AetherDraw.Plugin.Log?.Warning($"[DrawableImage.DrawToImage] SVG {ImageResourcePath} has invalid original bounds ({svgBounds}). Using identity matrix.");
                                }

                                skCanvas.DrawPicture(svg.Picture, in skiaTransformMatrix);
                                skCanvas.Flush();

                                using (var skImage = SKImage.FromBitmap(skBitmap))
                                using (var skData = skImage.Encode(SKEncodedImageFormat.Png, 100))
                                {
                                    if (skData == null)
                                    {
                                        AetherDraw.Plugin.Log?.Error($"[DrawableImage.DrawToImage] Failed to encode rasterized SVG to PNG for {ImageResourcePath}.");
                                        return;
                                    }
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
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[DrawableImage.DrawToImage] Error loading or rasterizing resource for {ImageResourcePath}.");
                return;
            }

            if (initialImageBytes == null || initialImageBytes.Length == 0)
            {
                AetherDraw.Plugin.Log?.Warning($"[DrawableImage.DrawToImage] Failed to obtain image bytes for: {ImageResourcePath}");
                return;
            }

            try
            {
                using (var sourceImage = SixLabors.ImageSharp.Image.Load(initialImageBytes))
                {
                    Image<Rgba32> imageToRenderOnContext = sourceImage.CloneAs<Rgba32>();

                    if (!isSvg && (imageToRenderOnContext.Width != renderTargetWidth || imageToRenderOnContext.Height != renderTargetHeight))
                    {
                        imageToRenderOnContext.Mutate(op => op.Resize(renderTargetWidth, renderTargetHeight));
                    }

                    if (Math.Abs(this.RotationAngle) > 0.001f)
                    {
                        imageToRenderOnContext.Mutate(op => op.Rotate(this.RotationAngle * (180f / MathF.PI)));
                    }

                    PointF targetCenterOnImage = new PointF(
                        (this.PositionRelative.X * currentGlobalScale) + canvasOriginInOutputImage.X,
                        (this.PositionRelative.Y * currentGlobalScale) + canvasOriginInOutputImage.Y
                    );

                    Point drawLocation = new Point(
                        (int)Math.Round(targetCenterOnImage.X - imageToRenderOnContext.Width / 2f),
                        (int)Math.Round(targetCenterOnImage.Y - imageToRenderOnContext.Height / 2f)
                    );

                    context.DrawImage(imageToRenderOnContext, drawLocation, this.Color.W);

                    var tintColorForBrush = SixLabors.ImageSharp.Color.FromRgba(
                        (byte)(this.Color.X * 255), (byte)(this.Color.Y * 255),
                        (byte)(this.Color.Z * 255), (byte)(this.Color.W * 255));

                    if (this.Color.X < 0.99f || this.Color.Y < 0.99f || this.Color.Z < 0.99f)
                    {
                        var imageBoundsRect = new RectangularPolygon(drawLocation.X, drawLocation.Y, imageToRenderOnContext.Width, imageToRenderOnContext.Height);

                        var tintDrawingOptions = new DrawingOptions
                        {
                            GraphicsOptions = new GraphicsOptions
                            {
                                Antialias = true,
                                ColorBlendingMode = PixelColorBlendingMode.Multiply,
                                BlendPercentage = 1.0f
                            }
                        };
                        context.Fill(tintDrawingOptions, new SolidBrush(tintColorForBrush), imageBoundsRect);
                    }

                    if (imageToRenderOnContext != sourceImage)
                    {
                        imageToRenderOnContext.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[DrawableImage.DrawToImage] Error drawing final image {ImageResourcePath} with ImageSharp.");
            }
        }

        public override bool IsHit(Vector2 queryPointOrEraserCenterRelative, float unscaledHitThresholdOrEraserRadius = 5.0f)
        {
            Vector2 localQueryPoint = Vector2.Transform(queryPointOrEraserCenterRelative - this.PositionRelative, Matrix3x2.CreateRotation(-this.RotationAngle));
            Vector2 logicalHalfSize = this.DrawSize / 2f;
            return Math.Abs(localQueryPoint.X) <= logicalHalfSize.X + unscaledHitThresholdOrEraserRadius &&
                   Math.Abs(localQueryPoint.Y) <= logicalHalfSize.Y + unscaledHitThresholdOrEraserRadius;
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
