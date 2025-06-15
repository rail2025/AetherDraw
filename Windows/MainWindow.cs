// AetherDraw/Windows/MainWindow.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Linq;
using AetherDraw.DrawingLogic;
using AetherDraw.Core;
using AetherDraw.UI;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using AetherDraw.Serialization;
using AetherDraw.Networking;
using System.IO;

namespace AetherDraw.Windows
{
    /// <summary>
    /// Represents the main window for the AetherDraw plugin.
    /// This window orchestrates UI components and delegates core functionalities.
    /// </summary>
    public class MainWindow : Window, IDisposable
    {
        private readonly Plugin plugin;
        private readonly Configuration configuration;
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly InPlaceTextEditor inPlaceTextEditor;
        private readonly CanvasController canvasController;
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;
        private readonly PlanIOManager planIOManager;
        private readonly ToolbarDrawer toolbarDrawer;
        private BaseDrawable? hoveredDrawable = null;
        private List<BaseDrawable> selectedDrawables = new List<BaseDrawable>();
        private List<BaseDrawable> clipboard = new List<BaseDrawable>();
        private DrawMode currentDrawMode = DrawMode.Pen;
        private Vector4 currentBrushColor;
        private float currentBrushThickness;
        private bool currentShapeFilled = false;
        private float ScaledCanvasGridSize => 40f * ImGuiHelpers.GlobalScale;
        private Vector2 currentCanvasDrawSize;

        /// <summary>
        /// A buffer to hold the pasted text for loading a plan.
        /// </summary>
        private string textToLoad = "";

        // State flags for managing popups
        private bool openClearConfirmPopup = false;
        private bool openDeletePageConfirmPopup = false;
        private bool openRoomClosingPopup = false;
        /// <summary>
        /// A flag used to reliably trigger the opening of the text import modal.
        /// </summary>
        private bool openImportTextModal = false;
        private string clearConfirmText = "";

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        /// <param name="plugin">The main plugin instance.</param>
        /// <param name="id">An optional unique ID to append to the window name for creating multiple instances.</param>
        public MainWindow(Plugin plugin, string id = "") : base($"AetherDraw Whiteboard{id}###AetherDrawMainWindow{id}")
        {
            this.plugin = plugin;
            this.configuration = plugin.Configuration;
            this.undoManager = new UndoManager();
            this.pageManager = new PageManager();
            this.inPlaceTextEditor = new InPlaceTextEditor(this.plugin, this.undoManager, this.pageManager);
            this.shapeInteractionHandler = new ShapeInteractionHandler(this.plugin, this.undoManager, this.pageManager);
            this.planIOManager = new PlanIOManager(this.pageManager, this.inPlaceTextEditor, Plugin.PluginInterface, () => this.ScaledCanvasGridSize, this.GetLayerPriority, this.pageManager.GetCurrentPageIndex);
            this.planIOManager.OnPlanLoadSuccess += HandleSuccessfulPlanLoad;
            this.toolbarDrawer = new ToolbarDrawer(() => this.currentDrawMode, (newMode) => this.currentDrawMode = newMode, this.shapeInteractionHandler, this.inPlaceTextEditor, this.PerformCopySelected, this.PerformPasteCopied, this.PerformClearAll, this.PerformUndo, () => this.currentShapeFilled, (isFilled) => this.currentShapeFilled = isFilled, this.undoManager, () => this.currentBrushThickness, (newThickness) => this.currentBrushThickness = newThickness, () => this.currentBrushColor, (newColor) => this.currentBrushColor = newColor);
            this.canvasController = new CanvasController(this.undoManager, this.pageManager, () => currentDrawMode, (newMode) => currentDrawMode = newMode, () => currentBrushColor, () => currentBrushThickness, () => currentShapeFilled, selectedDrawables, () => hoveredDrawable, (newHovered) => hoveredDrawable = newHovered, this.shapeInteractionHandler, this.inPlaceTextEditor, this.configuration, this.plugin);
            this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(850f * 0.75f * ImGuiHelpers.GlobalScale, 600f * ImGuiHelpers.GlobalScale), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
            this.RespectCloseHotkey = true;
            this.currentBrushColor = new Vector4(this.configuration.DefaultBrushColorR, this.configuration.DefaultBrushColorG, this.configuration.DefaultBrushColorB, this.configuration.DefaultBrushColorA);
            var initialThicknessPresets = new float[] { 1.5f, 4f, 7f, 10f };
            this.currentBrushThickness = initialThicknessPresets.Contains(this.configuration.DefaultBrushThickness) ? this.configuration.DefaultBrushThickness : initialThicknessPresets[1];
            undoManager.ClearHistory();

            // Subscribe to network events
            plugin.NetworkManager.OnConnected += HandleNetworkConnect;
            plugin.NetworkManager.OnDisconnected += HandleNetworkDisconnect;
            plugin.NetworkManager.OnStateUpdateReceived += HandleStateUpdateReceived;
            plugin.NetworkManager.OnRoomClosingWarning += HandleRoomClosingWarning;
        }

        /// <summary>
        /// Disposes of managed resources and unsubscribes from events.
        /// </summary>
        public void Dispose()
        {
            if (this.planIOManager != null) this.planIOManager.OnPlanLoadSuccess -= HandleSuccessfulPlanLoad;

            // Unsubscribe from network events
            plugin.NetworkManager.OnConnected -= HandleNetworkConnect;
            plugin.NetworkManager.OnDisconnected -= HandleNetworkDisconnect;
            plugin.NetworkManager.OnStateUpdateReceived -= HandleStateUpdateReceived;
            plugin.NetworkManager.OnRoomClosingWarning -= HandleRoomClosingWarning;
        }

        #region Network Event Handlers
        private void HandleNetworkConnect() => pageManager.EnterLiveMode();
        private void HandleNetworkDisconnect() => pageManager.ExitLiveMode();
        private void HandleRoomClosingWarning() => openRoomClosingPopup = true;

        /// <summary>
        /// Main handler for all incoming state updates from the server. It is page-aware.
        /// </summary>
        /// <param name="payload">The network payload containing the page index, action, and data.</param>
        private void HandleStateUpdateReceived(NetworkPayload payload)
        {
            if (!pageManager.IsLiveMode) return;

            var allPages = pageManager.GetAllPages();

            // If a ReplacePage command comes for a page that doesn't exist, create pages until it does.
            if (payload.Action == PayloadActionType.ReplacePage)
            {
                while (allPages.Count <= payload.PageIndex)
                {
                    // Add a new page but don't switch to it.
                    // The contents will be immediately overwritten by the payload data.
                    pageManager.AddNewPage(false);
                }
            }

            if (payload.PageIndex < 0 || payload.PageIndex >= allPages.Count)
            {
                Plugin.Log?.Warning($"Received state update for invalid page index: {payload.PageIndex}");
                return;
            }

            // A user should not process updates for objects they are currently dragging.
            if (payload.Action == PayloadActionType.UpdateObjects)
            {
                if (shapeInteractionHandler.DraggedObjectIds.Any())
                {
                    return; // Simplified: if we are dragging anything, ignore incoming updates.
                }
            }

            var targetPageDrawables = allPages[payload.PageIndex].Drawables;

            try
            {
                switch (payload.Action)
                {
                    case PayloadActionType.AddObjects:
                        if (payload.Data == null) return;
                        var receivedObjects = DrawableSerializer.DeserializePageFromBytes(payload.Data);
                        targetPageDrawables.AddRange(receivedObjects);
                        break;

                    case PayloadActionType.DeleteObjects:
                        if (payload.Data == null) return;
                        using (var ms = new MemoryStream(payload.Data))
                        using (var reader = new BinaryReader(ms))
                        {
                            int count = reader.ReadInt32();
                            for (int i = 0; i < count; i++)
                            {
                                var objectId = new Guid(reader.ReadBytes(16));
                                targetPageDrawables.RemoveAll(d => d.UniqueId == objectId);
                            }
                        }
                        break;

                    case PayloadActionType.UpdateObjects:
                        if (payload.Data == null) return;
                        var updatedObjects = DrawableSerializer.DeserializePageFromBytes(payload.Data);
                        foreach (var updatedObject in updatedObjects)
                        {
                            int index = targetPageDrawables.FindIndex(d => d.UniqueId == updatedObject.UniqueId);
                            if (index != -1)
                            {
                                targetPageDrawables[index] = updatedObject;
                            }
                            else
                            {
                                targetPageDrawables.Add(updatedObject); // Add if not found, for robustness
                            }
                        }
                        break;

                    case PayloadActionType.ClearPage:
                        targetPageDrawables.Clear();
                        break;

                    case PayloadActionType.ReplacePage:
                        if (payload.Data == null) return;
                        var fullPageState = DrawableSerializer.DeserializePageFromBytes(payload.Data);
                        allPages[payload.PageIndex].Drawables = fullPageState;
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, $"Failed to process received network payload action: {payload.Action}");
            }
        }
        #endregion

        /// <summary>
        /// Handles post-plan-load logic, including broadcasting the new plan in a live session.
        /// </summary>
        private void HandleSuccessfulPlanLoad()
        {
            ResetInteractionStates();
            undoManager.ClearHistory();

            // If in a live session, broadcast the new state of ALL pages to other clients.
            if (pageManager.IsLiveMode)
            {
                var allLoadedPages = pageManager.GetAllPages();
                for (int i = 0; i < allLoadedPages.Count; i++)
                {
                    var page = allLoadedPages[i];
                    var payloadData = DrawableSerializer.SerializePageToBytes(page.Drawables);

                    var payload = new NetworkPayload
                    {
                        PageIndex = i,
                        Action = PayloadActionType.ReplacePage,
                        Data = payloadData
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
            }
        }

        /// <inheritdoc/>
        public override void PreDraw() => Flags = configuration.IsMainWindowMovable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoMove;

        /// <inheritdoc/>
        public override void Draw()
        {
            if (openClearConfirmPopup) { ImGui.OpenPopup("Confirm Clear All"); openClearConfirmPopup = false; }
            if (openDeletePageConfirmPopup) { ImGui.OpenPopup("Confirm Delete Page"); openDeletePageConfirmPopup = false; }
            if (openRoomClosingPopup) { ImGui.OpenPopup("Room Closing"); openRoomClosingPopup = false; }

            using (var toolbarRaii = ImRaii.Child("ToolbarRegion", new Vector2(125f * ImGuiHelpers.GlobalScale, 0), true, ImGuiWindowFlags.None))
            {
                if (toolbarRaii) this.toolbarDrawer.DrawLeftToolbar();
            }

            ImGui.SameLine();

            using (var rightPaneRaii = ImRaii.Child("RightPane", Vector2.Zero, false, ImGuiWindowFlags.None))
            {
                if (rightPaneRaii)
                {
                    float bottomControlsHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().WindowPadding.Y * 2 + ImGui.GetStyle().ItemSpacing.Y;
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
            DrawConfirmationPopups();
        }

        private void PerformCopySelected() => CopySelected();
        private void PerformPasteCopied()
        {
            var currentDrawables = pageManager.GetCurrentPageDrawables();
            undoManager.RecordAction(currentDrawables, "Paste Drawables");
            var pastedItems = new List<BaseDrawable>();
            if (clipboard.Any())
            {
                foreach (var dsel in selectedDrawables) dsel.IsSelected = false;
                selectedDrawables.Clear();
                foreach (var item in clipboard)
                {
                    var newItemClone = item.Clone();
                    newItemClone.Translate(new Vector2(15f, 15f));
                    newItemClone.IsSelected = true;
                    currentDrawables.Add(newItemClone);
                    selectedDrawables.Add(newItemClone);
                    pastedItems.Add(newItemClone);
                }
            }
            if (pageManager.IsLiveMode && pastedItems.Any())
            {
                var payload = new NetworkPayload
                {
                    PageIndex = pageManager.GetCurrentPageIndex(),
                    Action = PayloadActionType.AddObjects,
                    Data = DrawableSerializer.SerializePageToBytes(pastedItems)
                };
                _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
            }
        }
        private void PerformClearAll()
        {
            if (pageManager.IsLiveMode)
            {
                openClearConfirmPopup = true;
            }
            else
            {
                var currentDrawables = pageManager.GetCurrentPageDrawables();
                if (currentDrawables.Any())
                {
                    undoManager.RecordAction(currentDrawables, "Clear All");
                    pageManager.ClearCurrentPageDrawables();
                }
                ResetInteractionStates();
            }
        }
        private void PerformUndo()
        {
            var undoneState = undoManager.Undo();
            if (undoneState == null) return;
            pageManager.SetCurrentPageDrawables(undoneState);
            ResetInteractionStates();
            if (pageManager.IsLiveMode)
            {
                var payload = new NetworkPayload
                {
                    PageIndex = pageManager.GetCurrentPageIndex(),
                    Action = PayloadActionType.ReplacePage,
                    Data = DrawableSerializer.SerializePageToBytes(undoneState)
                };
                _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
            }
        }

        /// <summary>
        /// Draws the bottom bar containing page tabs and main action buttons.
        /// </summary>
        /// <param name="barHeight">The calculated height for the bar.</param>
        private void DrawBottomControlsBar(float barHeight)
        {
            using var bottomBarChild = ImRaii.Child("BottomControlsRegion", new Vector2(0, barHeight), true, ImGuiWindowFlags.None);
            if (!bottomBarChild) return;
            DrawPageTabs();
            DrawActionButtons();
        }

        private void DrawPageTabs()
        {
            using var pageTabsChild = ImRaii.Child("PageTabsSubRegion", new Vector2(0, ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y), false, ImGuiWindowFlags.HorizontalScrollbar);
            if (!pageTabsChild) return;
            var currentPages = pageManager.GetAllPages();
            for (int i = 0; i < currentPages.Count; i++)
            {
                bool isSelectedPage = (i == pageManager.GetCurrentPageIndex());
                string pageName = pageManager.IsLiveMode ? $"L {currentPages[i].Name}" : currentPages[i].Name;
                Vector4 normalColor, activeColor;
                if (pageManager.IsLiveMode)
                {
                    normalColor = new Vector4(0.2f, 0.6f, 0.3f, 1.0f);
                    activeColor = new Vector4(1.0f, 0.84f, 0.0f, 1.0f);
                }
                else
                {
                    normalColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];
                    activeColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
                }
                using (ImRaii.PushColor(ImGuiCol.Button, isSelectedPage ? activeColor : normalColor))
                {
                    if (ImGui.Button($"{pageName}##Page{i}", new Vector2(ImGui.CalcTextSize(pageName).X + ImGui.GetStyle().FramePadding.X * 2.0f, ImGui.GetFrameHeight())))
                    {
                        if (!isSelectedPage) RequestSwitchToPage(i);
                    }
                }
                ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
            }

            if (ImGui.Button("+##AddPage", new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()))) RequestAddNewPage();
            ImGui.SameLine();
            if (ImGui.Button("Copy Page##CopyPageButton", new Vector2(ImGui.CalcTextSize("Copy Page").X + ImGui.GetStyle().FramePadding.X * 2.0f, ImGui.GetFrameHeight()))) RequestCopyPage();
            ImGui.SameLine();
            using (ImRaii.Disabled(!pageManager.HasCopiedPage()))
            {
                if (ImGui.Button("Paste Page##PastePageButton", new Vector2(ImGui.CalcTextSize("Paste Page").X + ImGui.GetStyle().FramePadding.X * 2.0f, ImGui.GetFrameHeight()))) RequestPastePage();
            }

            if (currentPages.Count > 1)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f)))
                using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f)))
                using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)))
                {
                    if (ImGui.Button("X##DeletePage", new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()))) RequestDeleteCurrentPage();
                }
            }
        }

        /// <summary>
        /// Draws the main action buttons in the bottom bar. The UI has been simplified to remove URL loading functionality.
        /// </summary>
        private void DrawActionButtons()
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;
            int numberOfActionButtons = 4; // Load, Save, WDIG, Live
            float totalSpacing = ImGui.GetStyle().ItemSpacing.X * (numberOfActionButtons - 1);
            float actionButtonWidth = (availableWidth - totalSpacing) / numberOfActionButtons;

            // --- Load Button ---
            if (ImGui.Button("Load##LoadButton", new Vector2(actionButtonWidth, 0)))
            {
                ImGui.OpenPopup("LoadPopup");
            }
            if (ImGui.BeginPopup("LoadPopup"))
            {
                if (ImGui.MenuItem("Load from File..."))
                {
                    planIOManager.RequestLoadPlan();
                }
                // Updated menu item to be more specific.
                if (ImGui.MenuItem("Load from Text..."))
                {
                    textToLoad = "";
                    openImportTextModal = true;
                }
                ImGui.EndPopup();
            }

            if (openImportTextModal)
            {
                ImGui.OpenPopup("Import From Text");
                openImportTextModal = false;
            }

            // --- Modal Popup for Text Input ---
            bool pOpen = true;
            if (ImGui.BeginPopupModal("Import From Text", ref pOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                // Updated description to remove mention of URLs.
                ImGui.Text("Paste the plan's Base64 text below.");

                ImGui.InputTextMultiline("##TextToLoad", ref textToLoad, 100000, new Vector2(400 * ImGuiHelpers.GlobalScale, 200 * ImGuiHelpers.GlobalScale));

                if (ImGui.Button("Import", new Vector2(120, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(textToLoad))
                    {
                        // Simplified logic to only call the text loading function.
                        planIOManager.RequestLoadPlanFromText(textToLoad);
                    }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            ImGui.SameLine();

            // --- Save Button ---
            if (ImGui.Button("Save##SaveButton", new Vector2(actionButtonWidth, 0)))
            {
                ImGui.OpenPopup("SavePopup");
            }
            if (ImGui.BeginPopup("SavePopup"))
            {
                if (ImGui.MenuItem("Save Plan to File..."))
                {
                    planIOManager.RequestSavePlan();
                }
                if (ImGui.MenuItem("Copy Plan to Clipboard"))
                {
                    planIOManager.CopyCurrentPlanToClipboardCompressed();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Export as Images (for WDIGViewer)"))
                {
                    planIOManager.RequestSaveImage(this.currentCanvasDrawSize);
                }
                ImGui.EndPopup();
            }

            ImGui.SameLine();

            // --- WDIG and Live Buttons ---
            if (ImGui.Button("Open WDIG##OpenWDIGButton", new Vector2(actionButtonWidth, 0)))
            {
                try { Plugin.CommandManager.ProcessCommand("/wdig"); }
                catch (Exception ex) { Plugin.Log?.Error(ex, "Error processing /wdig command."); }
            }
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Button, pageManager.IsLiveMode ? new Vector4(0.8f, 0.2f, 0.2f, 1.0f) : ImGui.GetStyle().Colors[(int)ImGuiCol.Button]))
            {
                if (ImGui.Button((pageManager.IsLiveMode ? "Disconnect" : "Join/Create Live") + "##LiveRoomButton", new Vector2(actionButtonWidth, 0)))
                {
                    if (pageManager.IsLiveMode) _ = plugin.NetworkManager.DisconnectAsync();
                    else plugin.ToggleLiveSessionUI();
                }
            }

            var fileError = planIOManager.LastFileDialogError;
            if (!string.IsNullOrEmpty(fileError)) { ImGui.Spacing(); ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), fileError); }
        }

        private void DrawConfirmationPopups()
        {
            bool popupOpen = true;
            if (ImGui.BeginPopupModal("Confirm Clear All", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("This will clear the canvas for everyone in the session.\nThis action cannot be undone.\n\nType DELETE to confirm.");
                ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
                ImGui.InputText("##ClearConfirm", ref clearConfirmText, 6);
                ImGui.Separator();
                using (ImRaii.Disabled(clearConfirmText.ToUpper() != "DELETE"))
                {
                    if (ImGui.Button("Confirm Clear", new Vector2(120 * ImGuiHelpers.GlobalScale, 0)))
                    {
                        var payload = new NetworkPayload
                        {
                            PageIndex = pageManager.GetCurrentPageIndex(),
                            Action = PayloadActionType.ClearPage,
                            Data = null
                        };
                        _ = plugin.NetworkManager.SendStateUpdateAsync(payload);

                        pageManager.ClearCurrentPageDrawables();
                        clearConfirmText = "";
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    clearConfirmText = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (ImGui.BeginPopupModal("Confirm Delete Page", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Are you sure you want to delete this page?");
                ImGui.Text("This action cannot be undone.");
                ImGui.Separator();
                if (ImGui.Button("Yes, Delete It", new Vector2(120 * ImGuiHelpers.GlobalScale, 0)))
                {
                    if (pageManager.DeleteCurrentPage())
                    {
                        ResetInteractionStates();
                        undoManager.ClearHistory();
                    }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (ImGui.BeginPopupModal("Room Closing", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("This live session is closing due to inactivity or because it has expired.");
                ImGui.Text("You will be disconnected shortly.");
                ImGui.Separator();
                if (ImGui.Button("OK", new Vector2(120 * ImGuiHelpers.GlobalScale, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private void ResetInteractionStates()
        {
            selectedDrawables.Clear();
            hoveredDrawable = null;
            shapeInteractionHandler.ResetDragState();
            if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();
        }

        private void DrawCanvas()
        {
            Vector2 canvasSizeForImGuiDrawing = ImGui.GetContentRegionAvail();
            currentCanvasDrawSize = canvasSizeForImGuiDrawing;
            if (canvasSizeForImGuiDrawing.X < 50f * ImGuiHelpers.GlobalScale) canvasSizeForImGuiDrawing.X = 50f * ImGuiHelpers.GlobalScale;
            if (canvasSizeForImGuiDrawing.Y < 50f * ImGuiHelpers.GlobalScale) canvasSizeForImGuiDrawing.Y = 50f * ImGuiHelpers.GlobalScale;

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            Vector2 canvasOriginScreen = ImGui.GetCursorScreenPos();

            drawList.AddRectFilled(canvasOriginScreen, canvasOriginScreen + canvasSizeForImGuiDrawing, ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.17f, 1.0f)));
            float scaledGridCellSize = ScaledCanvasGridSize;
            if (scaledGridCellSize > 0)
            {
                for (float x = scaledGridCellSize; x < canvasSizeForImGuiDrawing.X; x += scaledGridCellSize) drawList.AddLine(new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y), new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y + canvasSizeForImGuiDrawing.Y), ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)), Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));
                for (float y = scaledGridCellSize; y < canvasSizeForImGuiDrawing.Y; y += scaledGridCellSize) drawList.AddLine(new Vector2(canvasOriginScreen.X, canvasOriginScreen.Y + y), new Vector2(canvasOriginScreen.X + canvasSizeForImGuiDrawing.X, canvasOriginScreen.Y + y), ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)), Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));
            }
            drawList.AddRect(canvasOriginScreen - Vector2.One, canvasOriginScreen + canvasSizeForImGuiDrawing + Vector2.One, ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.45f, 1f)), 0f, ImDrawFlags.None, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));

            if (inPlaceTextEditor.IsEditing) { inPlaceTextEditor.RecalculateEditorBounds(canvasOriginScreen, ImGuiHelpers.GlobalScale); inPlaceTextEditor.DrawEditorUI(); }

            ImGui.SetCursorScreenPos(canvasOriginScreen);
            ImGui.InvisibleButton("##AetherDrawCanvasInteractionLayer", canvasSizeForImGuiDrawing);
            Vector2 mousePosLogical = (ImGui.GetMousePos() - canvasOriginScreen) / ImGuiHelpers.GlobalScale;
            if (!inPlaceTextEditor.IsEditing && ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            {
                canvasController.ProcessCanvasInteraction(mousePosLogical, ImGui.GetMousePos(), canvasOriginScreen, drawList, ImGui.IsMouseDown(ImGuiMouseButton.Left), ImGui.IsMouseClicked(ImGuiMouseButton.Left), ImGui.IsMouseReleased(ImGuiMouseButton.Left), ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left), GetLayerPriority);
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
            if (pageManager.IsLiveMode && pageManager.GetAllPages().Count >= 5) return;
            if (pageManager.AddNewPage(true))
            {
                ResetInteractionStates();
                undoManager.ClearHistory();
            }
        }
        private void RequestDeleteCurrentPage()
        {
            if (pageManager.GetAllPages().Count <= 1) return;
            openDeletePageConfirmPopup = true;
        }
        private void RequestCopyPage() => pageManager.CopyCurrentPageToClipboard();
        private void RequestPastePage()
        {
            if (!pageManager.HasCopiedPage()) return;
            var currentDrawables = pageManager.GetCurrentPageDrawables();
            undoManager.RecordAction(currentDrawables, "Paste Page (Overwrite)");
            if (pageManager.PastePageFromClipboard())
            {
                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.ReplacePage,
                        Data = DrawableSerializer.SerializePageToBytes(pageManager.GetCurrentPageDrawables())
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
                ResetInteractionStates();
            }
            else
            {
                undoManager.Undo();
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
            }
        }

        private int GetLayerPriority(DrawMode mode)
        {
            return mode switch
            {
                DrawMode.TextTool => 10,
                DrawMode.Waymark1Image or DrawMode.Waymark2Image or DrawMode.Waymark3Image or DrawMode.Waymark4Image or DrawMode.WaymarkAImage or DrawMode.WaymarkBImage or DrawMode.WaymarkCImage or DrawMode.WaymarkDImage or DrawMode.RoleTankImage or DrawMode.RoleHealerImage or DrawMode.RoleMeleeImage or DrawMode.RoleRangedImage or DrawMode.Party1Image or DrawMode.Party2Image or DrawMode.Party3Image or DrawMode.Party4Image or DrawMode.Party5Image or DrawMode.Party6Image or DrawMode.Party7Image or DrawMode.Party8Image or DrawMode.SquareImage or DrawMode.CircleMarkImage or DrawMode.TriangleImage or DrawMode.PlusImage or DrawMode.StackIcon or DrawMode.SpreadIcon or DrawMode.TetherIcon or DrawMode.BossIconPlaceholder or DrawMode.AddMobIcon => 5,
                DrawMode.BossImage or DrawMode.CircleAoEImage or DrawMode.DonutAoEImage or DrawMode.FlareImage or DrawMode.LineStackImage or DrawMode.SpreadImage or DrawMode.StackImage => 3,
                DrawMode.Pen or DrawMode.StraightLine or DrawMode.Rectangle or DrawMode.Circle or DrawMode.Arrow or DrawMode.Cone or DrawMode.Dash or DrawMode.Donut => 2,
                _ => 1,
            };
        }
    }
}
