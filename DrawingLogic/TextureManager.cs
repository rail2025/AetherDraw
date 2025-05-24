// In AetherDraw/DrawingLogic/TextureManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Dalamud.Interface.Textures.TextureWraps;
using AetherDraw; // Required for accessing Plugin.TextureProvider and Plugin.Log

namespace AetherDraw.DrawingLogic
{
    public static class TextureManager
    {
        private static Dictionary<string, IDalamudTextureWrap?> LoadedTextures = new Dictionary<string, IDalamudTextureWrap?>();

        public static IDalamudTextureWrap? GetTexture(string resourcePathFromPluginRoot)
        {
            // Ensure TextureProvider service is available (should be injected into Plugin.cs)
            if (Plugin.TextureProvider == null)
            {
                Plugin.Log?.Error("TextureProvider service is not available. Textures cannot be loaded by TextureManager.");
                return null;
            }

            // Try to get from cache
            if (LoadedTextures.TryGetValue(resourcePathFromPluginRoot, out var tex))
            {
                // Check if the cached texture is still valid (e.g., not disposed, ImGui handle is good)
                if (tex != null && tex.ImGuiHandle == IntPtr.Zero) // IntPtr.Zero indicates an invalid/disposed texture
                {
                    Plugin.Log?.Warning($"Cached texture for '{resourcePathFromPluginRoot}' was disposed or invalid. Attempting reload.");
                    LoadedTextures.Remove(resourcePathFromPluginRoot); // Remove from cache to force reload
                    tex.Dispose(); // Ensure it's fully disposed if we're discarding it
                    tex = null;    // Set to null to fall through to loading logic
                }
                else if (tex != null) // Texture is valid and cached
                {
                    return tex;
                }
                // If 'tex' was initially null in the dictionary (previous load failure), it will also fall through.
            }

            // If not in cache or was invalid, try to load it
            try
            {
                var assembly = Assembly.GetExecutingAssembly(); // Gets the assembly of this code (AetherDraw.dll)

                // Construct the full embedded resource path.
                // Example: resourcePathFromPluginRoot = "PluginImages.toolbar.Tank.JPG"
                // Assembly Name (e.g., "AetherDraw")
                // Resulting path: "AetherDraw.PluginImages.toolbar.Tank.JPG"
                string fullResourcePath = $"{assembly.GetName().Name}.{resourcePathFromPluginRoot.Replace("\\", ".").Replace("/", ".")}";

                Plugin.Log?.Debug($"Attempting to load embedded resource texture: {fullResourcePath}");

                using (Stream? resourceStream = assembly.GetManifestResourceStream(fullResourcePath))
                {
                    if (resourceStream == null)
                    {
                        Plugin.Log?.Error($"Resource stream is null for '{fullResourcePath}'. Please check: " +
                                          "1. The root namespace of your project (e.g., 'AetherDraw' should match your assembly name). " +
                                          "2. The full path to the image ('PluginImages.toolbar.Tank.JPG'). " +
                                          "3. The image file's 'Build Action' property is set to 'Embedded resource'. " +
                                          "4. Case sensitivity of the path and filename. " +
                                          "5. If the original filename had spaces, they might have been replaced with underscores (_) in the embedded resource name.");
                        LoadedTextures[resourcePathFromPluginRoot] = null; // Cache as null to prevent repeated failed load attempts
                        return null;
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        resourceStream.CopyTo(ms);
                        // Plugin.TextureProvider is expected to be initialized by Dalamud.
                        // CreateFromImageAsync(...).GetAwaiter().GetResult() is a way to synchronously await an async method.
                        var newTex = Plugin.TextureProvider.CreateFromImageAsync(ms.ToArray()).GetAwaiter().GetResult();
                        LoadedTextures[resourcePathFromPluginRoot] = newTex; // Add successfully loaded texture to cache
                        Plugin.Log?.Information($"Successfully loaded texture: {fullResourcePath}");
                        return newTex;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, $"Failed to load texture from resource: {resourcePathFromPluginRoot}");
                LoadedTextures[resourcePathFromPluginRoot] = null; // Cache as null on exception
                return null;
            }
        }

        // Call this when the plugin is disposed to clean up textures
        public static void Dispose()
        {
            Plugin.Log?.Debug("Disposing all textures managed by TextureManager.");
            foreach (var texPair in LoadedTextures)
            {
                texPair.Value?.Dispose(); // Dispose each loaded texture
            }
            LoadedTextures.Clear(); // Clear the cache
        }
    }
}