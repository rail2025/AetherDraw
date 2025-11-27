using AetherDraw.Networking;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AetherDraw.Core
{
    public class AccountManager : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Random _random = new();

        // --- Word Lists (Parity with ui.js) ---
        private static readonly string[] OpinionVerbs = { "I like", "I hate", "I want", "I need", "Craving", "Seeking", "Avoiding", "Serving", "Finding", "Cooking", "Tasting", "I found", "I lost", "I traded", "He stole", "She sold", "They want", "Remembering", "Forgetting", "Questioning", "Analyzing", "Ignoring", "Praising", "Chasing", "Selling" };
        private static readonly string[] Adjectives = { "spicy", "creamy", "sultry", "glimmering", "ancient", "crispy", "zesty", "hearty", "fluffy", "savory", "frozen", "bubbling", "forbidden", "radiant", "somber", "dented", "gilded", "rusted", "glowing", "cracked", "smelly", "aromatic", "stale", "fresh", "bitter", "sweet", "silken", "spiky" };
        private static readonly string[] FfxivNouns = { "Miqote", "Lalafell", "Gridanian", "Ul'dahn", "Limsan", "Ishgardian", "Doman", "Hrothgar", "Viera", "Garlean", "Sharlayan", "Sylph", "Au Ra", "Roegadyn", "Elezen", "Thavnairian", "Coerthan", "Ala Mhigan", "Ronkan", "Eorzean", "Astrologian", "Machinist", "Samurai", "Dancer", "Paladin", "Warrior" };
        private static readonly string[] FoodItems = { "rolanberry pie", "LaNoscean toast", "dodo omelette", "pixieberry tea", "king salmon", "knightly bread", "stone soup", "archon burgers", "bubble chocolate", "tuna miq", "syrcus tower", "dalamud shard", "aetheryte shard", "allagan tomestone", "company seal", "gil-turtle", "cactuar needle", "malboro breath", "behemoth horn", "mandragora root", "black truffle", "popoto", "ruby tomato", "apkallu egg", "thavnairian onion" };
        private static readonly string[] ActionPhrases = { "in my inventory", "on the marketboard", "from a retainer", "for the Grand Company", "in a treasure chest", "from a guildhest", "at the Gold Saucer", "near the aetheryte", "without permission", "for a friend", "under the table", "with great haste", "against all odds", "for my free company", "in the goblet" };

        private List<string> _masterWordList = new();

        public AccountManager()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(NetworkManager.ApiBaseUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };

            InitializeWordList();
        }

        private void InitializeWordList()
        {
            _masterWordList = new List<string>();

            // Logic copied exactly from ui.js generateAccountKey()
            _masterWordList.AddRange(OpinionVerbs.Select(s => s.Split(' ')[0])); // "I like" -> "I"
            _masterWordList.AddRange(Adjectives);
            _masterWordList.AddRange(FfxivNouns);
            _masterWordList.AddRange(FoodItems.Select(s => s.Split(' ').Last())); // "rolanberry pie" -> "pie"
            _masterWordList.AddRange(ActionPhrases.Select(s => s.Split(' ').Last())); // "in my inventory" -> "inventory"

            // Filter out any potential empty strings
            _masterWordList = _masterWordList.Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        public string GenerateNewKey()
        {
            if (_masterWordList == null || _masterWordList.Count == 0) return string.Empty;

            var keyWords = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                keyWords.Add(_masterWordList[_random.Next(_masterWordList.Count)]);
            }

            return string.Join("-", keyWords);
        }

        public bool ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            // Basic validation: 5 words separated by hyphens
            var parts = key.Split('-');
            if (parts.Length != 5) return false;

            // Ensure only letters (and maybe spaces if legacy keys exist, but generation uses hyphens)
            // ui.js regex: /[^a-z0-9 ]/g but replaces spaces with dashes.
            // We'll just check for reasonable length and content.
            return !key.Any(c => !char.IsLetterOrDigit(c) && c != '-');
        }

        public async Task<List<MyPlan>> GetMyPlansAsync(string accountKey)
        {
            if (string.IsNullOrEmpty(accountKey)) return new List<MyPlan>();

            try
            {
                var requestData = new { accountKey = accountKey };
                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/account/get_plans", content);

                if (!response.IsSuccessStatusCode)
                {
                    Plugin.Log.Error($"Failed to fetch plans. Status: {response.StatusCode}");
                    return new List<MyPlan>();
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var plans = JsonSerializer.Deserialize<List<MyPlan>>(responseString);

                return plans ?? new List<MyPlan>();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error fetching my plans: {ex.Message}");
                return new List<MyPlan>();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class MyPlan
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("plan_name")]
        public string PlanName { get; set; } = string.Empty;
    }
}
