// AetherDraw/Serialization/PayloadSerializer.cs
using System;
using System.IO;
using AetherDraw.Networking;

namespace AetherDraw.Serialization
{
    /// <summary>
    /// Handles the binary serialization and deserialization of the NetworkPayload object.
    /// This class is responsible for converting the high-level payload structure into a
    /// byte array for network transmission and back.
    /// </summary>
    public static class PayloadSerializer
    {
        /// <summary>
        /// Serializes a NetworkPayload object into a byte array.
        /// </summary>
        /// <param name="payload">The NetworkPayload object to serialize.</param>
        /// <returns>A byte array representing the serialized payload.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the provided payload is null.</exception>
        public static byte[] Serialize(NetworkPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            using (var memoryStream = new MemoryStream())
            using (var writer = new BinaryWriter(memoryStream))
            {
                // Write the page index.
                writer.Write(payload.PageIndex);

                // Write the action type as a single byte.
                writer.Write((byte)payload.Action);

                // Write the data payload.
                if (payload.Data != null && payload.Data.Length > 0)
                {
                    // Write the length of the data array first.
                    writer.Write(payload.Data.Length);
                    // Write the data array itself.
                    writer.Write(payload.Data);
                }
                else
                {
                    // Write 0 to indicate no data.
                    writer.Write(0);
                }

                return memoryStream.ToArray();
            }
        }

        /// <summary>
        /// Deserializes a byte array back into a NetworkPayload object.
        /// </summary>
        /// <param name="data">The byte array containing the serialized payload data.</param>
        /// <returns>A deserialized NetworkPayload object, or null if deserialization fails.</returns>
        public static NetworkPayload? Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            try
            {
                using (var memoryStream = new MemoryStream(data))
                using (var reader = new BinaryReader(memoryStream))
                {
                    var payload = new NetworkPayload();

                    // Read PageIndex (int, 4 bytes)
                    if (reader.BaseStream.Position + sizeof(int) > reader.BaseStream.Length) return null;
                    payload.PageIndex = reader.ReadInt32();

                    // Read Action (byte, 1 byte)
                    if (reader.BaseStream.Position + sizeof(byte) > reader.BaseStream.Length) return null;
                    payload.Action = (PayloadActionType)reader.ReadByte();

                    // Read data length (int, 4 bytes)
                    if (reader.BaseStream.Position + sizeof(int) > reader.BaseStream.Length) return null;
                    int dataLength = reader.ReadInt32();

                    // Read data array
                    if (dataLength > 0)
                    {
                        if (reader.BaseStream.Position + dataLength > reader.BaseStream.Length) return null;
                        payload.Data = reader.ReadBytes(dataLength);
                    }
                    else
                    {
                        payload.Data = null;
                    }

                    return payload;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error(ex, "Failed to deserialize NetworkPayload.");
                return null;
            }
        }
    }
}
