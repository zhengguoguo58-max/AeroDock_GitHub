using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GlassDock
{

// ==========================
// AeroDock 设置中心
// ==========================
// 入口：系统托盘右键菜单 →「AeroDock设置中心」
// 形态：独立 Window，无边框 + 透明背景 + 液态玻璃 + 圆角
// 内容：原来 Dock 齿轮面板里的滑块与选项，样式一套不变
// 存档：%AppData%\GlassDock\Settings.json（与 Dock 完全同一个文件）
//       opacity  背景透明度 / blur 背景模糊度（与原来同样的键、同样的写法）
//       materialPreset 材质预设（0 经典亚克力 / 1 极简纯色 / 2 高光质感）
//       locked   锁定位置 / wallpaperTint 壁纸取色（本次一并存下来）
// 记忆：ValueChanged / Checked 时写 JSON，Window_Loaded 时读 JSON 并回写到控件
// 关闭：只隐藏窗口，程序继续在托盘里活
// 核心：图标提取 / 拖拽 / 九点阵 / 悬停提示 / 桌面双模式 /
//       挤出动画 / 右键删除：一行未改
//       本窗口只做两件事：读写存档 + 把值转发给 MainWindow 的公开接口
// ==========================

public partial class SettingsWindow : Window
{

    // Dock 主窗口：所有“实时生效”都转给它

    private readonly MainWindow dock;


    // 存档：与 MainWindow 完全同一个文件

    private static readonly string StorePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GlassDock",
            "Settings.json");


    // 界面上的当前值

    private double opacityValue = 1.0;

    private double blurValue = 0.0;


    // 材质预设：0 = 经典亚克力，1 = 极简纯色，2 = 高光质感
    // 与 Dock 共用 Settings.json 里的 materialPreset 数值键

    private int materialPresetValue = 0;


    // 展板材质：与 Dock 完全独立

    private double launchBoardOpacityValue = 0.8;

    private double launchBoardBlurValue = 30.0;


    // ==========================
    // 展板自定义壁纸
    // ==========================
    // 单独存一份 JSON：%AppData%\GlassDock\LaunchBoardWallpaper.json
    // 不塞进 Settings.json：
    // 那个文件全项目都按 Dictionary<string, double> 读写，
    // 加字符串路径会让旧解析失败。
    // 内容：path（绝对路径）+ opacity（壁纸层不透明度）
    // ==========================

    private static readonly string WallpaperStorePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GlassDock",
            "LaunchBoardWallpaper.json");


    private string launchBoardWallpaperPath = "";

    private double launchBoardWallpaperOpacityValue = 0.8;


    // ==========================
    // 展板壁纸裁剪：缩放 + 平移
    // ==========================
    // Scale 范围 1.0 - 5.0（1.0 = 铺满，不露出空白）；
    // OffsetX / OffsetY 是预览框坐标系里的像素位移；
    // ReferenceWidth / ReferenceHeight 是预览框参考尺寸，
    // 主窗口按同样的比例把位移换算到展板上，两边裁切范围一致。

    private double launchBoardWallpaperScaleValue = 1.0;

    private double launchBoardWallpaperOffsetXValue = 0.0;

    private double launchBoardWallpaperOffsetYValue = 0.0;

    private double launchBoardWallpaperCropReferenceWidth =
        WallpaperCropReferenceWidth;

    private double launchBoardWallpaperCropReferenceHeight =
        WallpaperCropReferenceHeight;

    // 预览框与展板必须同比例，拖动范围才能覆盖展板裁掉的全部边缘。
    // 展板固定 900 x 650，预览宽度取 300，高度按同一比例换算。

    private const double WallpaperCropReferenceWidth = 300.0;

    private const double WallpaperCropReferenceHeight =
        300.0 * 650.0 / 900.0;

    private const double WallpaperCropScaleMin = 1.0;

    private const double WallpaperCropScaleMax = 5.0;

    private const double WallpaperCropOffsetLimit = 10000.0;

    private const double WallpaperCropWheelStep = 1.1;


    // 预览矩形实际使用的画刷：
    // 通过 Viewbox 裁源图，而不是 RenderTransform 缩放已经裁过的位图

    private ImageBrush? launchBoardWallpaperPreviewBrush = null;


    // 拖动状态：按下时记住起点与当时的偏移

    private bool isWallpaperPreviewDragging = false;

    private Point wallpaperPreviewDragStartPoint = new Point(0, 0);

    private double wallpaperPreviewDragStartOffsetX = 0.0;

    private double wallpaperPreviewDragStartOffsetY = 0.0;


    // 已加载到预览框的路径，避免每次回填都重新解码

    private string wallpaperPreviewLoadedPath = "";


    // 正在读盘 / 正在回填控件：这期间不许落盘

    private bool isLoading = false;


    // 第一次读盘完成之前，任何变化都不落盘
    // （XAML 里给 Slider 设初值也会触发 ValueChanged）

    private bool isLoadedFromStore = false;


    // 代码回写开关 / 药丸时，别再绕回来触发一次

    private bool isSyncingToggles = false;


    // 程序正在退出（托盘「退出软件」走 Application.Current.Shutdown）
    // 这时窗口不能再拦着不关，否则隐藏动作会和退出流程打架

    private bool isAppShuttingDown = false;


    // 拖动滑块时不写盘，停手约 0.35 秒写一次

    private DispatcherTimer? saveTimer = null;

    private const double SaveDelay = 350;


    public SettingsWindow(
        MainWindow dockWindow)
    {

        InitializeComponent();

        dock = dockWindow;


        // 退出软件时不拦门：
        // MainWindow 的退出路径会先调 CloseForShutdown()，
        // 它把 isAppShuttingDown 置 true 再 Close()，
        // OnClosing 一见标记就直接放行（见下方 OnClosing）
        // 注：Application 没有公开的 ShutdownStarted 事件（CS1061），
        // 所以放行必须由退出路径显式触发，不能靠订阅

    }


    // ==========================
    // Window_Loaded：读 JSON → 回写到控件 → 应用给 Dock
    // ==========================

    private void SettingsWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {

        LoadFromStore();

    }


    // 托盘每次打开都重新调一遍，界面上显示的一定是最新值

    public void ReloadFromStore()
    {

        LoadFromStore();

    }


    private void LoadFromStore()
    {

        // 壁纸是独立 JSON：先回填控件，最后统一应用到主窗口
        // 不走 Settings.json，不碰上面任何材质 / 开关的读写

        LoadWallpaperFromStore();


        // 默认值：先取 Dock 当前真正生效的状态
        // 存档里有就用存档里的（存档才是“重启不丢”的记忆）

        double opacity = opacityValue;

        double blur = blurValue;


        // 展板材质：与 Dock 完全独立
        // 默认 0.8 / 30；主窗口已从存档恢复过，这里先取它当前的值

        double launchBoardOpacity =
            dock != null
            ? dock.SettingsCenter_LaunchBoardOpacity
            : 0.8;

        double launchBoardBlur =
            dock != null
            ? dock.SettingsCenter_LaunchBoardBlur
            : 30.0;

        bool locked =
            dock != null &&
            dock.SettingsCenter_IsLocked;

        bool tinted =
            dock != null &&
            dock.SettingsCenter_IsWallpaperTinted;


        // 材质预设：默认沿用 Dock 当前真正生效的那一档
        // 存档里有 materialPreset 就用存档（重启不丢）

        int materialPreset =
            dock != null
            ? dock.SettingsCenter_MaterialPreset
            : materialPresetValue;


        // 点击空白处关闭展板：默认开启
        // 存档里沿用现有的 0 / 1 写法，键缺失时保持原来的行为

        bool closeBoardOnOutsideClick =
            true;


        try
        {

            if(File.Exists(StorePath))
            {

                string json =
                    File.ReadAllText(
                        StorePath);

                if(!string.IsNullOrWhiteSpace(json))
                {

                    Dictionary<string, double>? data =
                        System.Text.Json.JsonSerializer
                            .Deserialize<Dictionary<string, double>>(
                                json);

                    if(data != null)
                    {

                        double value;

                        if(data.TryGetValue("opacity", out value))
                            opacity = value;

                        if(data.TryGetValue("blur", out value))
                            blur = value;

                        if(data.TryGetValue("locked", out value))
                            locked = value > 0.5;

                        if(data.TryGetValue("wallpaperTint", out value))
                            tinted = value > 0.5;

                        if(data.TryGetValue("closeBoardOnOutsideClick", out value))
                            closeBoardOnOutsideClick = value > 0.5;

                        // 展板材质：键名与用户配置保持一致
                        // 缺键时保持默认 0.8 / 30

                        if(data.TryGetValue("LaunchBoardOpacity", out value))
                            launchBoardOpacity = value;

                        if(data.TryGetValue("LaunchBoardBlur", out value))
                            launchBoardBlur = value;

                        // 材质预设：0 / 1 / 2，旧存档没有这个键时保持默认

                        if(data.TryGetValue("materialPreset", out value))
                        {
                            materialPreset =
                                (int)Math.Round(
                                    ClampSetting(
                                        value,
                                        0.0,
                                        2.0,
                                        0.0));
                        }

                    }

                }

            }

        }
        catch(Exception ex)
        {

            // 文件坏了 / 被锁住 / 不是 JSON：
            // 当没存过，回默认值，绝不弹窗挡人

            Debug.WriteLine(
                "设置中心：读取 Settings.json 失败: "
                +
                ex.Message);

        }


        // 1) 回写材质两个滑块（回写期间不落盘）

        if(saveTimer != null)
            saveTimer.Stop();

        isLoading =
            true;

        try
        {

            opacity =
                ClampSetting(
                    opacity,
                    OpacitySlider.Minimum,
                    OpacitySlider.Maximum,
                    1.0);

            blur =
                ClampSetting(
                    blur,
                    BlurSlider.Minimum,
                    BlurSlider.Maximum,
                    0.0);

            launchBoardOpacity =
                ClampSetting(
                    launchBoardOpacity,
                    LaunchBoardOpacitySlider.Minimum,
                    LaunchBoardOpacitySlider.Maximum,
                    0.8);

            launchBoardBlur =
                ClampSetting(
                    launchBoardBlur,
                    LaunchBoardBlurSlider.Minimum,
                    LaunchBoardBlurSlider.Maximum,
                    30.0);

            opacityValue =
                opacity;

            blurValue =
                blur;

            launchBoardOpacityValue =
                launchBoardOpacity;

            launchBoardBlurValue =
                launchBoardBlur;

            OpacitySlider.Value =
                opacity;

            BlurSlider.Value =
                blur;

            LaunchBoardOpacitySlider.Value =
                launchBoardOpacity;

            LaunchBoardBlurSlider.Value =
                launchBoardBlur;

            OpacityText.Text =
                opacity.ToString("0.00");

            BlurText.Text =
                ((int)Math.Round(blur)).ToString();

            LaunchBoardOpacityText.Text =
                launchBoardOpacity.ToString("0.00");

            LaunchBoardBlurText.Text =
                ((int)Math.Round(launchBoardBlur)).ToString();


            // 材质预设：回填下拉框
            // 此时 isLoading 还是 true，SelectionChanged 不会重复应用 / 写盘
            // 真正的应用在下面 ApplyMaterialPresetToDock() 里统一做一次

            materialPresetValue =
                materialPreset;

            if(MaterialPresetCombo != null)
                MaterialPresetCombo.SelectedIndex =
                    materialPreset;

        }
        finally
        {

            isLoading =
                false;

        }

        isLoadedFromStore =
            true;


        // 2) 立刻应用给 Dock（走原来那套 ApplyDockSettings）

        ApplyToDock();


        // 2.5) 材质预设：与两个滑块互不覆盖，单独应用一次
        // 下拉框回填期间事件被 isLoading 挡掉了，所以这里必须显式应用

        ApplyMaterialPresetToDock();


        // 3) 展板材质也立刻应用（与 Dock 完全独立）

        ApplyLaunchBoardToDock();


        // 4) 四个开关 / 药丸跟着对齐

        SyncToggles(
            locked,
            tinted,
            closeBoardOnOutsideClick);


        // 5) 展板壁纸：路径 / 不透明度也立刻应用到主窗口
        // 没选壁纸时这里会保持壁纸层收起，不会影响原有材质

        ApplyLaunchBoardWallpaperToDock();

    }

    // ==========================
    // 实时生效：转发给 MainWindow
    // ==========================

    private void ApplyToDock()
    {

        if(dock == null)
            return;

        try
        {

            // 原来就有的 ApplyDockSettings：夹区间 + 刷新材质
            // 它会把 Dock 自己那两个滑块也同步过去，两边永远一致

            dock.SettingsCenter_ApplyMaterial(
                opacityValue,
                blurValue);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：应用材质设置失败: "
                +
                ex.Message);

        }

    }


    // 材质预设：下拉框改变 / 启动读档后，转发给 MainWindow
    // 只改材质，不碰 opacity / blur 两个滑块的值

    private void ApplyMaterialPresetToDock()
    {

        if(dock == null)
            return;


        try
        {

            dock.SettingsCenter_ApplyMaterialPreset(
                materialPresetValue);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：应用材质预设失败: "
                + ex.Message);

        }

    }


    // 材质预设下拉框：用户切换时立刻预览 + 自动保存
    // 启动回填 / 读盘期间由 isLoading 挡住，不会误触发

    private void MaterialPresetCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {

        if(!isLoadedFromStore ||
           isLoading)
            return;


        int preset =
            0;

        if(MaterialPresetCombo != null)
            preset =
                MaterialPresetCombo.SelectedIndex;


        if(preset < 0 ||
           preset > 2)
            preset = 0;


        materialPresetValue =
            preset;


        ApplyMaterialPresetToDock();


        ScheduleSave();

    }


    // 展板材质：只转发展板自己的两个值
    // 不碰 Dock 的 opacity / blur，两个滑块完全独立

    private void ApplyLaunchBoardToDock()
    {

        if(dock == null)
            return;


        try
        {

            dock.SettingsCenter_ApplyLaunchBoardMaterial(
                launchBoardOpacityValue,
                launchBoardBlurValue);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：应用展板材质设置失败: "
                +
                ex.Message);

        }

    }


    // 展板壁纸：只转发壁纸自己的路径 / 不透明度
    // 不碰 launchBoardOpacityValue / launchBoardBlurValue
    // MainWindow 内部用 ReadAllBytes + MemoryStream + OnLoad 解码，
    // 全程不占用图片文件

    private bool ApplyLaunchBoardWallpaperToDock()
    {

        if(dock == null)
        {

            MessageBox.Show(
                "主窗口尚未初始化，壁纸无法应用。\n" +
                "请关闭设置窗口后重新打开再试。",
                "展板壁纸",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;

        }


        try
        {

            bool applied =
                dock.SettingsCenter_ApplyLaunchBoardWallpaper(
                    launchBoardWallpaperPath,
                    launchBoardWallpaperOpacityValue);


            // 壁纸成功铺上之后，立刻把裁剪参数同步过去
            // 这样启动恢复 / 重新打开设置中心时，
            // 展板上的位置和预览框完全一致

            if(applied)
            {
                SyncLaunchBoardWallpaperCropToDock();
            }


            return applied;

        }
        catch(Exception ex)
        {

            // MainWindow 内部已经处理的错误不会走到这里；
            // 这里是最后一道兜底，绝不静默失败

            Debug.WriteLine(
                "设置中心：应用展板壁纸失败: "
                +
                ex.Message);


            MessageBox.Show(
                "应用展板壁纸失败。\n\n" +
                "文件：" +
                launchBoardWallpaperPath +
                "\n\n" +
                "错误类型：" +
                ex.GetType().Name +
                "\n" +
                "详细原因：" +
                ex.Message,
                "展板壁纸",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;

        }

    }


    // 路径文本 + 清除按钮状态
    // 没选壁纸时显示“未选择壁纸”，清除按钮置灰

    private void UpdateLaunchBoardWallpaperUi()
    {

        bool hasPath =
            !string.IsNullOrWhiteSpace(
                launchBoardWallpaperPath);


        if(LaunchBoardWallpaperPathText != null)
        {

            LaunchBoardWallpaperPathText.Text =
                hasPath
                ? launchBoardWallpaperPath
                : "未选择壁纸";

            LaunchBoardWallpaperPathText.ToolTip =
                hasPath
                ? launchBoardWallpaperPath
                : null;

        }


        if(LaunchBoardWallpaperClearButton != null)
        {

            LaunchBoardWallpaperClearButton.IsEnabled =
                hasPath;

        }


        // 路径变化后同步刷新 300x216.67 裁剪预览
        // 内部全程 try-catch，解码失败只显示提示文字

        UpdateLaunchBoardWallpaperPreview();

    }


    // ==========================
    // 展板壁纸：独立 JSON 读写
    // ==========================
    // 读写全部 try-catch：
    // 配置文件被手改坏 / 图片被移走 / 目录没权限
    // 都会明确弹窗说明原因，不再静默失败

    private void LoadWallpaperFromStore()
    {

        string path = "";

        double opacity =
            launchBoardWallpaperOpacityValue;

        double scale =
            launchBoardWallpaperScaleValue;

        double offsetX =
            launchBoardWallpaperOffsetXValue;

        double offsetY =
            launchBoardWallpaperOffsetYValue;

        double referenceWidth =
            launchBoardWallpaperCropReferenceWidth;

        double referenceHeight =
            launchBoardWallpaperCropReferenceHeight;


        if(saveTimer != null)
            saveTimer.Stop();


        try
        {

            if(File.Exists(WallpaperStorePath))
            {

                string json =
                    File.ReadAllText(
                        WallpaperStorePath);

                if(!string.IsNullOrWhiteSpace(json))
                {

                    using JsonDocument document =
                        JsonDocument.Parse(
                            json);

                    JsonElement root =
                        document.RootElement;

                    if(root.ValueKind ==
                       JsonValueKind.Object)
                    {

                        if(root.TryGetProperty(
                               "path",
                               out JsonElement pathElement) &&
                           pathElement.ValueKind ==
                           JsonValueKind.String)
                        {

                            path =
                                pathElement.GetString() ??
                                "";

                        }


                        if(root.TryGetProperty(
                               "opacity",
                               out JsonElement opacityElement) &&
                           opacityElement.ValueKind ==
                           JsonValueKind.Number &&
                           opacityElement.TryGetDouble(
                               out double value))
                        {

                            opacity =
                                value;

                        }


                        // 裁剪参数：旧配置没有这些键时保持默认 1.0 / 0 / 0

                        scale =
                            ReadWallpaperCropNumber(
                                root,
                                "scale",
                                scale);

                        offsetX =
                            ReadWallpaperCropNumber(
                                root,
                                "offsetX",
                                offsetX);

                        offsetY =
                            ReadWallpaperCropNumber(
                                root,
                                "offsetY",
                                offsetY);

                        referenceWidth =
                            ReadWallpaperCropNumber(
                                root,
                                "referenceWidth",
                                referenceWidth);

                        referenceHeight =
                            ReadWallpaperCropNumber(
                                root,
                                "referenceHeight",
                                referenceHeight);

                    }

                }

            }

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：读取 LaunchBoardWallpaper.json 失败: "
                +
                ex.Message);


            MessageBox.Show(
                "读取展板壁纸配置失败。\n\n" +
                "配置文件：" +
                WallpaperStorePath +
                "\n\n" +
                "错误类型：" +
                ex.GetType().Name +
                "\n" +
                "详细原因：" +
                ex.Message,
                "展板壁纸",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        }


        // 回填控件期间不落盘、不实时应用
        // 真正的应用在 LoadFromStore 最后统一做

        isLoading =
            true;

        try
        {

            launchBoardWallpaperPath =
                path.Trim();

            launchBoardWallpaperOpacityValue =
                ClampSetting(
                    opacity,
                    0.0,
                    1.0,
                    0.8);


            // 裁剪参数：全部夹回安全区间
            // 参考尺寸为 0 时回退到预览框的 300 x 216.67，绝不拿 0 去做除数

            launchBoardWallpaperScaleValue =
                ClampSetting(
                    scale,
                    WallpaperCropScaleMin,
                    WallpaperCropScaleMax,
                    1.0);

            launchBoardWallpaperOffsetXValue =
                ClampSetting(
                    offsetX,
                    -WallpaperCropOffsetLimit,
                    WallpaperCropOffsetLimit,
                    0.0);

            launchBoardWallpaperOffsetYValue =
                ClampSetting(
                    offsetY,
                    -WallpaperCropOffsetLimit,
                    WallpaperCropOffsetLimit,
                    0.0);

            launchBoardWallpaperCropReferenceWidth =
                ClampSetting(
                    referenceWidth,
                    1.0,
                    10000.0,
                    WallpaperCropReferenceWidth);

            launchBoardWallpaperCropReferenceHeight =
                ClampSetting(
                    referenceHeight,
                    1.0,
                    10000.0,
                    WallpaperCropReferenceHeight);


            // 旧版本参考框是 300x200（3:2），与展板 900x650 不同比例，
            // 拖到边上仍会差一截。这里统一换算到与展板同比例的新参考框；
            // 位移按旧参考尺寸等比缩放，用户原来的取景中心基本不变。

            launchBoardWallpaperOffsetXValue *=
                WallpaperCropReferenceWidth /
                launchBoardWallpaperCropReferenceWidth;

            launchBoardWallpaperOffsetYValue *=
                WallpaperCropReferenceHeight /
                launchBoardWallpaperCropReferenceHeight;


            launchBoardWallpaperCropReferenceWidth =
                WallpaperCropReferenceWidth;

            launchBoardWallpaperCropReferenceHeight =
                WallpaperCropReferenceHeight;


            if(LaunchBoardWallpaperOpacitySlider != null)
            {

                LaunchBoardWallpaperOpacitySlider.Value =
                    launchBoardWallpaperOpacityValue;

            }

            if(LaunchBoardWallpaperOpacityText != null)
            {

                LaunchBoardWallpaperOpacityText.Text =
                    launchBoardWallpaperOpacityValue.ToString(
                        "0.00");

            }

            UpdateLaunchBoardWallpaperUi();

        }
        finally
        {

            isLoading =
                false;

        }

    }


    private void SaveWallpaperSettings()
    {

        // 从没选过壁纸、也从没生成过文件：不凭空建文件
        // 清除壁纸时文件已存在，会正常写入空路径，下次启动即保持清除

        if(string.IsNullOrWhiteSpace(
               launchBoardWallpaperPath) &&
           !File.Exists(
               WallpaperStorePath))
            return;


        try
        {

            string? dir =
                Path.GetDirectoryName(
                    WallpaperStorePath);

            if(!string.IsNullOrWhiteSpace(dir) &&
               !Directory.Exists(dir))
            {

                Directory.CreateDirectory(
                    dir);

            }


            var data =
                new
                {

                    path =
                        launchBoardWallpaperPath,

                    opacity =
                        launchBoardWallpaperOpacityValue,


                    // 裁剪参数：缩放 + 平移 + 预览框参考尺寸
                    // 旧版本读到多余键会忽略，向后兼容

                    scale =
                        launchBoardWallpaperScaleValue,

                    offsetX =
                        launchBoardWallpaperOffsetXValue,

                    offsetY =
                        launchBoardWallpaperOffsetYValue,

                    referenceWidth =
                        launchBoardWallpaperCropReferenceWidth,

                    referenceHeight =
                        launchBoardWallpaperCropReferenceHeight

                };


            string json =
                JsonSerializer.Serialize(
                    data,
                    new JsonSerializerOptions
                    {

                        WriteIndented = true,

                        Encoder =
                            JavaScriptEncoder
                                .UnsafeRelaxedJsonEscaping

                    });


            File.WriteAllText(
                WallpaperStorePath,
                json);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：保存 LaunchBoardWallpaper.json 失败: "
                +
                ex.Message);


            MessageBox.Show(
                "保存展板壁纸配置失败，下次启动可能恢复不了这张壁纸。\n\n" +
                "配置文件：" +
                WallpaperStorePath +
                "\n\n" +
                "错误类型：" +
                ex.GetType().Name +
                "\n" +
                "详细原因：" +
                ex.Message,
                "展板壁纸",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        }

    }


    // ==========================
    // 展板壁纸：选择 / 清除 / 不透明度
    // ==========================

    private void LaunchBoardWallpaperChooseButton_Click(
        object sender,
        RoutedEventArgs e)
    {

        OpenFileDialog dialog =
            new OpenFileDialog
            {

                Title = "选择展板壁纸",

                Filter =
                    "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*",

                CheckFileExists = true,

                Multiselect = false,

                RestoreDirectory = true

            };


        bool? dialogResult;

        try
        {

            dialogResult =
                dialog.ShowDialog(
                    this);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：打开壁纸选择框失败: " +
                ex.Message);


            MessageBox.Show(
                "打开图片选择框失败。\n\n" +
                "错误类型：" +
                ex.GetType().Name +
                "\n" +
                "详细原因：" +
                ex.Message,
                "展板壁纸",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;

        }


        if(dialogResult != true)
            return;


        string previousPath =
            launchBoardWallpaperPath;


        double previousScale =
            launchBoardWallpaperScaleValue;

        double previousOffsetX =
            launchBoardWallpaperOffsetXValue;

        double previousOffsetY =
            launchBoardWallpaperOffsetYValue;


        launchBoardWallpaperPath =
            dialog.FileName;

        wallpaperPreviewLoadedPath =
            "";


        // 换了新图就从 1.0 居中重新开始裁
        // 选的是同一张图则保留当前裁剪位置

        if(!string.Equals(
               previousPath,
               launchBoardWallpaperPath,
               StringComparison.OrdinalIgnoreCase))
        {

            ResetWallpaperCropValues();

        }


        // 立即应用（MainWindow 内部解码不锁文件）
        // 解码失败就恢复上一张，不把坏路径写进配置

        bool applied =
            ApplyLaunchBoardWallpaperToDock();


        if(!applied)
        {

            launchBoardWallpaperPath =
                previousPath;

            launchBoardWallpaperScaleValue =
                previousScale;

            launchBoardWallpaperOffsetXValue =
                previousOffsetX;

            launchBoardWallpaperOffsetYValue =
                previousOffsetY;


            // 失败时 MainWindow 已经清掉了壁纸层
            // 这里把上一张重新铺回去
            // dock 为空时不要再调用，避免重复弹同一个提示框

            if(dock != null)
            {

                ApplyLaunchBoardWallpaperToDock();

            }


            UpdateLaunchBoardWallpaperUi();

            if(LaunchBoardWallpaperPathText != null)
            {

                LaunchBoardWallpaperPathText.Text =
                    "壁纸加载失败，请换一张图片（当前系统可能不支持该格式）";

            }

            return;

        }


        UpdateLaunchBoardWallpaperUi();

        SaveWallpaperSettings();

    }


    private void LaunchBoardWallpaperClearButton_Click(
        object sender,
        RoutedEventArgs e)
    {

        string previousPath =
            launchBoardWallpaperPath;


        double previousScale =
            launchBoardWallpaperScaleValue;

        double previousOffsetX =
            launchBoardWallpaperOffsetXValue;

        double previousOffsetY =
            launchBoardWallpaperOffsetYValue;


        launchBoardWallpaperPath = "";


        // 清图后裁剪参数也归零，下次换图从居中开始

        ResetWallpaperCropValues();


        bool cleared =
            false;


        try
        {

            if(dock == null)
            {

                MessageBox.Show(
                    "主窗口尚未初始化，壁纸无法清除。\n" +
                    "请关闭设置窗口后重新打开再试。",
                    "展板壁纸",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

            }
            else
            {

                cleared =
                    dock.SettingsCenter_ClearLaunchBoardWallpaper();

            }

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：清除展板壁纸失败: " +
                ex.Message);


            MessageBox.Show(
                "清除展板壁纸失败。\n\n" +
                "错误类型：" +
                ex.GetType().Name +
                "\n" +
                "详细原因：" +
                ex.Message,
                "展板壁纸",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        }


        if(!cleared)
        {

            // 没清干净：恢复上一张的路径和界面状态
            // 不写配置，避免出现“配置清空、画面还在”的错位

            launchBoardWallpaperPath =
                previousPath;

            launchBoardWallpaperScaleValue =
                previousScale;

            launchBoardWallpaperOffsetXValue =
                previousOffsetX;

            launchBoardWallpaperOffsetYValue =
                previousOffsetY;


            if(dock != null &&
               !string.IsNullOrWhiteSpace(previousPath))
            {

                ApplyLaunchBoardWallpaperToDock();

            }


            UpdateLaunchBoardWallpaperUi();

            return;

        }


        UpdateLaunchBoardWallpaperUi();

        SaveWallpaperSettings();

    }


    private void LaunchBoardWallpaperOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {

        launchBoardWallpaperOpacityValue =
            e.NewValue;


        if(LaunchBoardWallpaperOpacityText != null)
        {

            LaunchBoardWallpaperOpacityText.Text =
                e.NewValue.ToString("0.00");

        }


        // 读盘回填期间只更新文字，不发应用命令

        if(!isLoading &&
           dock != null)
        {

            dock.SettingsCenter_ApplyLaunchBoardWallpaperOpacity(
                e.NewValue);

        }


        ScheduleSave();

    }


    // ==========================
    // 展板壁纸裁剪：预览 / 实时同步 / 持久化
    // ==========================

    // 从 JSON 里读一个数字：
    // 键缺失 / 类型不对 / NaN / Infinity 全部回退默认值
    private static double ReadWallpaperCropNumber(
        JsonElement root,
        string name,
        double fallback)
    {

        try
        {

            if(root.ValueKind != JsonValueKind.Object)
                return fallback;

            if(!root.TryGetProperty(
                   name,
                   out JsonElement element) ||
               element.ValueKind != JsonValueKind.Number)
                return fallback;

            if(!element.TryGetDouble(
                   out double value) ||
               double.IsNaN(value) ||
               double.IsInfinity(value))
                return fallback;

            return value;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：读取壁纸裁剪参数失败: "
                + ex.Message);

            return fallback;

        }

    }


    // 把路径对应的图片加载到 300x216.67 预览框
    // 解码失败只显示提示文字，不弹窗、不崩
    private void UpdateLaunchBoardWallpaperPreview()
    {

        try
        {

            if(LaunchBoardWallpaperPreviewSurface == null)
                return;


            string path =
                (launchBoardWallpaperPath ?? "").Trim();

            bool hasFile =
                path.Length > 0 &&
                File.Exists(path);


            if(!hasFile)
            {

                wallpaperPreviewLoadedPath = "";

                ClearWallpaperPreviewImage();

                SetWallpaperPreviewEmptyText(
                    true,
                    path.Length == 0
                    ? "选择壁纸后，可在此裁剪"
                    : "壁纸文件不存在，请重新选择");

                SetWallpaperPreviewHintVisible(false);

                ApplyLaunchBoardWallpaperPreviewTransform();

                return;

            }


            if(!string.Equals(
                   wallpaperPreviewLoadedPath,
                   path,
                   StringComparison.OrdinalIgnoreCase))
            {

                try
                {

                    // 用 FileStream + OnLoad：
                    // 解码完立即释放句柄，不锁文件；
                    // 同时避免 Uri 把路径里的 # / % 当成转义字符

                    BitmapImage image =
                        new BitmapImage();

                    using(FileStream stream =
                          new FileStream(
                              path,
                              FileMode.Open,
                              FileAccess.Read,
                              FileShare.ReadWrite))
                    {

                        image.BeginInit();

                        image.CacheOption =
                            BitmapCacheOption.OnLoad;

                        // 注意：StreamSource + IgnoreImageCache 会在
                        // EndInit 内部尝试从缓存移除 null UriSource，
                        // 直接抛 ArgumentNullException（与 MainWindow 同坑）。
                        // 新文件流本来就是全新解码，不需要该选项。

                        image.StreamSource =
                            stream;

                        image.EndInit();

                    }

                    image.Freeze();


                    SetWallpaperPreviewImage(image);

                    wallpaperPreviewLoadedPath =
                        path;

                    SetWallpaperPreviewEmptyText(
                        false,
                        "");

                    SetWallpaperPreviewHintVisible(
                        true);

                }
                catch(Exception ex)
                {

                    wallpaperPreviewLoadedPath = "";

                    ClearWallpaperPreviewImage();

                    SetWallpaperPreviewEmptyText(
                        true,
                        "预览加载失败，请换一张图片");

                    SetWallpaperPreviewHintVisible(
                        false);

                    Debug.WriteLine(
                        "设置中心：生成壁纸预览失败: "
                        + ex.Message);

                }

            }
            else
            {

                SetWallpaperPreviewEmptyText(
                    false,
                    "");

                SetWallpaperPreviewHintVisible(
                    true);

            }


            ApplyLaunchBoardWallpaperPreviewTransform();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：刷新壁纸预览失败: "
                + ex.Message);

        }

    }


    private void SetWallpaperPreviewImage(BitmapSource image)
    {

        if(LaunchBoardWallpaperPreviewSurface == null)
            return;


        if(launchBoardWallpaperPreviewBrush == null ||
           launchBoardWallpaperPreviewBrush.IsFrozen)
        {

            launchBoardWallpaperPreviewBrush =
                new ImageBrush
                {

                    Stretch = Stretch.UniformToFill,

                    AlignmentX = AlignmentX.Center,

                    AlignmentY = AlignmentY.Center,

                    ViewboxUnits =
                        BrushMappingMode.RelativeToBoundingBox,

                };

        }


        launchBoardWallpaperPreviewBrush.ImageSource =
            image;

        LaunchBoardWallpaperPreviewSurface.Fill =
            launchBoardWallpaperPreviewBrush;

    }


    private void ClearWallpaperPreviewImage()
    {

        if(launchBoardWallpaperPreviewBrush != null)
            launchBoardWallpaperPreviewBrush.ImageSource = null;


        if(LaunchBoardWallpaperPreviewSurface != null)
            LaunchBoardWallpaperPreviewSurface.Fill = null;

    }


    // 预览框的参考尺寸（300 x 216.67，与展板 900:650 同比例），
    // 无效时回退到固定值，避免除零
    private void GetWallpaperCropReferenceSize(
        out double width,
        out double height)
    {

        width = launchBoardWallpaperCropReferenceWidth;

        height = launchBoardWallpaperCropReferenceHeight;


        if(!(width > 0) ||
           double.IsNaN(width) ||
           double.IsInfinity(width))
            width = WallpaperCropReferenceWidth;


        if(!(height > 0) ||
           double.IsNaN(height) ||
           double.IsInfinity(height))
            height = WallpaperCropReferenceHeight;

    }


    // 源图宽高比；没有图时用参考比例（此时任何平移边界都是 0）
    private double GetWallpaperCropSourceAspect(
        double fallbackAspect)
    {

        if(launchBoardWallpaperPreviewBrush != null &&
           launchBoardWallpaperPreviewBrush.ImageSource is BitmapSource bitmap &&
           bitmap.PixelWidth > 0 &&
           bitmap.PixelHeight > 0)
        {

            return
                bitmap.PixelWidth /
                (double)bitmap.PixelHeight;

        }


        return fallbackAspect;

    }


    private void SetWallpaperPreviewEmptyText(
        bool visible,
        string text)
    {

        try
        {

            if(LaunchBoardWallpaperPreviewEmptyText == null)
                return;


            LaunchBoardWallpaperPreviewEmptyText.Text =
                text ?? "";

            LaunchBoardWallpaperPreviewEmptyText.Visibility =
                visible
                ? Visibility.Visible
                : Visibility.Collapsed;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：更新预览提示失败: "
                + ex.Message);

        }

    }


    private void SetWallpaperPreviewHintVisible(
        bool visible)
    {

        try
        {

            if(LaunchBoardWallpaperPreviewHintBorder != null)
            {

                LaunchBoardWallpaperPreviewHintBorder.Visibility =
                    visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            }

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：更新预览提示失败: "
                + ex.Message);

        }

    }


    // 用 ImageBrush.Viewbox 框出源图里要显示的区域。
    // Viewbox 是源图的归一化矩形：UniformToFill 原本裁掉的
    // 左右 / 上下部分也能通过移动这个矩形重新取回来。
    // 主窗口用同一套算法，所以预览和展板的中心、缩放一致。
    private void ApplyLaunchBoardWallpaperPreviewTransform()
    {

        try
        {

            // 先按当前缩放把位移夹回边界，
            // 旧配置 / 手改 JSON 里的越界值也不会拖出空白

            ClampWallpaperCropOffsets();


            if(LaunchBoardWallpaperPreviewSurface == null)
                return;


            ImageBrush? brush =
                launchBoardWallpaperPreviewBrush;

            BitmapSource? source =
                brush != null
                ? brush.ImageSource as BitmapSource
                : null;


            if(brush == null ||
               source == null ||
               source.PixelWidth <= 0 ||
               source.PixelHeight <= 0)
            {

                LaunchBoardWallpaperPreviewSurface.Fill =
                    null;

                return;

            }


            if(LaunchBoardWallpaperPreviewSurface.Fill != brush)
                LaunchBoardWallpaperPreviewSurface.Fill = brush;


            double referenceWidth;
            double referenceHeight;

            GetWallpaperCropReferenceSize(
                out referenceWidth,
                out referenceHeight);


            double referenceAspect =
                referenceWidth /
                referenceHeight;

            double sourceAspect =
                source.PixelWidth /
                (double)source.PixelHeight;


            // 1.0 缩放时，源图铺满参考框后可见的归一化区域。
            // 例如 3:1 的宽图只会显示中间 1/2 宽，baseWidth = 0.5
            double baseWidth =
                Math.Min(1.0, referenceAspect / sourceAspect);

            double baseHeight =
                Math.Min(1.0, sourceAspect / referenceAspect);


            double scale =
                ClampSetting(
                    launchBoardWallpaperScaleValue,
                    WallpaperCropScaleMin,
                    WallpaperCropScaleMax,
                    1.0);


            double windowWidth =
                baseWidth / scale;

            double windowHeight =
                baseHeight / scale;


            // 正 offset 表示图片向右 / 向下移动，
            // 也就是取景框在源图里向左 / 向上移动
            double centerX =
                0.5 -
                (launchBoardWallpaperOffsetXValue / referenceWidth) *
                baseWidth / scale;

            double centerY =
                0.5 -
                (launchBoardWallpaperOffsetYValue / referenceHeight) *
                baseHeight / scale;


            centerX =
                ClampSetting(
                    centerX,
                    windowWidth / 2.0,
                    1.0 - windowWidth / 2.0,
                    0.5);

            centerY =
                ClampSetting(
                    centerY,
                    windowHeight / 2.0,
                    1.0 - windowHeight / 2.0,
                    0.5);


            brush.Stretch = Stretch.UniformToFill;

            brush.AlignmentX = AlignmentX.Center;

            brush.AlignmentY = AlignmentY.Center;

            brush.ViewboxUnits =
                BrushMappingMode.RelativeToBoundingBox;

            brush.Viewbox =
                new Rect(
                    centerX - windowWidth / 2.0,
                    centerY - windowHeight / 2.0,
                    windowWidth,
                    windowHeight);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：应用壁纸预览裁切失败: "
                + ex.Message);

        }

    }


    // 把当前裁剪参数交给主窗口，让展板实时跟着变
    private void SyncLaunchBoardWallpaperCropToDock()
    {

        try
        {

            if(dock == null)
                return;

            if(string.IsNullOrWhiteSpace(
                   launchBoardWallpaperPath))
                return;


            dock.SettingsCenter_ApplyLaunchBoardWallpaperCrop(
                launchBoardWallpaperScaleValue,
                launchBoardWallpaperOffsetXValue,
                launchBoardWallpaperOffsetYValue,
                launchBoardWallpaperCropReferenceWidth,
                launchBoardWallpaperCropReferenceHeight);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：同步壁纸裁剪失败: "
                + ex.Message);

        }

    }


    // 拖动 / 缩放后的统一收尾：预览 + 主界面 + 落盘
    private void CommitWallpaperCropChange()
    {

        ApplyLaunchBoardWallpaperPreviewTransform();

        SyncLaunchBoardWallpaperCropToDock();

        ScheduleSave();

    }


    // 换图 / 清图时回到 1.0 居中
    private void ResetWallpaperCropValues()
    {

        launchBoardWallpaperScaleValue =
            1.0;

        launchBoardWallpaperOffsetXValue =
            0.0;

        launchBoardWallpaperOffsetYValue =
            0.0;

    }


    // 平移边界：缩放 >= 1 时把图片限制在预览框内，
    // 保证四周不会拖出空白；缩放 < 1 时强制居中
    private void ClampWallpaperCropOffsets()
    {

        try
        {

            // 参考尺寸固定用 300 x 216.67；预览控件实际略小一点，
            // 两边用同一套参考值，主窗口换算时不会差 1px

            double width;
            double height;

            GetWallpaperCropReferenceSize(
                out width,
                out height);


            double scale =
                ClampSetting(
                    launchBoardWallpaperScaleValue,
                    WallpaperCropScaleMin,
                    WallpaperCropScaleMax,
                    1.0);

            launchBoardWallpaperScaleValue = scale;


            double referenceAspect =
                width /
                height;

            double sourceAspect =
                GetWallpaperCropSourceAspect(
                    referenceAspect);


            // 1.0 缩放时整张图铺满参考框后的实际显示尺寸：
            // 3:1 宽图铺进 1.5:1 框后，显示宽 = 参考高 * 3，
            // 多出来的部分正是可以左右拖动的范围
            double displayedWidth =
                Math.Max(
                    width,
                    height * sourceAspect);

            double displayedHeight =
                Math.Max(
                    height,
                    width / sourceAspect);


            double maxOffsetX =
                Math.Max(
                    0.0,
                    (displayedWidth * scale - width) /
                    2.0);

            double maxOffsetY =
                Math.Max(
                    0.0,
                    (displayedHeight * scale - height) /
                    2.0);


            launchBoardWallpaperOffsetXValue =
                ClampSetting(
                    launchBoardWallpaperOffsetXValue,
                    -maxOffsetX,
                    maxOffsetX,
                    0.0);

            launchBoardWallpaperOffsetYValue =
                ClampSetting(
                    launchBoardWallpaperOffsetYValue,
                    -maxOffsetY,
                    maxOffsetY,
                    0.0);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：限制壁纸平移边界失败: "
                + ex.Message);

        }

    }


    private void LaunchBoardWallpaperPreview_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {

        try
        {

            if(LaunchBoardWallpaperPreviewHost == null)
                return;

            if(LaunchBoardWallpaperPreviewSurface == null ||
               launchBoardWallpaperPreviewBrush == null ||
               launchBoardWallpaperPreviewBrush.ImageSource == null)
                return;


            isWallpaperPreviewDragging =
                true;


            wallpaperPreviewDragStartPoint =
                e.GetPosition(
                    LaunchBoardWallpaperPreviewHost);

            wallpaperPreviewDragStartOffsetX =
                launchBoardWallpaperOffsetXValue;

            wallpaperPreviewDragStartOffsetY =
                launchBoardWallpaperOffsetYValue;


            LaunchBoardWallpaperPreviewHost.CaptureMouse();

            LaunchBoardWallpaperPreviewHost.Cursor =
                Cursors.SizeAll;


            e.Handled = true;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：开始拖动壁纸预览失败: "
                + ex.Message);

        }

    }


    private void LaunchBoardWallpaperPreview_MouseMove(
        object sender,
        MouseEventArgs e)
    {

        try
        {

            if(!isWallpaperPreviewDragging)
                return;


            // 鼠标在预览框外松开时不会收到 MouseUp，
            // 这里补一次收尾，避免一直粘着鼠标

            if(e.LeftButton != MouseButtonState.Pressed)
            {

                EndWallpaperPreviewDrag();

                return;

            }


            if(LaunchBoardWallpaperPreviewHost == null)
                return;


            Point current =
                e.GetPosition(
                    LaunchBoardWallpaperPreviewHost);


            launchBoardWallpaperOffsetXValue =
                wallpaperPreviewDragStartOffsetX +
                (current.X -
                 wallpaperPreviewDragStartPoint.X);

            launchBoardWallpaperOffsetYValue =
                wallpaperPreviewDragStartOffsetY +
                (current.Y -
                 wallpaperPreviewDragStartPoint.Y);


            ClampWallpaperCropOffsets();

            CommitWallpaperCropChange();


            e.Handled = true;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：拖动壁纸预览失败: "
                + ex.Message);

        }

    }


    private void LaunchBoardWallpaperPreview_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {

        EndWallpaperPreviewDrag();

        e.Handled = true;

    }


    private void LaunchBoardWallpaperPreview_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {

        // 捕获已经丢失，只需复位状态

        if(isWallpaperPreviewDragging)
        {

            isWallpaperPreviewDragging =
                false;

            ScheduleSave();

        }

    }


    private void EndWallpaperPreviewDrag()
    {

        if(!isWallpaperPreviewDragging)
            return;


        isWallpaperPreviewDragging =
            false;


        try
        {

            if(LaunchBoardWallpaperPreviewHost != null)
            {

                LaunchBoardWallpaperPreviewHost.ReleaseMouseCapture();

                LaunchBoardWallpaperPreviewHost.Cursor =
                    Cursors.SizeAll;

            }

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：结束拖动壁纸预览失败: "
                + ex.Message);

        }


        ScheduleSave();

    }


    private void LaunchBoardWallpaperPreview_MouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {

        try
        {

            if(LaunchBoardWallpaperPreviewHost == null)
                return;

            if(LaunchBoardWallpaperPreviewSurface == null ||
               launchBoardWallpaperPreviewBrush == null ||
               launchBoardWallpaperPreviewBrush.ImageSource == null)
                return;


            double factor =
                e.Delta > 0
                ? WallpaperCropWheelStep
                : 1.0 / WallpaperCropWheelStep;


            double newScale =
                ClampSetting(
                    launchBoardWallpaperScaleValue * factor,
                    WallpaperCropScaleMin,
                    WallpaperCropScaleMax,
                    launchBoardWallpaperScaleValue);


            // 碰到 1.0 / 5.0 边界时实际倍数会被夹住，
            // 后面的位移补偿必须用真实倍数

            double appliedFactor =
                newScale /
                launchBoardWallpaperScaleValue;


            double width =
                LaunchBoardWallpaperPreviewHost.ActualWidth;

            double height =
                LaunchBoardWallpaperPreviewHost.ActualHeight;


            if(!(width > 0))
                width =
                    launchBoardWallpaperCropReferenceWidth;

            if(!(height > 0))
                height =
                    launchBoardWallpaperCropReferenceHeight;


            // 以鼠标位置为中心缩放：
            // p 是鼠标相对预览框中心的向量，
            // 新位移 = p * (1 - f) + f * 旧位移

            Point pointer =
                e.GetPosition(
                    LaunchBoardWallpaperPreviewHost);

            double pointerX =
                pointer.X - width / 2.0;

            double pointerY =
                pointer.Y - height / 2.0;


            launchBoardWallpaperScaleValue =
                newScale;

            launchBoardWallpaperOffsetXValue =
                pointerX * (1.0 - appliedFactor) +
                appliedFactor *
                launchBoardWallpaperOffsetXValue;

            launchBoardWallpaperOffsetYValue =
                pointerY * (1.0 - appliedFactor) +
                appliedFactor *
                launchBoardWallpaperOffsetYValue;


            ClampWallpaperCropOffsets();

            CommitWallpaperCropChange();


            e.Handled = true;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：缩放壁纸预览失败: "
                + ex.Message);

        }

    }


    // ==========================
    // 四个开关 + 药丸：界面与真实状态对齐
    // ==========================

    private void SyncToggles(
        bool locked,
        bool tinted,
        bool closeBoardOnOutsideClick)
    {

        isSyncingToggles =
            true;

        try
        {

            LockToggle.IsChecked =
                locked;

            // 点击空白处关闭展板：存档值回填到开关
            // （isSyncingToggles = true，不会反过来触发保存）

            CloseBoardOutsideToggle.IsChecked =
                closeBoardOnOutsideClick;

            // 壁纸取色没有开关，只有按钮文字 + 按钮高亮

            UpdateTintButtonVisual(
                tinted);

            // 开机自启动：注册表才是真实状态（原来那套逻辑原样复用）

            StartupToggle.IsChecked =
                dock != null &&
                dock.SettingsCenter_IsStartupEnabled;

            // 桌面图标处理模式：IconMode.txt 才是真实状态

            bool store =
                dock != null &&
                dock.SettingsCenter_IsIconModeStore;

            IconModeKeepRadio.IsChecked =
                !store;

            IconModeStoreRadio.IsChecked =
                store;

        }
        finally
        {

            isSyncingToggles =
                false;

        }


        // 让 Dock 跟上存档里的两个开关
        // （锁定：绝对值直接设；取色：只有不一致时才动，它是个开关）

        if(dock != null)
        {

            if(dock.SettingsCenter_IsLocked != locked)
                dock.SettingsCenter_SetLocked(locked);

            if(tinted &&
               !dock.SettingsCenter_IsWallpaperTinted)
            {

                // 只有“存档开着、Dock 还没取色”才重新截屏，
                // 已经取过色就保留当前颜色（与原逻辑一致）

                Color screenColor =
                    CaptureScreenTintColor();

                ApplyTintColorToDock(
                    screenColor);

            }
            else if(!tinted &&
                    dock.SettingsCenter_IsWallpaperTinted)
            {

                DisableDockTint();

            }


            // 以 Dock 的真实状态回写按钮视觉

            UpdateTintButtonVisual(
                dock.SettingsCenter_IsWallpaperTinted);

        }

    }

    // ==========================
    // 滑块：透明度 / 模糊度
    // 实时生效 + 攒 350ms 落盘（与 Dock 面板同一套节奏）
    // ==========================

    private void OpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {

        opacityValue =
            e.NewValue;

        if(OpacityText != null)
            OpacityText.Text =
                e.NewValue.ToString("0.00");

        ApplyToDock();

        ScheduleSave();

    }


    private void BlurSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {

        blurValue =
            e.NewValue;

        if(BlurText != null)
            BlurText.Text =
                ((int)Math.Round(e.NewValue)).ToString();

        ApplyToDock();

        ScheduleSave();

    }


    // ==========================
    // 展板材质滑块：透明度 / 模糊度
    // 实时生效 + 350ms 落盘，与 Dock 互不影响
    // ==========================

    private void LaunchBoardOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {

        launchBoardOpacityValue =
            e.NewValue;

        if(LaunchBoardOpacityText != null)
            LaunchBoardOpacityText.Text =
                e.NewValue.ToString("0.00");

        ApplyLaunchBoardToDock();

        ScheduleSave();

    }


    private void LaunchBoardBlurSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {

        launchBoardBlurValue =
            e.NewValue;

        if(LaunchBoardBlurText != null)
            LaunchBoardBlurText.Text =
                ((int)Math.Round(e.NewValue)).ToString();

        ApplyLaunchBoardToDock();

        ScheduleSave();

    }


    // ==========================
    // 锁定位置（开关）
    // ==========================

    private void LockToggle_Changed(
        object sender,
        RoutedEventArgs e)
    {

        if(isSyncingToggles)
            return;

        if(LockToggle == null)
            return;

        if(dock != null)
            dock.SettingsCenter_SetLocked(
                LockToggle.IsChecked == true);

        ScheduleSave();

    }


    // ==========================
    // 点击空白处关闭展板（开关）
    // ==========================

    private void CloseBoardOutsideToggle_Changed(
        object sender,
        RoutedEventArgs e)
    {

        if(isSyncingToggles)
            return;

        if(CloseBoardOutsideToggle == null)
            return;

        // 读盘完成之前，控件回填也会触发 Checked / Unchecked：
        // 这一次不是用户操作，不落盘

        if(isLoading ||
           !isLoadedFromStore)
            return;

        // 立刻落盘，不走 350ms 延时：
        // 主窗口每次点击都会重新读这一项，
        // 刚关掉开关就点空白处也必须马上按新值执行

        if(saveTimer != null)
            saveTimer.Stop();

        SaveToStore();

        SaveWallpaperSettings();

    }


    // ==========================
    // 开机自启动（写注册表，写完按真实状态回写开关）
    // ==========================

    private void StartupToggle_Changed(
        object sender,
        RoutedEventArgs e)
    {

        if(isSyncingToggles)
            return;

        if(StartupToggle == null)
            return;

        if(dock == null)
            return;

        dock.SettingsCenter_SetStartupEnabled(
            StartupToggle.IsChecked == true);

        // 注册表写失败时开关要弹回真实状态

        isSyncingToggles =
            true;

        try
        {

            StartupToggle.IsChecked =
                dock.SettingsCenter_IsStartupEnabled;

        }
        finally
        {

            isSyncingToggles =
                false;

        }

    }


    // ==========================
    // 桌面图标处理模式：两点药丸
    // ==========================

    private void IconModeKeep_Checked(
        object sender,
        RoutedEventArgs e)
    {

        if(isSyncingToggles)
            return;

        if(dock != null)
            dock.SettingsCenter_SetIconModeKeepOnDesktop();

    }


    private void IconModeStore_Checked(
        object sender,
        RoutedEventArgs e)
    {

        if(isSyncingToggles)
            return;

        if(dock != null)
            dock.SettingsCenter_SetIconModeMoveToStore();

    }


    // ==========================
    // 壁纸取色：屏幕截取方案
    // ==========================
    // 不再读注册表壁纸文件，直接截屏统计主色。
    // 取色 / 应用 / 关闭全程静默，任何失败只写 Debug 日志，绝不弹 MessageBox。

    private void TintButton_Click(
        object sender,
        RoutedEventArgs e)
    {

        e.Handled = true;

        Debug.WriteLine(
            "设置中心：壁纸取色按钮已触发 Click");


        if(dock == null)
        {

            Debug.WriteLine(
                "设置中心：dock == null，无法执行壁纸取色（已静默忽略）");

            UpdateTintButtonVisual(false);

            return;

        }


        bool wasTinted =
            dock.SettingsCenter_IsWallpaperTinted;


        try
        {

            if(wasTinted)
            {

                // 再点一下：关闭取色

                DisableDockTint();

                UpdateTintButtonVisual(false);

                Debug.WriteLine(
                    "设置中心：已关闭壁纸取色");

            }
            else
            {

                // 屏幕截取取主色（失败时 WallpaperAnalyzer 内部兜底 30,30,30）

                Color screenColor =
                    CaptureScreenTintColor();


                bool applied =
                    ApplyTintColorToDock(
                        screenColor);


                UpdateTintButtonVisual(
                    applied || dock.SettingsCenter_IsWallpaperTinted);


                Debug.WriteLine(
                    "设置中心：屏幕取色 RGB("
                    + screenColor.R + ","
                    + screenColor.G + ","
                    + screenColor.B + ")，应用"
                    + (applied ? "成功" : "失败（已静默处理）"));

            }

        }
        catch(Exception ex)
        {

            // 绝对不弹窗：只记录日志，并回写 Dock 的真实状态

            Debug.WriteLine(
                "设置中心：壁纸取色静默失败: "
                + ex);

            UpdateTintButtonVisual(
                dock.SettingsCenter_IsWallpaperTinted);

        }


        ScheduleSave();

    }


    // 截屏取色入口：任何异常都退回 RGB(30,30,30)

    private static Color CaptureScreenTintColor()
    {

        try
        {

            WallpaperAnalyzer analyzer =
                new WallpaperAnalyzer();


            return analyzer.GetAverageScreenColor();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：屏幕取色异常，使用兜底色 RGB(30,30,30): "
                + ex.Message);


            return Color.FromRgb(
                30,
                30,
                30);

        }

    }


    // 把屏幕主色应用到 Dock 的色调层

    private bool ApplyTintColorToDock(
        Color tintColor)
    {

        if(dock == null)
            return false;


        try
        {

            // 先沿用 MainWindow 的公开开关进入“取色状态”。
            // 旧逻辑会读壁纸文件，文件没了会退回纯色桌面；
            // 无论它取到什么颜色，下面都会立刻覆盖成屏幕主色。

            if(!dock.SettingsCenter_IsWallpaperTinted)
                dock.SettingsCenter_ToggleWallpaperTint();


            // 旧路径连纯色桌面都读不到时，状态仍是 false。
            // 用同程序集字段兜底打开，保证屏幕取色一定能显示。

            if(!dock.SettingsCenter_IsWallpaperTinted)
                SetDockTintStateFallback(true);


            bool tintedNow =
                dock.SettingsCenter_IsWallpaperTinted;


            // 旧路径失败时 MainWindow 会把文字写成“取色失败”，
            // 这里取色已经成功，把 Dock 面板文字同步回来

            if(tintedNow &&
               dock.WallpaperTintText != null)
            {

                dock.WallpaperTintText.Text =
                    "已跟随壁纸";

            }


            // 覆盖为屏幕主色

            if(dock.DockGlassTintBrush != null)
                dock.DockGlassTintBrush.Color = tintColor;


            // 按当前透明度 / 模糊度重刷材质，让色调层真正亮起来

            dock.SettingsCenter_ApplyMaterial(
                opacityValue,
                blurValue);


            // 连状态兜底都失败：直接给色调层一个可见透明度

            if(!dock.SettingsCenter_IsWallpaperTinted &&
               dock.DockGlassTint != null)
            {

                dock.DockGlassTint.Opacity =
                    0.30 * ClampSetting(
                        opacityValue,
                        0.0,
                        1.0,
                        1.0);

            }


            return tintedNow ||
                   (dock.DockGlassTint != null &&
                    dock.DockGlassTint.Opacity > 0);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：应用屏幕取色失败: "
                + ex);


            return false;

        }

    }


    // 关闭 Dock 的壁纸取色

    private void DisableDockTint()
    {

        if(dock == null)
            return;


        try
        {

            if(dock.SettingsCenter_IsWallpaperTinted)
                dock.SettingsCenter_ToggleWallpaperTint();


            if(dock.SettingsCenter_IsWallpaperTinted)
            {

                SetDockTintStateFallback(false);


                dock.SettingsCenter_ApplyMaterial(
                    opacityValue,
                    blurValue);

            }


            if(dock.DockGlassTint != null)
                dock.DockGlassTint.Opacity = 0;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：关闭壁纸取色失败: "
                + ex);

        }

    }


    // MainWindow 没有公开“设置取色状态”的接口。
    // 这里只在公开开关的旧路径失败时兜底，不修改 MainWindow 的代码。

    private void SetDockTintStateFallback(
        bool enabled)
    {

        if(dock == null)
            return;


        try
        {

            FieldInfo? field =
                typeof(MainWindow).GetField(
                    "isDockTinted",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);


            if(field == null ||
               field.FieldType != typeof(bool))
            {

                Debug.WriteLine(
                    "设置中心：未找到 MainWindow.isDockTinted，无法兜底切换取色状态");

                return;

            }


            field.SetValue(
                dock,
                enabled);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：切换取色状态失败: "
                + ex.Message);

        }

    }


    // 按钮视觉状态：文字 + 背景 / 描边高亮
    // 让点击一定有肉眼可见的反馈

    private void UpdateTintButtonVisual(
        bool tinted)
    {

        if(TintText != null)
        {

            TintText.Text =
                tinted
                ? "已跟随壁纸"
                : "壁纸取色";

        }


        if(TintButton != null)
        {

            TintButton.Background =
                new SolidColorBrush(
                    tinted
                    ? Color.FromArgb(0xFF, 0x3A, 0x2E, 0x63)
                    : Color.FromArgb(0xFF, 0x2A, 0x2A, 0x31));

            TintButton.BorderBrush =
                new SolidColorBrush(
                    tinted
                    ? Color.FromArgb(0xFF, 0x8B, 0x5C, 0xF6)
                    : Color.FromArgb(0xFF, 0x3F, 0x3F, 0x49));

        }

    }




    private void TintButton_MouseEnter(
        object sender,
        MouseEventArgs e)
    {

        AnimateScale(
            TintButtonScale,
            1.06);

    }


    private void TintButton_MouseLeave(
        object sender,
        MouseEventArgs e)
    {

        AnimateScale(
            TintButtonScale,
            1.0);

    }

    // ==========================
    // 标题栏：按住可以拖动窗口
    // ==========================

    private void Header_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {

        if(e.ButtonState != MouseButtonState.Pressed)
            return;

        try
        {

            DragMove();

        }
        catch
        {

            // 拖动过程中松手 / 系统插进来别的鼠标操作
            // DragMove 可能抛异常，忽略即可

        }

    }


    // ==========================
    // 右上角关闭 = 只隐藏窗口（程序继续在托盘里活）
    // ==========================

    private void CloseButton_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {

        // 吃掉按下事件，免得触发标题栏拖动

        e.Handled = true;

    }


    private void CloseButton_Click(
        object sender,
        MouseButtonEventArgs e)
    {

        e.Handled = true;

        Close();

    }


    private void CloseButton_MouseEnter(
        object sender,
        MouseEventArgs e)
    {

        AnimateScale(
            CloseButtonScale,
            1.12);

    }


    private void CloseButton_MouseLeave(
        object sender,
        MouseEventArgs e)
    {

        AnimateScale(
            CloseButtonScale,
            1.0);

    }


    // 程序退出时由 MainWindow 调用：置标记 + 真关闭
    // 只调 Close() 不置标记的话，OnClosing 会把这次关闭 Cancel 成“只隐藏”，
    // 可见窗口卡在“关不掉”的状态会拖住 Application.Shutdown

    public void CloseForShutdown()
    {

        isAppShuttingDown =
            true;


        try
        {

            Close();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：退出时关闭失败: "
                +
                ex.Message);

        }

    }


    protected override void OnClosing(
        System.ComponentModel.CancelEventArgs e)
    {

        // 先把还没落盘的值补上：设置绝不丢

        try
        {

            if(saveTimer != null)
                saveTimer.Stop();

            if(isLoadedFromStore)
            {
                SaveToStore();
                SaveWallpaperSettings();
            }

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：关闭前保存失败: "
                +
                ex.Message);

        }

        // 关掉设置中心 = 只隐藏，绝不退出程序
        // 托盘菜单的「显示 Dock」「退出软件」照旧

        // 程序正在退出：不拦，正常收尾（上面的保存已经做完）

        if(isAppShuttingDown)
        {

            base.OnClosing(e);

            return;

        }


        e.Cancel =
            true;

        Hide();

        base.OnClosing(e);

    }

    // 缩放动画：只动 ScaleTransform，不碰布局

    private static void AnimateScale(
        ScaleTransform? transform,
        double scale)
    {

        if(transform == null)
            return;

        CubicEase ease =
            new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };

        transform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = ease
            });

        transform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = ease
            });

    }


    // ==========================
    // 落盘：ValueChanged / Checked 之后攒 350ms 写一次
    // 写法与 MainWindow 里那两个滑块完全一致
    // ==========================

    private void ScheduleSave()
    {

        // 还没读完盘 / 正在读盘：
        // 这次变化是代码自己灌进去的，不写盘

        if(isLoading ||
           !isLoadedFromStore)
            return;

        try
        {

            if(saveTimer == null)
            {

                saveTimer =
                    new DispatcherTimer();

                saveTimer.Interval =
                    TimeSpan.FromMilliseconds(
                        SaveDelay);

                saveTimer.Tick +=
                    SaveTimer_Tick;

            }

            saveTimer.Stop();

            saveTimer.Start();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：排队保存失败: "
                +
                ex.Message);

        }

    }


    private void SaveTimer_Tick(
        object? sender,
        EventArgs e)
    {

        if(saveTimer != null)
            saveTimer.Stop();

        SaveToStore();

        SaveWallpaperSettings();

    }


    private void SaveToStore()
    {

        try
        {

            string? dir =
                Path.GetDirectoryName(
                    StorePath);

            if(!string.IsNullOrWhiteSpace(dir) &&
               !Directory.Exists(dir))
            {

                Directory.CreateDirectory(
                    dir);

            }


            // 读-改-写：只更新自己的这几个键
            // （Dock 两个滑块 + 材质预设 + 展板两个 + 开关），文件里别的键一个都不碰

            Dictionary<string, double> data =
                ReadStoreRaw();

            data["opacity"] =
                opacityValue;

            data["blur"] =
                blurValue;

            // 材质预设：0 / 1 / 2，与两个滑块互不覆盖

            data["materialPreset"] =
                materialPresetValue;

            // 展板材质：与 Dock 的两个键完全独立

            data["LaunchBoardOpacity"] =
                launchBoardOpacityValue;

            data["LaunchBoardBlur"] =
                launchBoardBlurValue;

            data["locked"] =
                LockToggle.IsChecked == true
                ? 1.0
                : 0.0;

            data["wallpaperTint"] =
                dock != null &&
                dock.SettingsCenter_IsWallpaperTinted
                ? 1.0
                : 0.0;

            // 点击空白处关闭展板：沿用 0 / 1 写法
            // 控件不存在时按默认开启处理

            data["closeBoardOnOutsideClick"] =
                CloseBoardOutsideToggle == null ||
                CloseBoardOutsideToggle.IsChecked == true
                ? 1.0
                : 0.0;


            string json =
                System.Text.Json.JsonSerializer
                    .Serialize<Dictionary<string, double>>(
                        data);


            File.WriteAllText(
                StorePath,
                json);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：保存 Settings.json 失败: "
                +
                ex.Message);

        }

    }


    private Dictionary<string, double> ReadStoreRaw()
    {

        if(!File.Exists(StorePath))
            return new Dictionary<string, double>();

        try
        {

            string json =
                File.ReadAllText(
                    StorePath);

            if(string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, double>();

            Dictionary<string, double>? data =
                System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, double>>(
                        json);

            if(data == null)
                return new Dictionary<string, double>();

            return data;

        }
        catch
        {

            return new Dictionary<string, double>();

        }

    }


    // 手改过的存档可能塞进 NaN / 越界值：一律夹回合法区间

    private static double ClampSetting(
        double value,
        double min,
        double max,
        double fallback)
    {

        if(double.IsNaN(value) ||
           double.IsInfinity(value))
            return fallback;

        if(max < min)
            return fallback;

        if(value < min)
            return min;

        if(value > max)
            return max;

        return value;

    }

}
}
