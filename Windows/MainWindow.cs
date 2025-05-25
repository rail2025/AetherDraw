using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Linq;
using AetherDraw.DrawingLogic;

namespace AetherDraw.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private class PageData
        {
            public string Name { get; set; } = "1";
            public List<BaseDrawable> Drawables { get; set; } = new List<BaseDrawable>();
        }

        private readonly Plugin plugin;
        private readonly Configuration configuration;
        private readonly ShapeInteractionHandler shapeInteractionHandler;

        private List<PageData> pages = new List<PageData>();
        private int currentPageIndex = 0;

        private static readonly List<BaseDrawable> EmptyDrawablesFallback = new List<BaseDrawable>();
        private List<BaseDrawable> DrawablesListOfCurrentPage =>
            (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            ? pages[currentPageIndex].Drawables
            : EmptyDrawablesFallback;

        private BaseDrawable? currentDrawingObject = null;
        private BaseDrawable? hoveredDrawable = null;
        private List<BaseDrawable> selectedDrawables = new List<BaseDrawable>();
        private List<BaseDrawable> clipboard = new List<BaseDrawable>();

        private bool isDrawingOnCanvas = false;
        private readonly float eraserVisualRadius = 5f;
        private float eraserLogicRadius = 10f;

        private Vector2 lastMouseDragPosRelative = Vector2.Zero;

        private float imageDefaultSize = 32f;

        private DrawMode currentDrawMode = DrawMode.Pen;
        private Vector4 currentBrushColor;
        private float currentBrushThickness;
        private bool currentShapeFilled = false;

        private static readonly float[] ThicknessPresets = new float[] { 1.5f, 4.0f, 7.0f, 10.0f };
        private static readonly Vector4[] ColorPalette = new Vector4[] {
            new Vector4(1.0f,1.0f,1.0f,1.0f),new Vector4(0.0f,0.0f,0.0f,1.0f),new Vector4(1.0f,0.0f,0.0f,1.0f),new Vector4(0.0f,1.0f,0.0f,1.0f),
            new Vector4(0.0f,0.0f,1.0f,1.0f),new Vector4(1.0f,1.0f,0.0f,1.0f),new Vector4(1.0f,0.0f,1.0f,1.0f),new Vector4(0.0f,1.0f,1.0f,1.0f),
            new Vector4(0.5f,0.5f,0.5f,1.0f),new Vector4(0.8f,0.4f,0.0f,1.0f),new Vector4(0.5f,0.0f,0.5f,1.0f),new Vector4(0.0f,0.5f,0.0f,1.0f),
            new Vector4(0.0f,0.0f,0.5f,1.0f),new Vector4(0.5f,0.0f,0.0f,1.0f),new Vector4(0.75f,0.75f,0.6f,1.0f),new Vector4(0.6f,0.8f,1.0f,1.0f)
        };

        private const float CanvasGridSize = 40f;
        private const float TabBarHeight = 40f;

        public MainWindow(Plugin plugin) : base("AetherDraw Whiteboard###AetherDrawMainWindow")
        {
            this.plugin = plugin;
            this.configuration = plugin.Configuration;
            this.shapeInteractionHandler = new ShapeInteractionHandler();

            float targetWidth = 850f * 0.75f;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(targetWidth, 600f),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
            this.RespectCloseHotkey = true;
            this.currentBrushColor = new Vector4(this.configuration.DefaultBrushColorR, this.configuration.DefaultBrushColorG, this.configuration.DefaultBrushColorB, this.configuration.DefaultBrushColorA);
            this.currentBrushThickness = ThicknessPresets.Contains(this.configuration.DefaultBrushThickness) ? this.configuration.DefaultBrushThickness : ThicknessPresets[1];

            if (pages.Count == 0) { AddNewPage(switchToPage: false); }
            currentPageIndex = 0;
        }

        public void Dispose() { }

        public override void PreDraw() { Flags = configuration.IsMainWindowMovable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoMove; }

        public override void Draw()
        {
            float toolbarWidth = 250f;
            ImGui.BeginChild("ToolbarRegion", new Vector2(toolbarWidth, 0), true, ImGuiWindowFlags.None);
            DrawToolbarControls();
            ImGui.EndChild();

            ImGui.SameLine();
            ImGui.BeginChild("RightPane", Vector2.Zero, false, ImGuiWindowFlags.None);
            float canvasAvailableHeight = ImGui.GetContentRegionAvail().Y - TabBarHeight - ImGui.GetStyle().ItemSpacing.Y;
            if (canvasAvailableHeight < 50f) canvasAvailableHeight = 50f;
            ImGui.BeginChild("CanvasDrawingArea", new Vector2(0, canvasAvailableHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            DrawCanvas();
            ImGui.EndChild();
            DrawTabBar();
            ImGui.EndChild();
        }

        private void DrawToolbarControls()
        {
            Vector4 activeToolColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float btnWidthQuarter = (availableWidth - ImGui.GetStyle().ItemSpacing.X * 3) / 4f;
            btnWidthQuarter = Math.Max(btnWidthQuarter, 10f);

            void ToolButton(string label, DrawMode mode, float buttonWidth)
            {
                bool isSelected = currentDrawMode == mode;
                if (isSelected) ImGui.PushStyleColor(ImGuiCol.Button, activeToolColor);
                if (ImGui.Button($"{label}##ToolBtn_{mode}", new Vector2(buttonWidth, 0)))
                {
                    currentDrawMode = mode;
                    FinalizeCurrentDrawing();
                    if (mode != DrawMode.Select) shapeInteractionHandler.ResetDragState();
                }
                if (isSelected) ImGui.PopStyleColor();
            }

            void PlacedImageToolButton(string label, DrawMode mode, string imageResourcePath, float buttonWidth)
            {
                bool isSelected = currentDrawMode == mode;
                if (isSelected) ImGui.PushStyleColor(ImGuiCol.Button, activeToolColor);
                var tex = TextureManager.GetTexture(imageResourcePath);
                float textLineHeight = ImGui.GetTextLineHeight();
                float imageDisplaySize = Math.Min(buttonWidth * 0.7f, textLineHeight * 1.5f); imageDisplaySize = Math.Max(imageDisplaySize, textLineHeight);
                Vector2 actualImageButtonSize = new Vector2(imageDisplaySize, imageDisplaySize);
                string uniqueId = $"##ImgBtn_{mode}_{label.Replace(" ", "_")}";
                ImGui.PushID(uniqueId);
                if (ImGui.Button(label.Length > 3 && tex == null ? label.Substring(0, 3) : $"##{label}_container_{mode}", new Vector2(buttonWidth, 0)))
                {
                    currentDrawMode = mode;
                    FinalizeCurrentDrawing();
                    if (mode != DrawMode.Select) shapeInteractionHandler.ResetDragState();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(label);
                if (tex != null && tex.ImGuiHandle != IntPtr.Zero)
                {
                    Vector2 itemMin = ImGui.GetItemRectMin(); Vector2 itemMax = ImGui.GetItemRectMax(); Vector2 itemCenter = (itemMin + itemMax) / 2f;
                    ImGui.GetWindowDrawList().AddImage(tex.ImGuiHandle, itemCenter - actualImageButtonSize / 2f, itemCenter + actualImageButtonSize / 2f);
                }
                else if (tex == null)
                {
                    Vector2 itemMin = ImGui.GetItemRectMin(); var textSize = ImGui.CalcTextSize(label.Substring(0, Math.Min(label.Length, 3)));
                    ImGui.GetWindowDrawList().AddText(itemMin + (new Vector2(buttonWidth, ImGui.GetFrameHeight()) - textSize) / 2f, ImGui.GetColorU32(ImGuiCol.Text), label.Substring(0, Math.Min(label.Length, 3)));
                }
                ImGui.PopID();
                if (isSelected) ImGui.PopStyleColor();
            }

            ToolButton("Select", DrawMode.Select, btnWidthQuarter); ImGui.SameLine();
            ToolButton("Eraser", DrawMode.Eraser, btnWidthQuarter); ImGui.SameLine();
            if (ImGui.Button("Copy", new Vector2(btnWidthQuarter, 0))) CopySelected(); ImGui.SameLine();
            if (ImGui.Button("Paste", new Vector2(btnWidthQuarter, 0))) PasteCopied();

            ImGui.Checkbox("Fill Shape", ref currentShapeFilled); ImGui.SameLine();
            // Make "Clear All" button width dynamic to fill remaining space if Checkbox is short
            float checkboxWidth = ImGui.GetItemRectMax().X - ImGui.GetItemRectMin().X;
            float clearAllButtonWidth = Math.Max(availableWidth - checkboxWidth - ImGui.GetStyle().ItemSpacing.X * 2, btnWidthQuarter); // Ensure min width

            if (ImGui.Button("Clear All", new Vector2(clearAllButtonWidth, 0)))
            {
                if (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
                {
                    DrawablesListOfCurrentPage.Clear();
                }
                currentDrawingObject = null; isDrawingOnCanvas = false; selectedDrawables.Clear(); hoveredDrawable = null;
                shapeInteractionHandler.ResetDragState();
                Plugin.Log.Information($"Page {(pages.Count > 0 && currentPageIndex < pages.Count ? pages[currentPageIndex].Name : "N/A")} cleared.");
            }
            ImGui.Separator();

            ToolButton("Pen", DrawMode.Pen, btnWidthQuarter); ImGui.SameLine();
            ToolButton("Line", DrawMode.StraightLine, btnWidthQuarter); ImGui.SameLine();
            ToolButton("Dash", DrawMode.Dash, btnWidthQuarter); ImGui.SameLine();
            ToolButton("Rect", DrawMode.Rectangle, btnWidthQuarter);

            ToolButton("Circle", DrawMode.Circle, btnWidthQuarter); ImGui.SameLine();
            ToolButton("Arrow", DrawMode.Arrow, btnWidthQuarter); ImGui.SameLine();
            ToolButton("Cone", DrawMode.Cone, btnWidthQuarter);
            ImGui.Separator();

            PlacedImageToolButton("Tank", DrawMode.RoleTankImage, "PluginImages.toolbar.Tank.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("Healer", DrawMode.RoleHealerImage, "PluginImages.toolbar.Healer.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("Melee", DrawMode.RoleMeleeImage, "PluginImages.toolbar.Melee.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("Ranged", DrawMode.RoleRangedImage, "PluginImages.toolbar.Ranged.JPG", btnWidthQuarter);
            ImGui.Separator();

            PlacedImageToolButton("WM1", DrawMode.Waymark1Image, "PluginImages.toolbar.1_waymark.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("WM2", DrawMode.Waymark2Image, "PluginImages.toolbar.2_waymark.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("WM3", DrawMode.Waymark3Image, "PluginImages.toolbar.3_waymark.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("WM4", DrawMode.Waymark4Image, "PluginImages.toolbar.4_waymark.JPG", btnWidthQuarter);
            PlacedImageToolButton("WMA", DrawMode.WaymarkAImage, "PluginImages.toolbar.A.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("WMB", DrawMode.WaymarkBImage, "PluginImages.toolbar.B.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("WMC", DrawMode.WaymarkCImage, "PluginImages.toolbar.C.JPG", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("WMD", DrawMode.WaymarkDImage, "PluginImages.toolbar.D.JPG", btnWidthQuarter);
            ImGui.Separator();

            PlacedImageToolButton("Boss", DrawMode.BossImage, "PluginImages.svg.boss.svg", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("Circle", DrawMode.CircleAoEImage, "PluginImages.svg.prox_aoe.svg", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("Donut", DrawMode.DonutAoEImage, "PluginImages.svg.donut.svg", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("Flare", DrawMode.FlareImage, "PluginImages.svg.flare.svg", btnWidthQuarter);
            PlacedImageToolButton("L.Stack", DrawMode.LineStackImage, "PluginImages.svg.line_stack.svg", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("Spread", DrawMode.SpreadImage, "PluginImages.svg.spread.svg", btnWidthQuarter); ImGui.SameLine();
            PlacedImageToolButton("Stack", DrawMode.StackImage, "PluginImages.svg.stack.svg", btnWidthQuarter);
            ImGui.Separator();

            ImGui.Text("Thickness:");
            for (int i = 0; i < ThicknessPresets.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                float t = ThicknessPresets[i]; bool iS = Math.Abs(currentBrushThickness - t) < 0.01f;
                if (iS) ImGui.PushStyleColor(ImGuiCol.Button, activeToolColor);
                float thicknessBtnWidth = (availableWidth - ImGui.GetStyle().ItemSpacing.X * 3) / 4f; // Assume 4 buttons per row
                if (ImGui.Button($"{t:0.0}##th{t}", new Vector2(Math.Max(thicknessBtnWidth - 2, 10f), 0))) currentBrushThickness = t;
                if (iS) ImGui.PopStyleColor();
            }
            ImGui.NewLine();
            ImGui.Separator();

            int cpr = 4; float pbs = (availableWidth - ImGui.GetStyle().ItemSpacing.X * (cpr - 1)) / cpr; pbs = Math.Max(pbs, ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2); Vector2 cbs = new Vector2(pbs, pbs); Vector4 soc = new Vector4(0.1f, 0.1f, 0.1f, 0.9f); float sot = 2.0f;
            for (int i = 0; i < ColorPalette.Length; i++)
            {
                bool iSC = (ColorPalette[i].X == currentBrushColor.X && ColorPalette[i].Y == currentBrushColor.Y && ColorPalette[i].Z == currentBrushColor.Z && ColorPalette[i].W == currentBrushColor.W);
                if (ImGui.ColorButton($"##cb{i}", ColorPalette[i], ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, cbs)) { currentBrushColor = ColorPalette[i]; }
                if (iSC) { ImDrawListPtr fdl = ImGui.GetForegroundDrawList(); fdl.AddRect(ImGui.GetItemRectMin() - new Vector2(1, 1), ImGui.GetItemRectMax() + new Vector2(1, 1), ImGui.GetColorU32(soc), 0f, ImDrawFlags.None, sot); }
                if ((i + 1) % cpr != 0 && i < ColorPalette.Length - 1) ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X);
            }
        }

        private void DrawTabBar()
        {
            ImGui.BeginChild("TabBarRegion", new Vector2(0, TabBarHeight), true, ImGuiWindowFlags.None);
            float buttonHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetStyle().WindowPadding.Y * 0.5f;
            buttonHeight = Math.Max(buttonHeight, ImGui.GetFrameHeight());

            Vector4 activeButtonStyleColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            Vector4 selectedTabColorVec = new Vector4(activeButtonStyleColor.X * 0.8f, activeButtonStyleColor.Y * 0.8f, activeButtonStyleColor.Z * 1.2f, activeButtonStyleColor.W);
            selectedTabColorVec.X = Math.Clamp(selectedTabColorVec.X, 0.0f, 1.0f); selectedTabColorVec.Y = Math.Clamp(selectedTabColorVec.Y, 0.0f, 1.0f); selectedTabColorVec.Z = Math.Clamp(selectedTabColorVec.Z, 0.0f, 1.0f);

            for (int i = 0; i < pages.Count; i++)
            {
                bool isSelectedPage = (i == currentPageIndex); string pageName = pages[i].Name;
                float buttonTextWidth = ImGui.CalcTextSize(pageName).X;
                float pageButtonWidth = buttonTextWidth + ImGui.GetStyle().FramePadding.X * 2.0f + 10f;
                pageButtonWidth = Math.Max(pageButtonWidth, buttonHeight * 0.8f);
                if (isSelectedPage) ImGui.PushStyleColor(ImGuiCol.Button, selectedTabColorVec);
                if (ImGui.Button(pageName, new Vector2(pageButtonWidth, buttonHeight))) { if (!isSelectedPage) SwitchToPage(i); }
                if (isSelectedPage) ImGui.PopStyleColor();
                ImGui.SameLine(0, 3f);
            }
            float plusButtonWidth = buttonHeight;
            if (ImGui.Button("+", new Vector2(plusButtonWidth, buttonHeight))) AddNewPage();
            if (pages.Count > 1)
            {
                ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f)); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f)); ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
                if (ImGui.Button("X", new Vector2(plusButtonWidth, buttonHeight))) DeleteCurrentPage();
                ImGui.PopStyleColor(3);
            }
            ImGui.EndChild();
        }

        private void AddNewPage(bool switchToPage = true)
        {
            FinalizeCurrentDrawing();
            int npn = 1;
            if (pages.Any())
            {
                npn = pages.Select(p => int.TryParse(p.Name, out int num) ? num : 0).DefaultIfEmpty(0).Max() + 1;
            }
            var newPage = new PageData { Name = npn.ToString() };

            // Preload default waymarks
            float refCanvasWidth = 387.5f;
            float refCanvasHeight = 550f;
            Vector2 canvasCenter = new Vector2(refCanvasWidth / 2f, refCanvasHeight / 2f);
            float waymarkPlacementRadius = Math.Min(refCanvasWidth, refCanvasHeight) * 0.40f;
            Vector2 waymarkImageSize = new Vector2(30, 30);
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
            int ptrI = currentPageIndex;
            pages.RemoveAt(ptrI);
            currentPageIndex = Math.Max(0, Math.Min(ptrI, pages.Count - 1));
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
                    if (currentDrawingObject is DrawablePath p && p.PointsRelative.Count < 2) isValidObject = false;
                    else if (currentDrawingObject is DrawableDash d && d.PointsRelative.Count < 2) isValidObject = false;
                    else if (currentDrawingObject is DrawableCircle ci && ci.Radius < 2f) isValidObject = false;
                    else if (currentDrawingObject is DrawableStraightLine sl && Vector2.DistanceSquared(sl.StartPointRelative, sl.EndPointRelative) < 4f) isValidObject = false;
                    else if (currentDrawingObject is DrawableRectangle r && Vector2.DistanceSquared(r.StartPointRelative, r.EndPointRelative) < 4f) isValidObject = false;
                    else if (currentDrawingObject is DrawableArrow a && Vector2.DistanceSquared(a.StartPointRelative, a.EndPointRelative) < 4f) isValidObject = false;
                    else if (currentDrawingObject is DrawableCone co && Vector2.DistanceSquared(co.ApexRelative, co.BaseCenterRelative) < 4f) isValidObject = false;

                    if (isValidObject) DrawablesListOfCurrentPage.Add(currentDrawingObject);
                }
            }
            currentDrawingObject = null;
            isDrawingOnCanvas = false;
        }
        private void CopySelected() { if (selectedDrawables.Any()) { clipboard.Clear(); foreach (var sel in selectedDrawables) clipboard.Add(sel.Clone()); Plugin.Log.Information($"Copied {clipboard.Count} items."); } }
        private void PasteCopied() { if (clipboard.Any()) { Vector2 po = new Vector2(15, 15); foreach (var dsel in selectedDrawables) dsel.IsSelected = false; selectedDrawables.Clear(); foreach (var item in clipboard) { var nd = item.Clone(); nd.Translate(po); nd.IsSelected = true; DrawablesListOfCurrentPage.Add(nd); selectedDrawables.Add(nd); } Plugin.Log.Information($"Pasted {selectedDrawables.Count} items."); } }
        private int GetLayerPriority(DrawMode mode) { if ((mode >= DrawMode.RoleTankImage && mode <= DrawMode.RoleRangedImage) || (mode >= DrawMode.Waymark1Image && mode <= DrawMode.WaymarkDImage)) return 4; if (mode >= DrawMode.BossImage && mode <= DrawMode.StackImage) return 3; if (mode == DrawMode.Pen || mode == DrawMode.StraightLine || mode == DrawMode.Rectangle || mode == DrawMode.Circle || mode == DrawMode.Arrow || mode == DrawMode.Cone || mode == DrawMode.Dash || mode == DrawMode.Donut) return 2; return 1; }

        private void DrawCanvas()
        {
            Vector2 canvasSize = ImGui.GetContentRegionAvail();
            if (canvasSize.X < 50) canvasSize.X = 50; if (canvasSize.Y < 50) canvasSize.Y = 50;
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            Vector2 canvasOriginScreen = ImGui.GetCursorScreenPos();

            uint backgroundColor = ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.17f, 1.0f));
            drawList.AddRectFilled(canvasOriginScreen, canvasOriginScreen + canvasSize, backgroundColor);
            uint gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
            for (float x = CanvasGridSize; x < canvasSize.X; x += CanvasGridSize) { drawList.AddLine(new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y), new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y + canvasSize.Y), gridColor, 1.0f); }
            for (float y = CanvasGridSize; y < canvasSize.Y; y += CanvasGridSize) { drawList.AddLine(new Vector2(canvasOriginScreen.X, canvasOriginScreen.Y + y), new Vector2(canvasOriginScreen.X + canvasSize.X, canvasOriginScreen.Y + y), gridColor, 1.0f); }
            uint canvasOutlineColor = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.45f, 1f));
            drawList.AddRect(canvasOriginScreen - new Vector2(1, 1), canvasOriginScreen + canvasSize + new Vector2(0, 0), canvasOutlineColor, 0f, ImDrawFlags.None, 1.0f);

            ImGui.SetCursorScreenPos(canvasOriginScreen);
            ImGui.InvisibleButton("##canvasInteractLayer", canvasSize);
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
                    singleSelectedItem,
                    selectedDrawables,
                    currentDrawables,
                    ref hoveredDrawable,
                    mousePosRelative,
                    mousePosScreen,
                    canvasOriginScreen,
                    canvasInteractLayerHovered,
                    isLMBClickedOnCanvas,
                    isLMBDown,
                    isLMBReleased,
                    drawList,
                    ref lastMouseDragPosRelative
                );
            }
            else if (currentDrawMode == DrawMode.Eraser)
            {
                if (canvasInteractLayerHovered) drawList.AddCircle(mousePosScreen, eraserVisualRadius, ImGui.GetColorU32(new Vector4(1, 0, 0, 0.7f)), 32, 1.0f);
                if (canvasInteractLayerHovered && isLMBDown)
                {
                    for (int i = currentDrawables.Count - 1; i >= 0; i--)
                    {
                        var d = currentDrawables[i]; bool rem = false;
                        if (d is DrawablePath p) { int oc = p.PointsRelative.Count; p.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosRelative) < eraserLogicRadius); if (p.PointsRelative.Count < 2 && oc >= 2) { currentDrawables.RemoveAt(i); rem = true; } }
                        else if (d is DrawableDash ds) { int oc = ds.PointsRelative.Count; ds.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosRelative) < eraserLogicRadius); if (ds.PointsRelative.Count < 2 && oc >= 2) { currentDrawables.RemoveAt(i); rem = true; } }
                        if (!rem && d.IsHit(mousePosRelative, eraserLogicRadius))
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
                    string ip = ""; Vector2 isz = new Vector2(imageDefaultSize, imageDefaultSize); Vector4 t = Vector4.One;
                    // Switch statement for image paths and sizes
                    switch (currentDrawMode)
                    {
                        case DrawMode.BossImage: ip = "PluginImages.svg.boss.svg"; isz = new Vector2(60, 60); break;
                        case DrawMode.CircleAoEImage: ip = "PluginImages.svg.prox_aoe.svg"; break;
                        case DrawMode.DonutAoEImage: ip = "PluginImages.svg.donut.svg"; break;
                        case DrawMode.FlareImage: ip = "PluginImages.svg.flare.svg"; break;
                        case DrawMode.LineStackImage: ip = "PluginImages.svg.line_stack.svg"; break;
                        case DrawMode.SpreadImage: ip = "PluginImages.svg.spread.svg"; break;
                        case DrawMode.StackImage: ip = "PluginImages.svg.stack.svg"; break;
                        case DrawMode.Waymark1Image: ip = "PluginImages.toolbar.1_waymark.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.Waymark2Image: ip = "PluginImages.toolbar.2_waymark.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.Waymark3Image: ip = "PluginImages.toolbar.3_waymark.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.Waymark4Image: ip = "PluginImages.toolbar.4_waymark.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.WaymarkAImage: ip = "PluginImages.toolbar.A.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.WaymarkBImage: ip = "PluginImages.toolbar.B.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.WaymarkCImage: ip = "PluginImages.toolbar.C.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.WaymarkDImage: ip = "PluginImages.toolbar.D.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.RoleTankImage: ip = "PluginImages.toolbar.Tank.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.RoleHealerImage: ip = "PluginImages.toolbar.Healer.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.RoleMeleeImage: ip = "PluginImages.toolbar.Melee.JPG"; isz = new Vector2(30, 30); break;
                        case DrawMode.RoleRangedImage: ip = "PluginImages.toolbar.Ranged.JPG"; isz = new Vector2(30, 30); break;
                    }
                    if (!string.IsNullOrEmpty(ip)) DrawablesListOfCurrentPage.Add(new DrawableImage(currentDrawMode, ip, mousePosRelative, isz, t));
                }
            }
            else // Drawing Modes (Pen, Line, Rect, Circle, Arrow, Cone, Dash)
            {
                if (canvasInteractLayerHovered && isLMBDown)
                {
                    if (!isDrawingOnCanvas)
                    {
                        isDrawingOnCanvas = true;
                        foreach (var d_sel in selectedDrawables) d_sel.IsSelected = false; selectedDrawables.Clear();
                        if (hoveredDrawable != null) hoveredDrawable.IsHovered = false;
                        hoveredDrawable = null;

                        switch (currentDrawMode)
                        {
                            case DrawMode.Pen: currentDrawingObject = new DrawablePath(mousePosRelative, currentBrushColor, currentBrushThickness); break;
                            case DrawMode.StraightLine: currentDrawingObject = new DrawableStraightLine(mousePosRelative, currentBrushColor, currentBrushThickness); break;
                            case DrawMode.Dash: currentDrawingObject = new DrawableDash(mousePosRelative, currentBrushColor, currentBrushThickness); break;
                            case DrawMode.Rectangle: currentDrawingObject = new DrawableRectangle(mousePosRelative, currentBrushColor, currentBrushThickness, currentShapeFilled); break;
                            case DrawMode.Circle: currentDrawingObject = new DrawableCircle(mousePosRelative, currentBrushColor, currentBrushThickness, currentShapeFilled); break;
                            case DrawMode.Arrow: currentDrawingObject = new DrawableArrow(mousePosRelative, currentBrushColor, currentBrushThickness); break;
                            case DrawMode.Cone: currentDrawingObject = new DrawableCone(mousePosRelative, currentBrushColor, currentBrushThickness, currentShapeFilled); break;
                        }
                    }
                    if (currentDrawingObject != null) { if (currentDrawingObject is DrawablePath p) p.AddPoint(mousePosRelative); else if (currentDrawingObject is DrawableDash d) d.AddPoint(mousePosRelative); else currentDrawingObject.UpdatePreview(mousePosRelative); }
                }
                if (isDrawingOnCanvas && isLMBReleased)
                {
                    FinalizeCurrentDrawing();
                }
            }

            // Draw all elements on the canvas
            if (currentDrawables != null)
            {
                var sortedDrawables = currentDrawables.OrderBy(d => GetLayerPriority(d.ObjectDrawMode)).ToList();
                drawList.PushClipRect(canvasOriginScreen, canvasOriginScreen + canvasSize, true);
                foreach (var drawable in sortedDrawables)
                {
                    drawable.Draw(drawList, canvasOriginScreen);
                }
                currentDrawingObject?.Draw(drawList, canvasOriginScreen);
                drawList.PopClipRect();
            }
        }
    }
}
