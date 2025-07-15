using AetherDraw.Core;
using AetherDraw.DrawingLogic;
using AetherDraw.RaidPlan.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace AetherDraw.RaidPlan.Services
{
    internal class RaidPlanTranslator
    {
        private const string RaidPlanAssetBaseUrl = "https://cf.raidplan.io/";

        private static readonly Dictionary<string, string> RaidPlanAssetMap = new()
        {
            { "ff-boss", "PluginImages.svg.boss.svg" },
            { "ff-stack", "PluginImages.svg.stack.svg" },
            { "ff-spread", "PluginImages.svg.spread.svg" },
            { "ff-linestack", "PluginImages.svg.line_stack.svg" },
            { "ff-flare", "PluginImages.svg.flare.svg" },
            { "ff-player-prox", "PluginImages.svg.flare.svg" },
            { "ff-donut", "PluginImages.svg.donut.svg" },
            { "ff-aoe", "PluginImages.svg.prox_aoe.svg" },
            { "ff-area-prox", "PluginImages.svg.prox_aoe.svg" },
            { "ff-knock", "PluginImages.svg.spread.svg" },
            { "ff-stackline", "PluginImages.svg.line_stack.svg" },
            { "a", "PluginImages.toolbar.A.png" },
            { "b", "PluginImages.toolbar.B.png" },
            { "c", "PluginImages.toolbar.C.png" },
            { "d", "PluginImages.toolbar.D.png" },
            { "1", "PluginImages.toolbar.1_waymark.png" },
            { "2", "PluginImages.toolbar.2_waymark.png" },
            { "3", "PluginImages.toolbar.3_waymark.png" },
            { "4", "PluginImages.toolbar.4_waymark.png" }
        };

        private static readonly Dictionary<string, DrawMode> AssetIdToDrawModeMap = new()
        {
            { "ff-boss", DrawMode.BossImage },
            { "ff-stack", DrawMode.StackImage },
            { "ff-spread", DrawMode.SpreadImage },
            { "ff-linestack", DrawMode.LineStackImage },
            { "ff-stackline", DrawMode.LineStackImage },
            { "ff-flare", DrawMode.FlareImage },
            { "ff-player-prox", DrawMode.FlareImage },
            { "ff-donut", DrawMode.DonutAoEImage },
            { "ff-aoe", DrawMode.CircleAoEImage },
            { "ff-area-prox", DrawMode.CircleAoEImage },
            { "ff-knock", DrawMode.SpreadImage },
            { "a", DrawMode.WaymarkAImage },
            { "b", DrawMode.WaymarkBImage },
            { "c", DrawMode.WaymarkCImage },
            { "d", DrawMode.WaymarkDImage },
            { "1", DrawMode.Waymark1Image },
            { "2", DrawMode.Waymark2Image },
            { "3", DrawMode.Waymark3Image },
            { "4", DrawMode.Waymark4Image },
            { "role_tank.png", DrawMode.RoleTankImage },
            { "role_healer.png", DrawMode.RoleHealerImage },
            { "role_melee.png", DrawMode.RoleMeleeImage },
            { "role_ranged.png", DrawMode.RoleRangedImage },
            { "Tank.JPG", DrawMode.RoleTankImage },
            { "Healer.JPG", DrawMode.RoleHealerImage },
            { "Melee.JPG", DrawMode.RoleMeleeImage },
            { "Ranged.JPG", DrawMode.RoleRangedImage },
            { "BossIconPlaceholder.svg", DrawMode.BossIconPlaceholder }
        };


        public List<PageData> Translate(Models.RaidPlan raidPlan, string? fallbackBackgroundImageUrl = null)
        {
            var pages = new List<PageData>();
            if (raidPlan?.Nodes == null || raidPlan.Steps == 0) return pages;

            try
            {
                // 1. Correctly resolve the background image URL.
                var arenaNode = raidPlan.Nodes.FirstOrDefault(n => n.Type == "arena");
                string? finalBackgroundImageUrl = null;
                if (arenaNode?.Attr != null && !string.IsNullOrEmpty(arenaNode.Attr.ImageUrl))
                {
                    finalBackgroundImageUrl = arenaNode.Attr.ImageUrl;
                }
                else if (!string.IsNullOrEmpty(raidPlan.Raid) && !string.IsNullOrEmpty(raidPlan.Boss))
                {
                    string mapFileName = !string.IsNullOrEmpty(raidPlan.MapType) ? $"{raidPlan.Boss}-{raidPlan.MapType}" : raidPlan.Boss;
                    finalBackgroundImageUrl = $"{RaidPlanAssetBaseUrl}raid/{raidPlan.Raid}/map/{mapFileName}.jpg";
                }
                else if (!string.IsNullOrEmpty(fallbackBackgroundImageUrl))
                {
                    finalBackgroundImageUrl = fallbackBackgroundImageUrl;
                }

                // 2. Use 16:9 coordinate system for ALL nodes.
                var sourceSize = new Vector2(1200, 675);
                var targetSize = new Vector2(800, 600);
                if (sourceSize.X == 0 || sourceSize.Y == 0) return pages;

                // 3. Calculate the single, consistent scale and offset for the entire plan.
                float baseScale = Math.Min(targetSize.X / sourceSize.X, targetSize.Y / sourceSize.Y);
                var offset = (targetSize - (sourceSize * baseScale)) / 2;

                BaseDrawable? backgroundDrawable = null;
                if (!string.IsNullOrEmpty(finalBackgroundImageUrl))
                {
                    Vector2 backgroundDrawSize;
                    // The size of the entire 16:9 canvas after being scaled down.
                    var scaledSourceCanvasSize = sourceSize * baseScale;

                    if (finalBackgroundImageUrl.Contains("imgur.com"))
                    {
                        // For Imgur, create a square background. Its side length is based on the
                        // HEIGHT of the source canvas, scaled consistently with all other nodes.
                        backgroundDrawSize = new Vector2(675, 675) * baseScale;
                    }
                    else
                    {
                        // For standard 16:9 backgrounds, the drawable's size is the full scaled canvas.
                        backgroundDrawSize = scaledSourceCanvasSize;
                    }

                    // The position is ALWAYS the center of the scaled 16:9 area.
                    var backgroundDrawPosition = offset + (scaledSourceCanvasSize / 2);

                    backgroundDrawable = new DrawableImage(DrawMode.Image, finalBackgroundImageUrl, backgroundDrawPosition, backgroundDrawSize, Vector4.One, 0);
                }

                var nodesByStep = raidPlan.Nodes
                    .Where(n => n.Meta != null && n.Type != "arena")
                    .GroupBy(n => n.Meta!.Step)
                    .ToDictionary(g => g.Key, g => g.ToList());

                for (int i = 0; i < raidPlan.Steps; i++)
                {
                    var page = new PageData { Name = (i + 1).ToString(), Drawables = new List<BaseDrawable>() };
                    if (backgroundDrawable != null)
                    {
                        page.Drawables.Add(backgroundDrawable.Clone());
                    }
                    if (nodesByStep.TryGetValue(i, out var nodesForStep))
                    {
                        foreach (var node in nodesForStep)
                        {
                            if (ToAetherDrawDrawable(node, baseScale, offset, targetSize) is { } drawable)
                                page.Drawables.Add(drawable);
                        }
                    }
                    pages.Add(page);
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, "An unexpected error occurred during RaidPlan translation.");
                return new List<PageData>();
            }
            return pages;
        }



        private BaseDrawable? ToAetherDrawDrawable(Node node, float scale, Vector2 offset, Vector2 targetCanvasSize)
        {
            if (node.Type == null || node.Meta?.Pos?.X == null || node.Meta.Pos.Y == null)
                return null;

            var pos = new Vector2(node.Meta.Pos.X.Value * scale, node.Meta.Pos.Y.Value * scale) + offset;
            var size = new Vector2((node.Meta.Size?.W ?? 0f) * (node.Meta.Scale?.X ?? 1f) * scale, (node.Meta.Size?.H ?? 0f) * (node.Meta.Scale?.Y ?? 1f) * scale);
            float rotation = node.Meta.Angle * (float)(Math.PI / 180.0);
            var color = HexToVector4(node.Attr?.Fill ?? node.Attr?.colorA ?? "#FFFFFF", node.Attr?.Opacity ?? 1.0f);

            if (!IsValid(pos) || !IsValid(size) || !IsValid(rotation)) return null;

            switch (node.Type)
            {
                case "arena":
                    if (node.Attr != null && !string.IsNullOrEmpty(node.Attr.ImageUrl))
                        return new DrawableImage(DrawMode.Image, node.Attr.ImageUrl, targetCanvasSize / 2, targetCanvasSize, Vector4.One, 0);
                    return null;

                case "rect":
                    return new DrawableRectangle(pos - size / 2, color, 1f, true) { EndPointRelative = pos + size / 2, RotationAngle = rotation };
                case "circle":
                    return new DrawableCircle(pos, color, 1f, true) { Radius = size.X / 2 };
                case "arrow":
                    var transform = Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateTranslation(pos);
                    var startPoint = Vector2.Transform(new Vector2(0, size.Y / 2), transform);
                    var endPoint = Vector2.Transform(new Vector2(0, -size.Y / 2), transform);
                    return new DrawableArrow(startPoint, color, 4f * scale) { EndPointRelative = endPoint };
                case "triangle":
                    return CreateTriangleFromNode(pos, size, rotation, color);

                case "marker":
                case "waypoint":
                case "ability":
                case "emoji":
                    if (node.Attr == null) return null;
                    // Just add more emojis here every tier for stupid raidplan users that want to be cute
                    var replacementEmojis = new HashSet<string> { "🔫", "😈", "🍀", "🪁", "🏹", "🗡️", "🐲", "🐏", "🐱", "🐿️" };
                    var textContent = node.Attr.Text?.ToLowerInvariant() ?? "";
                    bool needsReplacement = replacementEmojis.Contains(node.Attr.Emoji ?? "") || textContent.Contains("gun") || textContent.Contains("animal");
                    if (needsReplacement)
                    {
                        var orangeTint = new Vector4(1.0f, 0.65f, 0.0f, 1.0f);
                        var bossIconPath = "PluginImages.svg.boss.svg";
                        var defaultIconSize = new Vector2(30f * scale, 30f * scale);
                        return new DrawableImage(DrawMode.BossIconPlaceholder, bossIconPath, pos, defaultIconSize, orangeTint, rotation);
                    }

                    if (!string.IsNullOrEmpty(node.Attr.abilityId))
                    {
                        switch (node.Attr.abilityId)
                        {
                            case "ff-circle": return new DrawableCircle(pos, color, 1f, true) { Radius = size.X / 2 };
                            case "ff-square": return new DrawableRectangle(pos - size / 2, color, 1f, true) { EndPointRelative = pos + size / 2, RotationAngle = rotation };
                            case "ff-wedge": return CreateTriangleFromNode(pos, size, rotation, color);
                            case "ff-ring": return new DrawableCircle(pos, color, 2f * scale, false) { Radius = size.X / 2 };
                            case "ff-pie":
                                var pieWidth = size.X * 0.75f;
                                var pieOffset = Vector2.Transform(new Vector2(size.X * 0.125f, 0), Matrix3x2.CreateRotation(rotation));
                                return new DrawableRectangle((pos + pieOffset) - new Vector2(pieWidth, size.Y) / 2, color, 1f, true) { EndPointRelative = (pos + pieOffset) + new Vector2(pieWidth, size.Y) / 2, RotationAngle = rotation };
                            case "ff-half":
                                var halfWidth = size.X / 2;
                                var halfOffset = Vector2.Transform(new Vector2(-size.X / 4, 0), Matrix3x2.CreateRotation(rotation));
                                return new DrawableRectangle((pos + halfOffset) - new Vector2(halfWidth, size.Y) / 2, color, 1f, true) { EndPointRelative = (pos + halfOffset) + new Vector2(halfWidth, size.Y) / 2, RotationAngle = rotation };
                        }
                    }

                    string imagePath = "";
                    DrawMode drawMode = DrawMode.Image;
                    string assetId = node.Attr.abilityId ?? node.Attr.WayId ?? string.Empty;

                    if (!string.IsNullOrEmpty(node.Attr.Asset))
                    {
                        var fileName = Path.GetFileName(node.Attr.Asset);
                        if (AssetIdToDrawModeMap.TryGetValue(fileName, out var specificMode))
                        {
                            drawMode = specificMode;
                        }
                    }
                    else if (!string.IsNullOrEmpty(assetId) && AssetIdToDrawModeMap.TryGetValue(assetId, out var specificMode))
                    {
                        drawMode = specificMode;
                    }

                    if (!string.IsNullOrEmpty(node.Attr.abilityId) && RaidPlanAssetMap.TryGetValue(node.Attr.abilityId, out var localAbilityPath))
                        imagePath = localAbilityPath;
                    else if (!string.IsNullOrEmpty(node.Attr.WayId) && RaidPlanAssetMap.TryGetValue(node.Attr.WayId, out var localWaymarkPath))
                        imagePath = localWaymarkPath;
                    else
                    {
                        if (!string.IsNullOrEmpty(node.Attr.Asset)) imagePath = RaidPlanAssetBaseUrl + node.Attr.Asset;
                        else if (!string.IsNullOrEmpty(node.Attr.abilityId)) imagePath = $"{RaidPlanAssetBaseUrl}ability/{node.Attr.abilityId}.png";
                        else if (!string.IsNullOrEmpty(node.Attr.WayId)) imagePath = $"{RaidPlanAssetBaseUrl}waypoint/{node.Attr.WayId}.png";
                    }

                    if (!string.IsNullOrEmpty(imagePath))
                        return new DrawableImage(drawMode, imagePath, pos, size, color, rotation);
                    return null;

                case "itext":
                    const float largeTextSize = 24f;
                    const float mediumTextSize = 20f;
                    const float smallTextSize = 16f;

                    float finalUnscaledSize = mediumTextSize;
                    if (node.Attr?.FontSize == 4) finalUnscaledSize = largeTextSize;
                    else if (node.Attr?.FontSize == 2) finalUnscaledSize = smallTextSize;

                    return new DrawableText(pos, node.Attr?.Text ?? "", color, finalUnscaledSize * scale, size.X);

                default:
                    return null;
            }
        }

        private bool IsValid(Vector2 vec) => !float.IsNaN(vec.X) && !float.IsNaN(vec.Y) && !float.IsInfinity(vec.X) && !float.IsInfinity(vec.Y);
        private bool IsValid(float val) => !float.IsNaN(val) && !float.IsInfinity(val);

        private DrawableTriangle CreateTriangleFromNode(Vector2 center, Vector2 size, float rotation, Vector4 color)
        {
            var halfSize = size / 2;
            var vertices = new[] { new Vector2(0, -halfSize.Y), new Vector2(-halfSize.X, halfSize.Y), new Vector2(halfSize.X, halfSize.Y) };
            var transform = Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateTranslation(center);
            return new DrawableTriangle(Vector2.Transform(vertices[0], transform), Vector2.Transform(vertices[1], transform), Vector2.Transform(vertices[2], transform), color);
        }

        private Vector4 HexToVector4(string hex, float alpha)
        {
            var cleaned = hex.StartsWith("#") ? hex.Substring(1) : hex;
            if (cleaned.Length != 6) return new Vector4(1, 1, 1, alpha);
            try
            {
                return new Vector4(
                    int.Parse(cleaned.Substring(0, 2), NumberStyles.HexNumber) / 255f,
                    int.Parse(cleaned.Substring(2, 2), NumberStyles.HexNumber) / 255f,
                    int.Parse(cleaned.Substring(4, 2), NumberStyles.HexNumber) / 255f,
                    alpha);
            }
            catch { return new Vector4(1, 1, 1, alpha); }
        }
    }
}
