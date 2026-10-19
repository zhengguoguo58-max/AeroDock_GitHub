using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;


namespace GlassDock
{

    public class DockWatcher
    {


        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT point);



        private DispatcherTimer timer;


        private Window window;


        private bool lastState = false;



        public event Action<bool>? DockHover;





        public DockWatcher(Window target)
        {

            window = target;


            timer =
                new DispatcherTimer();


            timer.Interval =
                TimeSpan.FromMilliseconds(50);


            timer.Tick += Check;


        }





        public void Start()
        {

            timer.Start();

        }





        public void Stop()
        {

            timer.Stop();

        }






        private void Check(
            object? sender,
            EventArgs e)
        {


            GetCursorPos(out POINT p);



            // 获取窗口左上角屏幕坐标

            Point pos =
                window.PointToScreen(
                    new Point(0, 0));



            double left =
                pos.X;



            double right =
                pos.X +
                window.ActualWidth;



            double top =
                pos.Y;



            double bottom =
                pos.Y +
                window.ActualHeight;





            /*
             * 整个 Dock 窗口区域作为感应区
             */

            bool inside =

                p.X >= left &&

                p.X <= right &&

                p.Y >= top &&

                p.Y <= bottom;







            if(inside != lastState)
            {

                lastState = inside;


                DockHover?.Invoke(inside);

            }


        }






        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {

            public int X;

            public int Y;

        }


    }

}