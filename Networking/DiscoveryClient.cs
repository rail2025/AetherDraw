using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AetherDraw.RaidPlan.Models;
using Dalamud.Logging;

namespace AetherDraw.Networking
{
    public class DiscoveryClient : IDisposable
    {
        private readonly HttpClient httpClient;
        private List<PublicPlanMetadata> cachedPlans = new();
        private DateTime lastFetchTime = DateTime.MinValue;
        private const int CacheDurationMinutes = 5;

        public DiscoveryClient()
        {
            this.httpClient = new HttpClient();
            this.httpClient.BaseAddress = new Uri(NetworkManager.ApiBaseUrl);
        }

        public async Task<List<PublicPlanMetadata>> GetDiscoveryPlansAsync()
        {
            // Return cache if valid
            if (this.cachedPlans.Count > 0 && (DateTime.Now - this.lastFetchTime).TotalMinutes < CacheDurationMinutes)
            {
                return this.cachedPlans;
            }

            try
            {
                var response = await this.httpClient.GetStringAsync("/api/discovery");
                var plans = JsonSerializer.Deserialize<List<PublicPlanMetadata>>(response);

                if (plans != null)
                {
                    this.cachedPlans = plans;
                    this.lastFetchTime = DateTime.Now;
                    return plans;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to fetch discovery plans: {ex.Message}");
            }

            return new List<PublicPlanMetadata>();
        }

        public async Task<List<PublicPlanMetadata>> SearchPlansAsync(string tagPrefix)
        {
            try
            {
                var encodedTag = Uri.EscapeDataString(tagPrefix);
                var response = await this.httpClient.GetStringAsync($"/api/browse/filter?tag_prefix={encodedTag}");
                var plans = JsonSerializer.Deserialize<List<PublicPlanMetadata>>(response);
                return plans ?? new List<PublicPlanMetadata>();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to search plans: {ex.Message}");
                return new List<PublicPlanMetadata>();
            }
        }

        public void Dispose()
        {
            this.httpClient.Dispose();
        }
    }
}
