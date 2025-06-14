using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using AetherDraw.Windows;
using AetherDraw.DrawingLogic;
using AetherDraw.Networking;

namespace AetherDraw
{
    /// <summary>
    /// The main plugin class for AetherDraw.
    /// Initializes all services, windows, and managers.
    /// </summary>
    public sealed class Plugin : IDalamudPlugin
    {
        // Plugin Services
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;
        [PluginService] internal static ITextureProvider? TextureProvider { get; private set; } = null!;
        [PluginService] internal static IPartyList? PartyList { get; private set; } = null!;

        /// <inheritdoc/>
        public string Name => "AetherDraw";
        private const string CommandName = "/aetherdraw";
        private const string SecondWindowCommandName = "/aetherdraw2";

        /// <summary>
        /// Gets the plugin's configuration.
        /// </summary>
        public Configuration Configuration { get; init; }

        /// <summary>
        /// Gets the system that manages all plugin windows.
        /// </summary>
        public readonly WindowSystem WindowSystem = new("AetherDraw");

        // Windows
        private ConfigWindow ConfigWindow { get; init; }
        private MainWindow MainWindow { get; init; }
        private LiveSessionWindow LiveSessionWindow { get; init; }
        private MainWindow? secondWindow; // A second main window for testing

        /// <summary>
        /// Gets the manager for handling WebSocket network connections.
        /// </summary>
        public NetworkManager NetworkManager { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        public Plugin()
        {
            this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(PluginInterface);

            this.NetworkManager = new NetworkManager();
            this.ConfigWindow = new ConfigWindow(this);
            this.MainWindow = new MainWindow(this); // First window has no ID suffix
            this.LiveSessionWindow = new LiveSessionWindow(this);

            this.WindowSystem.AddWindow(this.ConfigWindow);
            this.WindowSystem.AddWindow(this.MainWindow);
            this.WindowSystem.AddWindow(this.LiveSessionWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the main AetherDraw whiteboard."
            });
            CommandManager.AddHandler(SecondWindowCommandName, new CommandInfo(OnSecondWindowCommand)
            {
                ShowInHelp = false,
                HelpMessage = "Opens a second AetherDraw window for testing."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

            Log.Information("AetherDraw loaded successfully.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;

            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(SecondWindowCommandName);

            this.NetworkManager.Dispose();

            // Dispose all windows
            this.ConfigWindow.Dispose();
            this.MainWindow.Dispose();
            this.LiveSessionWindow.Dispose();
            this.secondWindow?.Dispose();
            this.WindowSystem.RemoveAllWindows();

            TextureManager.Dispose();

            Log.Information("AetherDraw disposed.");
        }

        private void OnCommand(string command, string args)
        {
            this.MainWindow.IsOpen = !this.MainWindow.IsOpen;
        }

        private void OnSecondWindowCommand(string command, string args)
        {
            // If the second window doesn't exist yet, create it with a unique ID.
            if (this.secondWindow == null)
            {
                // Give the second window a suffix to make its name/ID unique.
                this.secondWindow = new MainWindow(this, " 2");
                this.WindowSystem.AddWindow(this.secondWindow);
            }
            // Toggle its visibility.
            this.secondWindow.IsOpen = !this.secondWindow.IsOpen;
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
        }

        /// <summary>
        /// Toggles the visibility of the configuration window.
        /// </summary>
        public void ToggleConfigUI() => this.ConfigWindow.IsOpen = !this.ConfigWindow.IsOpen;

        /// <summary>
        /// Toggles the visibility of the main window.
        /// </summary>
        public void ToggleMainUI() => this.MainWindow.IsOpen = !this.MainWindow.IsOpen;

        /// <summary>
        /// Toggles the visibility of the live session window.
        /// </summary>
        public void ToggleLiveSessionUI() => this.LiveSessionWindow.IsOpen = !this.LiveSessionWindow.IsOpen;
    }
}
