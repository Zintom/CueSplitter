using System;
using System.IO;
using System.Buffers.Binary;
using CueSplitter.Hashing;

namespace CueSplitter
{
    public class FlacSlimDecoder
    {
        private static readonly byte[] FLAC_MARKER = new byte[] { 0x66, 0x4C, 0x61, 0x43 };

        private static readonly CRC8Calc CRC8Encoder = new CRC8Calc(CRC8_POLY.CRC8_CCITT);

        public void AnalyseStream(Stream stream)
        {
            MoveToMetaDataArea(stream);

            // Skip past all the meta data blocks (we're interested in frames).
            while (GetMetaDataBlock(stream) != null)
            {
            }

            // We're now at the start of the frames.
            bool on = IsOnFrameHeader(stream);

            NextFrame(stream);

            on = IsOnFrameHeader(stream);
        }

        /// <summary>
        /// Ensures that the stream begins with the fLaC marker.
        /// </summary>
        private static bool VerifyValidFlacEntryPoint(Stream stream)
        {
            // Ensure presence of the 32bit fLaC marker.
            byte[] fLaC = new byte[4];
            stream.Read(fLaC, 0, 4);
            if (!fLaC.EqualBytes(FLAC_MARKER))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Advances the stream to the next valid frame.
        /// </summary>
        /// <param name="stream"></param>
        public static void NextFrame(Stream stream)
        {
            // Assume we are on a valid frame boundary
            // and move the position one ahead.
            stream.Position++;

            while (!IsOnFrameHeader(stream))
            {
                stream.Position++;
            }
        }

        /// <summary>
        /// Determines whether the stream is sitting at the start of a frame header
        /// (aka: a frame boundary).
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static bool IsOnFrameHeader(Stream stream)
        {
            long startOfHeaderPosition = stream.Position;

            try
            {
                Span<byte> buffer = stackalloc byte[2]; buffer.Clear();
                stream.Read(buffer);

                // Read the first 16 bits, this contains
                // the sync code <14>, reserved <1>, and the blocking strategy <1>
                ushort sixteenBits = BinaryPrimitives.ReadUInt16BigEndian(buffer);

                // The sync code is 14 bits long not 16, so shift right by 2.
                ushort syncCode = (ushort)(sixteenBits >> 2);

                if (syncCode != 0B11111111111110)
                {
                    return false;
                    //throw new FormatException("Clearly we're not at the start of the Frame Header as the sync code area is not correct.");
                }

                // Yes we are ignoring the 'reserved' value

                ushort blockingStrategy = (ushort)(sixteenBits >> 15);

                // Ignore block size, sample rate, channel assignment, sample size in bits and reserved (16 bits)
                // https://xiph.org/flac//format.html#frame_header_notes
                // We are using stackalloc as its such a small value (16 bits(2 bytes)).
                stream.Read(stackalloc byte[2]);

                // There is a UTF-8 value at this point which annoyingly
                // has a variable size, using the Consume method to
                // advance the stream position past this.
                DecodingHelpers.ConsumeUtf8Value(stream);

                // We are at the CRC-8
                // We need to calculate our own CRC-8 and compare it to the one in the header.

                // At this point we know the length of the header (stream.Position - startOfHeaderPosition)
                int headerLength = (int)(stream.Position - startOfHeaderPosition);
                Span<byte> header = headerLength < 1024 ? stackalloc byte[headerLength] : new byte[headerLength]; header.Clear();
                // Move the stream back to the start of the header.
                stream.Position -= header.Length;
                // Re-read the header as a whole (this excludes the CRC as we have not advanced that far quite yet).
                stream.Read(header);

                byte ourHeaderChecksum = CRC8Encoder.Checksum(header);

                // Now read the checksum in the header and make sure
                // they match.
                byte[] headerChecksum = new byte[1];
                stream.Read(headerChecksum, 0, 1);

                if (ourHeaderChecksum != headerChecksum[0])
                {
                    return false;
                    //throw new FormatException("Header checksums did not match!");
                }

                return true;
            }
            finally
            {
                // Ensure we always restore the stream position before returning control back to the caller.
                stream.Position = startOfHeaderPosition;
            }
        }

        /// <summary>
        /// Forces the stream to be repositioned to the start of the meta-data block area.
        /// </summary>
        /// <param name="stream"></param>
        public static void MoveToMetaDataArea(Stream stream)
        {
            stream.Position = 4;
        }

        /// <summary>
        /// Advances the stream position past the next Meta Data block.
        /// </summary>
        /// <param name="stream"></param>
        /// <remarks>Note: Viewing the consumed meta-data block is not possible - simply because we do not 
        /// actually decode it fully.
        /// <para/>
        /// <b>Warning! </b> You must <c>MoveToMetaDataArea()</c> before trying to consume meta-data blocks.
        /// </remarks>
        /// <returns><see langword="true"/> if we managed to consume a meta-data block, or <see langword="false"/> if we have come to the end of the meta data section.</returns>
        public static bool NextMetaDataBlock(Stream stream)
        {
            return GetMetaDataBlock(stream) != null;
        }

        private static MetaDataBlock? GetMetaDataBlock(Stream stream)
        {
            // TODO Big note, we do not actually need to be interpreting this crap really,
            // We just want to be able to skip past all the meta data and jump to the frames.

            MetaDataBlockHeader? header = GetNextMetaDataBlockHeader(stream);
            if (header == null) { return null; }

            stream.Position += header.LengthOfBlock;

            return new UnknownMetaData();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stream"></param>
        /// <returns>The next <see cref="MetaDataBlockHeader"/> or <see langword="null"/> if there is no more meta data (audio frames have started).</returns>
        private static MetaDataBlockHeader? GetNextMetaDataBlockHeader(Stream stream)
        {
            Span<byte> headerBuffer = stackalloc byte[4]; headerBuffer.Clear();
            int bytesRead = stream.Read(headerBuffer);

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

        public abstract class MetaDataBlock
        {

        }

        public class UnknownMetaData : MetaDataBlock
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
            public uint LengthOfBlock;

            public MetaDataBlockHeader(bool isLastMetaDataFlag, BlockType type, uint length)
            {
                Type = type;
                IsLastMetaDataFlag = isLastMetaDataFlag;
                LengthOfBlock = length;
            }
        }
    }
}