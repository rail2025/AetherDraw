using AetherDraw.RaidPlan.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace AetherDraw.Windows
{
    public class LoadSearchWindow : Window, IDisposable
    {
        private readonly Plugin plugin;
        private string searchQuery = "";
        private string importUrlInput = "";
        private List<PublicPlanMetadata> plans = new();
        private bool isLoading = false;
        private string statusMessage = "";

        // Debounce state
        private DateTime lastInputTime;
        private bool searchPending = false;
        private const int DebounceDelayMs = 500;

        public LoadSearchWindow(Plugin plugin) : base("Community Plans###AetherDrawLoadSearchWindow")
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
            if (ImGui.BeginTabBar("LoadSearchTabs"))
            {
                // Tab 1: Community Plans (Existing Logic moved here)
                if (ImGui.BeginTabItem("Community Plans"))
                {
                    ImGui.Spacing();
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
                        // Adjust child height to account for the tab bar overhead
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

                    ImGui.EndTabItem();
                }

                // Tab 2: Import Placeholder
                if (ImGui.BeginTabItem("Import File/URL"))
                {
                    ImGui.Spacing();

                    // --- File Import Section ---
                    ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.6f, 1.0f), "Load from File");
                    ImGui.TextWrapped("Select a local .draw plan file.");

                    if (ImGui.Button("Replace Current Plan##FileReplace", new Vector2(200 * ImGuiHelpers.GlobalScale, 0)))
                    {
                        plugin.MainWindow.PlanIOManager.RequestLoadPlan();
                        IsOpen = false;
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Append to Plan##FileAppend", new Vector2(200 * ImGuiHelpers.GlobalScale, 0)))
                    {
                        plugin.MainWindow.PlanIOManager.RequestAppendPlan();
                        IsOpen = false;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Adds the pages from the file to the end of your current plan.");

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    // --- URL Import Section ---
                    ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.6f, 1.0f), "Load from URL");
                    ImGui.TextWrapped("Paste an aetherdraw.me or raidplan.io link (or raw JSON URL).");

                    ImGui.SetNextItemWidth(-1);
                    
                    ImGui.InputTextWithHint("##ImportUrlInput", "https://aetherdraw.me/...", ref importUrlInput, 512);

                    ImGui.Spacing();

                    if (ImGui.Button("Replace from URL##UrlReplace", new Vector2(200 * ImGuiHelpers.GlobalScale, 0)))
                    {
                        if (!string.IsNullOrWhiteSpace(importUrlInput))
                        {
                            _ = plugin.MainWindow.PlanIOManager.RequestLoadPlanFromUrl(importUrlInput);
                        }
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Append from URL##UrlAppend", new Vector2(200 * ImGuiHelpers.GlobalScale, 0)))
                    {
                        if (!string.IsNullOrWhiteSpace(importUrlInput))
                        {
                            _ = plugin.MainWindow.PlanIOManager.RequestAppendPlanFromUrl(importUrlInput);
                        }
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Adds the pages from the URL to the end of your current plan.");

                    // Feedback Area
                    ImGui.Spacing();
                    var lastError = plugin.MainWindow.PlanIOManager.LastFileDialogError;
                    if (!string.IsNullOrEmpty(lastError))
                    {
                        bool isSuccess = lastError.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
                                         lastError.Contains("Importing", StringComparison.OrdinalIgnoreCase) ||
                                         lastError.Contains("loaded", StringComparison.OrdinalIgnoreCase);

                        var color = isSuccess ? new Vector4(0.4f, 1.0f, 0.4f, 1.0f) : new Vector4(1.0f, 0.3f, 0.3f, 1.0f);
                        ImGui.TextColored(color, lastError);
                    }

                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            // Global Footer
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

            bool isLocked = plugin.MainWindow.PageManager.IsSessionLocked;
            using (ImRaii.Disabled(isLocked))
            {
                if (ImGui.Button("Load", new Vector2(btnWidth, 0)))
                {
                    // Construct URL and trigger load via MainWindow's existing logic
                    string url = $"https://aetherdraw.me/?plan={plan.Id}";

                    plugin.MainWindow.LoadPlanFromUrlSafe(url);
                    IsOpen = false;
                }
            }
            if (isLocked && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Session is locked by Host.");
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
