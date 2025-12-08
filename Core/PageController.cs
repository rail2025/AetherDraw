using AetherDraw.DrawingLogic;
using AetherDraw.Networking;
using System.Collections.Generic;

namespace AetherDraw.Core
{
    public class PageController
    {
        private readonly Plugin plugin;

        public PageController(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void SetAllLocked(PageManager pageManager, bool shouldLock)
        {
            if (!plugin.PermissionManager.IsHost) return;

            var drawables = pageManager.GetCurrentPageDrawables();
            bool anyChanged = false;

            foreach (var d in drawables)
            {
                if (d.IsLocked != shouldLock)
                {
                    d.IsLocked = shouldLock;
                    // If locking, ensure it is deselected to prevent edits
                    if (shouldLock) d.IsSelected = false;
                    anyChanged = true;
                }
            }

            if (anyChanged && plugin.NetworkManager.IsConnected)
            {
                // Serialize the updated page state and broadcast it
                var payload = new NetworkPayload
                {
                    PageIndex = pageManager.GetCurrentPageIndex(),
                    Action = PayloadActionType.UpdateObjects,
                    Data = Serialization.DrawableSerializer.SerializePageToBytes(drawables)
                };
                _ = plugin.NetworkManager.SendStateUpdateAsync(payload);
            }
        }
    }
}
