using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System;
using System.IO;
using System.Text;
using System.Xml;

namespace HabboFurniTools
{
    /// <summary>
    /// Provides functionality to extract the maximum number of animation states (MaxStates)
    /// from Habbo furni SWF files by analyzing embedded visualization data.
    /// </summary>
    public static class FurniMaxStateExtractor
    {
        /// <summary>
        /// Loads a Habbo furni SWF file, decompresses it if necessary, extracts the embedded binaryData XML,
        /// parses the visualization data, and returns the maximum number of states (MaxStates) defined for the furni.
        /// </summary>
        /// <param name="swfPath">Path to the furni SWF file on disk.</param>
        /// <returns>
        /// The maximum state value found (incremented by 1 since states are zero-based),
        /// or -1 if no valid visualization data is found.
        /// </returns>
        /// <exception cref="FileNotFoundException">Thrown if the specified SWF file does not exist.</exception>
        /// <exception cref="InvalidDataException">Thrown if the file signature is not a valid SWF signature (FWS/CWS).</exception>
        public static int GetMaxStatesFromSwf(string swfPath)
        {
            if (!File.Exists(swfPath))
                throw new FileNotFoundException($"SWF file not found: {swfPath}");

            byte[] swfData;

            using (var fs = File.OpenRead(swfPath))
            using (var br = new BinaryReader(fs))
            {
                string signature = new string(br.ReadChars(3)); // FWS or CWS
                byte version = br.ReadByte();
                uint fileLength = br.ReadUInt32();

                if (signature == "CWS")
                {
                    // Compressed SWF: decompress it
                    byte[] compressedData = br.ReadBytes((int)(fileLength - 8));
                    using (var compressedStream = new MemoryStream(compressedData))
                    {
                        using (var decompressedStream = new MemoryStream())
                        {

                            using (var inflater = new InflaterInputStream(compressedStream))
                                inflater.CopyTo(decompressedStream);

                            swfData = new byte[8 + decompressedStream.Length];
                            Array.Copy(Encoding.ASCII.GetBytes("FWS"), 0, swfData, 0, 3);
                            swfData[3] = version;
                            BitConverter.GetBytes((uint)(8 + decompressedStream.Length)).CopyTo(swfData, 4);
                            decompressedStream.Position = 0;
                            decompressedStream.Read(swfData, 8, (int)decompressedStream.Length);
                        }
                    }
                }
                else if (signature == "FWS")
                {
                    // Uncompressed SWF: read it directly
                    fs.Position = 0;
                    swfData = br.ReadBytes((int)fs.Length);
                }
                else
                {
                    throw new InvalidDataException("Not a valid SWF signature (expected FWS/CWS).");
                }
            }

            using (var swfStream = new MemoryStream(swfData))
            {
                using (var swfReader = new BinaryReader(swfStream))
                {

                    swfStream.Position = 8; // Skip SWF header (8 bytes)

                    // Skip RECT header: compute RECT length to move past it
                    int nbits = swfReader.ReadByte() >> 3;
                    int rectBits = 5 + nbits * 4;
                    int rectBytes = (rectBits + 7) / 8;
                    swfStream.Position = 8 + rectBytes + 2 + 2; // RECT + FrameRate(2) + FrameCount(2)

                    while (swfStream.Position < swfStream.Length)
                    {
                        if (!TryReadTagHeader(swfReader, out ushort tagCode, out uint tagLength))
                            break;

                        long tagEndPos = swfStream.Position + tagLength;

                        if (tagCode == 87) // DefineBinaryData
                        {
                            ushort characterId = swfReader.ReadUInt16();
                            swfReader.ReadUInt32(); // Reserved field
                            long dataLength = tagLength - 6;
                            byte[] binaryData = swfReader.ReadBytes((int)dataLength);

                            if (IsVisualizationXml(binaryData))
                            {
                                return ExtractMaxStates(binaryData);
                            }
                        }

                        swfStream.Position = tagEndPos;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Reads a SWF tag header from the binary stream and extracts the tag code and tag length.
        /// Handles both short (6-bit length) and long (32-bit extended) formats.
        /// </summary>
        /// <param name="reader">BinaryReader over the SWF data stream.</param>
        /// <param name="tagCode">Outputs the tag code read from the header.</param>
        /// <param name="tagLength">Outputs the length of the tag's data in bytes.</param>
        /// <returns>True if a valid tag header was read; false if the stream is too short or invalid.</returns>
        private static bool TryReadTagHeader(BinaryReader reader, out ushort tagCode, out uint tagLength)
        {
            tagCode = 0;
            tagLength = 0;

            if (reader.BaseStream.Position + 2 > reader.BaseStream.Length)
                return false;

            ushort tagCodeAndLength = reader.ReadUInt16();
            tagCode = (ushort)(tagCodeAndLength >> 6);
            ushort shortLength = (ushort)(tagCodeAndLength & 0x3F);

            if (shortLength == 0x3F)
            {
                if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
                    return false;

                tagLength = reader.ReadUInt32();
            }
            else
            {
                tagLength = shortLength;
            }

            return true;
        }

        /// <summary>
        /// Checks if a given binaryData block appears to contain XML visualization data for furni assets.
        /// Looks for standard XML markers (e.g., &lt;?xml&gt; and visualizationData) in the binary.
        /// </summary>
        /// <param name="binaryData">The raw binary data extracted from DefineBinaryData tag.</param>
        /// <returns>True if the binary data looks like XML visualization data; otherwise, false.</returns>
        private static bool IsVisualizationXml(byte[] binaryData)
        {
            try
            {
                string xml = Encoding.UTF8.GetString(binaryData);
                return xml.Contains("<?xml") && xml.Contains("visualizationData");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Parses the extracted binary XML data to locate animations in the visualizationData section,
        /// then determines the highest state ID (MaxStates) defined for the furni.
        /// </summary>
        /// <param name="binary">The XML binary data representing visualization information.</param>
        /// <returns>The highest state value + 1 (since states start at 0), or -1 if not found.</returns>
        private static int ExtractMaxStates(byte[] binary)
        {
            string xmlContent = Encoding.UTF8.GetString(binary);

            // Remove surrounding <graphics> tags that may be included in binaryData
            xmlContent = xmlContent.Replace("<graphics>", "").Replace("</graphics>", "");

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlContent);

            int maxStates = -1;

            // Search for animations directly under visualization, or under directions->direction
            XmlNodeList animations = xmlDoc.SelectNodes("//visualizationData/visualization/animations/animation");

            if (animations == null || animations.Count == 0)
            {
                animations = xmlDoc.SelectNodes("//visualizationData/visualization/directions/direction/animations/animation");
            }

            if (animations != null)
            {
                foreach (XmlNode animation in animations)
                {
                    if (animation.Name != "animation" || animation.Attributes?["id"] == null)
                        continue;

                    if (int.TryParse(animation.Attributes["id"].InnerText, out int state))
                    {
                        if (state > maxStates)
                            maxStates = state;
                    }
                }
            }

            return maxStates > -1 ? maxStates + 1 : -1;
        }
    }
}
