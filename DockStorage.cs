using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace GlassDock
{

    public static class DockStorage
    {


        // ==============================
        // 配置文件位置
        // %AppData%\GlassDock\dock.json
        // ==============================


        private static readonly string ConfigFolder =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "GlassDock");



        private static readonly string ConfigFile =
            Path.Combine(
                ConfigFolder,
                "dock.json");




        // ==============================
        // JSON序列化参数
        // ==============================


        private static readonly JsonSerializerOptions Options =
            new JsonSerializerOptions
            {

                // 缩进输出，方便人工查看

                WriteIndented =
                    true,


                // 保留中文，不转义成\uXXXX

                Encoder =
                    JavaScriptEncoder.UnsafeRelaxedJsonEscaping,


                // 属性名使用camelCase

                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,


                // 枚举按字符串保存

                Converters =
                {
                    new JsonStringEnumConverter()
                }

            };




        // UTF-8 不带BOM

        private static readonly UTF8Encoding Utf8NoBom =
            new UTF8Encoding(false);




        // ==============================
        // 保存配置
        // ==============================


        public static void Save()
        {

            try
            {


                DockConfig config =
                    new DockConfig();



                config.Version = 1;



                foreach(AppItem app in AppManager.Apps)
                {

                    if(app == null)
                        continue;



                    config.Apps.Add(
                        new AppEntry
                        {

                            Name =
                                app.Name ?? "",

                            LaunchPath =
                                app.LaunchPath ?? "",

                            Type =
                                app.Type,

                            IconPath =
                                app.IconPath ?? ""

                        });

                }




                string json =
                    JsonSerializer.Serialize(
                        config,
                        Options);




                if(!Directory.Exists(ConfigFolder))
                {

                    Directory.CreateDirectory(
                        ConfigFolder);

                }




                // 先写临时文件，再整体替换
                // 避免写入过程中断导致配置损坏

                string temp =
                    ConfigFile + ".tmp";



                File.WriteAllText(
                    temp,
                    json,
                    Utf8NoBom);



                try
                {

                    File.Move(
                        temp,
                        ConfigFile,
                        true);

                }
                catch
                {

                    // 替换失败时退化为直接覆盖

                    File.WriteAllText(
                        ConfigFile,
                        json,
                        Utf8NoBom);

                }


            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "配置保存失败: "
                    +
                    ex.Message);

            }


        }

        // ==============================
        // 读取配置并恢复Dock
        // ==============================


        public static void Load()
        {

            try
            {


                if(!File.Exists(ConfigFile))
                    return;



                string json =
                    File.ReadAllText(ConfigFile);



                if(string.IsNullOrWhiteSpace(json))
                    return;



                DockConfig? config =
                    JsonSerializer.Deserialize<DockConfig>(
                        json,
                        Options);



                if(config == null ||
                   config.Apps == null)
                    return;



                bool iconRepaired =
                    false;



                foreach(AppEntry entry in config.Apps)
                {

                    if(entry == null)
                        continue;



                    if(string.IsNullOrWhiteSpace(
                        entry.LaunchPath))
                        continue;




                    // 已经存在则跳过（按启动路径去重）

                    if(Contains(entry.LaunchPath))
                        continue;




                    AppItem app =
                        new AppItem(

                            string.IsNullOrWhiteSpace(
                                entry.Name)
                            ?
                            entry.LaunchPath
                            :
                            entry.Name,

                            entry.LaunchPath,

                            entry.Type);



                    string icon =
                        RepairIcon(entry);



                    app.IconPath =
                        icon;



                    if(!string.Equals(
                        icon,
                        entry.IconPath,
                        StringComparison.OrdinalIgnoreCase))
                    {

                        iconRepaired =
                            true;

                    }



                    AppManager.Apps.Add(app);


                }




                // 有图标被修复过则回写一次

                if(iconRepaired)
                {

                    Save();

                }


            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "配置读取失败: "
                    +
                    ex.Message);



                // 备份损坏文件，保证下次能正常启动

                BackupBrokenFile();


            }


        }




        // ==============================
        // 判断启动路径是否已存在
        // ==============================


        private static bool Contains(
            string launchPath)
        {

            foreach(AppItem item in AppManager.Apps)
            {


                if(string.Equals(
                    item.LaunchPath,
                    launchPath,
                    StringComparison.OrdinalIgnoreCase))
                {

                    return true;

                }


            }



            return false;


        }




        // ==============================
        // 图标自愈
        // 文件在就用，不在就重新提取
        // ==============================


        private static string RepairIcon(
            AppEntry entry)
        {


            try
            {


                // 缓存图标仍然有效且足够清晰

                if(!string.IsNullOrWhiteSpace(
                    entry.IconPath) &&
                   File.Exists(entry.IconPath) &&
                   !IconHelper.IsTooSmall(
                        entry.IconPath,
                        64))
                {

                    return entry.IconPath;

                }




                // 图标丢失，按启动路径重新提取

                if(!string.IsNullOrWhiteSpace(
                    entry.LaunchPath))
                {

                    string icon =
                        ShellHelper.ExtractIcon(
                            entry.LaunchPath);



                    if(!string.IsNullOrWhiteSpace(icon))
                    {

                        return icon;

                    }


                }


            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "图标自愈失败: "
                    +
                    ex.Message);

            }




            return entry.IconPath ?? "";


        }




        // ==============================
        // 备份损坏的配置文件
        // ==============================


        private static void BackupBrokenFile()
        {

            try
            {


                if(!File.Exists(ConfigFile))
                    return;



                string backup =
                    ConfigFile + ".bad";



                if(File.Exists(backup))
                {

                    File.Delete(backup);

                }



                File.Move(
                    ConfigFile,
                    backup);


            }
            catch
            {


            }


        }

    }

}
