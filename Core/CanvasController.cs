// AetherDraw/Core/CanvasController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherDraw.DrawingLogic;
using AetherDraw.Windows; // For InPlaceTextEditor
using Dalamud.Interface.Utility;
using ImGuiNET;

namespace AetherDraw.Core
{
    /// <summary>
    /// Manages interactions, drawing logic, and object creation on the drawing canvas.
    /// </summary>
    public class CanvasController
    {
        // Delegates to access MainWindow's state
        private readonly Func<DrawMode> getCurrentDrawMode;
        private readonly Action<DrawMode> setCurrentDrawMode;
        private readonly Func<Vector4> getCurrentBrushColor;
        private readonly Func<float> getCurrentBrushThickness; // Unscaled
        private readonly Func<bool> getCurrentShapeFilled;
        private readonly Func<List<BaseDrawable>> getDrawablesForCurrentPageFunc;
        private readonly List<BaseDrawable> selectedDrawablesListRef; // Direct reference
        private readonly Func<BaseDrawable?> getHoveredDrawableFunc;
        private readonly Action<BaseDrawable?> setHoveredDrawableAction;

        // Dependent services
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly InPlaceTextEditor inPlaceTextEditor;
        private readonly Configuration configuration; // For default sizes

        // Internal state for drawing operations
        private bool isDrawingOnCanvas = false;
        private BaseDrawable? currentDrawingObjectInternal = null; // The live preview object
        private Vector2 lastMouseDragPosLogical = Vector2.Zero;

        // Constants for new object defaults
        private const float DefaultUnscaledFontSize = 16f;
        private const float DefaultUnscaledTextWrapWidth = 200f;
        private static readonly Vector2 DefaultUnscaledImageSize = new Vector2(30f, 30f);


        /// <summary>
        /// Initializes a new instance of the <see cref="CanvasController"/> class.
        /// </summary>
        public CanvasController(
            Func<DrawMode> getDrawModeFunc,
            Action<DrawMode> setDrawModeAction,
            Func<Vector4> getBrushColorFunc,
            Func<float> getBrushThicknessFunc,
            Func<bool> getShapeFilledFunc,
            Func<List<BaseDrawable>> getDrawablesFunc,
            List<BaseDrawable> selectedDrawablesRef,
            Func<BaseDrawable?> getHoveredDrawableDelegate,
            Action<BaseDrawable?> setHoveredDrawableDelegate,
            ShapeInteractionHandler siHandler,
            InPlaceTextEditor itEditor,
            Configuration config)
        {
            this.getCurrentDrawMode = getDrawModeFunc;
            this.setCurrentDrawMode = setDrawModeAction;
            this.getCurrentBrushColor = getBrushColorFunc;
            this.getCurrentBrushThickness = getBrushThicknessFunc;
            this.getCurrentShapeFilled = getShapeFilledFunc;
            this.getDrawablesForCurrentPageFunc = getDrawablesFunc;
            this.selectedDrawablesListRef = selectedDrawablesRef;
            this.getHoveredDrawableFunc = getHoveredDrawableDelegate;
            this.setHoveredDrawableAction = setHoveredDrawableDelegate;

            this.shapeInteractionHandler = siHandler;
            this.inPlaceTextEditor = itEditor;
            this.configuration = config;
            AetherDraw.Plugin.Log?.Debug("[CanvasController] Initialized."); // Corrected Log access
        }

        /// <summary>
        /// Gets the current drawable object being created (the live preview).
        /// </summary>
        /// <returns>The current preview drawable, or null.</returns>
        public BaseDrawable? GetCurrentDrawingObjectForPreview() => currentDrawingObjectInternal;

        /// <summary>
        /// Processes all interactions and drawing operations on the canvas based on user input.
        /// </summary>
        public void ProcessCanvasInteraction(
            Vector2 mousePosLogical, Vector2 mousePosScreen, Vector2 canvasOriginScreen, ImDrawListPtr drawList,
            bool isLMBDown, bool isLMBClickedOnCanvas, bool isLMBReleased, bool isLMBDoubleClickedOnCanvas,
            Func<DrawMode, int> getLayerPriorityFunc)
        {
            AetherDraw.Plugin.Log?.Debug($"[CanvasController.Process] Mode: {getCurrentDrawMode()}, LMBDown: {isLMBDown}, Clicked: {isLMBClickedOnCanvas}, Released: {isLMBReleased}, DblClicked: {isLMBDoubleClickedOnCanvas}"); // Corrected Log access

            var currentDrawablesOnPage = getDrawablesForCurrentPageFunc();
            BaseDrawable? localHoveredDrawable = getHoveredDrawableFunc(); // Get current hovered from MainWindow

            // Handle double-click for text editing if in select mode
            if (isLMBDoubleClickedOnCanvas && getCurrentDrawMode() == DrawMode.Select && localHoveredDrawable is DrawableText dt)
            {
                AetherDraw.Plugin.Log?.Debug($"[CanvasController.Process] Double-clicked on DrawableText, attempting to edit."); // Corrected Log access
                if (!inPlaceTextEditor.IsCurrentlyEditing(dt))
                {
                    inPlaceTextEditor.BeginEdit(dt, canvasOriginScreen, ImGuiHelpers.GlobalScale);
                }
                return; // Text editor takes focus, further canvas interaction paused
            }

            // Handle interactions based on the current drawing mode
            switch (getCurrentDrawMode())
            {
                case DrawMode.Select:
                    BaseDrawable? singleSelectedItem = selectedDrawablesListRef.Count == 1 ? selectedDrawablesListRef[0] : null;
                    shapeInteractionHandler.ProcessInteractions(
                        singleSelectedItem, selectedDrawablesListRef, currentDrawablesOnPage, getLayerPriorityFunc,
                        ref localHoveredDrawable, // SH directly modifies this local copy
                        mousePosLogical, mousePosScreen, canvasOriginScreen, true,
                        isLMBClickedOnCanvas, isLMBDown, isLMBReleased, drawList, ref lastMouseDragPosLogical
                    );
                    setHoveredDrawableAction(localHoveredDrawable); // Update MainWindow's state with the (potentially) modified hovered drawable
                    break;

                case DrawMode.Eraser:
                    HandleEraserInput(mousePosLogical, mousePosScreen, drawList, isLMBDown, currentDrawablesOnPage);
                    break;

                case DrawMode.TextTool:
                    HandleTextToolInput(mousePosLogical, canvasOriginScreen, isLMBClickedOnCanvas, currentDrawablesOnPage);
                    break;

                default: // Covers Pen, Shapes, and Image Placement modes
                    if (IsImagePlacementMode(getCurrentDrawMode()))
                    {
                        HandleImagePlacementInput(mousePosLogical, isLMBClickedOnCanvas, currentDrawablesOnPage);
                    }
                    else // Pen, Line, Rectangle, Circle, Arrow, Cone, Dash
                    {
                        HandleShapeDrawingInput(mousePosLogical, isLMBDown, isLMBClickedOnCanvas, isLMBReleased, currentDrawablesOnPage);
                    }
                    break;
            }
        }

        /// <summary>
        /// Handles input for the eraser tool.
        /// </summary>
        private void HandleEraserInput(Vector2 mousePosLogical, Vector2 mousePosScreen, ImDrawListPtr drawList, bool isLMBDown, List<BaseDrawable> currentDrawablesOnPage)
        {
            AetherDraw.Plugin.Log?.Debug($"[CanvasController.Eraser] PosL: {mousePosLogical}"); // Corrected Log access
            float scaledEraserVisualRadius = 5f * ImGuiHelpers.GlobalScale;
            drawList.AddCircle(mousePosScreen, scaledEraserVisualRadius, ImGui.GetColorU32(new Vector4(1, 0, 0, 0.7f)), 32, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));

            if (isLMBDown)
            {
                float logicalEraserRadius = 10f; // This is unscaled
                AetherDraw.Plugin.Log?.Debug($"[CanvasController.Eraser] Erasing with radius {logicalEraserRadius} at {mousePosLogical}"); // Corrected Log access

                for (int i = currentDrawablesOnPage.Count - 1; i >= 0; i--)
                {
                    var d = currentDrawablesOnPage[i];
                    bool removedByPathEdit = false;
                    if (d is DrawablePath path)
                    {
                        int originalCount = path.PointsRelative.Count;
                        path.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosLogical) < logicalEraserRadius);
                        if (path.PointsRelative.Count < 2 && originalCount >= 2) { currentDrawablesOnPage.RemoveAt(i); removedByPathEdit = true; }
                    }
                    else if (d is DrawableDash dashPath)
                    {
                        int originalCount = dashPath.PointsRelative.Count;
                        dashPath.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosLogical) < logicalEraserRadius);
                        if (dashPath.PointsRelative.Count < 2 && originalCount >= 2) { currentDrawablesOnPage.RemoveAt(i); removedByPathEdit = true; }
                    }

                    if (!removedByPathEdit && d.IsHit(mousePosLogical, logicalEraserRadius))
                    {
                        AetherDraw.Plugin.Log?.Info($"[CanvasController.Eraser] Erased: {d.GetType().Name}"); // Corrected Log access
                        currentDrawablesOnPage.RemoveAt(i);
                        if (selectedDrawablesListRef.Contains(d)) selectedDrawablesListRef.Remove(d);

                        // Use getHoveredDrawableFunc to check current MainWindow state before setting
                        if (getHoveredDrawableFunc() != null && getHoveredDrawableFunc()!.Equals(d))
                        {
                            setHoveredDrawableAction(null);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles input for the text tool.
        /// </summary>
        private void HandleTextToolInput(Vector2 mousePosLogical, Vector2 canvasOriginScreen, bool isLMBClickedOnCanvas, List<BaseDrawable> currentDrawablesOnPage)
        {
            AetherDraw.Plugin.Log?.Debug($"[CanvasController.TextTool] PosL: {mousePosLogical}"); // Corrected Log access
            if (isLMBClickedOnCanvas)
            {
                var newText = new DrawableText(mousePosLogical, "New Text", getCurrentBrushColor(), DefaultUnscaledFontSize, DefaultUnscaledTextWrapWidth);
                currentDrawablesOnPage.Add(newText);
                AetherDraw.Plugin.Log?.Info($"[CanvasController.TextTool] Created DrawableText at {mousePosLogical}"); // Corrected Log access

                foreach (var sel in selectedDrawablesListRef) sel.IsSelected = false;
                selectedDrawablesListRef.Clear();
                newText.IsSelected = true;
                selectedDrawablesListRef.Add(newText);
                setHoveredDrawableAction(newText);

                inPlaceTextEditor.BeginEdit(newText, canvasOriginScreen, ImGuiHelpers.GlobalScale);
                setCurrentDrawMode(DrawMode.Select);
            }
        }

        /// <summary>
        /// Determines if the current draw mode is for placing an image.
        /// </summary>
        private bool IsImagePlacementMode(DrawMode mode)
        {
            // Covers all image types from DrawMode enum
            return mode switch
            {
                DrawMode.BossImage => true,
                DrawMode.CircleAoEImage => true,
                DrawMode.DonutAoEImage => true,
                DrawMode.FlareImage => true,
                DrawMode.LineStackImage => true,
                DrawMode.SpreadImage => true,
                DrawMode.StackImage => true,
                DrawMode.Waymark1Image => true,
                DrawMode.Waymark2Image => true,
                DrawMode.Waymark3Image => true,
                DrawMode.Waymark4Image => true,
                DrawMode.WaymarkAImage => true,
                DrawMode.WaymarkBImage => true,
                DrawMode.WaymarkCImage => true,
                DrawMode.WaymarkDImage => true,
                DrawMode.RoleTankImage => true,
                DrawMode.RoleHealerImage => true,
                DrawMode.RoleMeleeImage => true,
                DrawMode.RoleRangedImage => true,
                DrawMode.StackIcon => true,
                DrawMode.SpreadIcon => true,
                DrawMode.TetherIcon => true,
                DrawMode.BossIconPlaceholder => true,
                DrawMode.AddMobIcon => true,
                _ => false,
            };
        }

        /// <summary>
        /// Handles input for placing images on the canvas.
        /// </summary>
        private void HandleImagePlacementInput(Vector2 mousePosLogical, bool isLMBClickedOnCanvas, List<BaseDrawable> currentDrawablesOnPage)
        {
            DrawMode currentMode = getCurrentDrawMode();
            AetherDraw.Plugin.Log?.Debug($"[CanvasController.ImagePlacement] Mode: {currentMode}, PosL: {mousePosLogical}"); // Corrected Log access
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Click to place {currentMode}");

            if (isLMBClickedOnCanvas)
            {
                string imagePath = "";
                Vector2 imageUnscaledSize = DefaultUnscaledImageSize;
                Vector4 tint = Vector4.One;

                switch (currentMode)
                {
                    case DrawMode.BossImage: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(60f, 60f); break;
                    case DrawMode.CircleAoEImage: imagePath = "PluginImages.svg.prox_aoe.svg"; imageUnscaledSize = new Vector2(50f, 50f); break;
                    case DrawMode.DonutAoEImage: imagePath = "PluginImages.svg.donut.svg"; imageUnscaledSize = new Vector2(50f, 50f); break;
                    case DrawMode.FlareImage: imagePath = "PluginImages.svg.flare.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                    case DrawMode.LineStackImage: imagePath = "PluginImages.svg.line_stack.svg"; imageUnscaledSize = new Vector2(30f, 60f); break;
                    case DrawMode.SpreadImage: imagePath = "PluginImages.svg.spread.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                    case DrawMode.StackImage: imagePath = "PluginImages.svg.stack.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                    case DrawMode.Waymark1Image: imagePath = "PluginImages.toolbar.1_waymark.JPG"; break;
                    case DrawMode.Waymark2Image: imagePath = "PluginImages.toolbar.2_waymark.JPG"; break;
                    case DrawMode.Waymark3Image: imagePath = "PluginImages.toolbar.3_waymark.JPG"; break;
                    case DrawMode.Waymark4Image: imagePath = "PluginImages.toolbar.4_waymark.JPG"; break;
                    case DrawMode.WaymarkAImage: imagePath = "PluginImages.toolbar.A.JPG"; break;
                    case DrawMode.WaymarkBImage: imagePath = "PluginImages.toolbar.B.JPG"; break;
                    case DrawMode.WaymarkCImage: imagePath = "PluginImages.toolbar.C.JPG"; break;
                    case DrawMode.WaymarkDImage: imagePath = "PluginImages.toolbar.D.JPG"; break;
                    case DrawMode.RoleTankImage: imagePath = "PluginImages.toolbar.Tank.JPG"; break;
                    case DrawMode.RoleHealerImage: imagePath = "PluginImages.toolbar.Healer.JPG"; break;
                    case DrawMode.RoleMeleeImage: imagePath = "PluginImages.toolbar.Melee.JPG"; break;
                    case DrawMode.RoleRangedImage: imagePath = "PluginImages.toolbar.Ranged.JPG"; break;
                    case DrawMode.StackIcon: imagePath = "PluginImages.svg.stack.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.SpreadIcon: imagePath = "PluginImages.svg.spread.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.TetherIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.BossIconPlaceholder: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(35f, 35f); break;
                    case DrawMode.AddMobIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(30f, 30f); break;
                }

                if (!string.IsNullOrEmpty(imagePath))
                {
                    var newImage = new DrawableImage(currentMode, imagePath, mousePosLogical, imageUnscaledSize, tint);
                    currentDrawablesOnPage.Add(newImage);
                    AetherDraw.Plugin.Log?.Info($"[CanvasController.ImagePlacement] Placed {currentMode} at {mousePosLogical}"); // Corrected Log access
                }
                else
                {
                    AetherDraw.Plugin.Log?.Warning($"[CanvasController.ImagePlacement] No imagePath defined for DrawMode: {currentMode}"); // Corrected Log access
                }
            }
        }

        /// <summary>
        /// Handles input for drawing shapes like Pen, Line, Rectangle, etc.
        /// </summary>
        private void HandleShapeDrawingInput(Vector2 mousePosLogical, bool isLMBDown, bool isLMBClickedOnCanvas, bool isLMBReleased, List<BaseDrawable> currentDrawablesOnPage)
        {
            DrawMode currentMode = getCurrentDrawMode();
            AetherDraw.Plugin.Log?.Debug($"[CanvasController.ShapeDrawing] Mode: {currentMode}, PosL: {mousePosLogical}, LMBDown: {isLMBDown}"); // Corrected Log access

            float logicalBrushThickness = getCurrentBrushThickness();

            if (isLMBDown)
            {
                if (!isDrawingOnCanvas && isLMBClickedOnCanvas)
                {
                    isDrawingOnCanvas = true;
                    foreach (var sel in selectedDrawablesListRef) sel.IsSelected = false;
                    selectedDrawablesListRef.Clear();
                    if (getHoveredDrawableFunc() != null) setHoveredDrawableAction(null);

                    currentDrawingObjectInternal = CreateNewDrawingObject(currentMode, mousePosLogical, getCurrentBrushColor(), logicalBrushThickness, getCurrentShapeFilled());
                    AetherDraw.Plugin.Log?.Info($"[CanvasController.ShapeDrawing] Started drawing: {currentMode} at {mousePosLogical}"); // Corrected Log access

                    if (currentDrawingObjectInternal is DrawablePath p) p.AddPoint(mousePosLogical);
                    else if (currentDrawingObjectInternal is DrawableDash d) d.AddPoint(mousePosLogical);
                    else currentDrawingObjectInternal?.UpdatePreview(mousePosLogical);
                }
                else if (isDrawingOnCanvas && currentDrawingObjectInternal != null)
                {
                    if (currentDrawingObjectInternal is DrawablePath p) p.AddPoint(mousePosLogical);
                    else if (currentDrawingObjectInternal is DrawableDash d) d.AddPoint(mousePosLogical);
                    else currentDrawingObjectInternal.UpdatePreview(mousePosLogical);
                }
            }

            if (isDrawingOnCanvas && isLMBReleased)
            {
                FinalizeCurrentDrawing(currentDrawablesOnPage);
            }
        }

        /// <summary>
        /// Creates a new drawable object based on the current drawing mode.
        /// </summary>
        private BaseDrawable? CreateNewDrawingObject(DrawMode mode, Vector2 startPosLogical, Vector4 color, float thickness, bool isFilled)
        {
            AetherDraw.Plugin.Log?.Debug($"[CanvasController.CreateNew] Mode: {mode}"); // Corrected Log access
            switch (mode)
            {
                case DrawMode.Pen: return new DrawablePath(startPosLogical, color, thickness);
                case DrawMode.StraightLine: return new DrawableStraightLine(startPosLogical, color, thickness);
                case DrawMode.Dash: return new DrawableDash(startPosLogical, color, thickness);
                case DrawMode.Rectangle: return new DrawableRectangle(startPosLogical, color, thickness, isFilled);
                case DrawMode.Circle: return new DrawableCircle(startPosLogical, color, thickness, isFilled);
                case DrawMode.Arrow: return new DrawableArrow(startPosLogical, color, thickness);
                case DrawMode.Cone: return new DrawableCone(startPosLogical, color, thickness, isFilled);
                default:
                    AetherDraw.Plugin.Log?.Warning($"[CanvasController.CreateNew] Attempted to create drawable for unhandled mode: {mode}"); // Corrected Log access
                    return null;
            }
        }

        /// <summary>
        /// Finalizes the current drawing object, validates it, and adds it to the page.
        /// </summary>
        private void FinalizeCurrentDrawing(List<BaseDrawable> currentDrawablesOnPage)
        {
            AetherDraw.Plugin.Log?.Debug($"[CanvasController.Finalize] Finalizing: {currentDrawingObjectInternal?.GetType().Name}"); // Corrected Log access
            if (currentDrawingObjectInternal == null)
            {
                isDrawingOnCanvas = false;
                return;
            }

            currentDrawingObjectInternal.IsPreview = false;
            bool isValidObject = true;
            float minDistanceSqUnscaled = (2f * 2f);
            float minRadiusUnscaled = 1.5f;

            if (currentDrawingObjectInternal is DrawablePath p && p.PointsRelative.Count < 2) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableDash d && d.PointsRelative.Count < 2) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableCircle ci && ci.Radius < minRadiusUnscaled) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableStraightLine sl && Vector2.DistanceSquared(sl.StartPointRelative, sl.EndPointRelative) < minDistanceSqUnscaled) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableRectangle r && (Math.Abs(r.StartPointRelative.X - r.EndPointRelative.X) < 2f || Math.Abs(r.StartPointRelative.Y - r.EndPointRelative.Y) < 2f)) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableArrow arrow && Vector2.DistanceSquared(arrow.StartPointRelative, arrow.EndPointRelative) < minDistanceSqUnscaled && arrow.StartPointRelative != arrow.EndPointRelative) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableCone co && Vector2.DistanceSquared(co.ApexRelative, co.BaseCenterRelative) < minDistanceSqUnscaled) isValidObject = false;

            if (isValidObject)
            {
                currentDrawablesOnPage.Add(currentDrawingObjectInternal);
                AetherDraw.Plugin.Log?.Info($"[CanvasController.Finalize] Added {currentDrawingObjectInternal.GetType().Name} to page."); // Corrected Log access
            }
            else
            {
                AetherDraw.Plugin.Log?.Debug($"[CanvasController.Finalize] Discarded invalid/too small: {currentDrawingObjectInternal.GetType().Name}"); // Corrected Log access
            }

            currentDrawingObjectInternal = null;
            isDrawingOnCanvas = false;
        }
    }
}
