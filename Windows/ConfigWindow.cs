using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Dalamud.Interface.Utility; // Added for ImGuiHelpers

namespace AetherDraw.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Plugin plugin;
        private readonly Configuration configuration;

        public ConfigWindow(Plugin plugin) : base("AetherDraw Settings###AetherDrawConfigWindow")
        {
            // Scale the MinimumSize constraints.
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(300f * ImGuiHelpers.GlobalScale, 150f * ImGuiHelpers.GlobalScale),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
            this.RespectCloseHotkey = true;

            this.plugin = plugin;
            this.configuration = plugin.Configuration;
        }

        public void Dispose()
        {
            // No specific disposable resources owned directly by ConfigWindow to manage here.
        }

        public override void Draw()
        {
            ImGui.Text("AetherDraw Configuration");
            ImGui.Spacing();

            bool tempMovable = this.configuration.IsMainWindowMovable;
            if (ImGui.Checkbox("Main Window Movable", ref tempMovable))
            {
                this.configuration.IsMainWindowMovable = tempMovable;
                this.configuration.Save();
            }

            ImGui.Text("Default Brush Color:");
            Vector4 color = new Vector4(
                this.configuration.DefaultBrushColorR,
                this.configuration.DefaultBrushColorG,
                this.configuration.DefaultBrushColorB,
                this.configuration.DefaultBrushColorA
            );
            if (ImGui.ColorEdit4("##DefaultBrushColor", ref color))
            {
                this.configuration.DefaultBrushColorR = color.X;
                this.configuration.DefaultBrushColorG = color.Y;
                this.configuration.DefaultBrushColorB = color.Z;
                this.configuration.DefaultBrushColorA = color.W;
                this.configuration.Save();
            }

            float tempThickness = this.configuration.DefaultBrushThickness;
            // DragFloat's speed and min/max values are logical, not usually scaled by GlobalScale directly.
            // The visual size of the DragFloat widget itself is handled by ImGui's scaling.
            if (ImGui.DragFloat("Default Brush Thickness", ref tempThickness, 0.1f, 1.0f, 50.0f))
            {
                this.configuration.DefaultBrushThickness = tempThickness;
                this.configuration.Save();
            }
        }
    }
}
