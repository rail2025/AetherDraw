// AetherDraw/Core/PlanIOManager.cs
using System;
using System.Collections.Generic;
using System.IO; // Specifically for System.IO.Path, Directory, File
using System.Linq;
using System.Numerics;
using AetherDraw.DrawingLogic; // For BaseDrawable, DrawMode, and InPlaceTextEditor
using AetherDraw.Serialization; // For PlanSerializer
using Dalamud.Interface.ImGuiFileDialog; // For FileDialogManager
using Dalamud.Plugin; // For IDalamudPluginInterface
using Dalamud.Interface.Utility; // For ImGuiHelpers

// ImageSharp specific usings for image manipulation and saving
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats; // For Rgba32
using SixLabors.ImageSharp.Processing; // For Mutate, Resize, Rotate etc.
using SixLabors.ImageSharp.Drawing; // For PathBuilder, Pens etc.
using SixLabors.ImageSharp.Drawing.Processing; // For Fill, Draw extension methods
using SixLabors.Fonts; // For Font, FontFamily, RichTextOptions etc.

namespace AetherDraw.Core
{
    /// <summary>
    /// Manages file input/output operations for AetherDraw.
    /// This includes loading and saving multi-page plans, as well as exporting
    /// individual pages or entire plans as images. It encapsulates the
    /// logic for interacting with file dialogs and the file system.
    /// </summary>
    public class PlanIOManager
    {
        // Dependencies injected via constructor
        private readonly PageManager pageManager;
        private readonly IDalamudPluginInterface pluginInterface;
        private readonly FileDialogManager fileDialogManager;
        private readonly DrawingLogic.InPlaceTextEditor inPlaceTextEditor;

        // Delegates to retrieve necessary state or configuration from the main window or other services
        private readonly Func<float> getScaledCanvasGridSizeFunc;
        private readonly Func<DrawMode, int> getLayerPriorityFunc;
        private readonly Func<int> getCurrentPageIndexFunc;

        /// <summary>
        /// Event triggered when a plan is successfully loaded from a file.
        /// The main window can subscribe to this to perform UI updates or state resets.
        /// </summary>
        public Action? OnPlanLoadSuccess { get; set; }

        /// <summary>
        /// Gets the last error message encountered during a file dialog operation.
        /// This can be displayed to the user in the UI.
        /// </summary>
        public string LastFileDialogError { get; private set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlanIOManager"/> class.
        /// </summary>
        /// <param name="pm">The <see cref="PageManager"/> instance for accessing page data.</param>
        /// <param name="editor">The <see cref="DrawingLogic.InPlaceTextEditor"/> instance to check for active text editing states, primarily during image saving.</param>
        /// <param name="pi">The Dalamud <see cref="IDalamudPluginInterface"/> for accessing plugin-specific paths and logging.</param>
        /// <param name="getGridSizeFunc">A function delegate to retrieve the current scaled canvas grid size, used for rendering images.</param>
        /// <param name="getPriorityFunc">A function delegate to retrieve the layer priority for a given <see cref="DrawMode"/>, used for ordering drawables in image export.</param>
        /// <param name="getIndexFunc">A function delegate to retrieve the current active page index, used for context-aware operations like checking if an edited text object is on the page being saved.</param>
        public PlanIOManager(
            PageManager pm,
            DrawingLogic.InPlaceTextEditor editor,
            IDalamudPluginInterface pi,
            Func<float> getGridSizeFunc,
            Func<DrawMode, int> getPriorityFunc,
            Func<int> getIndexFunc)
        {
            this.pageManager = pm ?? throw new ArgumentNullException(nameof(pm));
            this.inPlaceTextEditor = editor ?? throw new ArgumentNullException(nameof(editor));
            this.pluginInterface = pi ?? throw new ArgumentNullException(nameof(pi));
            this.fileDialogManager = new FileDialogManager();
            this.getScaledCanvasGridSizeFunc = getGridSizeFunc ?? throw new ArgumentNullException(nameof(getGridSizeFunc));
            this.getLayerPriorityFunc = getPriorityFunc ?? throw new ArgumentNullException(nameof(getPriorityFunc));
            this.getCurrentPageIndexFunc = getIndexFunc ?? throw new ArgumentNullException(nameof(getIndexFunc));
        }

        /// <summary>
        /// Draws any active file dialogs. This method should be called
        /// from the main plugin window's drawing loop to render ImGuiFileDialogs.
        /// </summary>
        public void DrawFileDialogs()
        {
            this.fileDialogManager.Draw();
        }

        /// <summary>
        /// Initiates opening a file dialog to load an AetherDraw plan.
        /// </summary>
        public void RequestLoadPlan()
        {
            LastFileDialogError = string.Empty; // Clear previous errors
            string initialPath = GetInitialDialogPath();
            // Configure and open the "Load Plan" file dialog.
            // The result will be handled by HandleLoadPlanDialogResult callback.
            fileDialogManager.OpenFileDialog(
                "Load AetherDraw Plan",      // Dialog title
                "AetherDraw Plan{.adp}",     // File type filter
                HandleLoadPlanDialogResult,  // Callback for when a file is selected or dialog is closed
                1,                           // Number of files to select (1 for loading a single plan)
                initialPath,                 // Starting directory for the dialog
                true                         // Force overwrite of existing path if dialog is re-opened
            );
        }

        /// <summary>
        /// Initiates opening a file dialog to save the current AetherDraw plan.
        /// </summary>
        public void RequestSavePlan()
        {
            LastFileDialogError = string.Empty;
            string initialPath = GetInitialDialogPath();
            // Configure and open the "Save Plan As..." file dialog.
            fileDialogManager.SaveFileDialog(
                "Save AetherDraw Plan As...", // Dialog title
                "AetherDraw Plan{.adp}",      // File type filter
                "MyAetherDrawPlan",          // Default file name
                ".adp",                      // Default extension
                HandleSavePlanDialogResult,  // Callback for when a path is chosen or dialog is closed
                initialPath,                 // Starting directory
                true                         // Force overwrite
            );
        }

        /// <summary>
        /// Initiates opening a file dialog to save the current AetherDraw view (all pages) as images.
        /// </summary>
        /// <param name="currentCanvasVisualSize">The current dimensions of the canvas, used to determine image export size.</param>
        public void RequestSaveImage(Vector2 currentCanvasVisualSize)
        {
            LastFileDialogError = string.Empty;
            string initialPath = GetInitialDialogPath();
            // Configure and open the "Save Image As..." file dialog.
            // A lambda is used for the callback to capture currentCanvasVisualSize.
            fileDialogManager.SaveFileDialog(
                "Save Image As...",          // Dialog title
                "PNG Image{.png,.PNG}",      // File type filter (suggesting PNG)
                "MyAetherDrawImage",         // Default base file name
                ".png",                      // Default extension
                (success, path) => HandleSaveImageDialogResult(success, path, currentCanvasVisualSize), // Callback
                initialPath,                 // Starting directory
                true                         // Force overwrite
            );
        }

        /// <summary>
        /// Determines the initial path for file dialogs.
        /// Prefers the plugin's configuration directory, falling back to "My Documents".
        /// </summary>
        /// <returns>A string representing the initial path for file dialogs.</returns>
        private string GetInitialDialogPath()
        {
            string path = pluginInterface.GetPluginConfigDirectory();
            // Fallback if config directory is not available or doesn't exist
            if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path))
            {
                path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            return path;
        }

        /// <summary>
        /// Callback method invoked by the FileDialogManager after the "Load Plan" dialog is closed.
        /// </summary>
        /// <param name="success">True if a file was selected; false if the dialog was cancelled or closed.</param>
        /// <param name="paths">A list containing the selected file path if successful.</param>
        private void HandleLoadPlanDialogResult(bool success, List<string> paths)
        {
            AetherDraw.Plugin.Log?.Debug($"[PlanIOManager] HandleLoadPlanDialogResult: Success - {success}, Paths count - {(paths?.Count ?? 0)}");
            if (success && paths != null && paths.Count > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                string filePathFromDialog = paths[0];
                AetherDraw.Plugin.Log?.Info($"[PlanIOManager] LoadFileDialog selected path: {filePathFromDialog}");
                ActuallyLoadPlanFromFile(filePathFromDialog); // Proceed to load the plan
            }
            else if (!success)
            {
                AetherDraw.Plugin.Log?.Info("[PlanIOManager] LoadFileDialog was cancelled or resulted in an error.");
                LastFileDialogError = "Load operation cancelled or failed.";
            }
        }

        /// <summary>
        /// Callback method invoked by the FileDialogManager after the "Save Plan" dialog is closed.
        /// </summary>
        /// <param name="success">True if a file path was confirmed; false if the dialog was cancelled.</param>
        /// <param name="filePath">The confirmed file path for saving if successful.</param>
        private void HandleSavePlanDialogResult(bool success, string filePath)
        {
            AetherDraw.Plugin.Log?.Debug($"[PlanIOManager] HandleSavePlanDialogResult: Success - {success}, Path - '{filePath ?? "null"}'");
            if (success && !string.IsNullOrEmpty(filePath))
            {
                ActuallySavePlanToFile(filePath); // Proceed to save the plan
            }
            else if (!success)
            {
                AetherDraw.Plugin.Log?.Info("[PlanIOManager] SaveFileDialog was cancelled or resulted in an error.");
                LastFileDialogError = "Save plan operation cancelled or failed.";
            }
        }

        /// <summary>
        /// Callback method invoked by the FileDialogManager after the "Save Image" dialog is closed.
        /// </summary>
        /// <param name="success">True if a file path was confirmed; false if the dialog was cancelled.</param>
        /// <param name="baseFilePathFromDialog">The confirmed base file path for saving images.</param>
        /// <param name="canvasVisualSize">The canvas dimensions to use for the exported images.</param>
        private void HandleSaveImageDialogResult(bool success, string baseFilePathFromDialog, Vector2 canvasVisualSize)
        {
            AetherDraw.Plugin.Log?.Debug($"[PlanIOManager] HandleSaveImageDialogResult: Success - {success}, Base Path - '{baseFilePathFromDialog ?? "null"}', CanvasSize: {canvasVisualSize}");
            if (success && !string.IsNullOrEmpty(baseFilePathFromDialog))
            {
                string directory = System.IO.Path.GetDirectoryName(baseFilePathFromDialog) ?? "";
                string baseNameOnly = System.IO.Path.GetFileNameWithoutExtension(baseFilePathFromDialog);
                string targetExtension = ".png"; // Ensure PNG format

                var currentPages = pageManager.GetAllPages();
                if (currentPages.Count == 0)
                {
                    LastFileDialogError = "No pages to save.";
                    AetherDraw.Plugin.Log?.Warning("[PlanIOManager] No pages found to save as images.");
                    return;
                }

                int successCount = 0;
                int failureCount = 0;
                List<string> savedFiles = new List<string>();

                // Iterate through each page and save it as an image
                for (int i = 0; i < currentPages.Count; i++)
                {
                    PageData currentPageToSave = currentPages[i];
                    string pagePrefix = $"{i + 1}"; // 1-based numbering for file names
                    string pageSpecificFileName = $"{pagePrefix}-{baseNameOnly}{targetExtension}";
                    string fullPagePath = System.IO.Path.Combine(directory, pageSpecificFileName);

                    AetherDraw.Plugin.Log?.Info($"[PlanIOManager] Preparing to save page '{currentPageToSave.Name}' (index {i}) as '{fullPagePath}'.");
                    try
                    {
                        ActuallySaveSinglePageAsImage(currentPageToSave, fullPagePath, canvasVisualSize);
                        successCount++;
                        savedFiles.Add(System.IO.Path.GetFileName(fullPagePath));
                    }
                    catch (Exception ex)
                    {
                        AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Failed to save page '{currentPageToSave.Name}' to '{fullPagePath}'.");
                        failureCount++;
                    }
                }

                // Provide feedback to the user
                if (failureCount > 0) LastFileDialogError = $"Saved {successCount} page(s). Failed to save {failureCount} page(s). Check log.";
                else if (successCount > 0) LastFileDialogError = $"Successfully saved: {string.Join(", ", savedFiles)}";
                else LastFileDialogError = "No pages were processed or saved."; // Should not happen if currentPages.Count > 0
            }
            else if (!success)
            {
                AetherDraw.Plugin.Log?.Info("[PlanIOManager] Save Image dialog was cancelled or resulted in an error.");
                LastFileDialogError = "Save image operation cancelled.";
            }
        }

        /// <summary>
        /// Loads a plan from the specified file path using the PlanSerializer.
        /// Updates the PageManager with the loaded pages and invokes OnPlanLoadSuccess event.
        /// </summary>
        /// <param name="filePath">The full path to the plan file (.adp).</param>
        private void ActuallyLoadPlanFromFile(string filePath)
        {
            AetherDraw.Plugin.Log?.Info($"[PlanIOManager] Attempting to load plan from: {filePath}");
            LastFileDialogError = string.Empty;

            if (!System.IO.File.Exists(filePath))
            {
                AetherDraw.Plugin.Log?.Warning($"[PlanIOManager] Plan file not found: {filePath}");
                LastFileDialogError = "Plan file not found.";
                return;
            }

            try
            {
                byte[] fileData = System.IO.File.ReadAllBytes(filePath);
                PlanSerializer.DeserializedPlan? loadedPlan = PlanSerializer.DeserializePlanFromBytes(fileData);

                if (loadedPlan == null || loadedPlan.Pages == null)
                {
                    AetherDraw.Plugin.Log?.Error($"[PlanIOManager] Failed to deserialize plan from {filePath}. Plan data is invalid or corrupt.");
                    LastFileDialogError = "Failed to read plan file. It might be corrupt, an incompatible version, or empty.";
                    return;
                }

                // Update PageManager with the newly loaded pages
                pageManager.LoadPages(loadedPlan.Pages);

                AetherDraw.Plugin.Log?.Info($"[PlanIOManager] Plan '{loadedPlan.PlanName}' loaded successfully. {pageManager.GetAllPages().Count} pages loaded.");
                LastFileDialogError = $"Plan '{loadedPlan.PlanName}' loaded.";

                // Notify subscribers (e.g., MainWindow) that the plan load was successful
                OnPlanLoadSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Error loading plan from {filePath}.");
                LastFileDialogError = $"Error loading plan: {ex.Message}";
            }
        }

        /// <summary>
        /// Saves the current set of pages to the specified file path using PlanSerializer.
        /// </summary>
        /// <param name="filePath">The full path where the plan file (.adp) will be saved.</param>
        private void ActuallySavePlanToFile(string filePath)
        {
            var currentPages = pageManager.GetAllPages();
            AetherDraw.Plugin.Log?.Info($"[PlanIOManager] Attempting to save current plan ({currentPages.Count} pages) to: {filePath}");
            LastFileDialogError = string.Empty;

            // Check if there's anything to save
            if (!currentPages.Any() || !currentPages.Any(p => p.Drawables.Any()))
            {
                AetherDraw.Plugin.Log?.Warning("[PlanIOManager] No content to save. Plan is empty.");
                LastFileDialogError = "Nothing to save in the current plan.";
                return;
            }

            string planName = System.IO.Path.GetFileNameWithoutExtension(filePath);

            try
            {
                // Ensure the directory exists before writing the file
                string? directory = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                    AetherDraw.Plugin.Log?.Info($"[PlanIOManager] Created directory for saving: {directory}");
                }

                // Define application version for serialization (could be dynamic in a real app)
                ushort appVersionMajor = 1;
                ushort appVersionMinor = 1;
                ushort appVersionPatch = 0;
                byte[]? serializedPlanData = PlanSerializer.SerializePlanToBytes(currentPages, planName, appVersionMajor, appVersionMinor, appVersionPatch);

                if (serializedPlanData != null)
                {
                    System.IO.File.WriteAllBytes(filePath, serializedPlanData);
                    AetherDraw.Plugin.Log?.Info($"[PlanIOManager] Plan '{planName}' saved successfully to {filePath}. Size: {serializedPlanData.Length} bytes.");
                    LastFileDialogError = $"Plan '{planName}' saved.";
                }
                else
                {
                    AetherDraw.Plugin.Log?.Error($"[PlanIOManager] Plan serialization returned null for '{planName}'. Save failed.");
                    LastFileDialogError = "Failed to serialize plan data.";
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Error saving plan to {filePath}.");
                LastFileDialogError = $"Error saving plan: {ex.Message}";
            }
        }

        /// <summary>
        /// Renders and saves a single page as a PNG image file.
        /// </summary>
        /// <param name="pageToSave">The <see cref="PageData"/> object to save.</param>
        /// <param name="targetFilePath">The full path where the image will be saved.</param>
        /// <param name="canvasVisualSize">The current visual dimensions of the canvas, used for image sizing.</param>
        private void ActuallySaveSinglePageAsImage(PageData pageToSave, string targetFilePath, Vector2 canvasVisualSize)
        {
            AetherDraw.Plugin.Log?.Info($"[PlanIOManager] Saving page '{pageToSave.Name}' to image: {targetFilePath} with canvas size {canvasVisualSize}");

            string finalFilePath = targetFilePath;
            // Ensure the .png extension
            if (!string.IsNullOrEmpty(finalFilePath) && !finalFilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                finalFilePath = System.IO.Path.ChangeExtension(finalFilePath, ".png");
            }

            try
            {
                // Determine image dimensions, ensuring they are positive
                int imageWidth = (int)Math.Max(100, canvasVisualSize.X); // Minimum width of 100px
                int imageHeight = (int)Math.Max(100, canvasVisualSize.Y); // Minimum height of 100px

                if (imageWidth <= 0 || imageHeight <= 0)
                {
                    AetherDraw.Plugin.Log?.Error($"[PlanIOManager] Invalid canvas dimensions for image export: {imageWidth}x{imageHeight} for page '{pageToSave.Name}'.");
                    throw new ArgumentOutOfRangeException(nameof(canvasVisualSize), "Canvas dimensions for image export must be positive.");
                }

                using (var image = new Image<Rgba32>(imageWidth, imageHeight)) // Create image buffer
                {
                    // Fill background (same color as canvas)
                    var backgroundColor = new Rgba32((byte)(0.15f * 255), (byte)(0.15f * 255), (byte)(0.17f * 255), (byte)(1.0f * 255));
                    image.Mutate(ctx => ctx.Fill(backgroundColor));

                    // Draw grid lines (using delegate to get current scaled grid size)
                    var gridColor = SixLabors.ImageSharp.Color.FromRgba((byte)(0.3f * 255), (byte)(0.3f * 255), (byte)(0.3f * 255), (byte)(1.0f * 255));
                    float gridLineThickness = 1f;
                    float scaledGridCellSize = getScaledCanvasGridSizeFunc();

                    if (scaledGridCellSize > 0)
                    {
                        image.Mutate(ctx => {
                            // Vertical lines
                            for (float x = scaledGridCellSize; x < imageWidth; x += scaledGridCellSize)
                            {
                                var pathBuilder = new PathBuilder(); // From SixLabors.ImageSharp.Drawing
                                pathBuilder.AddLine(new PointF(x, 0), new PointF(x, imageHeight));
                                ctx.Draw(gridColor, gridLineThickness, pathBuilder.Build());
                            }
                            // Horizontal lines
                            for (float y = scaledGridCellSize; y < imageHeight; y += scaledGridCellSize)
                            {
                                var pathBuilder = new PathBuilder();
                                pathBuilder.AddLine(new PointF(0, y), new PointF(imageWidth, y));
                                ctx.Draw(gridColor, gridLineThickness, pathBuilder.Build());
                            }
                        });
                    }

                    // Prepare drawables, ordered by layer priority (using delegate)
                    var drawablesToRender = pageToSave.Drawables.OrderBy(d => getLayerPriorityFunc(d.ObjectDrawMode)).ToList();
                    Vector2 imageOrigin = Vector2.Zero; // ImageSharp drawing origin is top-left
                    float scale = ImGuiHelpers.GlobalScale; // Use current ImGui global scale for consistency

                    // Render each drawable object to the image context
                    image.Mutate(ctx =>
                    {
                        foreach (var drawable in drawablesToRender)
                        {
                            // Check if the drawable is currently being edited via InPlaceTextEditor
                            // and if the page being saved is the currently active page.
                            bool isCurrentlyEditedDrawable = false;
                            if (pageManager.GetAllPages().IndexOf(pageToSave) == getCurrentPageIndexFunc()) // Use delegate for current index
                            {
                                isCurrentlyEditedDrawable = inPlaceTextEditor.IsEditing &&
                                                            inPlaceTextEditor.IsCurrentlyEditing(drawable);
                            }

                            // Skip rendering this drawable if it's actively being edited (to avoid partial text etc.)
                            if (isCurrentlyEditedDrawable) continue;

                            drawable.DrawToImage(ctx, imageOrigin, scale);
                        }
                    });

                    // Save the composed image to the file system as PNG
                    image.SaveAsPng(finalFilePath);
                    AetherDraw.Plugin.Log?.Info($"[PlanIOManager] Page '{pageToSave.Name}' saved as image successfully to {finalFilePath}.");
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanIOManager] Error during image saving for page '{pageToSave.Name}' to {targetFilePath}.");
                throw; // Re-throw to allow error handling by caller (e.g., to update LastFileDialogError)
            }
        }
    }
}
