using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Linq;
using AetherDraw.DrawingLogic;
using Dalamud.Interface.Utility; // For ImGuiHelpers
using Dalamud.Interface.Utility.Raii; // For ImRaii

namespace AetherDraw.Windows
{
    public class MainWindow : Window, IDisposable
    {
        // Represents a single drawable page with its elements.
        private class PageData
        {
            public string Name { get; set; } = "1"; // Default name for a new page.
            public List<BaseDrawable> Drawables { get; set; } = new List<BaseDrawable>();
        }

        private readonly Plugin plugin;
        private readonly Configuration configuration;
        private readonly ShapeInteractionHandler shapeInteractionHandler;

        private List<PageData> pages = new List<PageData>();
        private int currentPageIndex = 0;

        private static readonly List<BaseDrawable> EmptyDrawablesFallback = new List<BaseDrawable>();
        // Provides safe access to the drawables of the currently selected page.
        private List<BaseDrawable> DrawablesListOfCurrentPage =>
            (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            ? pages[currentPageIndex].Drawables
            : EmptyDrawablesFallback;

        private BaseDrawable? currentDrawingObject = null; // The object currently being drawn by the user.
        private BaseDrawable? hoveredDrawable = null; // The drawable object currently under the mouse cursor.
        private List<BaseDrawable> selectedDrawables = new List<BaseDrawable>(); // List of currently selected drawable objects.
        private List<BaseDrawable> clipboard = new List<BaseDrawable>(); // For copy-paste functionality.

        private bool isDrawingOnCanvas = false; // Flag to indicate if a drawing action is in progress on the canvas.

        // Scaled radii for eraser visuals and logic.
        private float ScaledEraserVisualRadius => 5f * ImGuiHelpers.GlobalScale;
        private float ScaledEraserLogicRadius => 10f * ImGuiHelpers.GlobalScale;

        private Vector2 lastMouseDragPosRelative = Vector2.Zero; // Stores the last mouse position during a drag operation.

        private float ScaledImageDefaultSize => 32f * ImGuiHelpers.GlobalScale; // Default scaled size for placed images.

        private DrawMode currentDrawMode = DrawMode.Pen; // Active drawing mode.
        private Vector4 currentBrushColor; // Current color selected for drawing.
        private float currentBrushThickness; // Unscaled thickness preset value.
        private bool currentShapeFilled = false; // Whether shapes like rectangles/circles should be filled.

        // Predefined thickness values for brush/pen.
        private static readonly float[] ThicknessPresets = new float[] { 1.5f, 4f, 7f, 10f };
        // Predefined color palette for quick color selection.
        private static readonly Vector4[] ColorPalette = new Vector4[] {
            new Vector4(1.0f,1.0f,1.0f,1.0f), // White
            new Vector4(0.0f,0.0f,0.0f,1.0f), // Black
            new Vector4(1.0f,0.0f,0.0f,1.0f), // Red
            new Vector4(0.0f,1.0f,0.0f,1.0f), // Green
            new Vector4(0.0f,0.0f,1.0f,1.0f), // Blue
            new Vector4(1.0f,1.0f,0.0f,1.0f), // Yellow
            new Vector4(1.0f,0.0f,1.0f,1.0f), // Magenta
            new Vector4(0.0f,1.0f,1.0f,1.0f), // Cyan
            new Vector4(0.5f,0.5f,0.5f,1.0f), // Medium Grey
            new Vector4(0.8f,0.4f,0.0f,1.0f)  // Brown (Orange-Brown)
        };

        private float ScaledCanvasGridSize => 40f * ImGuiHelpers.GlobalScale; // Scaled size for canvas grid cells.

        public MainWindow(Plugin plugin) : base("AetherDraw Whiteboard###AetherDrawMainWindow")
        {
            this.plugin = plugin;
            this.configuration = plugin.Configuration;
            this.shapeInteractionHandler = new ShapeInteractionHandler();

            float targetMinimumWidth = 850f * 0.75f * ImGuiHelpers.GlobalScale;
            float targetMinimumHeight = 600f * ImGuiHelpers.GlobalScale;

            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(targetMinimumWidth, targetMinimumHeight),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
            this.RespectCloseHotkey = true;
            this.currentBrushColor = new Vector4(this.configuration.DefaultBrushColorR, this.configuration.DefaultBrushColorG, this.configuration.DefaultBrushColorB, this.configuration.DefaultBrushColorA);
            this.currentBrushThickness = ThicknessPresets.Contains(this.configuration.DefaultBrushThickness) ? this.configuration.DefaultBrushThickness : ThicknessPresets[1];

            if (pages.Count == 0) { AddNewPage(switchToPage: false); }
            currentPageIndex = 0;
        }

        public void Dispose()
        {
            // Specific disposal logic for MainWindow, if any, would go here.
        }

        public override void PreDraw()
        {
            Flags = configuration.IsMainWindowMovable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoMove;
        }

        public override void Draw()
        {
            float scaledToolbarWidth = 125f * ImGuiHelpers.GlobalScale; // Toolbar width is now halved.

            using (var toolbarChild = ImRaii.Child("ToolbarRegion", new Vector2(scaledToolbarWidth, 0), true, ImGuiWindowFlags.None))
            {
                if (toolbarChild) { DrawToolbarControls(); }
            }

            ImGui.SameLine();

            using (var rightPaneChild = ImRaii.Child("RightPane", Vector2.Zero, false, ImGuiWindowFlags.None))
            {
                if (rightPaneChild)
                {
                    // Approximate height for bottom bar, e.g., for two rows of controls.
                    float bottomControlsBarHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().WindowPadding.Y * 2 + ImGui.GetStyle().ItemSpacing.Y;

                    float canvasAvailableHeight = ImGui.GetContentRegionAvail().Y - bottomControlsBarHeight - ImGui.GetStyle().ItemSpacing.Y;
                    if (canvasAvailableHeight < 50f * ImGuiHelpers.GlobalScale) canvasAvailableHeight = 50f * ImGuiHelpers.GlobalScale;

                    if (ImGui.BeginChild("CanvasDrawingArea", new Vector2(0, canvasAvailableHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                    {
                        DrawCanvas();
                    }
                    ImGui.EndChild();

                    DrawBottomControlsBar(bottomControlsBarHeight);
                }
            }
        }

        private void DrawToolbarControls()
        {
            Vector4 activeToolColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            float availableWidth = ImGui.GetContentRegionAvail().X;

            // Width for buttons when 2 are in a row.
            float btnWidthHalf = (availableWidth - ImGui.GetStyle().ItemSpacing.X * 1) / 2f;
            btnWidthHalf = Math.Max(btnWidthHalf, 30f * ImGuiHelpers.GlobalScale);

            // Helper local function to create a tool button.
            void ToolButton(string label, DrawMode mode, float buttonWidth)
            {
                bool isSelected = currentDrawMode == mode;
                using (isSelected ? ImRaii.PushColor(ImGuiCol.Button, activeToolColor) : null)
                {
                    if (ImGui.Button($"{label}##ToolBtn_{mode}", new Vector2(buttonWidth, 0)))
                    {
                        currentDrawMode = mode;
                        FinalizeCurrentDrawing();
                        if (mode != DrawMode.Select) shapeInteractionHandler.ResetDragState();
                    }
                }
            }

            // Helper local function to create a tool button for placing images.
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
                        FinalizeCurrentDrawing();
                        if (mode != DrawMode.Select) shapeInteractionHandler.ResetDragState();
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
                        var textSize = ImGui.CalcTextSize(label.Substring(0, Math.Min(label.Length, 3)));
                        ImGui.GetWindowDrawList().AddText(itemMin + (new Vector2(buttonWidth, ImGui.GetFrameHeight()) - textSize) / 2f, ImGui.GetColorU32(ImGuiCol.Text), label.Substring(0, Math.Min(label.Length, 3)));
                    }
                }
            }

            ToolButton("Select", DrawMode.Select, btnWidthHalf); ImGui.SameLine();
            ToolButton("Eraser", DrawMode.Eraser, btnWidthHalf);

            if (ImGui.Button("Copy", new Vector2(btnWidthHalf, 0))) CopySelected(); ImGui.SameLine();
            if (ImGui.Button("Paste", new Vector2(btnWidthHalf, 0))) PasteCopied();

            ImGui.Checkbox("Fill Shape", ref currentShapeFilled);
            // "Clear All" button on a new line, taking btnWidthHalf width.
            float clearAllButtonWidth = btnWidthHalf;

            if (ImGui.Button("Clear All", new Vector2(clearAllButtonWidth, 0)))
            {
                if (DrawablesListOfCurrentPage.Any())
                {
                    DrawablesListOfCurrentPage.Clear();
                }
                currentDrawingObject = null; isDrawingOnCanvas = false; selectedDrawables.Clear(); hoveredDrawable = null;
                shapeInteractionHandler.ResetDragState();
                Plugin.Log.Information($"Page {(pages.Count > 0 && currentPageIndex < pages.Count ? pages[currentPageIndex].Name : "N/A")} cleared.");
            }
            ImGui.Separator();

            ToolButton("Pen", DrawMode.Pen, btnWidthHalf); ImGui.SameLine();
            ToolButton("Line", DrawMode.StraightLine, btnWidthHalf);
            ToolButton("Dash", DrawMode.Dash, btnWidthHalf); ImGui.SameLine();
            ToolButton("Rect", DrawMode.Rectangle, btnWidthHalf);
            ToolButton("Circle", DrawMode.Circle, btnWidthHalf); ImGui.SameLine();
            ToolButton("Arrow", DrawMode.Arrow, btnWidthHalf);
            ToolButton("Cone", DrawMode.Cone, btnWidthHalf);
            ImGui.NewLine();
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
            // Calculate width for 4 thickness buttons to fit in the available toolbar width.
            float thicknessButtonWidth = availableWidth / ThicknessPresets.Length;
            thicknessButtonWidth = Math.Max(thicknessButtonWidth, 20f * ImGuiHelpers.GlobalScale); // Minimum scaled width for each button.

            for (int i = 0; i < ThicknessPresets.Length; i++)
            {
                if (i > 0) ImGui.SameLine(0, 0.0f); // Place buttons on the same line.
                float t = ThicknessPresets[i];
                bool isSelectedThickness = Math.Abs(currentBrushThickness - t) < 0.01f;

                using (isSelectedThickness ? ImRaii.PushColor(ImGuiCol.Button, activeToolColor) : null)
                {
                    if (ImGui.Button($"{t:0}##ThicknessBtn{i}", new Vector2(thicknessButtonWidth, 0))) currentBrushThickness = t;
                }
            }
            ImGui.Separator();

            int colorsPerRow = 5; 
            float smallColorButtonBaseSize = (availableWidth - ImGui.GetStyle().ItemSpacing.X * (colorsPerRow - 1)) / colorsPerRow;
            float smallColorButtonActualSize = Math.Max(smallColorButtonBaseSize, ImGui.GetTextLineHeight() * 0.8f);
            smallColorButtonActualSize = Math.Max(smallColorButtonActualSize, 16f * ImGuiHelpers.GlobalScale);
            Vector2 colorButtonDimensions = new Vector2(smallColorButtonActualSize, smallColorButtonActualSize);

            Vector4 selectedColorIndicatorColorVec = new Vector4(0.9f, 0.9f, 0.1f, 1.0f);
            uint selectedColorIndicatorU32 = ImGui.GetColorU32(selectedColorIndicatorColorVec);
            float selectedColorIndicatorThickness = 3.0f * ImGuiHelpers.GlobalScale;

            for (int i = 0; i < ColorPalette.Length; i++)
            {
                bool isSelectedColor = (ColorPalette[i].X == currentBrushColor.X &&
                                        ColorPalette[i].Y == currentBrushColor.Y &&
                                        ColorPalette[i].Z == currentBrushColor.Z &&
                                        ColorPalette[i].W == currentBrushColor.W);

                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
                try
                {
                    if (ImGui.ColorButton($"##ColorPaletteButton{i}", ColorPalette[i], ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, colorButtonDimensions))
                    {
                        currentBrushColor = ColorPalette[i];
                    }
                }
                finally
                {
                    ImGui.PopStyleVar();
                }

                if (isSelectedColor)
                {
                    ImDrawListPtr foregroundDrawList = ImGui.GetForegroundDrawList();
                    Vector2 rectMin = ImGui.GetItemRectMin() - new Vector2(1, 1) * ImGuiHelpers.GlobalScale;
                    Vector2 rectMax = ImGui.GetItemRectMax() + new Vector2(1, 1) * ImGuiHelpers.GlobalScale;
                    foregroundDrawList.AddRect(rectMin, rectMax, selectedColorIndicatorU32, 2f * ImGuiHelpers.GlobalScale, ImDrawFlags.None, selectedColorIndicatorThickness);
                }
                if ((i + 1) % colorsPerRow != 0 && i < ColorPalette.Length - 1) ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X / 2f);
            }
        }

        private void DrawBottomControlsBar(float barHeight)
        {
            using (var tabBarChild = ImRaii.Child("TabBarRegion", new Vector2(0, barHeight), true, ImGuiWindowFlags.None))
            {
                if (!tabBarChild) return;

                float buttonHeight = ImGui.GetFrameHeight();
                Vector4 activeButtonStyleColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
                Vector4 selectedTabColorVec = new Vector4(
                    Math.Clamp(activeButtonStyleColor.X * 0.8f, 0.0f, 1.0f),
                    Math.Clamp(activeButtonStyleColor.Y * 0.8f, 0.0f, 1.0f),
                    Math.Clamp(activeButtonStyleColor.Z * 1.2f, 0.0f, 1.0f),
                    activeButtonStyleColor.W);

                for (int i = 0; i < pages.Count; i++)
                {
                    bool isSelectedPage = (i == currentPageIndex);
                    string pageName = pages[i].Name;
                    float buttonTextWidth = ImGui.CalcTextSize(pageName).X;
                    float pageButtonWidth = buttonTextWidth + ImGui.GetStyle().FramePadding.X * 2.0f + (10f * ImGuiHelpers.GlobalScale);
                    pageButtonWidth = Math.Max(pageButtonWidth, buttonHeight * 0.8f);

                    using (isSelectedPage ? ImRaii.PushColor(ImGuiCol.Button, selectedTabColorVec) : null)
                    {
                        if (ImGui.Button(pageName, new Vector2(pageButtonWidth, buttonHeight)))
                        {
                            if (!isSelectedPage) SwitchToPage(i);
                        }
                    }
                    ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
                }

                float plusButtonWidth = buttonHeight;
                if (ImGui.Button("+", new Vector2(plusButtonWidth, buttonHeight))) AddNewPage();

                if (pages.Count > 1)
                {
                    ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X);
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
                    try
                    {
                        if (ImGui.Button("X", new Vector2(plusButtonWidth, buttonHeight))) DeleteCurrentPage();
                    }
                    finally
                    {
                        ImGui.PopStyleColor(3);
                    }
                }
            }
        }

        private void AddNewPage(bool switchToPage = true)
        {
            FinalizeCurrentDrawing();
            int newPageNumber = 1;
            if (pages.Any())
            {
                newPageNumber = pages.Select(p => int.TryParse(p.Name, out int num) ? num : 0).DefaultIfEmpty(0).Max() + 1;
            }
            var newPage = new PageData { Name = newPageNumber.ToString() };

            float baseMinWindowWidth = 850f * 0.75f;
            float baseToolbarWidth = 125f; // Using the new halved toolbar width base.
            float logicalRefCanvasWidth = baseMinWindowWidth - baseToolbarWidth;
            float logicalRefCanvasHeight = 550f; // Retaining a fixed logical height for waymark placement.

            float refCanvasWidth = logicalRefCanvasWidth;
            float refCanvasHeight = logicalRefCanvasHeight;

            Vector2 canvasCenter = new Vector2(refCanvasWidth / 2f, refCanvasHeight / 2f);
            float waymarkPlacementRadius = Math.Min(refCanvasWidth, refCanvasHeight) * 0.40f;
            Vector2 waymarkImageSize = new Vector2(30f * ImGuiHelpers.GlobalScale, 30f * ImGuiHelpers.GlobalScale);
            Vector4 waymarkTint = Vector4.One;

            var waymarksToPreload = new[]
            {
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
                Vector2 position = new Vector2(x, y);

                var drawableImage = new DrawableImage(wmInfo.Mode, wmInfo.Path, position, waymarkImageSize, waymarkTint);
                drawableImage.IsPreview = false;
                newPage.Drawables.Add(drawableImage);
            }

            pages.Add(newPage);
            if (switchToPage)
            {
                SwitchToPage(pages.Count - 1);
            }
        }

        private void DeleteCurrentPage()
        {
            if (pages.Count <= 1) return;
            FinalizeCurrentDrawing();
            int pageIndexToRemove = currentPageIndex;
            pages.RemoveAt(pageIndexToRemove);
            currentPageIndex = Math.Max(0, Math.Min(pageIndexToRemove, pages.Count - 1));
            SwitchToPage(currentPageIndex, true);
        }

        private void SwitchToPage(int newPageIndex, bool forceSwitch = false)
        {
            if (newPageIndex < 0 || newPageIndex >= pages.Count || (!forceSwitch && newPageIndex == currentPageIndex && pages.Count > 0)) return;
            FinalizeCurrentDrawing();
            currentPageIndex = newPageIndex;
            currentDrawingObject = null;
            hoveredDrawable = null;
            selectedDrawables.Clear();
            shapeInteractionHandler.ResetDragState();
            isDrawingOnCanvas = false;
        }

        private void FinalizeCurrentDrawing()
        {
            if (currentDrawingObject != null && pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            {
                if (isDrawingOnCanvas)
                {
                    currentDrawingObject.IsPreview = false;
                    bool isValidObject = true;
                    float minRadiusScaled = 2f * ImGuiHelpers.GlobalScale;
                    float minDistanceSqScaled = (2f * ImGuiHelpers.GlobalScale) * (2f * ImGuiHelpers.GlobalScale);

                    if (currentDrawingObject is DrawablePath p && p.PointsRelative.Count < 2) isValidObject = false;
                    else if (currentDrawingObject is DrawableDash d && d.PointsRelative.Count < 2) isValidObject = false;
                    else if (currentDrawingObject is DrawableCircle ci && ci.Radius < minRadiusScaled) isValidObject = false;
                    else if (currentDrawingObject is DrawableStraightLine sl && Vector2.DistanceSquared(sl.StartPointRelative, sl.EndPointRelative) < minDistanceSqScaled) isValidObject = false;
                    else if (currentDrawingObject is DrawableRectangle r && Vector2.DistanceSquared(r.StartPointRelative, r.EndPointRelative) < minDistanceSqScaled) isValidObject = false;
                    else if (currentDrawingObject is DrawableArrow a && Vector2.DistanceSquared(a.StartPointRelative, a.EndPointRelative) < minDistanceSqScaled) isValidObject = false;
                    else if (currentDrawingObject is DrawableCone co && Vector2.DistanceSquared(co.ApexRelative, co.BaseCenterRelative) < minDistanceSqScaled) isValidObject = false;

                    if (isValidObject) DrawablesListOfCurrentPage.Add(currentDrawingObject);
                }
            }
            currentDrawingObject = null;
            isDrawingOnCanvas = false;
        }

        private void CopySelected()
        {
            if (selectedDrawables.Any())
            {
                clipboard.Clear();
                foreach (var sel in selectedDrawables) clipboard.Add(sel.Clone());
                Plugin.Log.Information($"Copied {clipboard.Count} items.");
            }
        }
        private void PasteCopied()
        {
            if (clipboard.Any())
            {
                Vector2 pasteOffset = new Vector2(15 * ImGuiHelpers.GlobalScale, 15 * ImGuiHelpers.GlobalScale);
                foreach (var dsel in selectedDrawables) dsel.IsSelected = false;
                selectedDrawables.Clear();
                foreach (var item in clipboard)
                {
                    var newItemClone = item.Clone();
                    newItemClone.Translate(pasteOffset);
                    newItemClone.IsSelected = true;
                    DrawablesListOfCurrentPage.Add(newItemClone);
                    selectedDrawables.Add(newItemClone);
                }
                Plugin.Log.Information($"Pasted {selectedDrawables.Count} items.");
            }
        }
        private int GetLayerPriority(DrawMode mode)
        {
            if ((mode >= DrawMode.RoleTankImage && mode <= DrawMode.RoleRangedImage) || (mode >= DrawMode.Waymark1Image && mode <= DrawMode.WaymarkDImage)) return 4;
            if (mode >= DrawMode.BossImage && mode <= DrawMode.StackImage) return 3;
            if (mode == DrawMode.Pen || mode == DrawMode.StraightLine || mode == DrawMode.Rectangle || mode == DrawMode.Circle || mode == DrawMode.Arrow || mode == DrawMode.Cone || mode == DrawMode.Dash || mode == DrawMode.Donut) return 2;
            return 1;
        }

        private void DrawCanvas()
        {
            Vector2 canvasSize = ImGui.GetContentRegionAvail();
            float minCanvasDimension = 50f * ImGuiHelpers.GlobalScale;
            if (canvasSize.X < minCanvasDimension) canvasSize.X = minCanvasDimension;
            if (canvasSize.Y < minCanvasDimension) canvasSize.Y = minCanvasDimension;

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            Vector2 canvasOriginScreen = ImGui.GetCursorScreenPos();

            uint backgroundColor = ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.17f, 1.0f));
            drawList.AddRectFilled(canvasOriginScreen, canvasOriginScreen + canvasSize, backgroundColor);

            uint gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
            float scaledGridCellSize = ScaledCanvasGridSize;
            float scaledGridLineThickness = 1.0f * ImGuiHelpers.GlobalScale;
            if (scaledGridCellSize > 0)
            {
                for (float x = scaledGridCellSize; x < canvasSize.X; x += scaledGridCellSize) { drawList.AddLine(new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y), new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y + canvasSize.Y), gridColor, scaledGridLineThickness); }
                for (float y = scaledGridCellSize; y < canvasSize.Y; y += scaledGridCellSize) { drawList.AddLine(new Vector2(canvasOriginScreen.X, canvasOriginScreen.Y + y), new Vector2(canvasOriginScreen.X + canvasSize.X, canvasOriginScreen.Y + y), gridColor, scaledGridLineThickness); }
            }

            uint canvasOutlineColorU32 = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.45f, 1f));
            drawList.AddRect(canvasOriginScreen - new Vector2(1, 1) * ImGuiHelpers.GlobalScale, canvasOriginScreen + canvasSize, canvasOutlineColorU32, 0f, ImDrawFlags.None, 1.0f * ImGuiHelpers.GlobalScale);

            ImGui.SetCursorScreenPos(canvasOriginScreen);
            ImGui.InvisibleButton("##AetherDrawCanvasInteractionLayer", canvasSize);

            Vector2 mousePosScreen = ImGui.GetMousePos();
            Vector2 mousePosRelative = mousePosScreen - canvasOriginScreen;
            bool canvasInteractLayerHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.None);
            bool isLMBDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            bool isLMBClickedOnCanvas = canvasInteractLayerHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            bool isLMBReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

            var currentDrawables = DrawablesListOfCurrentPage;
            BaseDrawable? singleSelectedItem = selectedDrawables.Count == 1 ? selectedDrawables[0] : null;

            if (currentDrawMode == DrawMode.Select)
            {
                shapeInteractionHandler.ProcessInteractions(
                    singleSelectedItem, selectedDrawables, currentDrawables, ref hoveredDrawable,
                    mousePosRelative, mousePosScreen, canvasOriginScreen, canvasInteractLayerHovered,
                    isLMBClickedOnCanvas, isLMBDown, isLMBReleased, drawList, ref lastMouseDragPosRelative
                );
            }
            else if (currentDrawMode == DrawMode.Eraser)
            {
                if (canvasInteractLayerHovered) drawList.AddCircle(mousePosScreen, ScaledEraserVisualRadius, ImGui.GetColorU32(new Vector4(1, 0, 0, 0.7f)), 32, 1.0f * ImGuiHelpers.GlobalScale);
                if (canvasInteractLayerHovered && isLMBDown)
                {
                    for (int i = currentDrawables.Count - 1; i >= 0; i--)
                    {
                        var d = currentDrawables[i]; bool removedByPathEdit = false;
                        if (d is DrawablePath path)
                        {
                            int originalCount = path.PointsRelative.Count;
                            path.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosRelative) < ScaledEraserLogicRadius);
                            if (path.PointsRelative.Count < 2 && originalCount >= 2) { currentDrawables.RemoveAt(i); removedByPathEdit = true; }
                        }
                        else if (d is DrawableDash dashPath)
                        {
                            int originalCount = dashPath.PointsRelative.Count;
                            dashPath.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosRelative) < ScaledEraserLogicRadius);
                            if (dashPath.PointsRelative.Count < 2 && originalCount >= 2) { currentDrawables.RemoveAt(i); removedByPathEdit = true; }
                        }

                        if (!removedByPathEdit && d.IsHit(mousePosRelative, ScaledEraserLogicRadius))
                        {
                            currentDrawables.RemoveAt(i);
                            if (selectedDrawables.Contains(d)) selectedDrawables.Remove(d);
                            if (hoveredDrawable != null && hoveredDrawable.Equals(d)) hoveredDrawable = null;
                        }
                    }
                }
            }
            else if ((currentDrawMode >= DrawMode.BossImage && currentDrawMode <= DrawMode.RoleRangedImage))
            {
                if (canvasInteractLayerHovered) ImGui.SetTooltip($"Click to place {currentDrawMode}");
                if (isLMBClickedOnCanvas)
                {
                    string imagePath = "";
                    Vector2 imageSize = new Vector2(ScaledImageDefaultSize, ScaledImageDefaultSize);
                    Vector4 tint = Vector4.One;
                    float scaledSize30 = 30f * ImGuiHelpers.GlobalScale;
                    float scaledSize60 = 60f * ImGuiHelpers.GlobalScale;

                    switch (currentDrawMode)
                    {
                        case DrawMode.BossImage: imagePath = "PluginImages.svg.boss.svg"; imageSize = new Vector2(scaledSize60, scaledSize60); break;
                        case DrawMode.CircleAoEImage: imagePath = "PluginImages.svg.prox_aoe.svg"; break;
                        case DrawMode.DonutAoEImage: imagePath = "PluginImages.svg.donut.svg"; break;
                        case DrawMode.FlareImage: imagePath = "PluginImages.svg.flare.svg"; break;
                        case DrawMode.LineStackImage: imagePath = "PluginImages.svg.line_stack.svg"; break;
                        case DrawMode.SpreadImage: imagePath = "PluginImages.svg.spread.svg"; break;
                        case DrawMode.StackImage: imagePath = "PluginImages.svg.stack.svg"; break;
                        case DrawMode.Waymark1Image: imagePath = "PluginImages.toolbar.1_waymark.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.Waymark2Image: imagePath = "PluginImages.toolbar.2_waymark.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.Waymark3Image: imagePath = "PluginImages.toolbar.3_waymark.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.Waymark4Image: imagePath = "PluginImages.toolbar.4_waymark.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.WaymarkAImage: imagePath = "PluginImages.toolbar.A.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.WaymarkBImage: imagePath = "PluginImages.toolbar.B.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.WaymarkCImage: imagePath = "PluginImages.toolbar.C.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.WaymarkDImage: imagePath = "PluginImages.toolbar.D.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.RoleTankImage: imagePath = "PluginImages.toolbar.Tank.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.RoleHealerImage: imagePath = "PluginImages.toolbar.Healer.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.RoleMeleeImage: imagePath = "PluginImages.toolbar.Melee.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                        case DrawMode.RoleRangedImage: imagePath = "PluginImages.toolbar.Ranged.JPG"; imageSize = new Vector2(scaledSize30, scaledSize30); break;
                    }
                    if (!string.IsNullOrEmpty(imagePath)) DrawablesListOfCurrentPage.Add(new DrawableImage(currentDrawMode, imagePath, mousePosRelative, imageSize, tint));
                }
            }
            else
            {
                float scaledBrushThicknessForDrawing = currentBrushThickness * ImGuiHelpers.GlobalScale;
                if (canvasInteractLayerHovered && isLMBDown)
                {
                    if (!isDrawingOnCanvas)
                    {
                        isDrawingOnCanvas = true;
                        foreach (var sel in selectedDrawables) sel.IsSelected = false;
                        selectedDrawables.Clear();
                        if (hoveredDrawable != null) hoveredDrawable.IsHovered = false;
                        hoveredDrawable = null;

                        switch (currentDrawMode)
                        {
                            case DrawMode.Pen: currentDrawingObject = new DrawablePath(mousePosRelative, currentBrushColor, scaledBrushThicknessForDrawing); break;
                            case DrawMode.StraightLine: currentDrawingObject = new DrawableStraightLine(mousePosRelative, currentBrushColor, scaledBrushThicknessForDrawing); break;
                            case DrawMode.Dash: currentDrawingObject = new DrawableDash(mousePosRelative, currentBrushColor, scaledBrushThicknessForDrawing); break;
                            case DrawMode.Rectangle: currentDrawingObject = new DrawableRectangle(mousePosRelative, currentBrushColor, scaledBrushThicknessForDrawing, currentShapeFilled); break;
                            case DrawMode.Circle: currentDrawingObject = new DrawableCircle(mousePosRelative, currentBrushColor, scaledBrushThicknessForDrawing, currentShapeFilled); break;
                            case DrawMode.Arrow: currentDrawingObject = new DrawableArrow(mousePosRelative, currentBrushColor, scaledBrushThicknessForDrawing); break;
                            case DrawMode.Cone: currentDrawingObject = new DrawableCone(mousePosRelative, currentBrushColor, scaledBrushThicknessForDrawing, currentShapeFilled); break;
                        }
                    }
                    if (currentDrawingObject != null)
                    {
                        if (currentDrawingObject is DrawablePath p) p.AddPoint(mousePosRelative);
                        else if (currentDrawingObject is DrawableDash d) d.AddPoint(mousePosRelative);
                        else currentDrawingObject.UpdatePreview(mousePosRelative);
                    }
                }
                if (isDrawingOnCanvas && isLMBReleased)
                {
                    FinalizeCurrentDrawing();
                }
            }

            bool clippingPushed = false;
            try
            {
                ImGui.PushClipRect(canvasOriginScreen, canvasOriginScreen + canvasSize, true);
                clippingPushed = true;

                if (currentDrawables != null && currentDrawables.Any())
                {
                    var sortedDrawables = currentDrawables.OrderBy(d => GetLayerPriority(d.ObjectDrawMode)).ToList();
                    foreach (var drawable in sortedDrawables)
                    {
                        drawable.Draw(drawList, canvasOriginScreen);
                    }
                }
                currentDrawingObject?.Draw(drawList, canvasOriginScreen);
            }
            finally
            {
                if (clippingPushed) ImGui.PopClipRect();
            }
        }
    }
}
