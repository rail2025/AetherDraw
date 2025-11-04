using AetherDraw.Core;
using AetherDraw.DrawingLogic;
using AetherDraw.Networking;
using AetherDraw.Serialization;
using AetherDraw.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using Lumina.Excel.Sheets.Experimental;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace AetherDraw.Windows
{
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
        private string textToLoad = "";
        private string raidPlanUrlToLoad = "";
        private bool openClearConfirmPopup = false;
        private bool openDeletePageConfirmPopup = false;
        private bool openRoomClosingPopup = false;
        private bool openImportTextModal = false;
        private bool openRaidPlanImportModal = false;
        private bool isAwaitingUndoEcho = false;
        private string clearConfirmText = "";

        private bool openStatusSearchPopup = false;
        private string statusSearchInput = "";
        private List<Lumina.Excel.Sheets.Status> statusSearchResults = new();

        public interface IPlanAction
        {
            void Undo(MainWindow window);
            string Description { get; }
        }

        private class PlanMovePageAction : IPlanAction
        {
            private readonly int fromIndex;
            private readonly int toIndex;
            public string Description => $"Move Page from {fromIndex + 1} to {toIndex + 1}";
            public PlanMovePageAction(int from, int to)
            {
                this.fromIndex = from;
                this.toIndex = to;
            }

            public void Undo(MainWindow window)
            {
                window.RequestPageMove(this.toIndex, this.fromIndex, true);
            }
        }
        private readonly Stack<IPlanAction> planUndoStack = new();
        private bool isAwaitingMovePageEcho = false;

        private bool openEmojiInputModal = false;
        private bool openBackgroundUrlModal = false;
        private readonly ConcurrentDictionary<Guid, bool> pendingEchoGuids = new();
        private string backgroundUrlInput = "";

        // helper grid methods
        private Vector2 SnapToGrid(Vector2 point)
        {
            if (!configuration.IsSnapToGrid || configuration.GridSize <= 0) return point;
            return new Vector2(
                MathF.Round(point.X / configuration.GridSize) * configuration.GridSize,
                MathF.Round(point.Y / configuration.GridSize) * configuration.GridSize
            );
        }

        private bool IsDrawingOrPlacingMode(DrawMode mode)
        {
            return mode != DrawMode.Select && mode != DrawMode.Eraser;
        }


        public MainWindow(Plugin plugin, string id = "") : base($"AetherDraw Whiteboard{id}###AetherDrawMainWindow{id}")
        {
            this.plugin = plugin;
            this.configuration = plugin.Configuration;
            this.undoManager = new UndoManager();
            this.pageManager = new PageManager();
            //this.pageManager.InitializeDefaultPage();

            this.undoManager.InitializeStacks(this.pageManager.GetAllPages().Count);

            this.inPlaceTextEditor = new InPlaceTextEditor(this.plugin, this.undoManager, this.pageManager);
            this.shapeInteractionHandler = new ShapeInteractionHandler(this.plugin, this.undoManager, this.pageManager, this.AddToPending, this.OnObjectsCommitted);
            this.planIOManager = new PlanIOManager(this.pageManager, this.inPlaceTextEditor, Plugin.PluginInterface, () => this.ScaledCanvasGridSize, this.GetLayerPriority, this.pageManager.GetCurrentPageIndex);
            this.planIOManager.OnPlanLoadSuccess += HandleSuccessfulPlanLoad;

            // The subscription for local file import is commented out but kept for reference.
            // this.planIOManager.OnBackgroundImageSelected += PlaceCustomBackground; 

            this.canvasController = new CanvasController(
                this.undoManager, this.pageManager,
                () => currentDrawMode, (newMode) => currentDrawMode = newMode,
                () => currentBrushColor, () => currentBrushThickness, () => currentShapeFilled,
                selectedDrawables, () => hoveredDrawable, (newHovered) => hoveredDrawable = newHovered,
                this.shapeInteractionHandler, this.inPlaceTextEditor, this.configuration, this.plugin
            );

            this.toolbarDrawer = new ToolbarDrawer(
                this.plugin, this.pageManager,
                () => this.currentDrawMode, (newMode) => this.currentDrawMode = newMode,
                this.shapeInteractionHandler, this.inPlaceTextEditor,
                this.PerformCopySelected, this.PerformPasteCopied, this.PerformClearAll, this.PerformUndo,
                () => this.currentShapeFilled, (isFilled) => {
                    // Record state BEFORE changing fill
                    if (selectedDrawables.Count == 1) // Only record if applying to a selection
                        undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Change Fill Style");
                    this.currentShapeFilled = isFilled;
                    // Apply immediately if one item is selected
                    if (selectedDrawables.Count == 1)
                        ApplyFillToSelection(isFilled);
                },
                this.undoManager,
                this.planUndoStack,
                () => this.currentBrushThickness, (newThickness) => {
                    // Record state BEFORE changing thickness
                    if (selectedDrawables.Count == 1) // Only record if applying to a selection
                        undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Change Thickness");
                    this.currentBrushThickness = newThickness;
                    // Apply immediately if one item is selected
                    if (selectedDrawables.Count == 1)
                        ApplyThicknessToSelection(newThickness);
                },
                () => this.currentBrushColor, (newColor) => {
                    // Record state BEFORE changing color
                    if (selectedDrawables.Count == 1) // Only record if applying to a selection
                        undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Change Color");
                    this.currentBrushColor = newColor;
                    // Apply immediately if one item is selected
                    if (selectedDrawables.Count == 1)
                        ApplyColorToSelection(newColor);
                },
                () => this.openEmojiInputModal = true,
                () => this.openStatusSearchPopup = true,
                () => this.openBackgroundUrlModal = true, // This action opens the URL modal
                () => this.configuration.IsGridVisible, (v) => { this.configuration.IsGridVisible = v; this.configuration.Save(); },
                () => this.configuration.GridSize, (v) => { this.configuration.GridSize = v; this.configuration.Save(); },
                () => this.configuration.IsSnapToGrid, (v) => { this.configuration.IsSnapToGrid = v; this.configuration.Save(); }
            );

            this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(850f * 0.75f * ImGuiHelpers.GlobalScale, 600f * ImGuiHelpers.GlobalScale), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
            this.RespectCloseHotkey = true;
            this.currentBrushColor = new Vector4(this.configuration.DefaultBrushColorR, this.configuration.DefaultBrushColorG, this.configuration.DefaultBrushColorB, this.configuration.DefaultBrushColorA);
            var initialThicknessPresets = new float[] { 1.5f, 4f, 7f, 10f };
            this.currentBrushThickness = initialThicknessPresets.Contains(this.configuration.DefaultBrushThickness) ? this.configuration.DefaultBrushThickness : initialThicknessPresets[1];
            //undoManager.ClearHistory();

            plugin.NetworkManager.OnConnected += HandleNetworkConnect;
            plugin.NetworkManager.OnDisconnected += HandleNetworkDisconnect;
            plugin.NetworkManager.OnStateUpdateReceived += HandleStateUpdateReceived;
            plugin.NetworkManager.OnRoomClosingWarning += HandleRoomClosingWarning;
        }

        private void ApplyColorToSelection(Vector4 newColor)
        {
            if (selectedDrawables.Count != 1) return;
            var drawable = selectedDrawables[0];
            drawable.Color = newColor;
            shapeInteractionHandler.CommitObjectChanges(new List<BaseDrawable> { drawable }); // Sends network update
        }

        private void ApplyThicknessToSelection(float newThickness)
        {
            if (selectedDrawables.Count != 1) return;
            var drawable = selectedDrawables[0];
            drawable.Thickness = newThickness;
            // Specific adjustments for Arrow arrowhead size based on thickness
            if (drawable is DrawableArrow arrow)
            {
                arrow.UpdateArrowheadSize();
            }
            shapeInteractionHandler.CommitObjectChanges(new List<BaseDrawable> { drawable }); // Sends network update
        }

        private void ApplyFillToSelection(bool isFilled)
        {
            if (selectedDrawables.Count != 1) return;
            var drawable = selectedDrawables[0];
            drawable.IsFilled = isFilled;
            // Adjust alpha for shapes when filling/unfilling
            if (drawable.Color.W < 1.0f || isFilled) // Check if alpha needs adjustment
            {
                var tempColor = drawable.Color; // Get the struct
                tempColor.W = isFilled ? 0.4f : 1.0f; // Modify the copy
                drawable.Color = tempColor; // Assign the modified struct back
            }
            // Use CommitDragChanges instead of CommitObjectChanges
            shapeInteractionHandler.CommitObjectChanges(new List<BaseDrawable> { drawable }); // Sends network update
        }

        public void Dispose()
        {
            if (this.planIOManager != null) this.planIOManager.OnPlanLoadSuccess -= HandleSuccessfulPlanLoad;
            // this.planIOManager.OnBackgroundImageSelected -= PlaceCustomBackground;
            plugin.NetworkManager.OnConnected -= HandleNetworkConnect;
            plugin.NetworkManager.OnDisconnected -= HandleNetworkDisconnect;
            plugin.NetworkManager.OnStateUpdateReceived -= HandleStateUpdateReceived;
            plugin.NetworkManager.OnRoomClosingWarning -= HandleRoomClosingWarning;
        }

        private void PlaceCustomBackground(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
            {
                planIOManager.LastFileDialogError = "Invalid URL provided.";
                return;
            }

            var drawables = pageManager.GetCurrentPageDrawables();

            // This action will be recorded for both local and live sessions.
            undoManager.RecordAction(drawables, "Import Background");

            // Define a smaller default size for imported images
            var defaultImageSize = new Vector2(150f, 150f);

            // Remove any existing background image first.
            //drawables.RemoveAll(d => d.ObjectDrawMode == DrawMode.Image);

            var newImage = new DrawableImage(
                DrawMode.Image,
                imageUrl,
                this.currentCanvasDrawSize / (2f * ImGuiHelpers.GlobalScale),
                //this.currentCanvasDrawSize / ImGuiHelpers.GlobalScale,
                defaultImageSize,
                Vector4.One, 0f
            );
            newImage.IsPreview = false;

            // Add the new background to the beginning of the list.
            //drawables.Insert(0, backgroundImage);
            drawables.Add(newImage);

            // If in a live session, send the entire page state to ensure everyone is synced.
            // This is the safest way to handle adding/replacing a background.
            if (pageManager.IsLiveMode)
            {
                var payload = new NetworkPayload
                {
                    PageIndex = pageManager.GetCurrentPageIndex(),
                    Action = PayloadActionType.ReplacePage, // Use ReplacePage to guarantee sync
                    Data = DrawableSerializer.SerializePageToBytes(drawables)
                };
                _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
            }
        }

        #region Network Event Handlers
        private async void HandleNetworkConnect()
        {
            pageManager.EnterLiveMode(); // create the default page locally
            this.undoManager.InitializeStacks(this.pageManager.GetAllPages().Count);

            // Add a small delay to allow the server to send the room history first.
            await Task.Delay(100);

            var currentPageDrawables = pageManager.GetCurrentPageDrawables();
            if (currentPageDrawables != null && currentPageDrawables.Any())
            {
                Plugin.Log?.Debug("[Sync Fix] Connected. Sending my default page as a candidate for initial state.");
                var payload = new NetworkPayload
                {
                    PageIndex = pageManager.GetCurrentPageIndex(),
                    Action = PayloadActionType.ReplacePage,
                    Data = DrawableSerializer.SerializePageToBytes(currentPageDrawables)
                };
                _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
            }
        }
        private void HandleNetworkDisconnect()
        {
            pageManager.ExitLiveMode();
            this.undoManager.InitializeStacks(this.pageManager.GetAllPages().Count);
        }
        private void HandleRoomClosingWarning() => openRoomClosingPopup = true;

        private void HandleStateUpdateReceived(NetworkPayload payload)
        {
            if (!pageManager.IsLiveMode) return;
            // This condition is expanded to handle both AddNewPage and ReplacePage
            if (payload.Action == PayloadActionType.ReplacePage || payload.Action == PayloadActionType.AddNewPage)
            {
                var pages = pageManager.GetAllPages();
                while (pages.Count <= payload.PageIndex)
                {
                    pageManager.AddNewPage(false);
                }
            }

            var allPages = pageManager.GetAllPages(); // Get a fresh reference after potential additions
            if (payload.PageIndex < 0 || payload.PageIndex >= allPages.Count)
            {
                Plugin.Log?.Warning($"Received state update for invalid page index: {payload.PageIndex}");
                return;
            }
            // old copy paste logic
            /*if (payload.Action == PayloadActionType.UpdateObjects)
            {
                if (shapeInteractionHandler.DraggedObjectIds.Any())
                {
                    return;
                }
            }*/
            var targetPageDrawables = allPages[payload.PageIndex].Drawables;

            // Record the state BEFORE applying the remote change, if it's a modifying action
            bool isModifyingAction = payload.Action switch
            {
                PayloadActionType.AddObjects => true,
                PayloadActionType.DeleteObjects => true,
                PayloadActionType.UpdateObjects => true,
                PayloadActionType.ClearPage => true,
                PayloadActionType.ReplacePage => true, // Also record state before replacing
                PayloadActionType.DeletePage => true, // Record state before deleting
                // AddNewPage doesn't modify existing state to undo *to*, so skip recording
                // UpdateGrid/UpdateGridVisibility are config changes, not drawable state, skip recording
                _ => false
            };

            // Only record undo if it's a modifying action AND NOT a ReplacePage (which is used for Undo sync)
            if (isModifyingAction && payload.Action != PayloadActionType.ReplacePage)
            {
                // Ensure the page exists before trying to get drawables
                if (payload.PageIndex < allPages.Count)
                {
                    // For DeletePage, we record the state of the page *being* deleted
                    var drawablesToRecord = allPages[payload.PageIndex].Drawables;
                    // Temporarily set the active stack to the payload's target page
                    int originalActivePage = pageManager.GetCurrentPageIndex();
                    undoManager.SetActivePage(payload.PageIndex);

                    // record even if it's an echo (like ReplacePage from undo) because state needed *before* the echo replaced it to undo back again.
                    undoManager.RecordAction(drawablesToRecord, $"Remote {payload.Action} on Page {payload.PageIndex + 1}");

                    // Restore the user's previously active page
                    undoManager.SetActivePage(originalActivePage);
                }
                else if (payload.Action != PayloadActionType.DeletePage) // Don't log error if trying to record a page that's about to be deleted remotely anyway
                {
                    Plugin.Log?.Warning($"Tried to record undo state for remote action on non-existent page index: {payload.PageIndex}");
                }
            }

            try
            {
                switch (payload.Action)
                {
                    case PayloadActionType.AddObjects:
                        if (payload.Data == null) return;
                        var receivedObjects = DrawableSerializer.DeserializePageFromBytes(payload.Data);
                        // Filter out objects that this client has already created locally to prevent duplication from the server echo.
                        var objectsToAdd = receivedObjects.Where(obj => !pendingEchoGuids.TryRemove(obj.UniqueId, out _)).ToList();

                        if (objectsToAdd.Any())
                        {
                            targetPageDrawables.AddRange(objectsToAdd);
                        }
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

                        //Plugin.Log?.Debug($"[Receiver] Received UpdateObjects payload containing {updatedObjects.Count} object(s).");
                        //Plugin.Log?.Debug($"[Receiver] Current local drawable count: {targetPageDrawables.Count}");
                        foreach (var updatedObject in updatedObjects)
                        {
                            if (pendingEchoGuids.TryRemove(updatedObject.UniqueId, out _))
                            {
                                continue;
                            }
                             //Plugin.Log?.Debug($"[Receiver] Processing incoming object with ID: {updatedObject.UniqueId} and Type: {updatedObject.ObjectDrawMode}");
                            int index = targetPageDrawables.FindIndex(d => d.UniqueId == updatedObject.UniqueId);
                            if (index != -1)
                            {
                                //Plugin.Log?.Debug($"[Receiver] Found match by ID. Updating object at index {index}.");
                                targetPageDrawables[index] = updatedObject;
                            }
                            else
                            {
                                //Plugin.Log?.Debug($"[Receiver] No match for ID {updatedObject.UniqueId}. Adding it as a new object.");
                                targetPageDrawables.Add(updatedObject);
                            }
                        }
                        //Plugin.Log?.Debug($"[Receiver] After processing, local drawable count is now: {targetPageDrawables.Count}");
                        break;
                    case PayloadActionType.ClearPage:
                        targetPageDrawables.Clear();
                        break;
                    case PayloadActionType.ReplacePage:
                        if (payload.Data == null) return;
                        if (isAwaitingUndoEcho)
                        {
                            isAwaitingUndoEcho = false; // Clear flag
                            Plugin.Log?.Debug("[Network] Ignored own ReplacePage echo from Undo.");
                            return; // Stop processing this message
                        }
                        var fullPageState = DrawableSerializer.DeserializePageFromBytes(payload.Data);
                        allPages[payload.PageIndex].Drawables = fullPageState;
                        break;
                    case PayloadActionType.AddNewPage:
                        this.undoManager.AddStack(payload.PageIndex);
                        break;
                    case PayloadActionType.DeletePage:
                        this.undoManager.RemoveStack(payload.PageIndex);
                        pageManager.DeletePageAtIndex(payload.PageIndex);
                        break;
                    case PayloadActionType.UpdateGrid:
                        if (payload.Data != null && payload.Data.Length >= 4)
                        {
                            float newGridSize = BitConverter.ToSingle(payload.Data, 0);
                            this.configuration.GridSize = newGridSize;
                        }
                        break;
                    case PayloadActionType.UpdateGridVisibility:
                        if (payload.Data != null && payload.Data.Length >= 1)
                        {
                            bool isVisible = payload.Data[0] == 1;
                            this.configuration.IsGridVisible = isVisible;
                        }
                        break;
                    case PayloadActionType.MovePage:
                        if (payload.Data == null) return;
                        using (var ms = new MemoryStream(payload.Data))
                        using (var reader = new BinaryReader(ms))
                        {
                            // Read the 8-byte packet
                            int fromIndex = reader.ReadInt32();
                            int toIndex = reader.ReadInt32();
                            Plugin.Log?.Debug($"[MainWindow] HandleStateUpdate(MovePage): Received move from {fromIndex} to {toIndex}.");

                            // Check if we are the sender of an Undo move.
                            if (this.isAwaitingMovePageEcho)
                            {
                                // This is our own undo echo.
                                this.isAwaitingMovePageEcho = false;
                                Plugin.Log?.Debug($"[MainWindow] Ignoring own Undo echo.");
                                return; // We already applied this state locally.
                            }

                            // This is a NEW move from another client (or an echo of our own NEW move).
                            // 1. Push the action to the stack for undo.
                            planUndoStack.Push(new PlanMovePageAction(fromIndex, toIndex));
                            Plugin.Log?.Debug($"[MainWindow] Pushed new MovePage action.");

                            // 2. Apply the state change.
                            this.undoManager.MoveStack(fromIndex, toIndex);
                            pageManager.MovePageAndRenumber(fromIndex, toIndex);
                            ResetInteractionStates();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, $"Failed to process received network payload action: {payload.Action}");
            }
        }
        #endregion

        private void HandleSuccessfulPlanLoad()
        {
            ResetInteractionStates();
            this.undoManager.InitializeStacks(pageManager.GetAllPages().Count);
            planUndoStack.Clear();
            if (pageManager.IsLiveMode)
            {
                var allLoadedPages = pageManager.GetAllPages();
                for (int i = 0; i < allLoadedPages.Count; i++)
                {
                    var page = allLoadedPages[i];
                    var payloadData = DrawableSerializer.SerializePageToBytes(page.Drawables);
                    var payload = new NetworkPayload { PageIndex = i, Action = PayloadActionType.ReplacePage, Data = payloadData };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
            }
        }

        public override void PreDraw() => Flags = configuration.IsMainWindowMovable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoMove;

        public override void Draw()
        {
            TextureManager.DoMainThreadWork();

            if (openClearConfirmPopup) { ImGui.OpenPopup("Confirm Clear All"); openClearConfirmPopup = false; }
            if (openDeletePageConfirmPopup) { ImGui.OpenPopup("Confirm Delete Page"); openDeletePageConfirmPopup = false; }
            if (openRoomClosingPopup) { ImGui.OpenPopup("Room Closing"); openRoomClosingPopup = false; }
            if (openRaidPlanImportModal) { ImGui.OpenPopup("Import from URL"); openRaidPlanImportModal = false; }
            if (openEmojiInputModal) { ImGui.OpenPopup("Place Emoji"); openEmojiInputModal = false; }
            if (openStatusSearchPopup) { ImGui.OpenPopup("Status Search"); openStatusSearchPopup = false; }
            if (openBackgroundUrlModal) { ImGui.OpenPopup("Import Image from URL"); openBackgroundUrlModal = false; }


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
            DrawEmojiInputModal();
            DrawStatusSearchPopup();
            DrawBackgroundUrlModal();
        }

        private void DrawBackgroundUrlModal()
        {
            bool pOpen = true;
            if (ImGui.BeginPopupModal("Import Image from URL", ref pOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Paste the URL of an image below.");
                ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
                ImGui.InputText("##BackgroundUrl", ref backgroundUrlInput, 512);
                ImGui.TextDisabled("e.g., a direct link to a .png or .jpeg from Imgur.");

                ImGui.Separator();

                if (ImGui.Button("Import", new Vector2(120, 0)))
                {
                    PlaceCustomBackground(backgroundUrlInput);
                    backgroundUrlInput = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    backgroundUrlInput = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private void DrawStatusSearchPopup()
        {
            bool pOpen = true;
            ImGui.SetNextWindowSize(new Vector2(350 * ImGuiHelpers.GlobalScale, 400 * ImGuiHelpers.GlobalScale));
            if (ImGui.BeginPopupModal("Status Search", ref pOpen, ImGuiWindowFlags.None))
            {
                ImGui.Text("Search for a status icon by name:");

                if (ImGui.InputText("##StatusSearch", ref statusSearchInput, 100))
                {
                    if (string.IsNullOrWhiteSpace(statusSearchInput))
                    {
                        statusSearchResults.Clear();
                    }
                    else
                    {
                        var statusSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
                        if (statusSheet != null)
                        {
                            string lowerSearch = statusSearchInput.ToLowerInvariant();
                            statusSearchResults = statusSheet
                                .Where(s => s.Icon > 0 && !string.IsNullOrEmpty(s.Name.ToString()) && s.Name.ToString().ToLowerInvariant().Contains(lowerSearch))
                                .Take(50) // Limit results to 50
                                .ToList();
                        }
                    }
                }

                ImGui.Separator();

                using (var child = ImRaii.Child("##statusresults", new Vector2(-1, -ImGui.GetFrameHeightWithSpacing() - 5), true, ImGuiWindowFlags.HorizontalScrollbar))
                {
                    if (child)
                    {
                        int columns = (int)(ImGui.GetContentRegionAvail().X / (32 * ImGuiHelpers.GlobalScale + ImGui.GetStyle().ItemSpacing.X));
                        if (columns < 1) columns = 1;
                        int i = 0;

                        foreach (var status in statusSearchResults)
                        {
                            string iconPath = $"luminaicon:{status.Icon}";
                            var iconTex = TextureManager.GetTexture(iconPath);

                            if (iconTex != null)
                            {
                                if ((i % columns) != 0) ImGui.SameLine();

                                if (ImGui.ImageButton(iconTex.Handle, new Vector2(32 * ImGuiHelpers.GlobalScale, 32 * ImGuiHelpers.GlobalScale)))
                                {
                                    canvasController.StartPlacingStatusIcon(status.Icon);
                                    ImGui.CloseCurrentPopup();
                                }
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.SetTooltip($"{status.Name} (ID: {status.RowId})");
                                }
                                i++;
                            }
                        }
                    }
                }

                if (ImGui.Button("Close"))
                {
                    statusSearchInput = "";
                    statusSearchResults.Clear(); 
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void DrawEmojiInputModal()
        {
            bool pOpen = true;
            ImGui.SetNextWindowSize(new Vector2(300 * ImGuiHelpers.GlobalScale, 0));
            if (ImGui.BeginPopupModal("Place Emoji", ref pOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("1. Find an emoji and copy it.");
                ImGui.Text("2. Click the button below to place it.");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Place from Clipboard", new Vector2(-1, 0)))
                {
                    try
                    {
                        string clipboardText = ImGui.GetClipboardText();
                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            string emojiToPlace = char.IsSurrogatePair(clipboardText, 0)
                                ? clipboardText.Substring(0, 2)
                                : clipboardText.Substring(0, 1);

                            canvasController.StartPlacingEmoji(emojiToPlace);
                            ImGui.CloseCurrentPopup();
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.Error(ex, "Failed to place emoji from clipboard.");
                        ImGui.CloseCurrentPopup();
                    }
                }

                if (ImGui.Button("Cancel", new Vector2(-1, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
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
                foreach (var item in pastedItems)
                    AddToPending(item.UniqueId); 
                
                var payload = new NetworkPayload { PageIndex = pageManager.GetCurrentPageIndex(), Action = PayloadActionType.AddObjects, Data = DrawableSerializer.SerializePageToBytes(pastedItems) };
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
            if (planUndoStack.Count > 0)
            {
                var lastPlanAction = planUndoStack.Pop();
                Plugin.Log?.Debug($"[MainWindow] Undoing Plan Action: {lastPlanAction.Description}");
                lastPlanAction.Undo(this);
            }
            else if (undoManager.CanUndo())
            {
                Plugin.Log?.Debug($"[MainWindow] Undoing Drawing Action.");
                var undoneState = undoManager.Undo();
                if (undoneState == null) return;

                pageManager.SetCurrentPageDrawables(undoneState);
                ResetInteractionStates();
                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload { PageIndex = pageManager.GetCurrentPageIndex(), Action = PayloadActionType.ReplacePage, Data = DrawableSerializer.SerializePageToBytes(undoneState) };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
            }
        }

        private void DrawBottomControlsBar(float barHeight)
        {
            using var bottomBarChild = ImRaii.Child("BottomControlsRegion", new Vector2(0, barHeight), true, ImGuiWindowFlags.None);
            if (!bottomBarChild) return;
            DrawPageTabs();
            DrawActionButtons();
        }

        private unsafe void DrawPageTabs()
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
                    var buttonSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
                    if (ImGui.Button($"{pageName}##Page{i}", buttonSize))
                    {
                        if (!isSelectedPage) RequestSwitchToPage(i);
                    }
                }

                if (ImGui.BeginDragDropSource())
                {
                    int draggedIndex = i;
                    ImGui.SetDragDropPayload("AETHERDRAW_PAGE_DRAG", new ReadOnlySpan<byte>(&draggedIndex, sizeof(int)), ImGuiCond.None);
                    ImGui.Text($"Moving Page {pageName}");
                    ImGui.EndDragDropSource();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("AETHERDRAW_PAGE_DRAG");
                    if (payload.Data != null && payload.DataSize == sizeof(int))
                    {
                        int fromIndex = *(int*)payload.Data;
                        RequestPageMove(fromIndex, i);
                    }
                    ImGui.EndDragDropTarget();
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
                int currentPageIndex = pageManager.GetCurrentPageIndex(); // Get current page index for the buttons
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f)))
                using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f)))
                using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)))
                {
                    if (ImGui.Button("X##DeletePage", new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()))) RequestDeleteCurrentPage();
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(currentPageIndex == 0))
                {
                    if (ImGui.Button("<##MovePageLeft", new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()))) RequestPageMove(currentPageIndex, currentPageIndex - 1);
                }
                ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
                using (ImRaii.Disabled(currentPageIndex == currentPages.Count - 1))
                {
                    if (ImGui.Button(">##MovePageRight", new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()))) RequestPageMove(currentPageIndex, currentPageIndex + 1);
                }
            }

            ImGui.SameLine();
            ImGui.InvisibleButton("##PageDropTargetEnd", new Vector2(Math.Max(50f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X), ImGui.GetFrameHeight()));
            if (ImGui.BeginDragDropTarget())
            {
                ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("AETHERDRAW_PAGE_DRAG");
                if (payload.Data != null && payload.DataSize == sizeof(int))
                { 
                    int fromIndex = *(int*)payload.Data;
                    RequestPageMove(fromIndex, currentPages.Count - 1);
                }
                ImGui.EndDragDropTarget();
            }
        }

        private void DrawActionButtons()
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;
            int numberOfActionButtons = 4;
            float totalSpacing = ImGui.GetStyle().ItemSpacing.X * (numberOfActionButtons - 1);
            float actionButtonWidth = (availableWidth - totalSpacing) / numberOfActionButtons;

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
                if (ImGui.MenuItem("Load from Text..."))
                {
                    textToLoad = "";
                    openImportTextModal = true;
                }
                if (ImGui.MenuItem("Import from URL..."))
                {
                    raidPlanUrlToLoad = "";
                    openRaidPlanImportModal = true;
                }
                ImGui.EndPopup();
            }
            if (openImportTextModal)
            {
                ImGui.OpenPopup("Import From Text");
                openImportTextModal = false;
            }
            if (openRaidPlanImportModal)
            {
                ImGui.OpenPopup("Import from URL");
                openRaidPlanImportModal = false;
            }

            bool pOpen = true;
            if (ImGui.BeginPopupModal("Import From Text", ref pOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Paste the plan's Base64 text below.");
                ImGui.InputTextMultiline("##TextToLoad", ref textToLoad, 100000, new Vector2(400 * ImGuiHelpers.GlobalScale, 200 * ImGuiHelpers.GlobalScale));
                if (ImGui.Button("Import", new Vector2(120, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(textToLoad))
                        planIOManager.RequestLoadPlanFromText(textToLoad);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            if (ImGui.BeginPopupModal("Import from URL", ref pOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Enter the URL below.");
                ImGui.InputText("##Url", ref raidPlanUrlToLoad, 256);
                if (ImGui.Button("Import##URLImport", new Vector2(120, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(raidPlanUrlToLoad))
                    {
                        _ = planIOManager.RequestLoadPlanFromUrl(raidPlanUrlToLoad);
                    }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel##RaidPlanCancel"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            ImGui.SameLine();

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
                        var payload = new NetworkPayload { PageIndex = pageManager.GetCurrentPageIndex(), Action = PayloadActionType.ClearPage, Data = null };
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
                    if (pageManager.IsLiveMode)
                    {
                        this.undoManager.RemoveStack(pageManager.GetCurrentPageIndex()); 
                        isAwaitingUndoEcho = true;
                        // In live mode, send a command to the server and wait for the echo.
                        var payload = new NetworkPayload
                        {
                            PageIndex = pageManager.GetCurrentPageIndex(),
                            Action = PayloadActionType.DeletePage,
                            Data = null
                        };
                        _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                    }
                    else
                    {
                        // In offline mode, delete the page locally immediately.
                        this.undoManager.RemoveStack(pageManager.GetCurrentPageIndex());
                        if (pageManager.DeleteCurrentPage())
                        {
                            ResetInteractionStates();
                        }
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
            if (configuration.IsGridVisible)
            {
                float scaledGridCellSize = configuration.GridSize * ImGuiHelpers.GlobalScale;
                if (scaledGridCellSize > 2)
                {
                    var gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
                    for (float x = scaledGridCellSize; x < canvasSizeForImGuiDrawing.X; x += scaledGridCellSize)
                        drawList.AddLine(new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y), new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y + canvasSizeForImGuiDrawing.Y), ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)), Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));
                    for (float y = scaledGridCellSize; y < canvasSizeForImGuiDrawing.Y; y += scaledGridCellSize)
                        drawList.AddLine(new Vector2(canvasOriginScreen.X, canvasOriginScreen.Y + y), new Vector2(canvasOriginScreen.X + canvasSizeForImGuiDrawing.X, canvasOriginScreen.Y + y), ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)), Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));
                }
            }
            drawList.AddRect(canvasOriginScreen - Vector2.One, canvasOriginScreen + canvasSizeForImGuiDrawing + Vector2.One, ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.45f, 1f)), 0f, ImDrawFlags.None, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));
                if (inPlaceTextEditor.IsEditing) { inPlaceTextEditor.RecalculateEditorBounds(canvasOriginScreen, ImGuiHelpers.GlobalScale); inPlaceTextEditor.DrawEditorUI(); }
            
            ImGui.SetCursorScreenPos(canvasOriginScreen);
            ImGui.InvisibleButton("##AetherDrawCanvasInteractionLayer", canvasSizeForImGuiDrawing);
            Vector2 mousePosLogical = (ImGui.GetMousePos() - canvasOriginScreen) / ImGuiHelpers.GlobalScale;

            if (!inPlaceTextEditor.IsEditing && ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            {
                // --- SNAPPING LOGIC FOR DRAWING/PLACING NEW OBJECTS ---
                Vector2 finalMousePosLogical = mousePosLogical;
                if (configuration.IsSnapToGrid && IsDrawingOrPlacingMode(currentDrawMode))
                {
                    finalMousePosLogical = SnapToGrid(mousePosLogical);
                }

                canvasController.ProcessCanvasInteraction(finalMousePosLogical, ImGui.GetMousePos(), canvasOriginScreen, drawList, ImGui.IsMouseDown(ImGuiMouseButton.Left), ImGui.IsMouseClicked(ImGuiMouseButton.Left), ImGui.IsMouseReleased(ImGuiMouseButton.Left), ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left), GetLayerPriority);
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
            if (pageManager.IsLiveMode)
            {
                // Prevent adding too many pages in a live session.
                if (pageManager.GetAllPages().Count >= 30) return; // Use a reasonable limit.

                // In live mode, send a command and wait for the echo.
                var payload = new NetworkPayload
                {
                    // The new page will be at the end of the list, so its index is the current count.
                    PageIndex = pageManager.GetAllPages().Count,
                    Action = PayloadActionType.AddNewPage,
                    Data = null
                };
                _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
            }
            else
            {
                // In offline mode, add the page locally immediately.
                if (pageManager.AddNewPage(true))
                {
                    this.undoManager.AddStack(pageManager.GetAllPages().Count - 1);
                    this.undoManager.SetActivePage(pageManager.GetCurrentPageIndex());
                    ResetInteractionStates();
                }
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
                    var payload = new NetworkPayload { PageIndex = pageManager.GetCurrentPageIndex(), Action = PayloadActionType.ReplacePage, Data = DrawableSerializer.SerializePageToBytes(pageManager.GetCurrentPageDrawables()) };
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
                this.undoManager.SetActivePage(newPageIndex);
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
                DrawMode.EmojiImage => 6,
                DrawMode.Image => 0,
                DrawMode.Waymark1Image or DrawMode.Waymark2Image or DrawMode.Waymark3Image or DrawMode.Waymark4Image or DrawMode.WaymarkAImage or DrawMode.WaymarkBImage or DrawMode.WaymarkCImage or DrawMode.WaymarkDImage or DrawMode.RoleTankImage or DrawMode.RoleHealerImage or DrawMode.RoleMeleeImage or DrawMode.RoleRangedImage or DrawMode.Party1Image or DrawMode.Party2Image or DrawMode.Party3Image or DrawMode.Party4Image or DrawMode.Party5Image or DrawMode.Party6Image or DrawMode.Party7Image or DrawMode.Party8Image or DrawMode.SquareImage or DrawMode.CircleMarkImage or DrawMode.TriangleImage or DrawMode.PlusImage or DrawMode.StackIcon or DrawMode.SpreadIcon or DrawMode.TetherIcon or DrawMode.BossIconPlaceholder or DrawMode.AddMobIcon or DrawMode.Dot1Image or DrawMode.Dot2Image or DrawMode.Dot3Image or DrawMode.Dot4Image or DrawMode.Dot5Image or DrawMode.Dot6Image or DrawMode.Dot7Image or DrawMode.Dot8Image => 5,
                DrawMode.BossImage or DrawMode.CircleAoEImage or DrawMode.DonutAoEImage or DrawMode.FlareImage or DrawMode.LineStackImage or DrawMode.SpreadImage or DrawMode.StackImage => 3,
                DrawMode.Pen or DrawMode.StraightLine or DrawMode.Rectangle or DrawMode.Circle or DrawMode.Arrow or DrawMode.Cone or DrawMode.Dash or DrawMode.Donut => 2,
                _ => 1,
            };
        }
        private async void AddToPending(Guid guid)
        {
            pendingEchoGuids.TryAdd(guid, true);
            await Task.Delay(500);
            pendingEchoGuids.TryRemove(guid, out _);
        }
        private void OnObjectsCommitted(List<BaseDrawable> committedDrawables)
        {
            if (pageManager.IsLiveMode && committedDrawables.Any())
            {
                //logging for id tracing
                foreach (var drawable in committedDrawables)
                {
                    Plugin.Log?.Debug($"[Sender] Sending UpdateObjects for object with ID: {drawable.UniqueId} and Type: {drawable.ObjectDrawMode}");
                }
                var payload = new NetworkPayload
                {
                    PageIndex = pageManager.GetCurrentPageIndex(),
                    Action = PayloadActionType.UpdateObjects,
                    Data = DrawableSerializer.SerializePageToBytes(committedDrawables)
                };
                _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
            }
        }
        private void RequestPageMove(int fromIndex, int toIndex, bool isUndo = false)
        {
            if (fromIndex == toIndex || fromIndex < 0 || toIndex < 0 || fromIndex >= pageManager.GetAllPages().Count || toIndex >= pageManager.GetAllPages().Count)
                return;
            Plugin.Log?.Debug($"[MainWindow] RequestPageMove from {fromIndex} to {toIndex}. IsUndo: {isUndo}");

            // OFFLINE logic:
            if (!plugin.NetworkManager.IsConnected)
            {
                if (!isUndo)
                {
                    planUndoStack.Push(new PlanMovePageAction(fromIndex, toIndex));
                    Plugin.Log?.Debug($"[MainWindow] (Offline) Pushed MovePage action to Plan Undo Stack.");
                }
                Plugin.Log?.Debug($"[MainWindow] (Offline) Moving local state.");
                this.undoManager.MoveStack(fromIndex, toIndex);
                pageManager.MovePageAndRenumber(fromIndex, toIndex);
                ResetInteractionStates();
                return;
            }

            // ONLINE logic:
            // Both NEW moves and UNDO moves must send a packet to the server.

            if (isUndo)
            {
                // This is an UNDO action.
                // Set the flag so we ignore our own echo.
                this.isAwaitingMovePageEcho = true;
                Plugin.Log?.Debug($"[MainWindow] (Online-Undo) Moving local state and sending packet.");
                // We must apply the state change locally *now*.
                this.undoManager.MoveStack(fromIndex, toIndex);
                pageManager.MovePageAndRenumber(fromIndex, toIndex);
                ResetInteractionStates();
            }
            else
            {
                // This is a NEW action.
                Plugin.Log?.Debug($"[MainWindow] (Online) Sending MovePage packet.");
                // We wait for the echo to apply the state change.
            }

            // Send an 8-BYTE packet in all online cases.
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(fromIndex);
            writer.Write(toIndex);
            var payload = new NetworkPayload { PageIndex = 0, Action = PayloadActionType.MovePage, Data = ms.ToArray() };
            _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
        }
    }
}
