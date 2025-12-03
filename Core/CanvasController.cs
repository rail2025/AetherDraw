using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AetherDraw.DrawingLogic;
using AetherDraw.Networking;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace AetherDraw.Core
{
    public class CanvasController
    {
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;
        private readonly Plugin plugin;
        private readonly Func<DrawMode> getCurrentDrawMode;
        private readonly Action<DrawMode> setCurrentDrawMode;
        private readonly Func<Vector4> getCurrentBrushColor;
        private readonly Func<float> getCurrentBrushThickness;
        private readonly Func<bool> getCurrentShapeFilled;
        private readonly List<BaseDrawable> selectedDrawablesListRef;
        private readonly Func<BaseDrawable?> getHoveredDrawableFunc;
        private readonly Action<BaseDrawable?> setHoveredDrawableAction;
        private readonly ShapeInteractionHandler shapeInteractionHandler;
        private readonly InPlaceTextEditor inPlaceTextEditor;
        private readonly Configuration configuration;

        private bool isDrawingOnCanvas = false;
        private BaseDrawable? currentDrawingObjectInternal = null;
        private double lastEraseTime = 0;
        private string? emojiToPlace = null; // New field for placing emojis
        private uint? statusIconToPlace = null;

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
            Configuration config,
            Plugin pluginInstance)
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
            this.plugin = pluginInstance ?? throw new ArgumentNullException(nameof(pluginInstance));
        }

        public void StartPlacingEmoji(string emoji)
        {
            this.emojiToPlace = emoji;
            setCurrentDrawMode(DrawMode.EmojiImage);
            TextureManager.PreloadEmojiTexture(emoji);
        }
        public void StartPlacingStatusIcon(uint iconId)
        {
            this.statusIconToPlace = iconId;
            setCurrentDrawMode(DrawMode.StatusIconPlaceholder);
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
                case DrawMode.EmojiImage:
                    HandleEmojiPlacement(mousePosLogical, isLMBClickedOnCanvas, drawList);
                    break;
                case DrawMode.StatusIconPlaceholder:
                    HandleStatusIconPlacement(mousePosLogical, isLMBClickedOnCanvas, drawList);
                    break;
                default:
                    if (IsImagePlacementMode(getCurrentDrawMode()))
                        HandleImagePlacementInput(mousePosLogical, isLMBClickedOnCanvas, currentDrawablesOnPage);
                    else
                        HandleShapeDrawingInput(mousePosLogical, isLMBDown, isLMBClickedOnCanvas, isLMBReleased);
                    break;
            }
        }

        private void HandleEmojiPlacement(Vector2 mousePosLogical, bool isLMBClickedOnCanvas, ImDrawListPtr drawList)
        {
            if (emojiToPlace == null) return;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            var tex = TextureManager.GetTexture("emoji:" + emojiToPlace);
            if (tex != null && tex.Handle != IntPtr.Zero)
            {
                var previewSize = new Vector2(30, 30) * ImGuiHelpers.GlobalScale;
                var screenPos = ImGui.GetMousePos() - (previewSize / 2);
                ImGui.GetForegroundDrawList().AddImage(tex.Handle, screenPos, screenPos + previewSize);
            }
            else
            {
                ImGui.SetTooltip("Loading emoji...");
            }

            if (isLMBClickedOnCanvas)
            {
                var newImage = new DrawableImage(
                    DrawMode.EmojiImage,
                    "emoji:" + emojiToPlace,
                    mousePosLogical,
                    new Vector2(30f, 30f),
                    Vector4.One,
                    0f
                );
                newImage.IsPreview = false;

                var currentDrawables = pageManager.GetCurrentPageDrawables();
                undoManager.RecordAction(currentDrawables, "Place Emoji");
                currentDrawables.Add(newImage);

                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.AddObjects,
                        Data = Serialization.DrawableSerializer.SerializePageToBytes(new List<BaseDrawable> { newImage })
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }

                emojiToPlace = null;
                setCurrentDrawMode(DrawMode.Select);
            }
        }

        private void HandleStatusIconPlacement(Vector2 mousePosLogical, bool isLMBClickedOnCanvas, ImDrawListPtr drawList)
        {
            if (statusIconToPlace == null) return;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            string iconPath = $"luminaicon:{statusIconToPlace.Value}";
            var tex = TextureManager.GetTexture(iconPath);
            if (tex != null && tex.Handle != IntPtr.Zero)
            {
                var previewSize = DefaultUnscaledImageSize * ImGuiHelpers.GlobalScale;
                var screenPos = ImGui.GetMousePos() - (previewSize / 2);
                ImGui.GetForegroundDrawList().AddImage(tex.Handle, screenPos, screenPos + previewSize);
            }
            else
            {
                ImGui.SetTooltip("Loading icon...");
            }

            if (isLMBClickedOnCanvas)
            {
                var newImage = new DrawableImage(
                    DrawMode.StatusIconPlaceholder,
                    iconPath,
                    mousePosLogical,
                    DefaultUnscaledImageSize,
                    Vector4.One,
                    0f
                );
                newImage.IsPreview = false;

                var currentDrawables = pageManager.GetCurrentPageDrawables();
                undoManager.RecordAction(currentDrawables, "Place Status Icon");
                currentDrawables.Add(newImage);

                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.AddObjects,
                        Data = Serialization.DrawableSerializer.SerializePageToBytes(new List<BaseDrawable> { newImage })
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }

                statusIconToPlace = null;
                setCurrentDrawMode(DrawMode.Select);
            }
        }

        private void HandleEraserInput(Vector2 mousePosLogical, Vector2 mousePosScreen, ImDrawListPtr drawList, bool isLMBDown, List<BaseDrawable> currentDrawablesOnPage)
        {
            float scaledEraserVisualRadius = 5f * ImGuiHelpers.GlobalScale;
            drawList.AddCircle(mousePosScreen, scaledEraserVisualRadius, ImGui.GetColorU32(new Vector4(1, 0, 0, 0.7f)), 32, Math.Max(1f, 1.0f * ImGuiHelpers.GlobalScale));

            if (!isLMBDown) return;
            if (ImGui.GetTime() < lastEraseTime + 0.1) return;

            float logicalEraserRadius = 10f;
            var objectsToDelete = new List<Guid>();

            foreach (var d in currentDrawablesOnPage)
            {
                if (d.IsHit(mousePosLogical, logicalEraserRadius))
                {
                    objectsToDelete.Add(d.UniqueId);
                }
            }

            if (objectsToDelete.Any())
            {
                if (pageManager.IsLiveMode)
                {
                    using var ms = new MemoryStream();
                    using var writer = new BinaryWriter(ms);
                    writer.Write(objectsToDelete.Count);
                    foreach (var guid in objectsToDelete)
                    {
                        writer.Write(guid.ToByteArray());
                    }

                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.DeleteObjects,
                        Data = ms.ToArray()
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
                else
                {
                    undoManager.RecordAction(currentDrawablesOnPage, "Eraser Action");
                    currentDrawablesOnPage.RemoveAll(d => objectsToDelete.Contains(d.UniqueId));
                    selectedDrawablesListRef.RemoveAll(d => objectsToDelete.Contains(d.UniqueId));
                    if (getHoveredDrawableFunc() != null && objectsToDelete.Contains(getHoveredDrawableFunc()!.UniqueId))
                        setHoveredDrawableAction(null);
                }
                lastEraseTime = ImGui.GetTime();
            }
        }

        private void HandleTextToolInput(Vector2 mousePosLogical, Vector2 canvasOriginScreen, bool isLMBClickedOnCanvas, List<BaseDrawable> currentDrawablesOnPage)
        {
            if (isLMBClickedOnCanvas)
            {
                var newText = new DrawableText(mousePosLogical, "New Text", getCurrentBrushColor(), DefaultUnscaledFontSize, DefaultUnscaledTextWrapWidth);

                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.AddObjects,
                        Data = Serialization.DrawableSerializer.SerializePageToBytes(new List<BaseDrawable> { newText })
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
                else
                {
                    undoManager.RecordAction(currentDrawablesOnPage, "Add Text");
                    currentDrawablesOnPage.Add(newText);
                }

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
                DrawMode.Party1Image or DrawMode.Party2Image or DrawMode.Party3Image or DrawMode.Party4Image or
                DrawMode.Party5Image or DrawMode.Party6Image or DrawMode.Party7Image or DrawMode.Party8Image or
                DrawMode.Bind1Image or DrawMode.Bind2Image or DrawMode.Bind3Image or
                DrawMode.Ignore1Image or DrawMode.Ignore2Image or
                DrawMode.TriangleImage or DrawMode.SquareImage or DrawMode.CircleMarkImage or DrawMode.PlusImage or
                DrawMode.Dot1Image or DrawMode.Dot2Image or DrawMode.Dot3Image or DrawMode.Dot4Image or DrawMode.Dot5Image or DrawMode.Dot6Image or DrawMode.Dot7Image or DrawMode.Dot8Image or DrawMode.RoleCasterImage
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
                string imagePath = "";
                Vector2 imageUnscaledSize = DefaultUnscaledImageSize;
                Vector4 imageTint = Vector4.One;
                switch (currentMode)
                {
                    case DrawMode.BossImage: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(60f, 60f); break;
                    case DrawMode.CircleAoEImage: imagePath = "PluginImages.svg.prox_aoe.svg"; imageUnscaledSize = new Vector2(50f, 50f); break;
                    case DrawMode.DonutAoEImage: imagePath = "PluginImages.svg.donut.svg"; imageUnscaledSize = new Vector2(50f, 50f); break;
                    case DrawMode.FlareImage: imagePath = "PluginImages.svg.flare.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                    case DrawMode.LineStackImage: imagePath = "PluginImages.svg.line_stack.svg"; imageUnscaledSize = new Vector2(30f, 60f); break;
                    case DrawMode.SpreadImage: imagePath = "PluginImages.svg.spread.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
                    case DrawMode.StackImage: imagePath = "PluginImages.svg.stack.svg"; imageUnscaledSize = new Vector2(40f, 40f); break;
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
                    case DrawMode.RoleCasterImage: imagePath = "PluginImages.toolbar.caster.png"; break;
                    case DrawMode.Party1Image: imagePath = "PluginImages.toolbar.Party1.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party2Image: imagePath = "PluginImages.toolbar.Party2.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party3Image: imagePath = "PluginImages.toolbar.Party3.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party4Image: imagePath = "PluginImages.toolbar.Party4.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party5Image: imagePath = "PluginImages.toolbar.Party5.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party6Image: imagePath = "PluginImages.toolbar.Party6.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party7Image: imagePath = "PluginImages.toolbar.Party7.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Party8Image: imagePath = "PluginImages.toolbar.Party8.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Bind1Image: imagePath = "PluginImages.toolbar.bind1.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Bind2Image: imagePath = "PluginImages.toolbar.bind2.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Bind3Image: imagePath = "PluginImages.toolbar.bind3.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Ignore1Image: imagePath = "PluginImages.toolbar.ignore1.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Ignore2Image: imagePath = "PluginImages.toolbar.ignore2.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.SquareImage: imagePath = "PluginImages.toolbar.Square.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.CircleMarkImage: imagePath = "PluginImages.toolbar.CircleMark.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.TriangleImage: imagePath = "PluginImages.toolbar.Triangle.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.PlusImage: imagePath = "PluginImages.toolbar.Plus.png"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.StackIcon: imagePath = "PluginImages.svg.stack.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.SpreadIcon: imagePath = "PluginImages.svg.spread.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.TetherIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.BossIconPlaceholder: imagePath = "PluginImages.svg.boss.svg"; imageUnscaledSize = new Vector2(35f, 35f); break;
                    case DrawMode.AddMobIcon: imagePath = "PluginImages.svg.placeholder.svg"; imageUnscaledSize = new Vector2(30f, 30f); break;
                    case DrawMode.Dot1Image: imagePath = "PluginImages.svg.1dot.svg"; imageTint = new Vector4(0.3f, 0.5f, 1.0f, 1.0f); imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Dot2Image: imagePath = "PluginImages.svg.2dot.svg"; imageTint = new Vector4(1.0f, 0.3f, 0.3f, 1.0f); imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Dot3Image: imagePath = "PluginImages.svg.3dot.svg"; imageTint = new Vector4(0.3f, 0.5f, 1.0f, 1.0f); imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Dot4Image: imagePath = "PluginImages.svg.4dot.svg"; imageTint = new Vector4(1.0f, 0.3f, 0.3f, 1.0f); imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Dot5Image: imagePath = "PluginImages.svg.5dot.svg"; imageTint = new Vector4(0.3f, 0.5f, 1.0f, 1.0f); imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Dot6Image: imagePath = "PluginImages.svg.6dot.svg"; imageTint = new Vector4(1.0f, 0.3f, 0.3f, 1.0f); imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Dot7Image: imagePath = "PluginImages.svg.7dot.svg"; imageTint = new Vector4(0.3f, 0.5f, 1.0f, 1.0f); imageUnscaledSize = new Vector2(25f, 25f); break;
                    case DrawMode.Dot8Image: imagePath = "PluginImages.svg.8dot.svg"; imageTint = new Vector4(1.0f, 0.3f, 0.3f, 1.0f); imageUnscaledSize = new Vector2(25f, 25f); break;

                }

                if (!string.IsNullOrEmpty(imagePath))
                {
                    var newImage = new DrawableImage(currentMode, imagePath, mousePosLogical, imageUnscaledSize, imageTint);

                    if (pageManager.IsLiveMode)
                    {
                        var payload = new NetworkPayload
                        {
                            PageIndex = pageManager.GetCurrentPageIndex(),
                            Action = PayloadActionType.AddObjects,
                            Data = Serialization.DrawableSerializer.SerializePageToBytes(new List<BaseDrawable> { newImage })
                        };
                        _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                    }
                    else
                    {
                        undoManager.RecordAction(currentDrawablesOnPage, $"Place Image ({currentMode})");
                        currentDrawablesOnPage.Add(newImage);
                    }
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
            Vector4 finalColor = color;
            if (isFilled)
            {
                switch (mode)
                {
                    case DrawMode.Rectangle:
                    case DrawMode.Circle:
                    case DrawMode.Cone:
                    case DrawMode.Donut:
                    case DrawMode.Triangle:
                    case DrawMode.Arrow:
                    case DrawMode.Pie:
                        finalColor.W = 0.4f;
                        break;
                }
            }

            return mode switch
            {
                DrawMode.Pen => new DrawablePath(startPosLogical, color, thickness),
                DrawMode.StraightLine => new DrawableStraightLine(startPosLogical, color, thickness),
                DrawMode.Dash => new DrawableDash(startPosLogical, color, thickness),
                DrawMode.Rectangle => new DrawableRectangle(startPosLogical, finalColor, thickness, isFilled),
                DrawMode.Circle => new DrawableCircle(startPosLogical, finalColor, thickness, isFilled),
                DrawMode.Arrow => new DrawableArrow(startPosLogical, finalColor, thickness),
                DrawMode.Cone => new DrawableCone(startPosLogical, finalColor, thickness, isFilled),
                DrawMode.Triangle => new DrawableTriangle(startPosLogical, finalColor, thickness, isFilled),
                DrawMode.Pie => new DrawablePie(startPosLogical, finalColor, thickness, isFilled),
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
            else if (currentDrawingObjectInternal is DrawablePie pie && pie.Radius < 1.5f) isValidObject = false;

            if (isValidObject)
            {
                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.AddObjects,
                        Data = Serialization.DrawableSerializer.SerializePageToBytes(new List<BaseDrawable> { currentDrawingObjectInternal })
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
                else
                {
                    pageManager.GetCurrentPageDrawables().Add(currentDrawingObjectInternal);
                }
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
