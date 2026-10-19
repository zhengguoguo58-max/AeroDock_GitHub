using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;


namespace GlassDock
{

// ==============================
// 图标兜底提取引擎
// ==============================
// 为什么需要它：
//   少数软件（第三方启动器启动的游戏、易语言 / 打包工具生成的 exe）
//   本身只带 16x16 的小图标，或者 Shell 给出的图标是空白 / 全黑，
//   被 Dock 放大到 50px 之后就是一块模糊白框。
//
// 这里的做法是分级尝试 + 体检：
//   宁可退回"系统默认普通程序图标"，也不把 16x16 的糊图强行拉伸。
//
// 尝试顺序：
//   1) .lnk 解析出真实目标（支持快捷方式套快捷方式）
//   2) ExtractIconEx 从 exe / dll 资源里取（大图标小图标全取，挑最大的）
//   3) 目标所在文件夹里的外置 .ico（打包工具常见做法）
//   4) SHGetFileInfo 取系统关联大图标
//   5) ExtractAssociatedIcon
//
// 体检标准（IsAcceptable）：
//   - 边长 >= 32（小于 32 一律判不合格）
//   - 不能全透明
//   - 不能是不透明全黑 / 不透明全白（坏图标、坏快捷方式的典型特征）
//
// 全部失败返回 null，由调用方用"系统默认普通程序图标"收尾
// ==============================

public static class IconExtractor
{

    // 可接受的最小边长（小于它一律判不合格）
    public const int MinSize = 32;

    // 优先边长：能拿到这么大的就用它（外置 .ico 往往是 256）
    public const int PreferredSize = 64;

    // 单个文件里最多查看几个图标资源
    private const int MaxIconsPerFile = 8;

    // 兜底探针：借 .exe 后缀去要系统"普通程序"图标
    // 配合 SHGFI_USEFILEATTRIBUTES 使用，不会真的去读磁盘
    private const string DefaultAppProbe = "GlassDock.default.app.exe";

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    private const uint SHGFI_ICON = 0x100;

    private const uint SHGFI_LARGEICON = 0x0;

    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;

    // 只对这几类文件做"资源提取"，别去折腾 txt / jpg
    private static readonly string[] IconSourceExtensions =
    {

        ".exe", ".dll", ".ocx", ".scr", ".cpl", ".msi"

    };


    // ==============================
    // Win32 互操作
    // ==============================

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {

        public IntPtr hIcon;

        public int iIcon;

        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;

    }


    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(
        IntPtr hInst,
        string lpszExeFileName,
        int nIconIndex);


    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        [In, Out] IntPtr[]? phiconLarge,
        [In, Out] IntPtr[]? phiconSmall,
        uint nIcons);


    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);


    [DllImport("User32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(
        IntPtr hIcon);


    // ==============================
    // .lnk → 真实目标
    // shell:AppsFolder\... 这类虚拟路径原样返回，不做解析
    // ==============================

    public static string ResolveTarget(
        string? path)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(path))
                return "";

            string current =
                path.Trim().Trim('"');

            if(current.StartsWith(
                "shell:",
                StringComparison.OrdinalIgnoreCase))
                return current;

            // 快捷方式可能指向另一个快捷方式，最多解析 8 层

            for(int depth = 0; depth < 8; depth++)
            {

                if(!current.EndsWith(
                    ".lnk",
                    StringComparison.OrdinalIgnoreCase))
                    break;

                string next =
                    ReadShortcutTarget(current);

                if(string.IsNullOrWhiteSpace(next) ||
                   string.Equals(
                       next,
                       current,
                       StringComparison.OrdinalIgnoreCase))
                    break;

                current =
                    next.Trim().Trim('"');

            }

            return current;

        }
        catch
        {

            return path ?? "";

        }

    }


    // 读 .lnk 里的 TargetPath
    // 和 ShellHelper 用同一套做法（WScript.Shell），不引新依赖

    private static string ReadShortcutTarget(
        string shortcut)
    {

        try
        {

            Type? shellType =
                Type.GetTypeFromProgID("WScript.Shell");

            if(shellType == null)
                return "";

            dynamic shell =
                Activator.CreateInstance(shellType)!;

            dynamic link =
                shell.CreateShortcut(shortcut);

            string? target =
                link.TargetPath;

            Marshal.ReleaseComObject(link);

            Marshal.ReleaseComObject(shell);

            return target ?? "";

        }
        catch
        {

            return "";

        }

    }


    // ==============================
    // 对外主入口：多路尝试，拿不到就返回 null
    // 先按 PreferredSize 要，实在没有再放宽到 MinSize
    // 每一步都做体检，不合格就继续往下找
    // ==============================

    public static Icon? GetBestIcon(
        string? path)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(path))
                return null;

            string original =
                path.Trim().Trim('"');

            string target =
                ResolveTarget(original);

            List<string> candidates =
                new List<string>();

            if(!string.IsNullOrWhiteSpace(target))
                candidates.Add(target);

            if(!string.Equals(
                target,
                original,
                StringComparison.OrdinalIgnoreCase))
                candidates.Add(original);

            // 存在的路径才值得提取
            // AppX 的 AUMID（shell:AppsFolder\...）不是文件，直接放弃
            // 交给调用方用 IconPath 兜底

            bool usable =
                false;

            foreach(string candidate in candidates)
            {

                if(File.Exists(candidate) ||
                   Directory.Exists(candidate))
                {

                    usable =
                        true;

                    break;

                }

            }

            if(!usable)
                return null;

            Icon? icon =
                Search(
                    candidates,
                    PreferredSize);

            if(icon != null)
                return icon;

            return Search(
                candidates,
                MinSize);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "图标提取失败: "
                +
                ex.Message);

            return null;

        }

    }


    // 按给定门槛，逐级尝试
    // 1) exe / dll 资源 → 2) 外置 .ico → 3) 系统关联图标 → 4) 系统 API

    private static Icon? Search(
        List<string> candidates,
        int minSize)
    {

        foreach(string file in candidates)
        {

            if(!IsIconSourceFile(file))
                continue;

            Icon? icon =
                Take(
                    ExtractFromResources(
                        file,
                        minSize),
                    minSize);

            if(icon != null)
                return icon;

        }

        foreach(string file in candidates)
        {

            string ico =
                FindExternalIcon(file);

            if(ico.Length == 0)
                continue;

            Icon? icon =
                Take(
                    LoadIconFile(ico),
                    minSize);

            if(icon != null)
                return icon;

        }

        foreach(string file in candidates)
        {

            Icon? icon =
                Take(
                    IconFromShell(file),
                    minSize);

            if(icon != null)
                return icon;

        }

        foreach(string file in candidates)
        {

            Icon? icon =
                Take(
                    AssociatedIcon(file),
                    minSize);

            if(icon != null)
                return icon;

        }

        return null;

    }


    // 体检：不合格的直接丢掉（顺手释放，避免 GDI 句柄泄漏）

    private static Icon? Take(
        Icon? icon,
        int minSize)
    {

        if(icon == null)
            return null;

        if(IsAcceptable(
            icon,
            minSize))
            return icon;

        icon.Dispose();

        return null;

    }

    // ==============================
    // ExtractIconEx：从 exe / dll 的资源里取图标
    // 大图标组和小图标组都取一遍，挑尺寸最大的那个
    // ==============================

    private static Icon? ExtractFromResources(
        string file,
        int minSize)
    {

        IntPtr[] large =
            new IntPtr[MaxIconsPerFile];

        IntPtr[] small =
            new IntPtr[MaxIconsPerFile];

        Icon? best =
            null;

        try
        {

            uint got =
                ExtractIconEx(
                    file,
                    0,
                    large,
                    small,
                    (uint)MaxIconsPerFile);

            if(got == 0)
                return null;

            for(int i = 0; i < MaxIconsPerFile; i++)
            {

                best =
                    Consider(
                        best,
                        large[i],
                        minSize);

                best =
                    Consider(
                        best,
                        small[i],
                        minSize);

            }

            return best;

        }
        catch
        {

            return best;

        }
        finally
        {

            Release(large);

            Release(small);

        }

    }


    // 句柄 → 体检 → 只留最大的那个
    // 留下的是克隆件，系统句柄随后在 finally 里统一销毁
    // 返回的是"调用方拥有"的图标，可以直接 Dispose

    private static Icon? Consider(
        Icon? best,
        IntPtr handle,
        int minSize)
    {

        if(handle == IntPtr.Zero)
            return best;

        try
        {

            Icon wrapper =
                Icon.FromHandle(handle);

            if(!IsAcceptable(
                wrapper,
                minSize))
                return best;

            int size =
                Math.Max(
                    wrapper.Width,
                    wrapper.Height);

            int bestSize =
                best == null
                    ? 0
                    : Math.Max(
                        best.Width,
                        best.Height);

            if(size <= bestSize)
                return best;

            Icon clone =
                (Icon)wrapper.Clone();

            if(best != null)
                best.Dispose();

            return clone;

        }
        catch
        {

            return best;

        }

    }


    // 把 ExtractIconEx 交出来的句柄全部还回去

    private static void Release(
        IntPtr[] handles)
    {

        foreach(IntPtr handle in handles)
        {

            if(handle != IntPtr.Zero)
                DestroyIcon(handle);

        }

    }


    // ==============================
    // SHGetFileInfo：取系统关联的大图标（32x32）
    // ==============================

    private static Icon? IconFromShell(
        string path)
    {

        try
        {

            SHFILEINFO info =
                new SHFILEINFO();

            uint attr =
                Directory.Exists(path)
                    ? FILE_ATTRIBUTE_DIRECTORY
                    : FILE_ATTRIBUTE_NORMAL;

            IntPtr result =
                SHGetFileInfo(
                    path,
                    attr,
                    ref info,
                    (uint)Marshal.SizeOf(
                        typeof(SHFILEINFO)),
                    SHGFI_ICON | SHGFI_LARGEICON);

            if(result == IntPtr.Zero ||
               info.hIcon == IntPtr.Zero)
                return null;

            try
            {

                Icon wrapper =
                    Icon.FromHandle(info.hIcon);

                return (Icon)wrapper.Clone();

            }
            finally
            {

                DestroyIcon(info.hIcon);

            }

        }
        catch
        {

            return null;

        }

    }


    // 最后的系统 API：拿"文件关联图标"（32x32）

    private static Icon? AssociatedIcon(
        string path)
    {

        try
        {

            return Icon.ExtractAssociatedIcon(path);

        }
        catch
        {

            return null;

        }

    }

    // ==============================
    // 外置 .ico：打包工具 / 易语言程序常常只把 16x16 塞进 exe，
    // 但在同目录放了一个 256x256 的 .ico
    // 这里只找，不解码（解码交给调用方，WPF 解 PNG 压缩的 256 帧更在行）
    // ==============================

    public static string FindExternalIcon(
        string? path)
    {

        try
        {

            string target =
                ResolveTarget(path);

            if(!IsIconSourceFile(target))
                return "";

            string? folder =
                Path.GetDirectoryName(target);

            if(string.IsNullOrWhiteSpace(folder) ||
               !Directory.Exists(folder))
                return "";

            // 1) 与 exe 完全同名：图吧工具箱.exe → 图吧工具箱.ico

            string sameName =
                Path.Combine(
                    folder,
                    Path.GetFileNameWithoutExtension(target) + ".ico");

            if(File.Exists(sameName))
                return sameName;

            // 2) 常见命名

            string[] commonNames =
            {

                "icon.ico", "app.ico", "logo.ico", Path.GetFileNameWithoutExtension(target) + "_icon.ico"

            };

            foreach(string name in commonNames)
            {

                string candidate =
                    Path.Combine(
                        folder,
                        name);

                if(File.Exists(candidate))
                    return candidate;

            }

            // 3) 整个文件夹只有一个 .ico 的话才敢赌

            string[] files =
                Directory.GetFiles(
                    folder,
                    "*.ico");

            if(files.Length == 1)
                return files[0];

            return "";

        }
        catch
        {

            return "";

        }

    }


    // ==============================
    // 加载 .ico（System.Drawing 这一侧）
    // 先要 256x256 那一帧，拿不到再退回默认帧
    // ==============================

    private static Icon? LoadIconFile(
        string iconPath)
    {

        try
        {

            if(!File.Exists(iconPath))
                return null;

            try
            {

                return new Icon(
                    iconPath,
                    new Size(
                        256,
                        256));

            }
            catch
            {

                return new Icon(iconPath);

            }

        }
        catch
        {

            return null;

        }

    }


    // ==============================
    // 系统默认"普通程序"图标（最后一层）
    // 借一个不存在的 .exe 名字问系统：得到的就是 exe 类型的默认大图标
    // ==============================

    public static Icon? GetDefaultAppIcon()
    {

        try
        {

            SHFILEINFO info =
                new SHFILEINFO();

            IntPtr result =
                SHGetFileInfo(
                    DefaultAppProbe,
                    FILE_ATTRIBUTE_NORMAL,
                    ref info,
                    (uint)Marshal.SizeOf(
                        typeof(SHFILEINFO)),
                    SHGFI_ICON |
                    SHGFI_LARGEICON |
                    SHGFI_USEFILEATTRIBUTES);

            if(result == IntPtr.Zero ||
               info.hIcon == IntPtr.Zero)
                return null;

            try
            {

                Icon wrapper =
                    Icon.FromHandle(info.hIcon);

                return (Icon)wrapper.Clone();

            }
            finally
            {

                DestroyIcon(info.hIcon);

            }

        }
        catch
        {

            return null;

        }

    }

    // ==============================
    // 体检
    // ==============================

    public static bool IsAcceptable(
        Icon? icon,
        int minSize)
    {

        try
        {

            if(icon == null)
                return false;

            if(icon.Width < minSize ||
               icon.Height < minSize)
                return false;

            return !IsBlankOrBlack(icon);

        }
        catch
        {

            return false;

        }

    }


    public static bool IsBlankOrBlack(
        Icon? icon)
    {

        if(icon == null)
            return true;

        try
        {

            using(Bitmap raw =
                icon.ToBitmap())
            {

                return IsBlankOrBlack(raw);

            }

        }
        catch
        {

            // 判不出来就当它是好的，别误杀正常图标

            return false;

        }

    }


    // 全透明 / 不透明全黑 / 不透明全白 都算坏图

    public static bool IsBlankOrBlack(
        Bitmap? bitmap)
    {

        if(bitmap == null ||
           bitmap.Width <= 0 ||
           bitmap.Height <= 0)
            return true;

        Bitmap argb =
            ToArgb(bitmap);

        try
        {

            BitmapData data =
                argb.LockBits(
                    new Rectangle(
                        0,
                        0,
                        argb.Width,
                        argb.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

            try
            {

                int stride =
                    Math.Abs(data.Stride);

                byte[] row =
                    new byte[stride];

                bool anyVisible =
                    false;

                bool allOpaque =
                    true;

                bool allBlack =
                    true;

                bool allWhite =
                    true;

                for(int y = 0; y < argb.Height; y++)
                {

                    Marshal.Copy(
                        IntPtr.Add(
                            data.Scan0,
                            y * data.Stride),
                        row,
                        0,
                        stride);

                    for(int x = 0; x < argb.Width; x++)
                    {

                        // 32bppArgb 的内存顺序就是 B G R A

                        int i =
                            x * 4;

                        byte b =
                            row[i];

                        byte g =
                            row[i + 1];

                        byte r =
                            row[i + 2];

                        byte a =
                            row[i + 3];

                        if(a != 0)
                            anyVisible = true;

                        if(a != 255)
                            allOpaque = false;

                        if(r > 8 || g > 8 || b > 8)
                            allBlack = false;

                        if(r < 247 || g < 247 || b < 247)
                            allWhite = false;

                    }

                }

                // 整张全透明

                if(!anyVisible)
                    return true;

                // 不透明且全黑 / 全白

                if(allOpaque && allBlack)
                    return true;

                if(allOpaque && allWhite)
                    return true;

                return false;

            }
            finally
            {

                argb.UnlockBits(data);

            }

        }
        finally
        {

            if(!ReferenceEquals(
                argb,
                bitmap))
                argb.Dispose();

        }

    }


    // 统一转成 32bppArgb，保证能按 BGRA 直接读像素

    private static Bitmap ToArgb(
        Bitmap source)
    {

        if(source.PixelFormat == PixelFormat.Format32bppArgb)
            return source;

        Bitmap copy =
            new Bitmap(
                source.Width,
                source.Height,
                PixelFormat.Format32bppArgb);

        using(Graphics g =
            Graphics.FromImage(copy))
        {

            g.Clear(
                Color.Transparent);

            g.DrawImage(
                source,
                new Rectangle(
                    0,
                    0,
                    copy.Width,
                    copy.Height));

        }

        return copy;

    }


    // 只有这几种文件才值得去做"资源提取"

    private static bool IsIconSourceFile(
        string? path)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(path) ||
               !File.Exists(path))
                return false;

            string ext =
                Path.GetExtension(path);

            foreach(string known in IconSourceExtensions)
            {

                if(string.Equals(
                    ext,
                    known,
                    StringComparison.OrdinalIgnoreCase))
                    return true;

            }

            return false;

        }
        catch
        {

            return false;

        }

    }

    // ==============================
    // 兼容老接口：提取图标并落成 PNG，返回文件路径（失败返回空串）
    // 现在走的是同一套兜底链，比原来的 ExtractIcon 单路可靠
    // ==============================

    public static string Extract(
        string filePath)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(filePath))
                return "";

            string folder =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Icons");

            if(!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            string savePath =
                Path.Combine(
                    folder,
                    Path.GetFileNameWithoutExtension(filePath) + ".png");

            if(File.Exists(savePath))
                return savePath;

            using(Icon? icon =
                GetBestIcon(filePath) ?? GetDefaultAppIcon())
            {

                if(icon == null)
                    return "";

                using(Bitmap bitmap =
                    IconToBitmap(icon))
                {

                    bitmap.Save(
                        savePath,
                        ImageFormat.Png);

                }

            }

            return savePath;

        }
        catch
        {

            return "";

        }

    }


    // 图标 → 32bppArgb 位图，保留透明通道

    private static Bitmap IconToBitmap(
        Icon icon)
    {

        Bitmap bitmap =
            new Bitmap(
                icon.Width,
                icon.Height,
                PixelFormat.Format32bppArgb);

        using(Graphics g =
            Graphics.FromImage(bitmap))
        {

            g.Clear(
                Color.Transparent);

            g.DrawIcon(
                icon,
                new Rectangle(
                    0,
                    0,
                    bitmap.Width,
                    bitmap.Height));

        }

        return bitmap;

    }


}

}

