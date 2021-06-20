using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;

namespace CueSplitter
{
    public class FlacAnalyser
    {
        private static readonly byte[] FLAC_MARKER = new byte[] { 0x66, 0x4C, 0x61, 0x43 };

        public void AnalyseStream(Stream stream)
        {
            // Ensure presence of the 32bit fLaC marker.
            byte[] fLaC = new byte[4];
            stream.Read(fLaC, 0, 4);
            if (!fLaC.EqualBytes(FLAC_MARKER))
            {
                throw new FormatException("No fLaC marker present.");
            }

            // Skip past all the meta data blocks (we're interested in frames).
            while (GetMetaDataBlock(stream) != null)
            {
            }


        }

        private static MetaDataBlock? GetMetaDataBlock(Stream stream)
        {
            // TODO Big note, we do not actually need to be interpreting this crap really,
            // We just want to be able to skip past all the meta data and jump to the frames.

            MetaDataBlockHeader? header = GetNextMetaDataBlockHeader(stream);
            if (header == null) { return null; }

            stream.Position += header.Length;

            return new UnknownMetaData();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <returns>The next <see cref="MetaDataBlockHeader"/> or <see langword="null"/> if there is no more meta data (audio frames have started).</returns>
        private static MetaDataBlockHeader? GetNextMetaDataBlockHeader(Stream stream)
        {
            byte[] headerBuffer = new byte[4];
            stream.Read(headerBuffer, 0, 4);

            // Check for the sync code (meaning we have hit a frame, no longer in the Meta Data area).
            // The sync code is 14 bits long.
            // The sync code is the number 16382 in binary, 11111111111110.
            uint synccodeArea = BinaryPrimitives.ReadUInt32BigEndian(headerBuffer) >> 18;
            if(synccodeArea == 0b11111111111110) // 16382
            {
                // Uh oh we're at an audio frame, back up, pretend we didn't just read those bytes.
                stream.Position -= 4;
                return null;
            }

            // Avoid BitConverter as that will use Little Endian
            // This neat BinaryPrimitives class allows for explicit
            // Big Endian (most significant byte first).
            uint header = BinaryPrimitives.ReadUInt32BigEndian(headerBuffer);
            var headerAsBinary = ToBinaryString(header);

            uint mask = 0B_1000_0000_0000_0000_0000_0000_0000_0000;
            uint lastMetaDataBlockFlag = (header & mask) >> 31;

            mask = 0B_0111_1111_0000_0000_0000_0000_0000_0000;
            uint blockType = (header & mask) >> 24;

            mask = 0B_0000_0000_1111_1111_1111_1111_1111_1111;
            uint length = header & mask; // We dont need to shift this one across because it is already right aligned.

            return new MetaDataBlockHeader(lastMetaDataBlockFlag == 1 ? true : false,
                                           (MetaDataBlockHeader.BlockType)blockType,
                                           length);
        }

        public static string ToBinaryString(uint num)
        {
            return Convert.ToString(num, 2).PadLeft(32, '0');
        }

        private abstract class MetaDataBlock
        {

        }

        private class UnknownMetaData : MetaDataBlock
        {

        }

        private class MetaDataBlockHeader
        {
            public enum BlockType
            {
                StreamInfo = 0
            }

            /// <summary>
            /// <see langword="true"/> if this block is the last metadata block before the audio blocks, <see langword="false"/> otherwise.
            /// </summary>
            public bool IsLastMetaDataFlag;

            public BlockType Type;

            /// <summary>
            /// Length (in bytes) of metadata in the block that this header relates to.
            /// </summary>
            public uint Length;

            public MetaDataBlockHeader(bool isLastMetaDataFlag, BlockType type, uint length)
            {
                Type = type;
                IsLastMetaDataFlag = isLastMetaDataFlag;
                Length = length;
            }
        }
    }
}