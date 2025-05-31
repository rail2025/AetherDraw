// AetherDraw/DrawingLogic/DrawableCone.cs
using System;
using System.Numerics;
using ImGuiNET; // For ImDrawListPtr in existing Draw method
using Dalamud.Interface.Utility; // For ImGuiHelpers

// ImageSharp using statements
using SixLabors.ImageSharp; // For PointF, Color, Matrix3x2 (from System.Numerics, but used with PointF)
using SixLabors.ImageSharp.PixelFormats; // For Rgba32 if needed directly
using SixLabors.ImageSharp.Processing; // For IImageProcessingContext
using SixLabors.ImageSharp.Drawing; // For PathBuilder, Pens, IPath, EllipsePolygon etc.
using SixLabors.ImageSharp.Drawing.Processing; // For Fill, Draw extension methods

namespace AetherDraw.DrawingLogic
{
    public class DrawableCone : BaseDrawable
    {
        // Logical, unscaled position of the cone's apex.
        public Vector2 ApexRelative { get; set; }
        // Defines the unrotated length and direction of the cone's axis from the Apex.
        // The actual base center is calculated by rotating this vector around ApexRelative by RotationAngle.
        public Vector2 BaseCenterRelative { get; set; }
        // Rotation angle in radians around the ApexRelative.
        public float RotationAngle { get; set; } = 0f;

        // Factor determining the cone's base width relative to its height.
        public static readonly float ConeWidthFactor = 0.3f;

        public DrawableCone(Vector2 apexRelative, Vector4 color, float unscaledThickness, bool isFilled)
        {
            this.ObjectDrawMode = DrawMode.Cone;
            this.ApexRelative = apexRelative;
            this.BaseCenterRelative = apexRelative; // Initially, base is at apex, resulting in a degenerate cone.
            this.Color = color;
            this.Thickness = unscaledThickness;
            this.IsFilled = isFilled;
            this.IsPreview = true;
        }

        // Updates the cone's geometry during preview drawing (e.g., when dragging to define the base).
        public override void UpdatePreview(Vector2 newBaseCenterRelativeWhileDrawing)
        {
            this.BaseCenterRelative = newBaseCenterRelativeWhileDrawing;
        }

        // Calculates the cone's vertices in a local coordinate system where the apex is at (0,0)
        // and the cone's axis aligns with the vector from ApexRelative to BaseCenterRelative (before rotation).
        // Returns:
        //  localApex: Always (0,0) in this local system.
        //  localBaseVert1: One vertex of the cone's base.
        //  localBaseVert2: The other vertex of the cone's base.
        //  localBaseCenterFromApexVec: The vector from the apex to the center of the base, unrotated.
        private (Vector2 localApex, Vector2 localBaseVert1, Vector2 localBaseVert2, Vector2 localBaseCenterFromApexVec) GetLocalUnrotatedVertices()
        {
            Vector2 localBaseCenterFromApexVec = this.BaseCenterRelative - this.ApexRelative;
            float height = localBaseCenterFromApexVec.Length();

            // If height is negligible, the cone is degenerate (effectively a point).
            // Return zero vectors for base vertices to indicate this.
            if (height < 0.1f)
            {
                return (Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero);
            }

            Vector2 direction = Vector2.Normalize(localBaseCenterFromApexVec); // Direction from apex to base center
            float baseHalfWidth = height * ConeWidthFactor; // Calculate half-width of the base

            // Calculate perpendicular vector to the direction vector to find base vertices
            Vector2 perpendicular = new Vector2(direction.Y, -direction.X);
            Vector2 localBaseVert1 = localBaseCenterFromApexVec + perpendicular * baseHalfWidth;
            Vector2 localBaseVert2 = localBaseCenterFromApexVec - perpendicular * baseHalfWidth;

            return (Vector2.Zero, localBaseVert1, localBaseVert2, localBaseCenterFromApexVec);
        }

        // Draws the cone on the ImGui canvas.
        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var displayColorVec = IsSelected ? new Vector4(1, 1, 0, 1) : (IsHovered ? new Vector4(0, 1, 1, 1) : Color);
            uint displayColor = ImGui.GetColorU32(displayColorVec);
            float scaledThickness = Math.Max(1f, Thickness * ImGuiHelpers.GlobalScale);
            if (IsSelected || IsHovered) scaledThickness += 2f * ImGuiHelpers.GlobalScale;

            var (localApex, localBaseVert1, localBaseVert2, _) = GetLocalUnrotatedVertices();

            if (localBaseVert1 == Vector2.Zero && localBaseVert2 == Vector2.Zero && IsPreview)
            {
                Vector2 screenApexForDot = (this.ApexRelative * ImGuiHelpers.GlobalScale) + canvasOriginScreen;
                drawList.AddCircleFilled(screenApexForDot, scaledThickness / 2f + (1f * ImGuiHelpers.GlobalScale), displayColor);
                return;
            }

            // Transformation: Rotate around local origin (0,0), then translate by ApexRelative, scale, then translate to screen.
            Matrix3x2 transform =
                Matrix3x2.CreateRotation(this.RotationAngle) * Matrix3x2.CreateTranslation(this.ApexRelative) * Matrix3x2.CreateScale(ImGuiHelpers.GlobalScale) *
                Matrix3x2.CreateTranslation(canvasOriginScreen);

            Vector2 screenApex = Vector2.Transform(localApex, transform);
            Vector2 screenB1 = Vector2.Transform(localBaseVert1, transform);
            Vector2 screenB2 = Vector2.Transform(localBaseVert2, transform);

            if (IsFilled)
            {
                drawList.AddTriangleFilled(screenApex, screenB1, screenB2, displayColor);
            }
            else
            {
                drawList.AddLine(screenApex, screenB1, displayColor, scaledThickness);
                drawList.AddLine(screenApex, screenB2, displayColor, scaledThickness);
                drawList.AddLine(screenB1, screenB2, displayColor, scaledThickness);
            }
        }

        // Draws the cone to an ImageSharp context for image export.
        public override void DrawToImage(IImageProcessingContext context, Vector2 canvasOriginInOutputImage, float currentGlobalScale)
        {
            var imageSharpColor = SixLabors.ImageSharp.Color.FromRgba(
                (byte)(Color.X * 255), (byte)(Color.Y * 255),
                (byte)(Color.Z * 255), (byte)(Color.W * 255)
            );
            float scaledThickness = Math.Max(1f, this.Thickness * currentGlobalScale);

            var (localApex, localBaseVert1, localBaseVert2, _) = GetLocalUnrotatedVertices();

            // If cone is degenerate, do not draw to image (or draw a small dot if preferred for previews, but usually not for final export).
            if (localBaseVert1 == Vector2.Zero && localBaseVert2 == Vector2.Zero)
            {
                return;
            }

            // Create transformation matrix for ImageSharp rendering.
            // 1. Rotate around the local origin (localApex is 0,0).
            // 2. Translate by ApexRelative to position the rotated shape in logical space.
            // 3. Scale by currentGlobalScale.
            // 4. Translate by canvasOriginInOutputImage to position on the final image.
            Matrix3x2 transformMatrix =
                Matrix3x2.CreateRotation(this.RotationAngle) * Matrix3x2.CreateTranslation(this.ApexRelative) * Matrix3x2.CreateScale(currentGlobalScale) * Matrix3x2.CreateTranslation(canvasOriginInOutputImage);

            PointF screenApex = PointF.Transform(localApex, transformMatrix);
            PointF screenB1 = PointF.Transform(localBaseVert1, transformMatrix);
            PointF screenB2 = PointF.Transform(localBaseVert2, transformMatrix);

            var pathBuilder = new PathBuilder();
            pathBuilder.AddLine(screenApex, screenB1); // Path from Apex to Base1
            pathBuilder.AddLine(screenB1, screenB2);   // Path from Base1 to Base2
            pathBuilder.AddLine(screenB2, screenApex); // Path from Base2 back to Apex
            pathBuilder.CloseFigure(); // Ensure the triangle is closed

            IPath path = pathBuilder.Build();

            if (IsFilled)
            {
                context.Fill(imageSharpColor, path);
            }
            else
            {
                context.Draw(Pens.Solid(imageSharpColor, scaledThickness), path);
            }
        }

        // Performs hit detection for the cone in logical (unscaled) coordinates.
        public override bool IsHit(Vector2 queryPointCanvasRelative, float unscaledHitThreshold = 5.0f)
        {
            // Transform query point into the cone's local unrotated space (where Apex is at origin).
            Vector2 localQueryPoint = Vector2.Transform(queryPointCanvasRelative - this.ApexRelative, Matrix3x2.CreateRotation(-this.RotationAngle));

            var (localApex, localB1, localB2, localBaseCenter) = GetLocalUnrotatedVertices();

            if (localBaseCenter.LengthSquared() < 0.01f) // Degenerate cone (point)
            {
                return Vector2.DistanceSquared(localQueryPoint, localApex) < (unscaledHitThreshold * unscaledHitThreshold);
            }

            if (IsFilled)
            {
                // For filled cone, check if point is inside the triangle formed by localApex, localB1, localB2.
                return HitDetection.PointInTriangle(localQueryPoint, localApex, localB1, localB2);
            }
            else // Outline
            {
                // For outline, check distance to each of the three segments.
                float effectiveDist = unscaledHitThreshold + this.Thickness / 2f;
                if (HitDetection.DistancePointToLineSegment(localQueryPoint, localApex, localB1) <= effectiveDist) return true;
                if (HitDetection.DistancePointToLineSegment(localQueryPoint, localApex, localB2) <= effectiveDist) return true;
                if (HitDetection.DistancePointToLineSegment(localQueryPoint, localB1, localB2) <= effectiveDist) return true;
            }
            return false;
        }

        // Creates a clone of this drawable cone.
        public override BaseDrawable Clone()
        {
            var newCone = new DrawableCone(this.ApexRelative, this.Color, this.Thickness, this.IsFilled)
            {
                BaseCenterRelative = this.BaseCenterRelative,
                RotationAngle = this.RotationAngle
            };
            CopyBasePropertiesTo(newCone);
            return newCone;
        }

        // Translates the cone by a given delta in logical coordinates.
        public override void Translate(Vector2 delta)
        {
            this.ApexRelative += delta;
            this.BaseCenterRelative += delta; // BaseCenterRelative is also a point, so it translates directly.
        }

        // Sets the apex position and adjusts the base center to maintain relative position.
        public void SetApex(Vector2 newApexLogical)
        {
            Vector2 diff = newApexLogical - this.ApexRelative;
            this.ApexRelative = newApexLogical;
            this.BaseCenterRelative += diff;
        }

        // Sets the base center position directly.
        public void SetBaseCenter(Vector2 newBaseCenterLogical)
        {
            this.BaseCenterRelative = newBaseCenterLogical;
        }
    }
}
