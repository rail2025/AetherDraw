// AetherDraw/DrawingLogic/BaseDrawable.cs
using System;
using System.Drawing; // Required for RectangleF
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SixLabors.ImageSharp.Processing;

namespace AetherDraw.DrawingLogic
{
    public abstract class BaseDrawable
    {
        /// <summary>
        /// Gets or sets the Unique Identifier for this drawable object.
        /// It is assigned once upon creation but can be overwritten during deserialization.
        /// </summary>
        public Guid UniqueId { get; set; }

        public DrawMode ObjectDrawMode { get; set; } // Changed from protected set
        public Vector4 Color { get; set; }
        public float Thickness { get; set; }
        public bool IsFilled { get; set; }
        public bool IsPreview { get; set; }
        public bool IsSelected { get; set; } = false;
        public bool IsHovered { get; set; } = false;

        /// <summary>
        /// Protected constructor for BaseDrawable.
        /// Initializes the UniqueId for the drawable object.
        /// </summary>
        protected BaseDrawable()
        {
            this.UniqueId = Guid.NewGuid();
        }

        // Abstract method for ImGui drawing.
        public abstract void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen);

        // Abstract method for drawing to an ImageSharp context.
        public abstract void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale);

        /// <summary>
        /// A required method for all shapes to calculate and return their axis-aligned bounding box.
        /// The bounding box is the smallest non-rotated rectangle that completely encloses the shape.
        /// </summary>
        /// <returns>A RectangleF representing the bounding box in logical (unscaled) coordinates.</returns>
        public abstract RectangleF GetBoundingBox();

        public abstract bool IsHit(Vector2 queryPointOrEraserCenterRelative, float hitThresholdOrEraserRadius = 5.0f);
        public abstract BaseDrawable Clone();
        public abstract void Translate(Vector2 delta);

        public virtual void UpdatePreview(Vector2 currentPointRelative) { }

        protected void CopyBasePropertiesTo(BaseDrawable target)
        {
            // UniqueId is not copied; the clone gets its own new UniqueId upon its construction.
            target.ObjectDrawMode = this.ObjectDrawMode;
            target.Color = this.Color;
            target.Thickness = this.Thickness;
            target.IsFilled = this.IsFilled;
            target.IsPreview = false; // Cloned objects are generally not previews by default
            target.IsSelected = false;
            target.IsHovered = false;
        }
    }
}
