﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Timers;
using System.Windows;
using System.Windows.Forms;
using liblogtiny;
using libpaperang;
using libpaperang.Interfaces;
using libpaperang.Main;

namespace paperangapp {
	public partial class MainWindow : Window {
		private ILogTiny logger;
		private BaseTypes.Connection mmjcx=BaseTypes.Connection.USB;
		private BaseTypes.Model mmjmd=BaseTypes.Model.None;
		private IPrinter prtr=new USB(BaseTypes.Model.None); // T1 used as a generic, prtr used soley for the PrinterAvailable attr. all paperang devices tested report the exact same USB identifiers.
		private Paperang mmj=null;
		private System.Timers.Timer usbpoll;
		//private uint dThresh=127;
		private enum AppState {
			UnInitNoDev,
			UnInitDev,
			InitDev
		};
		private AppState state=AppState.UnInitDev;
		// TODO: is it out of scope for this library to provide functionality for printing bitmap data?
		public MainWindow() {
			InitializeComponent();
			logger = new LUITextbox();
			gMain.DataContext = (LUITextbox)logger;
			logger.Info("Application started");
			usbpoll = new System.Timers.Timer(200) {
				AutoReset = true
			};
			usbpoll.Elapsed += EvtUsbPoll;
			usbpoll.Start();
			logger.Verbose("USB presence interval event started");
		}

		private void EvtUsbPoll(object sender, ElapsedEventArgs e) => _ = Dispatcher.BeginInvoke(new invDgtUsbPoll(DgtUsbPoll));
		private delegate void invDgtUsbPoll();
		private void DgtUsbPoll() {
			try {
				if(state == AppState.UnInitNoDev && prtr.PrinterAvailable) {
					btInitUSB.IsEnabled = true;
					state = AppState.UnInitDev;
					logger.Info("Printer plugged in");
				} else if(state == AppState.UnInitDev && !prtr.PrinterAvailable) {
					btInitUSB.IsEnabled = false;
					state = AppState.UnInitNoDev;
					logger.Info("Printer unplugged");
				} else if(state == AppState.InitDev && !prtr.PrinterAvailable) {
					logger.Info("Printer unplugged while initialised");
					USBDeInit();
				}

			} catch(Exception) {
			} finally { }
		}

		~MainWindow() {
			logger.Warn("Application closing");
			if(mmj != null)
				USBDeInit();
		}
		private void BtClearLog_Click(object sender, RoutedEventArgs e) =>
			logger.Raw("!clearlog");
		private void SetP1_Click(object sender, RoutedEventArgs e) {
			mmjmd = BaseTypes.Model.P1;
			logger.Info("Model type set to P1 or P1S");
		}
		private void SetP2_Click(object sender, RoutedEventArgs e) {
			mmjmd = BaseTypes.Model.P2;
			logger.Info("Model type set to P2 or P2S");
		}
		private void SetT1_Click(object sender, RoutedEventArgs e) {
			mmjmd = BaseTypes.Model.T1;
			logger.Info("Model type set to T1");
		}
		private void BtInitUSB_Click(object sender, RoutedEventArgs e) => USBInit();
		private void USBInit() {
			mmj = new Paperang(mmjcx, mmjmd);
			mmj.SetLogContext(logger);
			logger.Verbose("# printers found: " + mmj.Printer.AvailablePrinters.Count);
			if(!mmj.Printer.PrinterAvailable) {
				logger.Error("Couldn't initialise printer as none is connected");
				return;
			}
			logger.Info("USB Initialising");
			mmj.Initialise();
			logger.Debug("PrinterInitialised? " + mmj.Printer.PrinterInitialised);
			logger.Debug("Printer initialised and ready");
			state = AppState.InitDev;
			btInitUSB.IsEnabled = false;
			btDeInitUSB.IsEnabled = true;
			gbOtherFunc.IsEnabled = true;
			gbPrinting.IsEnabled = true;
		}
		private void BtDeInitUSB_Click(object sender, RoutedEventArgs e) => USBDeInit();
		private void USBDeInit() {
			logger.Info("De-initialising printer");
			mmj.Printer.ClosePrinter();
			mmj.Printer.Deinitialise();
			mmj = null;
			state = AppState.UnInitDev;
			try {
				gbPrinting.IsEnabled = false;
				gbOtherFunc.IsEnabled = false;
				btInitUSB.IsEnabled = true;
				btDeInitUSB.IsEnabled = false;
			} catch(Exception) {
			} finally { }
		}
		private void BtFeed_Click(object sender, RoutedEventArgs e) => mmj.Feed((uint)slFeedTime.Value);
		private void BtPrintText_Click(object sender, RoutedEventArgs e) {
			if(!(txInput.Text.Length > 0)) {
				logger.Warn("PrintText event but nothing to print.");
				return;
			}
			Font fnt=new Font(txFont.Text, int.Parse(txSzF.Text));
			Graphics g=Graphics.FromImage(new Bitmap(mmj.Printer.LineWidth*8, 1));
			System.Drawing.Size szText = TextRenderer.MeasureText(g, txInput.Text, fnt);
			//SizeF szText=g.MeasureString(txInput.Text, fnt);
			g.Dispose();
			Bitmap b=new Bitmap(mmj.Printer.LineWidth*8, (int)szText.Height);
			g = Graphics.FromImage(b);
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
			g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
			g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
			TextRenderer.DrawText(g, txInput.Text, fnt, new System.Drawing.Point(0, 0), Color.Black);
			g.Flush();
			PrintBitmap(b);
			g.Dispose();
			b.Dispose();
		}
		private void BtPrintImage_Click(object sender, RoutedEventArgs e) {
			logger.Debug("Invoking file selection dialogue.");
			OpenFileDialog r = new OpenFileDialog {
				Title="Select 1 (one) image file",
				Multiselect=false,
				Filter="PNG files|*.png|JPEG files|*.jpg;*.jpeg|Jraphics Interchange Format files|*.gif|Bitte-Mappe files|*.bmp|All of the above|*.jpg;*.jpeg;*.png;*.gif;*.bmp",
				AutoUpgradeEnabled=true
			};
			if(r.ShowDialog() == System.Windows.Forms.DialogResult.Cancel) {
				logger.Warn("PrintImage event but no file was selected.");
				r.Dispose();
				return;
			}
			logger.Debug("Loading image for print");
			Image _=Image.FromFile(r.FileName);
			logger.Debug("Loaded image " + r.FileName);
			r.Dispose();
			logger.Debug("Disposed of dialog");
			Bitmap bimg=new Bitmap(_, mmj.Printer.LineWidth*8,
				(int)(mmj.Printer.LineWidth*8*(double)((double)_.Height/(double)_.Width)));
			logger.Debug("Loaded image as Bitmap");
			_.Dispose();
			logger.Debug("Disposed of Image");
			PrintBitmap(bimg);
			bimg.Dispose();
		}
		private void PrintBitmap(Bitmap bimg) {
			bimg = CopyToBpp(bimg);
			logger.Debug("Converted Bitmap to 1-bit");
			int hSzImg=bimg.Height;
			byte[] iimg = new byte[hSzImg*mmj.Printer.LineWidth];
			byte[] img;
			using(MemoryStream s = new MemoryStream()) {
				bimg.Save(s, ImageFormat.Bmp);
				img = s.ToArray();
			}
			logger.Debug("Got bitmap's bytes");
			bimg.Dispose();
			logger.Debug("Disposed of Bitmap");
			int startoffset=img.Length-(hSzImg*mmj.Printer.LineWidth);
			logger.Debug("Processing bytes with offset " + startoffset);
			for(int h = 0; h < hSzImg; h++) {
				for(int w = 0; w < mmj.Printer.LineWidth; w++) {
					iimg[(mmj.Printer.LineWidth * (hSzImg - 1 - h)) + (mmj.Printer.LineWidth - 1 - w)] = (byte)~
						img[startoffset + (mmj.Printer.LineWidth * h) + (mmj.Printer.LineWidth - 1 - w)];
				}
			}
			logger.Debug($"Have {img.Length} bytes of print data ({mmj.Printer.LineWidth * 8}x{hSzImg}@1bpp)");
			mmj.PrintBytes(iimg, false);
		}
		#region GDIBitmap1bpp
		static Bitmap CopyToBpp(Bitmap b) {
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
