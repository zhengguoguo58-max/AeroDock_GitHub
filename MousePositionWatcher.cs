using System;
using System.Runtime.InteropServices;
using System.Windows;


namespace GlassDock
{

    public class MousePositionWatcher
    {


        [DllImport("user32.dll")]
        static extern bool GetCursorPos(
            out POINT lpPoint);



        public event Action<double>? MouseDistanceChanged;



        private System.Windows.Threading.DispatcherTimer timer;



        public MousePositionWatcher()
        {


            timer =
                new System.Windows.Threading.DispatcherTimer();


            timer.Interval =
                TimeSpan.FromMilliseconds(50);


            timer.Tick += Timer_Tick;


        }





        public void Start()
        {

            timer.Start();

        }





        public void Stop()
        {

            timer.Stop();

        }





        private void Timer_Tick(
            object? sender,
            EventArgs e)
        {


            GetCursorPos(out POINT p);



            double screenHeight =
                SystemParameters.PrimaryScreenHeight;



            double distance =
                screenHeight - p.Y;



            MouseDistanceChanged?
                .Invoke(distance);


        }





        [StructLayout(
            LayoutKind.Sequential)]
        public struct POINT
        {

            public int X;

            public int Y;

        }


    }

}