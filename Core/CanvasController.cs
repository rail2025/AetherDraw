// AetherDraw/Core/CanvasController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherDraw.DrawingLogic;
using Dalamud.Interface.Utility;
using ImGuiNET;

namespace AetherDraw.Core
{
    /// <summary>
    /// Manages interactions, drawing logic, and object creation on the drawing canvas.
    /// </summary>
    public class CanvasController
    {
        // --- Injected Dependencies & Delegates ---
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;
        private readonly Func<DrawMode> getCurrentDrawMode;
        private readonly Action<DrawMode> setCurrentDrawMode;
        private readonly Func<Vector4> getCurrentBrushColor;
        private readonly Func<float> getCurrentBrushThickness; // Unscaled
        private readonly Func<bool> getCurrentShapeFilled;
        private readonly List<BaseDrawable> selectedDrawablesListRef; // Direct reference
        private readonly Func<BaseDrawable?> getHoveredDrawableFunc;
        private readonly Action<BaseDrawable?> setHoveredDrawableAction;
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly InPlaceTextEditor inPlaceTextEditor; // Ensure this is DrawingLogic.InPlaceTextEditor
        private readonly Configuration configuration;

        // --- Internal State ---
        private bool isDrawingOnCanvas = false;
        private BaseDrawable? currentDrawingObjectInternal = null; // The live preview object
        private Vector2 lastMouseDragPosLogical = Vector2.Zero;
        private double lastEraseTime = 0; // Timestamp for eraser cooldown

        // --- Constants for Object Defaults ---
        private const float DefaultUnscaledFontSize = 16f;
        private const float DefaultUnscaledTextWrapWidth = 200f;
        private static readonly Vector2 DefaultUnscaledImageSize = new Vector2(30f, 30f);

        /// <summary>
        /// Initializes a new instance of the <see cref="CanvasController"/> class.
        /// </summary>
        public CanvasController(
            UndoManager undoManagerInstance,
            PageManager pageManagerInstance,
            Func<DrawMode> getDrawModeFunc,
            Action<DrawMode> setDrawModeAction,
            Func<Vector4> getBrushColorFunc,
            Func<float> getBrushThicknessFunc,
            Func<bool> getShapeFilledFunc,
            List<BaseDrawable> selectedDrawablesRef,
            Func<BaseDrawable?> getHoveredDrawableDelegate,
            Action<BaseDrawable?> setHoveredDrawableDelegate,
            ShapeInteractionHandler siHandler,
            DrawingLogic.InPlaceTextEditor itEditor,
            Configuration config)
        {
            this.undoManager = undoManagerInstance ?? throw new ArgumentNullException(nameof(undoManagerInstance));
            this.pageManager = pageManagerInstance ?? throw new ArgumentNullException(nameof(pageManagerInstance));
            this.getCurrentDrawMode = getDrawModeFunc ?? throw new ArgumentNullException(nameof(getDrawModeFunc));
            this.setCurrentDrawMode = setDrawModeAction ?? throw new ArgumentNullException(nameof(setDrawModeAction));
            this.getCurrentBrushColor = getBrushColorFunc ?? throw new ArgumentNullException(nameof(getBrushColorFunc));
            this.getCurrentBrushThickness = getBrushThicknessFunc ?? throw new ArgumentNullException(nameof(getBrushThicknessFunc));
            this.getCurrentShapeFilled = getShapeFilledFunc ?? throw new ArgumentNullException(nameof(getShapeFilledFunc));
            this.selectedDrawablesListRef = selectedDrawablesRef ?? throw new ArgumentNullException(nameof(selectedDrawablesRef));
            this.getHoveredDrawableFunc = getHoveredDrawableDelegate ?? throw new ArgumentNullException(nameof(getHoveredDrawableDelegate));
            this.setHoveredDrawableAction = setHoveredDrawableDelegate ?? throw new ArgumentNullException(nameof(setHoveredDrawableDelegate));
            this.shapeInteractionHandler = siHandler ?? throw new ArgumentNullException(nameof(siHandler));
            this.inPlaceTextEditor = itEditor ?? throw new ArgumentNullException(nameof(itEditor));
            this.configuration = config ?? throw new ArgumentNullException(nameof(config));

            AetherDraw.Plugin.Log?.Debug("[CanvasController] Initialized with UndoManager and PageManager.");
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
            var currentDrawablesOnPage = pageManager.GetCurrentPageDrawables();
            if (currentDrawablesOnPage == null)
            {
                AetherDraw.Plugin.Log?.Error("[CanvasController.Process] currentDrawablesOnPage is null. Cannot process interactions.");
                return;
            }
            BaseDrawable? localHoveredDrawable = getHoveredDrawableFunc();

            if (isLMBDoubleClickedOnCanvas && getCurrentDrawMode() == DrawMode.Select && localHoveredDrawable is DrawableText dt)
            {
                if (!inPlaceTextEditor.IsCurrentlyEditing(dt))
                {
                    inPlaceTextEditor.BeginEdit(dt, canvasOriginScreen, ImGuiHelpers.GlobalScale);
                }
                return;
            }

            switch (getCurrentDrawMode())
            {
                case DrawMode.Select:
                    BaseDrawable? singleSelectedItem = selectedDrawablesListRef.Count == 1 ? selectedDrawablesListRef[0] : null;
                    shapeInteractionHandler.ProcessInteractions(
                        singleSelectedItem, selectedDrawablesListRef, currentDrawablesOnPage, getLayerPriorityFunc,
                        ref localHoveredDrawable,
                        mousePosLogical, mousePosScreen, canvasOriginScreen, true,
                        isLMBClickedOnCanvas, isLMBDown, isLMBReleased, drawList, ref lastMouseDragPosLogical
                    );
                    setHoveredDrawableAction(localHoveredDrawable);
                    break;

                case DrawMode.Eraser:
                    HandleEraserInput(mousePosLogical, mousePosScreen, drawList, isLMBDown, currentDrawablesOnPage);
                    break;

                case DrawMode.TextTool:
                    HandleTextToolInput(mousePosLogical, canvasOriginScreen, isLMBClickedOnCanvas, currentDrawablesOnPage);
                    break;

                default:
                    if (IsImagePlacementMode(getCurrentDrawMode()))
                    {
                        HandleImagePlacementInput(mousePosLogical, isLMBClickedOnCanvas, currentDrawablesOnPage);
                    }
                    else
                    {
                        HandleShapeDrawingInput(mousePosLogical, isLMBDown, isLMBClickedOnCanvas, isLMBReleased);
                    }
                    break;
            }
        }

        private void HandleEraserInput(Vector2 mousePosLogical, Vector2 mousePosScreen, ImDrawListPtr drawList, bool isLMBDown, List<BaseDrawable> currentDrawablesOnPage)
        {
            // Draw a visual representation of the eraser on the cursor
            float scaledEraserVisualRadius = 5f * ImGuiHelpers.GlobalScale;
            drawList.AddCircle(mousePosScreen, scaledEraserVisualRadius, ImGui.GetColorU32(new Vector4(1, 0, 0, 0.7f)), 32, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));

            if (!isLMBDown) return;

            // Enforce a 200ms cooldown to prevent frame-rate-speed erasing
            if (ImGui.GetTime() < lastEraseTime + 0.2) return;

            float logicalEraserRadius = 10f;
            bool actionTakenThisFrame = false;

            // Iterate backwards to check the top-most drawables first
            for (int i = currentDrawablesOnPage.Count - 1; i >= 0; i--)
            {
                var d = currentDrawablesOnPage[i];

                // Special handling for paths/dashes to allow partial erasing
                if (d is DrawablePath path)
                {
                    int originalCount = path.PointsRelative.Count;
                    path.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosLogical) < logicalEraserRadius);
                    if (path.PointsRelative.Count != originalCount) // If we erased any part of the path
                    {
                        undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Eraser Action (Path)");
                        actionTakenThisFrame = true;
                        // If erasing part of the path makes it too small to be valid, remove it entirely
                        if (path.PointsRelative.Count < 2 && originalCount >= 2)
                        {
                            currentDrawablesOnPage.RemoveAt(i);
                        }
                    }
                }
                else if (d is DrawableDash dashPath)
                {
                    int originalCount = dashPath.PointsRelative.Count;
                    dashPath.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosLogical) < logicalEraserRadius);
                    if (dashPath.PointsRelative.Count != originalCount) // If we erased any part of the dash
                    {
                        undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Eraser Action (Dash)");
                        actionTakenThisFrame = true;
                        if (dashPath.PointsRelative.Count < 2 && originalCount >= 2)
                        {
                            currentDrawablesOnPage.RemoveAt(i);
                        }
                    }
                }
                // Standard handling for all other drawable types
                else if (d.IsHit(mousePosLogical, logicalEraserRadius))
                {
                    undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Eraser Action (Object)");
                    currentDrawablesOnPage.RemoveAt(i); // Remove the single object
                    if (selectedDrawablesListRef.Contains(d)) selectedDrawablesListRef.Remove(d);
                    if (getHoveredDrawableFunc() != null && getHoveredDrawableFunc()!.Equals(d))
                    {
                        setHoveredDrawableAction(null);
                    }
                    actionTakenThisFrame = true;
                }

                // If any action was taken (partial erase, full removal), stop processing for this frame.
                if (actionTakenThisFrame)
                {
                    lastEraseTime = ImGui.GetTime(); // Start the cooldown
                    break;
                }
            }
        }

        private void HandleTextToolInput(Vector2 mousePosLogical, Vector2 canvasOriginScreen, bool isLMBClickedOnCanvas, List<BaseDrawable> currentDrawablesOnPage)
        {
            if (isLMBClickedOnCanvas)
            {
                undoManager.RecordAction(currentDrawablesOnPage, "Add Text"); // Record state BEFORE adding

                var newText = new DrawableText(mousePosLogical, "New Text", getCurrentBrushColor(), DefaultUnscaledFontSize, DefaultUnscaledTextWrapWidth);
                currentDrawablesOnPage.Add(newText);

                foreach (var sel in selectedDrawablesListRef) sel.IsSelected = false;
                selectedDrawablesListRef.Clear();
                newText.IsSelected = true;
                selectedDrawablesListRef.Add(newText);
                setHoveredDrawableAction(newText);

                inPlaceTextEditor.BeginEdit(newText, canvasOriginScreen, ImGuiHelpers.GlobalScale);
                setCurrentDrawMode(DrawMode.Select);
            }
        }

        private bool IsImagePlacementMode(DrawMode mode)
        {
            return mode switch
            {
                DrawMode.BossImage or DrawMode.CircleAoEImage or DrawMode.DonutAoEImage or DrawMode.FlareImage or
                DrawMode.LineStackImage or DrawMode.SpreadImage or DrawMode.StackImage or DrawMode.Waymark1Image or
                DrawMode.Waymark2Image or DrawMode.Waymark3Image or DrawMode.Waymark4Image or DrawMode.WaymarkAImage or
                DrawMode.WaymarkBImage or DrawMode.WaymarkCImage or DrawMode.WaymarkDImage or DrawMode.RoleTankImage or
                DrawMode.RoleHealerImage or DrawMode.RoleMeleeImage or DrawMode.RoleRangedImage or DrawMode.StackIcon or
                DrawMode.SpreadIcon or DrawMode.TetherIcon or DrawMode.BossIconPlaceholder or DrawMode.AddMobIcon => true,
                _ => false,
            };
        }

        private void HandleImagePlacementInput(Vector2 mousePosLogical, bool isLMBClickedOnCanvas, List<BaseDrawable> currentDrawablesOnPage)
        {
            DrawMode currentMode = getCurrentDrawMode();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Click to place {currentMode}");

            if (isLMBClickedOnCanvas)
            {
                // Record state BEFORE adding the image
                undoManager.RecordAction(currentDrawablesOnPage, $"Place Image ({currentMode})");

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
                    case DrawMode.TetherIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(25f, 25f); break; // Placeholder
                    case DrawMode.BossIconPlaceholder: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(35f, 35f); break;
                    case DrawMode.AddMobIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(30f, 30f); break; // Placeholder
                }

                if (!string.IsNullOrEmpty(imagePath))
                {
                    var newImage = new DrawableImage(currentMode, imagePath, mousePosLogical, imageUnscaledSize, tint);
                    currentDrawablesOnPage.Add(newImage);
                }
                else
                {
                    AetherDraw.Plugin.Log?.Warning($"[CanvasController.ImagePlacement] No imagePath defined for DrawMode: {currentMode}");
                }
            }
        }

        private void HandleShapeDrawingInput(Vector2 mousePosLogical, bool isLMBDown, bool isLMBClickedOnCanvas, bool isLMBReleased)
        {
            DrawMode currentMode = getCurrentDrawMode();
            float logicalBrushThickness = getCurrentBrushThickness();
            var currentDrawablesOnPage = pageManager.GetCurrentPageDrawables();

            if (isLMBDown)
            {
                if (!isDrawingOnCanvas && isLMBClickedOnCanvas)
                {
                    if (currentDrawablesOnPage != null)
                    {
                        undoManager.RecordAction(currentDrawablesOnPage, $"Start Drawing {currentMode}");
                    }

                    isDrawingOnCanvas = true;
                    foreach (var sel in selectedDrawablesListRef) sel.IsSelected = false;
                    selectedDrawablesListRef.Clear();
                    if (getHoveredDrawableFunc() != null) setHoveredDrawableAction(null);

                    currentDrawingObjectInternal = CreateNewDrawingObject(currentMode, mousePosLogical, getCurrentBrushColor(), logicalBrushThickness, getCurrentShapeFilled());

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
                FinalizeCurrentDrawing();
            }
        }

        private BaseDrawable? CreateNewDrawingObject(DrawMode mode, Vector2 startPosLogical, Vector4 color, float thickness, bool isFilled)
        {
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
                    AetherDraw.Plugin.Log?.Warning($"[CanvasController.CreateNew] Attempted to create drawable for unhandled mode: {mode}");
                    return null;
            }
        }

        private void FinalizeCurrentDrawing()
        {
            if (currentDrawingObjectInternal == null)
            {
                isDrawingOnCanvas = false;
                return;
            }

            var currentDrawablesOnPage = pageManager.GetCurrentPageDrawables();
            if (currentDrawablesOnPage == null)
            {
                isDrawingOnCanvas = false;
                currentDrawingObjectInternal = null;
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
            else if (currentDrawingObjectInternal is DrawableArrow arrow && Vector2.DistanceSquared(arrow.StartPointRelative, arrow.EndPointRelative) < minDistanceSqUnscaled) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableCone co && Vector2.DistanceSquared(co.ApexRelative, co.BaseCenterRelative) < minDistanceSqUnscaled) isValidObject = false;

            if (isValidObject)
            {
                currentDrawablesOnPage.Add(currentDrawingObjectInternal);
            }
            else
            {
                var undoneState = undoManager.Undo();
                if (undoneState != null)
                {
                    pageManager.SetCurrentPageDrawables(undoneState);
                }
                AetherDraw.Plugin.Log?.Debug($"[CanvasController.Finalize] Discarded invalid/too small: {currentDrawingObjectInternal.GetType().Name}. Attempted to revert undo stack.");
            }

            currentDrawingObjectInternal = null;
            isDrawingOnCanvas = false;
        }
    }
}
