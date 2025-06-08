// AetherDraw/DrawingLogic/InteractionHandlerHelpers.cs
using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility;

namespace AetherDraw.DrawingLogic
{
    /// <summary>
    /// Provides static helper methods for the main ShapeInteractionHandler.
    /// Each method encapsulates the logic for drawing handles or processing drag updates for a specific shape type.
    /// This keeps the main handler class clean and delegates specific logic to this utility class.
    /// </summary>
    public static class InteractionHandlerHelpers
    {
        #region Handle Drawing
        /// <summary>
        /// Draws the resize and rotation handles for a DrawableImage.
        /// </summary>
        /// <param name="handler">The main handler instance, used to access state and drawing functions.</param>
        /// <param name="mouseOverAny">A reference bool that is set to true if the mouse is over any handle drawn by this method.</param>
        public static void ProcessImageHandles(DrawableImage dImg, Vector2 mousePos, Vector2 canvasOrigin, ImDrawListPtr drawList, ShapeInteractionHandler handler, ref bool mouseOverAny)
        {
            // Get the four corners of the rotated image.
            Vector2[] logicalCorners = HitDetection.GetRotatedQuadVertices(dImg.PositionRelative, dImg.DrawSize / 2f, dImg.RotationAngle);
            for (int i = 0; i < 4; i++)
                if (handler.DrawAndCheckHandle(drawList, logicalCorners[i], canvasOrigin, mousePos, ref mouseOverAny, handler.handleColorResize, handler.handleColorResizeHover))
                    handler.draggedHandleIndex = i;

            // Calculate the position for the rotation handle, placed above the image.
            Vector2 handleOffsetLocal = new Vector2(0, -(dImg.DrawSize.Y / 2f + DrawableImage.UnscaledRotationHandleDistance));
            Vector2 rotatedHandleOffset = HitDetection.ImRotate(handleOffsetLocal, MathF.Cos(dImg.RotationAngle), MathF.Sin(dImg.RotationAngle));
            Vector2 logicalRotationHandlePos = dImg.PositionRelative + rotatedHandleOffset;
            if (handler.DrawAndCheckHandle(drawList, logicalRotationHandlePos, canvasOrigin, mousePos, ref mouseOverAny, handler.handleColorRotation, handler.handleColorRotationHover))
                handler.draggedHandleIndex = 4; // Use index 4 for the rotation handle.
        }

        /// <summary>
        /// Draws the resize and rotation handles for a DrawableRectangle.
        /// </summary>
        public static void ProcessRectangleHandles(DrawableRectangle dRect, Vector2 mousePos, Vector2 canvasOrigin, ImDrawListPtr drawList, ShapeInteractionHandler handler, ref bool mouseOverAny)
        {
            Vector2[] logicalRotatedCorners = dRect.GetRotatedCorners();
            for (int i = 0; i < 4; i++)
                if (handler.DrawAndCheckHandle(drawList, logicalRotatedCorners[i], canvasOrigin, mousePos, ref mouseOverAny, handler.handleColorResize, handler.handleColorResizeHover))
                    handler.draggedHandleIndex = i;

            var (rectCenterLogical, rectHalfSize) = dRect.GetGeometry();
            float handleDistance = rectHalfSize.Y + DrawableRectangle.UnscaledRotationHandleExtraOffset;
            Vector2 rotationHandleLogicalPos = rectCenterLogical + Vector2.Transform(new Vector2(0, -handleDistance), Matrix3x2.CreateRotation(dRect.RotationAngle));
            if (handler.DrawAndCheckHandle(drawList, rotationHandleLogicalPos, canvasOrigin, mousePos, ref mouseOverAny, handler.handleColorRotation, handler.handleColorRotationHover))
                handler.draggedHandleIndex = 4;
        }

        /// <summary>
        /// Draws the resize handles for a DrawableText object.
        /// </summary>
        public static void ProcessTextHandles(DrawableText dText, Vector2 mousePos, Vector2 canvasOrigin, ImDrawListPtr drawList, ShapeInteractionHandler handler, ref bool mouseOverAny)
        {
            Vector2 boxTopLeft = dText.PositionRelative;
            Vector2 boxSize = dText.CurrentBoundingBoxSize;
            Vector2[] bboxCorners = {
                boxTopLeft, // Top-left
                boxTopLeft + new Vector2(boxSize.X, 0), // Top-right
                boxTopLeft + boxSize, // Bottom-right
                boxTopLeft + new Vector2(0, boxSize.Y)  // Bottom-left
            };
            ImGuiMouseCursor[] cursors = { ImGuiMouseCursor.ResizeNWSE, ImGuiMouseCursor.ResizeNESW, ImGuiMouseCursor.ResizeNWSE, ImGuiMouseCursor.ResizeNESW };
            for (int i = 0; i < 4; i++)
                if (handler.DrawAndCheckHandle(drawList, bboxCorners[i], canvasOrigin, mousePos, ref mouseOverAny, cursors[i], handler.handleColorResize, handler.handleColorResizeHover))
                    handler.draggedHandleIndex = i;
        }

        /// <summary>
        /// Draws the handles for a DrawableArrow (start, end, rotation, thickness).
        /// </summary>
        public static void ProcessArrowHandles(DrawableArrow dArrow, Vector2 mousePos, Vector2 canvasOrigin, ImDrawListPtr drawList, ShapeInteractionHandler handler, ref bool mouseOverAny)
        {
            Vector2 logicalStart = dArrow.StartPointRelative;
            Vector2 localShaftEndUnrotated = dArrow.EndPointRelative - dArrow.StartPointRelative;
            Vector2 logicalRotatedShaftEnd = dArrow.StartPointRelative + Vector2.Transform(localShaftEndUnrotated, Matrix3x2.CreateRotation(dArrow.RotationAngle));
            if (handler.DrawAndCheckHandle(drawList, logicalStart, canvasOrigin, mousePos, ref mouseOverAny, ImGuiMouseCursor.ResizeAll, handler.handleColorResize, handler.handleColorResizeHover)) handler.draggedHandleIndex = 0;
            if (handler.DrawAndCheckHandle(drawList, logicalRotatedShaftEnd, canvasOrigin, mousePos, ref mouseOverAny, ImGuiMouseCursor.ResizeAll, handler.handleColorResize, handler.handleColorResizeHover)) handler.draggedHandleIndex = 1;

            Vector2 rotHandleOffsetLocal = new Vector2(0, -DrawableRectangle.UnscaledRotationHandleExtraOffset);
            Vector2 rotHandleLogical = dArrow.StartPointRelative + Vector2.Transform(rotHandleOffsetLocal, Matrix3x2.CreateRotation(dArrow.RotationAngle));
            if (handler.DrawAndCheckHandle(drawList, rotHandleLogical, canvasOrigin, mousePos, ref mouseOverAny, handler.handleColorRotation, handler.handleColorRotationHover)) handler.draggedHandleIndex = 2;

            Vector2 shaftMidLogicalRotated = dArrow.StartPointRelative + Vector2.Transform(localShaftEndUnrotated / 2f, Matrix3x2.CreateRotation(dArrow.RotationAngle));
            Vector2 shaftDirRotated = localShaftEndUnrotated.LengthSquared() > 0.001f ? Vector2.Normalize(Vector2.Transform(localShaftEndUnrotated, Matrix3x2.CreateRotation(dArrow.RotationAngle))) : Vector2.Transform(new Vector2(0, 1), Matrix3x2.CreateRotation(dArrow.RotationAngle));
            Vector2 perpOffsetThick = new Vector2(-shaftDirRotated.Y, shaftDirRotated.X) * (dArrow.Thickness / 2f + 10f);
            if (handler.DrawAndCheckHandle(drawList, shaftMidLogicalRotated + perpOffsetThick, canvasOrigin, mousePos, ref mouseOverAny, ImGuiMouseCursor.ResizeNS, handler.handleColorSpecial, handler.handleColorSpecialHover)) handler.draggedHandleIndex = 3;
        }

        /// <summary>
        /// Draws the handles for a DrawableCone (apex, base, rotation).
        /// </summary>
        public static void ProcessConeHandles(DrawableCone dCone, Vector2 mousePos, Vector2 canvasOrigin, ImDrawListPtr drawList, ShapeInteractionHandler handler, ref bool mouseOverAny)
        {
            Vector2 logicalApex = dCone.ApexRelative;
            Vector2 logicalBaseEndUnrotated = dCone.BaseCenterRelative - dCone.ApexRelative;
            Vector2 logicalBaseEndRotated = Vector2.Transform(logicalBaseEndUnrotated, Matrix3x2.CreateRotation(dCone.RotationAngle));
            Vector2 logicalRotatedBaseCenter = dCone.ApexRelative + logicalBaseEndRotated;
            if (handler.DrawAndCheckHandle(drawList, logicalApex, canvasOrigin, mousePos, ref mouseOverAny, ImGuiMouseCursor.ResizeAll, handler.handleColorResize, handler.handleColorResizeHover)) handler.draggedHandleIndex = 0;
            if (handler.DrawAndCheckHandle(drawList, logicalRotatedBaseCenter, canvasOrigin, mousePos, ref mouseOverAny, ImGuiMouseCursor.ResizeAll, handler.handleColorResize, handler.handleColorResizeHover)) handler.draggedHandleIndex = 1;

            Vector2 axisDir = logicalBaseEndUnrotated.LengthSquared() > 0.001f ? Vector2.Normalize(logicalBaseEndRotated) : new Vector2(0, 1);
            Vector2 rotHandleLogical = logicalRotatedBaseCenter + axisDir * (DrawableRectangle.UnscaledRotationHandleExtraOffset * 0.75f);
            if (handler.DrawAndCheckHandle(drawList, rotHandleLogical, canvasOrigin, mousePos, ref mouseOverAny, handler.handleColorRotation, handler.handleColorRotationHover)) handler.draggedHandleIndex = 2;
        }
        #endregion

        #region Drag Update Logic
        /// <summary>
        /// Updates the rotation of a drawable item based on mouse movement.
        /// </summary>
        public static void UpdateRotationDrag(BaseDrawable item, Vector2 mousePos, ShapeInteractionHandler handler)
        {
            // Calculate the change in angle from the start of the drag to the current mouse position.
            float angleNow = MathF.Atan2(mousePos.Y - handler.dragStartObjectPivotLogical.Y, mousePos.X - handler.dragStartObjectPivotLogical.X);
            float angleThen = MathF.Atan2(handler.dragStartMousePosLogical.Y - handler.dragStartObjectPivotLogical.Y, handler.dragStartMousePosLogical.X - handler.dragStartObjectPivotLogical.X);
            float newAngle = handler.dragStartRotationAngle + (angleNow - angleThen);

            switch (item)
            {
                case DrawableImage dImg: dImg.RotationAngle = newAngle; break;
                case DrawableRectangle dRect: dRect.RotationAngle = newAngle; break;
                case DrawableArrow dArrow: dArrow.RotationAngle = newAngle; break;
                case DrawableCone dCone: dCone.RotationAngle = newAngle; break;
            }
        }

        /// <summary>
        /// Updates the size of a DrawableImage during a resize drag.
        /// </summary>
        public static void UpdateImageDrag(DrawableImage dImg, Vector2 mousePos, ShapeInteractionHandler handler)
        {
            // Transform the mouse position into the image's local, unrotated coordinate space.
            Vector2 mouseInLocalUnrotated = HitDetection.ImRotate(mousePos - dImg.PositionRelative, MathF.Cos(-dImg.RotationAngle), MathF.Sin(-dImg.RotationAngle));
            Vector2 newHalfSize = new Vector2(Math.Abs(mouseInLocalUnrotated.X), Math.Abs(mouseInLocalUnrotated.Y));
            float minDimLogical = DrawableImage.UnscaledResizeHandleRadius * 2f;
            dImg.DrawSize = new Vector2(Math.Max(newHalfSize.X * 2f, minDimLogical), Math.Max(newHalfSize.Y * 2f, minDimLogical));
        }

        /// <summary>
        /// Updates the size and position of a DrawableRectangle during a resize drag.
        /// </summary>
        public static void UpdateRectangleDrag(DrawableRectangle dRect, Vector2 mousePos, ShapeInteractionHandler handler)
        {
            // The corner opposite the one being dragged acts as a fixed pivot.
            Vector2[] originalLogicalCorners = HitDetection.GetRotatedQuadVertices(handler.dragStartObjectPivotLogical, (handler.dragStartPoint2Logical - handler.dragStartPoint1Logical) / 2f, handler.dragStartRotationAngle);
            Vector2 pivotCornerLogical = originalLogicalCorners[(handler.draggedHandleIndex + 2) % 4];

            // Calculate new geometry based on the pivot and current mouse position.
            Vector2 mouseRelativeToPivot = mousePos - pivotCornerLogical;
            Vector2 mouseInRectLocalFrame = HitDetection.ImRotate(mouseRelativeToPivot, MathF.Cos(-handler.dragStartRotationAngle), MathF.Sin(-handler.dragStartRotationAngle));
            Vector2 newCenterInLocalFrame = mouseInRectLocalFrame / 2f;
            Vector2 newHalfSizeLocal = new Vector2(Math.Abs(mouseInRectLocalFrame.X) / 2f, Math.Abs(mouseInRectLocalFrame.Y) / 2f);
            newHalfSizeLocal.X = Math.Max(newHalfSizeLocal.X, 1f); newHalfSizeLocal.Y = Math.Max(newHalfSizeLocal.Y, 1f);
            Vector2 newCenter = pivotCornerLogical + HitDetection.ImRotate(newCenterInLocalFrame, MathF.Cos(handler.dragStartRotationAngle), MathF.Sin(handler.dragStartRotationAngle));

            // Update the rectangle with the new geometry.
            dRect.StartPointRelative = newCenter - newHalfSizeLocal;
            dRect.EndPointRelative = newCenter + newHalfSizeLocal;
            dRect.RotationAngle = handler.dragStartRotationAngle;
        }

        /// <summary>
        /// Updates the size and font size of a DrawableText object during a resize drag.
        /// </summary>
        public static void UpdateTextResizeDrag(DrawableText dText, Vector2 mousePos, ShapeInteractionHandler handler)
        {
            int anchorIndex = (handler.draggedHandleIndex + 2) % 4;
            Vector2[] initialCorners = {
                handler.dragStartTextPositionLogical,
                handler.dragStartTextPositionLogical + new Vector2(handler.dragStartTextBoundingBoxSizeLogical.X, 0),
                handler.dragStartTextPositionLogical + handler.dragStartTextBoundingBoxSizeLogical,
                handler.dragStartTextPositionLogical + new Vector2(0, handler.dragStartTextBoundingBoxSizeLogical.Y)
            };
            Vector2 anchorPoint = initialCorners[anchorIndex];
            Vector2 newTopLeft = new Vector2(Math.Min(anchorPoint.X, mousePos.X), Math.Min(anchorPoint.Y, mousePos.Y));
            Vector2 newBottomRight = new Vector2(Math.Max(anchorPoint.X, mousePos.X), Math.Max(anchorPoint.Y, mousePos.Y));
            float newWidth = newBottomRight.X - newTopLeft.X;
            float newHeight = newBottomRight.Y - newTopLeft.Y;

            dText.PositionRelative = newTopLeft;
            if (newWidth > 10f) dText.WrappingWidth = newWidth;
            if (handler.dragStartTextBoundingBoxSizeLogical.Y > 1f && newHeight > 10f)
            {
                float heightRatio = newHeight / handler.dragStartTextBoundingBoxSizeLogical.Y;
                dText.FontSize = Math.Max(8f, handler.dragStartFontSizeLogical * heightRatio);
            }
        }

        /// <summary>
        /// Updates the start point of a DrawableArrow during a drag.
        /// </summary>
        public static void UpdateArrowStartDrag(DrawableArrow dArrow, Vector2 mousePos, ShapeInteractionHandler handler)
        {
            dArrow.SetStartPoint(handler.dragStartPoint1Logical + (mousePos - handler.dragStartMousePosLogical));
        }

        /// <summary>
        /// Updates the end point of a DrawableArrow during a drag.
        /// </summary>
        public static void UpdateArrowEndDrag(DrawableArrow dArrow, Vector2 mousePos)
        {
            Vector2 mouseRelativeToStart = mousePos - dArrow.StartPointRelative;
            Vector2 unrotatedMouseRelativeToStart = HitDetection.ImRotate(mouseRelativeToStart, MathF.Cos(-dArrow.RotationAngle), MathF.Sin(-dArrow.RotationAngle));
            dArrow.SetEndPoint(dArrow.StartPointRelative + unrotatedMouseRelativeToStart);
        }

        /// <summary>
        /// Updates the thickness of a DrawableArrow during a drag.
        /// </summary>
        public static void UpdateArrowThicknessDrag(DrawableArrow dArrow, Vector2 mousePos, ShapeInteractionHandler handler)
        {
            Vector2 initialShaftVec = handler.dragStartPoint2Logical - handler.dragStartPoint1Logical;
            Vector2 initialShaftDir = initialShaftVec.LengthSquared() > 0.001f ? Vector2.Normalize(initialShaftVec) : new Vector2(0, -1);
            Vector2 perpDir = new Vector2(-initialShaftDir.Y, initialShaftDir.X);
            Vector2 currentMouseDeltaFromDragStartUnrotated = HitDetection.ImRotate((mousePos - handler.dragStartMousePosLogical), MathF.Cos(-handler.dragStartRotationAngle), MathF.Sin(-handler.dragStartRotationAngle));
            // Project the mouse movement onto the perpendicular vector to determine thickness change.
            float thicknessDeltaProjection = Vector2.Dot(currentMouseDeltaFromDragStartUnrotated, perpDir);
            dArrow.Thickness = Math.Max(1f, handler.dragStartValueLogical + thicknessDeltaProjection);
        }

        /// <summary>
        /// Updates the apex point of a DrawableCone during a drag.
        /// </summary>
        public static void UpdateConeApexDrag(DrawableCone dCone, Vector2 mousePos, ShapeInteractionHandler handler)
        {
            dCone.SetApex(handler.dragStartPoint1Logical + (mousePos - handler.dragStartMousePosLogical));
        }

        /// <summary>
        /// Updates the base center point of a DrawableCone during a drag.
        /// </summary>
        public static void UpdateConeBaseDrag(DrawableCone dCone, Vector2 mousePos)
        {
            Vector2 mouseRelativeToApex = mousePos - dCone.ApexRelative;
            Vector2 unrotatedMouseRelativeToApex = HitDetection.ImRotate(mouseRelativeToApex, MathF.Cos(-dCone.RotationAngle), MathF.Sin(-dCone.RotationAngle));
            dCone.SetBaseCenter(dCone.ApexRelative + unrotatedMouseRelativeToApex);
        }
        #endregion
    }
}
