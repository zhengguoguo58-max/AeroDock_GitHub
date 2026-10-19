using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;


namespace GlassDock
{

    // ==============================
    // 高分辨率图标提取
    // 使用 IShellItemImageFactory 获取256px图标
    // 解决 Dock 图标放大后模糊的问题
    // ==============================


    public static class IconHelper
    {


        // 默认提取尺寸

        public const int DefaultSize =
            256;




        // ==============================
        // 判断已有图标是否过小
        // ==============================


        public static bool IsTooSmall(
            string path,
            int minSize)
        {

            try
            {


                if(string.IsNullOrWhiteSpace(path) ||
                   !File.Exists(path))
                {

                    return true;

                }



                using(Image image =
                    Image.FromFile(path))
                {

                    return image.Width < minSize ||
                           image.Height < minSize;

                }


            }
            catch
            {

                return true;

            }


        }




        // ==============================
        // 提取高分辨率图标（默认256）
        // 返回PNG路径，失败返回空
        // ==============================


        public static string Extract(
            string path)
        {

            return Extract(
                path,
                DefaultSize);

        }




        public static string Extract(
            string path,
            int size)
        {

            try
            {


                if(string.IsNullOrWhiteSpace(path))
                    return "";



                string folder =
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Icons");



                if(!Directory.Exists(folder))
                {

                    Directory.CreateDirectory(folder);

                }




                // 确定性缓存文件名
                // 同一来源永远对应同一文件，避免产生大量垃圾图标

                string output =
                    Path.Combine(
                        folder,
                        GetKey(path)
                        +
                        "_"
                        +
                        size
                        +
                        ".png");




                // 命中缓存直接返回

                if(File.Exists(output))
                {

                    return output;

                }




                using(Bitmap? bitmap =
                    GetShellItemImage(path, size))
                {

                    if(bitmap == null)
                        return "";



                    bitmap.Save(
                        output,
                        ImageFormat.Png);

                }




                return output;


            }
            catch
            {

                return "";

            }


        }

        // ==============================
        // Win32 / COM 互操作
        // ==============================


        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {

            public int cx;

            public int cy;

        }




        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {

            public int bmType;

            public int bmWidth;

            public int bmHeight;

            public int bmWidthBytes;

            public ushort bmPlanes;

            public ushort bmBitsPixel;

            public IntPtr bmBits;

        }




        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {

            public int biSize;

            public int biWidth;

            public int biHeight;

            public ushort biPlanes;

            public ushort biBitCount;

            public int biCompression;

            public int biSizeImage;

            public int biXPelsPerMeter;

            public int biYPelsPerMeter;

            public int biClrUsed;

            public int biClrImportant;

        }




        [StructLayout(LayoutKind.Sequential)]
        private struct DIBSECTION
        {

            public BITMAP dsBm;

            public BITMAPINFOHEADER dsBmih;

            public int dsBitfields0;

            public int dsBitfields1;

            public int dsBitfields2;

            public IntPtr dshSection;

            public int dsOffset;

        }




        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {

            [PreserveSig]
            int GetImage(
                SIZE size,
                int flags,
                out IntPtr phbm);

        }




        private static readonly Guid IID_IShellItemImageFactory =
            new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");




        // 只返回图标，避免返回不透明的缩略图

        private const int SIIGBF_ICONONLY =
            0x4;




        // 允许返回大于请求尺寸的图标

        private const int SIIGBF_BIGGERSIZEOK =
            0x1;




        [DllImport(
            "shell32.dll",
            CharSet = CharSet.Unicode,
            PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);




        [DllImport("gdi32.dll")]
        private static extern int GetObject(
            IntPtr h,
            int c,
            ref DIBSECTION pv);




        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(
            IntPtr hdc);




        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(
            IntPtr hdc,
            IntPtr h);




        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(
            IntPtr hdc);




        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(
            IntPtr hObject);




        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(
            IntPtr hdcDest,
            int xDest,
            int yDest,
            int width,
            int height,
            IntPtr hdcSrc,
            int xSrc,
            int ySrc,
            int rop);




        private const int SRCCOPY =
            0x00CC0020;

        // ==============================
        // 通过 Shell 获取图标位图
        // ==============================


        private static Bitmap? GetShellItemImage(
            string path,
            int size)
        {

            IShellItemImageFactory? factory =
                null;



            IntPtr hBitmap =
                IntPtr.Zero;



            try
            {


                Guid iid =
                    IID_IShellItemImageFactory;



                int hr =
                    SHCreateItemFromParsingName(
                        path,
                        IntPtr.Zero,
                        ref iid,
                        out factory);



                if(hr != 0 ||
                   factory == null)
                {

                    return null;

                }




                SIZE sz =
                    new SIZE();



                sz.cx = size;

                sz.cy = size;



                hr =
                    factory.GetImage(
                        sz,
                        SIIGBF_ICONONLY |
                        SIIGBF_BIGGERSIZEOK,
                        out hBitmap);



                if(hr != 0 ||
                   hBitmap == IntPtr.Zero)
                {

                    return null;

                }




                return FromHBitmap(hBitmap);


            }
            catch
            {

                return null;

            }
            finally
            {

                if(hBitmap != IntPtr.Zero)
                {

                    DeleteObject(hBitmap);

                }



                if(factory != null)
                {

                    Marshal.ReleaseComObject(factory);

                }

            }


        }




        // ==============================
        // HBITMAP 转 Bitmap
        // Shell 返回的是32bpp DIB段
        // 直接复制原始位可完整保留Alpha通道
        // ==============================


        private static Bitmap? FromHBitmap(
            IntPtr hBitmap)
        {

            DIBSECTION ds =
                new DIBSECTION();



            int got =
                GetObject(
                    hBitmap,
                    Marshal.SizeOf(typeof(DIBSECTION)),
                    ref ds);



            if(got == 0)
            {

                return null;

            }




            int width =
                ds.dsBm.bmWidth;



            int height =
                ds.dsBm.bmHeight;



            // 负高度表示自上而下的位图

            bool topDown =
                height < 0;



            if(topDown)
            {

                height = -height;

            }



            if(width <= 0 ||
               height <= 0)
            {

                return null;

            }




            // 真彩色DIB段：按原始位复制

            if(got >= Marshal.SizeOf(typeof(DIBSECTION)) &&
               ds.dsBmih.biSize == 40 &&
               ds.dsBmih.biBitCount == 32 &&
               ds.dsBm.bmBits != IntPtr.Zero)
            {

                Bitmap? direct =
                    CopyBits(
                        ds.dsBm.bmBits,
                        width,
                        height,
                        topDown);



                if(direct != null)
                {

                    return direct;

                }

            }




            // 其他情况走GDI拷贝（会丢失Alpha）

            return BlitBitmap(
                hBitmap,
                width,
                height);


        }




        // ==============================
        // 复制DIB位数据（BGRA，非预乘）
        // ==============================


        private static Bitmap? CopyBits(
            IntPtr bits,
            int width,
            int height,
            bool topDown)
        {

            int stride =
                width * 4;



            Bitmap result =
                new Bitmap(
                    width,
                    height,
                    PixelFormat.Format32bppArgb);



            BitmapData data =
                result.LockBits(
                    new Rectangle(
                        0,
                        0,
                        width,
                        height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);



            try
            {


                byte[] row =
                    new byte[stride];



                for(int y = 0; y < height; y++)
                {


                    int sourceY =
                        topDown
                            ? y
                            : height - 1 - y;



                    Marshal.Copy(
                        IntPtr.Add(
                            bits,
                            sourceY * stride),
                        row,
                        0,
                        stride);



                    Marshal.Copy(
                        row,
                        0,
                        IntPtr.Add(
                            data.Scan0,
                            y * data.Stride),
                        stride);

                }


            }
            finally
            {

                result.UnlockBits(data);

            }




            return result;


        }




        // ==============================
        // GDI 拷贝兜底
        // ==============================


        private static Bitmap? BlitBitmap(
            IntPtr hBitmap,
            int width,
            int height)
        {

            Bitmap result =
                new Bitmap(
                    width,
                    height,
                    PixelFormat.Format32bppArgb);



            using(Graphics graphics =
                Graphics.FromImage(result))
            {

                IntPtr hdcDest =
                    graphics.GetHdc();



                IntPtr hdcSrc =
                    CreateCompatibleDC(hdcDest);



                IntPtr old =
                    SelectObject(hdcSrc, hBitmap);



                BitBlt(
                    hdcDest,
                    0,
                    0,
                    width,
                    height,
                    hdcSrc,
                    0,
                    0,
                    SRCCOPY);



                SelectObject(hdcSrc, old);



                DeleteDC(hdcSrc);



                graphics.ReleaseHdc(hdcDest);

            }




            return result;


        }




        // ==============================
        // 由来源路径生成稳定缓存键
        // ==============================


        private static string GetKey(
            string path)
        {

            using(MD5 md5 =
                MD5.Create())
            {

                byte[] hash =
                    md5.ComputeHash(
                        Encoding.Unicode.GetBytes(
                            path.ToLowerInvariant()));



                StringBuilder builder =
                    new StringBuilder();



                foreach(byte b in hash)
                {

                    builder.Append(
                        b.ToString("x2"));

                }



                return builder.ToString();

            }


        }

    }

}
