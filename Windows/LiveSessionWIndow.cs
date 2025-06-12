// AetherDraw/Windows/LiveSessionWindow.cs
using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using AetherDraw.Networking;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace AetherDraw.Windows
{
    /// <summary>
    /// A dedicated window for handling the live session connection user interface.
    /// It manages different states for choosing a connection method, entering details, and loading.
    /// </summary>
    public class LiveSessionWindow : Window, IDisposable
    {
        private readonly Plugin plugin;

        /// <summary>
        /// Defines the different views available within the Live Session window.
        /// </summary>
        private enum LiveSessionState { Choice, PassphraseEntry, Loading }
        private LiveSessionState currentState = LiveSessionState.Choice;

        // Fields to store user input for connection
        private string serverAddress = "wss://aetherdraw-server.onrender.com/ws";
        private string inputPassphrase = "";
        private string generatedPassphrase = "";
        private string statusMessage = "Disconnected";

        // --- Passphrase Generation Word Lists (Expanded for Security) ---
        private static readonly Random Random = new();
        private static readonly string[] OpinionVerbs = { "I like", "I hate", "I want", "I need", "Craving", "Seeking", "Avoiding", "Serving", "Finding", "Cooking", "Tasting", "I found", "I lost", "I traded", "He stole", "She sold", "They want", "Remembering", "Forgetting", "Questioning", "Analyzing", "Ignoring", "Praising", "Chasing", "Selling" };
        private static readonly string[] Adjectives = { "spicy", "creamy", "sultry", "glimmering", "ancient", "crispy", "zesty", "hearty", "fluffy", "savory", "frozen", "bubbling", "forbidden", "radiant", "somber", "dented", "gilded", "rusted", "glowing", "cracked", "smelly", "aromatic", "stale", "fresh", "bitter", "sweet", "silken", "spiky" };
        private static readonly string[] FfxivNouns = { "Miqote", "Lalafell", "Gridanian", "Ul'dahn", "Limsan", "Ishgardian", "Doman", "Hrothgar", "Viera", "Garlean", "Sharlayan", "Sylph", "Au Ra", "Roegadyn", "Elezen", "Thavnairian", "Coerthan", "Ala Mhigan", "Ronkan", "Eorzean", "Astrologian", "Machinist", "Samurai", "Dancer", "Paladin", "Warrior" };
        private static readonly string[] FoodItems = { "rolanberry pie", "LaNoscean toast", "dodo omelette", "pixieberry tea", "king salmon", "knightly bread", "stone soup", "archon burgers", "bubble chocolate", "tuna miq", "syrcus tower", "dalamud shard", "aetheryte shard", "allagan tomestone", "company seal", "gil-turtle", "cactuar needle", "malboro breath", "behemoth horn", "mandragora root", "black truffle", "popoto", "ruby tomato", "apkallu egg", "thavnairian onion" };
        private static readonly string[] ActionPhrases = { "in my inventory", "on the marketboard", "from a retainer", "for the Grand Company", "in a treasure chest", "from a guildhest", "at the Gold Saucer", "near the aetheryte", "without permission", "for a friend", "under the table", "with great haste", "against all odds", "for my free company", "in the goblet" };

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveSessionWindow"/> class.
        /// </summary>
        /// <param name="plugin">The main plugin instance, used to access the NetworkManager.</param>
        public LiveSessionWindow(Plugin plugin) : base("Live Session Setup", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
        {
            this.plugin = plugin;
            this.plugin.NetworkManager.OnConnected += OnNetworkConnect;
            this.plugin.NetworkManager.OnDisconnected += OnNetworkDisconnect;
            this.plugin.NetworkManager.OnError += OnNetworkError;
        }

        /// <summary>
        /// Disposes of the window and unsubscribes from events to prevent memory leaks.
        /// </summary>
        public void Dispose()
        {
            this.plugin.NetworkManager.OnConnected -= OnNetworkConnect;
            this.plugin.NetworkManager.OnDisconnected -= OnNetworkDisconnect;
            this.plugin.NetworkManager.OnError -= OnNetworkError;
        }

        /// <summary>
        /// Called by the Window System when the window is opened. Resets the UI to its initial state.
        /// </summary>
        public override void OnOpen()
        {
            this.currentState = LiveSessionState.Choice;
            this.statusMessage = plugin.NetworkManager.IsConnected ? "Connected" : "Disconnected";
            this.inputPassphrase = "";
            this.generatedPassphrase = "";
        }

        // Handles cleanup and state changes when a network connection is successfully established.
        private void OnNetworkConnect()
        {
            this.statusMessage = "Connected";
            this.IsOpen = false;
        }

        // Handles cleanup and state changes when the network connection is lost.
        private void OnNetworkDisconnect()
        {
            if (this.currentState == LiveSessionState.Loading)
            {
                this.statusMessage = "Connection failed or was closed.";
                this.currentState = LiveSessionState.Choice;
            }
            else
            {
                this.statusMessage = "Disconnected";
            }
        }

        private void OnNetworkError(string errorMessage)
        {
            this.statusMessage = errorMessage;
            this.currentState = LiveSessionState.Choice;
        }

        /// <summary>
        /// Main drawing method for the window, called every frame by the Window System.
        /// </summary>
        public override void Draw()
        {
            var viewportSize = ImGui.GetMainViewport().Size;
            ImGui.SetNextWindowPos(viewportSize / 2, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            switch (currentState)
            {
                case LiveSessionState.Choice: DrawChoiceView(); break;
                case LiveSessionState.PassphraseEntry: DrawPassphraseEntryView(); break;
                case LiveSessionState.Loading: DrawLoadingView(); break;
            }
        }

        // Renders the loading view while connecting.
        private void DrawLoadingView()
        {
            ImGui.Text(statusMessage);
            ImGui.Text("Please wait...");
            ImGui.Spacing();
            if (ImGui.Button("Cancel"))
            {
                _ = plugin.NetworkManager.DisconnectAsync();
                currentState = LiveSessionState.Choice;
            }
        }

        // Renders the initial view with two side-by-side connection choices.
        private void DrawChoiceView()
        {
            ImGui.Text("Choose Connection Method");
            ImGui.Separator();
            ImGui.Spacing();

            var paneWidth = 250 * ImGuiHelpers.GlobalScale;

            using (ImRaii.Group())
            {
                ImGui.TextWrapped("Quick Sync (Party)");
                ImGui.Separator();
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + paneWidth - 10);
                ImGui.TextWrapped("Creates a secure room for your party using a hash of your current Party ID.");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                if (ImGui.Button("Quick Sync##QuickSyncButton", new Vector2(paneWidth, 0)))
                {
                    string partyPassphrase = GetPartyIdHash();
                    if (string.IsNullOrEmpty(partyPassphrase))
                    {
                        statusMessage = "Could not get Party ID. Are you in a party?";
                    }
                    else
                    {
                        statusMessage = "Connecting via Party ID...";
                        currentState = LiveSessionState.Loading;
                        _ = plugin.NetworkManager.ConnectAsync(serverAddress, partyPassphrase);
                    }
                }
            }

            ImGui.SameLine(0, 15f * ImGuiHelpers.GlobalScale);

            using (ImRaii.Group())
            {
                ImGui.TextWrapped("Passphrase Connect");
                ImGui.Separator();
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + paneWidth - 10);
                ImGui.TextWrapped("Create or join a session using a shared passphrase. For cross-world groups.");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                if (ImGui.Button("Use Passphrase##PassphraseButton", new Vector2(paneWidth, 0)))
                {
                    currentState = LiveSessionState.PassphraseEntry;
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            if (!string.IsNullOrEmpty(statusMessage) && statusMessage != "Disconnected" && statusMessage != "Connected")
            {
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), statusMessage);
            }
            if (ImGui.Button("Cancel", new Vector2(120, 0))) this.IsOpen = false;
        }

        // Renders the view for creating or joining a session with a passphrase.
        private void DrawPassphraseEntryView()
        {
            ImGui.Text("Connect with Passphrase");
            ImGui.Separator();

            ImGui.InputText("Server Address", ref serverAddress, 256);
            ImGui.Spacing();

            ImGui.Text("Create new & copy:");
            if (ImGui.Button("Generate"))
            {
                string opinion = OpinionVerbs[Random.Next(OpinionVerbs.Length)];
                string adjective = Adjectives[Random.Next(Adjectives.Length)];
                string noun = FfxivNouns[Random.Next(FfxivNouns.Length)];
                string food = FoodItems[Random.Next(FoodItems.Length)];
                string action = ActionPhrases[Random.Next(ActionPhrases.Length)];
                generatedPassphrase = $"{opinion} {adjective} {noun} {food} {action}.";
                inputPassphrase = generatedPassphrase;
                ImGui.SetClipboardText(generatedPassphrase);
            }
            ImGui.SameLine();
            ImGui.InputText("##GeneratedPassphrase", ref generatedPassphrase, 256, ImGuiInputTextFlags.ReadOnly);

            ImGui.Spacing();
            ImGui.Text("Join existing:");
            ImGui.InputText("Enter Passphrase", ref inputPassphrase, 256);

            ImGui.Separator();

            bool canConnect = !string.IsNullOrWhiteSpace(serverAddress) && !string.IsNullOrWhiteSpace(inputPassphrase);
            using (ImRaii.Disabled(!canConnect))
            {
                if (ImGui.Button("Connect"))
                {
                    statusMessage = $"Connecting with passphrase...";
                    currentState = LiveSessionState.Loading;
                    _ = plugin.NetworkManager.ConnectAsync(serverAddress, inputPassphrase);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Back")) currentState = LiveSessionState.Choice;
        }

        private string GetPartyIdHash()
        {
            // Placeholder: In the future, we will get the real Party ID from the game state.
            // For now, we'll use a static string to simulate being in a party.
            string partyId = "STATIC_PARTY_ID_FOR_TESTING";
            if (string.IsNullOrEmpty(partyId)) return "";

            // Create a SHA256 hash of the party ID to use as a secure passphrase
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(partyId));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
