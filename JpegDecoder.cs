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
    
    public class JpegDecoder
    {
        JPEGCompression compression;
        JpegNPP.FrameHeader oFrameHeader;
        JpegNPP.QuantizationTable[] aQuantizationTables;
        CudaDeviceVariable<byte>[] pdQuantizationTables;
        JpegNPP.HuffmanTable[] aHuffmanTables;
        JpegNPP.ScanHeader oScanHeader;
        NppiDecodeHuffmanSpec[] apHuffmanDCTableDec ;
        NppiDecodeHuffmanSpec[] apHuffmanACTableDec ;
        public JpegDecoder()
        {
            compression = new JPEGCompression();
            oFrameHeader = new JpegNPP.FrameHeader();
            oFrameHeader.aComponentIdentifier = new byte[3];
            oFrameHeader.aSamplingFactors = new byte[3];
            oFrameHeader.aQuantizationTableSelector = new byte[3];

            aQuantizationTables = new JpegNPP.QuantizationTable[4];
            aQuantizationTables[0] = new JpegNPP.QuantizationTable();
            aQuantizationTables[1] = new JpegNPP.QuantizationTable();
            aQuantizationTables[2] = new JpegNPP.QuantizationTable();
            aQuantizationTables[3] = new JpegNPP.QuantizationTable();

            pdQuantizationTables = new CudaDeviceVariable<byte>[4];
            pdQuantizationTables[0] = new CudaDeviceVariable<byte>(64);
            pdQuantizationTables[1] = new CudaDeviceVariable<byte>(64);
            pdQuantizationTables[2] = new CudaDeviceVariable<byte>(64);
            pdQuantizationTables[3] = new CudaDeviceVariable<byte>(64);

            aHuffmanTables = new JpegNPP.HuffmanTable[4];
            aHuffmanTables[0] = new JpegNPP.HuffmanTable();
            aHuffmanTables[1] = new JpegNPP.HuffmanTable();
            aHuffmanTables[2] = new JpegNPP.HuffmanTable();
            aHuffmanTables[3] = new JpegNPP.HuffmanTable();

            oScanHeader = new JpegNPP.ScanHeader();
            oScanHeader.aComponentSelector = new byte[3];
            oScanHeader.aHuffmanTablesSelector = new byte[3];

             apHuffmanDCTableDec = new NppiDecodeHuffmanSpec[3];
             apHuffmanACTableDec = new NppiDecodeHuffmanSpec[3];

            
        }
        public  Bitmap LoadJpeg(string aFilename)
        {            
            byte[] pJpegData = File.ReadAllBytes(aFilename);
            int nInputLength = pJpegData.Length;

            // Check if this is a valid JPEG file
            int nPos = 0;
            int nMarker = JpegNPP.nextMarker(pJpegData, ref nPos, nInputLength);

            if (nMarker != JpegNPP.SOI)
            {
                throw new ArgumentException(aFilename + " is not a JPEG file.");
            }

            nMarker = JpegNPP.nextMarker(pJpegData, ref nPos, nInputLength);          
           

            int nMCUBlocksH = 0;
            int nMCUBlocksV = 0;

            int nRestartInterval = -1;

            NppiSize[] aSrcSize = new NppiSize[3];

            short[][] aphDCT = new short[3][];
            NPPImage_16sC1[] apdDCT = new NPPImage_16sC1[3];
            int[] aDCTStep = new int[3];

            NPPImage_8uC1[] apSrcImage = new NPPImage_8uC1[3];
            int[] aSrcImageStep = new int[3];

            NPPImage_8uC1[] apDstImage = new NPPImage_8uC1[3];
            int[] aDstImageStep = new int[3];
            NppiSize[] aDstSize = new NppiSize[3];

            //Same read routine as in NPP JPEG sample from Nvidia
            while (nMarker != -1)
            {
                if (nMarker == JpegNPP.SOI)
                {
                    // Embeded Thumbnail, skip it
                    int nNextMarker = JpegNPP.nextMarker(pJpegData, ref nPos, nInputLength);

                    while (nNextMarker != -1 && nNextMarker != JpegNPP.EOI)
                    {
                        nNextMarker = JpegNPP.nextMarker(pJpegData, ref nPos, nInputLength);
                    }
                }

                if (nMarker == JpegNPP.DRI)
                {
                    JpegNPP.readRestartInterval(pJpegData, ref nPos, ref nRestartInterval);
                }

                if ((nMarker == JpegNPP.SOF0) | (nMarker == JpegNPP.SOF2))
                {
                    //Assert Baseline for this Sample
                    //Note: NPP does support progressive jpegs for both encode and decode
                    if (nMarker != JpegNPP.SOF0)
                    {
                        this.Clean();                        
                        return  new Bitmap(aFilename);
                        //throw new ArgumentException(aFilename + " is not a Baseline-JPEG file.");
                    }

                    // Baseline or Progressive Frame Header
                    JpegNPP.readFrameHeader(pJpegData, ref nPos, ref oFrameHeader);
                    //Console.WriteLine("Image Size: " + oFrameHeader.nWidth + "x" + oFrameHeader.nHeight + "x" + (int)(oFrameHeader.nComponents));

                    //Assert 3-Channel Image for this Sample
                    if (oFrameHeader.nComponents != 3)
                    {
                        return new Bitmap(aFilename);
                        this.Clean();
                        //throw new ArgumentException(aFilename + " is not a three channel JPEG file.");
                    }

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

                        aSrcSize[i].width = oBlocks.width * 8;
                        aSrcSize[i].height = oBlocks.height * 8;

                        // Allocate Memory
                        apdDCT[i] = new NPPImage_16sC1(oBlocks.width * 64, oBlocks.height);
                        aDCTStep[i] = apdDCT[i].Pitch;

                        apSrcImage[i] = new NPPImage_8uC1(aSrcSize[i].width, aSrcSize[i].height);
                        aSrcImageStep[i] = apSrcImage[i].Pitch;

                        aphDCT[i] = new short[aDCTStep[i] * oBlocks.height];
                    }
                }

                if (nMarker == JpegNPP.DQT)
                {
                    // Quantization Tables
                    JpegNPP.readQuantizationTables(pJpegData, ref nPos, aQuantizationTables);
                }

                if (nMarker == JpegNPP.DHT)
                {
                    // Huffman Tables
                    JpegNPP.readHuffmanTables(pJpegData, ref nPos, aHuffmanTables);
                }

                if (nMarker == JpegNPP.SOS)
                {
                    // Scan
                    JpegNPP.readScanHeader(pJpegData, ref nPos, ref oScanHeader);
                    nPos += 6 + oScanHeader.nComponents * 2;

                    int nAfterNextMarkerPos = nPos;
                    int nAfterScanMarker = JpegNPP.nextMarker(pJpegData, ref nAfterNextMarkerPos, nInputLength);

                    if (nRestartInterval > 0)
                    {
                        while (nAfterScanMarker >= JpegNPP.RST0 && nAfterScanMarker <= JpegNPP.RST7)
                        {
                            // This is a restart marker, go on
                            nAfterScanMarker = JpegNPP.nextMarker(pJpegData, ref nAfterNextMarkerPos, nInputLength);
                        }
                    }

                    for (int i = 0; i < 3; ++i)
                    {
                        apHuffmanDCTableDec[i] = JPEGCompression.DecodeHuffmanSpecInitAllocHost(aHuffmanTables[(oScanHeader.aHuffmanTablesSelector[i] >> 4)].aCodes, NppiHuffmanTableType.nppiDCTable);
                        apHuffmanACTableDec[i] = JPEGCompression.DecodeHuffmanSpecInitAllocHost(aHuffmanTables[(oScanHeader.aHuffmanTablesSelector[i] & 0x0f) + 2].aCodes, NppiHuffmanTableType.nppiACTable);
                    }

                    byte[] img = new byte[nAfterNextMarkerPos - nPos - 2];
                    Buffer.BlockCopy(pJpegData, nPos, img, 0, nAfterNextMarkerPos - nPos - 2);
                    JPEGCompression.DecodeHuffmanScanHost(img, nRestartInterval, oScanHeader.nSs, oScanHeader.nSe, oScanHeader.nA >> 4, oScanHeader.nA & 0x0f, aphDCT[0], aphDCT[1], aphDCT[2], aDCTStep, apHuffmanDCTableDec, apHuffmanACTableDec, aSrcSize);
                }

                nMarker = JpegNPP.nextMarker(pJpegData, ref nPos, nInputLength);
            }
            // Copy DCT coefficients and Quantization Tables from host to device
            for (int i = 0; i < 4; ++i)
            {
                pdQuantizationTables[i].CopyToDevice(aQuantizationTables[i].aTable);
            }

            for (int i = 0; i < 3; ++i)
            {
                apdDCT[i].CopyToDevice(aphDCT[i], aDCTStep[i]);
            }

            // Inverse DCT
            for (int i = 0; i < 3; ++i)
            {
                compression.DCTQuantInv8x8LS(apdDCT[i], apSrcImage[i], aSrcSize[i], pdQuantizationTables[oFrameHeader.aQuantizationTableSelector[i]]);
            }

            //Alloc final image
            NPPImage_8uC3 res = new NPPImage_8uC3(apSrcImage[0].Width, apSrcImage[0].Height);

            //Copy Y color plane to first channel
            apSrcImage[0].Copy(res, 0);

            //Cb anc Cr channel might be smaller
            if ((oFrameHeader.aSamplingFactors[0] & 0x0f) == 1 && oFrameHeader.aSamplingFactors[0] >> 4 == 1)
            {
                //Color planes are of same size as Y channel
                apSrcImage[1].Copy(res, 1);
                apSrcImage[2].Copy(res, 2);
            }
            else
            {
                //rescale color planes to full size
                double scaleX = oFrameHeader.aSamplingFactors[0] & 0x0f;
                double scaleY = oFrameHeader.aSamplingFactors[0] >> 4;

                apSrcImage[1].ResizeSqrPixel(apSrcImage[0], scaleX, scaleY, 0, 0, InterpolationMode.Lanczos);
                apSrcImage[0].Copy(res, 1);
                apSrcImage[2].ResizeSqrPixel(apSrcImage[0], scaleX, scaleY, 0, 0, InterpolationMode.Lanczos);
                apSrcImage[0].Copy(res, 2);
            }
            

            //Convert from YCbCr to BGR
            res.ColorTwist(JpegNPP.YCbCrToBgr);

            Bitmap bmp = new Bitmap(apSrcImage[0].Width, apSrcImage[0].Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            res.CopyToHost(bmp);

            //Cleanup:
            res.Dispose();
            apSrcImage[2].Dispose();
            apSrcImage[1].Dispose();
            apSrcImage[0].Dispose();

            apdDCT[2].Dispose();
            apdDCT[1].Dispose();
            apdDCT[0].Dispose();

           

            return bmp;
        }
        public void Clean()
        {
            for (int i = 0; i < 3; ++i)
            {
                JPEGCompression.DecodeHuffmanSpecFreeHost(apHuffmanDCTableDec[i]);
                JPEGCompression.DecodeHuffmanSpecFreeHost(apHuffmanACTableDec[i]);
            }
            pdQuantizationTables[0].Dispose();
            pdQuantizationTables[1].Dispose();
            pdQuantizationTables[2].Dispose();
            pdQuantizationTables[3].Dispose();
            compression.Dispose();
        }
    }
}
