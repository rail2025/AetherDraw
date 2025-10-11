// AetherDraw/Networking/NetworkPayload.cs
using System;

namespace AetherDraw.Networking
{
    /// <summary>
    /// Defines the specific action being performed within a state update.
    /// This is part of the payload for the STATE_UPDATE message.
    /// </summary>
    public enum PayloadActionType : byte
    {
        /// <summary>
        /// Action to add one or more drawable objects to a page.
        /// The payload's Data will contain the serialized object(s).
        /// </summary>
        AddObjects,

        /// <summary>
        /// Action to remove one or more drawable objects from a page.
        /// The payload's Data will contain the Guid(s) of the object(s) to remove.
        /// </summary>
        DeleteObjects,

        /// <summary>
        /// Action to update/move/resize one or more drawable objects on a page.
        /// The payload's Data will contain the serialized updated object(s).
        /// </summary>
        UpdateObjects,

        /// <summary>
        /// Action to clear all drawable objects from a page.
        /// The payload's Data will typically be empty.
        /// </summary>
        ClearPage,

        /// <summary>
        /// Action to replace the entire content of a page with a new set of objects.
        /// Used for loading plans or performing an undo in a live session.
        /// The payload's Data will contain the full serialized page state.
        /// </summary>
        ReplacePage,

        /// <summary>
        /// Action to add a new, blank page to the end of the page list.
        /// The payload's Data will be empty.
        /// </summary>
        AddNewPage,

        /// <summary>
        /// Action to delete a page at a specific index.
        /// The payload's Data will be empty. The PageIndex indicates which page to remove.
        /// </summary>
        DeletePage,

        /// <summary>
        /// Action to update the grid spacing for a page.
        /// The payload's Data will contain a float representing the new grid size.
        /// </summary>
        UpdateGrid,

        /// <summary>
        /// Action to update the visibility of the grid for a page.
        /// The payload's Data will contain a byte (0 for false, 1 for true).
        /// </summary>
        UpdateGridVisibility,
    }

    /// <summary>
    /// Represents the page-aware data structure sent within a STATE_UPDATE message.
    /// This class is serialized and sent over the network.
    /// </summary>
    [Serializable]
    public class NetworkPayload
    {
        /// <summary>
        /// The zero-based index of the page that this action affects.
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// The specific action to be performed on the target page.
        /// </summary>
        public PayloadActionType Action { get; set; }

        /// <summary>
        /// The binary data associated with the action.
        /// For example, serialized drawable objects for an Add action,
        /// or object Guids for a Delete action.
        /// </summary>
        public byte[]? Data { get; set; }
    }
}
