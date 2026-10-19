using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;


namespace GlassDock
{


public static class ShellHelper
{


    // ==============================
    // Shell结构
    // ==============================


    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {

        public IntPtr hIcon;


        public int iIcon;


        public uint dwAttributes;



        [MarshalAs(UnmanagedType.ByValTStr,
            SizeConst = 260)]
        public string szDisplayName;



        [MarshalAs(UnmanagedType.ByValTStr,
            SizeConst = 80)]
        public string szTypeName;

    }





    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);





    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool DestroyIcon(
        IntPtr hIcon);





    private const uint SHGFI_ICON =
        0x100;



    private const uint SHGFI_LARGEICON =
        0x0;



    private const uint FILE_ATTRIBUTE_DIRECTORY =
        0x10;







    // ==============================
    // 获取快捷方式目标
    // ==============================


    private static string GetShortcutTarget(
        string shortcut)
    {


        try
        {


            Type? shellType =
                Type.GetTypeFromProgID(
                    "WScript.Shell");



            if(shellType == null)
                return "";




            dynamic shell =
                Activator.CreateInstance(
                    shellType)!;




            dynamic link =
                shell.CreateShortcut(
                    shortcut);





            string target =
                link.TargetPath;





            Marshal.ReleaseComObject(
                link);



            Marshal.ReleaseComObject(
                shell);





            return target;


        }
        catch
        {

            return "";

        }


    }






    // ==============================
    // 解析路径
    // ==============================


    public static string ResolvePath(
        string path)
    {


        try
        {


            if(Path.GetExtension(path)
                .Equals(
                    ".lnk",
                    StringComparison.OrdinalIgnoreCase))
            {


                string target =
                    GetShortcutTarget(path);





                if(!string.IsNullOrWhiteSpace(target))
                {

                    return target;

                }


            }



            return path;


        }
        catch
        {

            return path;

        }


    }
        // ==============================
    // 提取图标
    // ==============================


    public static string ExtractIcon(
        string path)
    {


        try
        {


            string target =
                ResolvePath(path);





            // ==========================
            // AppX图标
            // ==========================


            if(AppXHelper.IsAppX(target))
            {


                // 优先高分辨率图标
                // shell:AppsFolder\AppUserModelId

                string hiRes =
                    IconHelper.Extract(
                        "shell:AppsFolder\\"
                        +
                        target);



                if(!string.IsNullOrWhiteSpace(hiRes))
                {

                    return hiRes;

                }




                string icon =
                    AppXHelper.GetIcon(target);



                if(!string.IsNullOrWhiteSpace(icon))
                {

                    return icon;

                }


            }







            // ==========================
            // 高分辨率图标（256px）
            // 优先于下面的低分辨率兜底方案
            // ==========================


            string highRes =
                IconHelper.Extract(target);



            if(!string.IsNullOrWhiteSpace(highRes))
            {

                return highRes;

            }




            // ==========================
            // 图标缓存目录
            // ==========================


            string iconFolder =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Icons");




            if(!Directory.Exists(iconFolder))
            {

                Directory.CreateDirectory(
                    iconFolder);

            }







            string iconFile =
                Path.Combine(
                    iconFolder,
                    Guid.NewGuid()
                    +
                    ".png");








            SHFILEINFO info =
                new SHFILEINFO();





            uint attr =
                0;




            if(Directory.Exists(target))
            {

                attr =
                    FILE_ATTRIBUTE_DIRECTORY;

            }






            SHGetFileInfo(
                target,
                attr,
                ref info,
                (uint)Marshal.SizeOf(
                    typeof(SHFILEINFO)),
                SHGFI_ICON |
                SHGFI_LARGEICON);







            if(info.hIcon ==
                IntPtr.Zero)
            {

                return "";

            }








            using(Icon icon =
                Icon.FromHandle(
                    info.hIcon))
            {


                using(Bitmap bitmap =
                    icon.ToBitmap())
                {


                    bitmap.Save(
                        iconFile,
                        ImageFormat.Png);


                }


            }







            DestroyIcon(
                info.hIcon);







            return iconFile;



        }
        catch(Exception ex)
        {


            System.Diagnostics.Debug.WriteLine(
                ex.Message);



            return "";

        }


    }



}

}