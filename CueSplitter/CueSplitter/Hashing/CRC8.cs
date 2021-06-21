using System;

namespace CueSplitter.Hashing
{
    ///
    /// This enum is used to indicate what kind of checksum you will be calculating.
    /// 
    public enum CRC8_POLY
	{
		CRC8 = 0xd5,
		/// <summary>
		/// FLAC uses this!
		/// <para>
		/// x^8 + x^2 + x^1 + x^0
		/// </para>
		/// <para>
		/// x^8 + x^2 + x + 1
		/// </para>
		/// </summary>
		CRC8_CCITT = 0x07,
		CRC8_DALLAS_MAXIM = 0x31,
		CRC8_SAE_J1850 = 0x1D,
		CRC_8_WCDMA = 0x9b,
	};

	/// 
	/// Class for calculating CRC8 checksums.
	/// 
	public class CRC8Calc
	{
		private byte[] table = new byte[256];

		public byte Checksum(Span<byte> val)
		{
			if (val == null)
				throw new ArgumentNullException("val");

			byte c = 0;

			foreach (byte b in val)
			{
				c = table[c ^ b];
			}

			return c;
		}

		//public byte Checksum(params byte[] val)
		//{
		//	if (val == null)
		//		throw new ArgumentNullException("val");

		//	byte c = 0;

		//	foreach (byte b in val)
		//	{
		//		c = table[c ^ b];
		//	}

		//	return c;
		//}

		public byte[] Table
		{
			get
			{
				return this.table;
			}
			set
			{
				this.table = value;
			}
		}

		public static byte[] GenerateTable(CRC8_POLY polynomial)
		{
			byte[] csTable = new byte[256];

			for (int i = 0; i < 256; ++i)
			{
				int curr = i;

				for (int j = 0; j < 8; ++j)
				{
					if ((curr & 0x80) != 0)
					{
						curr = (curr << 1) ^ (int)polynomial;
					}
					else
					{
						curr <<= 1;
					}
				}

				csTable[i] = (byte)curr;
			}

			return csTable;
		}

		public CRC8Calc(CRC8_POLY polynomial)
		{
			this.table = GenerateTable(polynomial);
		}
	}
}