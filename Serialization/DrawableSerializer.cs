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
        private const int SERIALIZATION_VERSION = 1;

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
            if (data == null) throw new ArgumentNullException(nameof(data));

            var deserializedDrawables = new List<BaseDrawable>();
            using (var memoryStream = new MemoryStream(data))
            using (var reader = new BinaryReader(memoryStream))
            {
                // Read and check the serialization version
                int version = reader.ReadInt32();
                if (version != SERIALIZATION_VERSION)
                {
                    // Log error or throw exception for version mismatch
                    // For simplicity, returning empty list or throwing.
                    AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Deserialization version mismatch. Expected {SERIALIZATION_VERSION}, got {version}.");
                    // Depending on desired behavior, you might attempt an upgrade path or simply fail.
                    throw new InvalidDataException($"Serialization version mismatch. Expected {SERIALIZATION_VERSION}, got {version}.");
                }

                int drawableCount = reader.ReadInt32();
                for (int i = 0; i < drawableCount; i++)
                {
                    BaseDrawable? drawable = DeserializeSingleDrawable(reader);
                    if (drawable != null)
                    {
                        deserializedDrawables.Add(drawable);
                    }
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
            // IsPreview, IsSelected, IsHovered are runtime states, not typically serialized.

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
                // Generic handling for all image types that use DrawableImage
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
                case DrawMode.StackIcon:
                case DrawMode.SpreadIcon:
                case DrawMode.TetherIcon:
                case DrawMode.BossIconPlaceholder:
                case DrawMode.AddMobIcon:
                    var image = (DrawableImage)drawable;
                    writer.Write(image.ImageResourcePath ?? string.Empty); // Handle potential null path
                    writer.Write(image.PositionRelative.X); writer.Write(image.PositionRelative.Y);
                    writer.Write(image.DrawSize.X); writer.Write(image.DrawSize.Y); // Unscaled
                    writer.Write(image.RotationAngle);
                    // Tint is BaseDrawable.Color
                    break;
                case DrawMode.TextTool:
                    var text = (DrawableText)drawable;
                    writer.Write(text.RawText ?? string.Empty);
                    writer.Write(text.PositionRelative.X); writer.Write(text.PositionRelative.Y);
                    writer.Write(text.FontSize); // Unscaled
                    writer.Write(text.WrappingWidth); // Unscaled
                    // Color is BaseDrawable.Color
                    break;
                case DrawMode.Donut:
                    // Assuming Donut might be a shape in the future.
                    // If it has specific properties, serialize them here.
                    // For now, if it's just a DrawMode without a specific class/props beyond base:
                    AetherDraw.Plugin.Log?.Debug($"[DrawableSerializer] Serializing DrawMode.Donut (currently no specific properties beyond base).");
                    break;
                default:
                    AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Unhandled DrawMode during serialization: {drawable.ObjectDrawMode}");
                    // Consider throwing an error or writing a placeholder if this state is unexpected.
                    break;
            }
        }

        /// <summary>
        /// Deserializes a single BaseDrawable object from the reader.
        /// </summary>
        private static BaseDrawable? DeserializeSingleDrawable(BinaryReader reader)
        {
            // 1. Read Type Discriminator
            DrawMode mode = (DrawMode)reader.ReadByte();

            // 2. Read Common BaseDrawable Properties
            Vector4 color = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            float thickness = reader.ReadSingle();
            bool isFilled = reader.ReadBoolean();

            BaseDrawable? drawable = null;

            // 3. Construct object and Read Type-Specific Properties
            // Note: Constructors of DrawableX classes are used here. Ensure they match.
            // Properties not set by constructor are set afterwards.
            switch (mode)
            {
                case DrawMode.Pen:
                    int pathPointCount = reader.ReadInt32();
                    var pathPoints = new List<Vector2>(pathPointCount);
                    for (int i = 0; i < pathPointCount; i++) pathPoints.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                    var path = new DrawablePath(pathPoints.FirstOrDefault(), color, thickness); // Constructor uses first point, color, thickness
                    path.PointsRelative.Clear(); // Clear point added by constructor if it's based on the passed startPoint
                    path.PointsRelative.AddRange(pathPoints); // Add all deserialized points
                    drawable = path;
                    break;
                case DrawMode.StraightLine:
                    Vector2 lineStart = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 lineEnd = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    var line = new DrawableStraightLine(lineStart, color, thickness) { EndPointRelative = lineEnd };
                    drawable = line;
                    break;
                case DrawMode.Rectangle:
                    Vector2 rectStart = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 rectEnd = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float rectRotation = reader.ReadSingle();
                    var rect = new DrawableRectangle(rectStart, color, thickness, isFilled)
                    { EndPointRelative = rectEnd, RotationAngle = rectRotation };
                    drawable = rect;
                    break;
                case DrawMode.Circle:
                    Vector2 circleCenter = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float circleRadius = reader.ReadSingle();
                    var circle = new DrawableCircle(circleCenter, color, thickness, isFilled) { Radius = circleRadius };
                    drawable = circle;
                    break;
                case DrawMode.Arrow:
                    Vector2 arrowStart = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 arrowEnd = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float arrowRotation = reader.ReadSingle();
                    float arrowLengthOffset = reader.ReadSingle();
                    float arrowWidthScale = reader.ReadSingle();
                    var arrow = new DrawableArrow(arrowStart, color, thickness)
                    {
                        EndPointRelative = arrowEnd, RotationAngle = arrowRotation,
                        ArrowheadLengthOffset = arrowLengthOffset, ArrowheadWidthScale = arrowWidthScale
                    };
                    drawable = arrow;
                    break;
                case DrawMode.Cone:
                    Vector2 coneApex = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 coneBase = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float coneRotation = reader.ReadSingle();
                    var cone = new DrawableCone(coneApex, color, thickness, isFilled)
                    { BaseCenterRelative = coneBase, RotationAngle = coneRotation };
                    drawable = cone;
                    break;
                case DrawMode.Dash:
                    int dashPointCount = reader.ReadInt32();
                    var dashPoints = new List<Vector2>(dashPointCount);
                    for (int i = 0; i < dashPointCount; i++) dashPoints.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                    float dashLength = reader.ReadSingle();
                    float gapLength = reader.ReadSingle();
                    var dash = new DrawableDash(dashPoints.FirstOrDefault(), color, thickness);
                    dash.PointsRelative.Clear();
                    dash.PointsRelative.AddRange(dashPoints);
                    dash.DashLength = dashLength;
                    dash.GapLength = gapLength;
                    drawable = dash;
                    break;
                // Generic handling for all image types
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
                case DrawMode.StackIcon:
                case DrawMode.SpreadIcon:
                case DrawMode.TetherIcon:
                case DrawMode.BossIconPlaceholder:
                case DrawMode.AddMobIcon:
                    string imgPath = reader.ReadString();
                    Vector2 imgPos = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Vector2 imgSize = new Vector2(reader.ReadSingle(), reader.ReadSingle()); // Unscaled
                    float imgRotation = reader.ReadSingle();
                    // DrawableImage constructor: DrawMode drawMode, string imageResourcePath, Vector2 positionRelative, 
                    //                          Vector2 unscaledDrawSize, Vector4 tint, float rotation = 0f
                    // The 'mode' variable here is the specific ObjectDrawMode.
                    // The 'color' variable read earlier is the Tint.
                    var image = new DrawableImage(mode, imgPath, imgPos, imgSize, color, imgRotation);
                    drawable = image;
                    break;
                case DrawMode.TextTool:
                    string rawText = reader.ReadString();
                    Vector2 textPos = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    float fontSize = reader.ReadSingle(); // Unscaled
                    float wrapWidth = reader.ReadSingle(); // Unscaled
                    // DrawableText constructor: Vector2 positionRelative, string rawText, Vector4 color, 
                    //                           float fontSize, float wrappingWidth = 0f
                    var text = new DrawableText(textPos, rawText, color, fontSize, wrapWidth);
                    drawable = text;
                    break;
                case DrawMode.Donut:
                    // If DrawMode.Donut corresponds to a specific class with properties:
                    // var donut = new DrawableDonut(color, thickness, isFilled);
                    // ... read specific donut properties ...
                    // drawable = donut;
                    // For now, assuming it might just use base properties if no specific class exists.
                    AetherDraw.Plugin.Log?.Debug($"[DrawableSerializer] Deserializing DrawMode.Donut (currently no specific properties beyond base).");
                    // If it should be a generic placeholder or error:
                    // drawable = new BasePlaceholderDrawable(mode, color, thickness, isFilled); // Requires such a class
                    break;
                default:
                    AetherDraw.Plugin.Log?.Error($"[DrawableSerializer] Unhandled DrawMode during deserialization: {mode}");
                    // Skip any potential data for this unknown type if possible, or throw.
                    // This requires knowing the size of data for an unknown type, which is hard.
                    // Best to ensure all serializable types are handled.
                    break;
            }

            if (drawable != null)
            {
                // Ensure common properties are correctly set if not fully handled by constructor
                // (Constructors in the provided code generally do take color, thickness, isFilled)
                // drawable.ObjectDrawMode = mode; // Crucial: Set the read mode
                drawable.Color = color;
                drawable.Thickness = thickness;
                drawable.IsFilled = isFilled;
                drawable.IsPreview = false; // Deserialized objects are not previews
            }
            return drawable;
        }
    }
}
