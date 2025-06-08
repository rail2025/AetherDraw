// AetherDraw/DrawingLogic/ShapeInteractionHandler.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using System.Linq;
using AetherDraw.Core;
using Dalamud.Interface.Utility;

namespace AetherDraw.DrawingLogic
{
    /// <summary>
    /// Manages the state and coordinates all user interactions with shapes on the canvas,
    /// such as selection, movement, and manipulation via handles.
    /// It delegates shape-specific logic to the InteractionHandlerHelpers class.
    /// </summary>
    public class ShapeInteractionHandler
    {
        // Dependencies required from other parts of the plugin.
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;

        /// <summary>
        /// Defines the type of drag operation currently being performed by the user.
        /// </summary>
        public enum ActiveDragType
        {
            None, GeneralSelection, MarqueeSelection, ImageResize, ImageRotate, ConeApex, ConeBase, ConeRotate,
            RectResize, RectRotate, ArrowStartPoint, ArrowEndPoint, ArrowRotate, ArrowThickness, TextResize
        }

        // This holds the current state of the user's drag action.
        public ActiveDragType currentDragType = ActiveDragType.None;

        #region Public State for Helpers
        // These fields store information about a drag operation from its start,
        // so the helper class can perform calculations correctly. They are public
        // so the static helper class can access them.
        public Vector2 dragStartMousePosLogical;
        public Vector2 dragStartObjectPivotLogical;
        public float dragStartRotationAngle;
        public Vector2 dragStartPoint1Logical;
        public Vector2 dragStartPoint2Logical;
        public Vector2 dragStartSizeLogical;
        public float dragStartValueLogical;
        public Vector2 dragStartTextPositionLogical;
        public Vector2 dragStartTextBoundingBoxSizeLogical;
        public float dragStartFontSizeLogical;
        public int draggedHandleIndex = -1;
        #endregion

        #region UI Constants
        // Constants that define the size and appearance of the interaction handles.
        private const float LogicalHandleInteractionRadius = 7f;
        private float ScaledHandleDrawRadius => 5f * ImGuiHelpers.GlobalScale;
        public readonly uint handleColorDefault, handleColorHover, handleColorRotation, handleColorRotationHover,
                              handleColorResize, handleColorResizeHover, handleColorSpecial, handleColorSpecialHover;
        #endregion

        public ShapeInteractionHandler(UndoManager undoManagerInstance, PageManager pageManagerInstance)
        {
            this.undoManager = undoManagerInstance ?? throw new ArgumentNullException(nameof(undoManagerInstance));
            this.pageManager = pageManagerInstance ?? throw new ArgumentNullException(nameof(pageManagerInstance));

            // Set up the colors for the interaction handles.
            this.handleColorDefault = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.9f));
            this.handleColorHover = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.5f, 1.0f));
            this.handleColorRotation = ImGui.GetColorU32(new Vector4(0.5f, 1.0f, 0.5f, 0.9f));
            this.handleColorRotationHover = ImGui.GetColorU32(new Vector4(0.7f, 1.0f, 0.7f, 1.0f));
            this.handleColorResize = ImGui.GetColorU32(new Vector4(0.5f, 0.7f, 1.0f, 0.9f));
            this.handleColorResizeHover = ImGui.GetColorU32(new Vector4(0.7f, 0.9f, 1.0f, 1.0f));
            this.handleColorSpecial = ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.2f, 0.9f));
            this.handleColorSpecialHover = ImGui.GetColorU32(new Vector4(1.0f, 0.7f, 0.4f, 1.0f));
        }

        /// <summary>
        /// The main entry point called every frame to process interactions.
        /// It acts as a coordinator, calling smaller methods to handle specific tasks.
        /// </summary>
        public void ProcessInteractions(
            BaseDrawable? singleSelectedItem, List<BaseDrawable> selectedDrawables, List<BaseDrawable> allDrawablesOnPage,
            Func<DrawMode, int> getLayerPriorityFunc, ref BaseDrawable? hoveredDrawable,
            Vector2 mousePosLogical, Vector2 mousePosScreen, Vector2 canvasOriginScreen,
            bool isLMBClicked, bool isLMBDown, bool isLMBReleased, ImDrawListPtr drawList)
        {
            // If we are not currently dragging anything, reset hover states.
            if (currentDragType == ActiveDragType.None)
                ResetHoverStates(allDrawablesOnPage);

            // Check if the mouse is over any handles on the selected object.
            bool mouseOverAnyHandle = ProcessHandles(singleSelectedItem, mousePosLogical, canvasOriginScreen, drawList);

            // If not dragging and not over a handle, check if we are hovering over an object.
            if (currentDragType == ActiveDragType.None && !mouseOverAnyHandle)
                UpdateHoveredObject(allDrawablesOnPage, getLayerPriorityFunc, ref hoveredDrawable, mousePosLogical);

            // If the mouse was just clicked, figure out what action to start.
            if (isLMBClicked)
                InitiateDrag(singleSelectedItem, selectedDrawables, hoveredDrawable, mouseOverAnyHandle, mousePosLogical);

            // If the mouse is being held down, update any active drag operation.
            if (isLMBDown)
            {
                if (currentDragType != ActiveDragType.None && currentDragType != ActiveDragType.MarqueeSelection)
                    UpdateDrag(singleSelectedItem, mousePosLogical);

                // If we are marquee selecting, draw the visual box.
                if (currentDragType == ActiveDragType.MarqueeSelection)
                    DrawMarqueeVisuals(mousePosLogical, mousePosScreen, canvasOriginScreen, drawList);
            }

            // If the mouse was just released, end any drag operation.
            if (isLMBReleased)
            {
                // ADDED: If the drag was a marquee selection, finalize the selection.
                if (currentDragType == ActiveDragType.MarqueeSelection)
                    FinalizeMarqueeSelection(selectedDrawables, allDrawablesOnPage, mousePosLogical);

                ResetDragState();
            }
        }

        /// <summary>
        /// Draws the visual marquee selection box on the canvas, changing style based on drag direction.
        /// </summary>
        private void DrawMarqueeVisuals(Vector2 mousePosLogical, Vector2 mousePosScreen, Vector2 canvasOriginScreen, ImDrawListPtr drawList)
        {
            // Determine drag direction to set the style.
            bool isCrossing = mousePosLogical.X < dragStartMousePosLogical.X;

            uint borderColor;
            uint fillColor;
            float thickness;

            if (isCrossing) // Right-to-Left Drag (Crossing/Touching)
            {
                borderColor = ImGui.GetColorU32(new Vector4(0.3f, 1.0f, 0.4f, 0.9f)); // Greenish
                fillColor = ImGui.GetColorU32(new Vector4(0.3f, 1.0f, 0.4f, 0.2f));
                thickness = 4.0f; // Thicker border
            }
            else // Left-to-Right Drag (Enclosing/Window)
            {
                borderColor = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1.0f, 0.9f)); // Bluish
                fillColor = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1.0f, 0.2f));
                thickness = 1.0f; // Thinner border
            }

            // Draw the filled rectangle and its border.
            var rectStartScreen = dragStartMousePosLogical * ImGuiHelpers.GlobalScale + canvasOriginScreen;
            drawList.AddRectFilled(rectStartScreen, mousePosScreen, fillColor);
            drawList.AddRect(rectStartScreen, mousePosScreen, borderColor, 0f, ImDrawFlags.None, thickness);
        }

        /// <summary>
        /// This new method processes the selection logic when a marquee drag is finished.
        /// It now supports all drawable types and uses a more precise intersection test for circles.
        /// </summary>
        private void FinalizeMarqueeSelection(List<BaseDrawable> selectedList, List<BaseDrawable> allDrawables, Vector2 mousePos)
        {
            bool ctrl = ImGui.GetIO().KeyCtrl;
            // If not holding control, clear the previous selection first.
            if (!ctrl)
            {
                foreach (var d in selectedList) d.IsSelected = false;
                selectedList.Clear();
            }

            // Define the marquee selection box in logical (unscaled) coordinates.
            var min = Vector2.Min(dragStartMousePosLogical, mousePos);
            var max = Vector2.Max(dragStartMousePosLogical, mousePos);
            var marqueeRect = new System.Drawing.RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y);

            // A right-to-left drag is a "crossing" selection.
            bool isCrossing = mousePos.X < dragStartMousePosLogical.X;
            bool selectionChanged = false;

            foreach (var drawable in allDrawables)
            {
                var objectBoundingBox = drawable.GetBoundingBox();
                bool shouldSelect = false;

                if (isCrossing) // Right-to-left drag: select if object is touched.
                {
                    // We use a more precise check for circles to see if the marquee truly hits the circle.
                    if (drawable is DrawableCircle circle)
                    {
                        // This checks for intersection between the marquee Rectangle and the Circle itself.
                        if (HitDetection.IntersectCircleAABB(circle.CenterRelative, circle.Radius, new Vector2(marqueeRect.Left, marqueeRect.Top), new Vector2(marqueeRect.Right, marqueeRect.Bottom)))
                            shouldSelect = true;
                    }
                    // For all other shapes, we'll use their rectangular bounding box for now.
                    else if (marqueeRect.IntersectsWith(objectBoundingBox))
                    {
                        shouldSelect = true;
                    }
                }
                else // Left-to-right drag: select only if object is fully enclosed.
                {
                    // This check is accurate for all shapes.
                    if (marqueeRect.Contains(objectBoundingBox))
                        shouldSelect = true;
                }

                // If the object should be selected and isn't already, add it to the selection.
                if (shouldSelect && !drawable.IsSelected)
                {
                    drawable.IsSelected = true;
                    selectedList.Add(drawable);
                    selectionChanged = true;
                }
            }

            // If the selection actually changed, create an undo state.
            if (selectionChanged)
                undoManager.RecordAction(allDrawables, "Marquee Select");
        }

        /// <summary>
        /// Resets the drag state to None.
        /// </summary>
        public void ResetDragState()
        {
            currentDragType = ActiveDragType.None;
            draggedHandleIndex = -1;
        }

        /// <summary>
        /// A generic helper to draw a single circular handle and check if the mouse is over it.
        /// This is public so the helper class can call it.
        /// </summary>
        public bool DrawAndCheckHandle(ImDrawListPtr drawList, Vector2 logicalPos, Vector2 canvasOrigin, Vector2 mousePos, ref bool mouseOverAny, uint color, uint hoverColor)
        {
            Vector2 screenPos = logicalPos * ImGuiHelpers.GlobalScale + canvasOrigin;
            bool isHovering = Vector2.Distance(mousePos, logicalPos) < LogicalHandleInteractionRadius;
            if (isHovering) mouseOverAny = true;
            drawList.AddCircleFilled(screenPos, ScaledHandleDrawRadius, isHovering ? hoverColor : color);
            drawList.AddCircle(screenPos, ScaledHandleDrawRadius + 1f, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.7f)));
            return isHovering;
        }

        /// <summary>
        /// Overload for DrawAndCheckHandle that also sets the mouse cursor style.
        /// </summary>
        public bool DrawAndCheckHandle(ImDrawListPtr drawList, Vector2 logicalPos, Vector2 canvasOrigin, Vector2 mousePos, ref bool mouseOverAny, ImGuiMouseCursor cursor, uint color, uint hoverColor)
        {
            if (DrawAndCheckHandle(drawList, logicalPos, canvasOrigin, mousePos, ref mouseOverAny, color, hoverColor))
            {
                ImGui.SetMouseCursor(cursor);
                return true;
            }
            return false;
        }

        private void ResetHoverStates(List<BaseDrawable> allDrawablesOnPage)
        {
            foreach (var dItem in allDrawablesOnPage) dItem.IsHovered = false;
            draggedHandleIndex = -1;
        }

        /// <summary>
        /// Determines which set of handles to draw by checking the selected object's type
        /// and calling the appropriate helper method.
        /// </summary>
        private bool ProcessHandles(BaseDrawable? item, Vector2 mousePos, Vector2 canvasOrigin, ImDrawListPtr drawList)
        {
            if (item == null) return false;

            bool mouseOverAny = false;
            switch (item)
            {
                case DrawableImage dImg: InteractionHandlerHelpers.ProcessImageHandles(dImg, mousePos, canvasOrigin, drawList, this, ref mouseOverAny); break;
                case DrawableRectangle dRect: InteractionHandlerHelpers.ProcessRectangleHandles(dRect, mousePos, canvasOrigin, drawList, this, ref mouseOverAny); break;
                case DrawableText dText: InteractionHandlerHelpers.ProcessTextHandles(dText, mousePos, canvasOrigin, drawList, this, ref mouseOverAny); break;
                case DrawableArrow dArrow: InteractionHandlerHelpers.ProcessArrowHandles(dArrow, mousePos, canvasOrigin, drawList, this, ref mouseOverAny); break;
                case DrawableCone dCone: InteractionHandlerHelpers.ProcessConeHandles(dCone, mousePos, canvasOrigin, drawList, this, ref mouseOverAny); break;
            }
            return mouseOverAny;
        }

        private void UpdateHoveredObject(List<BaseDrawable> allDrawables, Func<DrawMode, int> layerPriority, ref BaseDrawable? hovered, Vector2 mousePos)
        {
            hovered = null;
            var sortedForHover = allDrawables.OrderByDescending(d => layerPriority(d.ObjectDrawMode));
            foreach (var drawable in sortedForHover)
            {
                if (drawable.IsHit(mousePos, LogicalHandleInteractionRadius * 0.8f))
                {
                    hovered = drawable;
                    drawable.IsHovered = true;
                    break;
                }
            }
        }

        /// <summary>
        /// Determines the correct action to take when the user first clicks the mouse.
        /// </summary>
        private void InitiateDrag(BaseDrawable? singleSelectedItem, List<BaseDrawable> selectedList, BaseDrawable? hovered, bool onHandle, Vector2 mousePos)
        {
            if (onHandle && singleSelectedItem != null)
            {
                StartHandleDrag(singleSelectedItem, mousePos);
            }
            else if (hovered != null)
            {
                bool ctrl = ImGui.GetIO().KeyCtrl;
                if (!ctrl)
                {
                    if (!selectedList.Contains(hovered))
                    {
                        foreach (var d in selectedList) d.IsSelected = false;
                        selectedList.Clear();
                        hovered.IsSelected = true;
                        selectedList.Add(hovered);
                    }
                }
                else
                {
                    if (selectedList.Contains(hovered)) { hovered.IsSelected = false; selectedList.Remove(hovered); }
                    else { hovered.IsSelected = true; selectedList.Add(hovered); }
                }
                if (selectedList.Count > 0) StartDrag(ActiveDragType.GeneralSelection, mousePos);
            }
            else
            {
                StartDrag(ActiveDragType.MarqueeSelection, mousePos);
            }
        }

        /// <summary>
        /// Begins a drag operation and records the initial state for the Undo system.
        /// </summary>
        private void StartDrag(ActiveDragType type, Vector2 mousePos)
        {
            currentDragType = type;
            dragStartMousePosLogical = mousePos;
            // Do not create an undo state for marquee selection, as it doesn't modify the canvas yet.
            if (type != ActiveDragType.MarqueeSelection)
                undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), $"Start {type}");
        }

        /// <summary>
        /// Sets the specific drag type and records initial state when a handle is dragged.
        /// </summary>
        private void StartHandleDrag(BaseDrawable item, Vector2 mousePos)
        {
            var initialType = ActiveDragType.None;
            switch (item)
            {
                case DrawableImage dImg when draggedHandleIndex == 4: initialType = ActiveDragType.ImageRotate; dragStartObjectPivotLogical = dImg.PositionRelative; dragStartRotationAngle = dImg.RotationAngle; break;
                case DrawableImage dImg: initialType = ActiveDragType.ImageResize; dragStartObjectPivotLogical = dImg.PositionRelative; dragStartSizeLogical = dImg.DrawSize; dragStartRotationAngle = dImg.RotationAngle; break;
                case DrawableRectangle dRect when draggedHandleIndex == 4: initialType = ActiveDragType.RectRotate; (dragStartObjectPivotLogical, _) = dRect.GetGeometry(); dragStartRotationAngle = dRect.RotationAngle; break;
                case DrawableRectangle dRect: initialType = ActiveDragType.RectResize; dragStartPoint1Logical = dRect.StartPointRelative; dragStartPoint2Logical = dRect.EndPointRelative; dragStartRotationAngle = dRect.RotationAngle; (dragStartObjectPivotLogical, _) = dRect.GetGeometry(); break;
                case DrawableText dText: initialType = ActiveDragType.TextResize; dragStartTextPositionLogical = dText.PositionRelative; dragStartTextBoundingBoxSizeLogical = dText.CurrentBoundingBoxSize; dragStartFontSizeLogical = dText.FontSize; break;
                case DrawableArrow dArr:
                    dragStartPoint1Logical = dArr.StartPointRelative;
                    dragStartPoint2Logical = dArr.EndPointRelative;
                    dragStartRotationAngle = dArr.RotationAngle;
                    if (draggedHandleIndex == 0) initialType = ActiveDragType.ArrowStartPoint;
                    else if (draggedHandleIndex == 1) initialType = ActiveDragType.ArrowEndPoint;
                    else if (draggedHandleIndex == 2) { initialType = ActiveDragType.ArrowRotate; dragStartObjectPivotLogical = dArr.StartPointRelative; }
                    else if (draggedHandleIndex == 3) { initialType = ActiveDragType.ArrowThickness; dragStartValueLogical = dArr.Thickness; }
                    break;
                case DrawableCone dCone:
                    dragStartRotationAngle = dCone.RotationAngle;
                    if (draggedHandleIndex == 0) { initialType = ActiveDragType.ConeApex; dragStartPoint1Logical = dCone.ApexRelative; dragStartPoint2Logical = dCone.BaseCenterRelative; }
                    else if (draggedHandleIndex == 1) { initialType = ActiveDragType.ConeBase; dragStartPoint1Logical = dCone.ApexRelative; dragStartPoint2Logical = dCone.BaseCenterRelative; }
                    else if (draggedHandleIndex == 2) { initialType = ActiveDragType.ConeRotate; dragStartObjectPivotLogical = dCone.ApexRelative; }
                    break;
            }

            if (initialType != ActiveDragType.None)
                StartDrag(initialType, mousePos);
        }

        /// <summary>
        /// Updates the properties of a dragged object by calling the appropriate helper method.
        /// </summary>
        private void UpdateDrag(BaseDrawable? item, Vector2 mousePos)
        {
            // First, handle the general case of moving selected objects.
            // This applies whether one or multiple items are selected.
            if (currentDragType == ActiveDragType.GeneralSelection)
            {
                var selectedDrawablesOnPage = pageManager.GetCurrentPageDrawables().Where(d => d.IsSelected);
                if (selectedDrawablesOnPage.Any())
                {
                    Vector2 dragDelta = mousePos - dragStartMousePosLogical;
                    if (dragDelta.LengthSquared() > 0)
                    {
                        foreach (var selected in selectedDrawablesOnPage)
                            selected.Translate(dragDelta);

                        // Update the start position for the next frame's delta calculation.
                        dragStartMousePosLogical = mousePos;
                    }
                }
                return; // Exit after processing the move.
            }

            // If it's not a general move, it must be a handle drag, which requires a single item.
            if (item == null)
            {
                // This case should not be hit for handle drags, but we exit to be safe.
                return;
            }

            // Process specific handle-based drag operations.
            switch (currentDragType)
            {
                case ActiveDragType.ImageResize: InteractionHandlerHelpers.UpdateImageDrag((DrawableImage)item, mousePos, this); break;
                case ActiveDragType.ImageRotate: InteractionHandlerHelpers.UpdateRotationDrag(item, mousePos, this); break;
                case ActiveDragType.RectResize: InteractionHandlerHelpers.UpdateRectangleDrag((DrawableRectangle)item, mousePos, this); break;
                case ActiveDragType.RectRotate: InteractionHandlerHelpers.UpdateRotationDrag(item, mousePos, this); break;
                case ActiveDragType.TextResize: InteractionHandlerHelpers.UpdateTextResizeDrag((DrawableText)item, mousePos, this); break;
                case ActiveDragType.ArrowStartPoint: InteractionHandlerHelpers.UpdateArrowStartDrag((DrawableArrow)item, mousePos, this); break;
                case ActiveDragType.ArrowEndPoint: InteractionHandlerHelpers.UpdateArrowEndDrag((DrawableArrow)item, mousePos); break;
                case ActiveDragType.ArrowRotate: InteractionHandlerHelpers.UpdateRotationDrag(item, mousePos, this); break;
                case ActiveDragType.ArrowThickness: InteractionHandlerHelpers.UpdateArrowThicknessDrag((DrawableArrow)item, mousePos, this); break;
                case ActiveDragType.ConeApex: InteractionHandlerHelpers.UpdateConeApexDrag((DrawableCone)item, mousePos, this); break;
                case ActiveDragType.ConeBase: InteractionHandlerHelpers.UpdateConeBaseDrag((DrawableCone)item, mousePos); break;
                case ActiveDragType.ConeRotate: InteractionHandlerHelpers.UpdateRotationDrag(item, mousePos, this); break;
            }
        }
    }
}
