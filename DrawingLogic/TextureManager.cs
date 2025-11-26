using AetherDraw;
using Dalamud.Interface.Textures.TextureWraps;
using SixLabors.ImageSharp.Formats;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using Lumina.Data.Files;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Lumina.Data;

namespace AetherDraw.DrawingLogic
{
    public static class TextureManager
    {
        private static readonly ConcurrentDictionary<string, IDalamudTextureWrap?> LoadedTextures = new();
        private static readonly ConcurrentDictionary<string, byte[]?> LoadedImageData = new();
        private static readonly ConcurrentBag<string> FailedDownloads = new();
        private static readonly ConcurrentBag<string> PendingDownloads = new();
        private static readonly Dictionary<string, Task<IDalamudTextureWrap>> PendingCreationTasks = new();
        private static readonly ConcurrentQueue<(string resourcePath, byte[] data)> TextureCreationQueue = new();
        private static readonly HttpClient HttpClient = new();

        static TextureManager()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
        }

        public static IDalamudTextureWrap? GetTexture(string resourcePath)
        {
            if (Plugin.TextureProvider == null || string.IsNullOrEmpty(resourcePath)) return null;
            if (FailedDownloads.Contains(resourcePath)) return null;

            if (resourcePath.StartsWith("luminaicon:"))
            {
          
                try
                {
                    if (uint.TryParse(resourcePath.AsSpan("luminaicon:".Length), out uint iconId))
                    {
                        // Construct the internal game path for the icon
                        var iconPath = $"ui/icon/{iconId / 1000 * 1000:000000}/{iconId:000000}.tex";

                        // Use GetTexture (which IS a method of ITextureProvider)
                        var iconTex = Plugin.TextureProvider.GetFromGame(iconPath);

                        if (iconTex != null)
                        {
                            // Convert it to IDalamudTextureWrap using GetWrapOrDefault()
                            var wrappedTex = iconTex.GetWrapOrDefault();
                           
                            return wrappedTex;
                        }
                    }
                    FailedDownloads.Add(resourcePath); // Failed to parse or get icon
                    return null;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Error(ex, $"[TextureManager] Failed to load Lumina icon: {resourcePath}");
                    FailedDownloads.Add(resourcePath);
                    return null;
                }
            }

            if (resourcePath.StartsWith("emoji:"))
            {
                if (LoadedTextures.TryGetValue(resourcePath, out var tex))
                {
                    // using .Equals() to resolve ambiguity
                    if (tex == null || tex.Handle.Handle.Equals(IntPtr.Zero))
                    {
                        LoadedTextures.TryRemove(resourcePath, out _);
                        tex?.Dispose();
                        // Fall through to regeneration logic below
                    }
                    else
                    {
                        return tex;
                    }
                }

                // Lazy Load: If not in cache and not pending, trigger generation now.
                if (!PendingDownloads.Contains(resourcePath))
                {
                    string emojiChar = resourcePath.Substring("emoji:".Length);
                    if (!string.IsNullOrEmpty(emojiChar))
                    {
                        Plugin.Log?.Debug($"[TextureManager] Lazy-loading emoji texture: {emojiChar}");
                        PendingDownloads.Add(resourcePath);
                        Task.Run(() => GenerateAndLoadEmojiTexture(emojiChar, resourcePath));
                    }
                }
                return null;
            }

            if (LoadedTextures.TryGetValue(resourcePath, out var existingTex))
            {
                // using .Equals() to resolve ambiguity
                if (existingTex == null || existingTex.Handle.Handle.Equals(IntPtr.Zero))
                {
                    LoadedTextures.TryRemove(resourcePath, out _);
                    existingTex?.Dispose();
                    return null;
                }
                return existingTex;
            }

            if (!PendingDownloads.Contains(resourcePath) && !PendingCreationTasks.ContainsKey(resourcePath))
            {
                Plugin.Log?.Debug($"[TextureManager] New texture request. Initiating download for: {resourcePath}");
                PendingDownloads.Add(resourcePath);
                Task.Run(() => LoadTextureInBackground(resourcePath));
            }

            return null;
        }


        public static void PreloadEmojiTexture(string emojiChar)
        {
            if (string.IsNullOrEmpty(emojiChar)) return;
            string resourcePath = "emoji:" + emojiChar;
            if (LoadedTextures.ContainsKey(resourcePath) || PendingDownloads.Contains(resourcePath))
                return;

            Plugin.Log?.Debug($"[TextureManager] New emoji texture request. Initiating generation for: {emojiChar}");
            PendingDownloads.Add(resourcePath);
            Task.Run(() => GenerateAndLoadEmojiTexture(emojiChar, resourcePath));
        }

        private static async Task GenerateAndLoadEmojiTexture(string emojiChar, string resourcePath)
        {
            try
            {
                byte[] finalUsableBytes = await EmojiRenderer.RenderEmojiToPngAsync(emojiChar);
                LoadedImageData.TryAdd(resourcePath, finalUsableBytes);
                TextureCreationQueue.Enqueue((resourcePath, finalUsableBytes));
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, $"[TextureManager] Failed to generate texture for emoji: {emojiChar}");
                FailedDownloads.Add(resourcePath);
            }
        }

        public static byte[]? GetImageData(string resourcePath)
        {
            LoadedImageData.TryGetValue(resourcePath, out var data);
            return data;
        }

        public static void DoMainThreadWork()
        {
            if (Plugin.TextureProvider == null) return;

            if (TextureCreationQueue.TryDequeue(out var item))
            {
                Plugin.Log?.Debug($"[TextureManager] Dequeued data for {item.resourcePath}. Starting texture creation task.");
                var creationTask = Plugin.TextureProvider.CreateFromImageAsync(item.data);
                PendingCreationTasks[item.resourcePath] = creationTask;
            }

            if (PendingCreationTasks.Any())
            {
                var completedTasks = PendingCreationTasks.Where(kvp => kvp.Value.IsCompleted).ToList();
                foreach (var completed in completedTasks)
                {
                    var resourcePath = completed.Key;
                    var task = completed.Value;
                    Plugin.Log?.Debug($"[TextureManager] Task for {resourcePath} completed with status: {task.Status}");
                    try
                    {
                        if (task.IsCompletedSuccessfully)
                        {
                            LoadedTextures[resourcePath] = task.Result;
                            Plugin.Log?.Info($"[TextureManager] Successfully created and cached texture for: {resourcePath}");
                        }
                        else
                        {
                            if (task.Exception != null)
                                Plugin.Log?.Error(task.Exception, $"[TextureManager] Texture creation task faulted for {resourcePath}");
                            FailedDownloads.Add(resourcePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.Error(ex, $"[TextureManager] Error processing completed texture task for {resourcePath}");
                        FailedDownloads.Add(resourcePath);
                    }
                    finally
                    {
                        PendingCreationTasks.Remove(resourcePath);
                    }
                }
            }
        }

        private static async Task LoadTextureInBackground(string resourcePath)
        {
            try
            {
                byte[]? rawImageBytes = null;
                if(resourcePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, resourcePath);
                    if (resourcePath.Contains("raidplan.io"))
                    {
                        request.Headers.Referrer = new Uri("https://raidplan.io/");
                    }
                    var response = await HttpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    rawImageBytes = await response.Content.ReadAsByteArrayAsync();
                }
                /*else 
                if (File.Exists(resourcePath))
                {
                    rawImageBytes = await File.ReadAllBytesAsync(resourcePath);
                }*/
                else
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var fullResourcePath = $"{assembly.GetName().Name}.{resourcePath.Replace("\\", ".").Replace("/", ".")}";
                    using var resourceStream = assembly.GetManifestResourceStream(fullResourcePath);
                    if (resourceStream != null) rawImageBytes = ReadStream(resourceStream);
                }

                if (rawImageBytes == null) throw new Exception("Image byte data was null.");

                byte[]? finalUsableBytes = rawImageBytes;

                if (resourcePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log?.Debug($"[TextureManager] Rasterizing SVG for: {resourcePath}");
                    using var stream = new MemoryStream(rawImageBytes);
                    finalUsableBytes = RasterizeSvg(stream);
                }

                if (finalUsableBytes != null)
                {
                    LoadedImageData.TryAdd(resourcePath, finalUsableBytes);
                    TextureCreationQueue.Enqueue((resourcePath, finalUsableBytes));
                }
                else
                {
                    throw new Exception($"Image processing for {resourcePath} resulted in null byte data.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, $"[TextureManager] Download/processing failed for: {resourcePath}");
                FailedDownloads.Add(resourcePath);
            }
        }

        private static byte[]? RasterizeSvg(Stream svgStream)
        {
            using var svg = new SKSvg();
            if (svg.Load(svgStream) is { } && svg.Picture != null)
            {
                var svgSize = svg.Picture.CullRect;
                var width = (int)Math.Ceiling(svgSize.Width);
                var height = (int)Math.Ceiling(svgSize.Height);

                if (width <= 0) width = 64;
                if (height <= 0) height = 64;

                var info = new SKImageInfo(width, height);

                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                var matrix = SKMatrix.CreateScale(
                    (float)info.Width / svgSize.Width,
                    (float)info.Height / svgSize.Height);
                canvas.DrawPicture(svg.Picture, in matrix);

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }
            return null;
        }

        private static byte[] ReadStream(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        public static void Dispose()
        {
            foreach (var texPair in LoadedTextures)
            {
                texPair.Value?.Dispose();
            }
            LoadedTextures.Clear();
            HttpClient.Dispose();
        }
    }
}
