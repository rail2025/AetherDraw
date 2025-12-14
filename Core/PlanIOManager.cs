using AetherDraw.DrawingLogic;
using AetherDraw.RaidPlan.Models;
using AetherDraw.RaidPlan.Services;
using AetherDraw.Serialization;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using HtmlAgilityPack;
using Dalamud.Bindings.ImGui;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AetherDraw.Core
{
    public class PlanIOManager
    {
        private readonly PageManager pageManager;
        private readonly IDalamudPluginInterface pluginInterface;
        private readonly FileDialogManager fileDialogManager;
        private readonly InPlaceTextEditor inPlaceTextEditor;
        private readonly Func<float> getScaledCanvasGridSizeFunc;
        private readonly Func<DrawMode, int> getLayerPriorityFunc;
        private readonly Func<int> getCurrentPageIndexFunc;
        private static readonly HttpClient HttpClient = new HttpClient();
        //public Action<string>? OnBackgroundImageSelected { get; set; } //user img in case ever solve bad actors problem
        public Action? OnPlanLoadSuccess { get; set; }
        public string LastFileDialogError { get; set; } = string.Empty;

        public PlanIOManager(PageManager pm, InPlaceTextEditor editor, IDalamudPluginInterface pi, Func<float> getGridSizeFunc, Func<DrawMode, int> getPriorityFunc, Func<int> getIndexFunc)
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
            if (pageManager.IsSessionLocked) { LastFileDialogError = "Session is locked."; return; }
            LastFileDialogError = string.Empty;
            string initialPath = GetInitialDialogPath();
            fileDialogManager.OpenFileDialog("Load AetherDraw Plan", "AetherDraw Plan{.adp}", HandleLoadPlanDialogResult, 1, initialPath, true);
        }

        public void RequestAppendPlan()
        {
            if (pageManager.IsSessionLocked) { LastFileDialogError = "Session is locked."; return; }
            LastFileDialogError = string.Empty;
            string initialPath = GetInitialDialogPath();
            fileDialogManager.OpenFileDialog("Append AetherDraw Plan", "AetherDraw Plan{.adp}", HandleAppendPlanDialogResult, 1, initialPath, true);
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

        /*public void RequestLoadCustomBackground()
        {
            LastFileDialogError = string.Empty;
            string initialPath = GetInitialDialogPath();
            fileDialogManager.OpenFileDialog("Import Background Image", "Image Files{.png,.jpg,.jpeg}", HandleLoadBackgroundDialogResult, 1, initialPath, true);
        }*/

        /*private void HandleLoadBackgroundDialogResult(bool success, List<string> paths)
        {
            if (success && paths != null && paths.Count > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                OnBackgroundImageSelected?.Invoke(paths[0]);
            }
            else if (!success)
            {
                LastFileDialogError = "Background import cancelled or failed.";
            }
        }*/
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
                Plugin.Log?.Error(ex, "[PlanIOManager] Error during CopyCurrentPlanToClipboardCompressed.");
            }
        }

        public void RequestLoadPlanFromText(string base64Text)
        {
            if (pageManager.IsSessionLocked) { LastFileDialogError = "Session is locked."; return; }
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
                    Plugin.Log?.Warning("[PlanIOManager] Pasted data was not GZip compressed. Loading as uncompressed.");
                    decompressedBytes = receivedBytes;
                }

                var loadedPlan = PlanSerializer.DeserializePlanFromBytes(decompressedBytes);
                if (loadedPlan == null || loadedPlan.Pages == null)
                {
                    LastFileDialogError = "Failed to read plan data. It might be corrupt or invalid.";
                    return;
                }

                pageManager.LoadPages(loadedPlan.Pages!);
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
                Plugin.Log?.Error(ex, "[PlanIOManager] Error in RequestLoadPlanFromText.");
            }
        }

        public async Task RequestLoadPlanFromUrl(string url)
        {
            if (pageManager.IsSessionLocked) { LastFileDialogError = "Session is locked."; return; }
            var pages = await FetchPagesFromUrl(url);
            if (pages != null && pages.Any())
            {
                pageManager.LoadPages(pages);
                OnPlanLoadSuccess?.Invoke();
            }
        }

        public async Task RequestAppendPlanFromUrl(string url)
        {
            if (pageManager.IsSessionLocked) { LastFileDialogError = "Session is locked."; return; }
            var pages = await FetchPagesFromUrl(url);
            if (pages != null && pages.Any())
            {
                pageManager.AppendPages(pages);
                OnPlanLoadSuccess?.Invoke();
            }
        }

        private async Task<List<PageData>?> FetchPagesFromUrl(string url)
        {
            LastFileDialogError = "Importing from URL...";
            string correctedUrl = url.Trim();
            try
            {
                if (!Uri.TryCreate(correctedUrl, UriKind.Absolute, out Uri? uri))
                {
                    LastFileDialogError = "Invalid URL format.";
                    return null;
                }

                // Case 1: AetherDraw URL
                if (uri.Host.EndsWith("aetherdraw-server.onrender.com") || uri.Host.EndsWith("aetherdraw.me"))
                {
                    var match = Regex.Match(uri.Query, @"[?&]plan=([^&]+)");
                    if (match.Success)
                    {
                        string planId = match.Groups[1].Value;
                        string apiUrl = $"https://aetherdraw-server.onrender.com/plan/load/{planId}";
                        byte[] planDataBytes = await HttpClient.GetByteArrayAsync(apiUrl);
                        var loadedPlan = PlanSerializer.DeserializePlanFromBytes(planDataBytes);

                        if (loadedPlan != null && loadedPlan.Pages != null)
                        {
                            LastFileDialogError = "Successfully imported AetherDraw plan.";
                            return loadedPlan.Pages;
                        }
                        else
                        {
                            LastFileDialogError = "Failed to deserialize plan data from AetherDraw URL.";
                            return null;
                        }
                    }
                }

                // Case 2: Pastebin URL (convert to raw).
                if (Regex.IsMatch(correctedUrl, @"^https?://pastebin\.com/([a-zA-Z0-9]+)$"))
                {
                    correctedUrl = $"https://pastebin.com/raw/{Regex.Match(correctedUrl, @"^https?://pastebin\.com/([a-zA-Z0-9]+)$").Groups[1].Value}";
                }

                // Case 3: RaidPlan.io or Raw Pastebin.
                if (!Uri.TryCreate(correctedUrl, UriKind.Absolute, out Uri? contentUri))
                {
                    LastFileDialogError = "Invalid URL format.";
                    return null;
                }

                string content = await HttpClient.GetStringAsync(contentUri);
                if (contentUri.Host.Contains("raidplan.io"))
                {
                    return await ProcessRaidPlanInBackend(content);
                }
                else
                {
                    // Case 4: Raw text/base64 (Pastebin raw)
                    // Inline logic similar to RequestLoadPlanFromText to return pages instead of void
                    if (string.IsNullOrWhiteSpace(content)) return null;
                    byte[] receivedBytes = Convert.FromBase64String(content);
                    byte[] decompressedBytes;
                    try
                    {
                        using var inputStream = new MemoryStream(receivedBytes);
                        using var outputStream = new MemoryStream();
                        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                            gzipStream.CopyTo(outputStream);
                        decompressedBytes = outputStream.ToArray();
                    }
                    catch (InvalidDataException) { decompressedBytes = receivedBytes; }

                    var loadedPlan = PlanSerializer.DeserializePlanFromBytes(decompressedBytes);
                    if (loadedPlan != null && loadedPlan.Pages != null)
                    {
                        LastFileDialogError = "Plan loaded successfully from text.";
                        return loadedPlan.Pages;
                    }
                }
            }
            catch (Exception ex)
            {
                LastFileDialogError = "Could not retrieve or process data from URL.";
                Plugin.Log?.Error(ex, $"[PlanIOManager] Error fetching from URL {correctedUrl}.");
            }
            return null;
        }

        private async Task<List<PageData>?> ProcessRaidPlanInBackend(string htmlContent)
        {
            Plugin.Log?.Debug($"[PlanIOManager] Starting background processing of HTML content (length: {htmlContent.Length}).");
            LastFileDialogError = "Parsing and translating plan...";

            var resultingPages = await Task.Run(() =>
            {
                try
                {
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(htmlContent);

                    var imageNode = htmlDoc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");
                    string? backgroundImageUrl = null;
                    if (imageNode != null)
                    {
                        backgroundImageUrl = imageNode.GetAttributeValue("content", "") ?? null;
                    }

                    var scriptNode = htmlDoc.DocumentNode.SelectSingleNode("//script[@id='__NEXT_DATA__']");
                    var jsonData = scriptNode?.InnerHtml.Trim();
                    if (string.IsNullOrEmpty(jsonData)) return new List<PageData>();

                    using (var doc = JsonDocument.Parse(jsonData))
                    {
                        if (doc.RootElement.TryGetProperty("props", out var props) &&
                            props.TryGetProperty("pageProps", out var pageProps) &&
                            pageProps.TryGetProperty("_plan", out var planElement))
                        {
                            var planJson = planElement.GetRawText();
                            var raidPlan = JsonSerializer.Deserialize<AetherDraw.RaidPlan.Models.RaidPlan>(planJson);
                            if (raidPlan == null) return new List<PageData>();

                            var translator = new RaidPlanTranslator();
                            return translator.Translate(raidPlan, backgroundImageUrl);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Error(ex, "[PlanIOManager] Error processing the HTML content.");
                    return new List<PageData>();
                }
                return new List<PageData>();
            });

            if (resultingPages != null && resultingPages.Any())
            {
                LastFileDialogError = $"Successfully imported {resultingPages.Count} pages.";
                return resultingPages;
            }
            else
            {
                LastFileDialogError = "Failed to parse or translate RaidPlan data.";
                return new List<PageData>();
            }
        }

        public async Task<string> SubmitPublicPlanAsync(string planName, string bossTag, string creatorName, string planType, string accountKey)
        {
            LastFileDialogError = "Submitting public plan...";
            try
            {
                var currentPages = pageManager.GetAllPages();
                if (currentPages == null || !currentPages.Any())
                    throw new InvalidOperationException("Cannot submit an empty plan.");

                // Serialize plan data
                byte[]? planBytes = PlanSerializer.SerializePlanToBytes(currentPages, planName);
                if (planBytes == null || planBytes.Length == 0)
                    throw new InvalidOperationException("Failed to serialize plan data.");

                using var content = new MultipartFormDataContent();

                // Add file data (matches 'file, _, err := r.FormFile("data")' in handlers.go)
                var fileContent = new ByteArrayContent(planBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "data", "plan.adp");

                // Add metadata fields required by handlers.go
                content.Add(new StringContent(planType ?? "PRIVATE"), "plan_type");
                content.Add(new StringContent(bossTag ?? ""), "boss_tag");
                content.Add(new StringContent(planName ?? "Untitled Plan"), "plan_name");
                content.Add(new StringContent(creatorName ?? ""), "plan_owner");

                if (!string.IsNullOrEmpty(accountKey))
                {
                    content.Add(new StringContent(accountKey), "accountKey");
                }

                // Post to server
                var response = await HttpClient.PostAsync("https://aetherdraw-server.onrender.com/plan/submit", content);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Check for specific name conflict error (403 Forbidden)
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        throw new Exception("This creator name is already in use by another user.");
                    }
                    throw new Exception($"Server error {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                // Parse success response for ID
                using var doc = JsonDocument.Parse(responseString);
                if (doc.RootElement.TryGetProperty("id", out var idElement))
                {
                    string id = idElement.GetString() ?? "";
                    string url = $"https://aetherdraw.me/?plan={id}";
                    LastFileDialogError = "Public plan submitted successfully!";
                    return url;
                }

                throw new Exception("Invalid server response (missing ID).");
            }
            catch (Exception ex)
            {
                LastFileDialogError = $"Submission failed: {ex.Message}";
                Plugin.Log?.Error(ex, "[PlanIOManager] Error submitting public plan.");
                throw;
            }
        }
        public async Task<string> SubmitPrivatePlanAsync(string planName, string accountKey)
        {
            LastFileDialogError = "Submitting private plan...";
            try
            {
                var currentPages = pageManager.GetAllPages();
                if (currentPages == null || !currentPages.Any())
                    throw new InvalidOperationException("Cannot submit an empty plan.");

                byte[]? planBytes = PlanSerializer.SerializePlanToBytes(currentPages, planName);
                if (planBytes == null || planBytes.Length == 0)
                    throw new InvalidOperationException("Failed to serialize plan data.");

                string base64Data = Convert.ToBase64String(planBytes);
                var payload = new
                {
                    name = planName ?? "Untitled Plan",
                    data = base64Data,
                    accountKey = accountKey ?? ""
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await HttpClient.PostAsync("https://aetherdraw-server.onrender.com/plan/save", content);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Server error {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                using var doc = JsonDocument.Parse(responseString);
                if (doc.RootElement.TryGetProperty("id", out var idElement))
                {
                    string id = idElement.GetString() ?? "";
                    string url = $"https://aetherdraw.me/?plan={id}";
                    LastFileDialogError = "Private plan saved successfully!";
                    return url;
                }

                throw new Exception("Invalid server response (missing ID).");
            }
            catch (Exception ex)
            {
                LastFileDialogError = $"Save failed: {ex.Message}";
                Plugin.Log?.Error(ex, "[PlanIOManager] Error submitting private plan.");
                throw;
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

        private void HandleAppendPlanDialogResult(bool success, List<string> paths)
        {
            if (success && paths != null && paths.Count > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                ActuallyAppendPlanFromFile(paths[0]);
            }
            else if (!success)
            {
                LastFileDialogError = "Append operation cancelled or failed.";
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
                    string fullPagePath = System.IO.Path.Combine(directory ?? "", pageSpecificFileName);
                    try
                    {
                        ActuallySaveSinglePageAsImage(currentPageToSave, fullPagePath, canvasVisualSize);
                        successCount++;
                        savedFiles.Add(System.IO.Path.GetFileName(fullPagePath));
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.Error(ex, $"[PlanIOManager] Failed to save page '{currentPageToSave.Name}' to '{fullPagePath}'.");
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
                pageManager.LoadPages(loadedPlan.Pages!);
                LastFileDialogError = $"Plan '{loadedPlan.PlanName}' loaded.";
                OnPlanLoadSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, $"[PlanIOManager] Error loading plan from {filePath}.");
                LastFileDialogError = $"Error loading plan: {ex.Message}";
            }
        }

        private void ActuallyAppendPlanFromFile(string filePath)
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
                    LastFileDialogError = "Failed to read plan file.";
                    return;
                }
                pageManager.AppendPages(loadedPlan.Pages!);
                LastFileDialogError = $"Plan '{loadedPlan.PlanName}' appended.";
                OnPlanLoadSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, $"[PlanIOManager] Error appending plan from {filePath}.");
                LastFileDialogError = $"Error appending plan: {ex.Message}";
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
            string planName = System.IO.Path.GetFileNameWithoutExtension(filePath) ?? "MyAetherDrawPlan";
            try
            {
                string? directory = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
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
                Plugin.Log?.Error(ex, $"[PlanIOManager] Error saving plan to {filePath}.");
                LastFileDialogError = $"Error saving plan: {ex.Message}";
            }
        }

        private void ActuallySaveSinglePageAsImage(PageData pageToSave, string targetFilePath, Vector2 canvasVisualSize)
        {
            string finalFilePath = targetFilePath;
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
