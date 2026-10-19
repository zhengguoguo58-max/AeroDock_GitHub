using System;
using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;


namespace GlassDock
{

    public class ColorAnalyzer
    {


        public System.Windows.Media.Color GetAverageColor()
        {

            try
            {

                int width =
                    (int)SystemParameters.PrimaryScreenWidth;


                int height =
                    (int)SystemParameters.PrimaryScreenHeight;



                // 只截取底部 Dock 附近区域
                int sampleHeight = 150;



                using Bitmap bitmap =
                    new Bitmap(
                        width / 5,
                        sampleHeight / 5);



                using Graphics graphics =
                    Graphics.FromImage(bitmap);



                graphics.CopyFromScreen(
                    0,
                    height - sampleHeight,
                    0,
                    0,
                    bitmap.Size);



                long r = 0;
                long g = 0;
                long b = 0;


                int count = 0;



                for(int x = 0; x < bitmap.Width; x++)
                {

                    for(int y = 0; y < bitmap.Height; y++)
                    {

                        System.Drawing.Color pixel =
                            bitmap.GetPixel(x,y);



                        r += pixel.R;
                        g += pixel.G;
                        b += pixel.B;


                        count++;

                    }

                }



                return System.Windows.Media.Color.FromRgb(

                    (byte)(r / count),

                    (byte)(g / count),

                    (byte)(b / count)

                );


            }
            catch
            {

                return System.Windows.Media.Colors.Gray;

            }


        }


    }

}