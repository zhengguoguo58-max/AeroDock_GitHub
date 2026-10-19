using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;


namespace GlassDock
{

public static class AppManager
{


    // ==============================
    // Dock应用列表
    // ==============================

    public static ObservableCollection<AppItem> Apps
    {
        get;
        set;
    }
    =
    new ObservableCollection<AppItem>();




    // ==============================
    // 启动时恢复
    // 从本地配置读取Dock应用
    // ==============================


    public static void Initialize()
    {

        DockStorage.Load();

    }





    // ==============================
    // 添加应用
    // exe / lnk / 文件夹 / AppX
    // ==============================


    public static void AddApp(
        string path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return;



        AppItem? app = 
            CreateAppItem(path);



        if(app == null)
            return;





        // ==============================
        // 防止重复
        // ==============================


        foreach(var item in Apps)
        {


            if(string.Equals(
                item.LaunchPath,
                app.LaunchPath,
                StringComparison.OrdinalIgnoreCase))
            {

                return;

            }


        }





        Apps.Add(app);




        // 保存到本地配置

        DockStorage.Save();


    }




    // ==============================
    // 创建应用对象
    // ==============================


    private static AppItem? CreateAppItem(
        string path)
    {


        try
        {


            // ==========================
            // 文件夹
            // ==========================

            if(Directory.Exists(path))
            {


                AppItem folder =
                    new AppItem(
                        Path.GetFileName(path),
                        path,
                        AppType.Folder);



                folder.IconPath =
                    ShellHelper.ExtractIcon(path);



                return folder;


            }






            // ==========================
            // 快捷方式
            // ==========================


            if(Path.GetExtension(path)
                .Equals(
                    ".lnk",
                    StringComparison.OrdinalIgnoreCase))
            {


                string target =
                    ShellHelper.ResolvePath(path);





                // AppX快捷方式

                if(AppXHelper.IsAppX(target))
                {


                    AppItem appx =
                        new AppItem(
                            Path.GetFileNameWithoutExtension(path),
                            target,
                            AppType.AppX);



                    appx.IconPath =
                        ShellHelper.ExtractIcon(
                            target);



                    return appx;


                }







                // 普通快捷方式


                AppItem shortcut =
                    new AppItem(
                        Path.GetFileNameWithoutExtension(path),
                        target,
                        AppType.Shortcut);



                shortcut.IconPath =
                    ShellHelper.ExtractIcon(
                        target);



                return shortcut;


            }







            // ==========================
            // AppX直接拖入
            // ==========================


            if(AppXHelper.IsAppX(path))
            {


                AppItem appx =
                    new AppItem(
                        path,
                        path,
                        AppType.AppX);



                appx.IconPath =
                    ShellHelper.ExtractIcon(
                        path);



                return appx;


            }









            // ==========================
            // 普通 exe / 文件
            // ==========================


            if(File.Exists(path))
            {


                AppItem exe =
                    new AppItem(
                        Path.GetFileNameWithoutExtension(path),
                        path,
                        AppType.Exe);





                exe.IconPath =
                    ShellHelper.ExtractIcon(
                        path);





                return exe;


            }




        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                ex.Message);


        }



        return null;


    }







    // ==============================
    // 从 Dock 移除
    // ==============================


    public static void RemoveApp(
        AppItem app)
    {


        if(app == null)
            return;



        Apps.Remove(app);




        // 保存到本地配置

        DockStorage.Save();



    }





    // ==============================
    // 启动应用
    // ==============================


    public static void Launch(
        AppItem app)
    {


        if(app == null)
            return;



        try
        {


            switch(app.Type)
            {


                // ==========================
                // 文件夹
                // ==========================

                case AppType.Folder:


                    Process.Start(
                        "explorer.exe",
                        app.LaunchPath);


                    break;






                // ==========================
                // AppX应用
                // ==========================

                case AppType.AppX:


                    Process.Start(
                        new ProcessStartInfo
                        {

                            FileName =
                                "explorer.exe",


                            Arguments =
                                "shell:AppsFolder\\"
                                +
                                app.LaunchPath,


                            UseShellExecute =
                                true

                        });


                    break;







                // ==========================
                // exe / 快捷方式
                // ==========================

                case AppType.Exe:

                case AppType.Shortcut:



                    Process.Start(
                        new ProcessStartInfo
                        {

                            FileName =
                                app.LaunchPath,


                            UseShellExecute =
                                true

                        });


                    break;



            }


        }
        catch(Exception ex)
        {


            System.Windows.MessageBox.Show(
                "启动失败:\n\n"
                +
                ex.Message,
                "GlassDock");


        }



    }





}

}