// AetherDraw/Windows/MainWindow.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing; // For Window base class
using ImGuiNET;                    // For ImGui UI calls
using System.Linq;                 // For LINQ operations like .Any(), .OrderBy()
using AetherDraw.DrawingLogic; // For BaseDrawable, DrawMode, TextureManager etc.
using AetherDraw.Core;         // For PageManager, PlanIOManager, UndoManager, CanvasController etc.
using AetherDraw.UI;           // For ToolbarDrawer
using Dalamud.Interface.Utility;    // For ImGuiHelpers
using Dalamud.Interface.Utility.Raii; // For ImRaii (RAII wrappers for ImGui)

// ImageSharp usings are less critical here now but retained for any minor utilities or type refs.
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace AetherDraw.Windows
{
    /// <summary>
    /// Represents the main window for the AetherDraw plugin.
    /// This window orchestrates UI components like toolbars and the drawing canvas,
    /// and delegates core functionalities such as page management, file operations,
    /// undo logic, and canvas interactions to specialized manager and controller classes.
    /// Its primary role is UI presentation and coordination of backend services.
    /// </summary>
    public class MainWindow : Window, IDisposable
    {
        // Core plugin instance and configuration, providing access to global settings and services.
        private readonly Plugin plugin;
        private readonly Configuration configuration;

        // Specialized managers, controllers, and UI drawers for different functionalities.
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly DrawingLogic.InPlaceTextEditor inPlaceTextEditor; // Fully qualified for clarity
        private readonly CanvasController canvasController;
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;
        private readonly PlanIOManager planIOManager;
        private readonly ToolbarDrawer toolbarDrawer; // Handles drawing the left-side toolbar

        // UI Interaction State, owned by MainWindow as it pertains to the overall window context.
        private BaseDrawable? hoveredDrawable = null;
        private List<BaseDrawable> selectedDrawables = new List<BaseDrawable>();
        private List<BaseDrawable> clipboard = new List<BaseDrawable>();

        // Current Drawing Tool Settings, owned by MainWindow and passed to/updated by ToolbarDrawer via delegates.
        private DrawMode currentDrawMode = DrawMode.Pen;
        private Vector4 currentBrushColor;
        private float currentBrushThickness;
        private bool currentShapeFilled = false;

        /// <summary>
        /// Gets the scaled size of the grid cells on the drawing canvas.
        /// Base size is 40f, scaled by ImGui's global scale.
        /// </summary>
        private float ScaledCanvasGridSize => 40f * ImGuiHelpers.GlobalScale;

        // currentCanvasDrawSize stores the pixel dimensions of the drawable canvas area.
        // Updated each frame in DrawCanvas() and used for image export.
        private Vector2 currentCanvasDrawSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// Sets up dependencies, initializes managers, configures default states, and subscribes to events.
        /// </summary>
        /// <param name="plugin">The main plugin instance.</param>
        public MainWindow(Plugin plugin) : base("AetherDraw Whiteboard###AetherDrawMainWindow")
        {
            this.plugin = plugin;
            this.configuration = plugin.Configuration;
            AetherDraw.Plugin.Log?.Debug("[MainWindow] Initializing...");

            // Instantiate core functional components.
            this.undoManager = new UndoManager();
            this.pageManager = new PageManager();

            // InPlaceTextEditor now needs UndoManager and PageManager
            this.inPlaceTextEditor = new DrawingLogic.InPlaceTextEditor(this.undoManager, this.pageManager);

            // ShapeInteractionHandler now needs UndoManager and PageManager
            this.shapeInteractionHandler = new ShapeInteractionHandler(this.undoManager, this.pageManager);


            // Instantiate PlanIOManager, providing its dependencies.
            this.planIOManager = new PlanIOManager(
                this.pageManager,
                this.inPlaceTextEditor, // This instance is now correctly initialized
                Plugin.PluginInterface,
                () => this.ScaledCanvasGridSize,
                this.GetLayerPriority,
                () => this.pageManager.GetCurrentPageIndex()
            );
            // Subscribe to PlanIOManager's event for successful plan loads.
            this.planIOManager.OnPlanLoadSuccess += HandleSuccessfulPlanLoad;

            // Instantiate ToolbarDrawer, passing delegates for MainWindow's state and action methods.
            this.toolbarDrawer = new ToolbarDrawer(
                () => this.currentDrawMode, (newMode) => this.currentDrawMode = newMode,
                this.shapeInteractionHandler,
                this.inPlaceTextEditor, // This instance is now correctly initialized
                this.PerformCopySelected, this.PerformPasteCopied,
                this.PerformClearAll, this.PerformUndo,
                () => this.currentShapeFilled, (isFilled) => this.currentShapeFilled = isFilled,
                this.undoManager,
                () => this.currentBrushThickness, (newThickness) => this.currentBrushThickness = newThickness,
                () => this.currentBrushColor, (newColor) => this.currentBrushColor = newColor
            );

            // Instantiate CanvasController, passing UndoManager and PageManager.
            this.canvasController = new CanvasController(
                this.undoManager,
                this.pageManager,
                () => currentDrawMode,
                (newMode) => currentDrawMode = newMode,
                () => currentBrushColor,
                () => currentBrushThickness,
                () => currentShapeFilled,
                selectedDrawables,
                () => hoveredDrawable,
                (newHovered) => hoveredDrawable = newHovered,
                this.shapeInteractionHandler,
                this.inPlaceTextEditor, // This instance is now correctly initialized
                this.configuration
            );

            // Configure window size constraints.
            float targetMinimumWidth = 850f * 0.75f * ImGuiHelpers.GlobalScale;
            float targetMinimumHeight = 600f * ImGuiHelpers.GlobalScale;
            this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(targetMinimumWidth, targetMinimumHeight), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
            this.RespectCloseHotkey = true;

            // Initialize brush settings from saved configuration.
            this.currentBrushColor = new Vector4(this.configuration.DefaultBrushColorR, this.configuration.DefaultBrushColorG, this.configuration.DefaultBrushColorB, this.configuration.DefaultBrushColorA);
            var initialThicknessPresets = new float[] { 1.5f, 4f, 7f, 10f };
            this.currentBrushThickness = initialThicknessPresets.Contains(this.configuration.DefaultBrushThickness)
                                            ? this.configuration.DefaultBrushThickness
                                            : initialThicknessPresets[1];

            undoManager.ClearHistory();
            AetherDraw.Plugin.Log?.Debug("[MainWindow] Initialization complete.");
        }

        private void HandleSuccessfulPlanLoad()
        {
            AetherDraw.Plugin.Log?.Debug("[MainWindow] PlanIOManager reported successful plan load. Resetting UI states.");
            ResetInteractionStates();
            undoManager.ClearHistory();
        }

        public void Dispose()
        {
            AetherDraw.Plugin.Log?.Debug("[MainWindow] Dispose called.");
            if (this.planIOManager != null)
            {
                this.planIOManager.OnPlanLoadSuccess -= HandleSuccessfulPlanLoad;
            }
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
                if (toolbarRaii) this.toolbarDrawer.DrawLeftToolbar();
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
            planIOManager.DrawFileDialogs();
        }

        private void PerformCopySelected() => CopySelected();
        private void PerformPasteCopied() => PasteCopied();

        private void PerformClearAll()
        {
            var currentDrawables = pageManager.GetCurrentPageDrawables();
            if (currentDrawables.Any())
            {
                undoManager.RecordAction(currentDrawables, "Clear All");
                pageManager.ClearCurrentPageDrawables();
            }
            ResetInteractionStates();
        }

        private void PerformUndo()
        {
            var undoneDrawables = undoManager.Undo();
            if (undoneDrawables != null)
            {
                pageManager.SetCurrentPageDrawables(undoneDrawables);
                ResetInteractionStates();
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

                        var currentPages = pageManager.GetAllPages();
                        int currentVisiblePageIndex = pageManager.GetCurrentPageIndex();

                        for (int i = 0; i < currentPages.Count; i++)
                        {
                            bool isSelectedPage = (i == currentVisiblePageIndex);
                            string pageName = currentPages[i].Name;
                            float pageTabWidth = ImGui.CalcTextSize(pageName).X + ImGui.GetStyle().FramePadding.X * 2.0f + (10f * ImGuiHelpers.GlobalScale);
                            pageTabWidth = Math.Max(pageTabWidth, tabButtonHeight * 1.5f);

                            using (isSelectedPage ? ImRaii.PushColor(ImGuiCol.Button, activeTabColor) : null)
                            {
                                if (ImGui.Button(pageName, new Vector2(pageTabWidth, tabButtonHeight)))
                                {
                                    if (!isSelectedPage) RequestSwitchToPage(i);
                                }
                            }
                            ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
                        }

                        if (ImGui.Button("+##AddPage", new Vector2(tabButtonHeight, tabButtonHeight))) RequestAddNewPage();
                        if (currentPages.Count > 1)
                        {
                            ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X);
                            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f)))
                            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f)))
                            using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)))
                            {
                                if (ImGui.Button("X##DeletePage", new Vector2(tabButtonHeight, tabButtonHeight))) RequestDeleteCurrentPage();
                            }
                        }
                    }
                }

                float availableWidth = ImGui.GetContentRegionAvail().X;
                int numberOfActionButtons = 5;
                float totalSpacing = ImGui.GetStyle().ItemSpacing.X * (numberOfActionButtons - 1);
                float actionButtonWidth = (availableWidth - totalSpacing) / numberOfActionButtons;
                actionButtonWidth = Math.Max(actionButtonWidth, 80f * ImGuiHelpers.GlobalScale);

                if (ImGui.Button("Load Plan##LoadPlanButton", new Vector2(actionButtonWidth, 0)))
                {
                    planIOManager.RequestLoadPlan();
                }
                ImGui.SameLine();
                if (ImGui.Button("Save Plan##SavePlanButton", new Vector2(actionButtonWidth, 0)))
                {
                    planIOManager.RequestSavePlan();
                }
                ImGui.SameLine();
                if (ImGui.Button("Save as Image##SaveAsImageButton", new Vector2(actionButtonWidth, 0)))
                {
                    planIOManager.RequestSaveImage(this.currentCanvasDrawSize);
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
                    if (ImGui.Button("Join/Create Live##LiveRoomButton", new Vector2(actionButtonWidth, 0)))
                    {
                        AetherDraw.Plugin.Log?.Info("[MainWindow] 'Join/Create Live' button clicked (currently placeholder).");
                    }
                }

                var fileError = planIOManager.LastFileDialogError;
                if (!string.IsNullOrEmpty(fileError))
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), fileError);
                }
            }
        }

        private void ResetInteractionStates()
        {
            selectedDrawables.Clear();
            hoveredDrawable = null;
            shapeInteractionHandler.ResetDragState();
            if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();
            AetherDraw.Plugin.Log?.Debug("[MainWindow] Interaction states reset.");
        }

        private void DrawCanvas()
        {
            Vector2 canvasSizeForImGuiDrawing = ImGui.GetContentRegionAvail();
            currentCanvasDrawSize = canvasSizeForImGuiDrawing;

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
            var drawablesToRender = pageManager.GetCurrentPageDrawables();
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

        private void RequestAddNewPage()
        {
            if (pageManager.AddNewPage(true))
            {
                ResetInteractionStates();
                undoManager.ClearHistory();
            }
        }

        private void RequestDeleteCurrentPage()
        {
            if (pageManager.DeleteCurrentPage())
            {
                ResetInteractionStates();
                undoManager.ClearHistory();
            }
        }

        private void RequestSwitchToPage(int newPageIndex)
        {
            if (pageManager.SwitchToPage(newPageIndex))
            {
                ResetInteractionStates();
                undoManager.ClearHistory();
            }
        }

        private void CopySelected()
        {
            if (selectedDrawables.Any())
            {
                clipboard.Clear();
                foreach (var sel in selectedDrawables) { clipboard.Add(sel.Clone()); }
                AetherDraw.Plugin.Log?.Info($"[MainWindow] Copied {clipboard.Count} drawables to clipboard.");
            }
        }

        private void PasteCopied()
        {
            if (clipboard.Any())
            {
                var currentDrawables = pageManager.GetCurrentPageDrawables();
                undoManager.RecordAction(currentDrawables, "Paste Drawables");

                foreach (var dsel in selectedDrawables) dsel.IsSelected = false;
                selectedDrawables.Clear();

                Vector2 pasteOffsetLogical = new Vector2(15f, 15f);

                foreach (var item in clipboard)
                {
                    var newItemClone = item.Clone();
                    newItemClone.Translate(pasteOffsetLogical);
                    newItemClone.IsSelected = true;
                    currentDrawables.Add(newItemClone);
                    selectedDrawables.Add(newItemClone);
                }
                AetherDraw.Plugin.Log?.Info($"[MainWindow] Pasted {selectedDrawables.Count} drawables from clipboard.");
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
