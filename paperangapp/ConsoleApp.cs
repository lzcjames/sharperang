using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Imaging.Filters;
using libpaperang;
using libpaperang.Main;
using static paperangapp.MainWindow;

namespace paperangapp {
	class ConsoleApp {

		static async Task Main(string[] args) {
			BaseTypes.Connection mmjcx = BaseTypes.Connection.USB;
			BaseTypes.Model mmjmd = BaseTypes.Model.P2;
			Paperang mmj = new Paperang(mmjcx, mmjmd);
			mmj.Initialise();
			Console.WriteLine("PrinterInitialised? " + mmj.Printer.PrinterInitialised);

			await PrintTextAsync(args[0], "Consolas", 20);
			Console.WriteLine("print:" + args[0]);

			async Task PrintTextAsync(string text, string font, int szf) {
				Font fnt=new Font(font, szf);
				TextFormatFlags tf=
				TextFormatFlags.Left |
				TextFormatFlags.NoPadding |
				TextFormatFlags.NoPrefix |
				TextFormatFlags.Top |
				TextFormatFlags.WordBreak;
				System.Drawing.Size szText = TextRenderer.MeasureText(text, fnt, new System.Drawing.Size(mmj.Printer.LineWidth*8,10000), tf);
				Bitmap b=new Bitmap(mmj.Printer.LineWidth*8, szText.Height);
				Graphics g = Graphics.FromImage(b);
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
				TextRenderer.DrawText(g, text, fnt, new System.Drawing.Point(0, 0), Color.Black, tf);
				g.Flush();
				await Task.Run(() => PrintBitmap(b, false));
				g.Dispose();
				b.Dispose();
			}
			async Task PrintBitmap(Bitmap bimg, bool dither = true) {
				byte[,] bayer4 = {
					{ 0,  8,  2, 10 },
					{ 12, 4, 14,  6 },
					{ 3, 11,  1,  9 },
					{ 15, 7, 13,  5 }
				};
				if(dither) {

					bimg = Grayscale.CommonAlgorithms.Y.Apply(bimg);
					OrderedDithering f = new
				OrderedDithering(bayer4);
					bimg = f.Apply(bimg);
				}
				bimg = CopyToBpp(bimg);

				int hSzImg=bimg.Height;
				byte[] iimg = new byte[hSzImg*mmj.Printer.LineWidth];
				byte[] img;
				using(MemoryStream s = new MemoryStream()) {
					bimg.Save(s, ImageFormat.Bmp);
					img = s.ToArray();
				}

				bimg.Dispose();

				int startoffset=img.Length-(hSzImg*mmj.Printer.LineWidth);

				for(int h = 0; h < hSzImg; h++) {
					for(int w = 0; w < mmj.Printer.LineWidth; w++) {
						iimg[(mmj.Printer.LineWidth * (hSzImg - 1 - h)) + (mmj.Printer.LineWidth - 1 - w)] = (byte)~
							img[startoffset + (mmj.Printer.LineWidth * h) + (mmj.Printer.LineWidth - 1 - w)];
					}
				}

				await mmj.PrintBytesAsync(iimg, false);
			}

			#region GDIBitmap1bpp
			Bitmap CopyToBpp(Bitmap b) {
				int w=b.Width, h=b.Height;
				IntPtr hbm = b.GetHbitmap();
				BITMAPINFO bmi = new BITMAPINFO {
					biSize=40,
					biWidth=w,
					biHeight=h,
					biPlanes=1,
					biBitCount=1,
					biCompression=BI_RGB,
					biSizeImage = (uint)(((w+7)&0xFFFFFFF8)*h/8),
					biXPelsPerMeter=1000000,
					biYPelsPerMeter=1000000
				};
				bmi.biClrUsed = 2;
				bmi.biClrImportant = 2;
				bmi.cols = new uint[256];
				bmi.cols[0] = MAKERGB(0, 0, 0);
				bmi.cols[1] = MAKERGB(255, 255, 255);
				IntPtr hbm0 = CreateDIBSection(IntPtr.Zero,ref bmi,DIB_RGB_COLORS,out _,IntPtr.Zero,0);
				IntPtr sdc = GetDC(IntPtr.Zero);
				IntPtr hdc = CreateCompatibleDC(sdc);
				_ = SelectObject(hdc, hbm);
				IntPtr hdc0 = CreateCompatibleDC(sdc);
				_ = SelectObject(hdc0, hbm0);
				_ = BitBlt(hdc0, 0, 0, w, h, hdc, 0, 0, SRCCOPY);
				Bitmap b0 = Image.FromHbitmap(hbm0);
				_ = DeleteDC(hdc);
				_ = DeleteDC(hdc0);
				_ = ReleaseDC(IntPtr.Zero, sdc);
				_ = DeleteObject(hbm);
				_ = DeleteObject(hbm0);
				return b0;
			}
		}
		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		public static extern bool DeleteObject(IntPtr hObject);
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern IntPtr GetDC(IntPtr hwnd);
		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		public static extern int DeleteDC(IntPtr hdc);
		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		public static extern int BitBlt(IntPtr hdcDst, int xDst, int yDst, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
		static int SRCCOPY = 0x00CC0020;
		[System.Runtime.InteropServices.DllImport("gdi32.dll")]
		static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint Usage, out IntPtr bits, IntPtr hSection, uint dwOffset);
		static uint BI_RGB = 0;
		static uint DIB_RGB_COLORS=0;
		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		public struct BITMAPINFO {
			public uint biSize;
			public int biWidth, biHeight;
			public short biPlanes, biBitCount;
			public uint biCompression, biSizeImage;
			public int biXPelsPerMeter, biYPelsPerMeter;
			public uint biClrUsed, biClrImportant;
			[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst=256)]
			public uint[] cols;
		}
		static uint MAKERGB(int r, int g, int b) => ((uint)(b & 255)) | ((uint)((r & 255) << 8)) | ((uint)((g & 255) << 16));
		#endregion
	}
}
		
	