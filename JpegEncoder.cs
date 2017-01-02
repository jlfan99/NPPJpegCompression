using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using ManagedCuda;
using ManagedCuda.NPP;
namespace NPPJpegCompression
{
  public   class JpegEncoder
    {
        int quality;
        ushort imageWidth;
        ushort imageHeight;
        
        
        JPEGCompression compression;
        JpegNPP.FrameHeader oFrameHeader;
        JpegNPP.QuantizationTable[] aQuantizationTables;
        JpegNPP.HuffmanTable[] aHuffmanTables;
        JpegNPP.ScanHeader oScanHeader;
        NppiEncodeHuffmanSpec[] apHuffmanDCTableEnc;
        NppiEncodeHuffmanSpec[] apHuffmanACTableEnc;
        CudaDeviceVariable<byte> pdScan;
        byte[] pDstOutput;
        NPPImage_8uC1[] apDstImage;
        NPPImage_16sC1[] apdDCT;
        NppiSize[] aDstSize;
        public JpegEncoder(ushort imgWidth, ushort imgHeight,int jpgQuality,int channels)
        {
            quality = jpgQuality;
            imageWidth = imgWidth;
            imageHeight = imgHeight;

            compression = new JPEGCompression();
            oFrameHeader = new JpegNPP.FrameHeader();
            oFrameHeader.nComponents = (byte)channels;
            oFrameHeader.nHeight = imageHeight;
            oFrameHeader.nSamplePrecision = 8;
            oFrameHeader.nWidth = imageWidth;
            oFrameHeader.aComponentIdentifier = new byte[] { 1, 2, 3 };
            oFrameHeader.aSamplingFactors = new byte[] { 34, 17, 17 }; //Y channel is twice the sice of Cb/Cr channel
            oFrameHeader.aQuantizationTableSelector = new byte[] { 0, 1, 1 };

            aQuantizationTables = new JpegNPP.QuantizationTable[2];
            aQuantizationTables[0] = new JpegNPP.QuantizationTable(JpegNPP.QuantizationTable.QuantizationType.Luminance, quality);
            aQuantizationTables[1] = new JpegNPP.QuantizationTable(JpegNPP.QuantizationTable.QuantizationType.Chroma, quality);

            aHuffmanTables = new JpegNPP.HuffmanTable[4];
            aHuffmanTables[0] = new JpegNPP.HuffmanTable(JpegNPP.HuffmanTable.HuffmanType.LuminanceDC);
            aHuffmanTables[1] = new JpegNPP.HuffmanTable(JpegNPP.HuffmanTable.HuffmanType.ChromaDC);
            aHuffmanTables[2] = new JpegNPP.HuffmanTable(JpegNPP.HuffmanTable.HuffmanType.LuminanceAC);
            aHuffmanTables[3] = new JpegNPP.HuffmanTable(JpegNPP.HuffmanTable.HuffmanType.ChromaAC);

            oScanHeader = new JpegNPP.ScanHeader();
            oScanHeader.nA = 0;
            oScanHeader.nComponents = (byte)channels;
            oScanHeader.nSe = 63;
            oScanHeader.nSs = 0;
            oScanHeader.aComponentSelector = new byte[] { 1, 2, 3 };
            oScanHeader.aHuffmanTablesSelector = new byte[] { 0, 17, 17 };

            apHuffmanDCTableEnc = new NppiEncodeHuffmanSpec[3];
            apHuffmanACTableEnc = new NppiEncodeHuffmanSpec[3];
            for (int i = 0; i < oFrameHeader.nComponents; ++i)
            {
                apHuffmanDCTableEnc[i] = JPEGCompression.EncodeHuffmanSpecInitAlloc(aHuffmanTables[(oScanHeader.aHuffmanTablesSelector[i] >> 4)].aCodes, NppiHuffmanTableType.nppiDCTable);
                apHuffmanACTableEnc[i] = JPEGCompression.EncodeHuffmanSpecInitAlloc(aHuffmanTables[(oScanHeader.aHuffmanTablesSelector[i] & 0x0f) + 2].aCodes, NppiHuffmanTableType.nppiACTable);
            }

            pdScan = new CudaDeviceVariable<byte>(JpegNPP.BUFFER_SIZE);
            pDstOutput = new byte[JpegNPP.BUFFER_SIZE];

            apdDCT = new NPPImage_16sC1[3];
            apDstImage = new NPPImage_8uC1[3];
            aDstSize = new NppiSize[3];
            int nMCUBlocksH = 0;
            int nMCUBlocksV = 0;

            // Compute channel sizes as stored in the JPEG (8x8 blocks & MCU block layout)
            for (int i = 0; i < oFrameHeader.nComponents; ++i)
            {
                nMCUBlocksV = Math.Max(nMCUBlocksV, oFrameHeader.aSamplingFactors[i] >> 4);
                nMCUBlocksH = Math.Max(nMCUBlocksH, oFrameHeader.aSamplingFactors[i] & 0x0f);
            }

            for (int i = 0; i < oFrameHeader.nComponents; ++i)
            {
                NppiSize oBlocks = new NppiSize();
                NppiSize oBlocksPerMCU = new NppiSize(oFrameHeader.aSamplingFactors[i] & 0x0f, oFrameHeader.aSamplingFactors[i] >> 4);

                oBlocks.width = (int)Math.Ceiling((oFrameHeader.nWidth + 7) / 8 *
                                          (float)(oBlocksPerMCU.width) / nMCUBlocksH);
                oBlocks.width = JpegNPP.DivUp(oBlocks.width, oBlocksPerMCU.width) * oBlocksPerMCU.width;

                oBlocks.height = (int)Math.Ceiling((oFrameHeader.nHeight + 7) / 8 *
                                           (float)(oBlocksPerMCU.height) / nMCUBlocksV);
                oBlocks.height = JpegNPP.DivUp(oBlocks.height, oBlocksPerMCU.height) * oBlocksPerMCU.height;

                // Allocate Memory
                apdDCT[i] = new NPPImage_16sC1(oBlocks.width * 64, oBlocks.height);
            }
        }
        public void SaveColorJpeg(string aFilename, int aQuality, Bitmap aImage)
        {
            if (aImage.PixelFormat != System.Drawing.Imaging.PixelFormat.Format24bppRgb)
            {
                throw new ArgumentException("Only three channel color images are supported.");
            }

            if (aImage.Width % 16 != 0 || aImage.Height % 16 != 0)
            {
                throw new ArgumentException("The provided bitmap must have a height and width of a multiple of 16.");
            }
            NPPImage_8uC3 src = new NPPImage_8uC3(aImage.Width, aImage.Height);
            NPPImage_8uC1 srcY = new NPPImage_8uC1(aImage.Width, aImage.Height);
            NPPImage_8uC1 srcCb = new NPPImage_8uC1(aImage.Width / 2, aImage.Height / 2);
            NPPImage_8uC1 srcCr = new NPPImage_8uC1(aImage.Width / 2, aImage.Height / 2);
            src.CopyToDevice(aImage);
            src.ColorTwist(JpegNPP.BgrToYCbCr);

            //Reduce size of of Cb and Cr channel
            src.Copy(srcY, 2);
            srcY.Resize(srcCr, 0.5, 0.5, InterpolationMode.SuperSampling);
            src.Copy(srcY, 1);
            srcY.Resize(srcCb, 0.5, 0.5, InterpolationMode.SuperSampling);
            src.Copy(srcY, 0);           

            CudaDeviceVariable<byte>[] pdQuantizationTables = new CudaDeviceVariable<byte>[2];
            pdQuantizationTables[0] = aQuantizationTables[0].aTable;
            pdQuantizationTables[1] = aQuantizationTables[1].aTable;      
            
            aDstSize[0] = new NppiSize(srcY.Width, srcY.Height);
            aDstSize[1] = new NppiSize(srcCb.Width, srcCb.Height);
            aDstSize[2] = new NppiSize(srcCr.Width, srcCr.Height);

            // Compute channel sizes as stored in the output JPEG (8x8 blocks & MCU block layout)
            NppiSize oDstImageSize = new NppiSize();
            float frameWidth = (float)Math.Floor((float)oFrameHeader.nWidth);
            float frameHeight = (float)Math.Floor((float)oFrameHeader.nHeight);

            oDstImageSize.width = (int)Math.Max(1.0f, frameWidth);
            oDstImageSize.height = (int)Math.Max(1.0f, frameHeight);

            apDstImage[0] = srcY;
            apDstImage[1] = srcCb;
            apDstImage[2] = srcCr;
                        
            /***************************			
			*   Output	
			***************************/
            // Forward DCT
            for (int i = 0; i < oFrameHeader.nComponents; ++i)
            {
                compression.DCTQuantFwd8x8LS(apDstImage[i], apdDCT[i], aDstSize[i], pdQuantizationTables[oFrameHeader.aQuantizationTableSelector[i]]);
            }
            // Huffman Encoding         
            int nScanLength = 0;

            int nTempSize = JPEGCompression.EncodeHuffmanGetSize(aDstSize[0], 3);
            CudaDeviceVariable<byte> pJpegEncoderTemp = new CudaDeviceVariable<byte>(nTempSize);
            JPEGCompression.EncodeHuffmanScan(apdDCT, 0, oScanHeader.nSs, oScanHeader.nSe, oScanHeader.nA >> 4, oScanHeader.nA & 0x0f, pdScan, ref nScanLength, apHuffmanDCTableEnc, apHuffmanACTableEnc, aDstSize, pJpegEncoderTemp);
                       

            // Write JPEG to byte array, as in original sample code    
            
            oFrameHeader.nWidth = (ushort)oDstImageSize.width;
            oFrameHeader.nHeight = (ushort)oDstImageSize.height;

            WriteFile(aFilename, nScanLength);
            
            //cleanup:
            pJpegEncoderTemp.Dispose();
            pdQuantizationTables[1].Dispose();
            pdQuantizationTables[0].Dispose();
            srcCr.Dispose();
            srcCb.Dispose();
            srcY.Dispose();
            src.Dispose();           
        }
        public void SaveGrayJpeg(string aFilename, int aQuality, Bitmap aImage)
        {
            if (aImage.PixelFormat != System.Drawing.Imaging.PixelFormat.Format8bppIndexed)
            {
                throw new ArgumentException("Only one channel gray images are supported.");
            }

            if (aImage.Width % 16 != 0 || aImage.Height % 16 != 0)
            {
                throw new ArgumentException("The provided bitmap must have a height and width of a multiple of 16.");
            }
            NPPImage_8uC1 src = new NPPImage_8uC1(aImage.Width, aImage.Height);
            src.CopyToDevice(aImage);

            //Get quantization tables from JPEG standard with quality scaling

            CudaDeviceVariable<byte>[] pdQuantizationTables = new CudaDeviceVariable<byte>[2];
            pdQuantizationTables[0] = aQuantizationTables[0].aTable;
            pdQuantizationTables[1] = aQuantizationTables[1].aTable;

            aDstSize[0] = new NppiSize(src.Width, src.Height);

            // Compute channel sizes as stored in the output JPEG (8x8 blocks & MCU block layout)
            NppiSize oDstImageSize = new NppiSize();
            float frameWidth = (float)Math.Floor((float)oFrameHeader.nWidth);
            float frameHeight = (float)Math.Floor((float)oFrameHeader.nHeight);

            oDstImageSize.width = (int)Math.Max(1.0f, frameWidth);
            oDstImageSize.height = (int)Math.Max(1.0f, frameHeight);
            apDstImage[0] = src;

            /***************************
			*   Output		
			***************************/
            // Forward DCT
            for (int i = 0; i < oFrameHeader.nComponents; ++i)
            {
                compression.DCTQuantFwd8x8LS(apDstImage[i], apdDCT[i], aDstSize[i], pdQuantizationTables[oFrameHeader.aQuantizationTableSelector[i]]);
            }
            // Huffman Encoding           
            int nScanLength = 0;
            int nTempSize = JPEGCompression.EncodeHuffmanGetSize(aDstSize[0], oFrameHeader.nComponents);
            CudaDeviceVariable<byte> pJpegEncoderTemp = new CudaDeviceVariable<byte>(nTempSize);

            JPEGCompression.EncodeHuffmanScan(apdDCT[0], 0, oScanHeader.nSs, oScanHeader.nSe, oScanHeader.nA >> 4, oScanHeader.nA & 0x0f, pdScan, ref nScanLength, apHuffmanDCTableEnc[0], apHuffmanACTableEnc[0], aDstSize[0], pJpegEncoderTemp);
            // Write JPEG to byte array, as in original sample code       

            oFrameHeader.nWidth = (ushort)oDstImageSize.width;
            oFrameHeader.nHeight = (ushort)oDstImageSize.height;

            WriteFile(aFilename, nScanLength);
            //cleanup:
            pJpegEncoderTemp.Dispose();
            pdQuantizationTables[1].Dispose();
            pdQuantizationTables[0].Dispose();
            src.Dispose();
        }

        private void WriteFile(string aFilename, int nScanLength)
        {
            int pos = 0;
            JpegNPP.writeMarker(JpegNPP.SOI, pDstOutput, ref pos);
            JpegNPP.writeJFIFTag(pDstOutput, ref pos);
            JpegNPP.writeQuantizationTable(aQuantizationTables[0], pDstOutput, ref pos);
            JpegNPP.writeQuantizationTable(aQuantizationTables[1], pDstOutput, ref pos);
            JpegNPP.writeFrameHeader(oFrameHeader, pDstOutput, ref pos);
            JpegNPP.writeHuffmanTable(aHuffmanTables[0], pDstOutput, ref pos);
            JpegNPP.writeHuffmanTable(aHuffmanTables[1], pDstOutput, ref pos);
            JpegNPP.writeHuffmanTable(aHuffmanTables[2], pDstOutput, ref pos);
            JpegNPP.writeHuffmanTable(aHuffmanTables[3], pDstOutput, ref pos);
            JpegNPP.writeScanHeader(oScanHeader, pDstOutput, ref pos);

            pdScan.CopyToHost(pDstOutput, 0, pos, nScanLength);

            pos += nScanLength;
            JpegNPP.writeMarker(JpegNPP.EOI, pDstOutput, ref pos);

            FileStream fs = new FileStream(aFilename, FileMode.Create, FileAccess.Write);
            fs.Write(pDstOutput, 0, pos);
            fs.Close();
            fs.Dispose();
        }

        public void Clean()
        {
            for (int i = 0; i < oFrameHeader.nComponents; ++i)
            {
                JPEGCompression.EncodeHuffmanSpecFree(apHuffmanDCTableEnc[i]);
                JPEGCompression.EncodeHuffmanSpecFree(apHuffmanACTableEnc[i]);
                apdDCT[i].Dispose();
            }                    
            pdScan.Dispose();           
            compression.Dispose();
        }
    }
}
