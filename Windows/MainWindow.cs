// AetherDraw/Windows/MainWindow.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Linq;
using AetherDraw.DrawingLogic;
using AetherDraw.Core;
using AetherDraw.Serialization;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.IO;
using Dalamud.Interface.ImGuiFileDialog;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace AetherDraw.Windows
{
    public class MainWindow : Window, IDisposable
    {
        public class PageData
        {
            public string Name { get; set; } = "1";
            public List<BaseDrawable> Drawables { get; set; } = new List<BaseDrawable>();
        }

        private readonly Plugin plugin;
        private readonly Configuration configuration;

        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly InPlaceTextEditor inPlaceTextEditor;
        private readonly CanvasController canvasController;

        private List<PageData> pages = new List<PageData>();
        private int currentPageIndex = 0;

        private BaseDrawable? hoveredDrawable = null;
        private List<BaseDrawable> selectedDrawables = new List<BaseDrawable>();
        private List<BaseDrawable> clipboard = new List<BaseDrawable>();

        private DrawMode currentDrawMode = DrawMode.Pen;
        private Vector4 currentBrushColor;
        private float currentBrushThickness;
        private bool currentShapeFilled = false;

        private static readonly List<BaseDrawable> EmptyDrawablesFallback = new List<BaseDrawable>();
        private List<BaseDrawable> DrawablesOfCurrentPageUi =>
            (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            ? pages[currentPageIndex].Drawables
            : EmptyDrawablesFallback;

        private float ScaledCanvasGridSize => 40f * ImGuiHelpers.GlobalScale;

        private static readonly float[] ThicknessPresets = new float[] { 1.5f, 4f, 7f, 10f };
        private static readonly Vector4[] ColorPalette = new Vector4[] {
            new Vector4(1.0f,1.0f,1.0f,1.0f), new Vector4(0.0f,0.0f,0.0f,1.0f),
            new Vector4(1.0f,0.0f,0.0f,1.0f), new Vector4(0.0f,1.0f,0.0f,1.0f),
            new Vector4(0.0f,0.0f,1.0f,1.0f), new Vector4(1.0f,1.0f,0.0f,1.0f),
            new Vector4(1.0f,0.0f,1.0f,1.0f), new Vector4(0.0f,1.0f,1.0f,1.0f),
            new Vector4(0.5f,0.5f,0.5f,1.0f), new Vector4(0.8f,0.4f,0.0f,1.0f)
        };

        private readonly FileDialogManager fileDialogManager;
        private string lastFileDialogError = string.Empty;
        private Vector2 currentCanvasDrawSize;


        public MainWindow(Plugin plugin) : base("AetherDraw Whiteboard###AetherDrawMainWindow")
        {
            this.plugin = plugin;
            this.configuration = plugin.Configuration;
            AetherDraw.Plugin.Log?.Debug("[MainWindow] Initializing...");

            this.shapeInteractionHandler = new ShapeInteractionHandler();
            this.inPlaceTextEditor = new InPlaceTextEditor();
            this.fileDialogManager = new FileDialogManager();

            this.canvasController = new CanvasController(
                () => currentDrawMode,
                (newMode) => currentDrawMode = newMode,
                () => currentBrushColor,
                () => currentBrushThickness,
                () => currentShapeFilled,
                () => DrawablesOfCurrentPageUi,
                selectedDrawables,
                () => hoveredDrawable,
                (newHovered) => hoveredDrawable = newHovered,
                this.shapeInteractionHandler,
                this.inPlaceTextEditor,
                this.configuration
            );

            float targetMinimumWidth = 850f * 0.75f * ImGuiHelpers.GlobalScale;
            float targetMinimumHeight = 600f * ImGuiHelpers.GlobalScale;
            this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(targetMinimumWidth, targetMinimumHeight), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
            this.RespectCloseHotkey = true;

            this.currentBrushColor = new Vector4(this.configuration.DefaultBrushColorR, this.configuration.DefaultBrushColorG, this.configuration.DefaultBrushColorB, this.configuration.DefaultBrushColorA);
            this.currentBrushThickness = ThicknessPresets.Contains(this.configuration.DefaultBrushThickness) ? this.configuration.DefaultBrushThickness : ThicknessPresets[1];

            if (pages.Count == 0) { AddNewPage(switchToPage: false); }
            currentPageIndex = 0;
            AetherDraw.Plugin.Log?.Debug("[MainWindow] Initialization complete.");
        }

        public void Dispose()
        {
            AetherDraw.Plugin.Log?.Debug("[MainWindow] Dispose called.");
        }

        public override void PreDraw()
        {
            Flags = configuration.IsMainWindowMovable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoMove;
        }

        public override void Draw()
        {
            float scaledToolbarWidth = 125f * ImGuiHelpers.GlobalScale;
            using (var toolbarRaii = ImRaii.Child("ToolbarRegion", new Vector2(scaledToolbarWidth, 0), true, ImGuiWindowFlags.None))
            {
                if (toolbarRaii) DrawToolbarControls();
            }

            ImGui.SameLine();

            using (var rightPaneRaii = ImRaii.Child("RightPane", Vector2.Zero, false, ImGuiWindowFlags.None))
            {
                if (rightPaneRaii)
                {
                    float bottomControlsHeight = ImGui.GetFrameHeightWithSpacing() * 2 +
                                                 ImGui.GetStyle().WindowPadding.Y * 2 +
                                                 ImGui.GetStyle().ItemSpacing.Y;
                    float canvasAvailableHeight = ImGui.GetContentRegionAvail().Y - bottomControlsHeight - ImGui.GetStyle().ItemSpacing.Y;
                    canvasAvailableHeight = Math.Max(canvasAvailableHeight, 50f * ImGuiHelpers.GlobalScale);

                    if (ImGui.BeginChild("CanvasDrawingArea", new Vector2(0, canvasAvailableHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                    {
                        currentCanvasDrawSize = ImGui.GetContentRegionAvail();
                        DrawCanvas();
                        ImGui.EndChild();
                    }
                    DrawBottomControlsBar(bottomControlsHeight);
                }
            }
            this.fileDialogManager.Draw();
        }

        private void DrawToolbarControls()
        {
            Vector4 activeToolColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float itemSpacingX = ImGui.GetStyle().ItemSpacing.X;
            float btnWidthHalf = Math.Max((availableWidth - itemSpacingX) / 2f, 30f * ImGuiHelpers.GlobalScale);
            float btnWidthFull = availableWidth;

            void ToolButton(string label, DrawMode mode, float buttonWidth)
            {
                bool isSelected = currentDrawMode == mode;
                using (isSelected ? ImRaii.PushColor(ImGuiCol.Button, activeToolColor) : null)
                {
                    if (ImGui.Button($"{label}##ToolBtn_{mode}", new Vector2(buttonWidth, 0)))
                    {
                        currentDrawMode = mode;
                        if (mode != DrawMode.Select && mode != DrawMode.TextTool) shapeInteractionHandler.ResetDragState();
                        if (inPlaceTextEditor.IsEditing && mode != DrawMode.TextTool && mode != DrawMode.Select) { inPlaceTextEditor.CommitAndEndEdit(); }
                    }
                }
            }

            void PlacedImageToolButton(string label, DrawMode mode, string imageResourcePath, float buttonWidth)
            {
                bool isSelected = currentDrawMode == mode;
                using (isSelected ? ImRaii.PushColor(ImGuiCol.Button, activeToolColor) : null)
                using (ImRaii.PushId($"ImgBtn_{mode}_{label.Replace(" ", "_")}"))
                {
                    var tex = TextureManager.GetTexture(imageResourcePath);
                    float textLineHeight = ImGui.GetTextLineHeight();
                    float imageDisplaySize = Math.Min(buttonWidth * 0.7f, textLineHeight * 1.2f);
                    imageDisplaySize = Math.Max(imageDisplaySize, textLineHeight * 0.8f);
                    imageDisplaySize = Math.Max(imageDisplaySize, 8f * ImGuiHelpers.GlobalScale);
                    Vector2 actualImageButtonSize = new Vector2(imageDisplaySize, imageDisplaySize);

                    if (ImGui.Button(label.Length > 3 && tex == null ? label.Substring(0, 3) : $"##{label}_container_{mode}", new Vector2(buttonWidth, 0)))
                    {
                        currentDrawMode = mode;
                        if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CommitAndEndEdit();
                        shapeInteractionHandler.ResetDragState();
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(label);

                    if (tex != null && tex.ImGuiHandle != IntPtr.Zero)
                    {
                        Vector2 itemMin = ImGui.GetItemRectMin();
                        Vector2 itemMax = ImGui.GetItemRectMax();
                        Vector2 itemCenter = (itemMin + itemMax) / 2f;
                        ImGui.GetWindowDrawList().AddImage(tex.ImGuiHandle, itemCenter - actualImageButtonSize / 2f, itemCenter + actualImageButtonSize / 2f);
                    }
                    else if (tex == null)
                    {
                        Vector2 itemMin = ImGui.GetItemRectMin();
                        var frameHeight = ImGui.GetFrameHeight();
                        var textSize = ImGui.CalcTextSize(label.Substring(0, Math.Min(label.Length, 3)));
                        ImGui.GetWindowDrawList().AddText(itemMin + (new Vector2(buttonWidth, frameHeight) - textSize) / 2f, ImGui.GetColorU32(ImGuiCol.Text), label.Substring(0, Math.Min(label.Length, 3)));
                    }
                }
            }

            ToolButton("Select", DrawMode.Select, btnWidthHalf); ImGui.SameLine();
            ToolButton("Eraser", DrawMode.Eraser, btnWidthHalf);
            if (ImGui.Button("Copy", new Vector2(btnWidthHalf, 0))) CopySelected(); ImGui.SameLine();
            if (ImGui.Button("Paste", new Vector2(btnWidthHalf, 0))) PasteCopied();
            ImGui.Checkbox("Fill Shape", ref currentShapeFilled);
            if (ImGui.Button("Clear All", new Vector2(btnWidthFull, 0)))
            {
                if (DrawablesOfCurrentPageUi.Any()) DrawablesOfCurrentPageUi.Clear();
                selectedDrawables.Clear();
                hoveredDrawable = null;
                shapeInteractionHandler.ResetDragState();
                if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();
                if (pages.Count == 1 && currentPageIndex == 0) pages[currentPageIndex].Name = "1";
            }
            ImGui.Separator();
            ToolButton("Pen", DrawMode.Pen, btnWidthHalf); ImGui.SameLine();
            ToolButton("Line", DrawMode.StraightLine, btnWidthHalf);
            ToolButton("Dash", DrawMode.Dash, btnWidthHalf); ImGui.SameLine();
            ToolButton("Rect", DrawMode.Rectangle, btnWidthHalf);
            ToolButton("Circle", DrawMode.Circle, btnWidthHalf); ImGui.SameLine();
            ToolButton("Arrow", DrawMode.Arrow, btnWidthHalf);
            ToolButton("Cone", DrawMode.Cone, btnWidthHalf); ImGui.SameLine();
            ToolButton("A", DrawMode.TextTool, btnWidthHalf);
            ImGui.Separator();
            PlacedImageToolButton("Tank", DrawMode.RoleTankImage, "PluginImages.toolbar.Tank.JPG", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("Healer", DrawMode.RoleHealerImage, "PluginImages.toolbar.Healer.JPG", btnWidthHalf);
            PlacedImageToolButton("Melee", DrawMode.RoleMeleeImage, "PluginImages.toolbar.Melee.JPG", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("Ranged", DrawMode.RoleRangedImage, "PluginImages.toolbar.Ranged.JPG", btnWidthHalf);
            ImGui.Separator();
            PlacedImageToolButton("WM1", DrawMode.Waymark1Image, "PluginImages.toolbar.1_waymark.JPG", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("WM2", DrawMode.Waymark2Image, "PluginImages.toolbar.2_waymark.JPG", btnWidthHalf);
            PlacedImageToolButton("WM3", DrawMode.Waymark3Image, "PluginImages.toolbar.3_waymark.JPG", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("WM4", DrawMode.Waymark4Image, "PluginImages.toolbar.4_waymark.JPG", btnWidthHalf);
            PlacedImageToolButton("WMA", DrawMode.WaymarkAImage, "PluginImages.toolbar.A.JPG", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("WMB", DrawMode.WaymarkBImage, "PluginImages.toolbar.B.JPG", btnWidthHalf);
            PlacedImageToolButton("WMC", DrawMode.WaymarkCImage, "PluginImages.toolbar.C.JPG", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("WMD", DrawMode.WaymarkDImage, "PluginImages.toolbar.D.JPG", btnWidthHalf);
            ImGui.Separator();
            PlacedImageToolButton("Boss", DrawMode.BossImage, "PluginImages.svg.boss.svg", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("Circle", DrawMode.CircleAoEImage, "PluginImages.svg.prox_aoe.svg", btnWidthHalf);
            PlacedImageToolButton("Donut", DrawMode.DonutAoEImage, "PluginImages.svg.donut.svg", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("Flare", DrawMode.FlareImage, "PluginImages.svg.flare.svg", btnWidthHalf);
            PlacedImageToolButton("L.Stack", DrawMode.LineStackImage, "PluginImages.svg.line_stack.svg", btnWidthHalf); ImGui.SameLine();
            PlacedImageToolButton("Spread", DrawMode.SpreadImage, "PluginImages.svg.spread.svg", btnWidthHalf);
            PlacedImageToolButton("Stack", DrawMode.StackImage, "PluginImages.svg.stack.svg", btnWidthHalf);
            ImGui.Separator();

            ImGui.Text("Thickness:");
            // Correctly define thicknessButtonWidth here
            float thicknessButtonWidth = (availableWidth - itemSpacingX * (ThicknessPresets.Length - 1)) / ThicknessPresets.Length;
            thicknessButtonWidth = Math.Max(thicknessButtonWidth, 20f * ImGuiHelpers.GlobalScale);
            for (int i = 0; i < ThicknessPresets.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                float t = ThicknessPresets[i];
                bool isSelectedThickness = Math.Abs(currentBrushThickness - t) < 0.01f;
                using (isSelectedThickness ? ImRaii.PushColor(ImGuiCol.Button, activeToolColor) : null)
                {
                    // Use thicknessButtonWidth
                    if (ImGui.Button($"{t:0}##ThicknessBtn{i}", new Vector2(thicknessButtonWidth, 0))) currentBrushThickness = t;
                }
            }
            ImGui.Separator();

            int colorsPerRow = 5;
            float smallColorButtonBaseSize = (availableWidth - itemSpacingX * (colorsPerRow - 1)) / colorsPerRow;
            float smallColorButtonActualSize = Math.Max(smallColorButtonBaseSize, ImGui.GetTextLineHeight() * 0.8f);
            smallColorButtonActualSize = Math.Max(smallColorButtonActualSize, 16f * ImGuiHelpers.GlobalScale);
            Vector2 colorButtonDimensions = new Vector2(smallColorButtonActualSize, smallColorButtonActualSize);

            for (int i = 0; i < ColorPalette.Length; i++)
            {
                bool isSelectedColor = (ColorPalette[i].X == currentBrushColor.X && ColorPalette[i].Y == currentBrushColor.Y && ColorPalette[i].Z == currentBrushColor.Z && ColorPalette[i].W == currentBrushColor.W);
                if (ImGui.ColorButton($"##ColorPaletteButton{i}", ColorPalette[i], ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, colorButtonDimensions))
                {
                    currentBrushColor = ColorPalette[i];
                }
                if (isSelectedColor)
                {
                    ImGui.GetForegroundDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.1f, 1.0f)), 1f * ImGuiHelpers.GlobalScale, ImDrawFlags.None, 2.0f * ImGuiHelpers.GlobalScale);
                }
                if ((i + 1) % colorsPerRow != 0 && i < ColorPalette.Length - 1)
                {
                    ImGui.SameLine(0, itemSpacingX / 2f);
                }
            }
        }

        private void DrawBottomControlsBar(float barHeight)
        {
            using (var bottomBarChild = ImRaii.Child("BottomControlsRegion", new Vector2(0, barHeight), true, ImGuiWindowFlags.None))
            {
                if (!bottomBarChild) return;

                float pageButtonRowHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
                using (var pageTabsChild = ImRaii.Child("PageTabsSubRegion", new Vector2(0, pageButtonRowHeight), false, ImGuiWindowFlags.HorizontalScrollbar))
                {
                    if (pageTabsChild)
                    {
                        float tabButtonHeight = ImGui.GetFrameHeight();
                        Vector4 activeTabColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];

                        for (int i = 0; i < pages.Count; i++)
                        {
                            bool isSelectedPage = (i == currentPageIndex);
                            string pageName = pages[i].Name;
                            float pageTabWidth = ImGui.CalcTextSize(pageName).X + ImGui.GetStyle().FramePadding.X * 2.0f + (10f * ImGuiHelpers.GlobalScale);
                            pageTabWidth = Math.Max(pageTabWidth, tabButtonHeight * 1.5f);

                            using (isSelectedPage ? ImRaii.PushColor(ImGuiCol.Button, activeTabColor) : null)
                            {
                                if (ImGui.Button(pageName, new Vector2(pageTabWidth, tabButtonHeight)))
                                {
                                    if (!isSelectedPage) SwitchToPage(i);
                                }
                            }
                            ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
                        }

                        if (ImGui.Button("+##AddPage", new Vector2(tabButtonHeight, tabButtonHeight))) AddNewPage();
                        if (pages.Count > 1)
                        {
                            ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X);
                            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f)))
                            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f)))
                            using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)))
                            {
                                if (ImGui.Button("X##DeletePage", new Vector2(tabButtonHeight, tabButtonHeight))) DeleteCurrentPage();
                            }
                        }
                    }
                }

                float availableWidth = ImGui.GetContentRegionAvail().X;
                float actionButtonWidth = (availableWidth - ImGui.GetStyle().ItemSpacing.X * 4) / 5f;
                actionButtonWidth = Math.Max(actionButtonWidth, 80f * ImGuiHelpers.GlobalScale);

                string initialPath = AetherDraw.Plugin.PluginInterface.GetPluginConfigDirectory();
                if (string.IsNullOrEmpty(initialPath) || !System.IO.Directory.Exists(initialPath))
                {
                    initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }

                if (ImGui.Button("Load Plan##LoadPlanButton", new Vector2(actionButtonWidth, 0)))
                {
                    lastFileDialogError = string.Empty;
                    fileDialogManager.OpenFileDialog("Load AetherDraw Plan", "AetherDraw Plan{.adp}", HandleLoadPlanDialogResult, 1, initialPath, true);
                }
                ImGui.SameLine();
                if (ImGui.Button("Save Plan##SavePlanButton", new Vector2(actionButtonWidth, 0)))
                {
                    lastFileDialogError = string.Empty;
                    fileDialogManager.SaveFileDialog("Save AetherDraw Plan As...", "AetherDraw Plan{.adp}", "MyAetherDrawPlan", ".adp", HandleSavePlanDialogResult, initialPath, true);
                }
                ImGui.SameLine();
                if (ImGui.Button("Save as Image##SaveAsImageButton", new Vector2(actionButtonWidth, 0)))
                {
                    lastFileDialogError = string.Empty;
                    fileDialogManager.SaveFileDialog("Save Image As...", "PNG Image{.png,.PNG}", "MyAetherDrawImage", ".png", HandleSaveImageDialogResult, initialPath, true);
                }
                ImGui.SameLine();
                if (ImGui.Button("Open WDIG##OpenWDIGButton", new Vector2(actionButtonWidth, 0)))
                {
                    try { Plugin.CommandManager.ProcessCommand("/wdig"); }
                    catch (Exception ex) { AetherDraw.Plugin.Log?.Error(ex, "Error processing /wdig command."); }
                }
                ImGui.SameLine();
                using (ImRaii.Disabled())
                {
                    if (ImGui.Button("Join/Create Live##LiveRoomButton", new Vector2(actionButtonWidth, 0))) { /* Log action */ }
                }
            }
        }

        private void HandleSaveImageDialogResult(bool success, string baseFilePathFromDialog)
        {
            AetherDraw.Plugin.Log?.Debug($"[MainWindow] HandleSaveImageDialogResult: Success - {success}, Base Path - '{baseFilePathFromDialog ?? "null"}'");
            if (success && !string.IsNullOrEmpty(baseFilePathFromDialog))
            {
                string directory = System.IO.Path.GetDirectoryName(baseFilePathFromDialog) ?? "";
                string baseNameOnly = System.IO.Path.GetFileNameWithoutExtension(baseFilePathFromDialog);
                string targetExtension = ".png";

                if (this.pages.Count == 0)
                {
                    lastFileDialogError = "No pages to save.";
                    AetherDraw.Plugin.Log?.Warning("[MainWindow] No pages found to save as images.");
                    return;
                }

                int successCount = 0;
                int failureCount = 0;
                List<string> savedFiles = new List<string>();

                for (int i = 0; i < this.pages.Count; i++)
                {
                    PageData currentPageToSave = this.pages[i];
                    string pagePrefix = $"{i + 1}";
                    string pageSpecificFileName = $"{pagePrefix}-{baseNameOnly}{targetExtension}";
                    string fullPagePath = System.IO.Path.Combine(directory, pageSpecificFileName);

                    AetherDraw.Plugin.Log?.Info($"[MainWindow] Preparing to save page '{currentPageToSave.Name}' (index {i}) as '{fullPagePath}'.");
                    try
                    {
                        ActuallySaveSinglePageAsImage(currentPageToSave, fullPagePath, this.currentCanvasDrawSize);
                        successCount++;
                        savedFiles.Add(System.IO.Path.GetFileName(fullPagePath));
                    }
                    catch (Exception ex)
                    {
                        AetherDraw.Plugin.Log?.Error(ex, $"[MainWindow] Failed to save page '{currentPageToSave.Name}' to '{fullPagePath}'.");
                        failureCount++;
                    }
                }

                if (failureCount > 0)
                {
                    lastFileDialogError = $"Saved {successCount} page(s). Failed to save {failureCount} page(s). Check log.";
                }
                else if (successCount > 0)
                {
                    lastFileDialogError = $"Successfully saved: {string.Join(", ", savedFiles)}";
                }
                else
                {
                    lastFileDialogError = "No pages were processed or saved.";
                }
            }
            else if (!success)
            {
                AetherDraw.Plugin.Log?.Info("[MainWindow] Save Image dialog was cancelled or resulted in an error.");
                lastFileDialogError = "Save image operation cancelled.";
            }
        }

        private void ActuallySaveSinglePageAsImage(PageData pageToSave, string targetFilePath, Vector2 canvasVisualSize)
        {
            AetherDraw.Plugin.Log?.Info($"[MainWindow] Saving page '{pageToSave.Name}' to image: {targetFilePath}");

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
                    AetherDraw.Plugin.Log?.Error($"[MainWindow] Invalid canvas dimensions for image export for page '{pageToSave.Name}': {imageWidth}x{imageHeight}");
                    throw new ArgumentOutOfRangeException(nameof(canvasVisualSize), "Canvas dimensions for image export are invalid.");
                }

                using (var image = new Image<Rgba32>(imageWidth, imageHeight))
                {
                    var backgroundColor = new Rgba32((byte)(0.15f * 255), (byte)(0.15f * 255), (byte)(0.17f * 255), (byte)(1.0f * 255));
                    image.Mutate(ctx => ctx.Fill(backgroundColor));

                    var gridColor = SixLabors.ImageSharp.Color.FromRgba((byte)(0.3f * 255), (byte)(0.3f * 255), (byte)(0.3f * 255), (byte)(1.0f * 255));
                    float gridLineThickness = 1f;
                    float scaledGridCellSize = ScaledCanvasGridSize;

                    if (scaledGridCellSize > 0)
                    {
                        image.Mutate(ctx => {
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

                    var drawablesToRender = pageToSave.Drawables.OrderBy(d => GetLayerPriority(d.ObjectDrawMode)).ToList();
                    Vector2 imageOrigin = Vector2.Zero;
                    float scale = ImGuiHelpers.GlobalScale;

                    image.Mutate(ctx =>
                    {
                        foreach (var drawable in drawablesToRender)
                        {
                            bool isCurrentlyEditedDrawable = (pageToSave == this.pages[currentPageIndex]) &&
                                                             inPlaceTextEditor.IsEditing &&
                                                             inPlaceTextEditor.IsCurrentlyEditing(drawable);
                            if (isCurrentlyEditedDrawable) continue;

                            drawable.DrawToImage(ctx, imageOrigin, scale);
                        }
                    });

                    image.SaveAsPng(finalFilePath);
                    AetherDraw.Plugin.Log?.Info($"[MainWindow] Page '{pageToSave.Name}' saved as image successfully to {finalFilePath}.");
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[MainWindow] Error saving page '{pageToSave.Name}' to image at {targetFilePath}.");
                throw;
            }
        }

        private void HandleSavePlanDialogResult(bool success, string filePath)
        {
            AetherDraw.Plugin.Log?.Debug($"[MainWindow] HandleSavePlanDialogResult: Success - {success}, Path - '{filePath ?? "null"}'");
            if (success && !string.IsNullOrEmpty(filePath))
            {
                ActuallySavePlanToFile(filePath);
            }
            else if (!success)
            {
                AetherDraw.Plugin.Log?.Info("[MainWindow] SaveFileDialog was cancelled or resulted in an error.");
            }
        }

        private void HandleLoadPlanDialogResult(bool success, List<string> paths)
        {
            AetherDraw.Plugin.Log?.Debug($"[MainWindow] HandleLoadPlanDialogResult: Success - {success}, Paths count - {(paths?.Count ?? 0)}");
            if (success && paths != null && paths.Count > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                string filePathFromDialog = paths[0];
                AetherDraw.Plugin.Log?.Info($"[MainWindow] LoadFileDialog selected path: {filePathFromDialog}");
                ActuallyLoadPlanFromFile(filePathFromDialog);
            }
            else if (!success)
            {
                AetherDraw.Plugin.Log?.Info("[MainWindow] LoadFileDialog was cancelled, resulted in an error, or no path selected.");
            }
        }

        private void ActuallySavePlanToFile(string filePath)
        {
            AetherDraw.Plugin.Log?.Info($"[MainWindow] Saving current plan ({pages.Count} pages) to: {filePath}");
            lastFileDialogError = string.Empty;
            if (!pages.Any() || !pages.Any(p => p.Drawables.Any()))
            {
                AetherDraw.Plugin.Log?.Warning("[MainWindow] No pages or no drawables on any page to save.");
                lastFileDialogError = "Nothing to save in the current plan.";
                return;
            }

            string planName = System.IO.Path.GetFileNameWithoutExtension(filePath);

            try
            {
                string? directory = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                    AetherDraw.Plugin.Log?.Info($"[MainWindow] Created directory: {directory}");
                }

                ushort appVersionMajor = 1; ushort appVersionMinor = 0; ushort appVersionPatch = 0;
                byte[]? serializedPlanData = PlanSerializer.SerializePlanToBytes(this.pages, planName, appVersionMajor, appVersionMinor, appVersionPatch);

                if (serializedPlanData != null)
                {
                    File.WriteAllBytes(filePath, serializedPlanData);
                    AetherDraw.Plugin.Log?.Info($"[MainWindow] Plan '{planName}' saved successfully to {filePath}. Size: {serializedPlanData.Length} bytes.");
                }
                else
                {
                    AetherDraw.Plugin.Log?.Error($"[MainWindow] Plan serialization returned null for '{planName}'.");
                    lastFileDialogError = "Failed to serialize plan data.";
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[MainWindow] Error saving plan to {filePath}.");
                lastFileDialogError = $"Error saving plan: {ex.Message}";
            }
        }

        private void ActuallyLoadPlanFromFile(string filePath)
        {
            AetherDraw.Plugin.Log?.Info($"[MainWindow] Loading plan from: {filePath}");
            lastFileDialogError = string.Empty;

            if (!File.Exists(filePath))
            {
                AetherDraw.Plugin.Log?.Warning($"[MainWindow] Plan file not found for loading: {filePath}");
                lastFileDialogError = "Plan file not found.";
                return;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                PlanSerializer.DeserializedPlan? loadedPlan = PlanSerializer.DeserializePlanFromBytes(fileData);

                if (loadedPlan == null || loadedPlan.Pages == null)
                {
                    AetherDraw.Plugin.Log?.Error($"[MainWindow] Failed to deserialize plan from {filePath}. Loaded plan or pages is null.");
                    lastFileDialogError = "Failed to read plan file. It might be corrupt, an incompatible version, or empty.";
                    return;
                }

                this.pages.Clear();
                foreach (var loadedPageData in loadedPlan.Pages)
                {
                    var newUiPage = new MainWindow.PageData { Name = loadedPageData.Name, Drawables = loadedPageData.Drawables ?? new List<BaseDrawable>() };
                    this.pages.Add(newUiPage);
                }

                currentPageIndex = 0;
                if (!this.pages.Any()) { AddNewPage(true); }

                selectedDrawables.Clear();
                hoveredDrawable = null;
                shapeInteractionHandler.ResetDragState();
                if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();

                AetherDraw.Plugin.Log?.Info($"[MainWindow] Plan '{loadedPlan.PlanName}' loaded successfully from {filePath}. {loadedPlan.Pages.Count} pages loaded.");
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[MainWindow] Error loading plan from {filePath}.");
                lastFileDialogError = $"Error loading plan: {ex.Message}";
            }
        }

        private void DrawCanvas()
        {
            Vector2 canvasSizeForImGuiDrawing = ImGui.GetContentRegionAvail();
            currentCanvasDrawSize = canvasSizeForImGuiDrawing; // Update for export, though also set in Draw()

            float minCanvasDimension = 50f * ImGuiHelpers.GlobalScale;
            if (canvasSizeForImGuiDrawing.X < minCanvasDimension) canvasSizeForImGuiDrawing.X = minCanvasDimension;
            if (canvasSizeForImGuiDrawing.Y < minCanvasDimension) canvasSizeForImGuiDrawing.Y = minCanvasDimension;

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            Vector2 canvasOriginScreen = ImGui.GetCursorScreenPos();

            uint backgroundColor = ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.17f, 1.0f));
            drawList.AddRectFilled(canvasOriginScreen, canvasOriginScreen + canvasSizeForImGuiDrawing, backgroundColor);
            uint gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
            float scaledGridCellSize = ScaledCanvasGridSize;
            float scaledGridLineThickness = Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale);
            if (scaledGridCellSize > 0)
            {
                for (float x = scaledGridCellSize; x < canvasSizeForImGuiDrawing.X; x += scaledGridCellSize)
                    drawList.AddLine(new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y), new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y + canvasSizeForImGuiDrawing.Y), gridColor, scaledGridLineThickness);
                for (float y = scaledGridCellSize; y < canvasSizeForImGuiDrawing.Y; y += scaledGridCellSize)
                    drawList.AddLine(new Vector2(canvasOriginScreen.X, canvasOriginScreen.Y + y), new Vector2(canvasOriginScreen.X + canvasSizeForImGuiDrawing.X, canvasOriginScreen.Y + y), gridColor, scaledGridLineThickness);
            }
            uint canvasOutlineColorU32 = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.45f, 1f));
            drawList.AddRect(canvasOriginScreen - Vector2.One, canvasOriginScreen + canvasSizeForImGuiDrawing + Vector2.One, canvasOutlineColorU32, 0f, ImDrawFlags.None, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));

            if (inPlaceTextEditor.IsEditing)
            {
                inPlaceTextEditor.RecalculateEditorBounds(canvasOriginScreen, ImGuiHelpers.GlobalScale);
                inPlaceTextEditor.DrawEditorUI();
            }

            ImGui.SetCursorScreenPos(canvasOriginScreen);
            ImGui.InvisibleButton("##AetherDrawCanvasInteractionLayer", canvasSizeForImGuiDrawing);

            Vector2 mousePosScreen = ImGui.GetMousePos();
            Vector2 mousePosLogical = (mousePosScreen - canvasOriginScreen) / ImGuiHelpers.GlobalScale;
            bool canvasInteractLayerHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.None);
            bool canInteractWithCanvas = !inPlaceTextEditor.IsEditing && canvasInteractLayerHovered;

            if (canInteractWithCanvas)
            {
                canvasController.ProcessCanvasInteraction(
                    mousePosLogical, mousePosScreen, canvasOriginScreen, drawList,
                    ImGui.IsMouseDown(ImGuiMouseButton.Left), ImGui.IsMouseClicked(ImGuiMouseButton.Left),
                    ImGui.IsMouseReleased(ImGuiMouseButton.Left), ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left),
                    GetLayerPriority
                );
            }

            ImGui.PushClipRect(canvasOriginScreen, canvasOriginScreen + canvasSizeForImGuiDrawing, true);
            var drawablesToRender = DrawablesOfCurrentPageUi;
            if (drawablesToRender != null && drawablesToRender.Any())
            {
                var sortedDrawables = drawablesToRender.OrderBy(d => GetLayerPriority(d.ObjectDrawMode)).ToList();
                foreach (var drawable in sortedDrawables)
                {
                    if (inPlaceTextEditor.IsEditing && inPlaceTextEditor.IsCurrentlyEditing(drawable)) continue;
                    drawable.Draw(drawList, canvasOriginScreen);
                }
            }
            canvasController.GetCurrentDrawingObjectForPreview()?.Draw(drawList, canvasOriginScreen);
            ImGui.PopClipRect();
        }

        private void AddNewPage(bool switchToPage = true)
        {
            AetherDraw.Plugin.Log?.Info("[MainWindow.PageManagement] Adding new page.");
            int newPageNumber = pages.Any() ? pages.Select(p => int.TryParse(p.Name, out int num) ? num : 0).DefaultIfEmpty(0).Max() + 1 : 1;
            var newPage = new PageData { Name = newPageNumber.ToString() };

            // Reverted to unconditional waymark preloading as per original logic.
            // If a configuration option like 'PreloadWaymarksOnNewPage' is added to Configuration.cs,
            // it can be used here: if (this.configuration.PreloadWaymarksOnNewPage) { ... }
            float logicalRefCanvasWidth = (850f * 0.75f) - 125f;
            float logicalRefCanvasHeight = 550f;
            Vector2 canvasCenter = new Vector2(logicalRefCanvasWidth / 2f, logicalRefCanvasHeight / 2f);
            float waymarkPlacementRadius = Math.Min(logicalRefCanvasWidth, logicalRefCanvasHeight) * 0.40f;
            Vector2 waymarkImageUnscaledSize = new Vector2(30f, 30f);
            Vector4 waymarkTint = Vector4.One;

            var waymarksToPreload = new[] {
                new { Mode = DrawMode.WaymarkAImage, Path = "PluginImages.toolbar.A.JPG", Angle = 3 * MathF.PI / 2 },
                new { Mode = DrawMode.WaymarkBImage, Path = "PluginImages.toolbar.B.JPG", Angle = 0f },
                new { Mode = DrawMode.WaymarkCImage, Path = "PluginImages.toolbar.C.JPG", Angle = MathF.PI / 2 },
                new { Mode = DrawMode.WaymarkDImage, Path = "PluginImages.toolbar.D.JPG", Angle = MathF.PI },
                new { Mode = DrawMode.Waymark1Image, Path = "PluginImages.toolbar.1_waymark.JPG", Angle = 5 * MathF.PI / 4 },
                new { Mode = DrawMode.Waymark2Image, Path = "PluginImages.toolbar.2_waymark.JPG", Angle = 7 * MathF.PI / 4 },
                new { Mode = DrawMode.Waymark3Image, Path = "PluginImages.toolbar.3_waymark.JPG", Angle = MathF.PI / 4 },
                new { Mode = DrawMode.Waymark4Image, Path = "PluginImages.toolbar.4_waymark.JPG", Angle = 3 * MathF.PI / 4 }
            };
            foreach (var wmInfo in waymarksToPreload)
            {
                float x = canvasCenter.X + waymarkPlacementRadius * MathF.Cos(wmInfo.Angle);
                float y = canvasCenter.Y + waymarkPlacementRadius * MathF.Sin(wmInfo.Angle);
                var drawableImage = new DrawableImage(wmInfo.Mode, wmInfo.Path, new Vector2(x, y), waymarkImageUnscaledSize, waymarkTint, 0f);
                drawableImage.IsPreview = false;
                newPage.Drawables.Add(drawableImage);
            }
            pages.Add(newPage);
            if (switchToPage) SwitchToPage(pages.Count - 1);
        }

        private void DeleteCurrentPage()
        {
            AetherDraw.Plugin.Log?.Info("[MainWindow.PageManagement] Deleting current page.");
            if (pages.Count <= 1) return;
            int pageIndexToRemove = currentPageIndex;
            selectedDrawables.Clear();
            hoveredDrawable = null;
            shapeInteractionHandler.ResetDragState();
            if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();

            pages.RemoveAt(pageIndexToRemove);
            currentPageIndex = Math.Max(0, Math.Min(pageIndexToRemove, pages.Count - 1));
            SwitchToPage(currentPageIndex, true);
        }

        private void SwitchToPage(int newPageIndex, bool forceSwitch = false)
        {
            AetherDraw.Plugin.Log?.Info($"[MainWindow.PageManagement] Switching to page index {newPageIndex}.");
            if (newPageIndex < 0 || newPageIndex >= pages.Count) return;
            if (!forceSwitch && newPageIndex == currentPageIndex && pages.Count > 0) return;

            currentPageIndex = newPageIndex;
            hoveredDrawable = null;
            selectedDrawables.Clear();
            shapeInteractionHandler.ResetDragState();
            if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();
        }

        private void CopySelected()
        {
            if (selectedDrawables.Any())
            {
                clipboard.Clear();
                foreach (var sel in selectedDrawables) { clipboard.Add(sel.Clone()); }
            }
        }
        private void PasteCopied()
        {
            if (clipboard.Any())
            {
                foreach (var dsel in selectedDrawables) dsel.IsSelected = false;
                selectedDrawables.Clear();
                Vector2 pasteOffsetLogical = new Vector2(15f, 15f);
                foreach (var item in clipboard)
                {
                    var newItemClone = item.Clone();
                    newItemClone.Translate(pasteOffsetLogical);
                    newItemClone.IsSelected = true;
                    DrawablesOfCurrentPageUi.Add(newItemClone);
                    selectedDrawables.Add(newItemClone);
                }
            }
        }
        private int GetLayerPriority(DrawMode mode)
        {
            switch (mode)
            {
                case DrawMode.TextTool: return 10;
                case DrawMode.Waymark1Image:
                case DrawMode.Waymark2Image:
                case DrawMode.Waymark3Image:
                case DrawMode.Waymark4Image:
                case DrawMode.WaymarkAImage:
                case DrawMode.WaymarkBImage:
                case DrawMode.WaymarkCImage:
                case DrawMode.WaymarkDImage:
                case DrawMode.RoleTankImage:
                case DrawMode.RoleHealerImage:
                case DrawMode.RoleMeleeImage:
                case DrawMode.RoleRangedImage:
                    return 4;
                case DrawMode.BossImage:
                case DrawMode.CircleAoEImage:
                case DrawMode.DonutAoEImage:
                case DrawMode.FlareImage:
                case DrawMode.LineStackImage:
                case DrawMode.SpreadImage:
                case DrawMode.StackImage:
                case DrawMode.StackIcon:
                case DrawMode.SpreadIcon:
                case DrawMode.TetherIcon:
                case DrawMode.BossIconPlaceholder:
                case DrawMode.AddMobIcon:
                    return 3;
                case DrawMode.Pen:
                case DrawMode.StraightLine:
                case DrawMode.Rectangle:
                case DrawMode.Circle:
                case DrawMode.Arrow:
                case DrawMode.Cone:
                case DrawMode.Dash:
                case DrawMode.Donut:
                    return 2;
                default: return 1;
            }
        }
    }
}
