// AetherDraw/DrawingLogic/ShapeInteractionHandler.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using System.Linq;
using AetherDraw.Core;
using Dalamud.Interface.Utility;
using SixLabors.ImageSharp;

namespace AetherDraw.DrawingLogic
{
    public class ShapeInteractionHandler
    {
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;

        public enum ActiveDragType
        {
            None, GeneralSelection, ImageResize, ImageRotate, ConeApex, ConeBase, ConeRotate,
            RectResize, RectRotate, ArrowStartPoint, ArrowEndPoint, ArrowRotate, ArrowThickness, TextResize
        }
        private ActiveDragType currentDragType = ActiveDragType.None;
        public ActiveDragType GetCurrentDragType() => currentDragType;

        private Vector2 dragStartMousePosLogical;
        private Vector2 dragStartObjectPivotLogical;
        private float dragStartRotationAngle;
        private Vector2 dragStartPoint1Logical;
        private Vector2 dragStartPoint2Logical;
        private Vector2 dragStartSizeLogical;
        private float dragStartValueLogical;
        private Vector2 dragStartTextPositionLogical;
        private Vector2 dragStartTextBoundingBoxSizeLogical;
        private float dragStartFontSizeLogical;

        private int draggedImageResizeHandleIndex = -1;
        private int draggedRectCornerIndex = -1;
        private int draggedArrowHandleIndex = -1;
        private int draggedTextResizeHandleIndex = -1;

        private const float LogicalHandleInteractionRadius = 7f;
        private float ScaledHandleDrawRadius => 5f * ImGuiHelpers.GlobalScale;

        private readonly uint handleColorDefault;
        private readonly uint handleColorHover;
        private readonly uint handleColorRotation;
        private readonly uint handleColorRotationHover;
        private readonly uint handleColorResize;
        private readonly uint handleColorResizeHover;
        private readonly uint handleColorSpecial;
        private readonly uint handleColorSpecialHover;

        public ShapeInteractionHandler(UndoManager undoManagerInstance, PageManager pageManagerInstance)
        {
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

        private bool DrawAndCheckHandle(ImDrawListPtr drawList, Vector2 logicalHandlePos, Vector2 canvasOriginScreen,
                                        Vector2 mousePosLogical, ref bool mouseOverAnyHandleFlag,
                                        ImGuiMouseCursor cursor = ImGuiMouseCursor.Hand, uint color = 0, uint hoverColor = 0)
        {
            uint actualColor = color == 0 ? this.handleColorDefault : color;
            uint actualHoverColor = hoverColor == 0 ? this.handleColorHover : hoverColor;

            Vector2 screenHandlePos = logicalHandlePos * ImGuiHelpers.GlobalScale + canvasOriginScreen;
            bool isHoveringThisHandle = Vector2.Distance(mousePosLogical, logicalHandlePos) < LogicalHandleInteractionRadius;

            if (isHoveringThisHandle)
            {
                mouseOverAnyHandleFlag = true;
                ImGui.SetMouseCursor(cursor);
            }
            drawList.AddCircleFilled(screenHandlePos, ScaledHandleDrawRadius, isHoveringThisHandle ? actualHoverColor : actualColor);
            drawList.AddCircle(screenHandlePos, ScaledHandleDrawRadius + 1f * ImGuiHelpers.GlobalScale, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.7f)), 12, 1.5f * ImGuiHelpers.GlobalScale);
            return isHoveringThisHandle;
        }

        public void ProcessInteractions(
            BaseDrawable? singleSelectedItem,
            List<BaseDrawable> selectedDrawables,
            List<BaseDrawable> allDrawablesOnPage,
            Func<DrawMode, int> getLayerPriorityFunc,
            ref BaseDrawable? hoveredDrawable,
            Vector2 mousePosLogical,
            Vector2 mousePosScreen,
            Vector2 canvasOriginScreen,
            bool isCanvasInteractableAndHovered,
            bool isLMBClickedOnCanvas,
            bool isLMBDown,
            bool isLMBReleased,
            ImDrawListPtr drawList,
            ref Vector2 lastMouseDragPosLogical)
        {
            BaseDrawable? newlyHoveredThisFrame = null;

            if (currentDragType == ActiveDragType.None)
            {
                foreach (var dItem in allDrawablesOnPage) dItem.IsHovered = false;
                draggedImageResizeHandleIndex = -1;
                draggedRectCornerIndex = -1;
                draggedArrowHandleIndex = -1;
                draggedTextResizeHandleIndex = -1;
            }

            bool mouseOverAnyHandle = false;

            // Draw the preview box if we are in the middle of a text resize drag.
            if (currentDragType == ActiveDragType.TextResize && singleSelectedItem is DrawableText)
            {
                int anchorIndex = (draggedTextResizeHandleIndex + 2) % 4;
                Vector2[] initialCorners = new Vector2[4];
                initialCorners[0] = dragStartTextPositionLogical;
                initialCorners[1] = dragStartTextPositionLogical + new Vector2(dragStartTextBoundingBoxSizeLogical.X, 0);
                initialCorners[2] = dragStartTextPositionLogical + dragStartTextBoundingBoxSizeLogical;
                initialCorners[3] = dragStartTextPositionLogical + new Vector2(0, dragStartTextBoundingBoxSizeLogical.Y);
                Vector2 anchorPoint = initialCorners[anchorIndex];

                Vector2 previewCorner1_screen = anchorPoint * ImGuiHelpers.GlobalScale + canvasOriginScreen;
                Vector2 previewCorner2_screen = mousePosScreen;

                drawList.AddRect(previewCorner1_screen, previewCorner2_screen, ImGui.GetColorU32(new Vector4(1, 1, 0, 0.4f)), 3f, ImDrawFlags.None, 1.5f * ImGuiHelpers.GlobalScale);
            }

            if (isCanvasInteractableAndHovered && singleSelectedItem != null)
            {
                if (singleSelectedItem is DrawableImage dImg)
                {
                    Vector2[] logicalCorners = HitDetection.GetRotatedQuadVertices(dImg.PositionRelative, dImg.DrawSize / 2f, dImg.RotationAngle);
                    for (int i = 0; i < 4; i++) { if (DrawAndCheckHandle(drawList, logicalCorners[i], canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorResize, this.handleColorResizeHover)) { draggedImageResizeHandleIndex = i; break; } }
                    if (!mouseOverAnyHandle) draggedImageResizeHandleIndex = -1;
                    Vector2 logicalCenter = dImg.PositionRelative;
                    Vector2 handleOffsetLocal = new Vector2(0, -(dImg.DrawSize.Y / 2f + DrawableImage.UnscaledRotationHandleDistance));
                    Vector2 rotatedHandleOffset = HitDetection.ImRotate(handleOffsetLocal, MathF.Cos(dImg.RotationAngle), MathF.Sin(dImg.RotationAngle));
                    Vector2 logicalRotationHandlePos = logicalCenter + rotatedHandleOffset;
                    DrawAndCheckHandle(drawList, logicalRotationHandlePos, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorRotation, this.handleColorRotationHover);
                }
                else if (singleSelectedItem is DrawableRectangle dRect)
                {
                    Vector2[] logicalRotatedCorners = dRect.GetRotatedCorners();
                    for (int i = 0; i < 4; i++) { if (DrawAndCheckHandle(drawList, logicalRotatedCorners[i], canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorResize, this.handleColorResizeHover)) { draggedRectCornerIndex = i; break; } }
                    if (!mouseOverAnyHandle) draggedRectCornerIndex = -1;
                    var (rectCenterLogical, rectHalfSize) = dRect.GetGeometry();
                    float handleDistance = rectHalfSize.Y + DrawableRectangle.UnscaledRotationHandleExtraOffset;
                    Vector2 rotationHandleLogicalPos = rectCenterLogical + Vector2.Transform(new Vector2(0, -handleDistance), Matrix3x2.CreateRotation(dRect.RotationAngle));
                    DrawAndCheckHandle(drawList, rotationHandleLogicalPos, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorRotation, this.handleColorRotationHover);
                }
                else if (singleSelectedItem is DrawableCone dCone)
                {
                    Vector2 logicalApex = dCone.ApexRelative;
                    Vector2 logicalBaseEndUnrotated = dCone.BaseCenterRelative - dCone.ApexRelative;
                    Vector2 logicalBaseEndRotated = Vector2.Transform(logicalBaseEndUnrotated, Matrix3x2.CreateRotation(dCone.RotationAngle));
                    Vector2 logicalRotatedBaseCenter = dCone.ApexRelative + logicalBaseEndRotated;
                    bool apexH = DrawAndCheckHandle(drawList, logicalApex, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.ResizeAll, this.handleColorResize, this.handleColorResizeHover);
                    bool baseH = !apexH && DrawAndCheckHandle(drawList, logicalRotatedBaseCenter, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.ResizeAll, this.handleColorResize, this.handleColorResizeHover);
                    Vector2 axisDir = logicalBaseEndUnrotated.LengthSquared() > 0.001f ? Vector2.Normalize(logicalBaseEndRotated) : new Vector2(0, 1);
                    Vector2 rotHandleLogical = logicalRotatedBaseCenter + axisDir * (DrawableRectangle.UnscaledRotationHandleExtraOffset * 0.75f);
                    bool rotH = !apexH && !baseH && DrawAndCheckHandle(drawList, rotHandleLogical, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorRotation, this.handleColorRotationHover);
                    if (apexH) draggedArrowHandleIndex = 0; else if (baseH) draggedArrowHandleIndex = 1; else if (rotH) draggedArrowHandleIndex = 2; else draggedArrowHandleIndex = -1;
                }
                else if (singleSelectedItem is DrawableArrow dArrow)
                {
                    Vector2 logicalStart = dArrow.StartPointRelative;
                    Vector2 localShaftEndUnrotated = dArrow.EndPointRelative - dArrow.StartPointRelative;
                    Vector2 logicalRotatedShaftEnd = dArrow.StartPointRelative + Vector2.Transform(localShaftEndUnrotated, Matrix3x2.CreateRotation(dArrow.RotationAngle));
                    bool startH = DrawAndCheckHandle(drawList, logicalStart, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.ResizeAll, this.handleColorResize, this.handleColorResizeHover);
                    bool endH = !startH && DrawAndCheckHandle(drawList, logicalRotatedShaftEnd, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.ResizeAll, this.handleColorResize, this.handleColorResizeHover);
                    Vector2 rotHandleOffsetLocal = new Vector2(0, -DrawableRectangle.UnscaledRotationHandleExtraOffset);
                    Vector2 rotHandleLogical = dArrow.StartPointRelative + Vector2.Transform(rotHandleOffsetLocal, Matrix3x2.CreateRotation(dArrow.RotationAngle));
                    bool rotH = !startH && !endH && DrawAndCheckHandle(drawList, rotHandleLogical, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorRotation, this.handleColorRotationHover);
                    Vector2 shaftMidLogicalRotated = dArrow.StartPointRelative + Vector2.Transform(localShaftEndUnrotated / 2f, Matrix3x2.CreateRotation(dArrow.RotationAngle));
                    Vector2 shaftDirRotated = localShaftEndUnrotated.LengthSquared() > 0.001f ? Vector2.Normalize(Vector2.Transform(localShaftEndUnrotated, Matrix3x2.CreateRotation(dArrow.RotationAngle))) : Vector2.Transform(new Vector2(0, 1), Matrix3x2.CreateRotation(dArrow.RotationAngle));
                    Vector2 perpOffsetThick = new Vector2(-shaftDirRotated.Y, shaftDirRotated.X) * (dArrow.Thickness / 2f + 10f);
                    bool thickH = !startH && !endH && !rotH && DrawAndCheckHandle(drawList, shaftMidLogicalRotated + perpOffsetThick, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.ResizeNS, this.handleColorSpecial, this.handleColorSpecialHover);
                    if (startH) draggedArrowHandleIndex = 0; else if (endH) draggedArrowHandleIndex = 1; else if (rotH) draggedArrowHandleIndex = 2; else if (thickH) draggedArrowHandleIndex = 3; else draggedArrowHandleIndex = -1;
                }
                else if (singleSelectedItem is DrawableText dText)
                {
                    Vector2 boxTopLeft = dText.PositionRelative;
                    Vector2 boxSize = dText.CurrentBoundingBoxSize;

                    Vector2[] bboxCorners = new Vector2[4];
                    bboxCorners[0] = boxTopLeft;
                    bboxCorners[1] = boxTopLeft + new Vector2(boxSize.X, 0);
                    bboxCorners[2] = boxTopLeft + boxSize;
                    bboxCorners[3] = boxTopLeft + new Vector2(0, boxSize.Y);

                    ImGuiMouseCursor[] cursors = { ImGuiMouseCursor.ResizeNWSE, ImGuiMouseCursor.ResizeNESW, ImGuiMouseCursor.ResizeNWSE, ImGuiMouseCursor.ResizeNESW };
                    int currentlyHoveredTextHandle = -1;
                    for (int i = 0; i < 4; i++) { if (DrawAndCheckHandle(drawList, bboxCorners[i], canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, cursors[i], this.handleColorResize, this.handleColorResizeHover)) { currentlyHoveredTextHandle = i; } }
                    draggedTextResizeHandleIndex = currentlyHoveredTextHandle;
                }
            }

            if (isCanvasInteractableAndHovered && !mouseOverAnyHandle && currentDragType == ActiveDragType.None)
            {
                var sortedForHover = allDrawablesOnPage.OrderByDescending(d => getLayerPriorityFunc(d.ObjectDrawMode)).ToList();
                newlyHoveredThisFrame = null;
                foreach (var drawable in sortedForHover) { if (drawable.IsHit(mousePosLogical, LogicalHandleInteractionRadius * 0.8f)) { newlyHoveredThisFrame = drawable; if (!selectedDrawables.Contains(newlyHoveredThisFrame)) { newlyHoveredThisFrame.IsHovered = true; } break; } }
            }
            if (currentDragType == ActiveDragType.None) { hoveredDrawable = newlyHoveredThisFrame; }

            if (isCanvasInteractableAndHovered && isLMBClickedOnCanvas)
            {
                bool clickedOnAHandle = false; ActiveDragType potentialDragType = ActiveDragType.None;
                if (singleSelectedItem != null && mouseOverAnyHandle)
                {
                    clickedOnAHandle = true; dragStartMousePosLogical = mousePosLogical;
                    if (singleSelectedItem is DrawableImage dImg) { Vector2 logicalCenter = dImg.PositionRelative; Vector2 handleOffsetLocal = new Vector2(0, -(dImg.DrawSize.Y / 2f + DrawableImage.UnscaledRotationHandleDistance)); Vector2 rotatedHandleOffset = HitDetection.ImRotate(handleOffsetLocal, MathF.Cos(dImg.RotationAngle), MathF.Sin(dImg.RotationAngle)); Vector2 logicalRotationHandlePos = logicalCenter + rotatedHandleOffset; if (Vector2.Distance(mousePosLogical, logicalRotationHandlePos) < LogicalHandleInteractionRadius) { potentialDragType = ActiveDragType.ImageRotate; dragStartObjectPivotLogical = dImg.PositionRelative; dragStartRotationAngle = dImg.RotationAngle; } else if (draggedImageResizeHandleIndex != -1) { potentialDragType = ActiveDragType.ImageResize; dragStartObjectPivotLogical = dImg.PositionRelative; dragStartSizeLogical = dImg.DrawSize; dragStartRotationAngle = dImg.RotationAngle; } else { clickedOnAHandle = false; } }
                    else if (singleSelectedItem is DrawableRectangle dRect) { var (rectCenterLogical, rectHalfSize) = dRect.GetGeometry(); float handleDistance = rectHalfSize.Y + DrawableRectangle.UnscaledRotationHandleExtraOffset; Vector2 rotationHandleLogicalPos = rectCenterLogical + Vector2.Transform(new Vector2(0, -handleDistance), Matrix3x2.CreateRotation(dRect.RotationAngle)); if (Vector2.Distance(mousePosLogical, rotationHandleLogicalPos) < LogicalHandleInteractionRadius) { potentialDragType = ActiveDragType.RectRotate; dragStartObjectPivotLogical = rectCenterLogical; dragStartRotationAngle = dRect.RotationAngle; } else if (draggedRectCornerIndex != -1) { potentialDragType = ActiveDragType.RectResize; dragStartPoint1Logical = dRect.StartPointRelative; dragStartPoint2Logical = dRect.EndPointRelative; dragStartRotationAngle = dRect.RotationAngle; dragStartObjectPivotLogical = rectCenterLogical; } else { clickedOnAHandle = false; } }
                    else if (singleSelectedItem is DrawableCone dCone) { if (draggedArrowHandleIndex == 0) { potentialDragType = ActiveDragType.ConeApex; dragStartPoint1Logical = dCone.ApexRelative; dragStartPoint2Logical = dCone.BaseCenterRelative; } else if (draggedArrowHandleIndex == 1) { potentialDragType = ActiveDragType.ConeBase; dragStartPoint1Logical = dCone.ApexRelative; dragStartPoint2Logical = dCone.BaseCenterRelative; } else if (draggedArrowHandleIndex == 2) { potentialDragType = ActiveDragType.ConeRotate; dragStartObjectPivotLogical = dCone.ApexRelative; dragStartRotationAngle = dCone.RotationAngle; } else { clickedOnAHandle = false; } }
                    else if (singleSelectedItem is DrawableArrow dArrow) { dragStartPoint1Logical = dArrow.StartPointRelative; dragStartPoint2Logical = dArrow.EndPointRelative; dragStartRotationAngle = dArrow.RotationAngle; if (draggedArrowHandleIndex == 0) { potentialDragType = ActiveDragType.ArrowStartPoint; } else if (draggedArrowHandleIndex == 1) { potentialDragType = ActiveDragType.ArrowEndPoint; } else if (draggedArrowHandleIndex == 2) { potentialDragType = ActiveDragType.ArrowRotate; dragStartObjectPivotLogical = dArrow.StartPointRelative; } else if (draggedArrowHandleIndex == 3) { potentialDragType = ActiveDragType.ArrowThickness; dragStartValueLogical = dArrow.Thickness; } else { clickedOnAHandle = false; } }
                    else if (singleSelectedItem is DrawableText dText && draggedTextResizeHandleIndex != -1)
                    {
                        potentialDragType = ActiveDragType.TextResize;
                        dragStartTextPositionLogical = dText.PositionRelative;
                        dragStartTextBoundingBoxSizeLogical = dText.CurrentBoundingBoxSize;
                        dragStartFontSizeLogical = dText.FontSize;
                    }
                    else { clickedOnAHandle = false; }
                    if (clickedOnAHandle && singleSelectedItem != null && !singleSelectedItem.IsSelected) { if (!ImGui.GetIO().KeyCtrl) { foreach (var d_sel_loop in selectedDrawables) d_sel_loop.IsSelected = false; selectedDrawables.Clear(); } singleSelectedItem.IsSelected = true; selectedDrawables.Add(singleSelectedItem); }
                }
                if (!clickedOnAHandle)
                {
                    if (hoveredDrawable != null) { if (!ImGui.GetIO().KeyCtrl) { if (!selectedDrawables.Contains(hoveredDrawable)) { foreach (var d_sel in selectedDrawables) d_sel.IsSelected = false; selectedDrawables.Clear(); hoveredDrawable.IsSelected = true; selectedDrawables.Add(hoveredDrawable); } } else { if (selectedDrawables.Contains(hoveredDrawable)) { hoveredDrawable.IsSelected = false; selectedDrawables.Remove(hoveredDrawable); } else { hoveredDrawable.IsSelected = true; selectedDrawables.Add(hoveredDrawable); } } if (selectedDrawables.Any(d => d.IsSelected)) { potentialDragType = ActiveDragType.GeneralSelection; lastMouseDragPosLogical = mousePosLogical; } }
                    else { foreach (var d_sel in selectedDrawables) d_sel.IsSelected = false; selectedDrawables.Clear(); potentialDragType = ActiveDragType.None; }
                }
                currentDragType = potentialDragType;

                if (currentDragType != ActiveDragType.None)
                {
                    var currentDrawablesForUndo = pageManager.GetCurrentPageDrawables();
                    if (currentDrawablesForUndo != null)
                    {
                        undoManager.RecordAction(currentDrawablesForUndo, $"Start {currentDragType}");
                    }
                }
            }

            if (isLMBDown && currentDragType != ActiveDragType.None)
            {
                if (currentDragType != ActiveDragType.TextResize && singleSelectedItem != null)
                {
                    if (singleSelectedItem is DrawableImage dImg) { if (currentDragType == ActiveDragType.ImageResize && draggedImageResizeHandleIndex != -1) { Vector2 mouseInLocalUnrotated = HitDetection.ImRotate(mousePosLogical - dImg.PositionRelative, MathF.Cos(-dImg.RotationAngle), MathF.Sin(-dImg.RotationAngle)); Vector2 newHalfSize = new Vector2(Math.Abs(mouseInLocalUnrotated.X), Math.Abs(mouseInLocalUnrotated.Y)); float minDimLogical = DrawableImage.UnscaledResizeHandleRadius * 2f; dImg.DrawSize = new Vector2(Math.Max(newHalfSize.X * 2f, minDimLogical), Math.Max(newHalfSize.Y * 2f, minDimLogical)); } else if (currentDragType == ActiveDragType.ImageRotate) { float angleNow = MathF.Atan2(mousePosLogical.Y - dragStartObjectPivotLogical.Y, mousePosLogical.X - dragStartObjectPivotLogical.X); float angleThen = MathF.Atan2(dragStartMousePosLogical.Y - dragStartObjectPivotLogical.Y, dragStartMousePosLogical.X - dragStartObjectPivotLogical.X); dImg.RotationAngle = dragStartRotationAngle + (angleNow - angleThen); } }
                    else if (singleSelectedItem is DrawableRectangle dRect) { if (currentDragType == ActiveDragType.RectResize && draggedRectCornerIndex != -1) { Vector2[] originalLogicalCorners = HitDetection.GetRotatedQuadVertices(dragStartObjectPivotLogical, (dragStartPoint2Logical - dragStartPoint1Logical) / 2f, dragStartRotationAngle); Vector2 pivotCornerLogical = originalLogicalCorners[(draggedRectCornerIndex + 2) % 4]; Vector2 mouseRelativeToPivot = mousePosLogical - pivotCornerLogical; Vector2 mouseInRectLocalFrame = HitDetection.ImRotate(mouseRelativeToPivot, MathF.Cos(-dragStartRotationAngle), MathF.Sin(-dragStartRotationAngle)); Vector2 newCenterInLocalFrame = mouseInRectLocalFrame / 2f; Vector2 newHalfSizeLocal = new Vector2(Math.Abs(mouseInRectLocalFrame.X) / 2f, Math.Abs(mouseInRectLocalFrame.Y) / 2f); newHalfSizeLocal.X = Math.Max(newHalfSizeLocal.X, 1f); newHalfSizeLocal.Y = Math.Max(newHalfSizeLocal.Y, 1f); Vector2 newCenter = pivotCornerLogical + HitDetection.ImRotate(newCenterInLocalFrame, MathF.Cos(dragStartRotationAngle), MathF.Sin(dragStartRotationAngle)); dRect.StartPointRelative = newCenter - newHalfSizeLocal; dRect.EndPointRelative = newCenter + newHalfSizeLocal; dRect.RotationAngle = dragStartRotationAngle; } else if (currentDragType == ActiveDragType.RectRotate) { float angleNow = MathF.Atan2(mousePosLogical.Y - dragStartObjectPivotLogical.Y, mousePosLogical.X - dragStartObjectPivotLogical.X); float angleThen = MathF.Atan2(dragStartMousePosLogical.Y - dragStartObjectPivotLogical.Y, dragStartMousePosLogical.X - dragStartObjectPivotLogical.X); dRect.RotationAngle = dragStartRotationAngle + (angleNow - angleThen); } }
                    else if (singleSelectedItem is DrawableCone dCone) { if (currentDragType == ActiveDragType.ConeApex) { dCone.SetApex(dragStartPoint1Logical + (mousePosLogical - dragStartMousePosLogical)); } else if (currentDragType == ActiveDragType.ConeBase) { Vector2 mouseRelativeToApex = mousePosLogical - dCone.ApexRelative; Vector2 unrotatedMouseRelativeToApex = HitDetection.ImRotate(mouseRelativeToApex, MathF.Cos(-dCone.RotationAngle), MathF.Sin(-dCone.RotationAngle)); dCone.SetBaseCenter(dCone.ApexRelative + unrotatedMouseRelativeToApex); } else if (currentDragType == ActiveDragType.ConeRotate) { float angleNow = MathF.Atan2(mousePosLogical.Y - dragStartObjectPivotLogical.Y, mousePosLogical.X - dragStartObjectPivotLogical.X); float angleThen = MathF.Atan2(dragStartMousePosLogical.Y - dragStartObjectPivotLogical.Y, dragStartMousePosLogical.X - dragStartObjectPivotLogical.X); dCone.RotationAngle = dragStartRotationAngle + (angleNow - angleThen); } }
                    else if (singleSelectedItem is DrawableArrow dArrow) { if (currentDragType == ActiveDragType.ArrowStartPoint) { dArrow.SetStartPoint(dragStartPoint1Logical + (mousePosLogical - dragStartMousePosLogical)); } else if (currentDragType == ActiveDragType.ArrowEndPoint) { Vector2 mouseRelativeToStart = mousePosLogical - dArrow.StartPointRelative; Vector2 unrotatedMouseRelativeToStart = HitDetection.ImRotate(mouseRelativeToStart, MathF.Cos(-dArrow.RotationAngle), MathF.Sin(-dArrow.RotationAngle)); dArrow.SetEndPoint(dArrow.StartPointRelative + unrotatedMouseRelativeToStart); } else if (currentDragType == ActiveDragType.ArrowRotate) { float angleNow = MathF.Atan2(mousePosLogical.Y - dragStartObjectPivotLogical.Y, mousePosLogical.X - dragStartObjectPivotLogical.X); float angleThen = MathF.Atan2(dragStartMousePosLogical.Y - dragStartObjectPivotLogical.Y, dragStartMousePosLogical.X - dragStartObjectPivotLogical.X); dArrow.RotationAngle = dragStartRotationAngle + (angleNow - angleThen); } else if (currentDragType == ActiveDragType.ArrowThickness) { Vector2 initialShaftVec = dragStartPoint2Logical - dragStartPoint1Logical; Vector2 initialShaftDir = initialShaftVec.LengthSquared() > 0.001f ? Vector2.Normalize(initialShaftVec) : new Vector2(0, -1); Vector2 perpDir = new Vector2(-initialShaftDir.Y, initialShaftDir.X); Vector2 currentMouseDeltaFromDragStartUnrotated = HitDetection.ImRotate((mousePosLogical - dragStartMousePosLogical), MathF.Cos(-dragStartRotationAngle), MathF.Sin(-dragStartRotationAngle)); float thicknessDeltaProjection = Vector2.Dot(currentMouseDeltaFromDragStartUnrotated, perpDir); dArrow.Thickness = Math.Max(1f, dragStartValueLogical + thicknessDeltaProjection); } }
                }
                else if (currentDragType == ActiveDragType.TextResize && singleSelectedItem is DrawableText dText)
                {
                    // Get the corner opposite to the one being dragged; this is our anchor.
                    int anchorIndex = (draggedTextResizeHandleIndex + 2) % 4;
                    Vector2[] initialCorners = {
                        dragStartTextPositionLogical,
                        dragStartTextPositionLogical + new Vector2(dragStartTextBoundingBoxSizeLogical.X, 0),
                        dragStartTextPositionLogical + dragStartTextBoundingBoxSizeLogical,
                        dragStartTextPositionLogical + new Vector2(0, dragStartTextBoundingBoxSizeLogical.Y)
                    };
                    Vector2 anchorPoint = initialCorners[anchorIndex];

                    // Determine the new bounding box from the anchor and the current mouse position.
                    Vector2 newTopLeft = new Vector2(Math.Min(anchorPoint.X, mousePosLogical.X), Math.Min(anchorPoint.Y, mousePosLogical.Y));
                    Vector2 newBottomRight = new Vector2(Math.Max(anchorPoint.X, mousePosLogical.X), Math.Max(anchorPoint.Y, mousePosLogical.Y));

                    float newWidth = newBottomRight.X - newTopLeft.X;
                    float newHeight = newBottomRight.Y - newTopLeft.Y;

                    // Update the text object's properties in real-time.
                    // This triggers PerformLayout() in DrawableText, reflowing the text.
                    dText.PositionRelative = newTopLeft;
                    if (newWidth > 10f) // Prevent the box from becoming too small.
                    {
                        dText.WrappingWidth = newWidth;
                    }

                    // Adjust font size proportionally to the change in height.
                    if (dragStartTextBoundingBoxSizeLogical.Y > 1f && newHeight > 10f)
                    {
                        float heightRatio = newHeight / dragStartTextBoundingBoxSizeLogical.Y;
                        dText.FontSize = Math.Max(8f, dragStartFontSizeLogical * heightRatio); // Minimum font size of 8.
                    }
                }

                if (currentDragType == ActiveDragType.GeneralSelection && selectedDrawables.Any())
                {
                    Vector2 dragDeltaLogical = mousePosLogical - lastMouseDragPosLogical;
                    if (dragDeltaLogical.LengthSquared() > 0.0001f) { foreach (var item in selectedDrawables) { item.Translate(dragDeltaLogical); } }
                }
                lastMouseDragPosLogical = mousePosLogical;
            }

            if (isCanvasInteractableAndHovered && isLMBReleased)
            {
                if (currentDragType != ActiveDragType.None)
                {
                    ResetDragState();
                }
            }
        }

        public void ResetDragState()
        {
            currentDragType = ActiveDragType.None;
            draggedImageResizeHandleIndex = -1;
            draggedRectCornerIndex = -1;
            draggedArrowHandleIndex = -1;
            draggedTextResizeHandleIndex = -1;
        }
    }
}
