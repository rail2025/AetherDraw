// AetherDraw/Core/PageManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherDraw.DrawingLogic; // For BaseDrawable, DrawableImage, DrawMode
using Dalamud.Interface.Utility; // For ImGuiHelpers

namespace AetherDraw.Core
{
    /// <summary>
    /// Represents a single page within the AetherDraw whiteboard.
    /// Each page has a name and a list of drawable objects.
    /// This class was formerly an inner class of MainWindow.
    /// </summary>
    public class PageData
    {
        /// <summary>
        /// Gets or sets the name of the page.
        /// </summary>
        public string Name { get; set; } = "1"; // Default name for a new page

        /// <summary>
        /// Gets or sets the list of drawable objects on this page.
        /// </summary>
        public List<BaseDrawable> Drawables { get; set; } = new List<BaseDrawable>();
    }

    /// <summary>
    /// Manages the collection of pages, current page state, and page operations for AetherDraw.
    /// This class encapsulates logic previously found in MainWindow.cs related to page management.
    /// </summary>
    public class PageManager
    {
        private List<PageData> pages = new List<PageData>();
        private int currentPageIndex = 0;
        private PageData? pageClipboard = null;

        // Fallback for when there are no drawables on the current page, to avoid null references.
        private static readonly List<BaseDrawable> EmptyDrawablesFallback = new List<BaseDrawable>();

        /// <summary>
        /// Initializes a new instance of the <see cref="PageManager"/> class.
        /// Ensures that at least one page exists upon creation.
        /// </summary>
        public PageManager()
        {
            InitializeDefaultPage();
        }

        /// <summary>
        /// Ensures that there is at least one page, creating it if necessary.
        /// This is typically called during initialization.
        /// </summary>
        private void InitializeDefaultPage()
        {
            if (!pages.Any())
            {
                AddNewPageInternal(false); // Add a new page but don't trigger a "switch" event from MainWindow's perspective
                currentPageIndex = 0;
            }
        }

        /// <summary>
        /// Gets the list of all pages.
        /// </summary>
        public List<PageData> GetAllPages() => pages;

        /// <summary>
        /// Gets the index of the currently active page.
        /// </summary>
        public int GetCurrentPageIndex() => currentPageIndex;

        /// <summary>
        /// Gets the drawables of the current page.
        /// Returns an empty list if no valid current page exists.
        /// </summary>
        public List<BaseDrawable> GetCurrentPageDrawables()
        {
            if (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            {
                return pages[currentPageIndex].Drawables;
            }
            AetherDraw.Plugin.Log?.Warning("[PageManager] Attempted to GetCurrentPageDrawables with no valid current page.");
            return EmptyDrawablesFallback;
        }

        /// <summary>
        /// Sets the drawables for the current page.
        /// Used primarily by the UndoManager to restore a page state.
        /// </summary>
        /// <param name="drawables">The list of drawables to set for the current page.</param>
        public void SetCurrentPageDrawables(List<BaseDrawable> drawables)
        {
            if (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            {
                pages[currentPageIndex].Drawables = drawables;
            }
            else
            {
                AetherDraw.Plugin.Log?.Warning("[PageManager] Attempted to SetCurrentPageDrawables with no valid current page.");
            }
        }

        /// <summary>
        /// Clears all drawables from the current page.
        /// </summary>
        public void ClearCurrentPageDrawables()
        {
            if (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            {
                GetCurrentPageDrawables().Clear();
                // Reset page name if it's the only page and it's cleared
                if (pages.Count == 1 && currentPageIndex == 0) pages[currentPageIndex].Name = "1";
            }
            else
            {
                AetherDraw.Plugin.Log?.Warning("[PageManager] Attempted to ClearCurrentPageDrawables with no valid current page.");
            }
        }


        /// <summary>
        /// Adds a new page with default waymarks.
        /// </summary>
        /// <param name="switchToPage">If true, sets the new page as the current page.</param>
        /// <returns>True if the page was added and switched to (if requested), false otherwise.</returns>
        public bool AddNewPage(bool switchToPage = true)
        {
            return AddNewPageInternal(switchToPage);
        }

        /// <summary>
        /// Internal implementation for adding a new page.
        /// </summary>
        private bool AddNewPageInternal(bool switchToPage)
        {
            AetherDraw.Plugin.Log?.Info("[PageManager] Adding new page.");
            int newPageNumber = pages.Any() ? pages.Select(p => int.TryParse(p.Name, out int num) ? num : 0).DefaultIfEmpty(0).Max() + 1 : 1;
            var newPage = new PageData { Name = newPageNumber.ToString() };

            // Preload default waymarks (copied from MainWindow)
            float logicalRefCanvasWidth = (850f * 0.75f) - 125f;
            float logicalRefCanvasHeight = 550f;
            Vector2 canvasCenter = new Vector2(logicalRefCanvasWidth / 2f, logicalRefCanvasHeight / 2f);
            float waymarkPlacementRadius = Math.Min(logicalRefCanvasWidth, logicalRefCanvasHeight) * 0.40f;
            Vector2 waymarkImageUnscaledSize = new Vector2(30f, 30f);
            Vector4 waymarkTint = Vector4.One;

            var waymarksToPreload = new[] {
                new { Mode = DrawMode.WaymarkAImage, Path = "PluginImages.toolbar.A.png", Angle = 3 * MathF.PI / 2 },
                new { Mode = DrawMode.WaymarkBImage, Path = "PluginImages.toolbar.B.png", Angle = 0f },
                new { Mode = DrawMode.WaymarkCImage, Path = "PluginImages.toolbar.C.png", Angle = MathF.PI / 2 },
                new { Mode = DrawMode.WaymarkDImage, Path = "PluginImages.toolbar.D.png", Angle = MathF.PI },
                new { Mode = DrawMode.Waymark1Image, Path = "PluginImages.toolbar.1_waymark.png", Angle = 5 * MathF.PI / 4 },
                new { Mode = DrawMode.Waymark2Image, Path = "PluginImages.toolbar.2_waymark.png", Angle = 7 * MathF.PI / 4 },
                new { Mode = DrawMode.Waymark3Image, Path = "PluginImages.toolbar.3_waymark.png", Angle = MathF.PI / 4 },
                new { Mode = DrawMode.Waymark4Image, Path = "PluginImages.toolbar.4_waymark.png", Angle = 3 * MathF.PI / 4 }
            };
            foreach (var wmInfo in waymarksToPreload)
            {
                float x = canvasCenter.X + waymarkPlacementRadius * MathF.Cos(wmInfo.Angle);
                float y = canvasCenter.Y + waymarkPlacementRadius * MathF.Sin(wmInfo.Angle);
                var drawableImage = new DrawableImage(wmInfo.Mode, wmInfo.Path, new Vector2(x, y), waymarkImageUnscaledSize, waymarkTint, 0f);
                drawableImage.IsPreview = false;
                newPage.Drawables.Add(drawableImage);
            }
            pages.Add(newPage);

            if (switchToPage)
            {
                return SwitchToPage(pages.Count - 1, true); // Force switch as it's a new page
            }
            return true; // Page added, but not switched
        }

        /// <summary>
        /// Deletes the current page. Does nothing if only one page exists.
        /// </summary>
        /// <returns>True if the page was deleted, false otherwise.</returns>
        public bool DeleteCurrentPage()
        {
            AetherDraw.Plugin.Log?.Info("[PageManager] Deleting current page.");
            if (pages.Count <= 1)
            {
                AetherDraw.Plugin.Log?.Warning("[PageManager] Cannot delete the last page.");
                return false; // Cannot delete the last page
            }

            int pageIndexToRemove = currentPageIndex;
            pages.RemoveAt(pageIndexToRemove);
            currentPageIndex = Math.Max(0, Math.Min(pageIndexToRemove, pages.Count - 1));

            // After deleting, we must be on a valid page.
            // The SwitchToPage will handle resetting interaction states via MainWindow.
            // It's implied MainWindow will call SwitchToPage on its end after this.
            // For now, this method just modifies the internal state.
            // MainWindow will call SwitchToPage(this.currentPageIndex, true) which clears undo.
            return true;
        }

        /// <summary>
        /// Checks if there is a page stored in the clipboard.
        /// </summary>
        /// <returns>True if a page has been copied, false otherwise.</returns>
        public bool HasCopiedPage()
        {
            return this.pageClipboard != null;
        }

        /// <summary>
        /// Creates a deep clone of the current page and stores it in the internal clipboard.
        /// </summary>
        public void CopyCurrentPageToClipboard()
        {
            if (pages.Count == 0 || currentPageIndex < 0 || currentPageIndex >= pages.Count) return;

            var sourcePage = pages[currentPageIndex];

            // Create a new PageData object and deep clone all drawables into it.
            this.pageClipboard = new PageData
            {
                Name = sourcePage.Name, // We'll rename it on paste.
                Drawables = sourcePage.Drawables.Select(d => d.Clone()).ToList()
            };

            AetherDraw.Plugin.Log?.Info($"[PageManager] Copied page '{sourcePage.Name}' to clipboard.");
        }

        /// <summary>
        /// Clears the current page and pastes the contents from the clipboard onto it.
        /// </summary>
        /// <returns>True if the paste and overwrite was successful, false otherwise.</returns>
        public bool PastePageFromClipboard()
        {
            // Do nothing if the clipboard is empty or the current page is invalid.
            if (this.pageClipboard == null || pages.Count == 0 || currentPageIndex < 0 || currentPageIndex >= pages.Count)
            {
                AetherDraw.Plugin.Log?.Warning("[PageManager] Paste attempted but clipboard is empty or page index is invalid.");
                return false;
            }

            // Get the current page that will be overwritten.
            var targetPage = pages[currentPageIndex];

            // Clear the existing contents.
            targetPage.Drawables.Clear();

            // Add a deep copy of each drawable from the clipboard to the target page.
            // Cloning is essential so the clipboard can be used for multiple pastes.
            targetPage.Drawables.AddRange(this.pageClipboard.Drawables.Select(d => d.Clone()));

            AetherDraw.Plugin.Log?.Info($"[PageManager] Pasted clipboard onto page '{targetPage.Name}'.");
            return true;
        }

        /// <summary>
        /// Switches the active page to the one at the specified index.
        /// </summary>
        /// <param name="newPageIndex">The index of the page to switch to.</param>
        /// <param name="forceSwitch">If true, forces the switch even if the index is the same.</param>
        /// <returns>True if the switch was successful, false otherwise.</returns>
        public bool SwitchToPage(int newPageIndex, bool forceSwitch = false)
        {
            AetherDraw.Plugin.Log?.Info($"[PageManager] Attempting to switch to page index {newPageIndex}. Current: {currentPageIndex}, Total: {pages.Count}");
            if (newPageIndex < 0 || newPageIndex >= pages.Count)
            {
                AetherDraw.Plugin.Log?.Warning($"[PageManager] Invalid page index {newPageIndex}.");
                return false; // Invalid index
            }
            if (!forceSwitch && newPageIndex == currentPageIndex)
            {
                AetherDraw.Plugin.Log?.Debug($"[PageManager] Already on page {newPageIndex}, no switch needed.");
                return true; // Already on the page, and not forced
            }

            currentPageIndex = newPageIndex;
            AetherDraw.Plugin.Log?.Info($"[PageManager] Switched to page index {currentPageIndex}.");
            // MainWindow will be responsible for clearing undo history after this call.
            return true;
        }

        /// <summary>
        /// Loads a set of pages, replacing all existing pages.
        /// Typically used when loading a plan from a file.
        /// </summary>
        /// <param name="loadedPagesData">The list of PageData objects to load.</param>
        public void LoadPages(List<PageData> loadedPagesData)
        {
            AetherDraw.Plugin.Log?.Info($"[PageManager] Loading {loadedPagesData.Count} pages.");
            pages.Clear();
            foreach (var loadedPage in loadedPagesData)
            {
                // Ensure the drawables list is not null, creating a new one if necessary.
                pages.Add(new PageData { Name = loadedPage.Name, Drawables = loadedPage.Drawables ?? new List<BaseDrawable>() });
            }

            currentPageIndex = 0; // Default to the first page after loading
            if (!pages.Any())
            {
                AetherDraw.Plugin.Log?.Warning("[PageManager] Loaded plan was empty. Adding a default page.");
                AddNewPageInternal(false); // Add a default page if the loaded plan was empty
            }
            // MainWindow will be responsible for clearing undo history after this call.
        }
    }
}
