// AetherDraw/DrawingLogic/InPlaceTextEditor.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using AetherDraw.Core;
using AetherDraw.Networking;
using AetherDraw.Serialization;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;




namespace AetherDraw.DrawingLogic
{
    /// <summary>
    /// Manages the UI and logic for editing a DrawableText object directly on the canvas.
    /// </summary>
    public class InPlaceTextEditor
    {
        private readonly Plugin plugin;
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;

        public bool IsEditing { get; private set; } = false;
        private DrawableText? targetTextObject_;
        private string originalText_ = string.Empty;
        private float originalFontSize_;
        private string editTextBuffer_ = string.Empty;
        private Vector2 editorWindowPosition_;
        private bool shouldSetFocus_ = false;

        private const int MaxBufferSize = 2048;
        private bool p_open = true;

        /// <summary>
        /// Initializes a new instance of the <see cref="InPlaceTextEditor"/> class.
        /// </summary>
        /// <param name="pluginInstance">The main plugin instance, used for network access.</param>
        /// <param name="undoManagerInstance">The undo manager for recording text changes.</param>
        /// <param name="pageManagerInstance">The page manager for context.</param>
        public InPlaceTextEditor(Plugin pluginInstance, UndoManager undoManagerInstance, PageManager pageManagerInstance)
        {
            this.plugin = pluginInstance ?? throw new ArgumentNullException(nameof(pluginInstance));
            this.undoManager = undoManagerInstance ?? throw new ArgumentNullException(nameof(undoManagerInstance));
            this.pageManager = pageManagerInstance ?? throw new ArgumentNullException(nameof(pageManagerInstance));
        }

        /// <summary>
        /// Begins an edit session for a specific text object.
        /// </summary>
        /// <param name="textObject">The text object to edit.</param>
        /// <param name="canvasOriginScreen">The screen coordinate of the canvas origin.</param>
        /// <param name="currentGlobalScale">The current ImGui global scale factor.</param>
        public void BeginEdit(DrawableText textObject, Vector2 canvasOriginScreen, float currentGlobalScale)
        {
            if (textObject == null) return;

            IsEditing = true;
            targetTextObject_ = textObject;
            originalText_ = textObject.RawText;
            originalFontSize_ = textObject.FontSize;
            editTextBuffer_ = textObject.RawText ?? string.Empty;

            if (editTextBuffer_.Length > MaxBufferSize)
                editTextBuffer_ = editTextBuffer_.Substring(0, MaxBufferSize);

            shouldSetFocus_ = true;
            RecalculateEditorBounds(canvasOriginScreen, currentGlobalScale);
        }

        /// <summary>
        /// Recalculates the on-screen position for the editor window based on the text object's location.
        /// </summary>
        /// <param name="canvasOriginScreen">The screen coordinate of the canvas origin.</param>
        /// <param name="currentGlobalScale">The current ImGui global scale factor.</param>
        public void RecalculateEditorBounds(Vector2 canvasOriginScreen, float currentGlobalScale)
        {
            if (targetTextObject_ == null) return;
            editorWindowPosition_ = (targetTextObject_.PositionRelative * currentGlobalScale) + canvasOriginScreen;
        }

        /// <summary>
        /// Draws the editor UI window.
        /// </summary>
        public void DrawEditorUI()
        {
            if (!IsEditing || targetTextObject_ == null) return;

            ImGui.SetNextWindowPos(editorWindowPosition_);
            ImGui.SetNextWindowSizeConstraints(new Vector2(200, 100) * ImGuiHelpers.GlobalScale, new Vector2(800, 600) * ImGuiHelpers.GlobalScale);
            ImGui.SetNextWindowSize(new Vector2(300, 200) * ImGuiHelpers.GlobalScale, ImGuiCond.Appearing);


            string windowId = $"##TextEditorWindow_{targetTextObject_.GetHashCodeShort()}";
            ImGuiWindowFlags editorFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8) * ImGuiHelpers.GlobalScale);

            if (ImGui.Begin(windowId, ref p_open, editorFlags))
            {
                var activeColor = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
                var fontSizes = new[] { ("S", 12f), ("M", 20f), ("L", 32f), ("XL", 48f) };

                for (int i = 0; i < fontSizes.Length; i++)
                {
                    var (label, size) = fontSizes[i];
                    bool isSelected = Math.Abs(targetTextObject_.FontSize - size) < 0.01f;

                    using (isSelected ? ImRaii.PushColor(ImGuiCol.Button, activeColor) : null)
                    {
                        if (ImGui.Button(label))
                        {
                            targetTextObject_.FontSize = size;
                        }
                    }
                    if (i < fontSizes.Length - 1) ImGui.SameLine();
                }
                ImGui.Separator();

                ImGui.InputTextMultiline("##EditText", ref editTextBuffer_, MaxBufferSize, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 5));

                if (shouldSetFocus_)
                {
                    ImGui.SetKeyboardFocusHere(-1);
                    shouldSetFocus_ = false;
                }

                bool committed = false;
                bool canceled = false;
                if (ImGui.Button("OK")) committed = true; ImGui.SameLine();
                if (ImGui.Button("Cancel")) canceled = true;
                if (ImGui.IsKeyPressed(ImGuiKey.Escape)) canceled = true;

                if (committed) CommitAndEndEdit();
                else if (canceled) CancelAndEndEdit();
            }
            ImGui.End();
            ImGui.PopStyleVar();
        }

        /// <summary>
        /// Commits the changes made in the editor to the text object and sends a network update.
        /// </summary>
        public void CommitAndEndEdit()
        {
            if (!IsEditing || targetTextObject_ == null) return;

            bool textChanged = originalText_ != editTextBuffer_;
            bool fontChanged = Math.Abs(originalFontSize_ - targetTextObject_.FontSize) > 0.01f;

            // Update the local object first
            targetTextObject_.RawText = editTextBuffer_;

            if (textChanged || fontChanged)
            {
                // Record the action for local undo
                undoManager.RecordAction(pageManager.GetCurrentPageDrawables(), "Edit Text");

                // If in a live session, send the update to other clients
                if (pageManager.IsLiveMode)
                {
                    var payload = new NetworkPayload
                    {
                        PageIndex = pageManager.GetCurrentPageIndex(),
                        Action = PayloadActionType.UpdateObjects,
                        Data = DrawableSerializer.SerializePageToBytes(new List<BaseDrawable> { targetTextObject_ })
                    };
                    _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
                }
            }

            CleanUpEditSession();
        }

        /// <summary>
        /// Cancels the edit session and reverts any changes.
        /// </summary>
        public void CancelAndEndEdit()
        {
            if (!IsEditing || targetTextObject_ == null) return;

            targetTextObject_.RawText = originalText_;
            targetTextObject_.FontSize = originalFontSize_;
            CleanUpEditSession();
        }

        /// <summary>
        /// Resets the editor state.
        /// </summary>
        private void CleanUpEditSession()
        {
            IsEditing = false;
            targetTextObject_ = null;
        }

        /// <summary>
        /// Checks if the editor is currently targeting a specific drawable object.
        /// </summary>
        /// <param name="drawable">The drawable to check.</param>
        /// <returns>True if the drawable is being edited, false otherwise.</returns>
        public bool IsCurrentlyEditing(BaseDrawable? drawable)
        {
            return IsEditing && targetTextObject_ != null && ReferenceEquals(targetTextObject_, drawable);
        }
    }
}
