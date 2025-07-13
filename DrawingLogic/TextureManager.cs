using AetherDraw;
using Dalamud.Interface.Textures.TextureWraps;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Concurrent;
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
        private static readonly HttpClient HttpClient = new();

        //Hold actions that need to be run on the main thread.
        private static readonly ConcurrentQueue<Action> MainThreadActions = new();

        static TextureManager()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
            
            // HttpClient.DefaultRequestHeaders.Referrer = new Uri("https://raidplan.io/");
        }

        public static IDalamudTextureWrap? GetTexture(string resourcePath)
        {
            if (Plugin.TextureProvider == null || string.IsNullOrEmpty(resourcePath)) return null;

            if (FailedDownloads.Contains(resourcePath))
            {
                return null;
            }

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

            if (!PendingDownloads.Contains(resourcePath))
            {
                PendingDownloads.Add(resourcePath);
                Task.Run(() => LoadTextureInBackground(resourcePath));
            }

            return null;
        }

        public static void DoMainThreadWork()
        {
            while (MainThreadActions.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }

        private static async Task LoadTextureInBackground(string resourcePath)
        {
            try
            {
                byte[]? imageBytes = null;
                if (resourcePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    imageBytes = await HttpClient.GetByteArrayAsync(resourcePath);
                }
                else
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var fullResourcePath = $"{assembly.GetName().Name}.{resourcePath.Replace("\\", ".").Replace("/", ".")}";
                    using var resourceStream = assembly.GetManifestResourceStream(fullResourcePath);
                    if (resourceStream != null)
                    {
                        imageBytes = ReadStream(resourceStream);
                    }
                }

                if (imageBytes == null) throw new Exception("Image byte data was null after download/resource loading.");

                if (resourcePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new MemoryStream(imageBytes);
                    imageBytes = RasterizeSvg(stream);
                }

                if (imageBytes != null)
                {
                    // queue to do it on the main thread.
                    MainThreadActions.Enqueue(() =>
                    {
                        if (Plugin.TextureProvider == null) return;
                        Plugin.TextureProvider.CreateFromImageAsync(imageBytes).ContinueWith(task =>
                        {
                            if (task.IsCompletedSuccessfully && task.Result != null)
                            {
                                LoadedTextures[resourcePath] = task.Result;
                            }
                            else
                            {
                                AetherDraw.Plugin.Log?.Error(task.Exception, $"Texture creation task failed for {resourcePath}");
                                FailedDownloads.Add(resourcePath);
                                LoadedTextures.TryRemove(resourcePath, out _);
                            }
                        });
                    });
                }
                else
                {
                    throw new Exception("SVG Rasterization resulted in null byte data.");
                }
            }
            catch (Exception ex)
            {
                // Queue the logging and cleanup to happen safely on the main thread.
                MainThreadActions.Enqueue(() =>
                {
                    AetherDraw.Plugin.Log?.Error(ex, $"Failed to load texture: {resourcePath}");
                    FailedDownloads.Add(resourcePath);
                    LoadedTextures.TryRemove(resourcePath, out _);
                });
            }
        }

        private static byte[]? RasterizeSvg(Stream svgStream)
        {
            using var svg = new SKSvg();
            if (svg.Load(svgStream) is { } && svg.Picture != null)
            {
                var rasterWidth = 64;
                var rasterHeight = 64;

                using var surface = SKSurface.Create(new SKImageInfo(rasterWidth, rasterHeight));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                var svgSize = svg.Picture.CullRect;
                if (svgSize.Width > 0 && svgSize.Height > 0)
                {
                    float scale = Math.Min(rasterWidth / svgSize.Width, rasterHeight / svgSize.Height);
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
