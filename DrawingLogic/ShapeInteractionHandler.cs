using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using System.Linq;
using Dalamud.Interface.Utility; // Added for ImGuiHelpers

namespace AetherDraw.DrawingLogic
{
    public class ShapeInteractionHandler
    {
        public enum ActiveDragType
        {
            None,
            GeneralSelection,
            ImageResize, ImageRotate,
            ConeApex, ConeBase, ConeRotate,
            RectResize, RectRotate,
            ArrowStartPoint, ArrowEndPoint, ArrowRotate, ArrowThickness
        }
        private ActiveDragType currentDragType = ActiveDragType.None;

        // State for drag operations
        private Vector2 dragStartMousePosRelative;
        private float dragStartRotationAngle;
        private Vector2 dragStartPoint1;
        private Vector2 dragStartPoint2;
        private float dragStartValue;

        private int draggedImageResizeHandleIndex = -1;
        private int draggedRectCornerIndex = -1;

        // Scaled handle visual properties
        private float ScaledHandleInteractionRadius => 7f * ImGuiHelpers.GlobalScale;
        private float ScaledHandleDrawRadius => 5f * ImGuiHelpers.GlobalScale;

        // Colors for handles
        private static readonly uint HandleColorDefault = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.9f));
        private static readonly uint HandleColorHover = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.5f, 1.0f));
        private static readonly uint HandleColorRotation = ImGui.GetColorU32(new Vector4(0.5f, 1.0f, 0.5f, 0.9f));
        private static readonly uint HandleColorRotationHover = ImGui.GetColorU32(new Vector4(0.7f, 1.0f, 0.7f, 1.0f));
        private static readonly uint HandleColorThickness = ImGui.GetColorU32(new Vector4(0.5f, 0.7f, 1.0f, 0.9f));
        private static readonly uint HandleColorThicknessHover = ImGui.GetColorU32(new Vector4(0.7f, 0.9f, 1.0f, 1.0f));

        // Processes user interactions for selecting, moving, and resizing shapes.
        public void ProcessInteractions(
            BaseDrawable? singleSelectedItem,
            List<BaseDrawable> selectedDrawables,
            List<BaseDrawable> currentDrawables,
            ref BaseDrawable? hoveredDrawable,
            Vector2 mousePosRelative,
            Vector2 mousePosScreen,
            Vector2 canvasOriginScreen,
            bool canvasInteractLayerHovered,
            bool isLMBClickedOnCanvas,
            bool isLMBDown,
            bool isLMBReleased,
            ImDrawListPtr drawList,
            ref Vector2 lastMouseDragPosForGeneralDrag)
        {
            BaseDrawable? newlyHoveredThisFrame = null;

            if (currentDragType == ActiveDragType.None)
            {
                foreach (var d_item in currentDrawables) { d_item.IsHovered = false; }
            }

            bool mouseOverAnyHandle = false;
            float scaledInteractionRadius = ScaledHandleInteractionRadius;
            float scaledDrawRadius = ScaledHandleDrawRadius;

            // --- Draw Handles for single selected item AND determine if mouse is over any of its handles ---
            if (canvasInteractLayerHovered && currentDragType == ActiveDragType.None && singleSelectedItem != null)
            {
                if (singleSelectedItem is DrawableImage selectedImage)
                {
                    Vector2[] imgCorners = HitDetection.GetRotatedQuadVertices(selectedImage.PositionRelative, selectedImage.DrawSize / 2f, selectedImage.RotationAngle);
                    for (int i = 0; i < 4; i++)
                    {
                        bool isHoveringThisHandle = Vector2.Distance(mousePosRelative, imgCorners[i]) < scaledInteractionRadius;
                        if (isHoveringThisHandle) { mouseOverAnyHandle = true; draggedImageResizeHandleIndex = i; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                        drawList.AddCircleFilled(imgCorners[i] + canvasOriginScreen, scaledDrawRadius, isHoveringThisHandle ? HandleColorHover : HandleColorDefault);
                        if (isHoveringThisHandle) break;
                    }
                    if (!mouseOverAnyHandle) { draggedImageResizeHandleIndex = -1; }

                    // DrawableImage.GetRotationHandleScreenPosition should use scaled constants internally
                    Vector2 imgRotationHandleScreenPos = selectedImage.GetRotationHandleScreenPosition(canvasOriginScreen);
                    bool isHoveringImgRotate = Vector2.Distance(mousePosScreen, imgRotationHandleScreenPos) < scaledInteractionRadius;
                    if (isHoveringImgRotate) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(imgRotationHandleScreenPos, scaledDrawRadius, isHoveringImgRotate ? HandleColorRotationHover : HandleColorRotation);
                }
                else if (singleSelectedItem is DrawableCone selectedCone)
                {
                    Vector2 screenApex = selectedCone.ApexRelative + canvasOriginScreen;
                    Vector2 localBaseCenter = selectedCone.BaseCenterRelative - selectedCone.ApexRelative;
                    Vector2 rotatedLocalBaseCenter = HitDetection.ImRotate(localBaseCenter, MathF.Cos(selectedCone.RotationAngle), MathF.Sin(selectedCone.RotationAngle));
                    Vector2 worldRotatedBaseCenter = selectedCone.ApexRelative + rotatedLocalBaseCenter;
                    Vector2 screenRotatedBaseCenter = worldRotatedBaseCenter + canvasOriginScreen;
                    Vector2 axisDirection = (worldRotatedBaseCenter - selectedCone.ApexRelative).LengthSquared() > 0.001f ? Vector2.Normalize(worldRotatedBaseCenter - selectedCone.ApexRelative) : new Vector2(0, -1);
                    // Scale the offset for the rotation handle
                    Vector2 worldRotationHandlePos = worldRotatedBaseCenter + axisDirection * (scaledInteractionRadius + 15f * ImGuiHelpers.GlobalScale);
                    Vector2 screenRotationHandlePos = worldRotationHandlePos + canvasOriginScreen;

                    bool mouseOverApex = Vector2.Distance(mousePosScreen, screenApex) < scaledInteractionRadius;
                    bool mouseOverBase = Vector2.Distance(mousePosScreen, screenRotatedBaseCenter) < scaledInteractionRadius;
                    bool mouseOverRotateCone = Vector2.Distance(mousePosScreen, screenRotationHandlePos) < scaledInteractionRadius;

                    if (mouseOverRotateCone) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenRotationHandlePos, scaledDrawRadius, mouseOverRotateCone ? HandleColorRotationHover : HandleColorRotation);
                    if (mouseOverApex) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenApex, scaledDrawRadius, mouseOverApex ? HandleColorHover : HandleColorDefault);
                    if (mouseOverBase) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenRotatedBaseCenter, scaledDrawRadius, mouseOverBase ? HandleColorHover : HandleColorDefault);
                }
                else if (singleSelectedItem is DrawableRectangle selectedRect)
                {
                    Vector2[] rotatedCorners = selectedRect.GetRotatedCorners();
                    for (int i = 0; i < 4; i++)
                    {
                        bool isHoveringThisHandle = Vector2.Distance(mousePosRelative, rotatedCorners[i]) < scaledInteractionRadius;
                        if (isHoveringThisHandle) { mouseOverAnyHandle = true; draggedRectCornerIndex = i; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                        drawList.AddCircleFilled(rotatedCorners[i] + canvasOriginScreen, scaledDrawRadius, isHoveringThisHandle ? HandleColorHover : HandleColorDefault);
                        if (isHoveringThisHandle) break;
                    }
                    if (!mouseOverAnyHandle) { draggedRectCornerIndex = -1; }

                    var (_, rectCenter) = selectedRect.GetGeometry();
                    Vector2 upishLocal = selectedRect.StartPointRelative.Y < selectedRect.EndPointRelative.Y ? new Vector2(0, -1) : new Vector2(0, 1);
                    Vector2 rotatedUpish = HitDetection.ImRotate(upishLocal, MathF.Cos(selectedRect.RotationAngle), MathF.Sin(selectedRect.RotationAngle));
                    float halfHeight = Math.Abs(selectedRect.StartPointRelative.Y - selectedRect.EndPointRelative.Y) / 2f;
                    if (halfHeight < scaledInteractionRadius) halfHeight = scaledInteractionRadius;
                    // Scale the offset for the rotation handle
                    Vector2 rotationHandlePos = rectCenter + rotatedUpish * (halfHeight + scaledInteractionRadius + 10f * ImGuiHelpers.GlobalScale);

                    bool isHoveringRectRotate = Vector2.Distance(mousePosRelative, rotationHandlePos) < scaledInteractionRadius;
                    if (isHoveringRectRotate) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(rotationHandlePos + canvasOriginScreen, scaledDrawRadius, isHoveringRectRotate ? HandleColorRotationHover : HandleColorRotation);
                }
                else if (singleSelectedItem is DrawableArrow selectedArrow)
                {
                    // Assume DrawableArrow constants like MinArrowheadDim, ArrowheadLengthFactor are scaled appropriately or used with scaled thickness
                    Vector2 worldStartPoint = selectedArrow.StartPointRelative;
                    Vector2 unrotatedShaftVector = selectedArrow.EndPointRelative - worldStartPoint;
                    Vector2 rotatedShaftVector = HitDetection.ImRotate(unrotatedShaftVector, MathF.Cos(selectedArrow.RotationAngle), MathF.Sin(selectedArrow.RotationAngle));
                    Vector2 worldRotatedShaftEnd = worldStartPoint + rotatedShaftVector;

                    Vector2 shaftDirection = (rotatedShaftVector.LengthSquared() > 0.001f) ? Vector2.Normalize(rotatedShaftVector) : new Vector2(0, -1);
                    float scaledThickness = selectedArrow.Thickness * ImGuiHelpers.GlobalScale; // Assuming selectedArrow.Thickness is unscaled
                    float arrowheadVisualLength = MathF.Max(DrawableArrow.MinArrowheadDim * ImGuiHelpers.GlobalScale, scaledThickness * DrawableArrow.ArrowheadLengthFactor);
                    Vector2 worldVisualTip = worldRotatedShaftEnd + shaftDirection * arrowheadVisualLength;

                    Vector2 screenStartPoint = worldStartPoint + canvasOriginScreen;
                    Vector2 screenVisualTipPoint = worldVisualTip + canvasOriginScreen;

                    // Scale offsets for handles
                    Vector2 rotationHandleOffset = new Vector2(0, -(scaledInteractionRadius + 20f * ImGuiHelpers.GlobalScale));
                    Vector2 rotatedRotationOffset = HitDetection.ImRotate(rotationHandleOffset, MathF.Cos(selectedArrow.RotationAngle), MathF.Sin(selectedArrow.RotationAngle));
                    Vector2 worldRotationHandlePos = worldStartPoint + rotatedRotationOffset;
                    Vector2 screenRotationHandlePos = worldRotationHandlePos + canvasOriginScreen;

                    Vector2 shaftMidPoint = worldStartPoint + rotatedShaftVector / 2f;
                    Vector2 perpDir = shaftDirection.LengthSquared() > 0.001f ? new Vector2(-shaftDirection.Y, shaftDirection.X) : new Vector2(1, 0);
                    Vector2 worldThicknessHandlePos = shaftMidPoint + perpDir * (scaledThickness / 2f + scaledInteractionRadius + 5f * ImGuiHelpers.GlobalScale);
                    Vector2 screenThicknessHandlePos = worldThicknessHandlePos + canvasOriginScreen;

                    bool mouseOverStart = Vector2.Distance(mousePosScreen, screenStartPoint) < scaledInteractionRadius;
                    bool mouseOverEndTip = Vector2.Distance(mousePosScreen, screenVisualTipPoint) < scaledInteractionRadius;
                    bool mouseOverRotateArrow = Vector2.Distance(mousePosScreen, screenRotationHandlePos) < scaledInteractionRadius;
                    bool mouseOverThickness = Vector2.Distance(mousePosScreen, screenThicknessHandlePos) < scaledInteractionRadius;

                    if (mouseOverRotateArrow) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenRotationHandlePos, scaledDrawRadius, mouseOverRotateArrow ? HandleColorRotationHover : HandleColorRotation);
                    if (mouseOverStart) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenStartPoint, scaledDrawRadius, mouseOverStart ? HandleColorHover : HandleColorDefault);
                    if (mouseOverEndTip) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenVisualTipPoint, scaledDrawRadius, mouseOverEndTip ? HandleColorHover : HandleColorDefault);
                    if (mouseOverThickness) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenThicknessHandlePos, scaledDrawRadius, mouseOverThickness ? HandleColorThicknessHover : HandleColorThickness);
                }
            }

            // --- General Shape Hover (only if not over a specific handle and not dragging) ---
            if (canvasInteractLayerHovered && !mouseOverAnyHandle && currentDragType == ActiveDragType.None)
            {
                for (int i = currentDrawables.Count - 1; i >= 0; i--)
                {
                    // Ensure IsHit in BaseDrawable and its overrides uses/expects scaled thresholds
                    if (currentDrawables[i].IsHit(mousePosRelative))
                    {
                        newlyHoveredThisFrame = currentDrawables[i];
                        if (newlyHoveredThisFrame != null && !selectedDrawables.Contains(newlyHoveredThisFrame))
                        {
                            newlyHoveredThisFrame.IsHovered = true;
                        }
                        break;
                    }
                }
            }
            hoveredDrawable = newlyHoveredThisFrame;

            // --- Mouse Click Logic ---
            if (isLMBClickedOnCanvas)
            {
                bool clickedOnAHandle = false;
                if (singleSelectedItem != null && mouseOverAnyHandle)
                {
                    clickedOnAHandle = true;
                    if (singleSelectedItem is DrawableImage selectedImage)
                    {
                        // DrawableImage.GetRotationHandleScreenPosition should use scaled constants
                        Vector2 imgRotationHandleScreenPos = selectedImage.GetRotationHandleScreenPosition(canvasOriginScreen);
                        if (draggedImageResizeHandleIndex != -1) { currentDragType = ActiveDragType.ImageResize; }
                        else if (Vector2.Distance(mousePosScreen, imgRotationHandleScreenPos) < scaledInteractionRadius) { currentDragType = ActiveDragType.ImageRotate; }
                        else { clickedOnAHandle = false; }
                    }
                    else if (singleSelectedItem is DrawableCone selectedCone)
                    {
                        Vector2 screenApex = selectedCone.ApexRelative + canvasOriginScreen;
                        Vector2 localBaseCenter = selectedCone.BaseCenterRelative - selectedCone.ApexRelative;
                        Vector2 rotatedLocalBaseCenter = HitDetection.ImRotate(localBaseCenter, MathF.Cos(selectedCone.RotationAngle), MathF.Sin(selectedCone.RotationAngle));
                        Vector2 worldRotatedBaseCenter = selectedCone.ApexRelative + rotatedLocalBaseCenter;
                        Vector2 axisDirection = (worldRotatedBaseCenter - selectedCone.ApexRelative).LengthSquared() > 0.001f ? Vector2.Normalize(worldRotatedBaseCenter - selectedCone.ApexRelative) : new Vector2(0, -1);
                        // Scale offset for rotation handle
                        Vector2 worldRotationHandlePos = worldRotatedBaseCenter + axisDirection * (scaledInteractionRadius + 15f * ImGuiHelpers.GlobalScale);
                        Vector2 screenRotationHandlePos = worldRotationHandlePos + canvasOriginScreen;

                        if (Vector2.Distance(mousePosScreen, screenRotationHandlePos) < scaledInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ConeRotate; dragStartMousePosRelative = mousePosRelative - selectedCone.ApexRelative; dragStartRotationAngle = selectedCone.RotationAngle;
                        }
                        else if (Vector2.Distance(mousePosScreen, screenApex) < scaledInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ConeApex; dragStartMousePosRelative = mousePosRelative; dragStartPoint1 = selectedCone.ApexRelative; dragStartPoint2 = selectedCone.BaseCenterRelative;
                        }
                        else if (Vector2.Distance(mousePosScreen, (worldRotatedBaseCenter + canvasOriginScreen)) < scaledInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ConeBase; dragStartMousePosRelative = mousePosRelative; dragStartPoint1 = selectedCone.ApexRelative; dragStartPoint2 = selectedCone.BaseCenterRelative;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else if (singleSelectedItem is DrawableRectangle selectedRect)
                    {
                        var (_, rectCenter) = selectedRect.GetGeometry();
                        Vector2[] rotatedCorners = selectedRect.GetRotatedCorners(); // Already relative coordinates
                        Vector2 upishLocal = selectedRect.StartPointRelative.Y < selectedRect.EndPointRelative.Y ? new Vector2(0, -1) : new Vector2(0, 1);
                        Vector2 rotatedUpish = HitDetection.ImRotate(upishLocal, MathF.Cos(selectedRect.RotationAngle), MathF.Sin(selectedRect.RotationAngle));
                        float halfHeight = Math.Abs(selectedRect.StartPointRelative.Y - selectedRect.EndPointRelative.Y) / 2f;
                        if (halfHeight < scaledInteractionRadius) halfHeight = scaledInteractionRadius;
                        // Scale offset for rotation handle
                        Vector2 rotationHandlePos = rectCenter + rotatedUpish * (halfHeight + scaledInteractionRadius + 10f * ImGuiHelpers.GlobalScale);

                        if (draggedRectCornerIndex != -1) // Hit one of the 4 corners
                        {
                            currentDragType = ActiveDragType.RectResize; dragStartMousePosRelative = mousePosRelative; dragStartPoint1 = selectedRect.StartPointRelative; dragStartPoint2 = selectedRect.EndPointRelative; dragStartRotationAngle = selectedRect.RotationAngle;
                        }
                        else if (Vector2.Distance(mousePosRelative, rotationHandlePos) < scaledInteractionRadius) // Hit rotation handle
                        {
                            currentDragType = ActiveDragType.RectRotate; dragStartMousePosRelative = mousePosRelative - rectCenter; dragStartRotationAngle = selectedRect.RotationAngle;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else if (singleSelectedItem is DrawableArrow selectedArrow)
                    {
                        Vector2 worldStartPoint = selectedArrow.StartPointRelative;
                        Vector2 unrotatedShaftVector = selectedArrow.EndPointRelative - worldStartPoint;
                        Vector2 rotatedShaftVector = HitDetection.ImRotate(unrotatedShaftVector, MathF.Cos(selectedArrow.RotationAngle), MathF.Sin(selectedArrow.RotationAngle));
                        Vector2 worldRotatedShaftEnd = worldStartPoint + rotatedShaftVector;
                        Vector2 shaftDirectionOnClick = (rotatedShaftVector.LengthSquared() > 0.001f) ? Vector2.Normalize(rotatedShaftVector) : new Vector2(0, -1);

                        float scaledThickness = selectedArrow.Thickness * ImGuiHelpers.GlobalScale;
                        float arrowheadVisualLengthOnClick = MathF.Max(DrawableArrow.MinArrowheadDim * ImGuiHelpers.GlobalScale, scaledThickness * DrawableArrow.ArrowheadLengthFactor);
                        Vector2 worldVisualTipOnClick = worldRotatedShaftEnd + shaftDirectionOnClick * arrowheadVisualLengthOnClick;

                        // Scale offset for rotation handle
                        Vector2 rotationHandleOffset = new Vector2(0, -(scaledInteractionRadius + 20f * ImGuiHelpers.GlobalScale));
                        Vector2 rotatedRotationOffset = HitDetection.ImRotate(rotationHandleOffset, MathF.Cos(selectedArrow.RotationAngle), MathF.Sin(selectedArrow.RotationAngle));
                        Vector2 worldRotationHandlePos = worldStartPoint + rotatedRotationOffset;

                        Vector2 shaftMidPoint = worldStartPoint + rotatedShaftVector / 2f;
                        Vector2 perpDir = shaftDirectionOnClick.LengthSquared() > 0.001f ? new Vector2(-shaftDirectionOnClick.Y, shaftDirectionOnClick.X) : new Vector2(1, 0);
                        // Scale offset for thickness handle
                        Vector2 worldThicknessHandlePos = shaftMidPoint + perpDir * (scaledThickness / 2f + scaledInteractionRadius + 5f * ImGuiHelpers.GlobalScale);

                        if (Vector2.Distance(mousePosScreen, worldRotationHandlePos + canvasOriginScreen) < scaledInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ArrowRotate; dragStartMousePosRelative = mousePosRelative - worldStartPoint; dragStartRotationAngle = selectedArrow.RotationAngle;
                        }
                        else if (Vector2.Distance(mousePosScreen, worldStartPoint + canvasOriginScreen) < scaledInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ArrowStartPoint; dragStartMousePosRelative = mousePosRelative; dragStartPoint1 = selectedArrow.StartPointRelative; dragStartPoint2 = selectedArrow.EndPointRelative;
                        }
                        else if (Vector2.Distance(mousePosScreen, worldVisualTipOnClick + canvasOriginScreen) < scaledInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ArrowEndPoint; dragStartPoint1 = selectedArrow.StartPointRelative; dragStartPoint2 = selectedArrow.EndPointRelative; dragStartRotationAngle = selectedArrow.RotationAngle;
                        }
                        else if (Vector2.Distance(mousePosScreen, worldThicknessHandlePos + canvasOriginScreen) < scaledInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ArrowThickness; dragStartMousePosRelative = mousePosRelative; dragStartValue = selectedArrow.Thickness; /* Store unscaled thickness */ dragStartPoint1 = worldStartPoint; dragStartPoint2 = worldRotatedShaftEnd;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else { clickedOnAHandle = false; } // Not a known draggable shape type or no handle matched.

                    if (clickedOnAHandle && !singleSelectedItem.IsSelected)
                    {
                        foreach (var d_sel_loop in selectedDrawables) d_sel_loop.IsSelected = false; selectedDrawables.Clear();
                        singleSelectedItem.IsSelected = true; selectedDrawables.Add(singleSelectedItem);
                    }
                }

                if (!clickedOnAHandle) // Click was not on a handle or was on an unselected item.
                {
                    if (hoveredDrawable != null)
                    {
                        BaseDrawable actualHoveredDrawable = hoveredDrawable;
                        if (!ImGui.GetIO().KeyCtrl && !selectedDrawables.Contains(actualHoveredDrawable))
                        {
                            foreach (var d_sel in selectedDrawables) d_sel.IsSelected = false;
                            selectedDrawables.Clear();
                        }
                        if (selectedDrawables.Contains(actualHoveredDrawable))
                        {
                            if (ImGui.GetIO().KeyCtrl) { actualHoveredDrawable.IsSelected = false; selectedDrawables.Remove(actualHoveredDrawable); }
                        }
                        else
                        {
                            actualHoveredDrawable.IsSelected = true;
                            selectedDrawables.Add(actualHoveredDrawable);
                        }
                        actualHoveredDrawable.IsHovered = false;
                        if (selectedDrawables.Any(d => d.Equals(actualHoveredDrawable) && d.IsSelected))
                        {
                            currentDragType = ActiveDragType.GeneralSelection;
                            lastMouseDragPosForGeneralDrag = mousePosRelative;
                        }
                    }
                    else // Clicked on empty canvas.
                    {
                        foreach (var d_sel in selectedDrawables) d_sel.IsSelected = false;
                        selectedDrawables.Clear();
                        currentDragType = ActiveDragType.None;
                    }
                }
            }

            // --- Mouse Drag Logic ---
            if (isLMBDown && currentDragType != ActiveDragType.None && singleSelectedItem != null)
            {
                if (singleSelectedItem is DrawableImage selectedImage) // Renamed to selectedImage to avoid conflict
                {
                    if (currentDragType == ActiveDragType.ImageResize && draggedImageResizeHandleIndex != -1)
                    {
                        Vector2 imageCenterRel = selectedImage.PositionRelative;
                        float angle = selectedImage.RotationAngle;
                        Vector2 mouseRelativeToCenter = mousePosRelative - imageCenterRel;
                        Vector2 localMousePos = HitDetection.ImRotate(mouseRelativeToCenter, MathF.Cos(-angle), MathF.Sin(-angle));
                        Vector2 newHalfSize;
                        switch (draggedImageResizeHandleIndex)
                        {
                            case 0: newHalfSize = new Vector2(-localMousePos.X, -localMousePos.Y); break;
                            case 1: newHalfSize = new Vector2(localMousePos.X, -localMousePos.Y); break;
                            case 2: newHalfSize = new Vector2(localMousePos.X, localMousePos.Y); break;
                            case 3: newHalfSize = new Vector2(-localMousePos.X, localMousePos.Y); break;
                            default: newHalfSize = selectedImage.DrawSize / 2f; break; // DrawSize is logical/unscaled here
                        }
                        // Ensure minimum size after scaling
                        // DrawableImage.UnscaledResizeHandleRadius is the base unscaled value.
                        float minDimLogical = DrawableImage.UnscaledResizeHandleRadius * 4; // Logical minimum dimension based on handle
                                                                                            // newHalfSize is logical. Compare with logical minDim.
                                                                                            // The DrawSize property of DrawableImage stores logical (unscaled) dimensions.
                        selectedImage.DrawSize = new Vector2(
                            MathF.Max(newHalfSize.X * 2, minDimLogical),
                            MathF.Max(newHalfSize.Y * 2, minDimLogical)
                        );
                    }
                    else if (currentDragType == ActiveDragType.ImageRotate)
                    {
                        Vector2 imageCenterRel = selectedImage.PositionRelative;
                        float dX = mousePosRelative.X - imageCenterRel.X;
                        float dY = mousePosRelative.Y - imageCenterRel.Y;
                        selectedImage.RotationAngle = MathF.Atan2(dY, dX) + MathF.PI / 2f;
                    }
                }
            }

            if (isLMBDown && currentDragType == ActiveDragType.GeneralSelection)
            {
                Vector2 mouseDelta = mousePosRelative - lastMouseDragPosForGeneralDrag;
                if (mouseDelta.LengthSquared() > 0.001f)
                {
                    foreach (var item in selectedDrawables) item.Translate(mouseDelta);
                }
                lastMouseDragPosForGeneralDrag = mousePosRelative;
            }

            if (isLMBReleased)
            {
                currentDragType = ActiveDragType.None;
                draggedImageResizeHandleIndex = -1;
                draggedRectCornerIndex = -1;
            }
        }

        public void ResetDragState()
        {
            currentDragType = ActiveDragType.None;
            draggedImageResizeHandleIndex = -1;
            draggedRectCornerIndex = -1;
        }
    }
}
