using System.Collections.Generic;


namespace GlassDock
{


    // ==============================
    // 本地配置文件结构
    // 对应 %AppData%\GlassDock\dock.json
    // ==============================


    public class DockConfig
    {


        // 配置结构版本
        // 便于以后升级/迁移

        public int Version
        {
            get;
            set;
        }
        =
        1;




        // Dock应用列表

        public List<AppEntry> Apps
        {
            get;
            set;
        }
        =
        new List<AppEntry>();


    }





    // ==============================
    // 单个应用记录
    // ==============================


    public class AppEntry
    {


        // 显示名称

        public string Name
        {
            get;
            set;
        }
        =
        "";




        // 启动目标
        // exe路径 / AppUserModelId / 文件夹路径

        public string LaunchPath
        {
            get;
            set;
        }
        =
        "";




        // 应用类型
        // JSON中以字符串保存

        public AppType Type
        {
            get;
            set;
        }
        =
        AppType.Exe;




        // 图标缓存路径

        public string IconPath
        {
            get;
            set;
        }
        =
        "";


    }


}
