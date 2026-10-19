namespace GlassDock
{

    public enum AppType
    {
        Exe,

        Shortcut,

        AppX,

        Folder
    }





    public class AppItem
    {


        // 显示名称
        public string Name { get; set; }



        // 启动目标
        // exe路径 / AppUserModelId / 文件夹路径
        public string LaunchPath { get; set; }



        // 图标缓存路径
        public string IconPath { get; set; }



        // 应用类型
        public AppType Type { get; set; }







        public AppItem(
            string name,
            string launchPath,
            AppType type)
        {

            Name = name;


            LaunchPath = launchPath;


            Type = type;


            IconPath = "";

        }


    }

}