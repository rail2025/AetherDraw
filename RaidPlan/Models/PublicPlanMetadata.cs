using System.Text.Json.Serialization;

namespace AetherDraw.RaidPlan.Models
{
    public class PublicPlanMetadata
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("boss_tag")]
        public string? BossTag { get; set; }

        [JsonPropertyName("plan_name")]
        public string PlanName { get; set; } = string.Empty;

        [JsonPropertyName("views")]
        public int Views { get; set; }

        [JsonPropertyName("pinned_rank")]
        public int PinnedRank { get; set; }

        [JsonPropertyName("plan_owner")]
        public string? PlanOwner { get; set; }
    }
}
