// AetherDraw/Core/UndoManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using AetherDraw.DrawingLogic; // Required for BaseDrawable
using AetherDraw.Windows;     // Required for MainWindow.PageData if we store full page states

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
        private Stack<UndoAction> undoStack = new Stack<UndoAction>();
        private const int MaxUndoLevels = 30; // Arbitrary limit for undo history

        /// <summary>
        /// Records the current state of drawables as an undoable action.
        /// </summary>
        /// <param name="currentDrawables">The current list of drawables on the page to save.</param>
        /// <param name="actionDescription">A brief description of the action being performed.</param>
        public void RecordAction(List<BaseDrawable> currentDrawables, string actionDescription)
        {
            if (undoStack.Count >= MaxUndoLevels)
            {
                // To prevent the stack from growing indefinitely, we might remove the oldest item.
                // This requires converting Stack to a List, removing at bottom, then back to Stack,
                // or using a different data structure like a LinkedList.
                // For simplicity now, we'll just not add if full, or let it grow then trim later.
                // Alternative: Trim by creating a new stack from the desired range.
                TrimOldestUndo();
            }

            var action = new UndoAction(currentDrawables, actionDescription);
            undoStack.Push(action);
            AetherDraw.Plugin.Log?.Debug($"[UndoManager] Action Recorded: {actionDescription}. Stack size: {undoStack.Count}");
        }

        /// <summary>
        /// Attempts to undo the last recorded action.
        /// </summary>
        /// <returns>The list of drawables representing the state *before* the undone action, or null if stack is empty.</returns>
        public List<BaseDrawable>? Undo()
        {
            if (undoStack.Count > 0)
            {
                UndoAction lastAction = undoStack.Pop();
                AetherDraw.Plugin.Log?.Debug($"[UndoManager] Undoing Action: {lastAction.Description}. Stack size: {undoStack.Count}");
                // The caller will replace the current page's drawables with this returned state.
                // The returned list contains clones, so they are safe to use directly.
                return lastAction.PreviousDrawablesState;
            }
            AetherDraw.Plugin.Log?.Debug("[UndoManager] Undo stack empty.");
            return null;
        }

        /// <summary>
        /// Checks if there are any actions that can be undone.
        /// </summary>
        public bool CanUndo()
        {
            return undoStack.Count > 0;
        }

        /// <summary>
        /// Clears the entire undo history.
        /// Typically called when switching pages or loading a new plan.
        /// </summary>
        public void ClearHistory()
        {
            undoStack.Clear();
            AetherDraw.Plugin.Log?.Debug("[UndoManager] Undo history cleared.");
        }

        /// <summary>
        /// Trims the oldest undo actions if the stack exceeds MaxUndoLevels.
        /// </summary>
        private void TrimOldestUndo()
        {
            if (undoStack.Count >= MaxUndoLevels)
            {
                // This is not the most efficient way for a Stack, but simple for now.
                // A more performant approach might use a LinkedList or Deque.
                var tempList = undoStack.ToList();
                while (tempList.Count >= MaxUndoLevels)
                {
                    tempList.RemoveAt(tempList.Count - 1); // Remove from the bottom (oldest)
                }
                undoStack = new Stack<UndoAction>(tempList.AsEnumerable().Reverse()); // Rebuild stack
                AetherDraw.Plugin.Log?.Debug($"[UndoManager] Trimmed undo stack to {undoStack.Count} items.");
            }
        }
    }
}
