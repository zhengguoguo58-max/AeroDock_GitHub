using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;


namespace GlassDock
{

    // ==========================
    // 壁纸 / 屏幕取色
    // ==========================
    // 不再依赖注册表里的壁纸文件路径：
    // 直接截取当前屏幕下半部分，缩成小图后统计主色调。
    // 动态壁纸、缓存文件被删、注册表路径失效，都能正常取色。
    // 截屏彻底失败时静默返回柔和深灰 RGB(30,30,30)，只写 Debug 日志，绝不弹窗。
    // ==========================

    public class WallpaperAnalyzer
    {

        // 缩略图尺寸：180 x 100 足够统计主色，速度也快

        private const int SampleWidth = 180;

        private const int SampleHeight = 100;


        // 兜底颜色：柔和的深色

        private static readonly System.Drawing.Color FallbackColor =
            System.Drawing.Color.FromArgb(30, 30, 30);


        // MainWindow 仍会调用这个方法，用屏幕明暗决定色调兑白比例

        public bool IsDarkBackground()
        {

            try
            {

                if(!TryCaptureScreenThumbnail(
                    out Bitmap? thumbnail,
                    out double brightness) ||
                   thumbnail == null)
                {
                    return false;
                }


                using(thumbnail)
                {

                    return brightness < 120;

                }

            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "WallpaperAnalyzer: 判断屏幕明暗失败: "
                    + ex.Message);

                return false;

            }

        }


        // 屏幕平均主色：
        // 成功 -> 截屏主色
        // 失败 -> RGB(30,30,30)

        public System.Windows.Media.Color GetAverageScreenColor()
        {

            try
            {

                if(TryCaptureScreenThumbnail(
                    out Bitmap? thumbnail,
                    out _) &&
                   thumbnail != null)
                {

                    using(thumbnail)
                    {

                        System.Drawing.Color main =
                            PickMainColor(thumbnail);


                        Debug.WriteLine(
                            "WallpaperAnalyzer: 屏幕取色成功 RGB("
                            + main.R + ","
                            + main.G + ","
                            + main.B + ")");


                        return System.Windows.Media.Color.FromRgb(
                            main.R,
                            main.G,
                            main.B);

                    }

                }

            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "WallpaperAnalyzer: 屏幕取色失败: "
                    + ex.Message);

            }


            Debug.WriteLine(
                "WallpaperAnalyzer: 使用兜底色 RGB(30,30,30)");


            return System.Windows.Media.Color.FromRgb(
                30,
                30,
                30);

        }

        // 截取屏幕下半部分并缩成缩略图。
        // 失败时抛异常，由上层统一静默兜底。

        private static bool TryCaptureScreenThumbnail(
            out Bitmap? thumbnail,
            out double averageBrightness)
        {

            thumbnail = null;

            averageBrightness = 0;


            Rectangle bounds =
                GetPrimaryScreenBounds();


            if(bounds.Width <= 0 ||
               bounds.Height <= 0)
            {
                return false;
            }


            // 取 50% ~ 88% 的区域：
            // 是壁纸主体，同时避开最底部的 Dock / 任务栏

            int captureHeight =
                (int)Math.Round(bounds.Height * 0.38);


            if(captureHeight < 2)
                captureHeight = bounds.Height;


            int sourceTop =
                bounds.Top +
                (int)Math.Round(bounds.Height * 0.50);


            if(sourceTop + captureHeight > bounds.Bottom)
                sourceTop = bounds.Bottom - captureHeight;


            if(sourceTop < bounds.Top)
                sourceTop = bounds.Top;


            Bitmap? small = null;


            try
            {

                using(Bitmap full =
                    new Bitmap(
                        bounds.Width,
                        captureHeight,
                        PixelFormat.Format32bppArgb))
                {

                    using(Graphics fullGraphics =
                        Graphics.FromImage(full))
                    {

                        fullGraphics.CopyFromScreen(
                            bounds.Left,
                            sourceTop,
                            0,
                            0,
                            new System.Drawing.Size(
                                bounds.Width,
                                captureHeight),
                            CopyPixelOperation.SourceCopy);

                    }


                    small =
                        new Bitmap(
                            SampleWidth,
                            SampleHeight,
                            PixelFormat.Format32bppArgb);


                    using(Graphics smallGraphics =
                        Graphics.FromImage(small))
                    {

                        smallGraphics.InterpolationMode =
                            InterpolationMode.HighQualityBilinear;

                        smallGraphics.PixelOffsetMode =
                            PixelOffsetMode.HighQuality;


                        smallGraphics.DrawImage(
                            full,
                            new Rectangle(
                                0,
                                0,
                                SampleWidth,
                                SampleHeight),
                            new Rectangle(
                                0,
                                0,
                                full.Width,
                                full.Height),
                            GraphicsUnit.Pixel);

                    }


                    averageBrightness =
                        CalculateAverageBrightness(small);


                    thumbnail = small;

                    return true;

                }

            }
            catch
            {

                small?.Dispose();

                throw;

            }

        }


        // 主屏物理像素区域（多显示器、非 (0,0) 原点也能取对位置）

        private static Rectangle GetPrimaryScreenBounds()
        {

            try
            {

                System.Windows.Forms.Screen? screen =
                    System.Windows.Forms.Screen.PrimaryScreen;


                if(screen != null &&
                   screen.Bounds.Width > 0 &&
                   screen.Bounds.Height > 0)
                {
                    return screen.Bounds;
                }

            }
            catch
            {

            }


            return new Rectangle(
                0,
                0,
                Math.Max(
                    1,
                    (int)SystemParameters.PrimaryScreenWidth),
                Math.Max(
                    1,
                    (int)SystemParameters.PrimaryScreenHeight));

        }


        // 缩略图整体亮度（0 ~ 255）

        private static double CalculateAverageBrightness(
            Bitmap bitmap)
        {

            double sum = 0;

            long count = 0;


            for(int y = 0; y < bitmap.Height; y++)
            {

                for(int x = 0; x < bitmap.Width; x++)
                {

                    System.Drawing.Color c =
                        bitmap.GetPixel(x, y);


                    sum +=
                        c.R * 0.299 +
                        c.G * 0.587 +
                        c.B * 0.114;


                    count++;

                }

            }


            return count > 0
                ? sum / count
                : 0;

        }


        // 主色统计：分桶找出现最多的彩色，跳过极暗 / 极亮 / 近灰像素

        private static System.Drawing.Color PickMainColor(
            Bitmap bitmap)
        {

            const int BucketCount = 64;


            int[] bucketCount =
                new int[BucketCount];

            long[] bucketR =
                new long[BucketCount];

            long[] bucketG =
                new long[BucketCount];

            long[] bucketB =
                new long[BucketCount];


            long allR = 0;

            long allG = 0;

            long allB = 0;

            long allCount = 0;


            for(int y = 0; y < bitmap.Height; y++)
            {

                for(int x = 0; x < bitmap.Width; x++)
                {

                    System.Drawing.Color c =
                        bitmap.GetPixel(x, y);


                    allR += c.R;

                    allG += c.G;

                    allB += c.B;

                    allCount++;


                    int luma =
                        (c.R * 299 +
                         c.G * 587 +
                         c.B * 114) / 1000;


                    // 任务栏 / 窗口 / 阴影：太暗、太亮都不要

                    if(luma < 18 || luma > 242)
                        continue;


                    int max =
                        Math.Max(c.R, Math.Max(c.G, c.B));

                    int min =
                        Math.Min(c.R, Math.Min(c.G, c.B));


                    // 接近灰色：不是壁纸主色

                    if(max - min < 14)
                        continue;


                    // 每通道 2 bit，共 64 个颜色桶

                    int index =
                        ((c.R >> 6) << 4) |
                        ((c.G >> 6) << 2) |
                        (c.B >> 6);


                    bucketCount[index]++;

                    bucketR[index] += c.R;

                    bucketG[index] += c.G;

                    bucketB[index] += c.B;

                }

            }


            int bestIndex = -1;

            int bestCount = 0;


            for(int i = 0; i < BucketCount; i++)
            {

                if(bucketCount[i] > bestCount)
                {

                    bestCount = bucketCount[i];

                    bestIndex = i;

                }

            }


            // 有足够多的彩色像素：用出现最多的颜色桶

            if(bestIndex >= 0 &&
               bestCount >= 16)
            {

                return System.Drawing.Color.FromArgb(
                    (int)(bucketR[bestIndex] / bestCount),
                    (int)(bucketG[bestIndex] / bestCount),
                    (int)(bucketB[bestIndex] / bestCount));

            }


            // 壁纸本身偏灰：退回整张缩略图的平均值

            if(allCount > 0)
            {

                return System.Drawing.Color.FromArgb(
                    (int)(allR / allCount),
                    (int)(allG / allCount),
                    (int)(allB / allCount));

            }


            return FallbackColor;

        }


    }

}