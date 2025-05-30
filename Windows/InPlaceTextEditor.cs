using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Utility;

namespace AetherDraw.DrawingLogic
{
    public class InPlaceTextEditor
    {
        public bool IsEditing { get; set; } = false;
        private DrawableText? targetTextObject_;
        private string editTextBuffer_ = string.Empty;
        private Vector2 editorWindowPosition_; // Screen position
        private Vector2 editorWindowSize_;     // Screen size
        private bool shouldSetFocus_ = false;
        private bool initialAutoSelectDone_ = false;

        private const int MaxBufferSize = 2048;

        public void BeginEdit(DrawableText textObject, Vector2 canvasOriginScreen, float currentGlobalScale)
        {
            if (textObject == null)
            {
                AetherDraw.Plugin.Log?.Warning("[InPlaceTextEditor] BeginEdit called with null textObject.");
                IsEditing = false;
                return;
            }

            IsEditing = true;
            targetTextObject_ = textObject;
            editTextBuffer_ = targetTextObject_.RawText ?? string.Empty;
            if (editTextBuffer_.Length > MaxBufferSize)
            {
                editTextBuffer_ = editTextBuffer_.Substring(0, MaxBufferSize);
                AetherDraw.Plugin.Log?.Warning($"[InPlaceTextEditor] Text truncated to {MaxBufferSize} chars for editing.");
            }
            shouldSetFocus_ = true;
            initialAutoSelectDone_ = false;

            AetherDraw.Plugin.Log?.Debug($"[InPlaceTextEditor] BeginEdit for DrawableText ID:{targetTextObject_.GetHashCodeShort()}.");
            RecalculateEditorBounds(canvasOriginScreen, currentGlobalScale);
        }

        public void RecalculateEditorBounds(Vector2 canvasOriginScreen, float currentGlobalScale)
        {
            if (targetTextObject_ == null) return;

            editorWindowPosition_ = Vector2.Transform(targetTextObject_.PositionRelative, targetTextObject_.GetBasePositionToScreenTransformMatrixForEditor(canvasOriginScreen));

            float scaledFontSize = Math.Max(1f, targetTextObject_.FontSize * currentGlobalScale);
            float scaledWrappingWidthTarget = 0f;

            ImFontPtr font = ImGui.GetFont();
            float originalScale = font.Scale;
            bool scaleChanged = false;

            if (font.ConfigDataCount > 0 && font.ConfigData.SizePixels > 0)
            {
                font.Scale = scaledFontSize / font.ConfigData.SizePixels;
                scaleChanged = true;
            }

            if (targetTextObject_.WrappingWidth > 0.01f)
            {
                scaledWrappingWidthTarget = targetTextObject_.WrappingWidth * currentGlobalScale;
            }
            else
            {
                Vector2 textSizeUnwrapped = ImGui.CalcTextSize(editTextBuffer_.Length > 0 ? editTextBuffer_ : "M"); // Measure with a char if empty
                scaledWrappingWidthTarget = Math.Min(textSizeUnwrapped.X + 50 * currentGlobalScale, 400 * currentGlobalScale); // Add more padding
                scaledWrappingWidthTarget = Math.Max(scaledWrappingWidthTarget, 200 * currentGlobalScale);
            }

            int numLines = editTextBuffer_.Split('\n').Length;
            float minContentHeight = ImGui.GetTextLineHeightWithSpacing() * Math.Max(2, numLines) + ImGui.GetStyle().FramePadding.Y * 2; // Min 2 lines height for content
            if (string.IsNullOrWhiteSpace(editTextBuffer_))
            {
                minContentHeight = ImGui.GetTextLineHeightWithSpacing() * 2.5f + ImGui.GetStyle().FramePadding.Y * 2;
            }

            Vector2 editorContentSize = ImGui.CalcTextSize(editTextBuffer_.Length > 0 ? editTextBuffer_ : " ", false, scaledWrappingWidthTarget);
            editorContentSize.Y = Math.Max(minContentHeight, editorContentSize.Y + ImGui.GetStyle().FramePadding.Y * 2);
            editorContentSize.X = scaledWrappingWidthTarget;

            if (scaleChanged) font.Scale = originalScale;

            // Ensure space for OK/Cancel buttons
            float buttonsHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
            editorWindowSize_ = new Vector2(
                Math.Max(200 * currentGlobalScale, editorContentSize.X + ImGui.GetStyle().FramePadding.X * 2 + 20 * currentGlobalScale),
                Math.Max(ImGui.GetTextLineHeightWithSpacing() * 3f + buttonsHeight, editorContentSize.Y + buttonsHeight + 15 * currentGlobalScale)
            );
            editorWindowSize_.X = Math.Min(editorWindowSize_.X, 600 * currentGlobalScale);
            editorWindowSize_.Y = Math.Min(editorWindowSize_.Y, 400 * currentGlobalScale);
        }


        public void DrawEditorUI()
        {
            if (!IsEditing || targetTextObject_ == null) return;

           

            ImGui.SetNextWindowPos(editorWindowPosition_);
            ImGui.SetNextWindowSize(editorWindowSize_);

            string windowId = $"##TextEditorWindow_{targetTextObject_.GetHashCodeShort()}";
            ImGuiWindowFlags editorFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
                                           ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings |
                                           ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove;
            // ImGuiWindowFlags.AlwaysAutoResize; // Removing this for more explicit control

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4) * ImGuiHelpers.GlobalScale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * ImGuiHelpers.GlobalScale);

            bool p_open = true;
            if (ImGui.Begin(windowId, ref p_open, editorFlags))
            {
                float targetScaledFontSize = targetTextObject_.FontSize * ImGuiHelpers.GlobalScale;
                ImFontPtr font = ImGui.GetFont();
                float originalFontScale = font.Scale;
                bool fontScaleWasAdjusted = false;

                if (font.ConfigDataCount > 0 && font.ConfigData.SizePixels > 0)
                {
                    font.Scale = targetScaledFontSize / font.ConfigData.SizePixels;
                    fontScaleWasAdjusted = true;
                }

                ImGuiInputTextFlags inputTextFlags = ImGuiInputTextFlags.AllowTabInput | ImGuiInputTextFlags.CtrlEnterForNewLine;
                if (!initialAutoSelectDone_) inputTextFlags |= ImGuiInputTextFlags.AutoSelectAll;

                float inputTextHeight = editorWindowSize_.Y - ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().WindowPadding.Y * 2 - ImGui.GetStyle().ItemSpacing.Y * 2;
                inputTextHeight = Math.Max(inputTextHeight, ImGui.GetTextLineHeightWithSpacing() * 1.5f);

                if (ImGui.InputTextMultiline("##EditText", ref editTextBuffer_, MaxBufferSize,
                    new Vector2(ImGui.GetContentRegionAvail().X, inputTextHeight),
                    inputTextFlags))
                {
                    // Text changed, could potentially re-evaluate editor size if it were dynamic
                }

                if (fontScaleWasAdjusted) font.Scale = originalFontScale;

                if (shouldSetFocus_)
                {
                    ImGui.SetKeyboardFocusHere(-1);
                    shouldSetFocus_ = false;
                }
                if (!initialAutoSelectDone_ && ImGui.IsItemActive()) initialAutoSelectDone_ = true;

                bool committed = false;
                bool canceled = false;

                if ((ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyPressed(ImGuiKey.Enter, true)))
                {
                    committed = true;
                }
                if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape, true))
                {
                    canceled = true;
                }

                if (ImGui.Button("OK", new Vector2(50 * ImGuiHelpers.GlobalScale, 0))) committed = true;
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(60 * ImGuiHelpers.GlobalScale, 0))) canceled = true;

                if (IsEditing && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.RootWindow))
                {
                    committed = true;
                }

                if (committed) CommitAndEndEdit();
                else if (canceled) CancelAndEndEdit();
            }
            ImGui.End();
            ImGui.PopStyleVar(2);

            if (!p_open && IsEditing)
            {
                CommitAndEndEdit();
            }
        }

        public void CommitAndEndEdit()
        {
            if (!IsEditing || targetTextObject_ == null) return;
            targetTextObject_.RawText = editTextBuffer_;
            CleanUpEditSession();
        }

        public void CancelAndEndEdit()
        {
            if (!IsEditing) return;
            CleanUpEditSession();
        }

        private void CleanUpEditSession()
        {
            IsEditing = false;
            targetTextObject_ = null;
            editTextBuffer_ = string.Empty;
            shouldSetFocus_ = false;
            initialAutoSelectDone_ = false;
        }

        public bool IsCurrentlyEditing(BaseDrawable? drawable)
        {
            return IsEditing && targetTextObject_ != null && ReferenceEquals(targetTextObject_, drawable);
        }
        private string LimitString(string s, int maxLength) => (s.Length <= maxLength) ? s : s.Substring(0, maxLength) + "...";
    }
}
