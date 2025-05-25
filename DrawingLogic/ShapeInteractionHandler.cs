using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using System.Linq;

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

        // Handle visual properties
        private const float HandleInteractionRadius = 7f;
        private const float HandleDrawRadius = 5f;
        private static readonly uint HandleColorDefault = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.9f));
        private static readonly uint HandleColorHover = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.5f, 1.0f));
        private static readonly uint HandleColorRotation = ImGui.GetColorU32(new Vector4(0.5f, 1.0f, 0.5f, 0.9f));
        private static readonly uint HandleColorRotationHover = ImGui.GetColorU32(new Vector4(0.7f, 1.0f, 0.7f, 1.0f));
        private static readonly uint HandleColorThickness = ImGui.GetColorU32(new Vector4(0.5f, 0.7f, 1.0f, 0.9f));
        private static readonly uint HandleColorThicknessHover = ImGui.GetColorU32(new Vector4(0.7f, 0.9f, 1.0f, 1.0f));

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

            // --- Draw Handles for single selected item AND determine if mouse is over any of its handles ---
            if (canvasInteractLayerHovered && currentDragType == ActiveDragType.None && singleSelectedItem != null)
            {
                if (singleSelectedItem is DrawableImage selectedImage)
                {
                    Vector2[] imgCorners = HitDetection.GetRotatedQuadVertices(selectedImage.PositionRelative, selectedImage.DrawSize / 2f, selectedImage.RotationAngle);
                    for (int i = 0; i < 4; i++)
                    {
                        bool isHoveringThisHandle = Vector2.Distance(mousePosRelative, imgCorners[i]) < HandleInteractionRadius;
                        if (isHoveringThisHandle) { mouseOverAnyHandle = true; draggedImageResizeHandleIndex = i; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                        drawList.AddCircleFilled(imgCorners[i] + canvasOriginScreen, HandleDrawRadius, isHoveringThisHandle ? HandleColorHover : HandleColorDefault);
                        if (isHoveringThisHandle) break;
                    }
                    if (!mouseOverAnyHandle) { draggedImageResizeHandleIndex = -1; }

                    Vector2 imgRotationHandleScreenPos = selectedImage.GetRotationHandleScreenPosition(canvasOriginScreen);
                    bool isHoveringImgRotate = Vector2.Distance(mousePosScreen, imgRotationHandleScreenPos) < HandleInteractionRadius;
                    if (isHoveringImgRotate) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(imgRotationHandleScreenPos, HandleDrawRadius, isHoveringImgRotate ? HandleColorRotationHover : HandleColorRotation);
                }
                else if (singleSelectedItem is DrawableCone selectedCone)
                {
                    Vector2 screenApex = selectedCone.ApexRelative + canvasOriginScreen;
                    Vector2 localBaseCenter = selectedCone.BaseCenterRelative - selectedCone.ApexRelative;
                    Vector2 rotatedLocalBaseCenter = HitDetection.ImRotate(localBaseCenter, MathF.Cos(selectedCone.RotationAngle), MathF.Sin(selectedCone.RotationAngle));
                    Vector2 worldRotatedBaseCenter = selectedCone.ApexRelative + rotatedLocalBaseCenter;
                    Vector2 screenRotatedBaseCenter = worldRotatedBaseCenter + canvasOriginScreen;
                    Vector2 axisDirection = (worldRotatedBaseCenter - selectedCone.ApexRelative).LengthSquared() > 0.001f ? Vector2.Normalize(worldRotatedBaseCenter - selectedCone.ApexRelative) : new Vector2(0, -1);
                    Vector2 worldRotationHandlePos = worldRotatedBaseCenter + axisDirection * (HandleInteractionRadius + 15f);
                    Vector2 screenRotationHandlePos = worldRotationHandlePos + canvasOriginScreen;

                    bool mouseOverApex = Vector2.Distance(mousePosScreen, screenApex) < HandleInteractionRadius;
                    bool mouseOverBase = Vector2.Distance(mousePosScreen, screenRotatedBaseCenter) < HandleInteractionRadius;
                    bool mouseOverRotateCone = Vector2.Distance(mousePosScreen, screenRotationHandlePos) < HandleInteractionRadius;

                    if (mouseOverRotateCone) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenRotationHandlePos, HandleDrawRadius, mouseOverRotateCone ? HandleColorRotationHover : HandleColorRotation);
                    if (mouseOverApex) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenApex, HandleDrawRadius, mouseOverApex ? HandleColorHover : HandleColorDefault);
                    if (mouseOverBase) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenRotatedBaseCenter, HandleDrawRadius, mouseOverBase ? HandleColorHover : HandleColorDefault);
                }
                else if (singleSelectedItem is DrawableRectangle selectedRect)
                {
                    Vector2[] rotatedCorners = selectedRect.GetRotatedCorners();
                    for (int i = 0; i < 4; i++)
                    {
                        bool isHoveringThisHandle = Vector2.Distance(mousePosRelative, rotatedCorners[i]) < HandleInteractionRadius;
                        if (isHoveringThisHandle) { mouseOverAnyHandle = true; draggedRectCornerIndex = i; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                        drawList.AddCircleFilled(rotatedCorners[i] + canvasOriginScreen, HandleDrawRadius, isHoveringThisHandle ? HandleColorHover : HandleColorDefault);
                        if (isHoveringThisHandle) break;
                    }
                    if (!mouseOverAnyHandle) { draggedRectCornerIndex = -1; }

                    var (_, rectCenter) = selectedRect.GetGeometry();
                    Vector2 upishLocal = selectedRect.StartPointRelative.Y < selectedRect.EndPointRelative.Y ? new Vector2(0, -1) : new Vector2(0, 1);
                    Vector2 rotatedUpish = HitDetection.ImRotate(upishLocal, MathF.Cos(selectedRect.RotationAngle), MathF.Sin(selectedRect.RotationAngle));
                    float halfHeight = Math.Abs(selectedRect.StartPointRelative.Y - selectedRect.EndPointRelative.Y) / 2f;
                    if (halfHeight < HandleInteractionRadius) halfHeight = HandleInteractionRadius;
                    Vector2 rotationHandlePos = rectCenter + rotatedUpish * (halfHeight + HandleInteractionRadius + 10f);

                    bool isHoveringRectRotate = Vector2.Distance(mousePosRelative, rotationHandlePos) < HandleInteractionRadius;
                    if (isHoveringRectRotate) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(rotationHandlePos + canvasOriginScreen, HandleDrawRadius, isHoveringRectRotate ? HandleColorRotationHover : HandleColorRotation);
                }
                else if (singleSelectedItem is DrawableArrow selectedArrow)
                {
                    Vector2 worldStartPoint = selectedArrow.StartPointRelative;
                    Vector2 unrotatedShaftVector = selectedArrow.EndPointRelative - worldStartPoint;
                    Vector2 rotatedShaftVector = HitDetection.ImRotate(unrotatedShaftVector, MathF.Cos(selectedArrow.RotationAngle), MathF.Sin(selectedArrow.RotationAngle));
                    Vector2 worldRotatedShaftEnd = worldStartPoint + rotatedShaftVector;

                    Vector2 shaftDirection = (rotatedShaftVector.LengthSquared() > 0.001f) ? Vector2.Normalize(rotatedShaftVector) : new Vector2(0, -1);
                    float arrowheadVisualLength = MathF.Max(DrawableArrow.MinArrowheadDim, selectedArrow.Thickness * DrawableArrow.ArrowheadLengthFactor);
                    Vector2 worldVisualTip = worldRotatedShaftEnd + shaftDirection * arrowheadVisualLength;

                    Vector2 screenStartPoint = worldStartPoint + canvasOriginScreen;
                    Vector2 screenVisualTipPoint = worldVisualTip + canvasOriginScreen;

                    Vector2 rotationHandleOffset = new Vector2(0, -(HandleInteractionRadius + 20f));
                    Vector2 rotatedRotationOffset = HitDetection.ImRotate(rotationHandleOffset, MathF.Cos(selectedArrow.RotationAngle), MathF.Sin(selectedArrow.RotationAngle));
                    Vector2 worldRotationHandlePos = worldStartPoint + rotatedRotationOffset;
                    Vector2 screenRotationHandlePos = worldRotationHandlePos + canvasOriginScreen;

                    Vector2 shaftMidPoint = worldStartPoint + rotatedShaftVector / 2f;
                    Vector2 perpDir = shaftDirection.LengthSquared() > 0.001f ? new Vector2(-shaftDirection.Y, shaftDirection.X) : new Vector2(1, 0);
                    Vector2 worldThicknessHandlePos = shaftMidPoint + perpDir * (selectedArrow.Thickness / 2f + HandleInteractionRadius + 5f);
                    Vector2 screenThicknessHandlePos = worldThicknessHandlePos + canvasOriginScreen;

                    bool mouseOverStart = Vector2.Distance(mousePosScreen, screenStartPoint) < HandleInteractionRadius;
                    bool mouseOverEndTip = Vector2.Distance(mousePosScreen, screenVisualTipPoint) < HandleInteractionRadius;
                    bool mouseOverRotateArrow = Vector2.Distance(mousePosScreen, screenRotationHandlePos) < HandleInteractionRadius;
                    bool mouseOverThickness = Vector2.Distance(mousePosScreen, screenThicknessHandlePos) < HandleInteractionRadius;

                    if (mouseOverRotateArrow) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenRotationHandlePos, HandleDrawRadius, mouseOverRotateArrow ? HandleColorRotationHover : HandleColorRotation);

                    if (mouseOverStart) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenStartPoint, HandleDrawRadius, mouseOverStart ? HandleColorHover : HandleColorDefault);

                    if (mouseOverEndTip) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenVisualTipPoint, HandleDrawRadius, mouseOverEndTip ? HandleColorHover : HandleColorDefault);

                    if (mouseOverThickness) { mouseOverAnyHandle = true; ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                    drawList.AddCircleFilled(screenThicknessHandlePos, HandleDrawRadius, mouseOverThickness ? HandleColorThicknessHover : HandleColorThickness);
                }
            }

            // --- General Shape Hover (only if not over a specific handle and not dragging) ---
            if (canvasInteractLayerHovered && !mouseOverAnyHandle && currentDragType == ActiveDragType.None)
            {
                for (int i = currentDrawables.Count - 1; i >= 0; i--)
                {
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
                        Vector2 imgRotationHandleScreenPos = selectedImage.GetRotationHandleScreenPosition(canvasOriginScreen);
                        if (draggedImageResizeHandleIndex != -1) { currentDragType = ActiveDragType.ImageResize; }
                        else if (Vector2.Distance(mousePosScreen, imgRotationHandleScreenPos) < HandleInteractionRadius) { currentDragType = ActiveDragType.ImageRotate; }
                        else { clickedOnAHandle = false; }
                    }
                    else if (singleSelectedItem is DrawableCone selectedCone)
                    {
                        Vector2 screenApex = selectedCone.ApexRelative + canvasOriginScreen;
                        Vector2 localBaseCenter = selectedCone.BaseCenterRelative - selectedCone.ApexRelative;
                        Vector2 rotatedLocalBaseCenter = HitDetection.ImRotate(localBaseCenter, MathF.Cos(selectedCone.RotationAngle), MathF.Sin(selectedCone.RotationAngle));
                        Vector2 worldRotatedBaseCenter = selectedCone.ApexRelative + rotatedLocalBaseCenter;
                        Vector2 axisDirection = (worldRotatedBaseCenter - selectedCone.ApexRelative).LengthSquared() > 0.001f ? Vector2.Normalize(worldRotatedBaseCenter - selectedCone.ApexRelative) : new Vector2(0, -1);
                        Vector2 worldRotationHandlePos = worldRotatedBaseCenter + axisDirection * (HandleInteractionRadius + 15f);
                        Vector2 screenRotationHandlePos = worldRotationHandlePos + canvasOriginScreen;

                        if (Vector2.Distance(mousePosScreen, screenRotationHandlePos) < HandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ConeRotate; dragStartMousePosRelative = mousePosRelative - selectedCone.ApexRelative; dragStartRotationAngle = selectedCone.RotationAngle;
                        }
                        else if (Vector2.Distance(mousePosScreen, screenApex) < HandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ConeApex; dragStartMousePosRelative = mousePosRelative; dragStartPoint1 = selectedCone.ApexRelative; dragStartPoint2 = selectedCone.BaseCenterRelative;
                        }
                        else if (Vector2.Distance(mousePosScreen, (worldRotatedBaseCenter + canvasOriginScreen)) < HandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ConeBase; dragStartMousePosRelative = mousePosRelative; dragStartPoint1 = selectedCone.ApexRelative; dragStartPoint2 = selectedCone.BaseCenterRelative;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else if (singleSelectedItem is DrawableRectangle selectedRect)
                    {
                        var (_, rectCenter) = selectedRect.GetGeometry();
                        Vector2[] rotatedCorners = selectedRect.GetRotatedCorners();
                        Vector2 upishLocal = selectedRect.StartPointRelative.Y < selectedRect.EndPointRelative.Y ? new Vector2(0, -1) : new Vector2(0, 1);
                        Vector2 rotatedUpish = HitDetection.ImRotate(upishLocal, MathF.Cos(selectedRect.RotationAngle), MathF.Sin(selectedRect.RotationAngle));
                        float halfHeight = Math.Abs(selectedRect.StartPointRelative.Y - selectedRect.EndPointRelative.Y) / 2f;
                        if (halfHeight < HandleInteractionRadius) halfHeight = HandleInteractionRadius;
                        Vector2 rotationHandlePos = rectCenter + rotatedUpish * (halfHeight + HandleInteractionRadius + 10f);

                        if (draggedRectCornerIndex != -1)
                        {
                            currentDragType = ActiveDragType.RectResize; dragStartMousePosRelative = mousePosRelative; dragStartPoint1 = selectedRect.StartPointRelative; dragStartPoint2 = selectedRect.EndPointRelative; dragStartRotationAngle = selectedRect.RotationAngle;
                        }
                        else if (Vector2.Distance(mousePosRelative, rotationHandlePos) < HandleInteractionRadius)
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
                        float arrowheadVisualLengthOnClick = MathF.Max(DrawableArrow.MinArrowheadDim, selectedArrow.Thickness * DrawableArrow.ArrowheadLengthFactor);
                        Vector2 worldVisualTipOnClick = worldRotatedShaftEnd + shaftDirectionOnClick * arrowheadVisualLengthOnClick;

                        Vector2 rotationHandleOffset = new Vector2(0, -(HandleInteractionRadius + 20f));
                        Vector2 rotatedRotationOffset = HitDetection.ImRotate(rotationHandleOffset, MathF.Cos(selectedArrow.RotationAngle), MathF.Sin(selectedArrow.RotationAngle));
                        Vector2 worldRotationHandlePos = worldStartPoint + rotatedRotationOffset;

                        Vector2 shaftMidPoint = worldStartPoint + rotatedShaftVector / 2f;
                        Vector2 perpDir = shaftDirectionOnClick.LengthSquared() > 0.001f ? new Vector2(-shaftDirectionOnClick.Y, shaftDirectionOnClick.X) : new Vector2(1, 0);
                        Vector2 worldThicknessHandlePos = shaftMidPoint + perpDir * (selectedArrow.Thickness / 2f + HandleInteractionRadius + 5f);

                        if (Vector2.Distance(mousePosScreen, worldRotationHandlePos + canvasOriginScreen) < HandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ArrowRotate; dragStartMousePosRelative = mousePosRelative - worldStartPoint; dragStartRotationAngle = selectedArrow.RotationAngle;
                        }
                        else if (Vector2.Distance(mousePosScreen, worldStartPoint + canvasOriginScreen) < HandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ArrowStartPoint; dragStartMousePosRelative = mousePosRelative; dragStartPoint1 = selectedArrow.StartPointRelative; dragStartPoint2 = selectedArrow.EndPointRelative;
                        }
                        else if (Vector2.Distance(mousePosScreen, worldVisualTipOnClick + canvasOriginScreen) < HandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ArrowEndPoint; dragStartPoint1 = selectedArrow.StartPointRelative; dragStartPoint2 = selectedArrow.EndPointRelative; dragStartRotationAngle = selectedArrow.RotationAngle;
                        }
                        else if (Vector2.Distance(mousePosScreen, worldThicknessHandlePos + canvasOriginScreen) < HandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ArrowThickness; dragStartMousePosRelative = mousePosRelative; dragStartValue = selectedArrow.Thickness; dragStartPoint1 = worldStartPoint; dragStartPoint2 = worldRotatedShaftEnd;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else { clickedOnAHandle = false; }

                    if (clickedOnAHandle && !singleSelectedItem.IsSelected)
                    {
                        foreach (var d_sel_loop in selectedDrawables) d_sel_loop.IsSelected = false; selectedDrawables.Clear();
                        singleSelectedItem.IsSelected = true; selectedDrawables.Add(singleSelectedItem);
                    }
                }

                if (!clickedOnAHandle)
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
                    else
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
                if (singleSelectedItem is DrawableImage selectedImage)
                {
                    if (currentDragType == ActiveDragType.ImageResize && draggedImageResizeHandleIndex != -1)
                    {
                        Vector2 imageCenterRel = selectedImage.PositionRelative; float angle = selectedImage.RotationAngle; Vector2 mouseRelativeToCenter = mousePosRelative - imageCenterRel;
                        Vector2 localMousePos = HitDetection.ImRotate(mouseRelativeToCenter, MathF.Cos(-angle), MathF.Sin(-angle)); Vector2 newHalfSize;
                        switch (draggedImageResizeHandleIndex) { case 0: newHalfSize = new Vector2(-localMousePos.X, -localMousePos.Y); break; case 1: newHalfSize = new Vector2(localMousePos.X, -localMousePos.Y); break; case 2: newHalfSize = new Vector2(localMousePos.X, localMousePos.Y); break; case 3: newHalfSize = new Vector2(-localMousePos.X, localMousePos.Y); break; default: newHalfSize = selectedImage.DrawSize / 2f; break; }
                        selectedImage.DrawSize = new Vector2(MathF.Max(newHalfSize.X * 2, DrawableImage.ResizeHandleRadius * 4), MathF.Max(newHalfSize.Y * 2, DrawableImage.ResizeHandleRadius * 4));
                    }
                    else if (currentDragType == ActiveDragType.ImageRotate)
                    {
                        Vector2 imageCenterRel = selectedImage.PositionRelative; float dX = mousePosRelative.X - imageCenterRel.X; float dY = mousePosRelative.Y - imageCenterRel.Y; selectedImage.RotationAngle = MathF.Atan2(dY, dX) + MathF.PI / 2f;
                    }
                }
                else if (singleSelectedItem is DrawableCone selectedCone)
                {
                    if (currentDragType == ActiveDragType.ConeRotate)
                    {
                        Vector2 currentMouseVecFromApex = mousePosRelative - selectedCone.ApexRelative; float currentMouseAngleFromApex = MathF.Atan2(currentMouseVecFromApex.Y, currentMouseVecFromApex.X); float initialMouseVecAngleFromApex = MathF.Atan2(dragStartMousePosRelative.Y, dragStartMousePosRelative.X); float angleDelta = currentMouseAngleFromApex - initialMouseVecAngleFromApex; selectedCone.RotationAngle = dragStartRotationAngle + angleDelta;
                    }
                    else if (currentDragType == ActiveDragType.ConeApex)
                    {
                        Vector2 mouseDelta = mousePosRelative - dragStartMousePosRelative; selectedCone.ApexRelative = dragStartPoint1 + mouseDelta; selectedCone.BaseCenterRelative = dragStartPoint2 + mouseDelta;
                    }
                    else if (currentDragType == ActiveDragType.ConeBase)
                    {
                        Vector2 localMousePos = mousePosRelative - selectedCone.ApexRelative; Vector2 unrotatedLocalMousePos = HitDetection.ImRotate(localMousePos, MathF.Cos(-selectedCone.RotationAngle), MathF.Sin(-selectedCone.RotationAngle)); selectedCone.BaseCenterRelative = selectedCone.ApexRelative + unrotatedLocalMousePos;
                    }
                }
                else if (singleSelectedItem is DrawableRectangle selectedRect)
                {
                    if (currentDragType == ActiveDragType.RectRotate)
                    {
                        var (_, rectCenter) = selectedRect.GetGeometry(); Vector2 currentMouseVecFromCenter = mousePosRelative - rectCenter; float currentMouseAngle = MathF.Atan2(currentMouseVecFromCenter.Y, currentMouseVecFromCenter.X); float initialMouseVecAngle = MathF.Atan2(dragStartMousePosRelative.Y, dragStartMousePosRelative.X); float angleDelta = currentMouseAngle - initialMouseVecAngle; selectedRect.RotationAngle = dragStartRotationAngle + angleDelta;
                    }
                    else if (currentDragType == ActiveDragType.RectResize && draggedRectCornerIndex != -1)
                    {
                        Vector2 originalP1 = dragStartPoint1; Vector2 originalP2 = dragStartPoint2; Vector2 tempMin = new Vector2(Math.Min(originalP1.X, originalP2.X), Math.Min(originalP1.Y, originalP2.Y)); Vector2 tempMax = new Vector2(Math.Max(originalP1.X, originalP2.X), Math.Max(originalP1.Y, originalP2.Y)); Vector2 originalCenterForResize = (tempMin + tempMax) / 2f; Vector2 localNewMousePos = HitDetection.ImRotate(mousePosRelative - originalCenterForResize, MathF.Cos(-dragStartRotationAngle), MathF.Sin(-dragStartRotationAngle)) + originalCenterForResize; Vector2 p1 = originalP1; Vector2 p2 = originalP2;
                        if (draggedRectCornerIndex == 0) { p1 = localNewMousePos; } else if (draggedRectCornerIndex == 1) { p2.X = localNewMousePos.X; p1.Y = localNewMousePos.Y; } else if (draggedRectCornerIndex == 2) { p2 = localNewMousePos; } else if (draggedRectCornerIndex == 3) { p1.X = localNewMousePos.X; p2.Y = localNewMousePos.Y; }
                        selectedRect.StartPointRelative = new Vector2(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y)); selectedRect.EndPointRelative = new Vector2(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y)); selectedRect.RotationAngle = dragStartRotationAngle;
                    }
                }
                else if (singleSelectedItem is DrawableArrow selectedArrow)
                {
                    if (currentDragType == ActiveDragType.ArrowStartPoint)
                    {
                        Vector2 mouseDelta = mousePosRelative - dragStartMousePosRelative;
                        selectedArrow.StartPointRelative = dragStartPoint1 + mouseDelta;
                        selectedArrow.EndPointRelative = dragStartPoint2 + mouseDelta;
                    }
                    else if (currentDragType == ActiveDragType.ArrowEndPoint)
                    {
                        Vector2 pivotPoint = dragStartPoint1;
                        Vector2 vectorToMouseInWorld_Unpivoted = mousePosRelative - pivotPoint;
                        Vector2 unrotated_vectorToMouse_Local = HitDetection.ImRotate(vectorToMouseInWorld_Unpivoted, MathF.Cos(-selectedArrow.RotationAngle), MathF.Sin(-selectedArrow.RotationAngle));

                        float arrowheadVisualLengthCurrent = MathF.Max(DrawableArrow.MinArrowheadDim, selectedArrow.Thickness * DrawableArrow.ArrowheadLengthFactor);

                        if (unrotated_vectorToMouse_Local.LengthSquared() > 0.0001f)
                        {
                            float currentLength = unrotated_vectorToMouse_Local.Length();
                            Vector2 unrotatedShaftDirection = Vector2.Normalize(unrotated_vectorToMouse_Local);
                            float newShaftLength = currentLength - arrowheadVisualLengthCurrent;

                            if (newShaftLength < 0) newShaftLength = 0;

                            selectedArrow.EndPointRelative = pivotPoint + unrotatedShaftDirection * newShaftLength;
                        }
                        else
                        {
                            selectedArrow.EndPointRelative = pivotPoint;
                        }
                    }
                    else if (currentDragType == ActiveDragType.ArrowRotate)
                    {
                        Vector2 currentMouseVecFromPivot = mousePosRelative - selectedArrow.StartPointRelative;
                        float currentMouseAngle = MathF.Atan2(currentMouseVecFromPivot.Y, currentMouseVecFromPivot.X);
                        float initialMouseVecAngle = MathF.Atan2(dragStartMousePosRelative.Y, dragStartMousePosRelative.X);
                        float angleDelta = currentMouseAngle - initialMouseVecAngle;
                        selectedArrow.RotationAngle = dragStartRotationAngle + angleDelta;
                    }
                    else if (currentDragType == ActiveDragType.ArrowThickness)
                    {
                        Vector2 shaftStartAtDrag = dragStartPoint1;
                        Vector2 shaftEndAtDrag = dragStartPoint2;

                        Vector2 shaftDirectionAtDrag;
                        if ((shaftEndAtDrag - shaftStartAtDrag).LengthSquared() > 0.001f)
                            shaftDirectionAtDrag = Vector2.Normalize(shaftEndAtDrag - shaftStartAtDrag);
                        else
                            shaftDirectionAtDrag = new Vector2(0, 1);

                        Vector2 perpendicularDirection = new Vector2(-shaftDirectionAtDrag.Y, shaftDirectionAtDrag.X);
                        Vector2 mouseDelta = mousePosRelative - dragStartMousePosRelative;
                        float thicknessProjection = Vector2.Dot(mouseDelta, perpendicularDirection);

                        float sensitivity = 0.25f;
                        float newThickness = dragStartValue + thicknessProjection * sensitivity;

                        selectedArrow.Thickness = Math.Clamp(newThickness, 1f, 50f);
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
