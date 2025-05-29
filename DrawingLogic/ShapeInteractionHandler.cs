using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using System.Linq;
using Dalamud.Interface.Utility; // For ImGuiHelpers

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
            // Note: TextTool interactions like TextResizeFont were in a newer version's context.
            // The ab6ebef... version of this enum is used here.
        }
        private ActiveDragType currentDragType = ActiveDragType.None;
        public ActiveDragType GetCurrentDragType() => currentDragType;

        // State for drag operations - these store logical (unscaled) values
        private Vector2 dragStartMousePosLogical;
        private Vector2 dragStartObjectPivotLogical; // e.g., center of rotation
        private float dragStartRotationAngle;
        private Vector2 dragStartPoint1Logical;     // e.g., rect start, arrow start
        private Vector2 dragStartPoint2Logical;     // e.g., rect end, arrow end
        private Vector2 dragStartSizeLogical;       // e.g., image logical size
        private float dragStartValueLogical;        // e.g., arrow thickness (unscaled)

        private int draggedImageResizeHandleIndex = -1; // Which of the 4 corners
        private int draggedRectCornerIndex = -1;      // Which of the 4 corners
        private int draggedArrowHandleIndex = -1;     // Used to identify which arrow/cone handle

        // Define a LOGICAL radius for handle interaction (unscaled)
        private const float LogicalHandleInteractionRadius = 7f;
        // Visual draw radius for handles (scaled for screen)
        private float ScaledHandleDrawRadius => 5f * ImGuiHelpers.GlobalScale;

        // Colors for handles - Instance fields, initialized in constructor
        private readonly uint handleColorDefault;
        private readonly uint handleColorHover;
        private readonly uint handleColorRotation;
        private readonly uint handleColorRotationHover;
        private readonly uint handleColorResize;
        private readonly uint handleColorResizeHover;
        private readonly uint handleColorSpecial;
        private readonly uint handleColorSpecialHover;

        public ShapeInteractionHandler()
        {
            // Initialize ImGui-dependent colors safely
            this.handleColorDefault = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.9f));
            this.handleColorHover = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.5f, 1.0f));
            this.handleColorRotation = ImGui.GetColorU32(new Vector4(0.5f, 1.0f, 0.5f, 0.9f));
            this.handleColorRotationHover = ImGui.GetColorU32(new Vector4(0.7f, 1.0f, 0.7f, 1.0f));
            this.handleColorResize = ImGui.GetColorU32(new Vector4(0.5f, 0.7f, 1.0f, 0.9f));
            this.handleColorResizeHover = ImGui.GetColorU32(new Vector4(0.7f, 0.9f, 1.0f, 1.0f));
            this.handleColorSpecial = ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.2f, 0.9f));
            this.handleColorSpecialHover = ImGui.GetColorU32(new Vector4(1.0f, 0.7f, 0.4f, 1.0f));
        }

        private bool DrawAndCheckHandle(ImDrawListPtr drawList, Vector2 logicalHandlePos, Vector2 canvasOriginScreen, Vector2 mousePosLogical, ref bool mouseOverAnyHandleFlag, ImGuiMouseCursor cursor = ImGuiMouseCursor.Hand, uint color = 0, uint hoverColor = 0)
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
            Vector2 mousePosLogical, // LOGICAL (unscaled) mouse position relative to canvas origin
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
            }

            bool mouseOverAnyHandle = false;

            if (isCanvasInteractableAndHovered && currentDragType == ActiveDragType.None && singleSelectedItem != null)
            {
                if (singleSelectedItem is DrawableImage dImg) // Use 'dImg' consistently
                {
                    Vector2[] logicalCorners = HitDetection.GetRotatedQuadVertices(dImg.PositionRelative, dImg.DrawSize / 2f, dImg.RotationAngle);
                    for (int i = 0; i < 4; i++)
                    {
                        if (DrawAndCheckHandle(drawList, logicalCorners[i], canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorResize, this.handleColorResizeHover))
                        {
                            draggedImageResizeHandleIndex = i; break;
                        }
                    }
                    if (!mouseOverAnyHandle) draggedImageResizeHandleIndex = -1;

                    Vector2 logicalCenter = dImg.PositionRelative;
                    Vector2 handleOffsetLocal = new Vector2(0, -(dImg.DrawSize.Y / 2f + DrawableImage.UnscaledRotationHandleDistance));
                    Vector2 rotatedHandleOffset = HitDetection.ImRotate(handleOffsetLocal, MathF.Cos(dImg.RotationAngle), MathF.Sin(dImg.RotationAngle));
                    Vector2 logicalRotationHandlePos = logicalCenter + rotatedHandleOffset;

                    DrawAndCheckHandle(drawList, logicalRotationHandlePos, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorRotation, this.handleColorRotationHover);
                }
                else if (singleSelectedItem is DrawableRectangle dRect) // Use 'dRect'
                {
                    Vector2[] logicalRotatedCorners = dRect.GetRotatedCorners();
                    for (int i = 0; i < 4; i++)
                    {
                        if (DrawAndCheckHandle(drawList, logicalRotatedCorners[i], canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorResize, this.handleColorResizeHover))
                        {
                            draggedRectCornerIndex = i; break;
                        }
                    }
                    if (!mouseOverAnyHandle) draggedRectCornerIndex = -1;

                    var (rectCenterLogical, rectHalfSize) = dRect.GetGeometry();
                    float handleDistance = rectHalfSize.Y + DrawableRectangle.UnscaledRotationHandleExtraOffset;
                    Vector2 rotationHandleLogicalPos = rectCenterLogical + Vector2.Transform(new Vector2(0, -handleDistance), Matrix3x2.CreateRotation(dRect.RotationAngle));
                    DrawAndCheckHandle(drawList, rotationHandleLogicalPos, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorRotation, this.handleColorRotationHover);
                }
                else if (singleSelectedItem is DrawableCone dCone) // Use 'dCone'
                {
                    Vector2 logicalApex = dCone.ApexRelative;
                    Vector2 logicalBaseEndUnrotated = dCone.BaseCenterRelative - dCone.ApexRelative;
                    Vector2 logicalBaseEndRotated = Vector2.Transform(logicalBaseEndUnrotated, Matrix3x2.CreateRotation(dCone.RotationAngle));
                    Vector2 logicalRotatedBaseCenter = dCone.ApexRelative + logicalBaseEndRotated;

                    bool apexH = DrawAndCheckHandle(drawList, logicalApex, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.ResizeAll, this.handleColorResize, this.handleColorResizeHover);
                    bool baseH = !apexH && DrawAndCheckHandle(drawList, logicalRotatedBaseCenter, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.ResizeAll, this.handleColorResize, this.handleColorResizeHover);

                    Vector2 axisDir = logicalBaseEndRotated.LengthSquared() > 0.001f ? Vector2.Normalize(logicalBaseEndRotated) : new Vector2(0, 1);
                    Vector2 rotHandleLogical = logicalRotatedBaseCenter + axisDir * (DrawableRectangle.UnscaledRotationHandleExtraOffset * 0.75f); // Use a defined unscaled offset
                    bool rotH = !apexH && !baseH && DrawAndCheckHandle(drawList, rotHandleLogical, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.Hand, this.handleColorRotation, this.handleColorRotationHover);

                    if (apexH) draggedArrowHandleIndex = 0;
                    else if (baseH) draggedArrowHandleIndex = 1;
                    else if (rotH) draggedArrowHandleIndex = 2;
                    else draggedArrowHandleIndex = -1;
                }
                else if (singleSelectedItem is DrawableArrow dArrow) // Use 'dArrow'
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
                    Vector2 shaftDirRotated = localShaftEndUnrotated.LengthSquared() > 0.001f ? Vector2.Normalize(Vector2.Transform(localShaftEndUnrotated, Matrix3x2.CreateRotation(dArrow.RotationAngle))) : Vector2.Transform(new Vector2(0, 1), Matrix3x2.CreateRotation(dArrow.RotationAngle)); // Default up if zero length
                    Vector2 perpOffsetThick = new Vector2(-shaftDirRotated.Y, shaftDirRotated.X) * (dArrow.Thickness / 2f + 10f); // 10f is a logical margin
                    bool thickH = !startH && !endH && !rotH && DrawAndCheckHandle(drawList, shaftMidLogicalRotated + perpOffsetThick, canvasOriginScreen, mousePosLogical, ref mouseOverAnyHandle, ImGuiMouseCursor.ResizeNS, this.handleColorSpecial, this.handleColorSpecialHover);

                    if (startH) draggedArrowHandleIndex = 0;
                    else if (endH) draggedArrowHandleIndex = 1;
                    else if (rotH) draggedArrowHandleIndex = 2;
                    else if (thickH) draggedArrowHandleIndex = 3;
                    else draggedArrowHandleIndex = -1;
                }
            }

            if (isCanvasInteractableAndHovered && !mouseOverAnyHandle && currentDragType == ActiveDragType.None)
            {
                var sortedForHover = allDrawablesOnPage.OrderByDescending(d => getLayerPriorityFunc(d.ObjectDrawMode)).ToList();
                newlyHoveredThisFrame = null;
                foreach (var drawable in sortedForHover)
                {
                    if (drawable.IsHit(mousePosLogical, LogicalHandleInteractionRadius / 2f))
                    {
                        newlyHoveredThisFrame = drawable;
                        if (!selectedDrawables.Contains(newlyHoveredThisFrame)) newlyHoveredThisFrame.IsHovered = true;
                        break;
                    }
                }
            }
            if (currentDragType == ActiveDragType.None) hoveredDrawable = newlyHoveredThisFrame;

            if (isCanvasInteractableAndHovered && isLMBClickedOnCanvas)
            {
                bool clickedOnAHandle = false;
                if (singleSelectedItem != null && mouseOverAnyHandle)
                {
                    clickedOnAHandle = true;
                    dragStartMousePosLogical = mousePosLogical;

                    if (singleSelectedItem is DrawableImage dImg) // Consistent variable name
                    {
                        Vector2 logicalCenter = dImg.PositionRelative;
                        Vector2 handleOffsetLocal = new Vector2(0, -(dImg.DrawSize.Y / 2f + DrawableImage.UnscaledRotationHandleDistance));
                        Vector2 rotatedHandleOffset = HitDetection.ImRotate(handleOffsetLocal, MathF.Cos(dImg.RotationAngle), MathF.Sin(dImg.RotationAngle));
                        Vector2 logicalRotationHandlePos = logicalCenter + rotatedHandleOffset;

                        if (Vector2.Distance(mousePosLogical, logicalRotationHandlePos) < LogicalHandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.ImageRotate;
                            dragStartObjectPivotLogical = dImg.PositionRelative;
                            dragStartRotationAngle = dImg.RotationAngle;
                        }
                        else if (draggedImageResizeHandleIndex != -1)
                        {
                            currentDragType = ActiveDragType.ImageResize;
                            dragStartObjectPivotLogical = dImg.PositionRelative;
                            dragStartSizeLogical = dImg.DrawSize;
                            dragStartRotationAngle = dImg.RotationAngle;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else if (singleSelectedItem is DrawableRectangle dRect)
                    {
                        var (rectCenterLogical, rectHalfSize) = dRect.GetGeometry();
                        float handleDistance = rectHalfSize.Y + DrawableRectangle.UnscaledRotationHandleExtraOffset;
                        Vector2 rotationHandleLogicalPos = rectCenterLogical + Vector2.Transform(new Vector2(0, -handleDistance), Matrix3x2.CreateRotation(dRect.RotationAngle));

                        if (Vector2.Distance(mousePosLogical, rotationHandleLogicalPos) < LogicalHandleInteractionRadius)
                        {
                            currentDragType = ActiveDragType.RectRotate;
                            dragStartObjectPivotLogical = rectCenterLogical;
                            dragStartRotationAngle = dRect.RotationAngle;
                        }
                        else if (draggedRectCornerIndex != -1)
                        {
                            currentDragType = ActiveDragType.RectResize;
                            dragStartPoint1Logical = dRect.StartPointRelative;
                            dragStartPoint2Logical = dRect.EndPointRelative;
                            dragStartRotationAngle = dRect.RotationAngle;
                            dragStartObjectPivotLogical = rectCenterLogical;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else if (singleSelectedItem is DrawableCone dCone)
                    {
                        if (draggedArrowHandleIndex == 0)
                        {
                            currentDragType = ActiveDragType.ConeApex;
                            dragStartPoint1Logical = dCone.ApexRelative;
                            dragStartPoint2Logical = dCone.BaseCenterRelative;
                        }
                        else if (draggedArrowHandleIndex == 1)
                        {
                            currentDragType = ActiveDragType.ConeBase;
                            dragStartPoint1Logical = dCone.ApexRelative;
                            dragStartPoint2Logical = dCone.BaseCenterRelative;
                        }
                        else if (draggedArrowHandleIndex == 2)
                        {
                            currentDragType = ActiveDragType.ConeRotate;
                            dragStartObjectPivotLogical = dCone.ApexRelative;
                            dragStartRotationAngle = dCone.RotationAngle;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else if (singleSelectedItem is DrawableArrow dArrow)
                    {
                        dragStartPoint1Logical = dArrow.StartPointRelative;
                        dragStartPoint2Logical = dArrow.EndPointRelative;
                        dragStartRotationAngle = dArrow.RotationAngle;

                        if (draggedArrowHandleIndex == 0) { currentDragType = ActiveDragType.ArrowStartPoint; }
                        else if (draggedArrowHandleIndex == 1) { currentDragType = ActiveDragType.ArrowEndPoint; }
                        else if (draggedArrowHandleIndex == 2)
                        {
                            currentDragType = ActiveDragType.ArrowRotate;
                            dragStartObjectPivotLogical = dArrow.StartPointRelative;
                        }
                        else if (draggedArrowHandleIndex == 3)
                        {
                            currentDragType = ActiveDragType.ArrowThickness;
                            dragStartValueLogical = dArrow.Thickness;
                        }
                        else { clickedOnAHandle = false; }
                    }
                    else { clickedOnAHandle = false; }

                    if (clickedOnAHandle && singleSelectedItem != null && !selectedDrawables.Contains(singleSelectedItem))
                    {
                        if (!ImGui.GetIO().KeyCtrl)
                        {
                            foreach (var d_sel_loop in selectedDrawables) d_sel_loop.IsSelected = false;
                            selectedDrawables.Clear();
                        }
                        singleSelectedItem.IsSelected = true;
                        selectedDrawables.Add(singleSelectedItem);
                    }
                }

                if (!clickedOnAHandle)
                {
                    if (hoveredDrawable != null)
                    {
                        if (!ImGui.GetIO().KeyCtrl && !selectedDrawables.Contains(hoveredDrawable))
                        {
                            foreach (var d_sel in selectedDrawables) d_sel.IsSelected = false;
                            selectedDrawables.Clear();
                        }

                        if (selectedDrawables.Contains(hoveredDrawable))
                        {
                            if (ImGui.GetIO().KeyCtrl) { hoveredDrawable.IsSelected = false; selectedDrawables.Remove(hoveredDrawable); }
                        }
                        else
                        {
                            hoveredDrawable.IsSelected = true;
                            selectedDrawables.Add(hoveredDrawable);
                        }

                        if (hoveredDrawable.IsSelected) // Check if the item we just interacted with is selected
                        {
                            currentDragType = ActiveDragType.GeneralSelection;
                            lastMouseDragPosLogical = mousePosLogical;
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

            if (isLMBDown && currentDragType != ActiveDragType.None)
            {
                Vector2 mouseDeltaLogicalUnrotated; // For operations in object's local unrotated space

                if (singleSelectedItem is DrawableImage dImg) // Use consistent variable name
                {
                    if (currentDragType == ActiveDragType.ImageResize && draggedImageResizeHandleIndex != -1)
                    {
                        // Transform mouse into image's local unrotated space centered at its position
                        Vector2 mouseInLocalUnrotated = HitDetection.ImRotate(mousePosLogical - dImg.PositionRelative, MathF.Cos(-dImg.RotationAngle), MathF.Sin(-dImg.RotationAngle));
                        Vector2 newHalfSize = new Vector2(Math.Abs(mouseInLocalUnrotated.X), Math.Abs(mouseInLocalUnrotated.Y));

                        float minDimLogical = DrawableImage.UnscaledResizeHandleRadius * 2f;
                        dImg.DrawSize = new Vector2(
                            Math.Max(newHalfSize.X * 2f, minDimLogical),
                            Math.Max(newHalfSize.Y * 2f, minDimLogical)
                        );
                    }
                    else if (currentDragType == ActiveDragType.ImageRotate)
                    {
                        float angleNow = MathF.Atan2(mousePosLogical.Y - dragStartObjectPivotLogical.Y, mousePosLogical.X - dragStartObjectPivotLogical.X);
                        float angleThen = MathF.Atan2(dragStartMousePosLogical.Y - dragStartObjectPivotLogical.Y, dragStartMousePosLogical.X - dragStartObjectPivotLogical.X);
                        dImg.RotationAngle = dragStartRotationAngle + (angleNow - angleThen);
                    }
                }
                else if (singleSelectedItem is DrawableRectangle dRect)
                {
                    if (currentDragType == ActiveDragType.RectResize && draggedRectCornerIndex != -1)
                    {
                        Vector2[] originalLogicalCorners = HitDetection.GetRotatedQuadVertices(dragStartObjectPivotLogical, (dragStartPoint2Logical - dragStartPoint1Logical) / 2f, dragStartRotationAngle);
                        Vector2 pivotCornerLogical = originalLogicalCorners[(draggedRectCornerIndex + 2) % 4];
                        Vector2 mouseRelativeToPivot = mousePosLogical - pivotCornerLogical;
                        Vector2 mouseInRectLocalFrame = HitDetection.ImRotate(mouseRelativeToPivot, MathF.Cos(-dragStartRotationAngle), MathF.Sin(-dragStartRotationAngle));
                        Vector2 newCenterInLocalFrame = mouseInRectLocalFrame / 2f;
                        Vector2 newHalfSizeLocal = new Vector2(Math.Abs(mouseInRectLocalFrame.X) / 2f, Math.Abs(mouseInRectLocalFrame.Y) / 2f);
                        newHalfSizeLocal.X = Math.Max(newHalfSizeLocal.X, 1f);
                        newHalfSizeLocal.Y = Math.Max(newHalfSizeLocal.Y, 1f);
                        Vector2 newCenter = pivotCornerLogical + HitDetection.ImRotate(newCenterInLocalFrame, MathF.Cos(dragStartRotationAngle), MathF.Sin(dragStartRotationAngle));

                        dRect.StartPointRelative = newCenter - newHalfSizeLocal;
                        dRect.EndPointRelative = newCenter + newHalfSizeLocal;
                        // dRect.RotationAngle = dragStartRotationAngle; // Rotation doesn't change
                    }
                    else if (currentDragType == ActiveDragType.RectRotate)
                    {
                        float angleNow = MathF.Atan2(mousePosLogical.Y - dragStartObjectPivotLogical.Y, mousePosLogical.X - dragStartObjectPivotLogical.X);
                        float angleThen = MathF.Atan2(dragStartMousePosLogical.Y - dragStartObjectPivotLogical.Y, dragStartMousePosLogical.X - dragStartObjectPivotLogical.X);
                        dRect.RotationAngle = dragStartRotationAngle + (angleNow - angleThen);
                    }
                }
                else if (singleSelectedItem is DrawableCone dCone)
                {
                    if (currentDragType == ActiveDragType.ConeApex)
                    {
                        Vector2 delta = mousePosLogical - dragStartMousePosLogical;
                        dCone.SetApex(dragStartPoint1Logical + delta);
                    }
                    else if (currentDragType == ActiveDragType.ConeBase)
                    {
                        Vector2 mouseRelativeToApex = mousePosLogical - dCone.ApexRelative;
                        Vector2 unrotatedMouseRelativeToApex = HitDetection.ImRotate(mouseRelativeToApex, MathF.Cos(-dCone.RotationAngle), MathF.Sin(-dCone.RotationAngle));
                        dCone.SetBaseCenter(dCone.ApexRelative + unrotatedMouseRelativeToApex);
                    }
                    else if (currentDragType == ActiveDragType.ConeRotate)
                    {
                        float angleNow = MathF.Atan2(mousePosLogical.Y - dragStartObjectPivotLogical.Y, mousePosLogical.X - dragStartObjectPivotLogical.X);
                        float angleThen = MathF.Atan2(dragStartMousePosLogical.Y - dragStartObjectPivotLogical.Y, dragStartMousePosLogical.X - dragStartObjectPivotLogical.X);
                        dCone.RotationAngle = dragStartRotationAngle + (angleNow - angleThen);
                    }
                }
                else if (singleSelectedItem is DrawableArrow dArrow)
                {
                    if (currentDragType == ActiveDragType.ArrowStartPoint)
                    {
                        Vector2 delta = mousePosLogical - dragStartMousePosLogical;
                        dArrow.SetStartPoint(dragStartPoint1Logical + delta);
                    }
                    else if (currentDragType == ActiveDragType.ArrowEndPoint)
                    {
                        Vector2 mouseRelativeToStart = mousePosLogical - dArrow.StartPointRelative;
                        Vector2 unrotatedMouseRelativeToStart = HitDetection.ImRotate(mouseRelativeToStart, MathF.Cos(-dArrow.RotationAngle), MathF.Sin(-dArrow.RotationAngle));
                        dArrow.SetEndPoint(dArrow.StartPointRelative + unrotatedMouseRelativeToStart);
                    }
                    else if (currentDragType == ActiveDragType.ArrowRotate)
                    {
                        float angleNow = MathF.Atan2(mousePosLogical.Y - dragStartObjectPivotLogical.Y, mousePosLogical.X - dragStartObjectPivotLogical.X);
                        float angleThen = MathF.Atan2(dragStartMousePosLogical.Y - dragStartObjectPivotLogical.Y, dragStartMousePosLogical.X - dragStartObjectPivotLogical.X);
                        dArrow.RotationAngle = dragStartRotationAngle + (angleNow - angleThen);
                    }
                    else if (currentDragType == ActiveDragType.ArrowThickness)
                    {
                        Vector2 initialShaftVec = dragStartPoint2Logical - dragStartPoint1Logical;
                        Vector2 initialShaftDir = initialShaftVec.LengthSquared() > 0.001f ? Vector2.Normalize(initialShaftVec) : new Vector2(0, -1);
                        Vector2 perpDir = new Vector2(-initialShaftDir.Y, initialShaftDir.X);

                        Vector2 currentMouseDeltaFromDragStartLogical = mousePosLogical - dragStartMousePosLogical;
                        mouseDeltaLogicalUnrotated = HitDetection.ImRotate(currentMouseDeltaFromDragStartLogical, MathF.Cos(-dragStartRotationAngle), MathF.Sin(-dragStartRotationAngle));
                        float thicknessDelta = Vector2.Dot(mouseDeltaLogicalUnrotated, perpDir);
                        dArrow.Thickness = Math.Max(1f, dragStartValueLogical + thicknessDelta);
                    }
                }

                // General selection drag needs to be handled AFTER specific handle drags for the singleSelectedItem
                // OR ensure currentDragType is exclusively GeneralSelection
                if (currentDragType == ActiveDragType.GeneralSelection) // Check again, as it might have been changed by handle logic
                {
                    if (selectedDrawables.Any()) // Ensure there's something to drag
                    {
                        Vector2 dragDeltaLogical = mousePosLogical - lastMouseDragPosLogical;
                        if (dragDeltaLogical.LengthSquared() > 0.0001f)
                        {
                            foreach (var item in selectedDrawables)
                            {
                                item.Translate(dragDeltaLogical);
                            }
                        }
                        lastMouseDragPosLogical = mousePosLogical;
                    }
                }
            }

            if (isCanvasInteractableAndHovered && isLMBReleased)
            {
                if (currentDragType != ActiveDragType.None) ResetDragState();
            }
        }

        public void ResetDragState()
        {
            currentDragType = ActiveDragType.None;
            draggedImageResizeHandleIndex = -1;
            draggedRectCornerIndex = -1;
            draggedArrowHandleIndex = -1;
        }
    }
}
