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
    /// Manages the direct interaction logic on the drawing canvas, such as creating new shapes,
    /// erasing, and delegating complex interactions like object manipulation to the ShapeInteractionHandler.
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
        private readonly List<BaseDrawable> selectedDrawablesListRef; // Direct reference to MainWindow's selection list
        private readonly Func<BaseDrawable?> getHoveredDrawableFunc;
        private readonly Action<BaseDrawable?> setHoveredDrawableAction;
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly InPlaceTextEditor inPlaceTextEditor;
        private readonly Configuration configuration;

        // --- Internal State ---
        private bool isDrawingOnCanvas = false;
        private BaseDrawable? currentDrawingObjectInternal = null; // The live preview object being drawn
        private double lastEraseTime = 0; // Timestamp for eraser cooldown to prevent rapid-fire erasing

        // --- Constants for Object Defaults ---
        private const float DefaultUnscaledFontSize = 16f;
        private const float DefaultUnscaledTextWrapWidth = 200f;
        private static readonly Vector2 DefaultUnscaledImageSize = new Vector2(30f, 30f);

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
        }

        public BaseDrawable? GetCurrentDrawingObjectForPreview() => currentDrawingObjectInternal;

        public void ProcessCanvasInteraction(
            Vector2 mousePosLogical, Vector2 mousePosScreen, Vector2 canvasOriginScreen, ImDrawListPtr drawList,
            bool isLMBDown, bool isLMBClickedOnCanvas, bool isLMBReleased, bool isLMBDoubleClickedOnCanvas,
            Func<DrawMode, int> getLayerPriorityFunc)
        {
            var currentDrawablesOnPage = pageManager.GetCurrentPageDrawables();
            if (currentDrawablesOnPage == null) return;

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
                        mousePosLogical, mousePosScreen, canvasOriginScreen,
                        isLMBClickedOnCanvas, isLMBDown, isLMBReleased, drawList
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
                        HandleImagePlacementInput(mousePosLogical, isLMBClickedOnCanvas, currentDrawablesOnPage);
                    else
                        HandleShapeDrawingInput(mousePosLogical, isLMBDown, isLMBClickedOnCanvas, isLMBReleased);
                    break;
            }
        }

        private void HandleEraserInput(Vector2 mousePosLogical, Vector2 mousePosScreen, ImDrawListPtr drawList, bool isLMBDown, List<BaseDrawable> currentDrawablesOnPage)
        {
            float scaledEraserVisualRadius = 5f * ImGuiHelpers.GlobalScale;
            drawList.AddCircle(mousePosScreen, scaledEraserVisualRadius, ImGui.GetColorU32(new Vector4(1, 0, 0, 0.7f)), 32, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));

            if (!isLMBDown) return;
            if (ImGui.GetTime() < lastEraseTime + 0.1) return;

            float logicalEraserRadius = 10f;
            bool actionTakenThisFrame = false;

            for (int i = currentDrawablesOnPage.Count - 1; i >= 0; i--)
            {
                var d = currentDrawablesOnPage[i];
                if (d is DrawablePath path)
                {
                    int originalCount = path.PointsRelative.Count;
                    path.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosLogical) < logicalEraserRadius);
                    if (path.PointsRelative.Count != originalCount)
                    {
                        undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Eraser Action (Path)");
                        actionTakenThisFrame = true;
                        if (path.PointsRelative.Count < 2 && originalCount >= 2)
                            currentDrawablesOnPage.RemoveAt(i);
                    }
                }
                else if (d is DrawableDash dashPath)
                {
                    int originalCount = dashPath.PointsRelative.Count;
                    dashPath.PointsRelative.RemoveAll(pt => Vector2.Distance(pt, mousePosLogical) < logicalEraserRadius);
                    if (dashPath.PointsRelative.Count != originalCount)
                    {
                        undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Eraser Action (Dash)");
                        actionTakenThisFrame = true;
                        if (dashPath.PointsRelative.Count < 2 && originalCount >= 2)
                            currentDrawablesOnPage.RemoveAt(i);
                    }
                }
                else if (d.IsHit(mousePosLogical, logicalEraserRadius))
                {
                    undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Eraser Action (Object)");
                    currentDrawablesOnPage.RemoveAt(i);
                    if (selectedDrawablesListRef.Contains(d)) selectedDrawablesListRef.Remove(d);
                    if (getHoveredDrawableFunc() != null && getHoveredDrawableFunc()!.Equals(d))
                        setHoveredDrawableAction(null);
                    actionTakenThisFrame = true;
                }
                if (actionTakenThisFrame)
                {
                    lastEraseTime = ImGui.GetTime();
                    break;
                }
            }
        }

        private void HandleTextToolInput(Vector2 mousePosLogical, Vector2 canvasOriginScreen, bool isLMBClickedOnCanvas, List<BaseDrawable> currentDrawablesOnPage)
        {
            if (isLMBClickedOnCanvas)
            {
                undoManager.RecordAction(currentDrawablesOnPage, "Add Text");
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
                DrawMode.SpreadIcon or DrawMode.TetherIcon or DrawMode.BossIconPlaceholder or DrawMode.AddMobIcon or
                // Add the new modes here to recognize them as placeable images
                DrawMode.Party1Image or DrawMode.Party2Image or DrawMode.Party3Image or DrawMode.Party4Image or
                DrawMode.TriangleImage or DrawMode.SquareImage or DrawMode.CircleMarkImage or DrawMode.PlusImage
                => true,
                _ => false,
            };
        }

        private void HandleImagePlacementInput(Vector2 mousePosLogical, bool isLMBClickedOnCanvas, List<BaseDrawable> currentDrawablesOnPage)
        {
            DrawMode currentMode = getCurrentDrawMode();
            if (ImGui.IsWindowHovered()) ImGui.SetTooltip($"Click to place {currentMode}");

            if (isLMBClickedOnCanvas)
            {
                undoManager.RecordAction(currentDrawablesOnPage, $"Place Image ({currentMode})");
                string imagePath = "";
                Vector2 imageUnscaledSize = DefaultUnscaledImageSize;
                switch (currentMode)
                {
                    case DrawMode.BossImage: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(60f, 60f); break;
                    case DrawMode.CircleAoEImage: imagePath = "PluginImages.svg.prox_aoe.svg"; imageUnscaledSize = new Vector2(50f, 50f); break;
                    case DrawMode.DonutAoEImage: imagePath = "PluginImages.svg.donut.svg"; imageUnscaledSize = new Vector2(50f, 50f); break;
                    case DrawMode.FlareImage: imagePath = "PluginImages.svg.flare.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                    case DrawMode.LineStackImage: imagePath = "PluginImages.svg.line_stack.svg"; imageUnscaledSize = new Vector2(30f, 60f); break;
                    case DrawMode.SpreadImage: imagePath = "PluginImages.svg.spread.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                    case DrawMode.StackImage: imagePath = "PluginImages.svg.stack.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;

                    // Note: Update these paths once the new transparent SVGs/PNGs are finalized
                    case DrawMode.Waymark1Image: imagePath = "PluginImages.toolbar.1_waymark.png"; break;
                    case DrawMode.Waymark2Image: imagePath = "PluginImages.toolbar.2_waymark.png"; break;
                    case DrawMode.Waymark3Image: imagePath = "PluginImages.toolbar.3_waymark.png"; break;
                    case DrawMode.Waymark4Image: imagePath = "PluginImages.toolbar.4_waymark.png"; break;
                    case DrawMode.WaymarkAImage: imagePath = "PluginImages.toolbar.A.png"; break;
                    case DrawMode.WaymarkBImage: imagePath = "PluginImages.toolbar.B.png"; break;
                    case DrawMode.WaymarkCImage: imagePath = "PluginImages.toolbar.C.png"; break;
                    case DrawMode.WaymarkDImage: imagePath = "PluginImages.toolbar.D.png"; break;

                    case DrawMode.RoleTankImage: imagePath = "PluginImages.toolbar.Tank.JPG"; break;
                    case DrawMode.RoleHealerImage: imagePath = "PluginImages.toolbar.Healer.JPG"; break;
                    case DrawMode.RoleMeleeImage: imagePath = "PluginImages.toolbar.Melee.JPG"; break;
                    case DrawMode.RoleRangedImage: imagePath = "PluginImages.toolbar.Ranged.JPG"; break;

                    // Add cases for the new icons
                    case DrawMode.Party1Image: imagePath = "PluginImages.toolbar.Party1.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party2Image: imagePath = "PluginImages.toolbar.Party2.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party3Image: imagePath = "PluginImages.toolbar.Party3.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party4Image: imagePath = "PluginImages.toolbar.Party4.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.SquareImage: imagePath = "PluginImages.toolbar.Square.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.CircleMarkImage: imagePath = "PluginImages.toolbar.CircleMark.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.TriangleImage: imagePath = "PluginImages.toolbar.Triangle.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.PlusImage: imagePath = "PluginImages.toolbar.Plus.png"; imageUnscaledSize = new Vector2(25f, 25f); break;

                    case DrawMode.StackIcon: imagePath = "PluginImages.svg.stack.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.SpreadIcon: imagePath = "PluginImages.svg.spread.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.TetherIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.BossIconPlaceholder: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(35f, 35f); break;
                    case DrawMode.AddMobIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(30f, 30f); break;
                }

                if (!string.IsNullOrEmpty(imagePath))
                {
                    var newImage = new DrawableImage(currentMode, imagePath, mousePosLogical, imageUnscaledSize, Vector4.One);
                    currentDrawablesOnPage.Add(newImage);
                }
            }
        }

        private void HandleShapeDrawingInput(Vector2 mousePosLogical, bool isLMBDown, bool isLMBClickedOnCanvas, bool isLMBReleased)
        {
            if (isLMBDown)
            {
                if (!isDrawingOnCanvas && isLMBClickedOnCanvas)
                {
                    undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), $"Start Drawing {getCurrentDrawMode()}");
                    isDrawingOnCanvas = true;
                    foreach (var sel in selectedDrawablesListRef) sel.IsSelected = false;
                    selectedDrawablesListRef.Clear();
                    if (getHoveredDrawableFunc() != null) setHoveredDrawableAction(null);
                    currentDrawingObjectInternal = CreateNewDrawingObject(getCurrentDrawMode(), mousePosLogical, getCurrentBrushColor(), getCurrentBrushThickness(), getCurrentShapeFilled());
                }
                if (isDrawingOnCanvas && currentDrawingObjectInternal != null)
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
            return mode switch
            {
                DrawMode.Pen => new DrawablePath(startPosLogical, color, thickness),
                DrawMode.StraightLine => new DrawableStraightLine(startPosLogical, color, thickness),
                DrawMode.Dash => new DrawableDash(startPosLogical, color, thickness),
                DrawMode.Rectangle => new DrawableRectangle(startPosLogical, color, thickness, isFilled),
                DrawMode.Circle => new DrawableCircle(startPosLogical, color, thickness, isFilled),
                DrawMode.Arrow => new DrawableArrow(startPosLogical, color, thickness),
                DrawMode.Cone => new DrawableCone(startPosLogical, color, thickness, isFilled),
                _ => null,
            };
        }

        private void FinalizeCurrentDrawing()
        {
            if (currentDrawingObjectInternal == null)
            {
                isDrawingOnCanvas = false;
                return;
            }
            currentDrawingObjectInternal.IsPreview = false;
            bool isValidObject = true;
            if (currentDrawingObjectInternal is DrawablePath p && p.PointsRelative.Count < 2) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableDash d && d.PointsRelative.Count < 2) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableCircle ci && ci.Radius < 1.5f) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableStraightLine sl && Vector2.DistanceSquared(sl.StartPointRelative, sl.EndPointRelative) < 4f) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableRectangle r && (Math.Abs(r.StartPointRelative.X - r.EndPointRelative.X) < 2f || Math.Abs(r.StartPointRelative.Y - r.EndPointRelative.Y) < 2f)) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableArrow arrow && Vector2.DistanceSquared(arrow.StartPointRelative, arrow.EndPointRelative) < 4f) isValidObject = false;
            else if (currentDrawingObjectInternal is DrawableCone co && Vector2.DistanceSquared(co.ApexRelative, co.BaseCenterRelative) < 4f) isValidObject = false;

            if (isValidObject)
            {
                pageManager.GetCurrentPageDrawables().Add(currentDrawingObjectInternal);
            }
            else
            {
                var undoneState = undoManager.Undo();
                if (undoneState != null)
                {
                    pageManager.SetCurrentPageDrawables(undoneState);
                }
            }
            currentDrawingObjectInternal = null;
            isDrawingOnCanvas = false;
        }
    }
}
