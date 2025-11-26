// AetherDraw/Serialization/PlanSerializer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AetherDraw.DrawingLogic;
using AetherDraw.Core; // Changed from AetherDraw.Windows to AetherDraw.Core for PageData

namespace AetherDraw.Serialization
{
    /// <summary>
    /// Handles serialization and deserialization of multi-page AetherDraw plans.
    /// This class defines a binary format for storing multiple pages,
    /// utilizing DrawableSerializer for individual page content.
    /// </summary>
    public static class PlanSerializer
    {
        // File signature to identify AetherDraw Plan files
        private static readonly byte[] FileSignature = { (byte)'A', (byte)'D', (byte)'P', (byte)'N' }; // "ADPN" for AetherDraw PlaN
        // Version for the multi-page plan structure itself. Increment if plan structure changes.
        private const uint CurrentPlanFormatVersion = 1;

        /// <summary>
        /// Represents the deserialized structure of an AetherDraw plan.
        /// Contains plan metadata and a list of pages.
        /// </summary>
        public class DeserializedPlan
        {
            /// <summary>
            /// Gets or sets the name of the plan.
            /// </summary>
            public string PlanName { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the list of pages in the plan.
            /// Uses Core.PageData after refactoring.
            /// </summary>
            public List<PageData> Pages { get; set; } = new List<PageData>(); // Changed to use Core.PageData

            /// <summary>
            /// Gets or sets the file format version read from the plan file.
            /// </summary>
            public uint FileFormatVersionRead { get; set; }

            /// <summary>
            /// Gets or sets the major version of AetherDraw that saved the plan.
            /// </summary>
            public ushort ProgramVersionMajorRead { get; set; }

            /// <summary>
            /// Gets or sets the minor version of AetherDraw that saved the plan.
            /// </summary>
            public ushort ProgramVersionMinorRead { get; set; }

            /// <summary>
            /// Gets or sets the patch version of AetherDraw that saved the plan.
            /// </summary>
            public ushort ProgramVersionPatchRead { get; set; }
        }

        /// <summary>
        /// Serializes a collection of pages into a byte array representing an AetherDraw plan.
        /// </summary>
        /// <param name="allPages">The list of PageData objects (from AetherDraw.Core) to serialize.</param>
        /// <param name="planName">The user-defined name for the plan.</param>
        /// <param name="appVersionMajor">Major version of the AetherDraw application.</param>
        /// <param name="appVersionMinor">Minor version of the AetherDraw application.</param>
        /// <param name="appVersionPatch">Patch version of the AetherDraw application.</param>
        /// <returns>A byte array representing the serialized plan, or null if an error occurs.</returns>
        public static byte[]? SerializePlanToBytes(List<PageData> allPages, string planName, // Changed to use Core.PageData
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
                using (var writer = new BinaryWriter(memoryStream, Encoding.UTF8, false)) // Use UTF-8 for string encoding
                {
                    // Write file header information
                    writer.Write(FileSignature);            // File type identifier
                    writer.Write(CurrentPlanFormatVersion); // Version of the plan format
                    writer.Write(appVersionMajor);          // Saving application's major version
                    writer.Write(appVersionMinor);          // Saving application's minor version
                    writer.Write(appVersionPatch);          // Saving application's patch version
                    writer.Write(planName);                 // Name of the plan
                    writer.Write(allPages.Count);           // Number of pages in the plan

                    // Serialize each page
                    for (int i = 0; i < allPages.Count; i++)
                    {
                        var page = allPages[i];
                        AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Serializing page {i + 1}/{allPages.Count}: '{page.Name}'");
                        writer.Write(page.Name ?? $"Page {i + 1}"); // Page name

                        // Serialize the drawables on the page using DrawableSerializer
                        byte[] pageDrawablesData = DrawableSerializer.SerializePageToBytes(page.Drawables);
                        writer.Write(pageDrawablesData.Length); // Length of the serialized page data
                        writer.Write(pageDrawablesData);        // Serialized page data itself
                    }

                    AetherDraw.Plugin.Log?.Info($"[PlanSerializer] Plan '{planName}' serialized successfully. Total size: {memoryStream.Length} bytes.");
                    return memoryStream.ToArray(); // Return the complete serialized plan as a byte array
                }
            }
            catch (Exception ex)
            {
                AetherDraw.Plugin.Log?.Error(ex, $"[PlanSerializer] Error during plan serialization for '{planName}'.");
                return null; // Return null if serialization fails
            }
        }

        /// <summary>
        /// Deserializes a byte array back into an AetherDraw plan structure.
        /// </summary>
        /// <param name="planDataBytes">The byte array containing the serialized plan data.</param>
        /// <returns>A DeserializedPlan object containing the plan details, or null if deserialization fails.</returns>
        public static DeserializedPlan? DeserializePlanFromBytes(byte[] planDataBytes)
        {
            // Basic validation of input data
            if (planDataBytes == null || planDataBytes.Length < FileSignature.Length + sizeof(uint))
            {
                AetherDraw.Plugin.Log?.Error("[PlanSerializer] Input plan data is null or too short to be valid.");
                return null;
            }
            AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Starting deserialization of plan data. Size: {planDataBytes.Length} bytes.");

            using (var memoryStream = new MemoryStream(planDataBytes))
            using (var reader = new BinaryReader(memoryStream, Encoding.UTF8, false)) // Use UTF-8 for string encoding
            {
                try
                {
                    // Verify file signature
                    byte[] signatureFromFile = reader.ReadBytes(FileSignature.Length);
                    if (!signatureFromFile.SequenceEqual(FileSignature))
                    {
                        AetherDraw.Plugin.Log?.Error("[PlanSerializer] Invalid file signature. This is not an AetherDraw Plan file.");
                        return null;
                    }

                    // Read and verify plan format version
                    uint fileFormatVersionRead = reader.ReadUInt32();
                    if (fileFormatVersionRead > CurrentPlanFormatVersion)
                    {
                        AetherDraw.Plugin.Log?.Error($"[PlanSerializer] Unsupported plan file format version. File version: {fileFormatVersionRead}, Max supported: {CurrentPlanFormatVersion}.");
                        return null;
                    }
                    // Future: Add logic here to handle older versions if backward compatibility requires data transformation.
                    AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] File Format Version: {fileFormatVersionRead}");

                    // Read AetherDraw version that saved the plan
                    ushort progMajor = reader.ReadUInt16();
                    ushort progMinor = reader.ReadUInt16();
                    ushort progPatch = reader.ReadUInt16();
                    AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Plan saved with AetherDraw Version: {progMajor}.{progMinor}.{progPatch}");

                    // Read plan name and number of pages
                    string planName = reader.ReadString();
                    AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Plan Name: '{planName}'");

                    int numberOfPages = reader.ReadInt32();
                    // Sanity check for page count to prevent issues with corrupted files
                    if (numberOfPages < 0 || numberOfPages > 1000)
                    {
                        AetherDraw.Plugin.Log?.Error($"[PlanSerializer] Invalid number of pages in plan: {numberOfPages}. File may be corrupt.");
                        return null;
                    }
                    AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Number of Pages in Plan: {numberOfPages}");

                    var loadedPages = new List<PageData>(); // Changed to use Core.PageData
                    for (int i = 0; i < numberOfPages; i++)
                    {
                        AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Deserializing page index {i}.");
                        string pageName = reader.ReadString(); // Page name
                        int pageDataLength = reader.ReadInt32(); // Length of serialized drawables for this page

                        // Sanity check for page data length
                        if (pageDataLength < 0 || pageDataLength > memoryStream.Length - memoryStream.Position)
                        {
                            AetherDraw.Plugin.Log?.Error($"[PlanSerializer] Invalid page data length ({pageDataLength} bytes) for page '{pageName}'. File may be corrupt.");
                            return null;
                        }
                        byte[] pageDrawablesData = reader.ReadBytes(pageDataLength); // Serialized drawables data

                        // Deserialize drawables for the page
                        List<BaseDrawable> drawables = DrawableSerializer.DeserializePageFromBytes(pageDrawablesData);

                        // Create PageData object (Core.PageData)
                        loadedPages.Add(new PageData { Name = pageName, Drawables = drawables ?? new List<BaseDrawable>() });
                        AetherDraw.Plugin.Log?.Debug($"[PlanSerializer] Page '{pageName}' deserialized with {(drawables?.Count ?? 0)} drawables.");
                    }

                    AetherDraw.Plugin.Log?.Info($"[PlanSerializer] Plan '{planName}' deserialized successfully with {loadedPages.Count} pages.");
                    // Return the fully deserialized plan
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
        /// <summary>
        /// Serializes a list of GUIDs (used for delete operations) into a byte array.
        /// </summary>
        public static byte[] SerializeGuids(List<Guid> guids)
        {
            if (guids == null) return Array.Empty<byte>();

            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                writer.Write(guids.Count);
                foreach (var guid in guids)
                {
                    writer.Write(guid.ToByteArray());
                }
                return memoryStream.ToArray();
            }
        }

        /// <summary>
        /// Deserializes a byte array into a list of GUIDs.
        /// </summary>
        public static List<Guid> DeserializeGuids(byte[] data)
        {
            var guids = new List<Guid>();
            if (data == null || data.Length < 4) return guids;

            using (var memoryStream = new MemoryStream(data))
            using (var reader = new BinaryReader(memoryStream))
            {
                try
                {
                    int count = reader.ReadInt32();
                    // Sanity check
                    if (count < 0 || count > 10000) return guids;

                    for (int i = 0; i < count; i++)
                    {
                        if (reader.BaseStream.Position + 16 > reader.BaseStream.Length) break;
                        guids.Add(new Guid(reader.ReadBytes(16)));
                    }
                }
                catch (Exception) { /* Handle corruption gracefully */ }
            }
            return guids;
        }
    }
}
