// AetherDraw/DrawingLogic/InPlaceTextEditor.cs
using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility;
using AetherDraw.Core;
using System.Collections.Generic;
using Dalamud.Interface.Utility.Raii;

namespace AetherDraw.DrawingLogic
{
    public class InPlaceTextEditor
    {
        private readonly UndoManager undoManager;
        private readonly PageManager pageManager;

        public bool IsEditing { get; private set; } = false;
        private DrawableText? targetTextObject_;
        private string originalText_ = string.Empty;
        private float originalFontSize_;
        private string editTextBuffer_ = string.Empty;
        private Vector2 editorWindowPosition_;
        //private Vector2 editorWindowSize_;
        private bool shouldSetFocus_ = false;
        //private bool initialAutoSelectDone_ = false;

        private const int MaxBufferSize = 2048;

        public InPlaceTextEditor(UndoManager undoManagerInstance, PageManager pageManagerInstance)
        {
            this.undoManager = undoManagerInstance ?? throw new ArgumentNullException(nameof(undoManagerInstance));
            this.pageManager = pageManagerInstance ?? throw new ArgumentNullException(nameof(pageManagerInstance));
        }

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
            // initialAutoSelectDone_ = false;
            RecalculateEditorBounds(canvasOriginScreen, currentGlobalScale);
        }

        public void RecalculateEditorBounds(Vector2 canvasOriginScreen, float currentGlobalScale)
        {
            if (targetTextObject_ == null) return;
            // Directly calculate the screen position. This is the correct and simpler way.
            editorWindowPosition_ = (targetTextObject_.PositionRelative * currentGlobalScale) + canvasOriginScreen;
        }

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
                // Font Size Buttons
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

                    if (i < fontSizes.Length - 1)
                    {
                        ImGui.SameLine();
                    }
                }
                ImGui.Separator();

                // Text Input
                ImGui.InputTextMultiline("##EditText", ref editTextBuffer_, MaxBufferSize, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 5));

                if (shouldSetFocus_)
                {
                    ImGui.SetKeyboardFocusHere(-1);
                    shouldSetFocus_ = false;
                }

                // OK/Cancel Buttons
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

        private bool p_open = true;

        public void CommitAndEndEdit()
        {
            if (!IsEditing || targetTextObject_ == null) return;

            bool textChanged = originalText_ != editTextBuffer_;
            bool fontChanged = Math.Abs(originalFontSize_ - targetTextObject_.FontSize) > 0.01f;

            if (textChanged || fontChanged)
            {
                var currentDrawables = pageManager.GetCurrentPageDrawables();
                if (currentDrawables != null)
                {
                    // Create a clone representing the "before" state for the undo stack
                    var originalStateClone = (DrawableText)targetTextObject_.Clone();
                    originalStateClone.RawText = originalText_;
                    originalStateClone.FontSize = originalFontSize_;
                    originalStateClone.PerformLayout();

                    // Find and replace the object in a temporary list to record the action
                    var listForUndo = new List<BaseDrawable>();
                    foreach (var d in currentDrawables)
                    {
                        listForUndo.Add(ReferenceEquals(d, targetTextObject_) ? originalStateClone : d.Clone());
                    }
                    undoManager.RecordAction(listForUndo, "Edit Text");
                }
            }

            targetTextObject_.RawText = editTextBuffer_;
            CleanUpEditSession();
        }

        public void CancelAndEndEdit()
        {
            if (!IsEditing || targetTextObject_ == null) return;

            targetTextObject_.RawText = originalText_;
            targetTextObject_.FontSize = originalFontSize_;
            CleanUpEditSession();
        }

        private void CleanUpEditSession()
        {
            IsEditing = false;
            targetTextObject_ = null;
        }

        public bool IsCurrentlyEditing(BaseDrawable? drawable)
        {
            return IsEditing && targetTextObject_ != null && ReferenceEquals(targetTextObject_, drawable);
        }
    }
}
