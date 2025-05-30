// AetherDraw/Serialization/PlanSerializer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AetherDraw.DrawingLogic;
using AetherDraw.Windows; // For MainWindow.PageData

namespace AetherDraw.Serialization
{
    /// <summary>
    /// Handles serialization and deserialization of multi-page AetherDraw plans.
    /// This class defines a binary format for storing multiple pages,
    /// utilizing DrawableSerializer for individual page content.
    /// </summary>
    public static class PlanSerializer
    {
        private static readonly byte[] FileSignature = { (byte)'A', (byte)'D', (byte)'P', (byte)'N' }; // "ADPN" for AetherDraw PlaN
        private const uint CurrentPlanFormatVersion = 1; // Version for the multi-page plan structure itself

        /// <summary>
        /// Represents the deserialized structure of an AetherDraw plan.
        /// </summary>
        public class DeserializedPlan
        {
            public string PlanName { get; set; } = string.Empty;
            public List<MainWindow.PageData> Pages { get; set; } = new List<MainWindow.PageData>();
            public uint FileFormatVersionRead { get; set; }
            public ushort ProgramVersionMajorRead { get; set; }
            public ushort ProgramVersionMinorRead { get; set; }
            public ushort ProgramVersionPatchRead { get; set; }
        }

        /// <summary>
        /// Serializes a collection of pages into a byte array representing an AetherDraw plan.
        /// </summary>
        /// <param name="allPages">The list of PageData objects (from MainWindow) to serialize.</param>
        /// <param name="planName">The user-defined name for the plan.</param>
        /// <param name="appVersionMajor">Major version of the AetherDraw application.</param>
        /// <param name="appVersionMinor">Minor version of the AetherDraw application.</param>
        /// <param name="appVersionPatch">Patch version of the AetherDraw application.</param>
        /// <returns>A byte array representing the serialized plan, or null if an error occurs.</returns>
        public static byte[]? SerializePlanToBytes(List<MainWindow.PageData> allPages, string planName,
                                                  ushort appVersionMajor = 1, ushort appVersionMinor = 0, ushort appVersionPatch = 0)
        {
            if (allPages == null)
            {
                AetherDraw.Plugin.Log?.Error("[PlanSerializer] Cannot serialize null list of pages.");
                return null;
            }
            planName = string.IsNullOrEmpty(planName) ? "Unnamed Plan" : planName;

            AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Starting serialization for plan '{planName}' with {allPages.Count} pages.");

            try
            {
                using (var memoryStream = new MemoryStream())
                using (var writer = new BinaryWriter(memoryStream, Encoding.UTF8, false))
                {
                    writer.Write(FileSignature);
                    writer.Write(CurrentPlanFormatVersion);
                    writer.Write(appVersionMajor);
                    writer.Write(appVersionMinor);
                    writer.Write(appVersionPatch);
                    writer.Write(planName); // BinaryWriter handles UTF-8 string length prefixing
                    writer.Write(allPages.Count);

                    for (int i = 0; i < allPages.Count; i++)
                    {
                        var page = allPages[i];
                        AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Serializing page {i + 1}/{allPages.Count}: '{page.Name}'");
                        writer.Write(page.Name ?? $"Page {i + 1}");

                        byte[] pageDrawablesData = DrawableSerializer.SerializePageToBytes(page.Drawables);
                        writer.Write(pageDrawablesData.Length);
                        writer.Write(pageDrawablesData);
                    }

                    AetherDraw.Plugin.Log?.Info($"[PlanSerializer] Plan '{planName}' serialized successfully. Total size: {memoryStream.Length} bytes.");
                    return memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanSerializer] Error during plan serialization for '{planName}'.");
                return null;
            }
        }

        /// <summary>
        /// Deserializes a byte array back into an AetherDraw plan structure.
        /// </summary>
        /// <param name="planDataBytes">The byte array containing the serialized plan data.</param>
        /// <returns>A DeserializedPlan object containing the plan details, or null if deserialization fails.</returns>
        public static DeserializedPlan? DeserializePlanFromBytes(byte[] planDataBytes)
        {
            if (planDataBytes == null || planDataBytes.Length < FileSignature.Length + sizeof(uint)) // Basic check for minimum size
            {
                AetherDraw.Plugin.Log?.Error("[PlanSerializer] Input plan data is null or too short to be valid.");
                return null;
            }
            AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Starting deserialization of plan data. Size: {planDataBytes.Length} bytes.");

            using (var memoryStream = new MemoryStream(planDataBytes))
            using (var reader = new BinaryReader(memoryStream, Encoding.UTF8, false))
            {
                try
                {
                    byte[] signatureFromFile = reader.ReadBytes(FileSignature.Length);
                    if (!signatureFromFile.SequenceEqual(FileSignature))
                    {
                        AetherDraw.Plugin.Log?.Error("[PlanSerializer] Invalid file signature. This is not an AetherDraw Plan file.");
                        return null;
                    }

                    uint fileFormatVersionRead = reader.ReadUInt32();
                    if (fileFormatVersionRead > CurrentPlanFormatVersion)
                    {
                        AetherDraw.Plugin.Log?.Error($"[PlanSerializer] Unsupported plan file format version. File version: {fileFormatVersionRead}, Max supported: {CurrentPlanFormatVersion}.");
                        return null;
                    }
                    // Older versions might require upgrade logic if format changes significantly.
                    // For now, we accept same or older versions if DrawableSerializer handles its own versioning robustly.
                    AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] File Format Version: {fileFormatVersionRead}");


                    ushort progMajor = reader.ReadUInt16();
                    ushort progMinor = reader.ReadUInt16();
                    ushort progPatch = reader.ReadUInt16();
                    AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Plan saved with AetherDraw Version: {progMajor}.{progMinor}.{progPatch}");

                    string planName = reader.ReadString();
                    AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Plan Name: '{planName}'");

                    int numberOfPages = reader.ReadInt32();
                    if (numberOfPages < 0 || numberOfPages > 1000) // Sanity check for page count
                    {
                        AetherDraw.Plugin.Log?.Error($"[PlanSerializer] Invalid number of pages in plan: {numberOfPages}. File may be corrupt.");
                        return null;
                    }
                    AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Number of Pages in Plan: {numberOfPages}");

                    var loadedPages = new List<MainWindow.PageData>();
                    for (int i = 0; i < numberOfPages; i++)
                    {
                        AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Deserializing page index {i}.");
                        string pageName = reader.ReadString();
                        int pageDataLength = reader.ReadInt32();

                        if (pageDataLength < 0 || pageDataLength > memoryStream.Length - memoryStream.Position) // Sanity check
                        {
                            AetherDraw.Plugin.Log?.Error($"[PlanSerializer] Invalid page data length ({pageDataLength} bytes) for page '{pageName}'. File may be corrupt.");
                            return null;
                        }
                        byte[] pageDrawablesData = reader.ReadBytes(pageDataLength);

                        List<BaseDrawable> drawables = DrawableSerializer.DeserializePageFromBytes(pageDrawablesData);
                        // DeserializePageFromBytes handles its own versioning and error logging for page data.
                        // If it returns an empty list due to error, we still add the page structure.

                        loadedPages.Add(new MainWindow.PageData { Name = pageName, Drawables = drawables ?? new List<BaseDrawable>() });
                        AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Page '{pageName}' deserialized with {(drawables?.Count ?? 0)} drawables.");
                    }

                    AetherDraw.Plugin.Log?.Info($"[PlanSerializer] Plan '{planName}' deserialized successfully with {loadedPages.Count} pages.");
                    return new DeserializedPlan
                    {
                        PlanName = planName,
                        Pages = loadedPages,
                        FileFormatVersionRead = fileFormatVersionRead,
                        ProgramVersionMajorRead = progMajor,
                        ProgramVersionMinorRead = progMinor,
                        ProgramVersionPatchRead = progPatch
                    };
                }
                catch (EndOfStreamException eof)
                {
                    AetherDraw.Plugin.Log?.Error(eof, "[PlanSerializer] Deserialization error: End of stream reached unexpectedly. File may be corrupt or incomplete.");
                    return null;
                }
                catch (Exception ex)
                {
                    AetherDraw.Plugin.Log?.Error(ex, "[PlanSerializer] General deserialization error.");
                    return null;
                }
            }
        }
    }
}
