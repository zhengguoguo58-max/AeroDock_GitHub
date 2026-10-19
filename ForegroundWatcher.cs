using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace GlassDock
{

    public class ForegroundWatcher
    {


        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();



        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();



        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(
            string lpClassName,
            string lpWindowName);



        public event Action<bool>? WindowChanged;



        private bool lastState = false;




        public void Start()
        {


            Task.Run(() =>
            {


                while(true)
                {


                    bool otherWindow =
                        IsApplicationWindow();



                    if(otherWindow != lastState)
                    {


                        lastState =
                            otherWindow;



                        WindowChanged?
                            .Invoke(otherWindow);


                    }



                    Thread.Sleep(200);


                }


            });


        }








        private bool IsApplicationWindow()
        {


            IntPtr foreground =
                GetForegroundWindow();



            if(foreground == IntPtr.Zero)
            {

                return false;

            }



            IntPtr shell =
                GetShellWindow();



            // 当前就是桌面
            if(foreground == shell)
            {

                return false;

            }



            string className =
                GetClassName(foreground);



            // Windows 桌面窗口
            if(className ==
                "Progman" ||
               className ==
                "WorkerW")
            {

                return false;

            }




            // 其他窗口
            return true;


        }







        [DllImport("user32.dll")]
        private static extern int GetClassName(
            IntPtr hWnd,
            System.Text.StringBuilder lpClassName,
            int nMaxCount);





        private string GetClassName(
            IntPtr hwnd)
        {


            var builder =
                new System.Text.StringBuilder(256);



            GetClassName(
                hwnd,
                builder,
                builder.Length);



            return builder.ToString();


        }


    }

}