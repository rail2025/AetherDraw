using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace AetherDraw.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Plugin plugin; // Renamed for convention
        private readonly Configuration configuration; // Renamed for convention

        public ConfigWindow(Plugin plugin) : base("AetherDraw Settings###AetherDrawConfigWindow")
        {
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(300, 150),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
            this.RespectCloseHotkey = true;

            this.plugin = plugin; // Use lowercase field name
            this.configuration = plugin.Configuration; // Use lowercase field name
        }

        public void Dispose()
        {
        }

        public override void Draw()
        {
            ImGui.Text("AetherDraw Configuration");
            ImGui.Spacing();

            // Corrected Checkbox for IsMainWindowMovable using the field 'configuration'
            bool tempMovable = this.configuration.IsMainWindowMovable;
            if (ImGui.Checkbox("Main Window Movable", ref tempMovable))
            {
                this.configuration.IsMainWindowMovable = tempMovable;
                this.configuration.Save();
            }

            ImGui.Text("Default Brush Color:");
            // Using the field 'configuration'
            Vector4 color = new Vector4(this.configuration.DefaultBrushColorR, this.configuration.DefaultBrushColorG, this.configuration.DefaultBrushColorB, this.configuration.DefaultBrushColorA);
            if (ImGui.ColorEdit4("##DefaultBrushColor", ref color))
            {
                this.configuration.DefaultBrushColorR = color.X;
                this.configuration.DefaultBrushColorG = color.Y;
                this.configuration.DefaultBrushColorB = color.Z;
                this.configuration.DefaultBrushColorA = color.W;
                this.configuration.Save();
            }

            // Corrected DragFloat for DefaultBrushThickness using the field 'configuration'
            float tempThickness = this.configuration.DefaultBrushThickness;
            if (ImGui.DragFloat("Default Brush Thickness", ref tempThickness, 0.1f, 1.0f, 50.0f))
            {
                this.configuration.DefaultBrushThickness = tempThickness;
                this.configuration.Save();
            }
        }
    }
}
