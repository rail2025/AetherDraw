using System;
using System.Numerics;

namespace AetherDraw.DrawingLogic
{
    public static class HitDetection
    {
        // Original HitDetection methods from MainWindow.cs
        public static float DistancePointToLineSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            float l2 = Vector2.DistanceSquared(a, b);
            if (l2 == 0.0f) return Vector2.Distance(p, a);
            float t = Math.Max(0, Math.Min(1, Vector2.Dot(p - a, b - a) / l2));
            Vector2 proj = a + t * (b - a);
            return Vector2.Distance(p, proj);
        }

        public static bool IntersectCircleAABB(Vector2 cC, float cR, Vector2 rMin, Vector2 rMax)
        {
            if (cR <= 0) return false;
            float cX = Math.Max(rMin.X, Math.Min(cC.X, rMax.X));
            float cY = Math.Max(rMin.Y, Math.Min(cC.Y, rMax.Y));
            float dX = cC.X - cX;
            float dY = cC.Y - cY;
            return (dX * dX + dY * dY) < (cR * cR);
        }

        public static bool IntersectCircleCircle(Vector2 c1, float r1, Vector2 c2, float r2)
        {
            if (r1 < 0 || r2 < 0) return false;
            return Vector2.DistanceSquared(c1, c2) < (r1 + r2) * (r1 + r2);
        }

        public static bool PointInTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            float d1 = (pt.X - v2.X) * (v1.Y - v2.Y) - (v1.X - v2.X) * (pt.Y - v2.Y);
            float d2 = (pt.X - v3.X) * (v2.Y - v3.Y) - (v2.X - v3.X) * (pt.Y - v3.Y);
            float d3 = (pt.X - v1.X) * (v3.Y - v1.Y) - (v3.X - v1.X) * (pt.Y - v1.Y);
            bool hn = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hp = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hn && hp);
        }

        public static bool IntersectCircleTriangle(Vector2 cC, float cR, Vector2 t1, Vector2 t2, Vector2 t3)
        {
            if (cR <= 0) return false;
            if (PointInTriangle(cC, t1, t2, t3)) return true;
            if (DistancePointToLineSegment(cC, t1, t2) < cR) return true;
            if (DistancePointToLineSegment(cC, t2, t3) < cR) return true;
            if (DistancePointToLineSegment(cC, t3, t1) < cR) return true;
            if (Vector2.DistanceSquared(cC, t1) < cR * cR) return true;
            if (Vector2.DistanceSquared(cC, t2) < cR * cR) return true;
            if (Vector2.DistanceSquared(cC, t3) < cR * cR) return true;
            return false;
        }
        public static bool IsPointInCircularSector(Vector2 pt, Vector2 center, float radius, float startAngleRad, float sweepAngleRad)
        {
            if (radius <= 0) return false;
            // 1. Check distance
            if (Vector2.DistanceSquared(pt, center) > radius * radius) return false;

            // 2. Check angle
            Vector2 diff = pt - center;
            float angle = MathF.Atan2(diff.Y, diff.X);

            // Normalize angle difference to [0, 2PI)
            float angleRelativeToStart = angle - startAngleRad;
            const float TwoPI = MathF.PI * 2f;

            angleRelativeToStart %= TwoPI;
            if (angleRelativeToStart < 0) angleRelativeToStart += TwoPI;

            return angleRelativeToStart <= sweepAngleRad;
        }

        // Methods moved from MainWindow.cs (made public)
        public static Vector2 ImRotate(Vector2 v, float cosA, float sinA)
        {
            return new Vector2(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA);
        }

        public static Vector2[] GetRotatedQuadVertices(Vector2 center, Vector2 halfSize, float angleInRadians)
        {
            float cosA = MathF.Cos(angleInRadians);
            float sinA = MathF.Sin(angleInRadians);

            // Define corners relative to a (0,0) center, then rotate and add the actual center
            Vector2[] corners = new Vector2[] {
                center + ImRotate(new Vector2(-halfSize.X, -halfSize.Y), cosA, sinA), // Top-Left
                center + ImRotate(new Vector2( halfSize.X, -halfSize.Y), cosA, sinA), // Top-Right
                center + ImRotate(new Vector2( halfSize.X,  halfSize.Y), cosA, sinA), // Bottom-Right
                center + ImRotate(new Vector2(-halfSize.X,  halfSize.Y), cosA, sinA)  // Bottom-Left
            };
            return corners;
        }

        // SnapToGrid helper method
        public static Vector2 SnapToGrid(Vector2 point, float gridSize)
        {
            if (gridSize <= 0) return point;
            return new Vector2(
                MathF.Round(point.X / gridSize) * gridSize,
                MathF.Round(point.Y / gridSize) * gridSize
            );
        }

        public static bool IsPointInRotatedRect(Vector2 pt, Vector2 center, float halfLength, float halfWidth, float rotationRad, bool isFilled, float thickness, float threshold)
        {
            // Transform point to local coordinates
            Vector2 localP = Vector2.Transform(pt - center, Matrix3x2.CreateRotation(-rotationRad));

            if (isFilled)
            {
                return Math.Abs(localP.X) <= halfLength + threshold &&
                       Math.Abs(localP.Y) <= halfWidth + threshold;
            }
            else
            {
                float effDist = threshold + thickness / 2f;
                bool inOuter = Math.Abs(localP.X) <= halfLength + effDist && Math.Abs(localP.Y) <= halfWidth + effDist;
                bool outInner = Math.Abs(localP.X) >= halfLength - effDist || Math.Abs(localP.Y) >= halfWidth - effDist;
                return inOuter && outInner;
            }
        }

        public static bool IsPointInStarburst(Vector2 pt, Vector2 center, float radius, float width, float baseRotationRad, bool isFilled, float thickness, float threshold)
        {
            // Starburst is 4 bars at 0, 45, 90, 135 degrees
            float[] angles = { 0f, MathF.PI / 4f, MathF.PI / 2f, 3f * MathF.PI / 4f };
            float halfLength = radius;
            float halfWidth = width / 2f;

            foreach (float angleOffset in angles)
            {
                float currentAngle = baseRotationRad + angleOffset;
                if (IsPointInRotatedRect(pt, center, halfLength, halfWidth, currentAngle, isFilled, thickness, threshold))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
