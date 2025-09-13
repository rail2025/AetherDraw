// AetherDraw/Core/PageManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherDraw.DrawingLogic;

namespace AetherDraw.Core
{
    /// <summary>
    /// Represents a single page within the AetherDraw whiteboard.
    /// Each page has a name and a list of drawable objects.
    /// </summary>
    public class PageData
    {
        /// <summary>
        /// Gets or sets the name of the page.
        /// </summary>
        public string Name { get; set; } = "1";

        /// <summary>
        /// Gets or sets the list of drawable objects on this page.
        /// </summary>
        public List<BaseDrawable> Drawables { get; set; } = new List<BaseDrawable>();
    }

    /// <summary>
    /// Manages the collection of pages, current page state, and page operations for AetherDraw.
    /// This class encapsulates logic for both local and live-session page management.
    /// </summary>
    public class PageManager
    {
        private List<PageData> localPages = new List<PageData>();
        private List<PageData> livePages = new List<PageData>();
        private int currentPageIndex = 0;
        private PageData? pageClipboard = null;
        private static readonly List<BaseDrawable> EmptyDrawablesFallback = new List<BaseDrawable>();

        /// <summary>
        /// Gets or sets a value indicating whether the manager is in a live network session.
        /// </summary>
        public bool IsLiveMode { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="PageManager"/> class.
        /// </summary>
        public PageManager()
        {
            InitializeDefaultPage();
        }

        private void InitializeDefaultPage()
        {
            if (!localPages.Any())
            {
                localPages.Add(CreateDefaultPage("1"));
                currentPageIndex = 0;
            }
        }

        private PageData CreateDefaultPage(string name)
        {
            var newPage = new PageData { Name = name };

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
                var drawableImage = new DrawableImage(wmInfo.Mode, wmInfo.Path, new Vector2(x, y), waymarkImageUnscaledSize, waymarkTint, 0f)
                {
                    IsPreview = false
                };
                Plugin.Log?.Debug($"[PageManager] Created default waymark '{wmInfo.Mode}' with UniqueId: {drawableImage.UniqueId}"); 
                newPage.Drawables.Add(drawableImage);
            }
            return newPage;
        }

        /// <summary>
        /// Switches the manager to live mode, clearing any previous live pages and creating a new default one.
        /// </summary>
        public void EnterLiveMode()
        {
            IsLiveMode = true;
            livePages.Clear();
            livePages.Add(CreateDefaultPage("1"));
            currentPageIndex = 0;
            Plugin.Log?.Info("[PageManager] Entered live mode. Created initial live page with default layout.");
        }

        /// <summary>
        /// Switches the manager back to local mode.
        /// </summary>
        public void ExitLiveMode()
        {
            IsLiveMode = false;
            currentPageIndex = 0;
            Plugin.Log?.Info("[PageManager] Exited live mode.");
        }

        /// <summary>
        /// Gets the list of all pages for the current mode (local or live).
        /// </summary>
        /// <returns>A list of <see cref="PageData"/>.</returns>
        public List<PageData> GetAllPages() => IsLiveMode ? livePages : localPages;

        /// <summary>
        /// Gets the index of the currently active page.
        /// </summary>
        /// <returns>The zero-based index of the current page.</returns>
        public int GetCurrentPageIndex() => currentPageIndex;

        /// <summary>
        /// Gets the drawables of the current page.
        /// </summary>
        /// <returns>A list of <see cref="BaseDrawable"/> objects, or an empty list if no valid page exists.</returns>
        public List<BaseDrawable> GetCurrentPageDrawables()
        {
            var pages = GetAllPages();
            if (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            {
                return pages[currentPageIndex].Drawables;
            }
            return EmptyDrawablesFallback;
        }

        /// <summary>
        /// Sets the drawables for the current page, replacing any existing ones.
        /// </summary>
        /// <param name="drawables">The list of drawables to set for the current page.</param>
        public void SetCurrentPageDrawables(List<BaseDrawable> drawables)
        {
            var pages = GetAllPages();
            if (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            {
                pages[currentPageIndex].Drawables = drawables;
            }
            else if (IsLiveMode && pages.Count == 0)
            {
                var newPage = new PageData { Name = "1", Drawables = drawables };
                pages.Add(newPage);
                currentPageIndex = 0;
            }
        }

        /// <summary>
        /// Clears all drawables from the current page.
        /// </summary>
        public void ClearCurrentPageDrawables()
        {
            var pages = GetAllPages();
            if (pages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < pages.Count)
            {
                pages[currentPageIndex].Drawables.Clear();
                if (pages.Count == 1 && currentPageIndex == 0) pages[currentPageIndex].Name = "1";
            }
        }

        /// <summary>
        /// Adds a new page with a default layout.
        /// </summary>
        /// <param name="switchToPage">If true, sets the new page as the current page.</param>
        /// <returns>True if the page was added successfully.</returns>
        public bool AddNewPage(bool switchToPage = true)
        {
            var pages = GetAllPages();
            int newPageNumber = pages.Any() ? pages.Select(p => int.TryParse(p.Name, out int num) ? num : 0).DefaultIfEmpty(0).Max() + 1 : 1;
            pages.Add(CreateDefaultPage(newPageNumber.ToString()));

            if (switchToPage)
            {
                return SwitchToPage(pages.Count - 1, true);
            }
            return true;
        }

        /// <summary>
        /// Deletes the current page, if more than one page exists.
        /// </summary>
        /// <returns>True if the page was deleted, false otherwise.</returns>
        public bool DeleteCurrentPage()
        {
            var pages = GetAllPages();
            if (pages.Count <= 1) return false;
            pages.RemoveAt(currentPageIndex);
            currentPageIndex = Math.Max(0, Math.Min(currentPageIndex, pages.Count - 1));
            return true;
        }

        /// <summary>
        /// Checks if there is a page stored in the internal clipboard.
        /// </summary>
        /// <returns>True if a page has been copied.</returns>
        public bool HasCopiedPage() => this.pageClipboard != null;

        /// <summary>
        /// Clones the current page and stores it in the internal clipboard.
        /// </summary>
        public void CopyCurrentPageToClipboard()
        {
            var pages = GetAllPages();
            if (pages.Count == 0 || currentPageIndex < 0 || currentPageIndex >= pages.Count) return;
            var sourcePage = pages[currentPageIndex];
            this.pageClipboard = new PageData
            {
                Name = sourcePage.Name,
                Drawables = sourcePage.Drawables.Select(d => d.Clone()).ToList()
            };
        }

        /// <summary>
        // Replaces the current page's content with the content from the clipboard.
        /// </summary>
        /// <returns>True if the paste was successful.</returns>
        public bool PastePageFromClipboard()
        {
            var pages = GetAllPages();
            if (this.pageClipboard == null || pages.Count == 0 || currentPageIndex < 0 || currentPageIndex >= pages.Count) return false;
            var targetPage = pages[currentPageIndex];
            targetPage.Drawables.Clear();
            targetPage.Drawables.AddRange(this.pageClipboard.Drawables.Select(d => d.Clone()));
            return true;
        }

        /// <summary>
        /// Switches the active page to the one at the specified index.
        /// </summary>
        /// <param name="newPageIndex">The index of the page to switch to.</param>
        /// <param name="forceSwitch">If true, forces the switch even if the index is the same as the current one.</param>
        /// <returns>True if the switch was successful.</returns>
        public bool SwitchToPage(int newPageIndex, bool forceSwitch = false)
        {
            var pages = GetAllPages();
            if (newPageIndex < 0 || newPageIndex >= pages.Count) return false;
            if (!forceSwitch && newPageIndex == currentPageIndex) return true;
            currentPageIndex = newPageIndex;
            return true;
        }

        /// <summary>
        /// Replaces all current pages with a new set of pages.
        /// </summary>
        /// <param name="loadedPagesData">The list of <see cref="PageData"/> to load.</param>
        public void LoadPages(List<PageData> loadedPagesData)
        {
            var pages = GetAllPages();
            pages.Clear();
            pages.AddRange(loadedPagesData);
            currentPageIndex = 0;
            if (!pages.Any())
            {
                InitializeDefaultPage();
            }
        }
    }
}
