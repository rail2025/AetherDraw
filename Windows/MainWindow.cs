using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System.Linq;
using AetherDraw.DrawingLogic;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

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
        private readonly InPlaceTextEditor inPlaceTextEditor;

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

        private float ScaledEraserVisualRadius => 5f * ImGuiHelpers.GlobalScale;
        private float ScaledEraserLogicRadius => 10f * ImGuiHelpers.GlobalScale; // This is a SCALED value, convert to logical for use

        private Vector2 lastMouseDragPosLogical = Vector2.Zero; // Stores LOGICAL mouse position for general drag

        private float DefaultUnscaledFontSize => 16f;
        private float DefaultUnscaledTextWrapWidth => 200f;
        // ScaledImageDefaultSize from ab6ebef... was 32f * ImGuiHelpers.GlobalScale.
        // It's better to define default image size as logical/unscaled.
        private Vector2 DefaultUnscaledImageSize => new Vector2(30f, 30f);


        private DrawMode currentDrawMode = DrawMode.Pen;
        private Vector4 currentBrushColor;
        private float currentBrushThickness; // This is unscaled
        private bool currentShapeFilled = false;

        private static readonly float[] ThicknessPresets = new float[] { 1.5f, 4f, 7f, 10f }; // Unscaled
        private static readonly Vector4[] ColorPalette = new Vector4[] {
            new Vector4(1.0f,1.0f,1.0f,1.0f), new Vector4(0.0f,0.0f,0.0f,1.0f),
            new Vector4(1.0f,0.0f,0.0f,1.0f), new Vector4(0.0f,1.0f,0.0f,1.0f),
            new Vector4(0.0f,0.0f,1.0f,1.0f), new Vector4(1.0f,1.0f,0.0f,1.0f),
            new Vector4(1.0f,0.0f,1.0f,1.0f), new Vector4(0.0f,1.0f,1.0f,1.0f),
            new Vector4(0.5f,0.5f,0.5f,1.0f), new Vector4(0.8f,0.4f,0.0f,1.0f)
        };

        private float ScaledCanvasGridSize => 40f * ImGuiHelpers.GlobalScale;

        public MainWindow(Plugin plugin) : base("AetherDraw Whiteboard###AetherDrawMainWindow")
        {
            this.plugin = plugin;
            this.configuration = plugin.Configuration;
            this.shapeInteractionHandler = new ShapeInteractionHandler();
            this.inPlaceTextEditor = new InPlaceTextEditor();

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

        public void Dispose() { }

        public override void PreDraw()
        {
            Flags = configuration.IsMainWindowMovable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoMove;
        }

        public override void Draw()
        {
            AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] Frame {ImGui.GetFrameCount()}: Entry. Window Size: {ImGui.GetWindowSize()}, Window Pos: {ImGui.GetWindowPos()}, GlobalScale: {ImGuiHelpers.GlobalScale}");

            float scaledToolbarWidth = 125f * ImGuiHelpers.GlobalScale;
            AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] scaledToolbarWidth: {scaledToolbarWidth}");
            AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] ContentRegionAvail before ToolbarRegion: {ImGui.GetContentRegionAvail()}");

            bool toolbarDrawnSuccessfully = false;
            using (var toolbarRaii = ImRaii.Child("ToolbarRegion", new Vector2(scaledToolbarWidth, 0), true, ImGuiWindowFlags.None))
            {
                if (toolbarRaii)
                {
                    toolbarDrawnSuccessfully = true;
                    AetherDraw.Plugin.Log?.Debug("[MainWindow.Draw] ToolbarRegion BeginChild succeeded. Drawing toolbar controls.");
                    DrawToolbarControls();
                }
                else
                {
                    toolbarDrawnSuccessfully = false;
                    AetherDraw.Plugin.Log?.Warning("[MainWindow.Draw] ToolbarRegion BeginChild FAILED.");
                }
            }
            AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] ToolbarRegion draw attempt was successful: {toolbarDrawnSuccessfully}");

            ImGui.SameLine();
            AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] ContentRegionAvail after SameLine (before RightPane): {ImGui.GetContentRegionAvail()}");

            bool rightPaneDrawnSuccessfully = false;
            using (var rightPaneRaii = ImRaii.Child("RightPane", Vector2.Zero, false, ImGuiWindowFlags.None))
            {
                if (rightPaneRaii)
                {
                    rightPaneDrawnSuccessfully = true;
                    AetherDraw.Plugin.Log?.Debug("[MainWindow.Draw] RightPane BeginChild succeeded.");

                    float bottomControlsBarHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().WindowPadding.Y * 2 + ImGui.GetStyle().ItemSpacing.Y;
                    AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] bottomControlsBarHeight: {bottomControlsBarHeight}");

                    float canvasAvailableHeight = ImGui.GetContentRegionAvail().Y - bottomControlsBarHeight - ImGui.GetStyle().ItemSpacing.Y;
                    AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] canvasAvailableHeight (initial): {canvasAvailableHeight}, ContentRegionAvail.Y for canvas: {ImGui.GetContentRegionAvail().Y}");

                    if (canvasAvailableHeight < 50f * ImGuiHelpers.GlobalScale)
                    {
                        AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] canvasAvailableHeight was < {50f * ImGuiHelpers.GlobalScale}, clamping to it.");
                        canvasAvailableHeight = 50f * ImGuiHelpers.GlobalScale;
                    }
                    AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] canvasAvailableHeight (final): {canvasAvailableHeight}");

                    AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] Attempting to BeginChild for CanvasDrawingArea with height {canvasAvailableHeight}");
                    if (ImGui.BeginChild("CanvasDrawingArea", new Vector2(0, canvasAvailableHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                    {
                        AetherDraw.Plugin.Log?.Debug("[MainWindow.Draw] CanvasDrawingArea BeginChild succeeded. Drawing canvas.");
                        DrawCanvas();
                        ImGui.EndChild(); // Correctly conditional
                    }
                    else
                    {
                        AetherDraw.Plugin.Log?.Warning("[MainWindow.Draw] CanvasDrawingArea BeginChild FAILED.");
                    }

                    AetherDraw.Plugin.Log?.Debug("[MainWindow.Draw] Drawing bottom controls bar.");
                    DrawBottomControlsBar(bottomControlsBarHeight);
                }
                else
                {
                    rightPaneDrawnSuccessfully = false;
                    AetherDraw.Plugin.Log?.Warning("[MainWindow.Draw] RightPane BeginChild FAILED.");
                }
            }
            AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] RightPane draw attempt was successful: {rightPaneDrawnSuccessfully}");
            AetherDraw.Plugin.Log?.Debug($"[MainWindow.Draw] Frame {ImGui.GetFrameCount()}: Exit.");
        }

        private void DrawToolbarControls()
        {
            Vector4 activeToolColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float itemSpacingX = ImGui.GetStyle().ItemSpacing.X;

            float btnWidthHalf = (availableWidth - itemSpacingX) / 2f; // For two buttons in a row
            btnWidthHalf = Math.Max(btnWidthHalf, 30f * ImGuiHelpers.GlobalScale);
            float btnWidthFull = availableWidth; // For one button in a row

            void ToolButton(string label, DrawMode mode, float buttonWidth)
            {
                bool isSelected = currentDrawMode == mode;
                using (isSelected ? ImRaii.PushColor(ImGuiCol.Button, activeToolColor) : null)
                {
                    if (ImGui.Button($"{label}##ToolBtn_{mode}", new Vector2(buttonWidth, 0)))
                    {
                        currentDrawMode = mode;
                        FinalizeCurrentDrawing();
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
                        FinalizeCurrentDrawing();
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

            if (ImGui.Button("Clear All", new Vector2(btnWidthFull, 0)))
            {
                if (DrawablesListOfCurrentPage.Any()) DrawablesListOfCurrentPage.Clear();
                currentDrawingObject = null; isDrawingOnCanvas = false; selectedDrawables.Clear(); hoveredDrawable = null;
                shapeInteractionHandler.ResetDragState();
                if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();
                AetherDraw.Plugin.Log.Information($"Page {(pages.Count > 0 && currentPageIndex < pages.Count ? pages[currentPageIndex].Name : "N/A")} cleared.");
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
            float thicknessButtonWidth = (availableWidth - itemSpacingX * (ThicknessPresets.Length - 1)) / ThicknessPresets.Length;
            thicknessButtonWidth = Math.Max(thicknessButtonWidth, 20f * ImGuiHelpers.GlobalScale);

            for (int i = 0; i < ThicknessPresets.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                float t = ThicknessPresets[i];
                bool isSelectedThickness = Math.Abs(currentBrushThickness - t) < 0.01f;

                using (isSelectedThickness ? ImRaii.PushColor(ImGuiCol.Button, activeToolColor) : null)
                {
                    if (ImGui.Button($"{t:0}##ThicknessBtn{i}", new Vector2(thicknessButtonWidth, 0))) currentBrushThickness = t;
                }
            }
            ImGui.Separator();

            int colorsPerRow = 5;
            float smallColorButtonBaseSize = (availableWidth - itemSpacingX * (colorsPerRow - 1)) / colorsPerRow;
            float smallColorButtonActualSize = Math.Max(smallColorButtonBaseSize, ImGui.GetTextLineHeight() * 0.8f);
            smallColorButtonActualSize = Math.Max(smallColorButtonActualSize, 16f * ImGuiHelpers.GlobalScale);
            Vector2 colorButtonDimensions = new Vector2(smallColorButtonActualSize, smallColorButtonActualSize);

            Vector4 selectedColorIndicatorColorVec = new Vector4(0.9f, 0.9f, 0.1f, 1.0f);
            uint selectedColorIndicatorU32 = ImGui.GetColorU32(selectedColorIndicatorColorVec);
            float selectedColorIndicatorThickness = 2.0f * ImGuiHelpers.GlobalScale; // Slightly thinner than original log version

            for (int i = 0; i < ColorPalette.Length; i++)
            {
                bool isSelectedColor = (ColorPalette[i].X == currentBrushColor.X &&
                                        ColorPalette[i].Y == currentBrushColor.Y &&
                                        ColorPalette[i].Z == currentBrushColor.Z &&
                                        ColorPalette[i].W == currentBrushColor.W);

                if (ImGui.ColorButton($"##ColorPaletteButton{i}", ColorPalette[i], ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, colorButtonDimensions))
                {
                    currentBrushColor = ColorPalette[i];
                }

                if (isSelectedColor)
                {
                    ImDrawListPtr foregroundDrawList = ImGui.GetForegroundDrawList();
                    Vector2 rectMin = ImGui.GetItemRectMin(); // Use exact item rect
                    Vector2 rectMax = ImGui.GetItemRectMax();
                    foregroundDrawList.AddRect(rectMin, rectMax, selectedColorIndicatorU32, 1f * ImGuiHelpers.GlobalScale, ImDrawFlags.None, selectedColorIndicatorThickness);
                }

                if ((i + 1) % colorsPerRow != 0 && i < ColorPalette.Length - 1)
                {
                    ImGui.SameLine(0, itemSpacingX / 2f);
                }
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
                    pageButtonWidth = Math.Max(pageButtonWidth, buttonHeight * 1.5f); // Ensure min width

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
                if (ImGui.Button("+##AddPage", new Vector2(plusButtonWidth, buttonHeight))) AddNewPage();

                if (pages.Count > 1)
                {
                    ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X);
                    using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f)))
                    using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 0.3f, 1.0f)))
                    using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)))
                    {
                        if (ImGui.Button("X##DeletePage", new Vector2(plusButtonWidth, buttonHeight))) DeleteCurrentPage();
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

            // Corrected: Use logical (unscaled) sizes for DrawableImage constructor
            float logicalRefCanvasWidth = (850f * 0.75f) - 125f;
            float logicalRefCanvasHeight = 550f;

            Vector2 canvasCenter = new Vector2(logicalRefCanvasWidth / 2f, logicalRefCanvasHeight / 2f);
            float waymarkPlacementRadius = Math.Min(logicalRefCanvasWidth, logicalRefCanvasHeight) * 0.40f;

            Vector2 waymarkImageUnscaledSize = new Vector2(30f, 30f); // LOGICAL size
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

                var drawableImage = new DrawableImage(wmInfo.Mode, wmInfo.Path, position, waymarkImageUnscaledSize, waymarkTint);
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
            selectedDrawables.Clear();
            hoveredDrawable = null;
            currentDrawingObject = null;
            isDrawingOnCanvas = false;
            shapeInteractionHandler.ResetDragState();
            if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();

            pages.RemoveAt(pageIndexToRemove);
            currentPageIndex = Math.Max(0, Math.Min(pageIndexToRemove, pages.Count - 1));
            SwitchToPage(currentPageIndex, true);
        }

        private void SwitchToPage(int newPageIndex, bool forceSwitch = false)
        {
            if (newPageIndex < 0 || newPageIndex >= pages.Count) return;
            if (!forceSwitch && newPageIndex == currentPageIndex && pages.Count > 0) return;

            FinalizeCurrentDrawing();
            currentPageIndex = newPageIndex;

            currentDrawingObject = null;
            hoveredDrawable = null;
            selectedDrawables.Clear();
            shapeInteractionHandler.ResetDragState();
            isDrawingOnCanvas = false;
            if (inPlaceTextEditor.IsEditing) inPlaceTextEditor.CancelAndEndEdit();
        }

        private void FinalizeCurrentDrawing()
        {
            if (currentDrawingObject == null || pages.Count == 0 || currentPageIndex < 0 || currentPageIndex >= pages.Count)
            {
                isDrawingOnCanvas = false;
                return;
            }

            if (isDrawingOnCanvas)
            {
                currentDrawingObject.IsPreview = false;
                bool isValidObject = true;
                float minDistanceSqUnscaled = (2f * 2f);
                float minRadiusUnscaled = 1.5f; // This is logical

                if (currentDrawingObject is DrawablePath p && p.PointsRelative.Count < 2) isValidObject = false;
                else if (currentDrawingObject is DrawableDash d && d.PointsRelative.Count < 2) isValidObject = false;
                else if (currentDrawingObject is DrawableCircle ci && ci.Radius < minRadiusUnscaled) isValidObject = false; // ci.Radius is logical
                else if (currentDrawingObject is DrawableStraightLine sl && Vector2.DistanceSquared(sl.StartPointRelative, sl.EndPointRelative) < minDistanceSqUnscaled) isValidObject = false;
                else if (currentDrawingObject is DrawableRectangle r && (Math.Abs(r.StartPointRelative.X - r.EndPointRelative.X) < 2f || Math.Abs(r.StartPointRelative.Y - r.EndPointRelative.Y) < 2f)) isValidObject = false;
                else if (currentDrawingObject is DrawableArrow arrow && Vector2.DistanceSquared(arrow.StartPointRelative, arrow.EndPointRelative) < minDistanceSqUnscaled && arrow.StartPointRelative != arrow.EndPointRelative) isValidObject = false;
                else if (currentDrawingObject is DrawableCone co && Vector2.DistanceSquared(co.ApexRelative, co.BaseCenterRelative) < minDistanceSqUnscaled) isValidObject = false;
                else if (currentDrawingObject is DrawableText txt && string.IsNullOrWhiteSpace(txt.RawText)) isValidObject = false;

                if (isValidObject)
                {
                    DrawablesListOfCurrentPage.Add(currentDrawingObject);
                }
                else
                {
                    AetherDraw.Plugin.Log?.Debug($"[MainWindow] Discarded invalid/too small drawing object: {currentDrawingObject.GetType().Name}.");
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
                foreach (var sel in selectedDrawables)
                {
                    clipboard.Add(sel.Clone());
                }
                AetherDraw.Plugin.Log?.Information($"Copied {clipboard.Count} items.");
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
                    DrawablesListOfCurrentPage.Add(newItemClone);
                    selectedDrawables.Add(newItemClone);
                }
                AetherDraw.Plugin.Log?.Information($"Pasted {selectedDrawables.Count} items.");
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
            float scaledGridLineThickness = Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale); // Ensure at least 1px
            if (scaledGridCellSize > 0)
            {
                for (float x = scaledGridCellSize; x < canvasSize.X; x += scaledGridCellSize)
                {
                    drawList.AddLine(new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y), new Vector2(canvasOriginScreen.X + x, canvasOriginScreen.Y + canvasSize.Y), gridColor, scaledGridLineThickness);
                }
                for (float y = scaledGridCellSize; y < canvasSize.Y; y += scaledGridCellSize)
                {
                    drawList.AddLine(new Vector2(canvasOriginScreen.X, canvasOriginScreen.Y + y), new Vector2(canvasOriginScreen.X + canvasSize.X, canvasOriginScreen.Y + y), gridColor, scaledGridLineThickness);
                }
            }

            uint canvasOutlineColorU32 = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.45f, 1f));
            drawList.AddRect(canvasOriginScreen - Vector2.One, canvasOriginScreen + canvasSize + Vector2.One, canvasOutlineColorU32, 0f, ImDrawFlags.None, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));

            if (inPlaceTextEditor.IsEditing)
            {
                inPlaceTextEditor.RecalculateEditorBounds(canvasOriginScreen, ImGuiHelpers.GlobalScale);
                inPlaceTextEditor.DrawEditorUI();
            }

            ImGui.SetCursorScreenPos(canvasOriginScreen);
            ImGui.InvisibleButton("##AetherDrawCanvasInteractionLayer", canvasSize);

            Vector2 mousePosScreen = ImGui.GetMousePos();
            Vector2 mousePosLogical = (mousePosScreen - canvasOriginScreen) / ImGuiHelpers.GlobalScale;

            bool canvasInteractLayerHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.None);
            bool canInteractWithCanvas = !inPlaceTextEditor.IsEditing && canvasInteractLayerHovered;

            bool isLMBDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            bool isLMBClickedOnCanvas = canInteractWithCanvas && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            bool isLMBReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            bool isLMBDoubleClickedOnCanvas = canInteractWithCanvas && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);

            var currentDrawables = DrawablesListOfCurrentPage;
            BaseDrawable? singleSelectedItem = selectedDrawables.Count == 1 ? selectedDrawables[0] : null;

            if (canInteractWithCanvas)
            {
                if (isLMBDoubleClickedOnCanvas && currentDrawMode == DrawMode.Select && hoveredDrawable is DrawableText dt)
                {
                    if (!inPlaceTextEditor.IsCurrentlyEditing(dt))
                    {
                        inPlaceTextEditor.BeginEdit(dt, canvasOriginScreen, ImGuiHelpers.GlobalScale);
                    }
                    goto EndInteractionProcessing;
                }

                if (currentDrawMode == DrawMode.Select)
                {
                    shapeInteractionHandler.ProcessInteractions(
                        singleSelectedItem, selectedDrawables, currentDrawables, GetLayerPriority, ref hoveredDrawable,
                        mousePosLogical,
                        mousePosScreen, canvasOriginScreen, canInteractWithCanvas, // Pass canInteractWithCanvas
                        isLMBClickedOnCanvas, isLMBDown, isLMBReleased, drawList, ref lastMouseDragPosLogical // Use logical last drag pos 
                    );
                }
                else if (currentDrawMode == DrawMode.Eraser)
                {
                    if (canvasInteractLayerHovered)
                    {
                        drawList.AddCircle(mousePosScreen, ScaledEraserVisualRadius, ImGui.GetColorU32(new Vector4(1, 0, 0, 0.7f)), 32, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));
                    }
                    if (isLMBDown)
                    {
                        float logicalEraserRadius = ScaledEraserLogicRadius / ImGuiHelpers.GlobalScale;

                        for (int i = currentDrawables.Count - 1; i >= 0; i--)
                        {
                            var d = currentDrawables[i];
                            bool removedByPathEdit = false;
                            if (d is DrawablePath path)
                            {
                                int originalCount = path.PointsRelative.Count;
                                path.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosLogical) < logicalEraserRadius);
                                if (path.PointsRelative.Count < 2 && originalCount >= 2) { currentDrawables.RemoveAt(i); removedByPathEdit = true; }
                            }
                            else if (d is DrawableDash dashPath)
                            {
                                int originalCount = dashPath.PointsRelative.Count;
                                dashPath.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosLogical) < logicalEraserRadius);
                                if (dashPath.PointsRelative.Count < 2 && originalCount >= 2) { currentDrawables.RemoveAt(i); removedByPathEdit = true; }
                            }

                            if (!removedByPathEdit && d.IsHit(mousePosLogical, logicalEraserRadius))
                            {
                                currentDrawables.RemoveAt(i);
                                if (selectedDrawables.Contains(d)) selectedDrawables.Remove(d);
                                if (hoveredDrawable != null && hoveredDrawable.Equals(d)) hoveredDrawable = null;
                            }
                        }
                    }
                }
                else if (currentDrawMode == DrawMode.TextTool)
                {
                    if (isLMBClickedOnCanvas)
                    {
                        // Use DefaultUnscaledFontSize and DefaultUnscaledTextWrapWidth
                        var newText = new DrawableText(mousePosLogical, "New Text", currentBrushColor, DefaultUnscaledFontSize, DefaultUnscaledTextWrapWidth);
                        DrawablesListOfCurrentPage.Add(newText);
                        foreach (var sel in selectedDrawables) sel.IsSelected = false;
                        selectedDrawables.Clear();
                        newText.IsSelected = true;
                        selectedDrawables.Add(newText);
                        hoveredDrawable = newText;
                        inPlaceTextEditor.BeginEdit(newText, canvasOriginScreen, ImGuiHelpers.GlobalScale);
                        currentDrawMode = DrawMode.Select;
                    }
                }
                else if ((currentDrawMode >= DrawMode.BossImage && currentDrawMode <= DrawMode.AddMobIcon) ||
                         (currentDrawMode >= DrawMode.Waymark1Image && currentDrawMode <= DrawMode.WaymarkDImage))
                {
                    if (canvasInteractLayerHovered) ImGui.SetTooltip($"Click to place {currentDrawMode}");
                    if (isLMBClickedOnCanvas)
                    {
                        string imagePath = "";
                        Vector2 imageUnscaledSize = DefaultUnscaledImageSize; // Use logical default
                        Vector4 tint = Vector4.One;

                        switch (currentDrawMode) // Assign path and specific unscaled sizes
                        {
                            case DrawMode.BossImage: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(60f, 60f); break;
                            case DrawMode.CircleAoEImage: imagePath = "PluginImages.svg.prox_aoe.svg"; imageUnscaledSize = new Vector2(50f, 50f); break;
                            case DrawMode.DonutAoEImage: imagePath = "PluginImages.svg.donut.svg"; imageUnscaledSize = new Vector2(50f, 50f); break;
                            case DrawMode.FlareImage: imagePath = "PluginImages.svg.flare.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                            case DrawMode.LineStackImage: imagePath = "PluginImages.svg.line_stack.svg"; imageUnscaledSize = new Vector2(30f, 60f); break;
                            case DrawMode.SpreadImage: imagePath = "PluginImages.svg.spread.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                            case DrawMode.StackImage: imagePath = "PluginImages.svg.stack.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;

                            case DrawMode.Waymark1Image: imagePath = "PluginImages.toolbar.1_waymark.JPG"; break; // Uses DefaultUnscaledImageSize
                            case DrawMode.Waymark2Image: imagePath = "PluginImages.toolbar.2_waymark.JPG"; break;
                            case DrawMode.Waymark3Image: imagePath = "PluginImages.toolbar.3_waymark.JPG"; break;
                            case DrawMode.Waymark4Image: imagePath = "PluginImages.toolbar.4_waymark.JPG"; break;
                            case DrawMode.WaymarkAImage: imagePath = "PluginImages.toolbar.A.JPG"; break;
                            case DrawMode.WaymarkBImage: imagePath = "PluginImages.toolbar.B.JPG"; break;
                            case DrawMode.WaymarkCImage: imagePath = "PluginImages.toolbar.C.JPG"; break;
                            case DrawMode.WaymarkDImage: imagePath = "PluginImages.toolbar.D.JPG"; break;
                            case DrawMode.RoleTankImage: imagePath = "PluginImages.toolbar.Tank.JPG"; break;
                            case DrawMode.RoleHealerImage: imagePath = "PluginImages.toolbar.Healer.JPG"; break;
                            case DrawMode.RoleMeleeImage: imagePath = "PluginImages.toolbar.Melee.JPG"; break;
                            case DrawMode.RoleRangedImage: imagePath = "PluginImages.toolbar.Ranged.JPG"; break;

                            case DrawMode.StackIcon: imagePath = "PluginImages.svg.stack.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                            case DrawMode.SpreadIcon: imagePath = "PluginImages.svg.spread.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                            case DrawMode.TetherIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                            case DrawMode.BossIconPlaceholder: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(35f, 35f); break;
                            case DrawMode.AddMobIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(30f, 30f); break;
                        }
                        if (!string.IsNullOrEmpty(imagePath))
                        {
                            // Pass logical mouse position and unscaled size
                            DrawablesListOfCurrentPage.Add(new DrawableImage(currentDrawMode, imagePath, mousePosLogical, imageUnscaledSize, tint));
                            AetherDraw.Plugin.Log?.Debug($"[MainWindow] Placed image: {currentDrawMode} at logical {mousePosLogical}");
                        }
                        else
                        {
                            AetherDraw.Plugin.Log?.Warning($"[MainWindow] No imagePath defined for DrawMode: {currentDrawMode}");
                        }
                    }
                }
                else
                {
                    float logicalBrushThickness = currentBrushThickness; // This is already unscaled
                    if (isLMBDown)
                    {
                        if (!isDrawingOnCanvas && isLMBClickedOnCanvas)
                        {
                            isDrawingOnCanvas = true;
                            foreach (var sel in selectedDrawables) sel.IsSelected = false;
                            selectedDrawables.Clear();
                            if (hoveredDrawable != null) hoveredDrawable.IsHovered = false;
                            hoveredDrawable = null;

                            switch (currentDrawMode)
                            {
                                case DrawMode.Pen: currentDrawingObject = new DrawablePath(mousePosLogical, currentBrushColor, logicalBrushThickness); break;
                                case DrawMode.StraightLine: currentDrawingObject = new DrawableStraightLine(mousePosLogical, currentBrushColor, logicalBrushThickness); break;
                                case DrawMode.Dash: currentDrawingObject = new DrawableDash(mousePosLogical, currentBrushColor, logicalBrushThickness); break;
                                case DrawMode.Rectangle: currentDrawingObject = new DrawableRectangle(mousePosLogical, currentBrushColor, logicalBrushThickness, currentShapeFilled); break;
                                case DrawMode.Circle: currentDrawingObject = new DrawableCircle(mousePosLogical, currentBrushColor, logicalBrushThickness, currentShapeFilled); break;
                                case DrawMode.Arrow: currentDrawingObject = new DrawableArrow(mousePosLogical, currentBrushColor, logicalBrushThickness); break;
                                case DrawMode.Cone: currentDrawingObject = new DrawableCone(mousePosLogical, currentBrushColor, logicalBrushThickness, currentShapeFilled); break;
                            }
                            AetherDraw.Plugin.Log?.Debug($"[MainWindow] Started drawing: {currentDrawMode} at logical {mousePosLogical}");
                            // Call AddPoint for Path/Dash here for the first point AFTER constructor
                            if (currentDrawingObject is DrawablePath p) p.AddPoint(mousePosLogical);
                            else if (currentDrawingObject is DrawableDash d) d.AddPoint(mousePosLogical);
                            else currentDrawingObject?.UpdatePreview(mousePosLogical);
                        }
                        if (isDrawingOnCanvas && currentDrawingObject != null)
                        {
                            if (currentDrawingObject is DrawablePath p) p.AddPoint(mousePosLogical);
                            else if (currentDrawingObject is DrawableDash d) d.AddPoint(mousePosLogical);
                            else currentDrawingObject.UpdatePreview(mousePosLogical);
                        }
                    }
                    if (isDrawingOnCanvas && isLMBReleased)
                    {
                        FinalizeCurrentDrawing();
                    }
                }
            }

        EndInteractionProcessing:;

            bool clippingPushed = false;
            try
            {
                ImGui.PushClipRect(canvasOriginScreen, canvasOriginScreen + canvasSize, true);
                clippingPushed = true;

                var drawablesToRender = DrawablesListOfCurrentPage;
                if (drawablesToRender != null && drawablesToRender.Any())
                {
                    var sortedDrawables = drawablesToRender.OrderBy(d => GetLayerPriority(d.ObjectDrawMode)).ToList();
                    foreach (var drawable in sortedDrawables)
                    {
                        if (inPlaceTextEditor.IsEditing && inPlaceTextEditor.IsCurrentlyEditing(drawable)) continue;
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
