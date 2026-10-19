using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;


namespace GlassDock
{


public static class DesktopLayerManager
{


    // ==============================
    // Win32结构
    // ==============================


    private delegate bool EnumWindowsProc(
        IntPtr hWnd,
        IntPtr lParam);






    [DllImport(
        "user32.dll")]
    private static extern bool EnumWindows(
        EnumWindowsProc lpEnumFunc,
        IntPtr lParam);







    [DllImport(
        "user32.dll",
        CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(
        IntPtr hwndParent,
        IntPtr hwndChildAfter,
        string className,
        string windowTitle);







    [DllImport(
        "user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam);







    [DllImport(
        "user32.dll")]
    private static extern IntPtr SetParent(
        IntPtr hWndChild,
        IntPtr hWndNewParent);








    [DllImport(
        "user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint flags);









    private const uint WM_SPAWN_WORKER =
        0x052C;






    private static readonly IntPtr HWND_BOTTOM =
        new IntPtr(1);






    private const uint SWP_NOSIZE =
        0x0001;



    private const uint SWP_NOMOVE =
        0x0002;



    private const uint SWP_NOACTIVATE =
        0x0010;









    // ==============================
    // 刷新进入桌面层
    // ==============================


    public static void Refresh(
        Window window)
    {


        try
        {


            PlaceWindowBehindIcons(
                window);



        }
        catch
        {

        }



    }









    // ==============================
    // 放入桌面层
    // ==============================


    public static void PlaceWindowBehindIcons(
        Window window)
    {


        try
        {


            IntPtr hwnd =
                new WindowInteropHelper(
                    window)
                .Handle;






            if(hwnd == IntPtr.Zero)
            {


                window.SourceInitialized +=
                    (s,e)=>
                    {

                        PlaceWindowBehindIcons(
                            window);

                    };


                return;


            }








            IntPtr worker =
                GetWorkerW();







            if(worker != IntPtr.Zero)
            {


                SetParent(
                    hwnd,
                    worker);






                // 防止置顶

                SetWindowPos(
                    hwnd,
                    HWND_BOTTOM,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOMOVE |
                    SWP_NOSIZE |
                    SWP_NOACTIVATE);



            }



        }
        catch
        {


        }



    }









    // ==============================
    // 获取WorkerW
    // ==============================


    private static IntPtr GetWorkerW()
    {


        IntPtr workerW =
            IntPtr.Zero;






        IntPtr progman =
            FindWindowEx(
                IntPtr.Zero,
                IntPtr.Zero,
                "Progman",
                null);







        if(progman != IntPtr.Zero)
        {


            SendMessage(
                progman,
                WM_SPAWN_WORKER,
                IntPtr.Zero,
                IntPtr.Zero);


        }








        EnumWindows(
            (hwnd,lParam)=>
            {


                IntPtr shellView =
                    FindWindowEx(
                        hwnd,
                        IntPtr.Zero,
                        "SHELLDLL_DefView",
                        null);






                if(shellView != IntPtr.Zero)
                {


                    workerW =
                        FindWindowEx(
                            IntPtr.Zero,
                            hwnd,
                            "WorkerW",
                            null);






                    return false;


                }





                return true;



            },
            IntPtr.Zero);






        return workerW;



    }






    // ==============================
    // 强制刷新层级
    // ==============================


    public static void ResetLayer(
        Window window)
    {


        try
        {


            IntPtr hwnd =
                new WindowInteropHelper(
                    window)
                .Handle;





            if(hwnd == IntPtr.Zero)
                return;






            SetWindowPos(
                hwnd,
                HWND_BOTTOM,
                0,
                0,
                0,
                0,
                SWP_NOMOVE |
                SWP_NOSIZE |
                SWP_NOACTIVATE);



        }
        catch
        {

        }



    }






}

}