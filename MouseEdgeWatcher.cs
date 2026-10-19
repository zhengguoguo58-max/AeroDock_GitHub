using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace GlassDock
{

    public class MouseEdgeWatcher
    {


        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }



        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(
            out POINT point);




        public event Action<bool>? EdgeChanged;



        private bool lastState = false;



        public void Start()
        {


            Task.Run(() =>
            {


                while(true)
                {


                    GetCursorPos(
                        out POINT point);



                    double screenWidth =
                        System.Windows.SystemParameters.PrimaryScreenWidth;



                    double screenHeight =
                        System.Windows.SystemParameters.PrimaryScreenHeight;




                    // Dock实际宽度区域
                    double dockWidth = 560;



                    double center =
                        screenWidth / 2;



                    double left =
                        center - dockWidth / 2;



                    double right =
                        center + dockWidth / 2;




                    // Dock隐藏区域
                    bool inDockArea =
                        point.X >= left &&
                        point.X <= right &&
                        point.Y >= screenHeight - 120;




                    if(inDockArea != lastState)
                    {


                        lastState =
                            inDockArea;



                        EdgeChanged?
                            .Invoke(inDockArea);


                    }



                    Thread.Sleep(80);


                }


            });


        }



    }

}