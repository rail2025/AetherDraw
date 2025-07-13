using AetherDraw;
using Dalamud.Interface.Textures.TextureWraps;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace AetherDraw.DrawingLogic
{
    public static class TextureManager
    {
        private static readonly ConcurrentDictionary<string, IDalamudTextureWrap?> LoadedTextures = new();
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

            if (LoadedTextures.TryGetValue(resourcePath, out var tex))
            {
                if (tex?.ImGuiHandle == IntPtr.Zero)
                {
                    LoadedTextures.TryRemove(resourcePath, out _);
                    tex?.Dispose();
                    return null;
                }
                return tex;
            }

            if (!PendingDownloads.Contains(resourcePath) && !PendingCreationTasks.ContainsKey(resourcePath))
            {
                Plugin.Log?.Debug($"[TextureManager] New texture request. Initiating download for: {resourcePath}");
                PendingDownloads.Add(resourcePath);
                Task.Run(() => LoadTextureInBackground(resourcePath));
            }

            return null;
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
            Plugin.Log?.Debug($"[TextureManager] Background task started for: {resourcePath}");
            try
            {
                byte[]? imageBytes = null;
                if (resourcePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, resourcePath);
                    if (resourcePath.Contains("raidplan.io"))
                    {
                        request.Headers.Referrer = new Uri("https://raidplan.io/");
                    }
                    var response = await HttpClient.SendAsync(request);
                    Plugin.Log?.Debug($"[TextureManager] HTTP response for {resourcePath}: {response.StatusCode}");
                    response.EnsureSuccessStatusCode();
                    imageBytes = await response.Content.ReadAsByteArrayAsync();
                    Plugin.Log?.Debug($"[TextureManager] Downloaded {imageBytes.Length} bytes for: {resourcePath}");
                }
                else
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var fullResourcePath = $"{assembly.GetName().Name}.{resourcePath.Replace("\\", ".").Replace("/", ".")}";
                    using var resourceStream = assembly.GetManifestResourceStream(fullResourcePath);
                    if (resourceStream != null) imageBytes = ReadStream(resourceStream);
                }

                if (imageBytes == null) throw new Exception("Image byte data was null.");

                if (resourcePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log?.Debug($"[TextureManager] Rasterizing SVG for: {resourcePath}");
                    using var stream = new MemoryStream(imageBytes);
                    imageBytes = RasterizeSvg(stream);
                }

                if (imageBytes != null)
                {
                    Plugin.Log?.Debug($"[TextureManager] Enqueuing texture data for main thread processing: {resourcePath}");
                    TextureCreationQueue.Enqueue((resourcePath, imageBytes));
                }
                else
                {
                    throw new Exception("Image processing resulted in null byte data.");
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
                using var surface = SKSurface.Create(new SKImageInfo(64, 64));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                var svgSize = svg.Picture.CullRect;
                if (svgSize.Width > 0 && svgSize.Height > 0)
                {
                    float scale = Math.Min(64 / svgSize.Width, 64 / svgSize.Height);
                    var matrix = SKMatrix.CreateScale(scale, scale);
                    canvas.DrawPicture(svg.Picture, in matrix);
                }
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
