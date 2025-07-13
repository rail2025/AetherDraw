using AetherDraw.Core;
using AetherDraw.DrawingLogic;
using AetherDraw.RaidPlan.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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
            { "ff-donut", "PluginImages.svg.donut.svg" },
            { "ff-aoe", "PluginImages.svg.prox_aoe.svg" },
            { "ff-area-prox", "PluginImages.svg.prox_aoe.svg" },
            { "ff-knock", "PluginImages.svg.spread.svg" },
            { "a", "PluginImages.toolbar.A.png" },
            { "b", "PluginImages.toolbar.B.png" },
            { "c", "PluginImages.toolbar.C.png" },
            { "d", "PluginImages.toolbar.D.png" },
            { "1", "PluginImages.toolbar.1_waymark.png" },
            { "2", "PluginImages.toolbar.2_waymark.png" },
            { "3", "PluginImages.toolbar.3_waymark.png" },
            { "4", "PluginImages.toolbar.4_waymark.png" }
        };

        public List<PageData> Translate(Models.RaidPlan raidPlan)
        {
            var pages = new List<PageData>();
            if (raidPlan?.Nodes == null || raidPlan.Steps == 0) return pages;

            try
            {
                var sourceSize = new Vector2(1200, 675);
                var targetSize = new Vector2(800, 600);

                if (sourceSize.X == 0 || sourceSize.Y == 0) return pages;

                float baseScale = Math.Min(targetSize.X / sourceSize.X, targetSize.Y / sourceSize.Y);
                var offset = (targetSize - (sourceSize * baseScale)) / 2;

                var arenaNode = raidPlan.Nodes.FirstOrDefault(n => n.Type == "arena");
                BaseDrawable? backgroundDrawable = null;
                if (arenaNode != null)
                {
                    backgroundDrawable = ToAetherDrawDrawable(arenaNode, baseScale, offset, targetSize);
                }

                // --- OPTIMIZATION: Group nodes by step once for efficient lookup ---
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

                    // --- OPTIMIZATION: Directly access the pre-grouped nodes for the current step ---
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
                return new List<PageData>(); // Return an empty list on failure
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
            var color = HexToVector4(node.Attr?.Fill ?? node.Attr?.colorA ?? "#FFFFFF");

            if (!IsValid(pos) || !IsValid(size) || !IsValid(rotation))
            {
                AetherDraw.Plugin.Log?.Warning($"Skipping invalid drawable node due to NaN/Infinity values. Type: {node.Type}");
                return null;
            }

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
                    var startPoint = pos - new Vector2(0, size.Y / 2);
                    var endPoint = pos + new Vector2(0, size.Y / 2);
                    return new DrawableArrow(startPoint, color, 4f * scale) { EndPointRelative = endPoint, RotationAngle = rotation };

                case "triangle":
                    return CreateTriangleFromNode(pos, size, rotation, color);

                case "marker":
                case "waypoint":
                case "ability":
                case "emoji":
                    if (node.Attr == null) return null;

                    if (!string.IsNullOrEmpty(node.Attr.abilityId))
                    {
                        switch (node.Attr.abilityId)
                        {
                            case "ff-circle":
                                return new DrawableCircle(pos, color, 1f, true) { Radius = size.X / 2 };
                            case "ff-square":
                                return new DrawableRectangle(pos - size / 2, color, 1f, true) { EndPointRelative = pos + size / 2, RotationAngle = rotation };
                            case "ff-wedge":
                                return CreateTriangleFromNode(pos, size, rotation, color);
                            case "ff-ring":
                                return new DrawableCircle(pos, color, 2f * scale, false) { Radius = size.X / 2 };
                        }
                    }

                    string imagePath = "";
                    if (!string.IsNullOrEmpty(node.Attr.abilityId) && RaidPlanAssetMap.TryGetValue(node.Attr.abilityId, out var localAbilityPath))
                    {
                        imagePath = localAbilityPath;
                    }
                    else if (!string.IsNullOrEmpty(node.Attr.WayId) && RaidPlanAssetMap.TryGetValue(node.Attr.WayId, out var localWaymarkPath))
                    {
                        imagePath = localWaymarkPath;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(node.Attr.Asset))
                            imagePath = RaidPlanAssetBaseUrl + node.Attr.Asset;
                        else if (!string.IsNullOrEmpty(node.Attr.abilityId))
                            imagePath = $"{RaidPlanAssetBaseUrl}ability/{node.Attr.abilityId}.png";
                        else if (!string.IsNullOrEmpty(node.Attr.WayId))
                            imagePath = $"{RaidPlanAssetBaseUrl}waypoint/{node.Attr.WayId}.png";
                    }

                    if (!string.IsNullOrEmpty(imagePath))
                        return new DrawableImage(DrawMode.Image, imagePath, pos, size, color, rotation);

                    return null;

                case "itext":
                    return new DrawableText(pos, node.Attr?.Text ?? "", color, 20f * scale, size.X);

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

        private Vector4 HexToVector4(string hex)
        {
            var cleaned = hex.StartsWith("#") ? hex.Substring(1) : hex;
            if (cleaned.Length != 6) return Vector4.One;
            try
            {
                return new Vector4(
                    int.Parse(cleaned.Substring(0, 2), NumberStyles.HexNumber) / 255f,
                    int.Parse(cleaned.Substring(2, 2), NumberStyles.HexNumber) / 255f,
                    int.Parse(cleaned.Substring(4, 2), NumberStyles.HexNumber) / 255f,
                    1f);
            }
            catch { return Vector4.One; }
        }
    }
}
