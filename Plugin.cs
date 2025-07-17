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
    public sealed class Plugin : IDalamudPlugin
    {
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;
        [PluginService] internal static ITextureProvider? TextureProvider { get; private set; } = null!;
        [PluginService] internal static IPartyList? PartyList { get; private set; } = null!;

        public string Name => "AetherDraw";
        private const string CommandName = "/aetherdraw";
        private const string SecondWindowCommandName = "/aetherdraw2";

        public Configuration Configuration { get; init; }

        public readonly WindowSystem WindowSystem = new("AetherDraw");

        private ConfigWindow ConfigWindow { get; init; }
        private MainWindow MainWindow { get; init; }
        private LiveSessionWindow LiveSessionWindow { get; init; }
        private MainWindow? secondWindow;

        public NetworkManager NetworkManager { get; init; }

        public Plugin()
        {
            this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(PluginInterface);

            this.NetworkManager = new NetworkManager();
            this.ConfigWindow = new ConfigWindow(this);
            this.MainWindow = new MainWindow(this);
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

        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;

            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(SecondWindowCommandName);

            this.NetworkManager.Dispose();

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
            if (this.secondWindow == null)
            {
                this.secondWindow = new MainWindow(this, " 2");
                this.WindowSystem.AddWindow(this.secondWindow);
            }
            this.secondWindow.IsOpen = !this.secondWindow.IsOpen;
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
        }

        public void ToggleConfigUI() => this.ConfigWindow.IsOpen = !this.ConfigWindow.IsOpen;

        public void ToggleMainUI() => this.MainWindow.IsOpen = !this.MainWindow.IsOpen;

        public void ToggleLiveSessionUI() => this.LiveSessionWindow.IsOpen = !this.LiveSessionWindow.IsOpen;
    }
}
