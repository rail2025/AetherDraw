using System;

namespace AetherDraw.Core
{
    public class PermissionManager
    {
        public bool IsHost { get; private set; } = true;
        public event Action<bool> OnPermissionsUpdated;

        public void SetHost(bool isHost)
        {
            if (IsHost != isHost)
            {
                IsHost = isHost;
                OnPermissionsUpdated?.Invoke(IsHost);
            }
        }
    }
}
