using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GlassDock
{
    // ==========================
    // 玻璃材质（云母 / 亚克力）兼容层
    // --------------------------
    // Win11（Build >= 22000）：沿用原有最好效果 —— DWM 系统背板 + 深色模式
    // Win10（Build >= 17134）：降级 SetWindowCompositionAttribute 亚克力
    // Win10（1507 ~ 1709）    ：再降一级到 ACCENT_ENABLE_BLURBEHIND 模糊
    // 上面全部失败            ：半透明纯色背景兜底（不会黑屏 / 闪退）
    //
    // 全程 try-catch：材质设置失败只代表「没有材质」，
    // 绝不影响窗口打开，也不碰 Dock 的布局 / 动画 / 拖拽 / 图标 / 收纳。
    // ==========================

    public static class AcrylicHelper
    {

        // ==========================
        // DWM 属性索引
        // ==========================

        // 系统背板类型（公开 API，Win11 22621 起可用）
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // Win11 21H2（22000）只认这个未公开的云母开关
        private const int DWMWA_MICA_EFFECT = 1029;

        // 深色模式（Win10 1809 起是 20，更早的预览版是 19）
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;

        // 背板类型：2 = Mica（原来用的就是这个，保持不变）
        private const int DWMSBT_MAINWINDOW = 2;


        // ==========================
        // 窗口样式（判断是不是分层窗口用的）
        // ==========================

        private const int GWL_EXSTYLE = -20;

        private const int WS_EX_LAYERED = 0x00080000;


        // ==========================
        // SetWindowCompositionAttribute（Win10 的亚克力）
        // ==========================

        private const int WCA_ACCENT_POLICY = 19;

        // AccentState

        // 半透明纯色（最弱的一档，但一定安全）
        private const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;

        // Win10 1507 起：老 Aero 模糊
        private const int ACCENT_ENABLE_BLURBEHIND = 3;

        // Win10 1803 起：真正的亚克力
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        // AccentFlags：1 = 四条边都画
        private const int ACCENT_FLAG_DRAW_ALL_BORDERS = 1;


        // ==========================
        // 亚克力着色（低调深灰蓝，和 Dock 深色玻璃靠色）
        // ==========================

        private const int AccentTintAlpha = 0x80;   // 50%

        private const int AccentTintRed = 0x16;

        private const int AccentTintGreen = 0x1A;

        private const int AccentTintBlue = 0x24;


        // 兜底纯色背景的不透明度（0x00 全透明 ~ 0xFF 不透明）

        private const byte SolidFallbackAlpha = 0xB4;   // 70%


        // ==========================
        // 系统版本分界线
        // ==========================

        private const int Win10AcrylicBuild = 17134;    // 1803：才有真亚克力

        private const int Win10DarkModeBuild = 17763;   // 1809：才有深色模式属性

        private const int Win11Build = 22000;           // Win11 起点



        /// <summary>
        /// 分层窗口（AllowsTransparency = true）上硬开亚克力，
        /// 有些 Win10 版本会把整个窗口刷成不透明黑。
        /// 所以默认不给分层窗口开亚克力
        ///（Dock 的玻璃本来就是 XAML 里的玻璃层自己画的）。
        /// 如果你的机器实测没问题，把这里改成 true 再试。
        /// </summary>
        private static readonly bool AllowAccentOnLayeredWindow = false;



        /// <summary>
        /// 上一次实际生效的材质方案（排查问题时看它就行）
        /// </summary>
        public static string LastAppliedMode { get; private set; } = "未应用";



        /// <summary>
        /// 当前系统是不是 Win11（22000 起）
        /// </summary>
        public static bool IsWindows11
        {
            get
            {
                return GetWindowsBuild() >= Win11Build;
            }
        }


        // ==========================
        // P/Invoke
        // ==========================

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attr,
            ref int attrValue,
            int attrSize);


        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(
            IntPtr hwnd,
            ref WindowCompositionAttributeData data);


        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(
            IntPtr hwnd,
            int index);


        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(
            IntPtr hwnd,
            int index);


        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }



        // ==========================
        // 对外唯一入口
        // 签名和原来完全一样，现有调用点一行都不用改
        // ==========================

        public static void EnableMica(Window window)
        {
            if (window == null)
                return;


            // 材质属于锦上添花
            // 任何异常都只写进 LastAppliedMode，绝不往外抛
            // 否则窗口初始化就会直接崩掉（闪退）

            try
            {
                ApplyGlass(window);
            }
            catch (Exception ex)
            {
                LastAppliedMode =
                    "禁用(异常兜底)：" +
                    ex.GetType().Name;
            }
        }



        // ==========================
        // 真正干活的：按系统版本挑方案
        // ==========================

        private static void ApplyGlass(Window window)
        {
            IntPtr hwnd =
                GetHandle(window);


            // 句柄没建立起来（窗口还没 Show）就什么都别设
            // 免得把无效句柄传给系统

            if (hwnd == IntPtr.Zero)
            {
                LastAppliedMode =
                    "窗口句柄未就绪，本次跳过";

                return;
            }


            int build =
                GetWindowsBuild();


            // 透明窗口(AllowsTransparency = true)是分层窗口(WS_EX_LAYERED)
            // DWM 的系统背板(云母 / 亚克力)在分层窗口上不会生效，
            // 但个别系统版本仍会在窗口四周留下矩形背板痕迹，
            // 而 Dock 的外观是由 XAML 里的玻璃层自己画的，
            // 所以这里干脆不给分层窗口开背板（普通窗口照旧）。

            bool transparent =
                IsTransparencyAllowed(window);


            // ==========================
            // 一、Win11：沿用原有最好效果（DWM 系统背板 + 深色模式）
            // ==========================

            if (build >= Win11Build)
            {
                bool backdropOk = false;


                if (!transparent)
                {
                    backdropOk =
                        TrySetSystemBackdrop(hwnd);
                }


                // 深色模式和分层无关，能设就设
                // 设不上也只是标题栏配色不同

                TrySetDarkMode(
                    hwnd,
                    build);


                if (backdropOk)
                {
                    LastAppliedMode =
                        "Win11 云母(Mica 系统背板)";

                    return;
                }


                if (transparent)
                {
                    // 和改之前完全一致：
                    // 透明窗口不开背板，外观交给 XAML 玻璃层

                    LastAppliedMode =
                        "Win11 透明窗口(由 XAML 玻璃层绘制)";

                    return;
                }


                // 走到这里 = Win11 的普通窗口但背板调用失败
                //（DWM 被关掉 / 属性不被支持，比如 22000）
                // → 继续往下走亚克力降级，保证有材质可用
            }
            else
            {
                // Win10：老系统上这个属性设了也不认，设一下没坏处

                TrySetDarkMode(
                    hwnd,
                    build);
            }


            // ==========================
            // 二、Win10 降级：SetWindowCompositionAttribute
            // ==========================

            bool layered =
                transparent ||
                IsLayeredByStyle(hwnd);


            if (layered &&
                !AllowAccentOnLayeredWindow)
            {
                // 分层窗口不碰 accent
                // 外观继续交给 XAML 玻璃层（避免整窗刷黑）

                LastAppliedMode =
                    "透明窗口(由 XAML 玻璃层绘制，未开亚克力)";

                return;
            }


            if (TryApplyAccent(hwnd, build))
                return;


            // ==========================
            // 三、最后兜底：半透明纯色背景
            // ==========================

            ApplySolidFallback(window);
        }



        // ==========================
        // Win11：系统背板（云母）
        // ==========================

        private static bool TrySetSystemBackdrop(IntPtr hwnd)
        {
            // 22621+：公开 API，2 = Mica（原有最好效果）

            if (TrySetDwmInt(
                    hwnd,
                    DWMWA_SYSTEMBACKDROP_TYPE,
                    DWMSBT_MAINWINDOW))
                return true;


            // 22000（Win11 21H2）：只认这个未公开的云母开关

            if (TrySetDwmInt(
                    hwnd,
                    DWMWA_MICA_EFFECT,
                    1))
                return true;


            return false;
        }



        // ==========================
        // 深色模式（Win10 1809+ / Win11）
        // ==========================

        private static void TrySetDarkMode(
            IntPtr hwnd,
            int build)
        {
            if (build >= Win10DarkModeBuild)
            {
                if (TrySetDwmInt(
                        hwnd,
                        DWMWA_USE_IMMERSIVE_DARK_MODE,
                        1))
                    return;
            }


            // 旧编号兜底（系统认不出就什么都不做）

            TrySetDwmInt(
                hwnd,
                DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY,
                1);
        }



        // ==========================
        // Win10：亚克力 → 模糊 → 半透明纯色，逐级降
        // ==========================

        private static bool TryApplyAccent(
            IntPtr hwnd,
            int build)
        {
            int tint =
                MakeAbgr(
                    AccentTintAlpha,
                    AccentTintRed,
                    AccentTintGreen,
                    AccentTintBlue);


            // 1803（17134）起才有真正的亚克力

            if (build >= Win10AcrylicBuild &&
                TrySetAccent(
                    hwnd,
                    ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    tint))
            {
                LastAppliedMode =
                    "Win10 亚克力(SetWindowCompositionAttribute)";

                return true;
            }


            // 1507 起的 Aero 模糊（没有噪点，但至少有磨砂感）

            if (TrySetAccent(
                    hwnd,
                    ACCENT_ENABLE_BLURBEHIND,
                    tint))
            {
                LastAppliedMode =
                    build >= Win10AcrylicBuild
                        ? "Win10 模糊(亚克力不可用，已降级)"
                        : "Win10 模糊(系统版本低于 1803)";

                return true;
            }


            // 连模糊都不给：只留半透明纯色

            if (TrySetAccent(
                    hwnd,
                    ACCENT_ENABLE_TRANSPARENTGRADIENT,
                    tint))
            {
                LastAppliedMode =
                    "Win10 半透明纯色(Accent)";

                return true;
            }


            return false;
        }


        // ==========================
        // SetWindowCompositionAttribute 单次调用
        // 返回 false = 系统不认这一档（老系统上是常事）
        // ==========================

        private static bool TrySetAccent(
            IntPtr hwnd,
            int accentState,
            int gradientColor)
        {
            IntPtr buffer = IntPtr.Zero;


            try
            {
                var policy = new AccentPolicy
                {
                    AccentState = accentState,
                    AccentFlags = ACCENT_FLAG_DRAW_ALL_BORDERS,
                    GradientColor = gradientColor,
                    AnimationId = 0
                };


                int size =
                    Marshal.SizeOf(typeof(AccentPolicy));


                buffer =
                    Marshal.AllocHGlobal(size);


                Marshal.StructureToPtr(
                    policy,
                    buffer,
                    false);


                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = buffer,
                    SizeOfData = size
                };


                int result =
                    SetWindowCompositionAttribute(
                        hwnd,
                        ref data);


                return result != 0;
            }
            catch
            {
                // 老系统上 user32 里可能根本没有这个入口
                // 或者结构体封送失败

                return false;
            }
            finally
            {
                // 非托管内存一定要还回去

                if (buffer != IntPtr.Zero)
                {
                    try
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                    catch
                    {
                        // 释放失败也没什么可做的，忽略
                    }
                }
            }
        }



        // ==========================
        // 最后的兜底：半透明纯色背景
        // 绝不覆盖 XAML 自己画好的玻璃层
        // ==========================

        private static void ApplySolidFallback(Window window)
        {
            try
            {
                // 改窗口属性必须回 UI 线程
                // 免得跨线程操作 UI 抛异常

                if (!window.Dispatcher.CheckAccess())
                {
                    window.Dispatcher.Invoke(
                        new Action(
                            delegate
                            {
                                ApplySolidFallback(window);
                            }));

                    return;
                }


                var current =
                    window.Background as SolidColorBrush;


                bool backgroundIsEmpty =
                    window.Background == null ||
                    (current != null &&
                     current.Color.A == 0);


                // 窗口本来就有背景（Dock 的玻璃是 XAML 画的）
                // 就不动它，免得把好看的效果盖成一坨纯色

                if (!backgroundIsEmpty)
                {
                    LastAppliedMode =
                        "保留 XAML 自带背景(未做纯色兜底)";

                    return;
                }


                var brush = new SolidColorBrush(
                    Color.FromArgb(
                        SolidFallbackAlpha,
                        (byte)AccentTintRed,
                        (byte)AccentTintGreen,
                        (byte)AccentTintBlue));


                brush.Freeze();


                window.Background = brush;


                LastAppliedMode =
                    "半透明纯色背景(材质 API 全部失败)";
            }
            catch (Exception ex)
            {
                // 兜底都失败，窗口照样能正常打开（透明而已）
                // 不影响启动，也不影响任何功能

                LastAppliedMode =
                    "纯色兜底失败：" +
                    ex.GetType().Name;
            }
        }


        // ==========================
        // 小工具
        // ==========================

        private static IntPtr GetHandle(Window window)
        {
            try
            {
                return
                    new WindowInteropHelper(
                        window)
                    .Handle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }



        private static int GetWindowsBuild()
        {
            try
            {
                // Win11 = 22000 起；Win10 = 10240 ~ 19045

                return
                    Environment.OSVersion.Version.Build;
            }
            catch
            {
                return 0;
            }
        }



        // 窗口是不是「透明窗口」
        // 读不到就当成透明（宁可不做背板，也不留矩形痕迹）

        private static bool IsTransparencyAllowed(Window window)
        {
            try
            {
                return
                    window.AllowsTransparency;
            }
            catch
            {
                return true;
            }
        }



        // 系统层面再确认一次是不是分层窗口
        //（有的窗口是被别处代码手工加上 WS_EX_LAYERED 的）

        private static bool IsLayeredByStyle(IntPtr hwnd)
        {
            try
            {
                IntPtr exStyle =
                    IntPtr.Size == 8
                        ? GetWindowLongPtr64(
                            hwnd,
                            GWL_EXSTYLE)
                        : new IntPtr(
                            GetWindowLong32(
                                hwnd,
                                GWL_EXSTYLE));


                return
                    (exStyle.ToInt64() & WS_EX_LAYERED) != 0;
            }
            catch
            {
                // 查不到就当不是分层窗口
                // 上面还有 AllowsTransparency 兜着

                return false;
            }
        }



        // 设一个 DWM 整型属性
        // S_OK == 0；系统不支持的属性会给 E_INVALIDARG

        private static bool TrySetDwmInt(
            IntPtr hwnd,
            int attribute,
            int value)
        {
            try
            {
                int data = value;


                int hr =
                    DwmSetWindowAttribute(
                        hwnd,
                        attribute,
                        ref data,
                        sizeof(int));


                return hr == 0;
            }
            catch
            {
                // 老系统上 dwmapi.dll 里没有这个入口

                return false;
            }
        }



        // SetWindowCompositionAttribute 的 GradientColor 是 ABGR

        private static int MakeAbgr(
            int alpha,
            int red,
            int green,
            int blue)
        {
            return (alpha << 24) |
                   (blue << 16) |
                   (green << 8) |
                   red;
        }



    }
}
