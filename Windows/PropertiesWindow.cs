using AetherDraw.DrawingLogic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace AetherDraw.Windows
{
    public class PropertiesWindow : Window, IDisposable
    {
        private readonly Plugin plugin;

        private Guid? renamingId = null;
        private string renamingBuffer = "";

        // from ToolbarDrawer to ensure consistency in presets
        private static readonly float[] ThicknessPresets = { 1.5f, 4f, 7f, 10f };
        private static readonly Vector4[] ColorPalette = {
            new(1.0f,1.0f,1.0f,1.0f), new(0.0f,0.0f,0.0f,1.0f),
            new(1.0f,0.0f,0.0f,1.0f), new(0.0f,1.0f,0.0f,1.0f),
            new(0.0f,0.0f,1.0f,1.0f), new(1.0f,1.0f,0.0f,1.0f),
            new(1.0f,0.0f,1.0f,1.0f), new(0.0f,1.0f,1.0f,1.0f),
            new(0.5f,0.5f,0.5f,1.0f), new(0.8f,0.4f,0.0f,1.0f)
        };

        public PropertiesWindow(Plugin plugin) : base("Properties###AetherDrawProperties")
        {
            this.plugin = plugin;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(250, 300),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
        }

        public void Dispose() { }

        // Define the mapping of Roles to their Jobs
        private static readonly Dictionary<DrawMode, List<DrawMode>> RoleToJobMap = new()
        {
            { DrawMode.RoleTankImage,   new List<DrawMode> { DrawMode.JobPldImage, DrawMode.JobWarImage, DrawMode.JobDrkImage, DrawMode.JobGnbImage } },
            { DrawMode.RoleHealerImage, new List<DrawMode> { DrawMode.JobWhmImage, DrawMode.JobSchImage, DrawMode.JobAstImage, DrawMode.JobSgeImage } },
            { DrawMode.RoleMeleeImage,  new List<DrawMode> { DrawMode.JobMnkImage, DrawMode.JobDrgImage, DrawMode.JobNinImage, DrawMode.JobSamImage, DrawMode.JobRprImage, DrawMode.JobVprImage } },
            { DrawMode.RoleRangedImage, new List<DrawMode> { DrawMode.JobBrdImage, DrawMode.JobMchImage, DrawMode.JobDncImage } },
            { DrawMode.RoleCasterImage, new List<DrawMode> { DrawMode.JobBlmImage, DrawMode.JobSmnImage, DrawMode.JobRdmImage, DrawMode.JobPctImage } }
        };

        // Helper to find which list a specific DrawMode belongs to
        private List<DrawMode>? GetJobListForMode(DrawMode currentMode)
        {
            // Check if it is a Role itself
            if (RoleToJobMap.ContainsKey(currentMode)) return RoleToJobMap[currentMode];

            // Check if it is a Job within a Role
            foreach (var kvp in RoleToJobMap)
            {
                if (kvp.Value.Contains(currentMode)) return kvp.Value;
            }
            return null;
        }

        private string GetIconPath(DrawMode mode)
        {
            // Quick lookup helper - ideally this would be shared from ToolbarDrawer but we can reconstruct the pattern easily
            if (mode == DrawMode.RoleCasterImage) return "PluginImages.toolbar.caster.png";
            // Map the jobs
            string name = mode.ToString().Replace("Job", "").Replace("Image", "").ToLower();
            return $"PluginImages.toolbar.{name}.png";
        }

        public override void Draw()
        {
            var mainWindow = plugin.MainWindow;
            if (mainWindow == null || !mainWindow.IsOpen)
            {
                this.IsOpen = false;
                return;
            }

            var selected = mainWindow.SelectedDrawables;
            if (selected == null || selected.Count == 0)
            {
                ImGui.TextDisabled("No objects selected.");
                return;
            }

            // COMMON PROPERTIES

            // Lock Status
            bool isLocked = selected.All(d => d.IsLocked);
            if (ImGui.Checkbox("Locked", ref isLocked))
            {
                foreach (var d in selected) d.IsLocked = isLocked;
                // Commit immediately
                mainWindow.UndoManager.RecordAction(mainWindow.PageManager.GetCurrentPageDrawables(), isLocked ? "Lock Objects" : "Unlock Objects");
                mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable>(selected));
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Locked objects cannot be selected or moved on the canvas.");

            // --- JOB SWAP UI ---
            // Check if we have a single selected item that is a Role or Job
            if (selected.Count == 1 && selected[0] is DrawableImage dImg)
            {
                var jobList = GetJobListForMode(dImg.ObjectDrawMode);
                if (jobList != null)
                {
                    ImGui.Separator();
                    ImGui.Text("Swap Job Icon");

                    float availW = ImGui.GetContentRegionAvail().X;
                    float gap = ImGui.GetStyle().ItemSpacing.X;
                    // spacing : (Width - (TotalGapSpace)) / ItemCount
                    // If 4 items, there are 3 gaps.
                    float btnSize = (availW - (gap * (jobList.Count - 1))) / jobList.Count;

                    // Sanity check to prevent huge buttons if list is small (e.g., 1 item)
                    btnSize = Math.Min(btnSize, 40f * ImGuiHelpers.GlobalScale);

                    Vector2 btnVec = new Vector2(btnSize, btnSize);

                    for (int i = 0; i < jobList.Count; i++)
                    {
                        var jobMode = jobList[i];
                        // Only add SameLine if it's NOT the first item
                        if (i > 0) ImGui.SameLine();

                        var tex = TextureManager.GetTexture(GetIconPath(jobMode));
                        if (tex != null)
                        {
                            if (ImGui.ImageButton(tex.Handle, btnVec))
                            {
                                dImg.ObjectDrawMode = jobMode;
                                dImg.ImageResourcePath = GetIconPath(jobMode);

                                mainWindow.UndoManager.RecordAction(mainWindow.PageManager.GetCurrentPageDrawables(), "Swap Job Icon");
                                mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable>(selected));
                            }
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip(jobMode.ToString().Replace("Job", "").Replace("Image", ""));
                        }
                    }
                    // Remove trailing SameLine
                    ImGui.NewLine();
                }
            }
            // -------------------

            ImGui.Separator();

            // Color Palette Buttons
            ImGui.Text("Color");
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float itemSpacing = ImGui.GetStyle().ItemSpacing.X;
            int colorsPerRow = 5;
            float smallColorButtonSize = (availableWidth - (itemSpacing * (colorsPerRow - 1))) / colorsPerRow;
            Vector2 colorButtonDimensions = new(smallColorButtonSize, smallColorButtonSize);

            for (int i = 0; i < ColorPalette.Length; i++)
            {
                if (i > 0 && i % colorsPerRow != 0) ImGui.SameLine();

                ImGui.PushID(i);
                // Use ColorButton
                if (ImGui.ColorButton($"##PropColor{i}", ColorPalette[i], ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, colorButtonDimensions))
                {
                    foreach (var d in selected) d.Color = ColorPalette[i];

                    mainWindow.UndoManager.RecordAction(mainWindow.PageManager.GetCurrentPageDrawables(), "Change Color");
                    mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable>(selected));
                }
                ImGui.PopID();

                // Highlight
                if (selected[0].Color == ColorPalette[i])
                {
                    ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), 0, ImDrawFlags.None, 2f);
                }
            }

            // Opacity Slider
            float currentAlpha = selected[0].Color.W;
            ImGui.SetNextItemWidth(availableWidth);
            if (ImGui.SliderFloat("##Opacity", ref currentAlpha, 0.0f, 1.0f, "Opacity: %.2f"))
            {
                foreach (var d in selected)
                {
                    var col = d.Color;
                    col.W = currentAlpha;
                    d.Color = col;
                }
            }

            // Only commit changes when the user releases the slider to prevent network spam
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                mainWindow.UndoManager.RecordAction(mainWindow.PageManager.GetCurrentPageDrawables(), "Change Opacity");
                mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable>(selected));
            }

            ImGui.Separator();

            // Thickness Buttons
            ImGui.Text("Thickness");
            float thicknessButtonWidth = (availableWidth - itemSpacing * (ThicknessPresets.Length - 1)) / ThicknessPresets.Length;

            foreach (var t in ThicknessPresets)
            {
                if (t != ThicknessPresets[0]) ImGui.SameLine();

                bool isSelectedThickness = Math.Abs(selected[0].Thickness - t) < 0.01f;

                // Use standard Buttons
                if (ImGui.Button($"{t:0}##PropThick{t}", new Vector2(thicknessButtonWidth, 0)))
                {
                    foreach (var d in selected)
                    {
                        d.Thickness = t;
                        if (d is DrawableArrow arrow) arrow.UpdateArrowheadSize();
                    }
                    mainWindow.UndoManager.RecordAction(mainWindow.PageManager.GetCurrentPageDrawables(), "Change Thickness");
                    mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable>(selected));
                }

                if (isSelectedThickness)
                {
                    ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), 0, ImDrawFlags.None, 2f);
                }
            }

            ImGui.Separator();

            // Fill
            bool isFilled = selected[0].IsFilled;
            if (ImGui.Checkbox("Filled", ref isFilled))
            {
                foreach (var d in selected)
                {
                    d.IsFilled = isFilled;
                    // Logic from MainWindow to adjust alpha when toggling fill
                    if (d.Color.W < 1.0f || isFilled)
                    {
                        var tempColor = d.Color;
                        tempColor.W = isFilled ? 0.4f : 1.0f;
                        d.Color = tempColor;
                    }
                }
                mainWindow.UndoManager.RecordAction(mainWindow.PageManager.GetCurrentPageDrawables(), "Change Fill");
                mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable>(selected));
            }

            // context aware content

            if (selected.Count == 1 && selected[0] is DrawableText textObj)
            {
                ImGui.Separator();
                ImGui.Text("Text Properties");

                float fontSize = textObj.FontSize;
                ImGui.SetNextItemWidth(availableWidth);
                if (ImGui.DragFloat("##FontSize", ref fontSize, 0.5f, 1.0f, 200.0f, "Size: %.1f"))
                {
                    textObj.FontSize = fontSize;
                }
                // Only commit when the user releases the slider
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    mainWindow.UndoManager.RecordAction(mainWindow.PageManager.GetCurrentPageDrawables(), "Change Font Size");
                    mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable>(selected));
                }

                string text = textObj.RawText;
                if (ImGui.InputTextMultiline("##Content", ref text, 1024, new Vector2(-1, 100 * ImGuiHelpers.GlobalScale)))
                {
                    textObj.RawText = text;
                }
                // Only commit when the user finishes editing (loses focus)
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    mainWindow.UndoManager.RecordAction(mainWindow.PageManager.GetCurrentPageDrawables(), "Edit Text");
                    mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable>(selected));
                }
            }
            ImGui.Separator();
            DrawLayerList(mainWindow);
        }

        private unsafe void DrawLayerList(MainWindow mainWindow)
        {
            ImGui.Text("Layers");
            var drawables = mainWindow.PageManager.GetCurrentPageDrawables();

            // Create a child window for scrolling
            if (ImGui.BeginChild("##LayerList", new Vector2(0, 0), true))
            {
                // Iterate backwards so top-most elements (highest Z-index) appear at the top of the list
                for (int i = drawables.Count - 1; i >= 0; i--)
                {
                    var item = drawables[i];
                    ImGui.PushID(item.UniqueId.ToString());

                    bool isSelected = mainWindow.SelectedDrawables.Contains(item);
                    string displayName = !string.IsNullOrEmpty(item.Name) ? item.Name : item.ObjectDrawMode.ToString();

                    // Renaming Mode
                    if (renamingId == item.UniqueId)
                    {
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.InputText("##Rename", ref renamingBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                        {
                            item.Name = renamingBuffer;
                            renamingId = null;
                            mainWindow.UndoManager.RecordAction(drawables, "Rename Object");
                            mainWindow.InteractionHandler.CommitObjectChanges(new List<BaseDrawable> { item });
                        }
                        else if (ImGui.IsItemDeactivated() && !ImGui.IsItemActive())
                        {
                            renamingId = null;
                        }
                    }
                    // Normal Selectable Mode
                    else
                    {
                        if (ImGui.Selectable($"{displayName}##{i}", isSelected, ImGuiSelectableFlags.AllowDoubleClick))
                        {
                            if (ImGui.GetIO().KeyCtrl)
                            {
                                if (isSelected) mainWindow.SelectedDrawables.Remove(item);
                                else mainWindow.SelectedDrawables.Add(item);
                            }
                            else
                            {
                                mainWindow.SelectedDrawables.Clear();
                                mainWindow.SelectedDrawables.Add(item);
                            }
                        }

                        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            renamingId = item.UniqueId;
                            renamingBuffer = item.Name ?? item.ObjectDrawMode.ToString();
                        }

                        // Drag and Drop: Source
                        if (ImGui.BeginDragDropSource())
                        {
                            int sourceIndex = i;
                            ImGui.SetDragDropPayload("LAYER_REORDER", new ReadOnlySpan<byte>(&sourceIndex, sizeof(int)), ImGuiCond.None);
                            ImGui.Text(displayName);
                            ImGui.EndDragDropSource();
                        }

                        // Drag and Drop: Target
                        if (ImGui.BeginDragDropTarget())
                        {
                            var payload = ImGui.AcceptDragDropPayload("LAYER_REORDER");

                            bool isPayloadValid = false;
                            // Safe check: verify the payload pointer itself is not null before accessing fields
                            if (&payload != null && (*(IntPtr*)&payload) != IntPtr.Zero)
                            {
                                isPayloadValid = true;
                            }

                            if (isPayloadValid && payload.Data != null)
                            {
                                int sourceIndex = *(int*)payload.Data;
                                if (sourceIndex != i)
                                {
                                    var itemToMove = drawables[sourceIndex];
                                    drawables.RemoveAt(sourceIndex);
                                    drawables.Insert(i, itemToMove);

                                    mainWindow.UndoManager.RecordAction(drawables, "Reorder Layer");

                                    var networkPayload = new AetherDraw.Networking.NetworkPayload
                                    {
                                        PageIndex = mainWindow.PageManager.GetCurrentPageIndex(),
                                        Action = AetherDraw.Networking.PayloadActionType.ReplacePage,
                                        Data = AetherDraw.Serialization.DrawableSerializer.SerializePageToBytes(drawables)
                                    };
                                    _ = plugin.NetworkManager.SendStateUpdateAsync(networkPayload);

                                    ImGui.EndDragDropTarget();
                                    // Just break the loop/return to redraw next frame.
                                    ImGui.PopID();
                                    ImGui.EndChild();
                                    return;
                                }
                            }
                            ImGui.EndDragDropTarget();
                        }
                    } 

                    ImGui.PopID();
                }

                ImGui.EndChild(); 
            }
        }

    }
}
