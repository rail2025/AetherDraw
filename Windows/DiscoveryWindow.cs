using AetherDraw.RaidPlan.Models;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace AetherDraw.Windows
{
    public class DiscoveryWindow : Window, IDisposable
    {
        private readonly Plugin plugin;
        private string searchQuery = "";
        private List<PublicPlanMetadata> plans = new();
        private bool isLoading = false;
        private string statusMessage = "";

        // Debounce state
        private DateTime lastInputTime;
        private bool searchPending = false;
        private const int DebounceDelayMs = 500;

        public DiscoveryWindow(Plugin plugin) : base("Community Plans###AetherDrawDiscoveryWindow")
        {
            this.plugin = plugin;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(400 * ImGuiHelpers.GlobalScale, 300 * ImGuiHelpers.GlobalScale),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };
        }

        public void Dispose() { }

        public override void OnOpen()
        {
            // Initial load if list is empty
            if (plans.Count == 0) TriggerSearch("");
        }

        public override void Draw()
        {
            ImGui.Text("Find Public Strategies");

            // Search Input with Debounce
            if (ImGui.InputText("##DiscoverySearch", ref searchQuery, 64))
            {
                lastInputTime = DateTime.Now;
                searchPending = true;
            }

            // Handle Debounce
            if (searchPending && (DateTime.Now - lastInputTime).TotalMilliseconds > DebounceDelayMs)
            {
                searchPending = false;
                TriggerSearch(searchQuery);
            }

            ImGui.Separator();

            if (isLoading)
            {
                ImGui.TextDisabled("Loading plans...");
            }
            else if (!string.IsNullOrEmpty(statusMessage))
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), statusMessage);
            }

            // Virtualized List
            if (plans.Count > 0)
            {
                float footerHeight = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();
                using var child = Dalamud.Interface.Utility.Raii.ImRaii.Child("PlanListRegion", new Vector2(0, -footerHeight));
                if (child)
                {
                    foreach (var plan in plans)
                    {
                        DrawPlanRow(plan);
                    }
                }
            }
            else if (!isLoading)
            {
                ImGui.Text("No plans found.");
            }

            ImGui.Separator();
            if (ImGui.Button("Close")) IsOpen = false;
        }

        private void DrawPlanRow(PublicPlanMetadata plan)
        {
            ImGui.PushID($"plan_{plan.Id}");

            // Rank Icon
            string rankIcon = "  ";
            if (plan.PinnedRank > 0) rankIcon = "★"; // Gold star substitute
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), rankIcon);
            ImGui.SameLine();

            // Plan Name & Tag
            string tag = string.IsNullOrEmpty(plan.BossTag) ? "" : $"[{plan.BossTag}] ";
            ImGui.Text(tag);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), plan.PlanName ?? "Untitled");

            // Views
            ImGui.SameLine();
            ImGui.TextDisabled($"({FormatViews(plan.Views)} Views)");

            // Load Button (Right Aligned)
            float btnWidth = 60 * ImGuiHelpers.GlobalScale;
            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - btnWidth - ImGui.GetStyle().ItemSpacing.X);

            if (ImGui.Button("Load", new Vector2(btnWidth, 0)))
            {
                // Construct URL and trigger load via MainWindow's existing logic
                string url = $"https://aetherdraw.me/?plan={plan.Id}";

                plugin.MainWindow.LoadPlanFromUrlSafe(url);
                IsOpen = false;
            }

            ImGui.PopID();
        }

        private string FormatViews(int views)
        {
            if (views > 999) return (views / 1000.0).ToString("0.0") + "K";
            return views.ToString();
        }

        private void TriggerSearch(string query)
        {
            isLoading = true;
            statusMessage = "";

            Task.Run(async () =>
            {
                try
                {
                    var results = await plugin.DiscoveryClient.SearchPlansAsync(query);

                    // Marshal back to UI thread
                    await Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        plans = results ?? new List<PublicPlanMetadata>();
                        isLoading = false;
                    });
                }
                catch (Exception)
                {
                    await Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        statusMessage = "Failed to load plans.";
                        isLoading = false;
                    });
                }
            });
        }
    }
}
