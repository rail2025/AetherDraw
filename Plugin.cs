// Your existing Plugin.cs
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services; // Ensure this using statement is present
using AetherDraw.Windows;
using AetherDraw.DrawingLogic;

namespace AetherDraw
{
    public sealed class Plugin : IDalamudPlugin
    {
        // Plugin Services
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;
        [PluginService] internal static ITextureProvider? TextureProvider { get; private set; } = null!;

        public string Name => "AetherDraw";
        private const string CommandName = "/aetherdraw";

        public Configuration Configuration { get; init; }
        public readonly WindowSystem WindowSystem = new("AetherDraw");

        private ConfigWindow ConfigWindow { get; init; }
        private MainWindow MainWindow { get; init; }

        public Plugin()
        {
            this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            // It's good practice to also pass PluginInterface to Configuration if it needs to save itself,
            // or handle saving within the Plugin class.
            // Your current Configuration.cs expects Initialize to be called.
            this.Configuration.Initialize(PluginInterface);


            // Pass 'this' (the Plugin instance) to MainWindow if it needs access to non-static plugin members
            // However, for TextureProvider and Log, we made them static, so direct access like Plugin.TextureProvider is okay.
            // For configuration, MainWindow already gets it.
            this.ConfigWindow = new ConfigWindow(this); // Pass 'this' if ConfigWindow needs the Plugin instance
            this.MainWindow = new MainWindow(this);   // Pass 'this' if MainWindow needs the Plugin instance

            this.WindowSystem.AddWindow(this.ConfigWindow);
            this.WindowSystem.AddWindow(this.MainWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the AetherDraw whiteboard."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
            // PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI; // Optional

            Log.Information("AetherDraw loaded successfully.");
        }

        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
            // PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;

            this.WindowSystem.RemoveAllWindows();

            // If your windows implement IDisposable and need to dispose resources (like textures they manage themselves)
            // For TextureManager, we'll call its Dispose method.
            this.MainWindow.Dispose(); // Ensure MainWindow has a Dispose method if it holds disposable resources
            this.ConfigWindow.Dispose();


            TextureManager.Dispose(); // Dispose all loaded images

            CommandManager.RemoveHandler(CommandName);

            Log.Information("AetherDraw disposed.");
        }

        private void OnCommand(string command, string args)
        {
            this.MainWindow.IsOpen = !this.MainWindow.IsOpen;
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
        }

        public void ToggleConfigUI() => this.ConfigWindow.IsOpen = !this.ConfigWindow.IsOpen;
        public void ToggleMainUI() => this.MainWindow.IsOpen = !this.MainWindow.IsOpen;
    }
}
