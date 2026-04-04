using AetherDraw.Core;
using AetherDraw.DrawingLogic;
using AetherDraw.Networking;
using AetherDraw.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Linq;

namespace AetherDraw
{
    public sealed class Plugin : IDalamudPlugin
    {
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
        [PluginService] internal static ITextureProvider? TextureProvider { get; private set; } = null!;
        [PluginService] internal static IPartyList? PartyList { get; private set; } = null!;

        [PluginService] internal static IKeyState KeyState { get; private set; } = null!;

        public string Name => "AetherDraw";
        private const string CommandName = "/aetherdraw";
        private const string SecondWindowCommandName = "/aetherdraw2";

        public Configuration Configuration { get; init; }

        public readonly WindowSystem WindowSystem = new("AetherDraw");

        private ConfigWindow ConfigWindow { get; init; }
        public MainWindow MainWindow { get; init; }
        private LiveSessionWindow LiveSessionWindow { get; init; }
        public PropertiesWindow PropertiesWindow { get; init; }
        private MainWindow? secondWindow;

        public NetworkManager NetworkManager { get; init; }
        public DiscoveryClient DiscoveryClient { get; init; }
        public LoadSearchWindow DiscoveryWindow { get; init; }
        public AccountManager AccountManager { get; init; }

        public PermissionManager PermissionManager { get; init; }
        public PageController PageController { get; init; }
        private Dalamud.Plugin.Ipc.ICallGateProvider<string, bool>? importPlanIpcProvider;

        public class PlanExportPayload
        {
            public string Version { get; set; } = "1.0";
            public System.Collections.Generic.List<PlanPagePayload> Pages { get; set; } = new();
        }

        public class PlanPagePayload
        {
            public string Background { get; set; } = string.Empty;
            public byte[] DrawableData { get; set; } = System.Array.Empty<byte>();
        }

        private bool HandleIpcImport(string jsonPayload)
        {
            try
            {
                Log.Information($"[IPC] Received payload of length: {jsonPayload?.Length ?? 0}");
                string processedPayload = jsonPayload ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(processedPayload) && !processedPayload.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        Log.Information("[IPC] Payload does not start with '{'. Attempting Base64 decode...");
                        processedPayload = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(processedPayload));
                        Log.Information("[IPC] Base64 decode successful.");
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning(ex, "[IPC] Base64 decode failed. Proceeding with original string.");
                    }
                }

                var importedPlan = Newtonsoft.Json.JsonConvert.DeserializeObject<PlanExportPayload>(processedPayload);

                if (importedPlan != null && importedPlan.Pages != null)
                {
                    var newPages = new System.Collections.Generic.List<AetherDraw.Core.PageData>();
                    int slideNumber = 1;
                    foreach (var page in importedPlan.Pages)
                    {
                        var drawables = AetherDraw.Serialization.DrawableSerializer.DeserializePageFromBytes(page.DrawableData);

                        newPages.Add(new AetherDraw.Core.PageData
                        {
                            Name = slideNumber.ToString(),
                            Drawables = drawables ?? new System.Collections.Generic.List<AetherDraw.DrawingLogic.BaseDrawable>()
                        });
                        slideNumber++;
                    }

                    MainWindow.PageManager.AppendPages(newPages);
                    Log.Information($"Successfully imported {newPages.Count} pages via binary IPC payload.");
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "Failed to parse incoming binary IPC plan data.");
                return false;
            }
        }

        public Plugin()
        {
            this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(PluginInterface);

            this.NetworkManager = new NetworkManager();
            this.DiscoveryClient = new DiscoveryClient();
            this.AccountManager = new AccountManager();

            this.PermissionManager = new PermissionManager();
            this.PageController = new PageController(this);

            this.NetworkManager.OnHostStatusReceived += (isHost) => this.PermissionManager.SetHost(isHost);
            this.NetworkManager.OnDisconnected += () => this.PermissionManager.SetHost(true);

            this.ConfigWindow = new ConfigWindow(this);
            this.MainWindow = new MainWindow(this);
            this.LiveSessionWindow = new LiveSessionWindow(this);
            this.PropertiesWindow = new PropertiesWindow(this);
            this.DiscoveryWindow = new LoadSearchWindow(this);



            this.WindowSystem.AddWindow(this.ConfigWindow);
            this.WindowSystem.AddWindow(this.MainWindow);
            this.WindowSystem.AddWindow(this.LiveSessionWindow);
            this.WindowSystem.AddWindow(this.PropertiesWindow);
            this.WindowSystem.AddWindow(this.DiscoveryWindow);


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

            try
            {
                importPlanIpcProvider = PluginInterface.GetIpcProvider<string, bool>("AetherDraw.ImportPlanJson");
                importPlanIpcProvider.RegisterFunc(HandleIpcImport);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "Failed to register IPC provider.");
            }

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
            this.DiscoveryClient.Dispose();
            this.AccountManager.Dispose();

            this.ConfigWindow.Dispose();
            this.MainWindow.Dispose();
            this.LiveSessionWindow.Dispose();
            this.PropertiesWindow.Dispose();
            this.secondWindow?.Dispose();
            this.WindowSystem.RemoveAllWindows();

            TextureManager.Dispose();
            importPlanIpcProvider?.UnregisterFunc();

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
        public void TogglePropertiesUI() => this.PropertiesWindow.IsOpen = !this.PropertiesWindow.IsOpen;
        public void ToggleDiscoveryUI() => this.DiscoveryWindow.IsOpen = !this.DiscoveryWindow.IsOpen;
    }
}
