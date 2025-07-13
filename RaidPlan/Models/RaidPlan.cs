using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace AetherDraw.RaidPlan.Models
{
    // Root object for the raid plan
    public class RaidPlan
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("nodes")]
        public List<Node>? Nodes { get; set; }

        [JsonPropertyName("steps")]
        public int Steps { get; set; }
    }

    // Represents a single drawable element on the canvas
    public class Node
    {
        [JsonPropertyName("attr")]
        public Attr? Attr { get; set; }

        [JsonPropertyName("meta")]
        public Meta? Meta { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    // Attributes of the node (e.g., color, size, text)
    public class Attr
    {
        [JsonPropertyName("fill")]
        public string? Fill { get; set; }

        [JsonPropertyName("colorA")]
        public string? colorA { get; set; }

        [JsonPropertyName("imageUrl")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("asset")]
        public string? Asset { get; set; }

        [JsonPropertyName("emoji")]
        public string? Emoji { get; set; }

        [JsonPropertyName("wayId")]
        public string? WayId { get; set; }

        [JsonPropertyName("abilityId")]
        public string? abilityId { get; set; }
    }

    // Metadata for the node (e.g., position, rotation, scale)
    public class Meta
    {
        [JsonPropertyName("pos")]
        public Pos? Pos { get; set; }

        [JsonPropertyName("size")]
        public Size? Size { get; set; }

        [JsonPropertyName("angle")]
        public float Angle { get; set; }

        [JsonPropertyName("scale")]
        public Scale? Scale { get; set; }

        [JsonPropertyName("step")]
        public int Step { get; set; }
    }

    public class Pos
    {
        [JsonPropertyName("x")]
        public float? X { get; set; }

        [JsonPropertyName("y")]
        public float? Y { get; set; }
    }

    public class Size
    {
        [JsonPropertyName("h")]
        public float? H { get; set; }

        [JsonPropertyName("w")]
        public float? W { get; set; }
    }

    public class Scale
    {
        [JsonPropertyName("x")]
        public float? X { get; set; }

        [JsonPropertyName("y")]
        public float? Y { get; set; }
    }
}
