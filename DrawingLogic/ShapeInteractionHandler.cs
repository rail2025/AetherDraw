// AetherDraw/DrawingLogic/ShapeInteractionHandler.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using System.Linq;
using AetherDraw.Core;
using Dalamud.Interface.Utility;
using AetherDraw.Serialization;

namespace AetherDraw.DrawingLogic
{
    /// <summary>
    /// Manages the state and coordinates all user interactions with shapes on the canvas,
    /// such as selection, movement, and manipulation via handles.
    /// It delegates shape-specific logic to the InteractionHandlerHelpers class.
    /// </summary>
    public class ShapeInteractionHandler
    {
        private readonly Plugin plugin;
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;

        /// <summary>
        /// Defines the type of drag operation currently being performed by the user.
        /// </summary>
        public enum ActiveDragType { None, GeneralSelection, MarqueeSelection, ImageResize, ImageRotate, ConeApex, ConeBase, ConeRotate, RectResize, RectRotate, ArrowStartPoint, ArrowEndPoint, ArrowRotate, ArrowThickness, TextResize }

        /// <summary>
        /// This holds the current state of the user's drag action.
        /// </summary>
        public ActiveDragType currentDragType = ActiveDragType.None;

        #region Public State for Helpers
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
        private const float LogicalHandleInteractionRadius = 7f;
        private float ScaledHandleDrawRadius => 5f * ImGuiHelpers.GlobalScale;
        public readonly uint handleColorDefault, handleColorHover, handleColorRotation, handleColorRotationHover, handleColorResize, handleColorResizeHover, handleColorSpecial, handleColorSpecialHover;
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeInteractionHandler"/> class.
        /// </summary>
        /// <param name="plugin">The main plugin instance for accessing networking.</param>
        /// <param name="undoManagerInstance">The UndoManager instance.</param>
        /// <param name="pageManagerInstance">The PageManager instance.</param>
        public ShapeInteractionHandler(Plugin plugin, UndoManager undoManagerInstance, PageManager pageManagerInstance)
        {
            this.plugin = plugin;
            this.undoManager = undoManagerInstance ?? throw new ArgumentNullException(nameof(undoManagerInstance));
            this.pageManager = pageManagerInstance ?? throw new ArgumentNullException(nameof(pageManagerInstance));

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
            if (currentDragType == ActiveDragType.None)
                ResetHoverStates(allDrawablesOnPage);

            bool mouseOverAnyHandle = ProcessHandles(singleSelectedItem, mousePosLogical, canvasOriginScreen, drawList);

            if (currentDragType == ActiveDragType.None && !mouseOverAnyHandle)
                UpdateHoveredObject(allDrawablesOnPage, getLayerPriorityFunc, ref hoveredDrawable, mousePosLogical);

            if (isLMBClicked)
                InitiateDrag(singleSelectedItem, selectedDrawables, hoveredDrawable, mouseOverAnyHandle, mousePosLogical);

            if (isLMBDown)
            {
                if (currentDragType != ActiveDragType.None && currentDragType != ActiveDragType.MarqueeSelection)
                    UpdateDrag(singleSelectedItem, mousePosLogical);

                if (currentDragType == ActiveDragType.MarqueeSelection)
                    DrawMarqueeVisuals(mousePosLogical, mousePosScreen, canvasOriginScreen, drawList);
            }

            if (isLMBReleased)
            {
                // If we were dragging/resizing a single selected object in live mode, send the update.
                if (pageManager.IsLiveMode && singleSelectedItem != null && currentDragType != ActiveDragType.None && currentDragType != ActiveDragType.MarqueeSelection)
                {
                    SendObjectUpdate(singleSelectedItem);
                }

                if (currentDragType == ActiveDragType.MarqueeSelection)
                    FinalizeMarqueeSelection(selectedDrawables, allDrawablesOnPage, mousePosLogical);

                ResetDragState();
            }
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

        private void SendObjectUpdate(BaseDrawable obj)
        {
            var objectList = new List<BaseDrawable> { obj };
            byte[] payload = DrawableSerializer.SerializePageToBytes(objectList);
            _ = plugin.NetworkManager.SendMessageAsync(Networking.MessageType.MOVE_OBJECT, payload);
        }

        private void DrawMarqueeVisuals(Vector2 mousePosLogical, Vector2 mousePosScreen, Vector2 canvasOriginScreen, ImDrawListPtr drawList)
        {
            bool isCrossing = mousePosLogical.X < dragStartMousePosLogical.X;
            uint borderColor = isCrossing ? ImGui.GetColorU32(new Vector4(0.3f, 1.0f, 0.4f, 0.9f)) : ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1.0f, 0.9f));
            uint fillColor = isCrossing ? ImGui.GetColorU32(new Vector4(0.3f, 1.0f, 0.4f, 0.2f)) : ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1.0f, 0.2f));
            float thickness = isCrossing ? 4.0f : 1.0f;
            var rectStartScreen = dragStartMousePosLogical * ImGuiHelpers.GlobalScale + canvasOriginScreen;
            drawList.AddRectFilled(rectStartScreen, mousePosScreen, fillColor);
            drawList.AddRect(rectStartScreen, mousePosScreen, borderColor, 0f, ImDrawFlags.None, thickness);
        }

        private void FinalizeMarqueeSelection(List<BaseDrawable> selectedList, List<BaseDrawable> allDrawables, Vector2 mousePos)
        {
            if (!ImGui.GetIO().KeyCtrl)
            {
                foreach (var d in selectedList) d.IsSelected = false;
                selectedList.Clear();
            }

            var min = Vector2.Min(dragStartMousePosLogical, mousePos);
            var max = Vector2.Max(dragStartMousePosLogical, mousePos);
            var marqueeRect = new System.Drawing.RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y);
            bool isCrossing = mousePos.X < dragStartMousePosLogical.X;

            foreach (var drawable in allDrawables)
            {
                bool shouldSelect = isCrossing ? marqueeRect.IntersectsWith(drawable.GetBoundingBox()) : marqueeRect.Contains(drawable.GetBoundingBox());
                if (shouldSelect && !drawable.IsSelected)
                {
                    drawable.IsSelected = true;
                    selectedList.Add(drawable);
                }
            }
        }

        private void ResetHoverStates(List<BaseDrawable> allDrawablesOnPage)
        {
            foreach (var dItem in allDrawablesOnPage) dItem.IsHovered = false;
            draggedHandleIndex = -1;
        }

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

        private void StartDrag(ActiveDragType type, Vector2 mousePos)
        {
            currentDragType = type;
            dragStartMousePosLogical = mousePos;
            if (type != ActiveDragType.MarqueeSelection)
                undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), $"Start {type}");
        }

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

        private void UpdateDrag(BaseDrawable? item, Vector2 mousePos)
        {
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

                        dragStartMousePosLogical = mousePos;
                    }
                }
                return;
            }

            if (item == null) return;

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
