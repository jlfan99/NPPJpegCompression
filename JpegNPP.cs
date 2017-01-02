using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ManagedCuda;
using ManagedCuda.NPP;

namespace NPPJpegCompression
{
	public static class JpegNPP
	{

        /// Defines jpeg constants defined in the specification.
        /// <summary>
        /// The maximum allowable length in each dimension of a jpeg image.
        /// </summary>
        public static ushort MaxLength = 65535;



        /// Defines jpeg file length of bytes.
        /// <summary>
        /// The maximum allowable length  of a jpeg image: 32 MegaBytes
        /// </summary>
		public const int BUFFER_SIZE = 4 << 23;


        #region Jpeg markers       

        /// <summary>
        /// Represents high detail chroma horizontal subsampling JpegSubsample.Ratio444.
        /// </summary>
        public static byte[] ChromaFourFourFourHorizontal = { 0x11, 0x11, 0x11 };

        /// <summary>
        /// Represents high detail chroma vertical subsampling JpegSubsample.Ratio444.
        /// </summary>
        public static byte[] ChromaFourFourFourVertical = { 0x11, 0x11, 0x11 };

        /// <summary>
        /// Represents medium detail chroma vertical subsampling JpegSubsample.Ratio422.
        /// </summary>
        public static byte[]  ChromaFourTwoTwoVertical= { 0x11, 0x11, 0x11 };

        /// <summary>
        /// Represents low detail chroma vertical subsampling JpegSubsample.Ratio420.
        /// </summary>
        public static byte[] ChromaFourTwoZeroVertical = { 0x22, 0x11, 0x11 };

        /// <summary>
        /// Represents medium detail chroma horizontal subsampling JpegSubsample.Ratio422.
        /// </summary>
        public static byte[] ChromaFourTwoTwoHorizontal = { 0x22, 0x11, 0x11 };

        /// <summary>
        /// Represents low detail chroma horizontal subsampling JpegSubsample.Ratio420.
        /// </summary>
        public static byte[] ChromaFourTwoZeroHorizontal = { 0x22, 0x11, 0x11 };

        /// <summary>
        /// Marker prefix. Next byte is a marker.
        /// </summary>
        public static byte XFF = 0xff;

        /// <summary>
        /// Start of Image
        /// </summary>
        public static byte SOI = 0xd8;

        /// <summary>
        /// Start of Frame (baseline DCT)
        /// <remarks>
        /// Indicates that this is a baseline DCT-based JPEG, and specifies the width, height, number of components,
        /// and component subsampling (e.g., 4:2:0).
        /// </remarks>
        /// </summary>
        public static byte SOF0 = 0xc0;

        /// <summary>
        /// Start Of Frame (Extended Sequential DCT)
        /// <remarks>
        /// Indicates that this is a progressive DCT-based JPEG, and specifies the width, height, number of components,
        /// and component subsampling (e.g., 4:2:0).
        /// </remarks>
        /// </summary>
        public static byte SOF1 = 0xc1;

        /// <summary>
        /// Start Of Frame (progressive DCT)
        /// <remarks>
        /// Indicates that this is a progressive DCT-based JPEG, and specifies the width, height, number of components,
        /// and component subsampling (e.g., 4:2:0).
        /// </remarks>
        /// </summary>
        public static byte SOF2 = 0xc2;

        /// <summary>
        /// Define Huffman Table(s)
        /// <remarks>
        /// Specifies one or more Huffman tables.
        /// </remarks>
        /// </summary>
        public static byte DHT = 0xc4;

        /// <summary>
        /// Define Quantization Table(s)
        /// <remarks>
        /// Specifies one or more quantization tables.
        /// </remarks>
        /// </summary>
        public static byte DQT = 0xdb;

        /// <summary>
        /// Define Restart Interval
        /// <remarks>
        /// Specifies the interval between RSTn markers, in macroblocks. This marker is followed by two bytes
        /// indicating the fixed size so it can be treated like any other variable size segment.
        /// </remarks>
        /// </summary>
        public static byte DRI = 0xdd;

        /// <summary>
        /// Define First Restart
        /// <remarks>
        /// Inserted every r macroblocks, where r is the restart interval set by a DRI marker.
        /// Not used if there was no DRI marker. The low three bits of the marker code cycle in value from 0 to 7.
        /// </remarks>
        /// </summary>
        public static byte RST0 = 0xd0;

        /// <summary>
        /// Define Eigth Restart
        /// <remarks>
        /// Inserted every r macroblocks, where r is the restart interval set by a DRI marker.
        /// Not used if there was no DRI marker. The low three bits of the marker code cycle in value from 0 to 7.
        /// </remarks>
        /// </summary>
        public static byte RST7 = 0xd7;

        /// <summary>
        /// Start of Scan
        /// <remarks>
        /// Begins a top-to-bottom scan of the image. In baseline DCT JPEG images, there is generally a single scan.
        /// Progressive DCT JPEG images usually contain multiple scans. This marker specifies which slice of data it
        /// will contain, and is immediately followed by entropy-coded data.
        /// </remarks>
        /// </summary>
        public static byte SOS = 0xda;

        /// <summary>
        /// Comment
        /// <remarks>
        /// Contains a text comment.
        /// </remarks>
        /// </summary>
        public static byte COM = 0xfe;

        /// <summary>
        /// End of Image
        /// </summary>
        public static byte EOI = 0xd9;

        /// <summary>
        /// Application specific marker for marking the jpeg format.
        /// <see href="http://www.sno.phy.queensu.ca/~phil/exiftool/TagNames/JPEG.html"/>
        /// </summary>
        public static byte APP0 = 0xe0;

        /// <summary>
        /// Application specific marker for marking where to store metadata.
        /// </summary>
        public static byte APP1 = 0xe1;

        /// <summary>
        /// Application specific marker used by Adobe for storing encoding information for DCT filters.
        /// </summary>
        public static byte APP14 = 0xee;

        /// <summary>
        /// Application specific marker used by GraphicConverter to store JPEG quality.
        /// </summary>
        public static byte APP15 = 0xef;

        #endregion

        //System.Drawing.Bitmap is ordered BGR not RGB
        //The NPP routine YCbCR to BGR needs clampled input values, following the YCbCr standard.
        //But JPEG uses unclamped values ranging all from [0..255], thus use our own color matrix:
        public static float[,] YCbCrToBgr = new float[3, 4]
        {{1.0f, 1.772f,     0.0f,    -226.816f  },
             {1.0f, -0.34414f, -0.71414f, 135.45984f},
             {1.0f, 0.0f,       1.402f,  -179.456f  }};
        //System.Drawing.Bitmap is ordered BGR not RGB
        //The NPP routine BGR to YCbCR outputs the values in clamped range, following the YCbCr standard.
        //But JPEG uses unclamped values ranging all from [0..255], thus use our own color matrix:
        public static float[,] BgrToYCbCr = new float[3, 4]
        {{0.114f,     0.587f,    0.299f,   0},
             {0.5f,      -0.33126f, -0.16874f, 128},
             {-0.08131f, -0.41869f,  0.5f,     128}};
        #region Data structures
        public struct FrameHeader
		{
			public byte nSamplePrecision;
			public ushort nHeight;
			public ushort nWidth;
			public byte nComponents;
			public byte[] aComponentIdentifier;
			public byte[] aSamplingFactors;
			public byte[] aQuantizationTableSelector;

		};

        public struct ScanHeader
		{
			public byte nComponents;
			public byte[] aComponentSelector;
			public byte[] aHuffmanTablesSelector;
			public byte nSs;
			public byte nSe;
			public byte nA;

		};

        public class QuantizationTable
		{
			public enum QuantizationType
			{
				Zero,
				Luminance,
				Chroma
			}

			public byte nPrecisionAndIdentifier;
			public byte[] aTable;

			public QuantizationTable() :
				this(QuantizationType.Zero, 0)
			{ }

			//Tables as given in JPEG standard / LibJPEG
			public QuantizationTable(QuantizationType type, int quality)
			{
				switch (type)
				{
					case QuantizationType.Zero:
						aTable = new byte[64];
						nPrecisionAndIdentifier = 0;
						break;
					case QuantizationType.Luminance:
						aTable = new byte[] {   16,  11,  10,  16,  24,  40,  51,  61,
                                                12,  12,  14,  19,  26,  58,  60,  55,
                                                14,  13,  16,  24,  40,  57,  69,  56,
                                                14,  17,  22,  29,  51,  87,  80,  62,
                                                18,  22,  37,  56,  68, 109, 103,  77,
                                                24,  35,  55,  64,  81, 104, 113,  92,
                                                49,  64,  78,  87, 103, 121, 120, 101,
                                                72,  92,  95,  98, 112, 100, 103,  99};
						nPrecisionAndIdentifier = 0;
						break;
					case QuantizationType.Chroma:
						aTable = new byte[] {   17,  18,  24,  47,  99,  99,  99,  99,
                                                18,  21,  26,  66,  99,  99,  99,  99,
                                                24,  26,  56,  99,  99,  99,  99,  99,
                                                47,  66,  99,  99,  99,  99,  99,  99,
                                                99,  99,  99,  99,  99,  99,  99,  99,
                                                99,  99,  99,  99,  99,  99,  99,  99,
                                                99,  99,  99,  99,  99,  99,  99,  99,
                                                99,  99,  99,  99,  99,  99,  99,  99 };
						nPrecisionAndIdentifier = 1;
						break;
					default:
						aTable = new byte[64];
						break;
				}

				if (type != QuantizationType.Zero)
				{
					if (quality <= 0) quality = 1;
					if (quality > 100) quality = 100;

					if (quality < 50)
						quality = 5000 / quality;
					else
						quality = 200 - quality * 2;

					for (int i = 0; i < aTable.Length; i++)
					{
						int temp = ((int)aTable[i] * quality + 50) / 100;
						/* limit the values to the valid range */
						if (temp <= 0L) temp = 1;
						if (temp > 32767L) temp = 32767; /* max quantizer needed for 12 bits */
						bool force_baseline = true;
						if (force_baseline && temp > 255L)
							temp = 255;		/* limit to baseline range if requested */
						aTable[i] = (byte)temp;
					}
				}
			}

		};

        public class HuffmanTable
		{
			public enum HuffmanType
			{
				Zero,
				LuminanceDC,
				ChromaDC,
				LuminanceAC,
				ChromaAC
			}

			public byte nClassAndIdentifier;
			public byte[] aCodes; //aCodes and aTable must be one continuous memory segment!
			//public byte[] aTable;

			public HuffmanTable() :
				this(HuffmanType.Zero)
			{ }

			//Tables as given in JPEG standard / LibJPEG
			public HuffmanTable(HuffmanType type)
			{
				switch (type)
				{
					case HuffmanType.Zero:
						aCodes = new byte[16 + 256];
						nClassAndIdentifier = 0;
						break;
					case HuffmanType.LuminanceDC:
						aCodes = new byte[16 + 256] { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, //bits
                            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0, 0, 0, 0, //vals
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0
                        };
						nClassAndIdentifier = 0;
						break;
					case HuffmanType.ChromaDC:
						aCodes = new byte[16 + 256] { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, //bits
                            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0, 0, 0, 0, //vals
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   0, 0, 0, 0, 0
                        };
						nClassAndIdentifier = 1;
						break;
					case HuffmanType.LuminanceAC:
						aCodes = new byte[16 + 256] { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d, //bits
                            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07, //vals
                            0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08, 0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
                            0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
                            0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
                            0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
                            0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
                            0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
                            0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
                            0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
                            0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
                            0xf9, 0xfa, 0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                        };
						nClassAndIdentifier = 16;
						break;
					case HuffmanType.ChromaAC:
						aCodes = new byte[16 + 256] { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77, //bits
                            0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71, //vals
                            0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
                            0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
                            0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
                            0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
                            0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
                            0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
                            0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
                            0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
                            0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
                            0xf9, 0xfa, 0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                            0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
                        };
						nClassAndIdentifier = 17;
						break;
					default:
						break;
				}
			}

		};
        #endregion

        #region internal methods (more or less 1:1 from NPP Jpeg sample)
        public static void writeMarker(byte nMarker, byte[] pData, ref int pos)
		{
			pData[pos] = XFF; pos++;
			pData[pos] = nMarker; pos++;
		}

        public static void write(byte[] pData, ushort value, ref int pos)
		{
			byte s1, s2;
			s1 = (byte)(value / 256);
			s2 = (byte)(value - (s1 * 256));
			
			pData[pos] = s1; pos++;
			pData[pos] = s2; pos++;
		}

        public static void write(byte[] pData, byte value, ref int pos)
		{
			pData[pos] = value; pos++;
		}

        public static void writeJFIFTag(byte[] pData, ref int pos)
		{
			byte[] JFIF_TAG = new byte[]
			{
				0x4a, 0x46, 0x49, 0x46, 0x00,
				0x01, 0x02,
				0x00,
				0x00, 0x01, 0x00, 0x01,
				0x00, 0x00
			};

			writeMarker(APP0, pData, ref pos);
			write(pData, (ushort)(JFIF_TAG.Length + 2), ref pos);
			for (int i = 0; i < JFIF_TAG.Length; i++)
			{
				pData[pos + i] = JFIF_TAG[i];
			}

			pos += JFIF_TAG.Length;
		}

        public static void writeFrameHeader(FrameHeader header, byte[] pData, ref int pos)
		{
			byte[] pTemp = new byte[128];
			int posTemp = 0;
			write(pTemp, header.nSamplePrecision, ref posTemp);
			write(pTemp, header.nHeight, ref posTemp);
			write(pTemp, header.nWidth, ref posTemp);
			write(pTemp, header.nComponents, ref posTemp);


			for (int c = 0; c < header.nComponents; ++c)
			{
				write(pTemp, header.aComponentIdentifier[c], ref posTemp);
				write(pTemp, header.aSamplingFactors[c], ref posTemp);
				write(pTemp, header.aQuantizationTableSelector[c], ref posTemp);
			}

			ushort nLength = (ushort)(posTemp);

			writeMarker(SOF0, pData, ref pos);
			write(pData, (ushort)(nLength + 2), ref pos);
			for (int i = 0; i < nLength; i++)
			{
				pData[pos + i] = pTemp[i];
			}
			pos += nLength;
		}

        public static void writeScanHeader(ScanHeader header, byte[] pData, ref int pos)
		{
			byte[] pTemp = new byte[128];
			int posTemp = 0;

			write(pTemp, header.nComponents, ref posTemp);

			for (int c = 0; c < header.nComponents; ++c)
			{
				write(pTemp, header.aComponentSelector[c], ref posTemp);
				write(pTemp, header.aHuffmanTablesSelector[c], ref posTemp);
			}

			write(pTemp, header.nSs, ref posTemp);
			write(pTemp, header.nSe, ref posTemp);
			write(pTemp, header.nA, ref posTemp);

			ushort nLength = (ushort)(posTemp);

			writeMarker(SOS, pData, ref pos);
			write(pData, (ushort)(nLength + 2), ref pos);
			for (int i = 0; i < nLength; i++)
			{
				pData[pos + i] = pTemp[i];
			}
			pos += nLength;
		}

        public static void writeQuantizationTable(QuantizationTable table, byte[] pData, ref int pos)
		{
			writeMarker(DQT, pData, ref pos);
			write(pData, (ushort)(65 + 2), ref pos);

			write(pData, table.nPrecisionAndIdentifier, ref pos);
			for (int i = 0; i < 64; i++)
			{
				pData[pos + i] = table.aTable[i];
			}
			pos += 64;
		}

        public static void writeHuffmanTable(HuffmanTable table, byte[] pData, ref int pos)
		{
			writeMarker(DHT, pData, ref pos);

			// Number of Codes for Bit Lengths [1..16]
			int nCodeCount = 0;

			for (int i = 0; i < 16; ++i)
			{
				nCodeCount += table.aCodes[i];
			}

			write(pData, (ushort)(17 + nCodeCount + 2), ref pos);

			write(pData, table.nClassAndIdentifier, ref pos);
			for (int i = 0; i < 16; i++)
			{
				pData[pos + i] = table.aCodes[i];
			}
			pos += 16;
			for (int i = 0; i < nCodeCount; i++)
			{
				pData[pos + i] = table.aCodes[i + 16];
			}
			pos += nCodeCount;
		}

        public static int DivUp(int x, int d)
		{
			return (x + d - 1) / d;
		}

        public static ushort readUShort(byte[] pData, ref int pos)
		{
			byte s1 = pData[pos], s2 = pData[pos + 1];
			pos += 2;

			return (ushort)(256 * s1 + s2);
		}

        public static byte readByte(byte[] pData, ref int pos)
		{
			byte s1 = pData[pos];
			pos++;

			return s1;
		}

        public static int nextMarker(byte[] pData, ref int nPos, int nLength)
		{
			if (nPos >= nLength)
				return -1;
			byte c = pData[nPos];
			nPos++;

			do
			{
				while (c != 0xffu && nPos < nLength)
				{
					c = pData[nPos];
					nPos++;
				}

				if (nPos >= nLength)
					return -1;

				c = pData[nPos];
				nPos++;
			}
			while (c == 0 || c == 0x0ffu);

			return c;
		}

        public static void readFrameHeader(byte[] pData, ref int p, ref FrameHeader header)
		{
			int pos = p;
			readUShort(pData, ref pos);
			header.nSamplePrecision = readByte(pData, ref pos);
			header.nHeight = readUShort(pData, ref pos);
			header.nWidth = readUShort(pData, ref pos);
			header.nComponents = readByte(pData, ref pos);


			for (int c = 0; c < header.nComponents; ++c)
			{
				header.aComponentIdentifier[c] = readByte(pData, ref pos);
				header.aSamplingFactors[c] = readByte(pData, ref pos);
				header.aQuantizationTableSelector[c] = readByte(pData, ref pos);
			}

		}

        public static void readScanHeader(byte[] pData, ref int p, ref ScanHeader header)
		{
			int pos = p;
			readUShort(pData, ref pos);

			header.nComponents = readByte(pData, ref pos);

			for (int c = 0; c < header.nComponents; ++c)
			{
				header.aComponentSelector[c] = readByte(pData, ref pos);
				header.aHuffmanTablesSelector[c] = readByte(pData, ref pos);
			}

			header.nSs = readByte(pData, ref pos);
			header.nSe = readByte(pData, ref pos);
			header.nA = readByte(pData, ref pos);
		}

        public static void readQuantizationTables(byte[] pData, ref int p, QuantizationTable[] pTables)
		{
			int pos = p;
			int nLength = readUShort(pData, ref pos) - 2;

			while (nLength > 0)
			{
				byte nPrecisionAndIdentifier = readByte(pData, ref pos);
				int nIdentifier = nPrecisionAndIdentifier & 0x0f;

				pTables[nIdentifier].nPrecisionAndIdentifier = nPrecisionAndIdentifier;
				for (int i = 0; i < 64; i++)
				{
					pTables[nIdentifier].aTable[i] = readByte(pData, ref pos);
				}
				nLength -= 65;
			}
		}

        public static void readHuffmanTables(byte[] pData, ref int p, HuffmanTable[] pTables)
		{
			int pos = p;
			int nLength = readUShort(pData, ref pos) - 2;

			while (nLength > 0)
			{
				byte nClassAndIdentifier = readByte(pData, ref pos);
				int nClass = nClassAndIdentifier >> 4; // AC or DC
				int nIdentifier = nClassAndIdentifier & 0x0f;
				int nIdx = nClass * 2 + nIdentifier;
				pTables[nIdx].nClassAndIdentifier = nClassAndIdentifier;

				// Number of Codes for Bit Lengths [1..16]
				int nCodeCount = 0;

				for (int i = 0; i < 16; ++i)
				{
					pTables[nIdx].aCodes[i] = readByte(pData, ref pos);
					nCodeCount += pTables[nIdx].aCodes[i];
				}
				for (int i = 0; i < nCodeCount; i++)
				{
					pTables[nIdx].aCodes[i + 16] = readByte(pData, ref pos);
				}

				nLength -= 17 + nCodeCount;
			}
		}

        public static void readRestartInterval(byte[] pData, ref int pos, ref int nRestartInterval)
		{
			int p = pos;
			readUShort(pData, ref p);
			nRestartInterval = readUShort(pData, ref p);
		}
        #endregion
                
    }
}
