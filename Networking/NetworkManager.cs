// AetherDraw/Networking/NetworkManager.cs
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherDraw.Serialization;

namespace AetherDraw.Networking
{
    public class NetworkManager : IDisposable
    {
        private ClientWebSocket? webSocket;
        private CancellationTokenSource? cancellationTokenSource;

        // General connection events
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action<string>? OnError;

        // Specific message-driven events with binary payloads
        public event Action<byte[]>? OnAddObjectsReceived;
        public event Action<byte[]>? OnDeleteObjectReceived;
        public event Action<byte[]>? OnMoveObjectReceived;
        public event Action? OnClearPageReceived;
        public event Action<byte[]>? OnReplaceFullPageReceived;

        public bool IsConnected => webSocket?.State == WebSocketState.Open;

        public async Task ConnectAsync(string serverUri, string passphrase)
        {
            if (IsConnected) return;

            try
            {
                webSocket = new ClientWebSocket();
                cancellationTokenSource = new CancellationTokenSource();
                Uri connectUri = new Uri($"{serverUri}?passphrase={Uri.EscapeDataString(passphrase)}");

                AetherDraw.Plugin.Log?.Info($"[NetworkManager] Connecting to {connectUri}...");
                await webSocket.ConnectAsync(connectUri, cancellationTokenSource.Token);
                AetherDraw.Plugin.Log?.Info("[NetworkManager] WebSocket connection established.");

                OnConnected?.Invoke();
                _ = Task.Run(() => StartListening(cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, "[NetworkManager] Failed to connect.");
                OnError?.Invoke($"Connection failed: {ex.Message}");
                await DisconnectAsync();
            }
        }

        public async Task DisconnectAsync()
        {
            if (webSocket == null) return;

            // Cancel any pending operations like the listening loop.
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
            }

            // Only attempt a graceful close if the socket is actually in an open state.
            if (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    // Use a timeout for the close operation to prevent it from hanging.
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", cts.Token);
                }
                catch (Exception ex)
                {
                    // This is expected if the connection was abruptly terminated.
                    // We can log it as a lower-level message instead of an error.
                    AetherDraw.Plugin.Log?.Debug(ex, "[NetworkManager] Exception during graceful disconnection attempt.");
                }
            }

            webSocket?.Dispose();
            webSocket = null;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;

            OnDisconnected?.Invoke();
        }

        private async Task StartListening(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];

            try
            {
                while (webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await DisconnectAsync();
                    }
                    else
                    {
                        ms.Seek(0, SeekOrigin.Begin);
                        HandleReceivedMessage(ms.ToArray());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AetherDraw.Plugin.Log?.Info("[NetworkManager] Listening task cancelled.");
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, "[NetworkManager] Error in listening loop.");
                OnError?.Invoke($"Network error: {ex.Message}");
                await DisconnectAsync();
            }
        }

        private void HandleReceivedMessage(byte[] messageBytes)
        {
            if (messageBytes.Length < 1) return;

            MessageType type = (MessageType)messageBytes[0];
            byte[] payload = new byte[messageBytes.Length - 1];
            Array.Copy(messageBytes, 1, payload, 0, payload.Length);

            switch (type)
            {
                case MessageType.ADD_OBJECTS: OnAddObjectsReceived?.Invoke(payload); break;
                case MessageType.DELETE_OBJECT: OnDeleteObjectReceived?.Invoke(payload); break;
                case MessageType.MOVE_OBJECT: OnMoveObjectReceived?.Invoke(payload); break;
                case MessageType.CLEAR_PAGE: OnClearPageReceived?.Invoke(); break;
                case MessageType.REPLACE_FULL_PAGE_STATE: OnReplaceFullPageReceived?.Invoke(payload); break;
            }
        }

        public async Task SendMessageAsync(MessageType type, byte[]? payload)
        {
            if (!IsConnected || webSocket == null) return;

            try
            {
                int payloadLength = payload?.Length ?? 0;
                byte[] messageToSend = new byte[1 + payloadLength];
                messageToSend[0] = (byte)type;
                if (payloadLength > 0 && payload != null) // Added "&& payload != null" to fix warning
                {
                    Array.Copy(payload, 0, messageToSend, 1, payloadLength);
                }

                await webSocket.SendAsync(new ArraySegment<byte>(messageToSend), WebSocketMessageType.Binary, true, cancellationTokenSource!.Token);
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, "[NetworkManager] Failed to send message.");
                OnError?.Invoke($"Failed to send message: {ex.Message}");
                await DisconnectAsync();
            }
        }

        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
