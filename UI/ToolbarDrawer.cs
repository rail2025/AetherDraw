using AetherDraw.Core;
using AetherDraw.DrawingLogic;
using AetherDraw.Networking;
using AetherDraw.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherDraw.UI
{
    public class ToolbarButton
    {
        public DrawMode Primary { get; set; }
        public List<DrawMode> SubModes { get; set; } = new();
        public string Tooltip { get; set; } = "";
    }

    public class ToolbarDrawer
    {
        private readonly Plugin plugin;
        private readonly PageManager pageManager; 
        private readonly Func<DrawMode> getCurrentDrawMode;
        private readonly Action<DrawMode> setCurrentDrawMode;
        private readonly Func<bool> getIsShapeFilled;
        private readonly Action<bool> setIsShapeFilled;
        private readonly Func<float> getCurrentBrushThickness;
        private readonly Action<float> setCurrentBrushThickness;
        private readonly Func<Vector4> getCurrentBrushColor;
        private readonly Action<Vector4> setCurrentBrushColor;
        private readonly Action onCopySelected;
        private readonly Action onPasteCopied;
        private readonly Action onClearAll;
        private readonly Action onUndo;
        private readonly Action onOpenEmojiPicker;
        private readonly Action onOpenStatusSearchPopup; // New action for Status Search
        private readonly Action onImportBackgroundUrl;
        private readonly Func<bool> getIsGridVisible;
        private readonly Action<bool> setIsGridVisible;
        private readonly Func<float> getGridSize;
        private readonly Action<float> setGridSize;
        private readonly Func<bool> getIsSnapToGrid;
        private readonly Action<bool> setIsSnapToGrid;
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly DrawingLogic.InPlaceTextEditor inPlaceTextEditor;
        private readonly UndoManager undoManager;
        private readonly Stack<MainWindow.IPlanAction> planUndoStack;
        private readonly List<ToolbarButton> mainToolbarButtons;
        private readonly Dictionary<DrawMode, DrawMode> activeSubModeMap;
        private readonly Dictionary<DrawMode, string> iconPaths;
        private readonly Dictionary<DrawMode, string> toolDisplayNames;
        private bool isAllLocked = false;
        private static readonly float[] ThicknessPresets = { 1.5f, 4f, 7f, 10f };
        private static readonly Vector4[] ColorPalette = {
            new(1.0f,1.0f,1.0f,1.0f), new(0.0f,0.0f,0.0f,1.0f),
            new(1.0f,0.0f,0.0f,1.0f), new(0.0f,1.0f,0.0f,1.0f),
            new(0.0f,0.0f,1.0f,1.0f), new(1.0f,1.0f,0.0f,1.0f),
            new(1.0f,0.0f,1.0f,1.0f), new(0.0f,1.0f,1.0f,1.0f),
            new(0.5f,0.5f,0.5f,1.0f), new(0.8f,0.4f,0.0f,1.0f)
        };

        public ToolbarDrawer(
            Plugin plugin, PageManager pageManager, 
            Func<DrawMode> getCurrentDrawMode, Action<DrawMode> setCurrentDrawMode,
            ShapeInteractionHandler shapeInteractionHandler, DrawingLogic.InPlaceTextEditor inPlaceTextEditor,
            Action onCopySelected, Action onPasteCopied, Action onClearAll, Action onUndo,
            Func<bool> getIsShapeFilled, Action<bool> setIsShapeFilled,
            UndoManager undoManager,
            Stack<MainWindow.IPlanAction> planUndoStack,
            Func<float> getCurrentBrushThickness, Action<float> setCurrentBrushThickness,
            Func<Vector4> getCurrentBrushColor, Action<Vector4> setCurrentBrushColor,
            Action onOpenEmojiPicker,
            Action onOpenStatusSearchPopup,
            Action onImportBackgroundUrl,
            Func<bool> getIsGridVisible, Action<bool> setIsGridVisible,
            Func<float> getGridSize, Action<float> setGridSize,
            Func<bool> getIsSnapToGrid, Action<bool> setIsSnapToGrid) 

        {
            this.plugin = plugin;
            this.pageManager = pageManager; 
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
            this.planUndoStack = planUndoStack ?? throw new ArgumentNullException(nameof(planUndoStack));
            this.getCurrentBrushThickness = getCurrentBrushThickness ?? throw new ArgumentNullException(nameof(getCurrentBrushThickness));
            this.setCurrentBrushThickness = setCurrentBrushThickness ?? throw new ArgumentNullException(nameof(setCurrentBrushThickness));
            this.getCurrentBrushColor = getCurrentBrushColor ?? throw new ArgumentNullException(nameof(getCurrentBrushColor));
            this.setCurrentBrushColor = setCurrentBrushColor ?? throw new ArgumentNullException(nameof(setCurrentBrushColor));
            this.onOpenEmojiPicker = onOpenEmojiPicker;
            this.onOpenStatusSearchPopup = onOpenStatusSearchPopup;
            this.onImportBackgroundUrl = onImportBackgroundUrl;
            this.getIsGridVisible = getIsGridVisible;
            this.setIsGridVisible = setIsGridVisible;
            this.getGridSize = getGridSize;
            this.setGridSize = setGridSize;
            this.getIsSnapToGrid = getIsSnapToGrid;
            this.setIsSnapToGrid = setIsSnapToGrid;

            this.mainToolbarButtons = new List<ToolbarButton>
            {
                new() { Primary = DrawMode.Pen, SubModes = new List<DrawMode> { DrawMode.Pen, DrawMode.StraightLine, DrawMode.Dash }, Tooltip = "Drawing Tools" },
                new() { Primary = DrawMode.Rectangle, SubModes = new List<DrawMode> { DrawMode.Rectangle, DrawMode.Circle, DrawMode.Arrow, DrawMode.Cone, DrawMode.Triangle, DrawMode.Pie }, Tooltip = "Shape Tools" },
                new() { Primary = DrawMode.SquareImage, SubModes = new List<DrawMode> { DrawMode.SquareImage, DrawMode.CircleMarkImage, DrawMode.TriangleImage, DrawMode.PlusImage }, Tooltip = "Placeable Shapes" },
                new() { Primary = DrawMode.RoleTankImage, SubModes = new List<DrawMode> { DrawMode.RoleTankImage, DrawMode.RoleHealerImage, DrawMode.RoleMeleeImage, DrawMode.RoleRangedImage, DrawMode.RoleCasterImage }, Tooltip = "Role Icons" },
                new() { Primary = DrawMode.Party1Image, SubModes = new List<DrawMode> { DrawMode.Party1Image, DrawMode.Party2Image, DrawMode.Party3Image, DrawMode.Party4Image, DrawMode.Party5Image, DrawMode.Party6Image, DrawMode.Party7Image, DrawMode.Party8Image,DrawMode.Bind1Image, DrawMode.Bind2Image, DrawMode.Bind3Image,
                    DrawMode.Ignore1Image, DrawMode.Ignore2Image }, Tooltip = "Party Number Icons" },
                new() { Primary = DrawMode.WaymarkAImage, SubModes = new List<DrawMode> { DrawMode.WaymarkAImage, DrawMode.WaymarkBImage, DrawMode.WaymarkCImage, DrawMode.WaymarkDImage }, Tooltip = "Waymarks A-D" },
                new() { Primary = DrawMode.Waymark1Image, SubModes = new List<DrawMode> { DrawMode.Waymark1Image, DrawMode.Waymark2Image, DrawMode.Waymark3Image, DrawMode.Waymark4Image }, Tooltip = "Waymarks 1-4" },
                new() { Primary = DrawMode.StackImage, SubModes = new List<DrawMode> { DrawMode.StackImage, DrawMode.SpreadImage, DrawMode.LineStackImage, DrawMode.FlareImage, DrawMode.DonutAoEImage, DrawMode.CircleAoEImage, DrawMode.BossImage }, Tooltip = "Mechanic Icons" },
                new() { Primary = DrawMode.TextTool, SubModes = new List<DrawMode>(), Tooltip = "Text Tool" },
                new() { Primary = DrawMode.Dot3Image, SubModes = new List<DrawMode> { DrawMode.Dot1Image, DrawMode.Dot2Image, DrawMode.Dot3Image, DrawMode.Dot4Image, DrawMode.Dot5Image, DrawMode.Dot6Image, DrawMode.Dot7Image, DrawMode.Dot8Image }, Tooltip = "Colored Dots" },
                new() { Primary = DrawMode.StatusIconPlaceholder, SubModes = new List<DrawMode>(), Tooltip = "Status Icon" }
            };

            this.activeSubModeMap = new Dictionary<DrawMode, DrawMode>();
            foreach (var button in this.mainToolbarButtons)
            {
                this.activeSubModeMap[button.Primary] = button.Primary;
            }

            this.iconPaths = new Dictionary<DrawMode, string>
            {
                { DrawMode.Pen, "" }, { DrawMode.StraightLine, "" }, { DrawMode.Dash, "" },
                { DrawMode.Rectangle, "" }, { DrawMode.Circle, "" }, { DrawMode.Arrow, "" }, { DrawMode.Cone, "" }, { DrawMode.Triangle, ""}, { DrawMode.Pie, "" },
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
                { DrawMode.Party5Image, "PluginImages.toolbar.Party5.png" },
                { DrawMode.Party6Image, "PluginImages.toolbar.Party6.png" },
                { DrawMode.Party7Image, "PluginImages.toolbar.Party7.png" },
                { DrawMode.Party8Image, "PluginImages.toolbar.Party8.png" },
                { DrawMode.Bind1Image, "PluginImages.toolbar.bind1.png" },
                { DrawMode.Bind2Image, "PluginImages.toolbar.bind2.png" },
                { DrawMode.Bind3Image, "PluginImages.toolbar.bind3.png" },
                { DrawMode.Ignore1Image, "PluginImages.toolbar.ignore1.png" },
                { DrawMode.Ignore2Image, "PluginImages.toolbar.ignore2.png" },
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
                { DrawMode.BossImage, "PluginImages.svg.boss.svg" },
                { DrawMode.Dot1Image, "PluginImages.svg.1dot.svg" },
                { DrawMode.Dot2Image, "PluginImages.svg.2dot.svg" },
                { DrawMode.Dot3Image, "PluginImages.svg.3dot.svg" },
                { DrawMode.Dot4Image, "PluginImages.svg.4dot.svg" },
                { DrawMode.Dot5Image, "PluginImages.svg.5dot.svg" },
                { DrawMode.Dot6Image, "PluginImages.svg.6dot.svg" },
                { DrawMode.Dot7Image, "PluginImages.svg.7dot.svg" },
                { DrawMode.Dot8Image, "PluginImages.svg.8dot.svg" },
                { DrawMode.TextTool, "" },
                { DrawMode.EmojiImage, "" },
                { DrawMode.StatusIconPlaceholder, "PluginImages.toolbar.StatusPlaceholder.png" },
                { DrawMode.RoleCasterImage, "PluginImages.toolbar.caster.png" },

                // Added Job Icons
                { DrawMode.JobPldImage, "PluginImages.toolbar.pld.png" }, { DrawMode.JobWarImage, "PluginImages.toolbar.war.png" },
                { DrawMode.JobDrkImage, "PluginImages.toolbar.drk.png" }, { DrawMode.JobGnbImage, "PluginImages.toolbar.gnb.png" },
                { DrawMode.JobWhmImage, "PluginImages.toolbar.whm.png" }, { DrawMode.JobSchImage, "PluginImages.toolbar.sch.png" },
                { DrawMode.JobAstImage, "PluginImages.toolbar.ast.png" }, { DrawMode.JobSgeImage, "PluginImages.toolbar.sge.png" },
                { DrawMode.JobMnkImage, "PluginImages.toolbar.mnk.png" }, { DrawMode.JobDrgImage, "PluginImages.toolbar.drg.png" },
                { DrawMode.JobNinImage, "PluginImages.toolbar.nin.png" }, { DrawMode.JobSamImage, "PluginImages.toolbar.sam.png" },
                { DrawMode.JobRprImage, "PluginImages.toolbar.rpr.png" }, { DrawMode.JobVprImage, "PluginImages.toolbar.vpr.png" },
                { DrawMode.JobBrdImage, "PluginImages.toolbar.brd.png" }, { DrawMode.JobMchImage, "PluginImages.toolbar.mch.png" },
                { DrawMode.JobDncImage, "PluginImages.toolbar.dnc.png" },
                { DrawMode.JobBlmImage, "PluginImages.toolbar.blm.png" }, { DrawMode.JobSmnImage, "PluginImages.toolbar.smn.png" },
                { DrawMode.JobRdmImage, "PluginImages.toolbar.rdm.png" }, { DrawMode.JobPctImage, "PluginImages.toolbar.pct.png" },
            };

            this.toolDisplayNames = new Dictionary<DrawMode, string>
            {
                { DrawMode.StraightLine, "Line" },
                { DrawMode.Rectangle, "Rect" },
                { DrawMode.Triangle, "Triangle" },
                { DrawMode.TextTool, "TEXT" },
                { DrawMode.EmojiImage, "EMOJI" },
                { DrawMode.StatusIconPlaceholder, "Status" },
            };
        }

        public void DrawLeftToolbar()
        {
            DrawMode currentDrawMode = getCurrentDrawMode();
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float itemSpacing = ImGui.GetStyle().ItemSpacing.X;
            float btnWidthFull = availableWidth;
            float btnWidthHalf = (availableWidth - itemSpacing) / 2f;

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

            if (undoManager.CanUndo() || planUndoStack.Count > 0) { if (ImGui.Button("Undo", new Vector2(btnWidthFull, 0))) onUndo(); }
            else { using (ImRaii.Disabled()) ImGui.Button("Undo", new Vector2(btnWidthFull, 0)); }

            if (ImGui.Button("Clear All", new Vector2(btnWidthFull, 0))) onClearAll();

            if (plugin.PermissionManager.IsHost)
            {
                string lockLabel = isAllLocked ? "Unlock All Items" : "Lock All Items";
                if (ImGui.Button(lockLabel, new Vector2(btnWidthFull, 0)))
                {
                    isAllLocked = !isAllLocked;
                    plugin.PageController.SetAllLocked(pageManager, isAllLocked);
                }
            }

            if (ImGui.Button("Emoji", new Vector2(btnWidthFull, 0)))
            {
                onOpenEmojiPicker();
            }

            if (ImGui.Button("Add Image (URL)", new Vector2(btnWidthFull, 0)))
            {
                onImportBackgroundUrl();
            }
            ImGui.Separator();

            // Grid Controls Section
            bool gridVisible = getIsGridVisible();
            if (ImGui.Checkbox("Grid", ref gridVisible))
            {
                setIsGridVisible(gridVisible);
                // live sync toggle
                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.UpdateGridVisibility,
                        Data = new byte[] { (byte)(gridVisible ? 1 : 0) }
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
            }
            ImGui.SameLine();
            ImGui.Text("size");

            ImGui.SameLine();
            // Create a temporary integer variable for the UI widget
            int gridSizeInt = (int)getGridSize();

            // Calculate remaining width for the input box to fit on one line
            float labelWidth = ImGui.CalcTextSize("Pxl").X;
            float checkboxWidth = ImGui.GetItemRectSize().X;
            float spacing = ImGui.GetStyle().ItemSpacing.X * 2; // Spacing after checkbox and after label
            ImGui.SetNextItemWidth(availableWidth - checkboxWidth - labelWidth - spacing);

            // Use the temporary integer with InputInt
            if (ImGui.InputInt("##GridSpacingInput", ref gridSizeInt))
            {
                // Clamp the integer value
                int newSize = Math.Clamp(gridSizeInt, 10, 200);
                // Convert the final integer back to a float to save it
                setGridSize((float)newSize);
                // live sync network call
                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.UpdateGrid,
                        Data = BitConverter.GetBytes((float)newSize)
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
            }

            bool snapToGrid = getIsSnapToGrid();
            if (ImGui.Checkbox("Snap to Grid", ref snapToGrid))
            {
                setIsSnapToGrid(snapToGrid);
            }

            DrawToolButton("Laser Pointer", DrawMode.Laser, btnWidthFull);

            ImGui.Separator();

            Vector2 iconButtonSize = new(btnWidthHalf, 45 * ImGuiHelpers.GlobalScale);
            Vector2 popupIconButtonSize = new(32 * ImGuiHelpers.GlobalScale, 32 * ImGuiHelpers.GlobalScale);

            for (int i = 0; i < mainToolbarButtons.Count; i++)
            {
                var group = mainToolbarButtons[i];
                if (i > 0 && i % 2 != 0) ImGui.SameLine();
                DrawMode activeModeInGroup = activeSubModeMap.GetValueOrDefault(group.Primary, group.Primary);
                string activePath = iconPaths.GetValueOrDefault(activeModeInGroup, "");
                var tex = activePath != "" ? TextureManager.GetTexture(activePath) : null;
                var drawList = ImGui.GetWindowDrawList();

                bool isGroupActive = currentDrawMode == group.Primary || (group.SubModes.Any() && group.SubModes.Contains(currentDrawMode));

                using (isGroupActive ? ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]) : null)
                {
                    if (ImGui.Button($"##{group.Primary}", iconButtonSize))
                    {
                        if (group.Primary == DrawMode.StatusIconPlaceholder)
                        {
                            onOpenStatusSearchPopup();
                        }
                        else if(group.SubModes.Any())
                        {
                            setCurrentDrawMode(activeModeInGroup);
                        }
                        else
                        {
                            setCurrentDrawMode(group.Primary);
                        }
                    }
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    var center = (min + max) / 2;
                    if (tex != null) drawList.AddImage(tex.Handle, min, max);
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
                        else if (group.Primary == DrawMode.TextTool)
                        {
                            var activeToolName = toolDisplayNames.GetValueOrDefault(activeModeInGroup, "TEXT");
                            var textSize = ImGui.CalcTextSize(activeToolName);
                            drawList.AddText(new Vector2(center.X - textSize.X / 2, center.Y - textSize.Y / 2), ImGui.GetColorU32(ImGuiCol.Text), activeToolName);
                        }

                        if (group.Primary != DrawMode.TextTool)
                        {
                            var activeToolName = toolDisplayNames.GetValueOrDefault(activeModeInGroup, activeModeInGroup.ToString().Replace("Image", ""));
                            var textSize = ImGui.CalcTextSize(activeToolName);
                            drawList.AddText(new Vector2(center.X - textSize.X / 2, max.Y - textSize.Y - (iconButtonSize.Y * 0.1f)), ImGui.GetColorU32(ImGuiCol.Text), activeToolName);
                        }
                    }
                    // Draw Caret for SubModes
                    if (group.SubModes.Any())
                    {
                        var arrowSize = 6f * ImGuiHelpers.GlobalScale;
                        var padding = 4f * ImGuiHelpers.GlobalScale;

                        Vector2 p1 = new Vector2(max.X - arrowSize - padding, max.Y - arrowSize - padding);
                        Vector2 p2 = new Vector2(max.X - padding, max.Y - arrowSize - padding);
                        Vector2 p3 = new Vector2(max.X - arrowSize * 0.5f - padding, max.Y - padding);

                        drawList.AddTriangleFilled(p1, p2, p3, ImGui.GetColorU32(ImGuiCol.Text));
                    }
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(group.Tooltip);

                if (group.SubModes.Any() && ImGui.BeginPopupContextItem($"popup_{group.Primary}", ImGuiPopupFlags.MouseButtonLeft))
                {
                    // Helper function to draw a single item, keeping the logic DRY
                    void DrawPopupItem(DrawMode subMode)
                    {
                        string subPath = iconPaths.GetValueOrDefault(subMode, "");
                        var subTex = subPath != "" ? TextureManager.GetTexture(subPath) : null;
                        if (subTex != null)
                        {
                            if (ImGui.ImageButton(subTex.Handle, popupIconButtonSize))
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

                    // Special Layout for Party Icons (4, 4, 5 Grid)
                    if (group.Primary == DrawMode.Party1Image && group.SubModes.Count >= 13)
                    {
                        ImGui.BeginGroup(); // Column 1 (1-4)
                        for (int k = 0; k < 4; k++) DrawPopupItem(group.SubModes[k]);
                        ImGui.EndGroup();

                        ImGui.SameLine();

                        ImGui.BeginGroup(); // Column 2 (5-8)
                        for (int k = 4; k < 8; k++) DrawPopupItem(group.SubModes[k]);
                        ImGui.EndGroup();

                        ImGui.SameLine();

                        ImGui.BeginGroup(); // Column 3 (Bind/Ignore)
                        for (int k = 8; k < group.SubModes.Count; k++) DrawPopupItem(group.SubModes[k]);
                        ImGui.EndGroup();
                    }
                    else
                    {
                        // Standard Vertical List for other tools
                        foreach (var subMode in group.SubModes)
                        {
                            DrawPopupItem(subMode);
                        }
                    }
                    ImGui.EndPopup();
                }
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

            ImGui.Separator();

            // New Properties Toggle Button
            if (ImGui.Button("Properties Window", new Vector2(btnWidthFull, 0)))
            {
                plugin.TogglePropertiesUI();
            }

            float availableHeight = ImGui.GetContentRegionAvail().Y;
            float bugReportButtonHeight = ImGui.CalcTextSize("Bug report/\nFeature request").Y + ImGui.GetStyle().FramePadding.Y * 2.0f;
            float kofiButtonHeight = ImGui.GetFrameHeight();
            float footerButtonsTotalHeight = bugReportButtonHeight + kofiButtonHeight + ImGui.GetStyle().ItemSpacing.Y;

            if (availableHeight > footerButtonsTotalHeight)
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + availableHeight - footerButtonsTotalHeight);
            }

            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.1f, 0.4f, 0.1f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.1f, 0.5f, 0.1f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.6f, 0.2f, 1.0f)))
            {
                if (ImGui.Button("Bug report/\nFeature request", new Vector2(btnWidthFull, bugReportButtonHeight)))
                {
                    Util.OpenLink("https://github.com/rail2025/AetherDraw/issues");
                }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Opens the GitHub Issues page in your browser.");
            }

            string kofiButtonText = "Support on Ko-Fi";
            uint kofiButtonColor = 0xFF312B;
            using (ImRaii.PushColor(ImGuiCol.Button, 0xFF000000 | kofiButtonColor))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive, 0xDD000000 | kofiButtonColor))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, 0xAA000000 | kofiButtonColor))
            {
                if (ImGui.Button(kofiButtonText, new Vector2(btnWidthFull, 0)))
                {
                    Util.OpenLink("https://ko-fi.com/rail2025");
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Buy me a coffee if this plugin drew a smile on your screen!");
                }
            }
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
