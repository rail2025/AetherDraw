using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Dalamud.Interface.Textures.TextureWraps;
using AetherDraw;
using Svg.Skia;
using SkiaSharp;

namespace AetherDraw.DrawingLogic
{
    public static class TextureManager
    {
        private static Dictionary<string, IDalamudTextureWrap?> LoadedTextures = new Dictionary<string, IDalamudTextureWrap?>();
        private const int DefaultRenderWidth = 256; // Desired render width for SVGs
        private const int DefaultRenderHeight = 256; // Desired render height for SVGs

        public static IDalamudTextureWrap? GetTexture(string resourcePathFromPluginRoot)
        {
            if (Plugin.TextureProvider == null)
            {
                Plugin.Log?.Error("TextureProvider service is not available. Textures cannot be loaded by TextureManager.");
                return null;
            }

            if (LoadedTextures.TryGetValue(resourcePathFromPluginRoot, out var tex))
            {
                if (tex != null && tex.ImGuiHandle == IntPtr.Zero)
                {
                    Plugin.Log?.Warning($"Cached texture for '{resourcePathFromPluginRoot}' was disposed or invalid. Attempting reload.");
                    tex.Dispose();
                    LoadedTextures.Remove(resourcePathFromPluginRoot);
                    tex = null;
                }
                else if (tex != null)
                {
                    return tex;
                }
                
            }

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string fullResourcePath = $"{assembly.GetName().Name}.{resourcePathFromPluginRoot.Replace("\\", ".").Replace("/", ".")}";

                Plugin.Log?.Debug($"Attempting to load embedded resource: {fullResourcePath}");

                using (Stream? resourceStream = assembly.GetManifestResourceStream(fullResourcePath))
                {
                    if (resourceStream == null)
                    {
                        Plugin.Log?.Error($"Resource stream is null for '{fullResourcePath}'. Check path, build action, and case sensitivity.");
                        LoadedTextures[resourcePathFromPluginRoot] = null;
                        return null;
                    }

                    byte[] imageBytes;
                    bool isSvg = fullResourcePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

                    if (isSvg)
                    {
                        Plugin.Log?.Debug($"Processing SVG resource: {fullResourcePath} for target render size {DefaultRenderWidth}x{DefaultRenderHeight}");
                        try
                        {
                            using var svg = new SKSvg();
                            if (svg.Load(resourceStream) == null || svg.Picture == null)
                            {
                                Plugin.Log?.Error($"Failed to load SVG picture from stream or SVG is empty: {fullResourcePath}");
                                LoadedTextures[resourcePathFromPluginRoot] = null;
                                return null;
                            }

                            // Get original SVG content dimensions
                            float svgContentWidth = svg.Picture.CullRect.Width;
                            float svgContentHeight = svg.Picture.CullRect.Height;
                            SKPoint svgContentOrigin = svg.Picture.CullRect.Location;

                            if (svgContentWidth <= 0 || svgContentHeight <= 0)
                            {
                                if (svgContentWidth <= 0 || svgContentHeight <= 0)
                                {
                                    Plugin.Log?.Warning($"SVG {fullResourcePath} has non-positive dimensions in CullRect ({svgContentWidth}x{svgContentHeight}). Defaulting content size to render target dimensions ({DefaultRenderWidth}x{DefaultRenderHeight}) for scaling purposes.");
                                    svgContentWidth = DefaultRenderWidth; // Default to render target width
                                    svgContentHeight = DefaultRenderHeight; // Default to render target height
                                    svgContentOrigin = SKPoint.Empty; // Assume origin is 0,0 if CullRect was not useful
                                }
                                else
                                {
                                    Plugin.Log?.Warning($"SVG {fullResourcePath} has no usable dimensions (CullRect/IntrinsicSize). Rendering at {DefaultRenderWidth}x{DefaultRenderHeight} without proper scaling.");
                                    svgContentWidth = DefaultRenderWidth;
                                    svgContentHeight = DefaultRenderHeight;
                                    svgContentOrigin = SKPoint.Empty;
                                }
                            }

                            using var bitmap = new SKBitmap(DefaultRenderWidth, DefaultRenderHeight);
                            using var canvas = new SKCanvas(bitmap);
                            canvas.Clear(SKColors.Transparent); // Ensure transparent background

                            // Calculate scaling factors to fit SVG content into DefaultRenderWidth/Height, maintaining aspect ratio
                            float scaleX = DefaultRenderWidth / svgContentWidth;
                            float scaleY = DefaultRenderHeight / svgContentHeight;
                            float scale = Math.Min(scaleX, scaleY); // Use the smaller scale factor to fit entirely and maintain aspect ratio

                            // Calculate translation to center the scaled SVG
                            float translateX = (DefaultRenderWidth - (svgContentWidth * scale)) / 2f;
                            float translateY = (DefaultRenderHeight - (svgContentHeight * scale)) / 2f;

                            SKMatrix matrix = SKMatrix.CreateIdentity();
                            // 1. Translate the SVG's native origin (CullRect.Location) to 0,0
                            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateTranslation(-svgContentOrigin.X, -svgContentOrigin.Y));
                            // 2. Scale the SVG content
                            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale(scale, scale));
                            // 3. Translate the scaled SVG to its centered position on the canvas
                            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateTranslation(translateX, translateY));

                            canvas.DrawPicture(svg.Picture, in matrix);
                            canvas.Flush();

                            using var skImage = SKImage.FromBitmap(bitmap);
                            using var data = skImage.Encode(SKEncodedImageFormat.Png, 100); // Encode to PNG, 100 quality
                            if (data == null)
                            {
                                Plugin.Log?.Error($"Failed to encode SVG (rendered at {DefaultRenderWidth}x{DefaultRenderHeight}) to PNG: {fullResourcePath}");
                                LoadedTextures[resourcePathFromPluginRoot] = null;
                                return null;
                            }
                            imageBytes = data.ToArray();
                        }
                        catch (Exception exSvg)
                        {
                            Plugin.Log?.Error(exSvg, $"Failed to process SVG resource: {fullResourcePath}");
                            LoadedTextures[resourcePathFromPluginRoot] = null;
                            return null;
                        }
                    }
                    else // Existing logic for non-SVG (JPG, PNG, etc.)
                    {
                        Plugin.Log?.Debug($"Processing non-SVG resource: {fullResourcePath}");
                        using (MemoryStream ms = new MemoryStream())
                        {
                            resourceStream.CopyTo(ms);
                            imageBytes = ms.ToArray();
                        }
                    }

                    if (imageBytes == null || imageBytes.Length == 0)
                    {
                        Plugin.Log?.Error($"Image byte array is null or empty for {fullResourcePath} after processing.");
                        LoadedTextures[resourcePathFromPluginRoot] = null;
                        return null;
                    }

                    var newTexture = Plugin.TextureProvider.CreateFromImageAsync(imageBytes).GetAwaiter().GetResult();
                    LoadedTextures[resourcePathFromPluginRoot] = newTexture; // Add successfully loaded texture to cache
                    Plugin.Log?.Information($"Successfully loaded texture: {fullResourcePath} (Type: {(isSvg ? $"SVG->PNG@{DefaultRenderWidth}x{DefaultRenderHeight}" : "Bitmap")}, Size: {newTexture?.Width}x{newTexture?.Height})");
                    return newTexture;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, $"Failed to load texture from resource: {resourcePathFromPluginRoot}");
                LoadedTextures[resourcePathFromPluginRoot] = null; // Cache as null on exception
                return null;
            }
        }

        public static void Dispose()
        {
            Plugin.Log?.Debug("Disposing all textures managed by TextureManager.");
            foreach (var texPair in LoadedTextures)
            {
                texPair.Value?.Dispose();
            }
            LoadedTextures.Clear();
        }
    }
}
