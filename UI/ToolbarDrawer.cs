// AetherDraw/UI/ToolbarDrawer.cs
using System;
using System.Numerics;
using AetherDraw.Core;
using AetherDraw.DrawingLogic;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;

namespace AetherDraw.UI
{
    /// <summary>
    /// Handles the drawing of the main toolbar controls for AetherDraw.
    /// This includes tool selection, drawing properties (color, thickness, fill),
    /// and actions like Undo, Clear All, Copy, Paste.
    /// </summary>
    public class ToolbarDrawer
    {
        // Delegates for accessing and modifying MainWindow's state
        private readonly Func<DrawMode> getCurrentDrawMode;
        private readonly Action<DrawMode> setCurrentDrawMode;
        private readonly Func<bool> getIsShapeFilled;
        private readonly Action<bool> setIsShapeFilled;
        private readonly Func<float> getCurrentBrushThickness;
        private readonly Action<float> setCurrentBrushThickness;
        private readonly Func<Vector4> getCurrentBrushColor;
        private readonly Action<Vector4> setCurrentBrushColor;

        // Callbacks for actions handled by MainWindow
        private readonly Action onCopySelected;
        private readonly Action onPasteCopied;
        private readonly Action onClearAll; // MainWindow will handle UndoManager.Record and actual clearing via PageManager
        private readonly Action onUndo;     // MainWindow will handle UndoManager.Undo and applying state via PageManager

        // Direct references to handlers/managers
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly DrawingLogic.InPlaceTextEditor inPlaceTextEditor; // Fully qualify to avoid ambiguity
        private readonly UndoManager undoManager; // Needed for CanUndo

        // Static UI definitions (moved from MainWindow)
        private static readonly float[] ThicknessPresets = new float[] { 1.5f, 4f, 7f, 10f };
        private static readonly Vector4[] ColorPalette = new Vector4[] {
            new Vector4(1.0f,1.0f,1.0f,1.0f), new Vector4(0.0f,0.0f,0.0f,1.0f),
            new Vector4(1.0f,0.0f,0.0f,1.0f), new Vector4(0.0f,1.0f,0.0f,1.0f),
            new Vector4(0.0f,0.0f,1.0f,1.0f), new Vector4(1.0f,1.0f,0.0f,1.0f),
            new Vector4(1.0f,0.0f,1.0f,1.0f), new Vector4(0.0f,1.0f,1.0f,1.0f),
            new Vector4(0.5f,0.5f,0.5f,1.0f), new Vector4(0.8f,0.4f,0.0f,1.0f)
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolbarDrawer"/> class.
        /// </summary>
        public ToolbarDrawer(
            Func<DrawMode> getCurrentDrawMode, Action<DrawMode> setCurrentDrawMode,
            ShapeInteractionHandler shapeInteractionHandler, DrawingLogic.InPlaceTextEditor inPlaceTextEditor,
            Action onCopySelected, Action onPasteCopied, Action onClearAll, Action onUndo,
            Func<bool> getIsShapeFilled, Action<bool> setIsShapeFilled,
            UndoManager undoManager,
            Func<float> getCurrentBrushThickness, Action<float> setCurrentBrushThickness,
            Func<Vector4> getCurrentBrushColor, Action<Vector4> setCurrentBrushColor)
        {
            this.getCurrentDrawMode = getCurrentDrawMode ?? throw new ArgumentNullException(nameof(getCurrentDrawMode));
            this.setCurrentDrawMode = setCurrentDrawMode ?? throw new ArgumentNullException(nameof(setCurrentDrawMode));
            this.shapeInteractionHandler = shapeInteractionHandler ?? throw new ArgumentNullException(nameof(shapeInteractionHandler));
            this.inPlaceTextEditor = inPlaceTextEditor ?? throw new ArgumentNullException(nameof(inPlaceTextEditor));
            this.onCopySelected = onCopySelected ?? throw new ArgumentNullException(nameof(onCopySelected));
            this.onPasteCopied = onPasteCopied ?? throw new ArgumentNullException(nameof(onPasteCopied));
            this.onClearAll = onClearAll ?? throw new ArgumentNullException(nameof(onClearAll));
            this.onUndo = onUndo ?? throw new ArgumentNullException(nameof(onUndo));
            this.getIsShapeFilled = getIsShapeFilled ?? throw new ArgumentNullException(nameof(getIsShapeFilled));
            this.setIsShapeFilled = setIsShapeFilled ?? throw new ArgumentNullException(nameof(setIsShapeFilled));
            this.undoManager = undoManager ?? throw new ArgumentNullException(nameof(undoManager));
            this.getCurrentBrushThickness = getCurrentBrushThickness ?? throw new ArgumentNullException(nameof(getCurrentBrushThickness));
            this.setCurrentBrushThickness = setCurrentBrushThickness ?? throw new ArgumentNullException(nameof(setCurrentBrushThickness));
            this.getCurrentBrushColor = getCurrentBrushColor ?? throw new ArgumentNullException(nameof(getCurrentBrushColor));
            this.setCurrentBrushColor = setCurrentBrushColor ?? throw new ArgumentNullException(nameof(setCurrentBrushColor));
        }

        /// <summary>
        /// Draws the toolbar controls. This method replicates the original DrawToolbarControls from MainWindow.
        /// </summary>
        public void DrawLeftToolbar()
        {
            DrawMode currentDrawMode = getCurrentDrawMode(); // Get current values
            bool currentShapeFilled = getIsShapeFilled();
            float currentBrushThickness = getCurrentBrushThickness();
            Vector4 currentBrushColor = getCurrentBrushColor();

            Vector4 activeToolColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float itemSpacingX = ImGui.GetStyle().ItemSpacing.X;
            float btnWidthHalf = Math.Max((availableWidth - itemSpacingX) / 2f, 30f * ImGuiHelpers.GlobalScale);
            float btnWidthFull = availableWidth;

            // --- Helper for standard tool buttons ---
            void ToolButton(string label, DrawMode mode, float buttonWidth)
            {
                bool isSelected = currentDrawMode == mode;
                using (isSelected ? ImRaii.PushColor(ImGuiCol.Button, activeToolColor) : null)
                {
                    if (ImGui.Button($"{label}##ToolBtn_{mode}", new Vector2(buttonWidth, 0)))
                    {
                        setCurrentDrawMode(mode); // Use setter delegate
                        if (mode != DrawMode.Select && mode != DrawMode.TextTool) shapeInteractionHandler.ResetDragState();
                        if (inPlaceTextEditor.IsEditing && mode != DrawMode.TextTool && mode != DrawMode.Select) { inPlaceTextEditor.CommitAndEndEdit(); }
                    }
                }
            }

            // --- Helper for tool buttons with embedded images ---
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
                        setCurrentDrawMode(mode); // Use setter delegate
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

            // --- Toolbar Buttons ---
            ToolButton("Select", DrawMode.Select, btnWidthHalf); ImGui.SameLine();
            ToolButton("Eraser", DrawMode.Eraser, btnWidthHalf);

            if (ImGui.Button("Copy", new Vector2(btnWidthHalf, 0))) onCopySelected(); ImGui.SameLine();
            if (ImGui.Button("Paste", new Vector2(btnWidthHalf, 0))) onPasteCopied();

            bool tempShapeFilled = currentShapeFilled; // Use a temporary variable for ImGui.Checkbox
            if (ImGui.Checkbox("Fill Shape", ref tempShapeFilled))
            {
                setIsShapeFilled(tempShapeFilled); // Use setter delegate
            }

            // Undo Button
            if (undoManager.CanUndo())
            {
                if (ImGui.Button("Undo", new Vector2(btnWidthFull, 0))) onUndo();
            }
            else
            {
                using (ImRaii.Disabled()) ImGui.Button("Undo", new Vector2(btnWidthFull, 0));
            }

            // Clear All Button
            if (ImGui.Button("Clear All", new Vector2(btnWidthFull, 0))) onClearAll();
            ImGui.Separator();

            // Drawing Shape Tools
            ToolButton("Pen", DrawMode.Pen, btnWidthHalf); ImGui.SameLine();
            ToolButton("Line", DrawMode.StraightLine, btnWidthHalf);
            ToolButton("Dash", DrawMode.Dash, btnWidthHalf); ImGui.SameLine();
            ToolButton("Rect", DrawMode.Rectangle, btnWidthHalf);
            ToolButton("Circle", DrawMode.Circle, btnWidthHalf); ImGui.SameLine();
            ToolButton("Arrow", DrawMode.Arrow, btnWidthHalf);
            ToolButton("Cone", DrawMode.Cone, btnWidthHalf); ImGui.SameLine();
            ToolButton("A", DrawMode.TextTool, btnWidthHalf);
            ImGui.Separator();

            // Placeable Image Tools
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

            // Thickness Selection
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
                    if (ImGui.Button($"{t:0}##ThicknessBtn{i}", new Vector2(thicknessButtonWidth, 0)))
                    {
                        setCurrentBrushThickness(t); // Use setter delegate
                    }
                }
            }
            ImGui.Separator();

            // Color Palette
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
                    setCurrentBrushColor(ColorPalette[i]); // Use setter delegate
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
    }
}
