using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CueSplitter
{
    public static class DecodingHelpers
    {

        /// <summary>
        /// Consumes a UTF-8 value without properly decoding it.
        /// <para>
        /// This method can be used to move 1 UTF-8 character ahead in a stream.
        /// </para>
        /// <para>
        /// A UTF-8 character may by multiple bytes.
        /// </para>
        /// </summary>
        /// <param name="stream"></param>
        public static char ConsumeUtf8Value(Stream stream)
        {
            long positionAtStart = stream.Position;

            Span<byte> currentByte = stackalloc byte[1]; currentByte.Clear();

            // Retrieve a UTF-8 byte
            stream.Read(currentByte);

            // Start of a single byte character.
            if (currentByte[0] >> 7 == 0B_0)
            {
                return Encoding.UTF8.GetString(currentByte)[0];
            }

            // Start of a multi-byte sequence.
            if (currentByte[0] >> 6 == 0B_11)
            {
                while (true)
                {
                    // Retrieve the UTF-8 byte
                    currentByte.Clear();
                    stream.Read(currentByte);

                    // Continuation byte
                    if (currentByte[0] >> 6 == 0B_10)
                    {
                        continue;
                    }
                    else
                    {
                        // There are no continuation bytes so we must
                        // have jumped past the last byte for this UTF-8 character.
                        // so move the stream position back by one 
                        stream.Position -= 1;

                        int utf8CharacterLength = (int)(stream.Position - positionAtStart);
                        stream.Position = positionAtStart;

                        Span<byte> utf8Character = utf8CharacterLength < 1024 ? stackalloc byte[utf8CharacterLength] : new byte[utf8CharacterLength]; utf8Character.Clear();

                        stream.Read(utf8Character);

                        return Encoding.UTF8.GetString(utf8Character)[0];
                    }
                }
            }

            Debug.WriteLine($"UTF8 Consumer: Found {Convert.ToString(currentByte[0], 2).PadLeft(8, '0')} when it wasn't expected. Proceeding as per UTF-8 spec, returning empty char.");

            return new char();
        }

    }
}
