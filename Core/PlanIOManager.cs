// AetherDraw/Core/PlanIOManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AetherDraw.DrawingLogic;
using AetherDraw.Serialization;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Plugin;
using Dalamud.Interface.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using ImGuiNET;
using System.Threading.Tasks;
using System.Text.Json;
using AetherDraw.RaidPlan.Models;
using AetherDraw.RaidPlan.Services;
using HtmlAgilityPack;

namespace AetherDraw.Core
{
    public class PlanIOManager
    {
        private readonly PageManager pageManager;
        private readonly IDalamudPluginInterface pluginInterface;
        private readonly FileDialogManager fileDialogManager;
        private readonly DrawingLogic.InPlaceTextEditor inPlaceTextEditor;
        private readonly Func<float> getScaledCanvasGridSizeFunc;
        private readonly Func<DrawMode, int> getLayerPriorityFunc;
        private readonly Func<int> getCurrentPageIndexFunc;
        private static readonly HttpClient HttpClient = new HttpClient();
        public Action? OnPlanLoadSuccess { get; set; }
        public string LastFileDialogError { get; private set; } = string.Empty;

        public PlanIOManager(PageManager pm, DrawingLogic.InPlaceTextEditor editor, IDalamudPluginInterface pi, Func<float> getGridSizeFunc, Func<DrawMode, int> getPriorityFunc, Func<int> getIndexFunc)
        {
            this.pageManager = pm ?? throw new ArgumentNullException(nameof(pm));
            this.inPlaceTextEditor = editor ?? throw new ArgumentNullException(nameof(editor));
            this.pluginInterface = pi ?? throw new ArgumentNullException(nameof(pi));
            this.fileDialogManager = new FileDialogManager();
            this.getScaledCanvasGridSizeFunc = getGridSizeFunc ?? throw new ArgumentNullException(nameof(getGridSizeFunc));
            this.getLayerPriorityFunc = getPriorityFunc ?? throw new ArgumentNullException(nameof(getPriorityFunc));
            this.getCurrentPageIndexFunc = getIndexFunc ?? throw new ArgumentNullException(nameof(getIndexFunc));
        }

        public void DrawFileDialogs() => this.fileDialogManager.Draw();

        public void RequestLoadPlan()
        {
            LastFileDialogError = string.Empty;
            string initialPath = GetInitialDialogPath();
            fileDialogManager.OpenFileDialog("Load AetherDraw Plan", "AetherDraw Plan{.adp}", HandleLoadPlanDialogResult, 1, initialPath, true);
        }

        public void RequestSavePlan()
        {
            LastFileDialogError = string.Empty;
            string initialPath = GetInitialDialogPath();
            fileDialogManager.SaveFileDialog("Save AetherDraw Plan As...", "AetherDraw Plan{.adp}", "MyAetherDrawPlan", ".adp", HandleSavePlanDialogResult, initialPath, true);
        }

        public void RequestSaveImage(Vector2 currentCanvasVisualSize)
        {
            LastFileDialogError = string.Empty;
            string initialPath = GetInitialDialogPath();
            fileDialogManager.SaveFileDialog("Save Image As...", "PNG Image{.png,.PNG}", "MyAetherDrawImage", ".png", (success, path) => HandleSaveImageDialogResult(success, path, currentCanvasVisualSize), initialPath, true);
        }

        public void CopyCurrentPlanToClipboardCompressed()
        {
            LastFileDialogError = string.Empty;
            try
            {
#pragma warning disable CS8600
                byte[] planBytes = PlanSerializer.SerializePlanToBytes(pageManager.GetAllPages(), "clipboard_plan");
#pragma warning restore CS8600
                if (planBytes == null || planBytes.Length == 0)
                {
                    LastFileDialogError = "Nothing to copy.";
                    return;
                }
                using var outputStream = new MemoryStream();
                using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
                {
                    gzipStream.Write(planBytes, 0, planBytes.Length);
                }
                string base64String = Convert.ToBase64String(outputStream.ToArray());
                ImGui.SetClipboardText(base64String);
                LastFileDialogError = "Compressed plan data copied to clipboard!";
            }
            catch (Exception ex)
            {
                LastFileDialogError = "Failed to copy plan to clipboard.";
                AetherDraw.Plugin.Log?.Error(ex, "[PlanIOManager] Error during CopyCurrentPlanToClipboardCompressed.");
            }
        }

        public void RequestLoadPlanFromText(string base64Text)
        {
            LastFileDialogError = string.Empty;
            if (string.IsNullOrWhiteSpace(base64Text))
            {
                LastFileDialogError = "Pasted text is empty.";
                return;
            }
            try
            {
                byte[] receivedBytes = Convert.FromBase64String(base64Text);
                byte[] decompressedBytes;
                try
                {
                    using var inputStream = new MemoryStream(receivedBytes);
                    using var outputStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                    {
                        gzipStream.CopyTo(outputStream);
                    }
                    decompressedBytes = outputStream.ToArray();
                }
                catch (InvalidDataException)
                {
                    AetherDraw.Plugin.Log?.Warning("[PlanIOManager] Pasted data was not GZip compressed. Loading as uncompressed.");
                    decompressedBytes = receivedBytes;
                }
                var loadedPlan = PlanSerializer.DeserializePlanFromBytes(decompressedBytes);
                if (loadedPlan == null || loadedPlan.Pages == null)
                {
                    LastFileDialogError = "Failed to read plan data. It might be corrupt or invalid.";
                    return;
                }
                pageManager.LoadPages(loadedPlan.Pages);
                LastFileDialogError = "Plan loaded successfully from text.";
                OnPlanLoadSuccess?.Invoke();
            }
            catch (FormatException)
            {
                LastFileDialogError = "Invalid format. Data must be a Base64 string.";
            }
            catch (Exception ex)
            {
                LastFileDialogError = "An error occurred while loading the data.";
                AetherDraw.Plugin.Log?.Error(ex, "[PlanIOManager] Error in RequestLoadPlanFromText.");
            }
        }

        public async Task RequestLoadPlanFromUrl(string url)
        {
            LastFileDialogError = "Importing from URL...";
            string correctedUrl = url.Trim();
            if (Regex.IsMatch(correctedUrl, @"^https?://pastebin\.com/([a-zA-Z0-9]+)$"))
            {
                correctedUrl = $"https://pastebin.com/raw/{Regex.Match(correctedUrl, @"^https?://pastebin\.com/([a-zA-Z0-9]+)$").Groups[1].Value}";
            }
            if (!Uri.TryCreate(correctedUrl, UriKind.Absolute, out Uri? uriResult))
            {
                LastFileDialogError = "Invalid or unsupported URL format.";
                return;
            }
            try
            {
                string content = await HttpClient.GetStringAsync(uriResult);
                bool isRaidPlan = uriResult.Host.EndsWith("raidplan.io");
                if (isRaidPlan)
                {
                    await ProcessRaidPlanInBackend(content);
                }
                else
                {
                    RequestLoadPlanFromText(content);
                }
            }
            catch (Exception ex)
            {
                LastFileDialogError = "Could not retrieve data from URL.";
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Error loading from URL {correctedUrl}.");
            }
        }

        private async Task ProcessRaidPlanInBackend(string htmlContent)
        {
            LastFileDialogError = "Parsing and translating plan...";
            var resultingPages = await Task.Run(() =>
            {
                try
                {
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(htmlContent);
                    var scriptNode = htmlDoc.DocumentNode.SelectSingleNode("//script[@id='__NEXT_DATA__']");
                    var jsonData = scriptNode?.InnerHtml.Trim();
                    if (string.IsNullOrEmpty(jsonData))
                    {
                        AetherDraw.Plugin.Log?.Warning("[PlanIOManager] Could not find '__NEXT_DATA__' script block.");
                        return null;
                    }
                    using (var doc = JsonDocument.Parse(jsonData))
                    {
                        if (doc.RootElement.TryGetProperty("props", out var props) &&
                            props.TryGetProperty("pageProps", out var pageProps) &&
                            pageProps.TryGetProperty("_plan", out var planElement))
                        {
                            var planJson = planElement.GetRawText();
                            // FIX CS0118: Fully qualify the RaidPlan type
                            var raidPlan = JsonSerializer.Deserialize<AetherDraw.RaidPlan.Models.RaidPlan>(planJson);
                            if (raidPlan == null) return null;
                            var translator = new RaidPlanTranslator();
                            return translator.Translate(raidPlan);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AetherDraw.Plugin.Log?.Error(ex, "[PlanIOManager] Error parsing or translating RaidPlan JSON.");
                    return null;
                }
                return null;
            });
            if (resultingPages != null && resultingPages.Any())
            {
                pageManager.LoadPages(resultingPages);
                LastFileDialogError = $"Successfully imported {resultingPages.Count} pages.";
                OnPlanLoadSuccess?.Invoke();
            }
            else
            {
                LastFileDialogError = "Failed to parse or translate RaidPlan data.";
            }
        }

        private string GetInitialDialogPath()
        {
            string path = pluginInterface.GetPluginConfigDirectory();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            return path;
        }

        private void HandleLoadPlanDialogResult(bool success, List<string> paths)
        {
            if (success && paths != null && paths.Count > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                ActuallyLoadPlanFromFile(paths[0]);
            }
            else if (!success)
            {
                LastFileDialogError = "Load operation cancelled or failed.";
            }
        }

        private void HandleSavePlanDialogResult(bool success, string filePath)
        {
            if (success && !string.IsNullOrEmpty(filePath))
            {
                ActuallySavePlanToFile(filePath);
            }
            else if (!success)
            {
                LastFileDialogError = "Save plan operation cancelled or failed.";
            }
        }

        private void HandleSaveImageDialogResult(bool success, string baseFilePathFromDialog, Vector2 canvasVisualSize)
        {
            if (success && !string.IsNullOrEmpty(baseFilePathFromDialog))
            {
                // FIX CS0104: Use System.IO.Path to resolve ambiguity
                string? directory = System.IO.Path.GetDirectoryName(baseFilePathFromDialog);
                string baseNameOnly = System.IO.Path.GetFileNameWithoutExtension(baseFilePathFromDialog) ?? "MyAetherDrawImage";
                string targetExtension = ".png";
                var currentPages = pageManager.GetAllPages();
                if (currentPages.Count == 0)
                {
                    LastFileDialogError = "No pages to save.";
                    return;
                }
                int successCount = 0;
                int failureCount = 0;
                List<string> savedFiles = new List<string>();
                for (int i = 0; i < currentPages.Count; i++)
                {
                    PageData currentPageToSave = currentPages[i];
                    string pagePrefix = $"{i + 1}";
                    string pageSpecificFileName = $"{pagePrefix}-{baseNameOnly}{targetExtension}";
                    // FIX CS0104: Use System.IO.Path to resolve ambiguity
                    string fullPagePath = System.IO.Path.Combine(directory ?? "", pageSpecificFileName);
                    try
                    {
                        ActuallySaveSinglePageAsImage(currentPageToSave, fullPagePath, canvasVisualSize);
                        successCount++;
                        // FIX CS0104: Use System.IO.Path to resolve ambiguity
                        savedFiles.Add(System.IO.Path.GetFileName(fullPagePath));
                    }
                    catch (Exception ex)
                    {
                        AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Failed to save page '{currentPageToSave.Name}' to '{fullPagePath}'.");
                        failureCount++;
                    }
                }
                if (failureCount > 0) LastFileDialogError = $"Saved {successCount} page(s). Failed to save {failureCount} page(s). Check log.";
                else if (successCount > 0) LastFileDialogError = $"Successfully saved: {string.Join(", ", savedFiles)}";
                else LastFileDialogError = "No pages were processed or saved.";
            }
            else if (!success)
            {
                LastFileDialogError = "Save image operation cancelled.";
            }
        }

        private void ActuallyLoadPlanFromFile(string filePath)
        {
            LastFileDialogError = string.Empty;
            if (!File.Exists(filePath))
            {
                LastFileDialogError = "Plan file not found.";
                return;
            }
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                var loadedPlan = PlanSerializer.DeserializePlanFromBytes(fileData);
                if (loadedPlan == null || loadedPlan.Pages == null)
                {
                    LastFileDialogError = "Failed to read plan file. It might be corrupt, an incompatible version, or empty.";
                    return;
                }
                pageManager.LoadPages(loadedPlan.Pages);
                LastFileDialogError = $"Plan '{loadedPlan.PlanName}' loaded.";
                OnPlanLoadSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Error loading plan from {filePath}.");
                LastFileDialogError = $"Error loading plan: {ex.Message}";
            }
        }

        private void ActuallySavePlanToFile(string filePath)
        {
            var currentPages = pageManager.GetAllPages();
            LastFileDialogError = string.Empty;
            if (!currentPages.Any() || !currentPages.Any(p => p.Drawables.Any()))
            {
                LastFileDialogError = "Nothing to save in the current plan.";
                return;
            }
            // FIX CS0104: Use System.IO.Path to resolve ambiguity
            string planName = System.IO.Path.GetFileNameWithoutExtension(filePath) ?? "MyAetherDrawPlan";
            try
            {
                // FIX CS0104: Use System.IO.Path to resolve ambiguity
                string? directory = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                byte[]? serializedPlanData = PlanSerializer.SerializePlanToBytes(currentPages, planName, 1, 1, 0);
                if (serializedPlanData != null)
                {
                    File.WriteAllBytes(filePath, serializedPlanData);
                    LastFileDialogError = $"Plan '{planName}' saved.";
                }
                else
                {
                    LastFileDialogError = "Failed to serialize plan data.";
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Error saving plan to {filePath}.");
                LastFileDialogError = $"Error saving plan: {ex.Message}";
            }
        }

        private void ActuallySaveSinglePageAsImage(PageData pageToSave, string targetFilePath, Vector2 canvasVisualSize)
        {
            string finalFilePath = targetFilePath;
            // FIX CS0104: Use System.IO.Path to resolve ambiguity
            if (!string.IsNullOrEmpty(finalFilePath) && !finalFilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                finalFilePath = System.IO.Path.ChangeExtension(finalFilePath, ".png");
            }
            try
            {
                int imageWidth = (int)Math.Max(100, canvasVisualSize.X);
                int imageHeight = (int)Math.Max(100, canvasVisualSize.Y);
                if (imageWidth <= 0 || imageHeight <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(canvasVisualSize), "Canvas dimensions for image export must be positive.");
                }
                using (var image = new Image<Rgba32>(imageWidth, imageHeight))
                {
                    var backgroundColor = new Rgba32((byte)(0.15f * 255), (byte)(0.15f * 255), (byte)(0.17f * 255), (byte)(1.0f * 255));
                    image.Mutate(ctx => ctx.Fill(backgroundColor));
                    var gridColor = Color.FromRgba((byte)(0.3f * 255), (byte)(0.3f * 255), (byte)(0.3f * 255), (byte)(1.0f * 255));
                    float gridLineThickness = 1f;
                    float scaledGridCellSize = getScaledCanvasGridSizeFunc();
                    if (scaledGridCellSize > 0)
                    {
                        image.Mutate(ctx =>
                        {
                            for (float x = scaledGridCellSize; x < imageWidth; x += scaledGridCellSize)
                            {
                                var pathBuilder = new PathBuilder();
                                pathBuilder.AddLine(new PointF(x, 0), new PointF(x, imageHeight));
                                ctx.Draw(gridColor, gridLineThickness, pathBuilder.Build());
                            }
                            for (float y = scaledGridCellSize; y < imageHeight; y += scaledGridCellSize)
                            {
                                var pathBuilder = new PathBuilder();
                                pathBuilder.AddLine(new PointF(0, y), new PointF(imageWidth, y));
                                ctx.Draw(gridColor, gridLineThickness, pathBuilder.Build());
                            }
                        });
                    }
                    var drawablesToRender = pageToSave.Drawables.OrderBy(d => getLayerPriorityFunc(d.ObjectDrawMode)).ToList();
                    Vector2 imageOrigin = Vector2.Zero;
                    float scale = ImGuiHelpers.GlobalScale;
                    image.Mutate(ctx =>
                    {
                        foreach (var drawable in drawablesToRender)
                        {
                            bool isCurrentlyEditedDrawable = false;
                            if (pageManager.GetAllPages().IndexOf(pageToSave) == getCurrentPageIndexFunc())
                            {
                                isCurrentlyEditedDrawable = inPlaceTextEditor.IsEditing && inPlaceTextEditor.IsCurrentlyEditing(drawable);
                            }
                            if (isCurrentlyEditedDrawable) continue;
                            drawable.DrawToImage(ctx, imageOrigin, scale);
                        }
                    });
                    image.SaveAsPng(finalFilePath);
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Error during image saving for page '{pageToSave.Name}' to {targetFilePath}.");
                throw;
            }
        }
    }
}
