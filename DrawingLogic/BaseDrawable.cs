using System.Numerics;
using System;  // For MathF
using ImGuiNET; // For ImDrawListPtr

namespace AetherDraw.DrawingLogic
{
    // The DrawMode enum is now in this namespace (from DrawMode.cs)

    public abstract class BaseDrawable
    {
        public DrawMode ObjectDrawMode { get; protected set; }
        public Vector4 Color { get; set; }
        public float Thickness { get; set; }
        public bool IsFilled { get; set; }
        public bool IsPreview { get; set; }
        public bool IsSelected { get; set; } = false;
        public bool IsHovered { get; set; } = false;

        // Abstract methods to be implemented by derived classes
        public abstract void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen);
        public abstract bool IsHit(Vector2 queryPointOrEraserCenterRelative, float hitThresholdOrEraserRadius = 5.0f);
        public abstract BaseDrawable Clone();
        public abstract void Translate(Vector2 delta);

        // Virtual method for live preview updates during drawing (e.g., resizing a rectangle)
        public virtual void UpdatePreview(Vector2 currentPointRelative) { } // Default empty implementation is fine for many shapes

        // Helper method for cloning base properties
        protected void CopyBasePropertiesTo(BaseDrawable target)
        {
            target.ObjectDrawMode = this.ObjectDrawMode;
            target.Color = this.Color;
            target.Thickness = this.Thickness;
            target.IsFilled = this.IsFilled;
            target.IsPreview = false; // Important: Cloned objects are typically not previews
            target.IsSelected = false; // Cloned objects are not selected by default
            target.IsHovered = false;  // Cloned objects are not hovered by default
        }
    }
}
