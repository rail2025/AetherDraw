using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace AetherDraw
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public bool IsMainWindowMovable { get; set; } = true;
        public float DefaultBrushColorR { get; set; } = 1.0f;
        public float DefaultBrushColorG { get; set; } = 1.0f;
        public float DefaultBrushColorB { get; set; } = 1.0f;
        public float DefaultBrushColorA { get; set; } = 1.0f;
        public float DefaultBrushThickness { get; set; } = 4.0f;

        public bool IsGridVisible { get; set; } = true;
        public float GridSize { get; set; } = 40f;
        public bool IsSnapToGrid { get; set; } = true;

        public string AccountKey { get; set; } = string.Empty;


        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface; // Renamed from _pluginInterface

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface?.SavePluginConfig(this);
        }
    }
}
