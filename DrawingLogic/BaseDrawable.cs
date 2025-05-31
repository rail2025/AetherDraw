// AetherDraw/DrawingLogic/BaseDrawable.cs
using System.Numerics;
using ImGuiNET; // For ImDrawListPtr in existing Draw method
using SixLabors.ImageSharp.Processing; // For IImageProcessingContext
// Potentially SixLabors.ImageSharp and SixLabors.ImageSharp.PixelFormats if needed directly here later

namespace AetherDraw.DrawingLogic
{
    public abstract class BaseDrawable
    {
        public DrawMode ObjectDrawMode { get; protected set; }
        public Vector4 Color { get; set; }
        public float Thickness { get; set; }
        public bool IsFilled { get; set; }
        public bool IsPreview { get; set; }
        public bool IsSelected { get; set; } = false;
        public bool IsHovered { get; set; } = false;

        // Abstract method for ImGui drawing
        public abstract void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen);

        // NEW abstract method for drawing to an ImageSharp context
        public abstract void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale);

        public abstract bool IsHit(Vector2 queryPointOrEraserCenterRelative, float hitThresholdOrEraserRadius = 5.0f);
        public abstract BaseDrawable Clone();
        public abstract void Translate(Vector2 delta);

        public virtual void UpdatePreview(Vector2 currentPointRelative) { }

        protected void CopyBasePropertiesTo(BaseDrawable target)
        {
            target.ObjectDrawMode = this.ObjectDrawMode;
            target.Color = this.Color;
            target.Thickness = this.Thickness;
            target.IsFilled = this.IsFilled;
            target.IsPreview = false;
            target.IsSelected = false;
            target.IsHovered = false;
        }
    }
}
