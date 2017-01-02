using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;
using ManagedCuda.NPP;

namespace NPPJpegCompression
{
	public partial class Form1 : Form
	{

        JpegEncoder jEncoder;
        JpegDecoder jDecoder;
        public Form1()
		{
			InitializeComponent();            
            jDecoder = new JpegDecoder();
            
            this.FormClosed += Form1_FormClosed;
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if(jEncoder!=null)
               jEncoder.Clean();
            if (jDecoder != null)
                jDecoder.Clean();
        }

        private void btn_OpenNPP_Click(object sender, EventArgs e)
		{
			OpenFileDialog ofd = new OpenFileDialog();
			ofd.Filter = "JPEG files|*.jpg";
			if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
				return;

			try
            {
                //pic_Image.Image = JpegNPP.LoadJpeg(ofd.FileName);
                pic_Image.Image = jDecoder.LoadJpeg(ofd.FileName);
                initJpegEncoder();
            }
            catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

        private void initJpegEncoder()
        {
            int ch = pic_Image.Image.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb ? 3 : pic_Image.Image.PixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed ? 1 : 3;
            if (jEncoder != null)
                jEncoder.Clean();
            jEncoder = new JpegEncoder((ushort)pic_Image.Image.Width, (ushort)pic_Image.Image.Height, 75, ch);
        }

        private void btn_openImageNet_Click(object sender, EventArgs e)
		{
			OpenFileDialog ofd = new OpenFileDialog();
			ofd.Filter = "JPEG files|*.*";
			if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
				return;

			pic_Image.Image = new Bitmap(ofd.FileName);
            initJpegEncoder();
        }

		private void trk_Size_Scroll(object sender, EventArgs e)
		{
			txt_Resize.Text = trk_Size.Value.ToString() + " %";
		}

		private void btn_SaveJpegNPP_Click(object sender, EventArgs e)
		{
			if ((Bitmap)pic_Image.Image == null) return;

			SaveFileDialog sfd = new SaveFileDialog();
			sfd.Filter = "JPEG files|*.jpg";

			if (sfd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
				return;

			try
			{
                if (((Bitmap)pic_Image.Image).PixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed)
                {
                    jEncoder.SaveGrayJpeg(sfd.FileName, trk_JpegQuality.Value, (Bitmap)pic_Image.Image);
                }
                else if (((Bitmap)pic_Image.Image).PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb)
                {
                    jEncoder.SaveColorJpeg(sfd.FileName, trk_JpegQuality.Value, (Bitmap)pic_Image.Image);
                }
                else
                {
                    MessageBox.Show("不支持的颜色格式，当前只支持24位RGB和8位灰度图");
                }
            }
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

		private void trk_JpegQuality_Scroll(object sender, EventArgs e)
		{
			txt_JpegQuality.Text = trk_JpegQuality.Value.ToString();
		}

		private void btn_Resize_Click(object sender, EventArgs e)
		{
			if ((Bitmap)pic_Image.Image == null) return;

			Bitmap bmp = (Bitmap)pic_Image.Image;
			int w = bmp.Width;
			int h = bmp.Height;

			if ((w <= 16 || h <= 16) && trk_Size.Value < 100)
			{
				MessageBox.Show("Image is too small for resizing.");
				return;
			}
			
			int newW = (int)(trk_Size.Value / 100.0f * w);
			int newH = (int)(trk_Size.Value / 100.0f * h);

			if (newW % 16 != 0)
			{
				newW = newW - (newW % 16);
			}
			if (newW < 16) newW = 16;
			
			if (newH % 16 != 0)
			{
				newH = newH - (newH % 16);
			}
			if (newH < 16) newH = 16;
			
			double ratioW = newW / (double)w;
			double ratioH = newH / (double)h;

			if (ratioW == 1 && ratioH == 1)
				return;

			if (bmp.PixelFormat != System.Drawing.Imaging.PixelFormat.Format24bppRgb)
			{
				MessageBox.Show("Only three channel color images are supported!");
				return;
			}

			NPPImage_8uC3 imgIn = new NPPImage_8uC3(w, h);
			NPPImage_8uC3 imgOut = new NPPImage_8uC3(newW, newH);

			InterpolationMode interpol = InterpolationMode.SuperSampling;
			if (ratioH >= 1 || ratioW >= 1)
				interpol = InterpolationMode.Lanczos;

			imgIn.CopyToDevice(bmp);
			imgIn.ResizeSqrPixel(imgOut, ratioW, ratioH, 0, 0, interpol);
			Bitmap bmpRes = new Bitmap(newW, newH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
			imgOut.CopyToHost(bmpRes);
			pic_Image.Image = bmpRes;

			imgIn.Dispose();
			imgOut.Dispose();
		}

        void test()
        {
            if (jEncoder != null)
                jEncoder.Clean();
            string spd = "";
            jEncoder = new JpegEncoder(5120,5120, 75, 1);
            string src = @"E:\VM\test\src\t-20160527113656080_5.bmp";
            Bitmap img;
            int n =int.Parse( this.textBox1.Text);
            List<string> filelist = new List<string>();
            List<string> filelist2 = new List<string>();
            for (int i = 0; i < n; i++)
            {
                filelist.Add(@"E:\VM\test\dst\" + i.ToString("0000") + ".jpg");
                filelist2.Add(@"E:\VM\test\dst\" + (n+i).ToString("0000") + ".jpg");
            }
            Stopwatch sw = new Stopwatch();
            sw.Start();          
            foreach(var f in filelist)
            {
                img  = new Bitmap(src);
                jEncoder.SaveGrayJpeg(f, 75, (Bitmap)img);
            }
            sw.Stop();
            spd += "cuda " + sw.ElapsedMilliseconds / 1000.0 / n+"\r\n";
            sw.Reset();



            ImageCodecInfo myImageCodecInfo;
            System.Drawing.Imaging.Encoder myEncoder;
            EncoderParameter myEncoderParameter;
            EncoderParameters myEncoderParameters;
            myImageCodecInfo = GetEncoderInfo("image/jpeg");
            myEncoder = System.Drawing.Imaging.Encoder.Quality;
            myEncoderParameters = new EncoderParameters(1);
            myEncoderParameter = new EncoderParameter(myEncoder, 75L);
            myEncoderParameters.Param[0] = myEncoderParameter;

            sw.Start();
            foreach(var f in filelist2)
            {
                img = new Bitmap(src);
                img.Save(f, myImageCodecInfo, myEncoderParameters);
            }
            sw.Stop();
            spd += "net " + sw.ElapsedMilliseconds / 1000.0 / n;
            MessageBox.Show(spd);
        }
        private static ImageCodecInfo GetEncoderInfo(String mimeType)
        {
            int j;
            ImageCodecInfo[] encoders;
            encoders = ImageCodecInfo.GetImageEncoders();
            for (j = 0; j < encoders.Length; ++j)
            {
                if (encoders[j].MimeType == mimeType)
                    return encoders[j];
            }
            return null;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            test();
        }
    }
}
