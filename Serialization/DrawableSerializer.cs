// AetherDraw/Serialization/DrawableSerializer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AetherDraw.DrawingLogic;

namespace AetherDraw.Serialization
{
    /// <summary>
    /// Handles binary serialization and deserialization of AetherDraw drawable objects.
    /// </summary>
    public static class DrawableSerializer
    {
        // Versioning for the serialization format. Increment if the format changes.
        private const int SERIALIZATION_VERSION = 3;

        /// <summary>
        /// A reasonable upper limit for the number of drawable objects on a single page to prevent malicious data from crashing the client.
        /// </summary>
        private const int MAX_DRAWABLES_PER_PAGE = 10000;

        /// <summary>
        /// A reasonable upper limit for the number of points in a single path or dash object.
        /// </summary>
        private const int MAX_POINTS_PER_OBJECT = 50000;


        // --- Public API ---

        /// <summary>
        /// Serializes a list of drawable objects from a page into a byte array.
        /// </summary>
        /// <param name="drawables">The list of BaseDrawable objects to serialize.</param>
        /// <returns>A byte array representing the serialized drawables.</returns>
        public static byte[] SerializePageToBytes(List<BaseDrawable> drawables)
        {
            if (drawables == null) throw new ArgumentNullException(nameof(drawables));

            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                // Write a version number for the serialization format
                writer.Write(SERIALIZATION_VERSION);

                // Write the number of drawables in the list
                writer.Write(drawables.Count);

                // Serialize each drawable object
                foreach (var drawable in drawables)
                {
                    SerializeSingleDrawable(writer, drawable);
                }
                return memoryStream.ToArray();
            }
        }

        /// <summary>
        /// Deserializes a byte array back into a list of drawable objects for a page.
        /// </summary>
        /// <param name="data">The byte array containing serialized drawable data.</param>
        /// <returns>A list of deserialized BaseDrawable objects.</returns>
        public static List<BaseDrawable> DeserializePageFromBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return new List<BaseDrawable>();

            var deserializedDrawables = new List<BaseDrawable>();
            using (var memoryStream = new MemoryStream(data))
            using (var reader = new BinaryReader(memoryStream))
            {
                try
                {
                    if (reader.BaseStream.Position + sizeof(int) > reader.BaseStream.Length) return deserializedDrawables;
                    int version = reader.ReadInt32();
                    if (version > SERIALIZATION_VERSION)
                    {
                        AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Deserialization version mismatch. Expected {SERIALIZATION_VERSION}, got {version}.");
                        return deserializedDrawables;
                    }

                    if (reader.BaseStream.Position + sizeof(int) > reader.BaseStream.Length) return deserializedDrawables;
                    int drawableCount = reader.ReadInt32();

                    // SANITY CHECK: Add a reasonable limit to how many objects can be on a single page.
                    if (drawableCount < 0 || drawableCount > MAX_DRAWABLES_PER_PAGE)
                    {
                        AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Invalid drawable count in data: {drawableCount}. Halting deserialization for this page.");
                        return deserializedDrawables; // Return empty list instead of crashing
                    }

                    for (int i = 0; i < drawableCount; i++)
                    {
                        BaseDrawable? drawable = DeserializeSingleDrawable(reader, version);
                        if (drawable != null)
                        {
                            deserializedDrawables.Add(drawable);
                        }
                    }
                }
                catch (EndOfStreamException e)
                {
                    AetherDraw.Plugin.Log?.Error(e, "Deserialization failed due to end of stream. The data may be corrupt or incomplete.");
                }
                catch (Exception e)
                {
                    AetherDraw.Plugin.Log?.Error(e, "An unexpected error occurred during deserialization.");
                }
            }
            return deserializedDrawables;
        }

        // --- Private Helper Methods ---

        /// <summary>
        /// Serializes a single BaseDrawable object to the writer.
        /// </summary>
        private static void SerializeSingleDrawable(BinaryWriter writer, BaseDrawable drawable)
        {
            // 1. Write Type Discriminator (ObjectDrawMode)
            writer.Write((byte)drawable.ObjectDrawMode);

            // 2. Write Common BaseDrawable Properties
            writer.Write(drawable.Color.X); writer.Write(drawable.Color.Y);
            writer.Write(drawable.Color.Z); writer.Write(drawable.Color.W);
            writer.Write(drawable.Thickness); // This is unscaled
            writer.Write(drawable.IsFilled);
            writer.Write(drawable.UniqueId.ToByteArray()); // Serialize the UniqueId for network identification
            writer.Write(drawable.Name ?? drawable.ObjectDrawMode.ToString());
            writer.Write(drawable.IsLocked);

            // 3. Write Type-Specific Properties
            switch (drawable.ObjectDrawMode)
            {
                case DrawMode.Pen:
                    var path = (DrawablePath)drawable;
                    writer.Write(path.PointsRelative.Count);
                    foreach (var point in path.PointsRelative) { writer.Write(point.X); writer.Write(point.Y); }
                    break;
                case DrawMode.StraightLine:
                    var line = (DrawableStraightLine)drawable;
                    writer.Write(line.StartPointRelative.X); writer.Write(line.StartPointRelative.Y);
                    writer.Write(line.EndPointRelative.X); writer.Write(line.EndPointRelative.Y);
                    break;
                case DrawMode.Rectangle:
                    var rect = (DrawableRectangle)drawable;
                    writer.Write(rect.StartPointRelative.X); writer.Write(rect.StartPointRelative.Y);
                    writer.Write(rect.EndPointRelative.X); writer.Write(rect.EndPointRelative.Y);
                    writer.Write(rect.RotationAngle);
                    break;
                case DrawMode.Circle:
                case DrawMode.Donut:
                    var circle = (DrawableCircle)drawable;
                    writer.Write(circle.CenterRelative.X); writer.Write(circle.CenterRelative.Y);
                    writer.Write(circle.Radius);
                    break;
                case DrawMode.Arrow:
                    var arrow = (DrawableArrow)drawable;
                    writer.Write(arrow.StartPointRelative.X); writer.Write(arrow.StartPointRelative.Y);
                    writer.Write(arrow.EndPointRelative.X); writer.Write(arrow.EndPointRelative.Y);
                    writer.Write(arrow.RotationAngle);
                    writer.Write(arrow.ArrowheadLengthOffset);
                    writer.Write(arrow.ArrowheadWidthScale);
                    break;
                case DrawMode.Cone:
                    var cone = (DrawableCone)drawable;
                    writer.Write(cone.ApexRelative.X); writer.Write(cone.ApexRelative.Y);
                    writer.Write(cone.BaseCenterRelative.X); writer.Write(cone.BaseCenterRelative.Y);
                    writer.Write(cone.RotationAngle);
                    break;
                case DrawMode.Dash:
                    var dash = (DrawableDash)drawable;
                    writer.Write(dash.PointsRelative.Count);
                    foreach (var point in dash.PointsRelative) { writer.Write(point.X); writer.Write(point.Y); }
                    writer.Write(dash.DashLength);
                    writer.Write(dash.GapLength);
                    break;
                case DrawMode.Triangle:
                    var triangle = (DrawableTriangle)drawable;
                    for (int i = 0; i < 3; i++)
                    {
                        writer.Write(triangle.Vertices[i].X);
                        writer.Write(triangle.Vertices[i].Y);
                    }
                    break;
                case DrawMode.Pie:
                    var pie = (DrawablePie)drawable;
                    writer.Write(pie.CenterRelative.X); writer.Write(pie.CenterRelative.Y);
                    writer.Write(pie.Radius);
                    writer.Write(pie.RotationAngle);
                    writer.Write(pie.SweepAngle);
                    break;
                case DrawMode.EmojiImage:
                case DrawMode.Image:
                case DrawMode.BossImage:
                case DrawMode.CircleAoEImage:
                case DrawMode.DonutAoEImage:
                case DrawMode.FlareImage:
                case DrawMode.LineStackImage:
                case DrawMode.SpreadImage:
                case DrawMode.StackImage:
                case DrawMode.Waymark1Image:
                case DrawMode.Waymark2Image:
                case DrawMode.Waymark3Image:
                case DrawMode.Waymark4Image:
                case DrawMode.WaymarkAImage:
                case DrawMode.WaymarkBImage:
                case DrawMode.WaymarkCImage:
                case DrawMode.WaymarkDImage:
                case DrawMode.RoleTankImage:
                case DrawMode.RoleHealerImage:
                case DrawMode.RoleMeleeImage:
                case DrawMode.RoleRangedImage:
                case DrawMode.Party1Image:
                case DrawMode.Party2Image:
                case DrawMode.Party3Image:
                case DrawMode.Party4Image:
                case DrawMode.Party5Image:
                case DrawMode.Party6Image:
                case DrawMode.Party7Image:
                case DrawMode.Party8Image:
                case DrawMode.SquareImage:
                case DrawMode.CircleMarkImage:
                case DrawMode.TriangleImage:
                case DrawMode.PlusImage:
                case DrawMode.StackIcon:
                case DrawMode.SpreadIcon:
                case DrawMode.TetherIcon:
                case DrawMode.BossIconPlaceholder:
                case DrawMode.AddMobIcon:
                case DrawMode.Dot1Image:
                case DrawMode.Dot2Image:
                case DrawMode.Dot3Image:
                case DrawMode.Dot4Image:
                case DrawMode.Dot5Image:
                case DrawMode.Dot6Image:
                case DrawMode.Dot7Image:
                case DrawMode.Dot8Image:
                case DrawMode.StatusIconPlaceholder:
                case DrawMode.RoleCasterImage:
                case DrawMode.JobPldImage:
                case DrawMode.JobWarImage:
                case DrawMode.JobDrkImage:
                case DrawMode.JobGnbImage:
                case DrawMode.JobWhmImage:
                case DrawMode.JobSchImage:
                case DrawMode.JobAstImage:
                case DrawMode.JobSgeImage:
                case DrawMode.JobMnkImage:
                case DrawMode.JobDrgImage:
                case DrawMode.JobNinImage:
                case DrawMode.JobSamImage:
                case DrawMode.JobRprImage:
                case DrawMode.JobVprImage:
                case DrawMode.JobBrdImage:
                case DrawMode.JobMchImage:
                case DrawMode.JobDncImage:
                case DrawMode.JobBlmImage:
                case DrawMode.JobSmnImage:
                case DrawMode.JobRdmImage:
                case DrawMode.JobPctImage:
                case DrawMode.Bind1Image:
                case DrawMode.Bind2Image:
                case DrawMode.Bind3Image:
                case DrawMode.Ignore1Image:
                case DrawMode.Ignore2Image:
                    var image = (DrawableImage)drawable;
                    writer.Write(image.ImageResourcePath ?? string.Empty);
                    writer.Write(image.PositionRelative.X); writer.Write(image.PositionRelative.Y);
                    writer.Write(image.DrawSize.X); writer.Write(image.DrawSize.Y);
                    writer.Write(image.RotationAngle);
                    break;
                case DrawMode.TextTool:
                    var text = (DrawableText)drawable;
                    writer.Write(text.RawText ?? string.Empty);
                    writer.Write(text.PositionRelative.X); writer.Write(text.PositionRelative.Y);
                    writer.Write(text.FontSize);
                    writer.Write(text.WrappingWidth);
                    break;
                case DrawMode.Laser:
                    var laser = (DrawableLaser)drawable;
                    var pts = laser.GetPoints();
                    writer.Write(pts.Count); 
                    foreach (var point in pts)
                    {
                        writer.Write(point.X);
                        writer.Write(point.Y);
                    }
                    break;
                default:
                    AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Unhandled DrawMode during serialization: {drawable.ObjectDrawMode}");
                    break;
            }
        }

        /// <summary>
        /// Deserializes a single BaseDrawable object from the reader.
        /// </summary>
        private static BaseDrawable? DeserializeSingleDrawable(BinaryReader reader, int version)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length) return null;
            DrawMode mode = (DrawMode)reader.ReadByte();

            // Perform safety checks before reading each block of data
            if (reader.BaseStream.Position + sizeof(float) * 4 + sizeof(float) + sizeof(bool) + 16 > reader.BaseStream.Length) return null;
            Vector4 color = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            float thickness = reader.ReadSingle();
            bool isFilled = reader.ReadBoolean();
            Guid uniqueId = new Guid(reader.ReadBytes(16));
            string name = "Object";
            if (version >= 2)
            {
                try
                {
                    if (reader.BaseStream.Position < reader.BaseStream.Length)
                        name = reader.ReadString();
                }
                catch (EndOfStreamException) { name = "Object"; }
            }
            else
            {
                name = mode.ToString();
            }

            bool isLocked = false;
            if (version >= 3)
            {
                try
                {
                    if (reader.BaseStream.Position < reader.BaseStream.Length)
                        isLocked = reader.ReadBoolean();
                }
                catch (EndOfStreamException) { isLocked = false; }
            }
            BaseDrawable? drawable = null;

            switch (mode)
            {
                case DrawMode.Pen:
                    if (reader.BaseStream.Position + sizeof(int) > reader.BaseStream.Length) return null;
                    int pathPointCount = reader.ReadInt32();
                    // SANITY CHECK: Protect against malicious data with an impossibly large number of points.
                    if (pathPointCount < 0 || pathPointCount > MAX_POINTS_PER_OBJECT)
                    {
                        AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Invalid path point count: {pathPointCount}. Skipping this drawable.");
                        return null;
                    }
                    if (reader.BaseStream.Position + sizeof(float) * 2 * pathPointCount > reader.BaseStream.Length) return null;
                    var pathPoints = new List<Vector2>(pathPointCount);
                    for (int i = 0; i < pathPointCount; i++) pathPoints.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                    var path = new DrawablePath(pathPoints.FirstOrDefault(), color, thickness);
                    path.PointsRelative.Clear(); path.PointsRelative.AddRange(pathPoints);
                    drawable = path;
                    break;
                case DrawMode.StraightLine:
                    if (reader.BaseStream.Position + sizeof(float) * 4 > reader.BaseStream.Length) return null;
                    Vector2 lineStart = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 lineEnd = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    drawable = new DrawableStraightLine(lineStart, color, thickness) { EndPointRelative = lineEnd };
                    break;
                case DrawMode.Rectangle:
                    if (reader.BaseStream.Position + sizeof(float) * 5 > reader.BaseStream.Length) return null;
                    Vector2 rectStart = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 rectEnd = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float rectRotation = reader.ReadSingle();
                    drawable = new DrawableRectangle(rectStart, color, thickness, isFilled) { EndPointRelative = rectEnd, RotationAngle = rectRotation };
                    break;
                case DrawMode.Circle:
                case DrawMode.Donut:
                    if (reader.BaseStream.Position + sizeof(float) * 3 > reader.BaseStream.Length) return null;
                    Vector2 circleCenter = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float circleRadius = reader.ReadSingle();
                    drawable = new DrawableCircle(circleCenter, color, thickness, isFilled) { Radius = circleRadius, ObjectDrawMode = mode };
                    break;
                case DrawMode.Arrow:
                    if (reader.BaseStream.Position + sizeof(float) * 7 > reader.BaseStream.Length) return null;
                    Vector2 arrowStart = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 arrowEnd = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float arrowRotation = reader.ReadSingle();
                    float arrowLengthOffset = reader.ReadSingle();
                    float arrowWidthScale = reader.ReadSingle();
                    drawable = new DrawableArrow(arrowStart, color, thickness) { EndPointRelative = arrowEnd, RotationAngle = arrowRotation, ArrowheadLengthOffset = arrowLengthOffset, ArrowheadWidthScale = arrowWidthScale };
                    break;
                case DrawMode.Cone:
                    if (reader.BaseStream.Position + sizeof(float) * 5 > reader.BaseStream.Length) return null;
                    Vector2 coneApex = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 coneBase = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float coneRotation = reader.ReadSingle();
                    drawable = new DrawableCone(coneApex, color, thickness, isFilled) { BaseCenterRelative = coneBase, RotationAngle = coneRotation };
                    break;
                case DrawMode.Dash:
                    if (reader.BaseStream.Position + sizeof(int) > reader.BaseStream.Length) return null;
                    int dashPointCount = reader.ReadInt32();
                    // SANITY CHECK: Protect against malicious data with an impossibly large number of points.
                    if (dashPointCount < 0 || dashPointCount > MAX_POINTS_PER_OBJECT)
                    {
                        AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Invalid dash point count: {dashPointCount}. Skipping this drawable.");
                        return null;
                    }
                    if (reader.BaseStream.Position + sizeof(float) * 2 * dashPointCount + sizeof(float) * 2 > reader.BaseStream.Length) return null;
                    var dashPoints = new List<Vector2>(dashPointCount);
                    for (int i = 0; i < dashPointCount; i++) dashPoints.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                    float dashLength = reader.ReadSingle();
                    float gapLength = reader.ReadSingle();
                    var dash = new DrawableDash(dashPoints.FirstOrDefault(), color, thickness);
                    dash.PointsRelative.Clear(); dash.PointsRelative.AddRange(dashPoints);
                    dash.DashLength = dashLength;
                    dash.GapLength = gapLength;
                    drawable = dash;
                    break;
                case DrawMode.Triangle:
                    if (reader.BaseStream.Position + sizeof(float) * 6 > reader.BaseStream.Length) return null;
                    var v1 = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    var v2 = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    var v3 = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    drawable = new DrawableTriangle(v1, v2, v3, color);
                    break;
                case DrawMode.Pie: // [Added] Deserialization for Pie
                    if (reader.BaseStream.Position + sizeof(float) * 4 > reader.BaseStream.Length) return null;
                    Vector2 pieCenter = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float pieRadius = reader.ReadSingle();
                    float pieRotation = reader.ReadSingle();
                    float pieSweep = reader.ReadSingle();
                    drawable = new DrawablePie(pieCenter, color, thickness, isFilled)
                    {
                        Radius = pieRadius,
                        RotationAngle = pieRotation,
                        SweepAngle = pieSweep
                    };
                    break;
                case DrawMode.EmojiImage:
                case DrawMode.Image:
                case DrawMode.BossImage:
                case DrawMode.CircleAoEImage:
                case DrawMode.DonutAoEImage:
                case DrawMode.FlareImage:
                case DrawMode.LineStackImage:
                case DrawMode.SpreadImage:
                case DrawMode.StackImage:
                case DrawMode.Waymark1Image:
                case DrawMode.Waymark2Image:
                case DrawMode.Waymark3Image:
                case DrawMode.Waymark4Image:
                case DrawMode.WaymarkAImage:
                case DrawMode.WaymarkBImage:
                case DrawMode.WaymarkCImage:
                case DrawMode.WaymarkDImage:
                case DrawMode.RoleTankImage:
                case DrawMode.RoleHealerImage:
                case DrawMode.RoleMeleeImage:
                case DrawMode.RoleRangedImage:
                case DrawMode.Party1Image:
                case DrawMode.Party2Image:
                case DrawMode.Party3Image:
                case DrawMode.Party4Image:
                case DrawMode.Party5Image:
                case DrawMode.Party6Image:
                case DrawMode.Party7Image:
                case DrawMode.Party8Image:
                case DrawMode.SquareImage:
                case DrawMode.CircleMarkImage:
                case DrawMode.TriangleImage:
                case DrawMode.PlusImage:
                case DrawMode.StackIcon:
                case DrawMode.SpreadIcon:
                case DrawMode.TetherIcon:
                case DrawMode.BossIconPlaceholder:
                case DrawMode.AddMobIcon:
                case DrawMode.Dot1Image:
                case DrawMode.Dot2Image:
                case DrawMode.Dot3Image:
                case DrawMode.Dot4Image:
                case DrawMode.Dot5Image:
                case DrawMode.Dot6Image:
                case DrawMode.Dot7Image:
                case DrawMode.Dot8Image:
                case DrawMode.StatusIconPlaceholder:
                case DrawMode.RoleCasterImage:
                case DrawMode.JobPldImage:
                case DrawMode.JobWarImage:
                case DrawMode.JobDrkImage:
                case DrawMode.JobGnbImage:
                case DrawMode.JobWhmImage:
                case DrawMode.JobSchImage:
                case DrawMode.JobAstImage:
                case DrawMode.JobSgeImage:
                case DrawMode.JobMnkImage:
                case DrawMode.JobDrgImage:
                case DrawMode.JobNinImage:
                case DrawMode.JobSamImage:
                case DrawMode.JobRprImage:
                case DrawMode.JobVprImage:
                case DrawMode.JobBrdImage:
                case DrawMode.JobMchImage:
                case DrawMode.JobDncImage:
                case DrawMode.JobBlmImage:
                case DrawMode.JobSmnImage:
                case DrawMode.JobRdmImage:
                case DrawMode.JobPctImage:
                case DrawMode.Bind1Image:
                case DrawMode.Bind2Image:
                case DrawMode.Bind3Image:
                case DrawMode.Ignore1Image:
                case DrawMode.Ignore2Image:
                    string imgPath = reader.ReadString();
                    if (reader.BaseStream.Position + sizeof(float) * 5 > reader.BaseStream.Length) return null;
                    Vector2 imgPos = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 imgSize = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float imgRotation = reader.ReadSingle();
                    drawable = new DrawableImage(mode, imgPath, imgPos, imgSize, color, imgRotation);
                    break;
                case DrawMode.TextTool:
                    string rawText = reader.ReadString();
                    if (reader.BaseStream.Position + sizeof(float) * 4 > reader.BaseStream.Length) return null;
                    Vector2 textPos = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float fontSize = reader.ReadSingle();
                    float wrapWidth = reader.ReadSingle();
                    drawable = new DrawableText(textPos, rawText, color, fontSize, wrapWidth);
                    break;
                case DrawMode.Laser:
                    if (reader.BaseStream.Position + sizeof(int) > reader.BaseStream.Length) return null;
                    int laserPointCount = reader.ReadInt32();

                    if (laserPointCount < 0 || laserPointCount > MAX_POINTS_PER_OBJECT)
                    {
                        AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Invalid laser point count: {laserPointCount}.");
                        return null;
                    }

                    var laserPoints = new List<Vector2>(laserPointCount);
                    for (int i = 0; i < laserPointCount; i++)
                        laserPoints.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));

                    drawable = new DrawableLaser(laserPoints, color, thickness);
                    break;
                default:
                    AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Unhandled DrawMode during deserialization: {mode}");
                    break;
            }

            if (drawable != null)
            {
                drawable.UniqueId = uniqueId;
                drawable.Color = color;
                drawable.Thickness = thickness;
                drawable.IsFilled = isFilled;
                drawable.IsPreview = false;
                drawable.IsLocked = isLocked;
            }
            return drawable;
        }
    }
}
