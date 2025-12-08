using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SixLabors.ImageSharp.Processing;

namespace AetherDraw.DrawingLogic
{
    public class DrawableLaser : BaseDrawable
    {
        private struct LaserPoint
        {
            public Vector2 Position;
            public DateTime Time;
        }

        private readonly List<LaserPoint> _points = new();
        public DateTime LastUpdateTime { get; private set; }

        private readonly Vector4 _coreColor = new Vector4(0.718f, 0.973f, 0.718f, 1.0f);
        private readonly Vector4 _glowColor = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);

        // Standard Constructor (Local User Drawing)
        public DrawableLaser(Vector2 startPoint, Vector4 color, float thickness)
        {
            ObjectDrawMode = DrawMode.Laser;
            Color = _coreColor;
            Thickness = thickness;
            IsFilled = false;
            AddPoint(startPoint);
        }

        // Deserialization Constructor (Network)
        public DrawableLaser(List<Vector2> points, Vector4 color, float thickness)
        {
            ObjectDrawMode = DrawMode.Laser;
            Color = color;
            Thickness = thickness;
            IsFilled = false;

            // [Match JS] Reconstruct timestamps so the tail fades correctly.
            // JS Logic: time: now - ((points.length - 1 - i) * 16)
            var now = DateTime.Now;
            for (int i = 0; i < points.Count; i++)
            {
                double offsetMs = (points.Count - 1 - i) * 16.0;
                _points.Add(new LaserPoint
                {
                    Position = points[i],
                    Time = now.AddMilliseconds(-offsetMs)
                });
            }
            LastUpdateTime = now;
        }

        public void AddPoint(Vector2 pos)
        {
            var now = DateTime.Now;
            if (_points.Count > 0 && Vector2.DistanceSquared(_points.Last().Position, pos) < 4.0f) return;
            _points.Add(new LaserPoint { Position = pos, Time = now });
            LastUpdateTime = now;
        }

        public override void Draw(ImDrawListPtr drawList, Vector2 canvasOriginScreen)
        {
            var now = DateTime.Now;
            // Prune points older than 1.2s (Matching JS logic)
            if (_points.Count > 0) _points.RemoveAll(p => (now - p.Time).TotalSeconds > 1.2);

            if (_points.Count < 2) return;

            uint colOuter, colInner, colCore;

            for (int i = 0; i < _points.Count - 1; i++)
            {
                var p1 = _points[i];
                var p2 = _points[i + 1];

                // Calculate fade: 1.0 at 0s, 0.0 at 1.2s
                float age = (float)(now - p1.Time).TotalSeconds;
                float baseAlpha = Math.Clamp(1.0f - (age / 1.2f), 0f, 1f);

                if (baseAlpha <= 0.01f) continue;

                // 1. OUTER GLOW (Wide, Low Opacity) - Simulates JS 'shadowBlur: 15'
                colOuter = ImGui.GetColorU32(new Vector4(_glowColor.X, _glowColor.Y, _glowColor.Z, baseAlpha * 0.4f));
                drawList.AddLine(canvasOriginScreen + p1.Position, canvasOriginScreen + p2.Position, colOuter, Thickness * 2.0f);

                // 2. INNER GLOW (Medium, Medium Opacity)
                colInner = ImGui.GetColorU32(new Vector4(_glowColor.X, _glowColor.Y, _glowColor.Z, baseAlpha * 0.6f));
                drawList.AddLine(canvasOriginScreen + p1.Position, canvasOriginScreen + p2.Position, colInner, Thickness * 1.0f);

                // 3. CORE (Pale Green, Solid) - Simulates JS 'stroke: #b7f8b7'
                colCore = ImGui.GetColorU32(new Vector4(_coreColor.X, _coreColor.Y, _coreColor.Z, baseAlpha));
                drawList.AddLine(canvasOriginScreen + p1.Position, canvasOriginScreen + p2.Position, colCore, Thickness);
            }
        }

        public List<Vector2> GetPoints() => _points.Select(p => p.Position).ToList();

        // Non-interactive overrides for the transient laser
        public override RectangleF GetBoundingBox() => RectangleF.Empty;
        public override bool IsHit(Vector2 p, float t) => false;
        public override BaseDrawable Clone() => new DrawableLaser(new List<Vector2>(), Color, Thickness);
        public override void Translate(Vector2 delta) { }
        public override void DrawToImage(IImageProcessingContext context, Vector2 origin, float scale) { }
    }
}
