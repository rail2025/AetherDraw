// AetherDraw/Core/UndoManager.cs
using AetherDraw.DrawingLogic; // Required for BaseDrawable
using AetherDraw.Windows;     // Required for MainWindow.PageData if we store full page states
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AetherDraw.Core
{
    /// <summary>
    /// Represents a single undoable action.
    /// This can be expanded later to include more details like action type (Add, Delete, Modify),
    /// specific object IDs, and before/after states for more granular undo.
    /// For now, it stores the entire list of drawables for a page before the action.
    /// </summary>
    public class UndoAction
    {
        /// <summary>
        /// Gets the state of drawables on the page before this action was performed.
        /// Each BaseDrawable in this list is a clone of an original drawable.
        /// </summary>
        public List<BaseDrawable> PreviousDrawablesState { get; private set; }

        /// <summary>
        /// Gets a description of the action (for debugging or potential future UI use).
        /// </summary>
        public string Description { get; private set; }

        public UndoAction(List<BaseDrawable> drawablesStateToSave, string description)
        {
            // Deep clone the drawables to save their state at this point in time.
            // This is crucial because the actual drawables on the canvas will continue to be modified.
            PreviousDrawablesState = new List<BaseDrawable>();
            foreach (var drawable in drawablesStateToSave)
            {
                PreviousDrawablesState.Add(drawable.Clone());
            }
            Description = description;
        }
    }

    public class UndoManager
    {
        private List<Stack<UndoAction>> undoStacks = new List<Stack<UndoAction>>();
        private int activeStackIndex = 0;
        private const int MaxUndoLevels = 30; // Arbitrary limit for undo history

        /// <summary>
        /// Ensures we have an undo stack for every page up to the requested count.
        /// Matches the behavior of the JS _ensureStacks helper.
        /// </summary>
        private void EnsureStacks(int count)
        {
            while (undoStacks.Count < count)
            {
                undoStacks.Add(new Stack<UndoAction>());
            }
        }

        /// <summary>
        // Initializes or re-initializes the undo stacks to match the number of pages.
        /// </summary>
        public void InitializeStacks(int pageCount)
        {
            undoStacks.Clear();
            for (int i = 0; i < pageCount; i++)
            {
                undoStacks.Add(new Stack<UndoAction>());
            }
            activeStackIndex = 0;
            AetherDraw.Plugin.Log?.Debug($"[UndoManager] Initialized with {pageCount} undo stacks.");
        }

        /// <summary>
        // Sets the currently active undo stack to match the selected page index.
        /// </summary>
        public void SetActivePage(int index)
        {
            // Auto-expand stacks if switching to a valid page index that doesn't have a stack yet.
            EnsureStacks(index + 1);

            if (index < 0 || index >= undoStacks.Count)
            {
                AetherDraw.Plugin.Log?.Error($"[UndoManager] SetActivePage: Invalid index {index}.");
                return;
            }
            activeStackIndex = index;
            AetherDraw.Plugin.Log?.Debug($"[UndoManager] Active page set to {index}.");
        }

        /// <summary>
        // Adds a new, empty undo stack at a specific index (e.g., when adding a page).
        /// </summary>
        public void AddStack(int index)
        {
            if (index < 0 || index > undoStacks.Count)
            {
                AetherDraw.Plugin.Log?.Error($"[UndoManager] AddStack: Invalid index {index}.");
                return;
            }
            undoStacks.Insert(index, new Stack<UndoAction>());
            AetherDraw.Plugin.Log?.Debug($"[UndoManager] Added undo stack at index {index}. Total stacks: {undoStacks.Count}");
        }

        /// <summary>
        // Removes an undo stack at a specific index (e.g., when deleting a page).
        /// </summary>
        public void RemoveStack(int index)
        {
            if (index < 0 || index >= undoStacks.Count)
            {
                AetherDraw.Plugin.Log?.Error($"[UndoManager] RemoveStack: Invalid index {index}.");
                return;
            }
            undoStacks.RemoveAt(index);
            AetherDraw.Plugin.Log?.Debug($"[UndoManager] Removed undo stack at index {index}. Total stacks: {undoStacks.Count}");
            if (activeStackIndex >= index)
            {
                activeStackIndex = Math.Max(0, activeStackIndex - 1);
            }
        }

        /// <summary>
        // Moves an undo stack from one index to another (e.g., when re-ordering pages).
        /// </summary>
        public void MoveStack(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= undoStacks.Count || toIndex < 0 || toIndex >= undoStacks.Count)
            {
                AetherDraw.Plugin.Log?.Error($"[UndoManager] MoveStack: Invalid indices. From: {fromIndex}, To: {toIndex}");
                return;
            }
            var stackToMove = undoStacks[fromIndex];
            undoStacks.RemoveAt(fromIndex);
            undoStacks.Insert(toIndex, stackToMove);
            AetherDraw.Plugin.Log?.Debug($"[UndoManager] Moved undo stack from {fromIndex} to {toIndex}.");
        }

        /// <summary>
        /// Records the current state of drawables as an undoable action.
        /// </summary>
        /// <param name="currentDrawables">The current list of drawables on the page to save.</param>
        /// <param name="actionDescription">A brief description of the action being performed.</param>
        /// <param name="actionDescription">A brief description of the action being performed.</param>
        public void RecordAction(List<BaseDrawable> currentDrawables, string actionDescription)
        {
            EnsureStacks(activeStackIndex + 1);

            if (undoStacks.Count == 0 || activeStackIndex < 0 || activeStackIndex >= undoStacks.Count)
            {
                AetherDraw.Plugin.Log?.Error($"[UndoManager] RecordAction: Cannot record, invalid state. Stacks: {undoStacks.Count}, Active: {activeStackIndex}");
                return;
            }

            var activeStack = undoStacks[activeStackIndex];
            if (activeStack.Count >= MaxUndoLevels)
            {
                TrimOldestUndo(activeStack);
            }

            var action = new UndoAction(currentDrawables, actionDescription);
            activeStack.Push(action);
            AetherDraw.Plugin.Log?.Debug($"[UndoManager] Action Recorded on page {activeStackIndex}: {actionDescription}. Stack size: {activeStack.Count}");
        }

        /// <summary>
        /// Attempts to undo the last recorded action.
        /// </summary>
        /// <returns>The list of drawables representing the state *before* the undone action, or null if stack is empty.</returns>
        public List<BaseDrawable>? Undo()
        {
            if (CanUndo())
            {
                var activeStack = undoStacks[activeStackIndex];
                UndoAction lastAction = activeStack.Pop();
                AetherDraw.Plugin.Log?.Debug($"[UndoManager] Undoing Action on page {activeStackIndex}: {lastAction.Description}. Stack size: {activeStack.Count}");
                // The caller will replace the current page's drawables with this returned state.
                // The returned list contains clones, so they are safe to use directly.
                return lastAction.PreviousDrawablesState;
            }
            AetherDraw.Plugin.Log?.Debug($"[UndoManager] Undo stack empty for page {activeStackIndex}.");
            return null;
        }

        /// <summary>
        /// Checks if there are any actions that can be undone.
        /// </summary>
        public bool CanUndo()
        {
            return undoStacks.Count > 0 && activeStackIndex >= 0 && activeStackIndex < undoStacks.Count && undoStacks[activeStackIndex].Count > 0;
        }

        /// <summary>
        /// Clears the entire undo history.
        /// Typically called when switching pages or loading a new plan.
        /// </summary>
        public void ClearHistory()
        {
            undoStacks.Clear();
            activeStackIndex = 0;
            AetherDraw.Plugin.Log?.Debug("[UndoManager] All undo history cleared.");
        }

        /// <summary>
        /// Trims the oldest undo actions if the stack exceeds MaxUndoLevels.
        /// </summary>
        private void TrimOldestUndo(Stack<UndoAction> activeStack)
        {
            if (activeStack.Count >= MaxUndoLevels)
            {
                var tempList = activeStack.ToList(); // Top item is at index 0
                while (tempList.Count >= MaxUndoLevels)
                {
                    tempList.RemoveAt(tempList.Count - 1); // Remove from the bottom (oldest)
                }
                activeStack.Clear();
                // Re-push in reverse order to get correct stack order (oldest at bottom)
                for (int i = tempList.Count - 1; i >= 0; i--)
                {
                    activeStack.Push(tempList[i]);
                }
                AetherDraw.Plugin.Log?.Debug($"[UndoManager] Trimmed undo stack to {activeStack.Count} items.");
            }
        }
    }
}
