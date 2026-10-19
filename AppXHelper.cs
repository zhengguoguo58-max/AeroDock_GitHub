using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;


namespace GlassDock
{


public static class AppXHelper
{


    // ==============================
    // Win32 Shell
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
        "user32.dll")]
    private static extern bool DestroyIcon(
        IntPtr hIcon);





    private const uint SHGFI_ICON =
        0x100;



    private const uint SHGFI_LARGEICON =
        0x0;






    // ==============================
    // 判断AppX
    // ==============================


    public static bool IsAppX(
        string target)
    {


        if(string.IsNullOrWhiteSpace(target))
            return false;




        // AppUserModelId格式:
        // PackageFamilyName!AppId

        return target.Contains("!");

    }





    // ==============================
    // 获取图标
    // ==============================


    public static string GetIcon(
        string appId)
    {


        try
        {


            // 优先Shell获取

            string shellIcon =
                GetShellIcon(appId);



            if(!string.IsNullOrWhiteSpace(shellIcon))
            {

                return shellIcon;

            }






            // 备用:
            // 扫描安装包资源


            string package =
                FindPackage(appId);



            if(string.IsNullOrWhiteSpace(package))
                return "";





            string logo =
                FindLogo(package);



            if(string.IsNullOrWhiteSpace(logo))
                return "";





            return CopyIcon(logo);



        }
        catch
        {

            return "";

        }


    }




    // ==============================
    // 使用Shell获取App图标
    // ==============================


    private static string GetShellIcon(
        string appId)
    {


        try
        {


            string temp =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Icons");




            if(!Directory.Exists(temp))
            {

                Directory.CreateDirectory(
                    temp);

            }







            string output =
                Path.Combine(
                    temp,
                    Guid.NewGuid()
                    +
                    ".png");







            SHFILEINFO info =
                new SHFILEINFO();







            SHGetFileInfo(
                "shell:AppsFolder\\" + appId,
                0,
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
                        output,
                        ImageFormat.Png);


                }


            }







            DestroyIcon(
                info.hIcon);






            return output;



        }
        catch
        {

            return "";

        }


    }







    // ==============================
    // 查找AppX包
    // ==============================


    private static string FindPackage(
        string appId)
    {


        try
        {


            string packageName =
                appId.Split('!')[0];







            string packages =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Packages");







            if(!Directory.Exists(packages))
                return "";







            foreach(string folder in
                Directory.GetDirectories(packages))
            {


                string name =
                    Path.GetFileName(folder);







                if(name.StartsWith(
                    packageName,
                    StringComparison.OrdinalIgnoreCase))
                {


                    return folder;


                }



            }





            return "";



        }
        catch
        {

            return "";

        }


    }










    // ==============================
    // 查找Logo
    // ==============================


    private static string FindLogo(
        string packagePath)
    {


        try
        {


            string assets =
                Path.Combine(
                    packagePath,
                    "Assets");






            if(!Directory.Exists(assets))
                return "";







            string best =
                "";



            int bestSize =
                0;







            foreach(string file in
                Directory.GetFiles(
                    assets,
                    "*.png",
                    SearchOption.AllDirectories))
            {


                try
                {


                    using(Bitmap bmp =
                        new Bitmap(file))
                    {


                        int size =
                            bmp.Width *
                            bmp.Height;





                        // 过滤太小的图片

                        if(size < 4096)
                            continue;







                        if(size > bestSize)
                        {

                            bestSize =
                                size;


                            best =
                                file;


                        }


                    }


                }
                catch
                {

                }


            }





            return best;



        }
        catch
        {

            return "";

        }


    }









    // ==============================
    // 复制图标缓存
    // ==============================


    private static string CopyIcon(
        string source)
    {


        try
        {


            string folder =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Icons");





            if(!Directory.Exists(folder))
            {

                Directory.CreateDirectory(
                    folder);

            }








            string output =
                Path.Combine(
                    folder,
                    Guid.NewGuid()
                    +
                    ".png");







            using(Bitmap bitmap =
                new Bitmap(source))
            {


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




}

}