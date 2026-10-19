using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace GlassDock
{

    public class WindowWatcher
    {


        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();



        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr hwnd,
            out uint processId);



        public event Action<bool>? DesktopStateChanged;



        private bool lastState = false;




        public void Start()
        {


            Task.Run(() =>
            {


                while(true)
                {


                    bool desktop =
                        IsDesktop();



                    if(desktop != lastState)
                    {

                        lastState = desktop;


                        DesktopStateChanged?
                            .Invoke(desktop);

                    }



                    Thread.Sleep(100);


                }


            });


        }






        private bool IsDesktop()
        {


            IntPtr hwnd =
                GetForegroundWindow();



            if(hwnd == IntPtr.Zero)
                return false;



            GetWindowThreadProcessId(
                hwnd,
                out uint pid);




            try
            {

                Process process =
                    Process.GetProcessById(
                        (int)pid);



                string name =
                    process.ProcessName;




                //Windows资源管理器桌面
                if(name.Equals(
                    "explorer",
                    StringComparison.OrdinalIgnoreCase))
                {

                    return true;

                }


            }
            catch
            {

            }



            return false;


        }



    }

}