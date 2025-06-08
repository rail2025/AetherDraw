// AetherDraw/UI/ToolbarDrawer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherDraw.Core;
using AetherDraw.DrawingLogic;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Dalamud.Utility;

namespace AetherDraw.UI
{
    public class ToolbarButton
    {
        public DrawMode Primary { get; set; } // The default icon for the button
        public List<DrawMode> SubModes { get; set; } = new(); // The icons in the popup
        public string Tooltip { get; set; } = "";
    }

    /// <summary>
    /// Handles the drawing of the main toolbar controls for AetherDraw.
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
        private readonly Action onClearAll;
        private readonly Action onUndo;

        // Direct references to handlers/managers
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly DrawingLogic.InPlaceTextEditor inPlaceTextEditor;
        private readonly UndoManager undoManager;

        // Data structures for the toolbar layout
        private readonly List<ToolbarButton> mainToolbarButtons;
        private readonly Dictionary<DrawMode, DrawMode> activeSubModeMap;
        private readonly Dictionary<DrawMode, string> iconPaths;
        private readonly Dictionary<DrawMode, string> toolDisplayNames;

        // Static UI definitions
        private static readonly float[] ThicknessPresets = { 1.5f, 4f, 7f, 10f };
        private static readonly Vector4[] ColorPalette = {
            new(1.0f,1.0f,1.0f,1.0f), new(0.0f,0.0f,0.0f,1.0f),
            new(1.0f,0.0f,0.0f,1.0f), new(0.0f,1.0f,0.0f,1.0f),
            new(0.0f,0.0f,1.0f,1.0f), new(1.0f,1.0f,0.0f,1.0f),
            new(1.0f,0.0f,1.0f,1.0f), new(0.0f,1.0f,1.0f,1.0f),
            new(0.5f,0.5f,0.5f,1.0f), new(0.8f,0.4f,0.0f,1.0f)
        };

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

            this.mainToolbarButtons = new List<ToolbarButton>
            {
                new() { Primary = DrawMode.Pen, SubModes = new List<DrawMode> { DrawMode.Pen, DrawMode.StraightLine, DrawMode.Dash }, Tooltip = "Drawing Tools" },
                new() { Primary = DrawMode.Rectangle, SubModes = new List<DrawMode> { DrawMode.Rectangle, DrawMode.Circle, DrawMode.Arrow, DrawMode.Cone }, Tooltip = "Shape Tools" },
                new() { Primary = DrawMode.SquareImage, SubModes = new List<DrawMode> { DrawMode.SquareImage, DrawMode.CircleMarkImage, DrawMode.TriangleImage, DrawMode.PlusImage }, Tooltip = "Placeable Shapes" },
                new() { Primary = DrawMode.RoleTankImage, SubModes = new List<DrawMode> { DrawMode.RoleTankImage, DrawMode.RoleHealerImage, DrawMode.RoleMeleeImage, DrawMode.RoleRangedImage }, Tooltip = "Role Icons" },
                new() { Primary = DrawMode.Party1Image, SubModes = new List<DrawMode> { DrawMode.Party1Image, DrawMode.Party2Image, DrawMode.Party3Image, DrawMode.Party4Image }, Tooltip = "Party Number Icons" },
                new() { Primary = DrawMode.WaymarkAImage, SubModes = new List<DrawMode> { DrawMode.WaymarkAImage, DrawMode.WaymarkBImage, DrawMode.WaymarkCImage, DrawMode.WaymarkDImage }, Tooltip = "Waymarks A-D" },
                new() { Primary = DrawMode.Waymark1Image, SubModes = new List<DrawMode> { DrawMode.Waymark1Image, DrawMode.Waymark2Image, DrawMode.Waymark3Image, DrawMode.Waymark4Image }, Tooltip = "Waymarks 1-4" },
                new() { Primary = DrawMode.StackImage, SubModes = new List<DrawMode> { DrawMode.StackImage, DrawMode.SpreadImage, DrawMode.LineStackImage, DrawMode.FlareImage, DrawMode.DonutAoEImage, DrawMode.CircleAoEImage, DrawMode.BossImage }, Tooltip = "Mechanic Icons" },
            };

            this.activeSubModeMap = new Dictionary<DrawMode, DrawMode>();
            foreach (var button in this.mainToolbarButtons)
            {
                this.activeSubModeMap[button.Primary] = button.Primary;
            }

            this.iconPaths = new Dictionary<DrawMode, string>
            {
                { DrawMode.Pen, "" }, { DrawMode.StraightLine, "" }, { DrawMode.Dash, "" },
                { DrawMode.Rectangle, "" }, { DrawMode.Circle, "" }, { DrawMode.Arrow, "" }, { DrawMode.Cone, "" },
                { DrawMode.SquareImage, "PluginImages.toolbar.Square.png" },
                { DrawMode.CircleMarkImage, "PluginImages.toolbar.CircleMark.png" },
                { DrawMode.TriangleImage, "PluginImages.toolbar.Triangle.png" },
                { DrawMode.PlusImage, "PluginImages.toolbar.Plus.png" },
                { DrawMode.RoleTankImage, "PluginImages.toolbar.Tank.JPG" },
                { DrawMode.RoleHealerImage, "PluginImages.toolbar.Healer.JPG" },
                { DrawMode.RoleMeleeImage, "PluginImages.toolbar.Melee.JPG" },
                { DrawMode.RoleRangedImage, "PluginImages.toolbar.Ranged.JPG" },
                { DrawMode.Party1Image, "PluginImages.toolbar.Party1.png" },
                { DrawMode.Party2Image, "PluginImages.toolbar.Party2.png" },
                { DrawMode.Party3Image, "PluginImages.toolbar.Party3.png" },
                { DrawMode.Party4Image, "PluginImages.toolbar.Party4.png" },
                { DrawMode.WaymarkAImage, "PluginImages.toolbar.A.png" },
                { DrawMode.WaymarkBImage, "PluginImages.toolbar.B.png" },
                { DrawMode.WaymarkCImage, "PluginImages.toolbar.C.png" },
                { DrawMode.WaymarkDImage, "PluginImages.toolbar.D.png" },
                { DrawMode.Waymark1Image, "PluginImages.toolbar.1_waymark.png" },
                { DrawMode.Waymark2Image, "PluginImages.toolbar.2_waymark.png" },
                { DrawMode.Waymark3Image, "PluginImages.toolbar.3_waymark.png" },
                { DrawMode.Waymark4Image, "PluginImages.toolbar.4_waymark.png" },
                { DrawMode.StackImage, "PluginImages.svg.stack.svg" },
                { DrawMode.SpreadImage, "PluginImages.svg.spread.svg" },
                { DrawMode.LineStackImage, "PluginImages.svg.line_stack.svg" },
                { DrawMode.FlareImage, "PluginImages.svg.flare.svg" },
                { DrawMode.DonutAoEImage, "PluginImages.svg.donut.svg" },
                { DrawMode.CircleAoEImage, "PluginImages.svg.prox_aoe.svg" },
                { DrawMode.BossImage, "PluginImages.svg.boss.svg" }
            };

            this.toolDisplayNames = new Dictionary<DrawMode, string>
            {
                { DrawMode.StraightLine, "Line" },
                { DrawMode.Rectangle, "Rect" }
            };
        }

        public void DrawLeftToolbar()
        {
            DrawMode currentDrawMode = getCurrentDrawMode();
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float itemSpacing = ImGui.GetStyle().ItemSpacing.X;
            float btnWidthFull = availableWidth;
            float btnWidthHalf = (availableWidth - itemSpacing) / 2f;

            // --- Static Buttons ---
            void DrawToolButton(string label, DrawMode mode, float width)
            {
                bool isSelected = currentDrawMode == mode;
                using (isSelected ? ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]) : null)
                {
                    if (ImGui.Button(label, new Vector2(width, 0))) setCurrentDrawMode(mode);
                }
            }

            DrawToolButton("Select", DrawMode.Select, btnWidthHalf);
            ImGui.SameLine();
            DrawToolButton("Eraser", DrawMode.Eraser, btnWidthHalf);

            if (ImGui.Button("Copy", new Vector2(btnWidthHalf, 0))) onCopySelected();
            ImGui.SameLine();
            if (ImGui.Button("Paste", new Vector2(btnWidthHalf, 0))) onPasteCopied();

            if (undoManager.CanUndo()) { if (ImGui.Button("Undo", new Vector2(btnWidthFull, 0))) onUndo(); }
            else { using (ImRaii.Disabled()) ImGui.Button("Undo", new Vector2(btnWidthFull, 0)); }

            if (ImGui.Button("Clear All", new Vector2(btnWidthFull, 0))) onClearAll();

            ImGui.Separator();

            // --- Dynamic Popup Buttons ---
            Vector2 iconButtonSize = new(btnWidthHalf, 45 * ImGuiHelpers.GlobalScale);
            Vector2 popupIconButtonSize = new(32 * ImGuiHelpers.GlobalScale, 32 * ImGuiHelpers.GlobalScale);

            for (int i = 0; i < mainToolbarButtons.Count; i++)
            {
                var group = mainToolbarButtons[i];
                if (i > 0 && i % 2 != 0) ImGui.SameLine();
                DrawMode activeModeInGroup = activeSubModeMap[group.Primary];
                string activePath = iconPaths.GetValueOrDefault(activeModeInGroup, "");
                var tex = activePath != "" ? TextureManager.GetTexture(activePath) : null;
                var drawList = ImGui.GetWindowDrawList();
                bool isGroupActive = group.SubModes.Contains(currentDrawMode);
                using (isGroupActive ? ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]) : null)
                {
                    if (ImGui.Button($"##{group.Primary}", iconButtonSize)) setCurrentDrawMode(activeModeInGroup);
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    var center = (min + max) / 2;
                    if (tex != null) drawList.AddImage(tex.ImGuiHandle, min, max);
                    else
                    {
                        var color = ImGui.GetColorU32(ImGuiCol.Text);
                        var graphicCenter = new Vector2(center.X, min.Y + iconButtonSize.Y * 0.35f);
                        if (group.Primary == DrawMode.Pen) drawList.AddLine(graphicCenter - new Vector2(iconButtonSize.X * 0.2f, iconButtonSize.Y * 0.15f), graphicCenter + new Vector2(iconButtonSize.X * 0.2f, iconButtonSize.Y * 0.15f), color, 2f);
                        else if (group.Primary == DrawMode.Rectangle)
                        {
                            drawList.AddRect(graphicCenter - new Vector2(iconButtonSize.X * 0.1f, iconButtonSize.Y * 0.1f), graphicCenter + new Vector2(iconButtonSize.X * 0.1f, iconButtonSize.Y * 0.1f), color, 0f, ImDrawFlags.None, 2f);
                            drawList.AddCircle(graphicCenter, iconButtonSize.X * 0.2f, color, 0, 2f);
                        }
                        var activeToolName = toolDisplayNames.GetValueOrDefault(activeModeInGroup, activeModeInGroup.ToString().Replace("Image", ""));
                        var textSize = ImGui.CalcTextSize(activeToolName);
                        drawList.AddText(new Vector2(center.X - textSize.X / 2, max.Y - textSize.Y - (iconButtonSize.Y * 0.1f)), ImGui.GetColorU32(ImGuiCol.Text), activeToolName);
                    }
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(group.Tooltip);
                if (ImGui.BeginPopupContextItem($"popup_{group.Primary}", ImGuiPopupFlags.MouseButtonLeft))
                {
                    foreach (var subMode in group.SubModes)
                    {
                        string subPath = iconPaths.GetValueOrDefault(subMode, "");
                        var subTex = subPath != "" ? TextureManager.GetTexture(subPath) : null;
                        if (subTex != null)
                        {
                            if (ImGui.ImageButton(subTex.ImGuiHandle, popupIconButtonSize))
                            {
                                setCurrentDrawMode(subMode);
                                activeSubModeMap[group.Primary] = subMode;
                                ImGui.CloseCurrentPopup();
                            }
                        }
                        else
                        {
                            var displayName = toolDisplayNames.GetValueOrDefault(subMode, subMode.ToString());
                            if (ImGui.Selectable(displayName, currentDrawMode == subMode))
                            {
                                setCurrentDrawMode(subMode);
                                activeSubModeMap[group.Primary] = subMode;
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }
                    ImGui.EndPopup();
                }
            }

            bool isTextToolSelected = currentDrawMode == DrawMode.TextTool;
            using (isTextToolSelected ? ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]) : null)
            {
                if (ImGui.Button("TEXT", new Vector2(btnWidthHalf, iconButtonSize.Y))) setCurrentDrawMode(DrawMode.TextTool);
            }

            ImGui.Separator();

            DrawFillOutlineToggle();

            ImGui.Separator();
            ImGui.Text("Thickness:");
            float thicknessButtonWidth = (availableWidth - itemSpacing * (ThicknessPresets.Length - 1)) / ThicknessPresets.Length;
            foreach (var t in ThicknessPresets)
            {
                if (t != ThicknessPresets[0]) ImGui.SameLine();
                if (ImGui.Selectable($"{t:0}", Math.Abs(getCurrentBrushThickness() - t) < 0.01f, 0, new Vector2(thicknessButtonWidth, 0)))
                    setCurrentBrushThickness(t);
            }

            ImGui.Separator();
            int colorsPerRow = 5;
            float smallColorButtonSize = (availableWidth - (itemSpacing * (colorsPerRow - 1))) / colorsPerRow;
            Vector2 colorButtonDimensions = new(smallColorButtonSize, smallColorButtonSize);
            for (int i = 0; i < ColorPalette.Length; i++)
            {
                if (i > 0 && i % colorsPerRow != 0) ImGui.SameLine();
                if (ImGui.ColorButton($"##ColorPaletteButton{i}", ColorPalette[i], ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, colorButtonDimensions))
                    setCurrentBrushColor(ColorPalette[i]);
                if (ColorPalette[i] == getCurrentBrushColor())
                    ImGui.GetForegroundDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), 0, ImDrawFlags.None, 2f);
            }

            // --- Ko-Fi Button (at the very bottom) ---
            float buttonHeight = ImGui.GetFrameHeight();
            float availableHeight = ImGui.GetContentRegionAvail().Y;
            if (availableHeight > buttonHeight)
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + availableHeight - buttonHeight);
            }

            string buttonText = "Support on Ko-Fi";
            uint buttonColor = 0xFF312B;
            ImGui.PushStyleColor(ImGuiCol.Button, 0xFF000000 | buttonColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, 0xDD000000 | buttonColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xAA000000 | buttonColor);
            if (ImGui.Button(buttonText, new Vector2(btnWidthFull, 0))) Util.OpenLink("https://ko-fi.com/rail2025");
            ImGui.PopStyleColor(3);
        }

        private void DrawFillOutlineToggle()
        {
            var isFilled = getIsShapeFilled();
            var drawList = ImGui.GetWindowDrawList();
            var screenPos = ImGui.GetCursorScreenPos();
            float height = ImGui.GetFrameHeight();
            float width = ImGui.GetContentRegionAvail().X;
            float radius = height * 0.5f;

            drawList.AddRectFilled(screenPos, screenPos + new Vector2(width, height), ImGui.GetColorU32(ImGuiCol.FrameBg), radius);

            var thumbColor = ImGui.GetColorU32(ImGuiCol.Button);
            float thumbWidth = (width / 2) - 4f;
            Vector2 thumbMin, thumbMax;
            if (isFilled)
            {
                thumbMin = screenPos + new Vector2(2f, 2f);
                thumbMax = thumbMin + new Vector2(thumbWidth, height - 4f);
            }
            else
            {
                thumbMin = screenPos + new Vector2(width - thumbWidth - 2f, 2f);
                thumbMax = thumbMin + new Vector2(thumbWidth, height - 4f);
            }
            drawList.AddRectFilled(thumbMin, thumbMax, thumbColor, radius - 2f);

            var textColor = ImGui.GetColorU32(ImGuiCol.Text);
            var fillSize = ImGui.CalcTextSize("Fill");
            var outlineSize = ImGui.CalcTextSize("Outline");
            drawList.AddText(new Vector2(screenPos.X + (width / 4) - fillSize.X / 2, screenPos.Y + (height - fillSize.Y) / 2), textColor, "Fill");
            drawList.AddText(new Vector2(screenPos.X + (width * 3 / 4) - outlineSize.X / 2, screenPos.Y + (height - outlineSize.Y) / 2), textColor, "Outline");

            if (ImGui.InvisibleButton("##FillToggle", new Vector2(width, height)))
            {
                setIsShapeFilled(!isFilled);
            }
        }
    }
}
