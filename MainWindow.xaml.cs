using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;

// ==========================
// 系统托盘（WinForms）
// 只用到 NotifyIcon / ContextMenuStrip
// 这里用别名引入，不用 using System.Windows.Forms;
// 因为那样会让 Application / MessageBox / Point / Size
// 变成二义性（CS0104），整个文件都编译不过
// ==========================

using WinForms = System.Windows.Forms;


// ==========================
// GDI 图标（System.Drawing.Icon）
// ==========================
// 图标兜底提取（IconExtractor）拿到的就是它
// 这里只给这一个类型起别名，不用 using System.Drawing;
// 否则 Image / Point / Size / Color / Font
// 会和 WPF 的同名类型撞成二义性（CS0104），整个文件都编译不过
// ==========================

using GdiIcon = System.Drawing.Icon;


namespace GlassDock
{


public partial class MainWindow : Window
{


    private DockWatcher watcher;


    // ==========================
    // 系统托盘图标
    // 关掉窗口后程序仍然在后台活着
    // 靠它把 Dock 叫回来，或者真正退出
    // ==========================

    private WinForms.NotifyIcon? trayIcon = null;


    private WinForms.ContextMenuStrip? trayMenu = null;


    // System.Drawing.Icon(Stream) 需要流保持打开
    // 所以留个引用，退出时一起释放

    private Stream? trayIconStream = null;


    // 真正的退出标记
    // false：点关闭只是隐藏窗口
    // true ：走托盘“退出软件”，才真退出

    private bool isExiting = false;


    private bool isHidden = false;


    private bool isPinned = true;


    private bool isDragging = false;


    // ==========================
    // Dock 位置锁定
    // 默认未锁定：按住 Dock 可以拖动窗口
    // 锁定后：按住 Dock 不再移动窗口
    //         点击图标启动应用 / 展板 / 设置面板都不受影响
    // ==========================

    private bool isLocked = false;




    // ==========================
    // 全局拖动状态
    // ==========================


    // Dock上已按下左键
    // 等待点击 / 拖动判定

    private bool dockDragArmed = false;


    // 本次手势已判定为拖动

    private bool dockDragStarted = false;


    // 刚发生过拖动
    // 抑制随之而来的图标点击

    private bool dockDragSuppressClick = false;


    // 全局异常处理器是否正在执行
    // 处理器里的弹窗会泵消息
    // 布局里抛异常 → 弹窗 → 重新布局 → 又抛同一个异常 → 又弹窗
    // 这样无限递归下去就是栈溢出（0xc00000fd）
    // 所以只允许弹一次

    private static bool handlingUnhandledException = false;


    // 按下时鼠标相对窗口的位置

    private Point dockDragStartPoint =
        new Point(0,0);





    // ==========================
    // Liquid Glass 参数
    // ==========================


    private Point glassTarget =
        new Point(0.5,0.5);



    private Point glassCurrent =
        new Point(0.5,0.5);







    // ==========================
    // 水凝胶物理系统
    // ==========================


    private double gelX = 0.5;


    private double gelY = 0.5;





    private double gelVelocityX = 0;


    private double gelVelocityY = 0;





    // 弹簧强度

    private double gelSpring = 0.045;





    // 阻尼系数

    private double gelDamping = 0.86;








    // 液态流动时间


    private double liquidTime = 0;





    // 呼吸时间


    private double breathTime = 0;





    // 高光移动时间


    private double sweepTime = 0;




    // ==========================
    // Dock 自适应宽度参数
    // ==========================


    // 单个图标占位宽度
    // 图标 62 + 左右 Margin 9

    private const double IconSlotWidth = 80;


    // Dock 左右内边距

    private const double DockPadding = 40;


    // 九点点阵按钮占位宽度
    // 按钮 62 + 左右各 9 的间距 = 正好一个图标位
    // 算进宽度后：首个图标离按钮 9px
    // 与图标之间的间距保持一致

    private const double LaunchButtonSlot = 80;


    // 最小宽度
    // 保持原有视觉尺寸不变

    private const double MinDockWidth = 560;


    // 距屏幕边缘的安全余量

    private const double DockMaxMargin = 24;


    // 宽度动画时长(毫秒)

    private const double DockWidthDuration = 420;


    // 当前目标宽度

    private double dockWidthTarget = MinDockWidth;


    // ==========================
    // Dock 收起 / 展开参数
    // ==========================


    // 收起后的圆形直径
    // 每次收起都刷新成“展开状态下胶囊的真实高度”
    // 也就是：胶囊左右两边同时向内收
    // 宽度缩到与高度相等时，长胶囊自然变成正圆
    // 这里只是兜底初值

    private double CollapseSize = DockExpandedHeight;


    // 展开时 DockBody(胶囊)高度兜底值
    // 首次收起前会记录真实值

    private const double DockExpandedHeight = 100;


    // 展开状态的窗口高度与胶囊高度
    // 每次收起前记录，展开时按此还原

    private double dockWindowHeight = 0;

    private double dockBodyHeight = 0;


    // 收起 / 展开动画时长(毫秒)
    // 只补间宽度，高度全程不动
    // 圆角固定为高度的一半，不参与动画

    private const double CollapseDuration = 380;


    // 收起时：图标 / 九点阵 / 箭头 淡出时长(毫秒)
    // 放在前段快速淡出，避免被收窄的胶囊挤变形

    private const double CollapseContentFade = 150;


    // 收起时：Logo 淡入时长(毫秒)
    // 从动画尾段开始，宽度刚好收成正圆时完全露出

    private const double CollapseLogoFade = 170;




    // 收起后圆形里的 Logo 边长
    // 与圆形直径的比例
    // 0.64 ≈ 直径的 64%，居中不顶边

    private const double CollapseLogoRatio = 0.64;


    // 收起圆里 Logo 的自转周期(毫秒)
    // 数值越大转得越慢，只影响观感，不影响任何尺寸逻辑
    // 10000 = 10 秒整整转一圈 ≈ 36°/秒
    // 慢速匀速：看得出在转，又不抢眼、不晃神

    private const double LogoSpinDuration = 10000;


    // 是否处于收起状态

    private bool isCollapsed = false;


    // 收起 / 展开动画进行中

    private bool isCollapseAnimating = false;


    // 收放动画期间锁定的窗口基准
    // 动画开始时记下中心与底边
    // 之后每帧都用它算目标矩形
    // 逐帧反推 + 取整会让窗口慢慢偏移

    private double collapseAnchorCenterX = double.NaN;

    private double collapseAnchorBottom = double.NaN;


    // Logo 双击判定时间(毫秒)
    // 该窗口收不到系统双击消息
    // 因此这里自行判定

    private const double LogoDoubleClickMilliseconds = 420;


    // Logo 双击允许的位置偏差(像素)

    private const double LogoDoubleClickSlop = 14;


    // 上一次点击 Logo 的时间与位置

    private DateTime lastLogoClickTime = DateTime.MinValue;

    private Point lastLogoClickPoint = new Point(-9999, -9999);


    // ==========================
    // 启动板（九点点阵）
    // 灵动岛式展开：Storyboard 弹簧动画
    // ==========================


    // 展板目标尺寸
    // 屏幕放不下时按工作区比例缩小

    private const double LaunchBoardDesiredWidth = 900;


    private const double LaunchBoardDesiredHeight = 650;


    // 展板底边距窗口底部的距离
    // Dock 高 100 + 间隙 10

    private const double LaunchBoardBottomMargin = 110;


    // 展板上方预留的空白

    private const double LaunchBoardTopSpace = 30;


    // 回弹过冲需要的余量
    // 窗口比展板大这一圈，弹簧才不会被窗口边缘裁掉

    private const double LaunchBoardSpringRoom = 34;


    // 旧版“灵动岛”长条尺寸
    // 尺寸动画已停用（改成纯缩放挤压弹出）
    // 常量保留，方便需要时回退

    private const double LaunchBoardPillWidth = 380;


    private const double LaunchBoardPillHeight = 88;


    // 打开（挤压弹出）总时长（毫秒）
    // 需求：0.4 秒左右，弹出后 Q 弹收敛

    private const double LaunchBoardOpenDuration = 400;


    // 收起（反向挤压收回）时长（毫秒）

    private const double LaunchBoardCloseDuration = 260;


    // 收起时透明度淡出时长（毫秒）

    private const double LaunchBoardFadeDuration = 240;


    // 旧版尺寸动画的弹簧力度（BackEase）
    // 尺寸动画已停用，常量保留备用

    private const double LaunchBoardSpringAmplitude = 0.35;


    // ==========================
    // 挤压弹出：渲染层 ScaleTransform
    // 打开：容器从底边中心被“挤”出来，ScaleY 0 → 1.05 → 1
    // 收起：反向挤回去，ScaleY 1 → 0
    // 只动容器缩放，内部图标的大小与布局完全不动
    // ==========================


    // 第一阶段（起点 → 峰值）占总时长的比例
    // 剩下的时间是回落 + Q 弹抖动收敛
    // 总时长用 LaunchBoardOpenDuration（0.4 秒）

    private const double LaunchBoardPopRiseRatio = 0.55;


    // ScaleY：从 0（完全压扁成一条线）弹到 1.05，再回落到 1

    private const double LaunchBoardPopScaleYStart = 0;


    private const double LaunchBoardPopScaleYPeak = 1.05;


    // ScaleX：同步被横向挤扁，再略微撑开一下
    // 只改这两个数字就能调节“挤扁感”的强弱

    private const double LaunchBoardPopScaleXStart = 0.55;


    private const double LaunchBoardPopScaleXPeak = 1.04;


    // 第一阶段 BackEase 的过冲幅度
    // 保持小值，否则展板顶部会顶出窗口被裁掉

    private const double LaunchBoardPopBackAmplitude = 0.2;


    // 回落阶段的 ElasticEase：围绕 1 来回摆几下，幅度越来越小
    // Oscillations 是整数（摆动次数），Springiness 是弹性

    private const int LaunchBoardPopOscillations = 2;


    private const double LaunchBoardPopSpringiness = 5;


    // 弹出时透明度渐显时长（毫秒）
    // 需求：0 → 1 用 0.3 秒，比缩放（0.4 秒）先到位

    private const double LaunchBoardPopFadeDuration = 300;


    // ==========================
    // 【当前生效】WPF 原生关键帧：展板容器的 TransformGroup
    //   竖向：ScaleY 0.01 → 1（先把"窄而高的柱"挤出来）
    //   横向：ScaleX 0.1  → 1（再向左右鼓开成完整面板）
    //   透明：容器淡入淡出 + 内容层（图标）延后淡入
    // 【没有平移】容器位置全程居中不动（TransformGroup 里只剩缩放）
    // 锚点：RenderTransformOrigin = (0.21, 1)：九点阵那一列 / 展板下边缘
    //       缩放中心 = 这个锚点（ScaleTransform.CenterX / CenterY 恒为 0）
    //       "从九点阵挤出来"完全由这个锚点实现，不再靠 TranslateTransform 位移
    // 只动最外层容器 + 内容层透明度，展板内部的图标控件一个都不缩放
    // ==========================


    // 【已停用·保留备用】旧版"挤压"动画的 ScaleY 两个端点
    // 0 = 被压成一条线（贴在九点阵那一点上），1 = 展板原始高度
    // 现在生效的起止值见 LaunchBoardExtrudeStartScaleX / Y（细线）与 1（完整面板）

    private const double LaunchBoardSqueezeClosed = 0;

    private const double LaunchBoardSqueezeOpen = 1;


    // 【锚点】展板容器的 RenderTransformOrigin：缩放原点 = 九点阵那一列
    // 单位是"占展板自身尺寸的比例"（X 按宽度、Y 按高度）：
    //   0 = 展板左边缘 / 上边缘，1 = 右边缘 / 下边缘
    // 这两个常数是"现场测量失败时的兜底值"：
    //   X = 九点阵中心在展板宽度上的比例
    //       九点阵贴 Dock 左端（按钮中心距 Dock 左边缘 49）
    //       展板 800 宽、Dock 560 宽时：九点阵中心 = 窗口中心 - 231
    //       展板左边缘 = 窗口中心 - 400 → (400 - 231) / 800 ≈ 0.21
    //   Y = 1 = 展板下边缘（Dock 上沿那条线），展板从这里往上长
    // 【为什么不是负数】九点阵在 Dock 左端，但它仍然在展板内部（展板比 Dock 宽得多）；
    //   写成负数 = 把缩放原点推到展板左边缘之外，而展板左边缘离窗口左边缘只有
    //   LaunchBoardSpringRoom（34px），原点会直接落到窗口外面被窗口裁掉，
    //   画面上就又变回"从左下角长出来"了
    // 正常运行时由 UpdateLaunchBoardTransitionAnchor() 现场量到的真实比例覆盖，
    // 这两个值只在量不到时生效（Dock 越宽，真实值越小 → 跟得上）

    private const double LaunchBoardOriginFixedX = 0.21;


    private const double LaunchBoardOriginFixedY = 1;


    // 【已停用·保留备用】旧版挤压动画的回弹幅度
    // 现在生效的转场是 WPF 原生关键帧（见下面的 LaunchBoardExtrude* 常数）

    private const double LaunchBoardSqueezeBackAmplitude = 1.2;


    // ==========================
    // 【当前生效】WPF 原生关键帧：展板从九点阵那一点"原地挤出"
    // 一条 Storyboard 驱动 4 条 DoubleAnimationUsingKeyFrames：
    //   ① BoardScale.ScaleX        （横向缩放）
    //   ② BoardScale.ScaleY        （竖向缩放）
    //   ③ LaunchBoard.Opacity      （整块容器透明度）
    //   ④ BoardContentLayer.Opacity（图标 / 文字淡入淡出）
    // 锚点：展板容器 RenderTransformOrigin = (0.21, 1) → 九点阵那一列 / 展板下边缘
    //   缩放中心只认这个锚点，ScaleTransform.CenterX / CenterY 恒为 0
    // 【所有平移动画已彻底删除】容器位置全程保持居中不动，
    //   动画只剩"在原地缩放"，视觉上就是从九点阵那一点挤出来
    // 缓动：全程 CubicEase(EasingMode = EaseOut)
    // 打开（总时长 500ms）：
    //   0.0s 起点：ScaleX 0.1 / ScaleY 0.01，Opacity 0
    //        → 九点阵上的一个 80 x 6 的小点（位置就在九点阵那一列）
    //   0.25s 拉长：ScaleY 0.01 → 1（ScaleX 保持 0.1），Opacity → 1
    //        → 一根从九点阵长出来的窄长玻璃柱（CubicEase EaseOut）
    //   0.5s 鼓开：ScaleX 0.1 → 1（ScaleY 保持 1）
    //        → 液体向左右鼓开成完整面板（BackEase EaseOut 弹簧回弹）
    // 收起（总时长 500ms）：完全反向
    //   0.25s 收窄：ScaleX 1 → 0.1（BackEase EaseOut）
    //   0.5s 缩回：ScaleY 1 → 0.01，Opacity → 0（缩回九点阵那个小点后消失）
    // TransformGroup 里现在只有一个变换：
    //   Children[0] = BoardScale（缩放）
    // 只动最外层容器 + 内容层透明度，展板内部的图标控件一个都不缩放
    // ==========================


    // 转场总时长（毫秒）：450~550ms 之间手感最好，取 500

    private const double LaunchBoardTransitionDuration = 500;


    // 起点那颗液态玻璃液滴的尺寸（px）：10 x 10
    // 圆心正好压在九点阵中心上（缩放比例 = 液滴尺寸 / 展板尺寸，每次打开现场算）

    private const double LaunchBoardDropletSize = 10;


    // 算不出展板尺寸时的兜底缩放比例（对应 800 x 600 的展板）

    private const double LaunchBoardDropletScaleFallbackX = 0.0125;

    private const double LaunchBoardDropletScaleFallbackY = 0.0167;


    // 竖向"拉长"阶段结束的时刻占整段时长的比例
    // 0.5 × 500ms = 0.25s（需求：拉长阶段在第 0.25 秒结束）：
    //   打开：0 → 0.25s 竖向拉长（横向一直保持 0.1 宽 → 一根窄柱）
    //         0.25s → 0.5s 横向鼓开（BackEase 弹簧回弹）
    //   收起：完全反向，0 → 0.25s 横向收窄、0.25s → 0.5s 竖向缩回

    private const double LaunchBoardColumnRatio = 0.5;


    // ==========================
    // 鼓开阶段（0.25s → 0.5s）的弹簧回弹幅度
    // 这一段用 BackEase(EasingMode = EaseOut)：滑到终点前轻轻往回弹一下，
    // 模拟开源项目那种液体向两边鼓开、再被表面张力拉住的手感
    // 0.2 约等于最多超出终点 2%（800 宽的展板鼓到 816 左右），
    // 仍在窗口顶部预留的余量里，不会被窗口边缘裁掉
    // 只挂在横向 ScaleX 上（竖向会缩到 0.01，回弹会越过 0 变负缩放 → 画面闪镜像）
    // ==========================

    private const double LaunchBoardExtrudeSpringAmplitude = 0.2;


    // 【写死的起点】九点阵上那个小点
    // 直接改这两个数字就能微调"从多大的点开始长"：
    //   ScaleX 0.1 / ScaleY 0.01 → 展板 800 x 600 时约 80 x 6 的小点
    // 这个小点贴在哪个位置，由 RenderTransformOrigin 决定（见 LaunchBoardOriginFixedX / Y）

    private const double LaunchBoardExtrudeStartScaleX = 0.1;


    private const double LaunchBoardExtrudeStartScaleY = 0.01;


    // 【已停用·保留备用】旧版起点的两个平移量
    // 平移已经彻底删除（容器位置必须永远居中不动），这两个常数不再被任何代码使用

    private const double LaunchBoardExtrudeStartTranslateX = -200;


    private const double LaunchBoardExtrudeStartTranslateY = 50;


    // 内容层（图标 / 文字）淡入的时刻比例
    // 面板鼓开到六成（0.3s）之后才开始显示图标，避免内容被挤扁

    private const double LaunchBoardContentFadeInRatio = 0.6;


    // 内容层淡出：收起一开始的 20%（0.1s）就把内容清掉
    // 内容被挤扁的那一瞬间它是看不见的

    private const double LaunchBoardContentFadeOutRatio = 0.2;


    // 【已停用·保留备用】终点平移：0 = 展板原本的位置（平移动画已删除，这个常数不再被使用）

    private const double LaunchBoardTransitionTranslateEnd = 0;


    // 【保命兜底】现场测量失败时写死的偏移（只改这两个数字就能手调）
    // 九点阵（LaunchButton）在屏幕上的大致位置（Dock 宽 560、高 100）：
    //   横向：Dock 居中 → 九点阵中心 ≈ 窗口中心 - 231
    //   纵向：Dock 贴窗口底边 → 九点阵中心 ≈ 窗口底边 - 50
    // 这组数字只保证偏移不是 NaN / Infinity（不会让展板突然消失）
    // 真正的动画起点（缩放比例）见 LaunchBoardExtrudeStartScaleX / ScaleY

    private const double LaunchBoardTranslateFallbackX = -230;

    private const double LaunchBoardTranslateFallbackY = 360;


    // 九点阵中心与展板中心之间的偏差
    // 只用来算锚点 X（= 0.5 + 偏移X / 展板宽度），不再当平移动画的起 / 终点
    // 打开前现场测一次存这里；测不到就保持上一次的好值 / 兜底常量

    private Point launchBoardTransitionOffset =
        new Point(
            LaunchBoardTranslateFallbackX,
            LaunchBoardTranslateFallbackY);


    // 起点液滴的缩放比例（= 10px / 展板尺寸）
    // 每次打开现场算，展板尺寸变了也跟着变

    private double launchBoardDropletScaleX =
        LaunchBoardDropletScaleFallbackX;

    private double launchBoardDropletScaleY =
        LaunchBoardDropletScaleFallbackY;


    // 本次转场的起点，也就是每条动画的 From
    // 中途反向（动画没跑完又点了一次）时，这组值 = 当前正在显示的实时值
    // → 从当前位置接着走，不会跳帧
    // 动画开始前这组值会被写成基值：基值 = From，Stop 之后也不会闪

    private double launchBoardFromScaleX = 1;

    private double launchBoardFromScaleY = 1;

    private double launchBoardFromBoardOpacity = 0;

    private double launchBoardFromContentOpacity = 1;


    // 转场是否正在跑
    // 用来判断这次是"全新打开"还是"中途反向"，决定要不要从当前实时值接着走

    private bool isLaunchBoardTransitionRunning;


    // Storyboard 的间接属性路径
    // 官方规则：动画目标必须是 FrameworkElement，
    // 要驱动 TransformGroup 里的 Freezable，必须这样穿进去
    // Children[0] = ScaleTransform（TransformGroup 里现在只剩这一个变换）

    private const string LaunchBoardScalePathX =
        "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)";


    private const string LaunchBoardScalePathY =
        "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)";


    // 直接属性的路径（容器 / 内容层的 Opacity）
    // 目标就是控件本身，所以路径只有一个属性名

    private const string LaunchBoardOpacityPath =
        "Opacity";


    // 打开前的窗口高度
    // 关闭展板时按此还原

    private double launchBoardBaseHeight = 0;


    // 本次打开时的展板尺寸
    // 每次打开按当前屏幕重新算

    private double launchBoardWidth = LaunchBoardDesiredWidth;


    private double launchBoardHeight = LaunchBoardDesiredHeight;


    // 展板是否处于打开状态

    private bool isLaunchBoardOpen = false;


    // ==========================
    // 右键菜单 / "移除"确认框是否正在弹（本次修复新增）
    // 右键点 Dock 或展板上的图标 → 会弹"是否移除"确认框
    // 弹框 / 弹菜单本身就会抢走窗口焦点
    // 窗口一失焦就会触发 Window_Deactivated
    // 于是展板就被"点外部关闭"逻辑误关了
    // 用这个标记把这段弹框时间圈起来：
    //   弹框期间 → 绝不允许关展板
    // ==========================

    private bool isRightClickMenuOpen = false;


    // 正在跑的展板动画
    // 开新动画前必须先停掉它
    // 否则两条动画会抢同一个属性

    private Storyboard launchBoardStoryboard = null;


    // ==========================
    // 展板槽位参数（iOS 灵动风格）
    // ==========================


    // 单个应用槽位：80 x 80
    // 四周 Margin 20 → 一位实际占 120 x 120
    // 留白是为了“呼吸感”，不是文件夹式的贴边网格

    private const double BoardSlotSize = 80;


    private const double BoardSlotMargin = 20;


    // 暂时只做前两页

    private const int BoardPageCount = 2;


    // 展板 800 x 600 时内容区的真实大小
    // 扣掉 XAML 里 BoardContentLayer 的 Margin(56,50,56,58)
    // 第一次布局之前取不到 Viewport，用这组兜底

    private const double BoardFallbackPageWidth = 688;


    private const double BoardFallbackPageHeight = 492;




    // 悬停放大：1.0 → 1.1

    private const double BoardHoverScale = 1.1;


    private const double BoardHoverInDuration = 180;


    private const double BoardHoverOutDuration = 240;




    // ==========================
    // Dock 容量：最多同时显示 12 个
    // 第 13 个起不再撑宽 Dock
    // 直接进"收纳盒"（展板）的数据集合
    // ==========================

    private const int DockCapacity = 12;


    // 展板里放的应用
    // 独立于 Dock 的一份列表，不参与 Dock 宽度计算

    private readonly ObservableCollection<AppItem> boardApps =
        new ObservableCollection<AppItem>();


    // 与 boardApps 一一对应的原始文件路径
    // 只用于存配置与显示名字，不碰 AppItem 内部结构

    private readonly List<string> boardSourcePaths =
        new List<string>();




    // 展板自己的轻量配置：一行一个路径

    private static readonly string BoardStorePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GlassDock",
            "LaunchBoard.txt");


    // ==========================
    // 桌面图标处理模式（设置面板最下面那一行）
    // 模式A 保留在桌面（默认）：
    //    拖入 Dock 只记录路径，桌面上的文件一动不动
    //    从 Dock 删除时只删除 Dock 里的图标
    // 模式B 收纳至 Dock：
    //    拖入 Dock 时把桌面上的 .lnk / .exe
    //    【移动】到 ShortcutStoreDir，
    //    并把 Dock 里记录的路径换成搬过去的新路径
    //    从 Dock 删除时，把收纳目录里的源文件
    //    【移动】回桌面（File.Move，绝不 File.Delete）
    // 这里只碰"文件移动"和"路径记录"
    // 图标提取 / 图标渲染 / 去重一行都没动
    // ==========================

    private enum DockIconMode
    {

        // 保留在桌面

        KeepOnDesktop = 0,


        // 收纳至 Dock

        MoveToStore = 1

    }


    // 当前生效的模式（默认模式A）

    private DockIconMode dockIconMode =
        DockIconMode.KeepOnDesktop;


    // 模式药丸是否正在被代码回写
    // 防止 Checked 事件再绕一圈回来

    private bool isSyncingIconMode = false;


    // 收纳目录：桌面快捷方式搬进来的专属文件夹
    // %AppData%\GlassDock\Shortcuts
    // （和展板配置 LaunchBoard.txt 同一个应用数据目录）

    private static readonly string ShortcutStoreDir =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GlassDock",
            "Shortcuts");


    // 模式配置：一个小文本文件，一行 KeepOnDesktop / MoveToStore

    private static readonly string IconModeStorePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GlassDock",
            "IconMode.txt");


    // 每页能放几个槽位（按真实视口算，布局前是兜底值）

    private int boardItemsPerPage = 20;


    // 指示点状态缓存：值没变就不重复起动画

    private int boardLastPages = -1;


    private int boardLastActivePage = -1;


    // ==========================
    // Dock ↔ 展板 互换
    // 拖拽载荷走自定义格式，只在本进程内传
    // 不碰文件系统，也不加全局钩子
    // ==========================

    private const string AppDragFormat =
        "GlassDock.AppItem";


    // 来源标记

    private const string AppDragFromDock =
        "dock";


    private const string AppDragFromBoard =
        "board";


    // 按下时是不是按在 Dock 图标上
    // 决定这次手势是"移动窗口"还是"拖应用"

    private AppItem? dockDragIconSource = null;


    // 方向还没定型时的等待范围（像素）
    // 在图标上按住后位移没出这个范围，就先别动窗口
    // 等方向清楚了再决定：拖应用还是移动窗口

    private const double DockAppDragUpDistance = 10;


    // 图标上按住后，是不是必须往上拖才算拖应用
    // 想"按住图标往任意方向拖都是拖应用"就改成 false
    // 用 static readonly 而不是 const：const 会被编译期折叠
    // 折掉之后下面的分支会被编译器报"无法访问的代码"

    private static readonly bool RequireUpwardDragForApp = true;


    // 展板图标的拖动状态

    private AppItem? boardDragSource = null;


    private bool boardDragArmed = false;


    private bool boardDragStarted = false;


    private Point boardDragStartPoint =
        new Point(0,0);


    // 刚拖完一次展板图标
    // 松手时不再启动应用

    private bool boardIconDragSuppressClick = false;


    // ==========================
    // 展板：选择性批量删除
    // ==========================

    // 当前是否处于批量管理模式
    // 普通模式下点图标照旧启动应用，逻辑一行没改

    private bool isBoardBatchMode = false;


    // 当前选中的展板图标

    private readonly HashSet<AppItem> boardSelectedApps =
        new HashSet<AppItem>();


    // 绑定给展板图标模板：批量模式的开关
    // 模板里的选择标记用它控制显示 / 隐藏

    public static readonly DependencyProperty BoardBatchModeProperty =
        DependencyProperty.Register(
            nameof(BoardBatchMode),
            typeof(bool),
            typeof(MainWindow),
            new PropertyMetadata(false));


    public bool BoardBatchMode
    {
        get
        {
            return (bool)GetValue(BoardBatchModeProperty);
        }

        set
        {
            SetValue(BoardBatchModeProperty, value);
        }
    }


    // 绑定给每一个展板图标：这个图标是否被选中
    // 用附加属性，AppItem.cs 不需要改

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected",
            typeof(bool),
            typeof(MainWindow),
            new FrameworkPropertyMetadata(false));


    public static bool GetIsSelected(
        DependencyObject element)
    {
        return (bool)element.GetValue(IsSelectedProperty);
    }


    public static void SetIsSelected(
        DependencyObject element,
        bool value)
    {
        element.SetValue(IsSelectedProperty, value);
    }



    // ==========================
    // 拖拽视觉：幽灵图标 + Dock 动态让位
    // 内部拖拽（Dock ↔ 展板）不用 DoDragDrop
    // 改成鼠标捕获：每一帧移动都直接进我们自己的消息循环
    // 这样让位动画与跟手图标不会被拖放循环冻住
    // 外部的文件拖入（资源管理器 → Dock/展板）仍然走原来的 DoDragDrop
    // ==========================


    // 自定义拖拽进行中

    private bool appDragActive = false;


    // 鼠标捕获是否成功

    private bool appDragCaptured = false;


    // 本次拖拽的来源：AppDragFromDock / AppDragFromBoard

    private string appDragFrom = "";


    // 被拖的应用与它的原始文件路径

    private AppItem? appDragItem = null;


    private string appDragSourcePath = "";


    // 跟手的幽灵图标

    private FrameworkElement? appDragGhost = null;


    // 拖拽起点所在的槽位
    // 拖动期间半透明，表示"这个位置已经腾出来了"

    private FrameworkElement? appDragSourceContainer = null;


    private double appDragSourceContainerOpacity = 1;


    // 当前鼠标是否压在 Dock 上

    private bool appDragOverDock = false;


    // 本次落点对应的插入槽位（-1 = 还没算）

    private int appDragInsertIndex = -1;


    // 已经画到界面上的让位槽位
    // 值没变就不重复起动画，避免每帧重排

    private int dockMakeWayIndex = -1;


    // 刚落进 Dock 的槽位（-1 = 没有）
    // 用它给新图标补一个入位动画

    private int dockArrivalIndex = -1;


    // 展板内部换位：拖动源在展板列表里的槽位（-1 = 拖动源不在展板里）

    private int appDragBoardSourceIndex = -1;


    // 本次落点对应的展板槽位（-1 = 还没算）

    private int boardDragInsertIndex = -1;


    // 已经画到界面上的展板让位槽位
    // 和 dockMakeWayIndex 一个套路：值没变就不重复起动画

    private int boardMakeWayIndex = -1;


    // 这次展板让位是从哪个槽位开始的
    // -1 = 新图标插入（Dock → 展板），不是换位

    private int boardMakeWaySourceIndex = -1;


    // 刚落进展板的新槽位（-1 = 没有）
    // 用它给那一格补一个入位动画

    private int boardArrivalIndex = -1;


    // 幽灵图标尺寸 / 图标绘制尺寸

    private const double DragGhostSize = 76;

    private const double DragGhostIconSize = 58;


    // 拖起 / 松手的动画时长(毫秒)

    private const double DragGhostRiseDuration = 190;

    private const double DragGhostDropDuration = 170;


    // Dock 图标让位 / 归位 / 被挤出 的动画时长(毫秒)

    private const double DockMakeWayDuration = 170;

    private const double DockSettleDuration = 220;

    private const double DockEjectDuration = 430;


    // 展板槽位间距：图标 80 + 左右各 20 的外边距 = 120
    // Dock 是单行均匀槽位(IconSlotWidth)，展板是网格槽位

    private const double BoardSlotPitch = BoardSlotSize + BoardSlotMargin * 2;


    // 展板让位 / 归位 / 入位 的动画时长(毫秒)
    // 和 Dock 共用同一组时长，两块区域的手感完全一致

    private const double BoardMakeWayDuration = DockMakeWayDuration;

    private const double BoardSettleDuration = DockSettleDuration;


    // 被挤出的图标飞走时的尺寸
    // 跟 Dock 里那个图标一样大(62 / 46)
    // 这样飞出去的一刻看着就是原位那个

    private const double DockIconVisualSize = 62;

    private const double DockIconVisualIconSize = 46;


    // Dock 落点判定容差(像素)
    // Dock 与展板之间正好留了 10px 缝，这点容差把缝补上

    private const double DockDropTolerance = 8;



    // 拖拽载荷

    private sealed class AppDragPayload
    {

        // "dock" / "board"

        public string From = "";


        // 被拖的那个应用

        public AppItem? App = null;


        // 放回 Dock / 收进展板时要用的文件路径

        public string SourcePath = "";

    }


        public MainWindow()
    {


        InitializeComponent();





        // 任务栏不再出现图标
        // 窗口改由系统托盘接管

        ShowInTaskbar = false;


        // 创建系统托盘图标
        // 图标：Assets\app.ico
        // 提示文字：AeroDock

        InitializeTrayIcon();


        Topmost = false;




        // ==========================
        // 全局拖动
        // Dock任何位置按住都可移动窗口
        // 按住不动仍然是点击图标
        // ==========================


        DockBody.PreviewMouseLeftButtonDown +=
            DockDrag_Down;


        PreviewMouseMove +=
            DockDrag_Move;


        PreviewMouseLeftButtonUp +=
            DockDrag_Up;


        // 任意一次左键按下
        // 先把展板图标的拖动状态清干净
        // 免得上一次手势的残留标记误触发拖拽

        PreviewMouseLeftButtonDown +=
            BoardDrag_Press;








        // 图标数量变化时自适应Dock宽度

        AppManager.Apps.CollectionChanged +=
            Apps_CollectionChanged;


        // 从本地配置恢复 Dock 图标

        AppManager.Initialize();




        // 展板初始化
        // 自己的列表 + 拖放 + 从本地配置恢复

        InitializeBoard();




        // 旧配置兜底：Dock 里如果存了超过 12 个
        // 多出来的全部挪进展板，Dock 只留前 12 个

        MigrateDockOverflowToBoard();





        IconPanel.ItemsSource =
            AppManager.Apps;







        // ==========================
        // Dock 圆角裁剪
        // ClipToBounds 只能裁矩形
        // 玻璃层的投影会漏进矩形四角
        // 这里换成随尺寸同步的圆角矩形裁剪
        // ==========================

        DockBody.SizeChanged +=
            DockBody_SizeChanged;


        UpdateDockClip(
            new Size(
                DockBody.Width,
                DockBody.Height));


        Loaded += MainWindow_Loaded;








        CompositionTarget.Rendering +=
            DockRendering;








        watcher =
            new DockWatcher(this);







        watcher.DockHover +=
            Watcher_DockHover;







        watcher.Start();








        Application.Current
        .DispatcherUnhandledException +=
        (s,e)=>
        {



            if(handlingUnhandledException)
            {

                Debug.WriteLine(
                    "DispatcherUnhandledException(重入): "
                    +
                    e.Exception.Message);

                e.Handled = true;

                return;

            }

            handlingUnhandledException = true;

            MessageBox.Show(
                e.Exception.Message,
                "AeroDock错误");

            handlingUnhandledException = false;



            e.Handled = true;



        };



    }









    // ==========================
    // Dock 圆角裁剪
    // ClipToBounds 只按矩形裁
    // 玻璃层的 DropShadow 会漏进矩形四角
    // 看着就是圆角胶囊外面套了一圈灰色直角框
    // 这里把裁剪换成圆角矩形：半径 = 高度 / 2
    // 与 DockBody.CornerRadius 的取值规则完全一致
    // 尺寸一变就同步：展开 / 收敛 / 正圆 都贴得住
    // ==========================

    private void DockBody_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {

        UpdateDockClip(
            e.NewSize);

    }


    private void UpdateDockClip(
        Size size)
    {

        if(DockClip == null)
            return;


        double width =
            size.Width;


        double height =
            size.Height;


        if(double.IsNaN(width) ||
           double.IsNaN(height) ||
           width <= 0 ||
           height <= 0)
            return;


        // 半径取高度的一半
        // 宽度比高度还小时再兜一次底
        // 保证收成正圆时半径也不会超过宽度的一半

        double radius =
            Math.Min(
                height / 2,
                width / 2);


        // 极少数情况下 XAML 里的几何会被冻结
        // 冻结了就换一个新的，逻辑完全一样

        if(DockClip.IsFrozen)
        {

            DockBody.Clip =
                new RectangleGeometry(
                    new Rect(
                        0,
                        0,
                        width,
                        height),
                    radius,
                    radius);

            return;

        }


        DockClip.Rect =
            new Rect(
                0,
                0,
                width,
                height);


        DockClip.RadiusX =
            radius;


        DockClip.RadiusY =
            radius;

    }


    private void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {


        // 按当前图标数量确定Dock宽度

        ApplyDockWidthImmediately();


        Left =
        (
            SystemParameters.WorkArea.Width
            -
            Width
        )
        /
        2;






        Top =
        SystemParameters.WorkArea.Height
        -
        Height;







        DesktopLayerManager.Refresh(
            this);







        // 开机自启动开关：按注册表里的真实状态回写
        // 注册表里存在 AeroDock 这个键 → 开关自动勾选
        // 只读注册表，不改任何 Dock 布局与动画

        UpdateStartupToggleState();


        // 桌面图标处理模式：读本地配置回写面板上的药丸
        // 只读一个小文本文件，不改 Dock 布局与动画

        LoadIconMode();

        UpdateIconModeUi();


        // 两个材质滑块（背景透明度 / 背景模糊度）：读上次存的值并应用
        // 文件缺失 / 损坏 / 没权限都只是回默认值
        // LoadDockSettings 内部全程 try-catch，绝不挡启动

        LoadDockSettings();


        // 展板自定义壁纸：读独立的 LaunchBoardWallpaper.json
        // 只有配置里有路径、且文件真实存在时才铺上去
        // 完全不碰上面两个材质值；清除后原有玻璃 / 模糊效果原样回来

        LoadLaunchBoardWallpaperFromStore();


        // 【本次新增】另外两个开关也一起恢复（重启不丢）
        // 「锁定位置」「壁纸取色」存在同一份 Settings.json 里
        // 只读文件 + 走原来就有的 SetDockLocked / 取色逻辑
        // 不碰图标 / 拖拽 / 九点阵 / 悬停 / 双模式 / 挤出动画

        LoadExtraDockSettingsFromStore();


        // 常驻模式

        ShowDock();



    }









    // ==========================
    // 图标数量变化
    // ==========================

    private void Apps_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {

        // 启动恢复阶段不做动画

        if(!IsLoaded)
            return;


        AnimateDockWidth();

    }




    // ==========================
    // 计算Dock目标宽度
    // ==========================

    private double CalculateDockWidth()
    {

        // Dock 按容量封顶
        // 超出的应用进收纳盒（展板）
        // 这样即使配置里存了更多，也不会把 Dock 撑长

        int dockCount =
            AppManager.Apps.Count;

        if(dockCount > DockCapacity)
            dockCount = DockCapacity;

        double content =
            dockCount *
            IconSlotWidth;


        double target =
            content +
            DockPadding * 2 +
            LaunchButtonSlot;


        if(target < MinDockWidth)
            target = MinDockWidth;


        double max =
            SystemParameters.WorkArea.Width -
            DockMaxMargin;


        if(max >= MinDockWidth &&
           target > max)
            target = max;


        return target;

    }




    // ==========================
    // 窗口几何
    // 位置与大小必须一次性原子写入
    // 否则窗口矩形先变、画面后变
    // 桌面上就会出现一帧闪烁
    // ==========================

    private IntPtr windowHandle = IntPtr.Zero;


    private const uint SWP_NOZORDER = 0x0004;

    private const uint SWP_NOACTIVATE = 0x0010;


    [DllImport(
        "user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);


    [DllImport(
        "user32.dll")]
    private static extern bool GetWindowRect(
        IntPtr hWnd,
        out NativeRect rect);


    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativeRect
    {

        public int Left;

        public int Top;

        public int Right;

        public int Bottom;

    }


    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativePoint
    {

        public int X;

        public int Y;

    }


    [DllImport(
        "user32.dll")]
    private static extern IntPtr GetParent(
        IntPtr hWnd);


    [DllImport(
        "user32.dll")]
    private static extern bool ClientToScreen(
        IntPtr hWnd,
        ref NativePoint point);


    // Dock 挂到桌面层(WorkerW)之后就是子窗口
    // SetWindowPos 用的是父窗口客户区坐标
    // GetWindowRect 给的却是屏幕坐标
    // 先取父窗口客户区在屏幕上的物理原点
    // 读写两边都按它换算，窗口才不会整体偏移

    private Point GetParentClientOrigin()
    {

        IntPtr handle =
            GetWindowHandle();


        IntPtr parent =
            handle == IntPtr.Zero
            ? IntPtr.Zero
            : GetParent(handle);


        if(parent == IntPtr.Zero)
            return new Point(0, 0);


        NativePoint origin =
            new NativePoint();


        if(!ClientToScreen(
            parent,
            ref origin))
            return new Point(0, 0);


        return new Point(
            origin.X,
            origin.Y);

    }


    // 取窗口句柄(缓存)

    private IntPtr GetWindowHandle()
    {

        if(windowHandle == IntPtr.Zero)
            windowHandle =
                new WindowInteropHelper(
                    this)
                .Handle;


        return windowHandle;

    }


    // 逻辑单位 -> 物理像素

    private Matrix GetDeviceTransform()
    {

        IntPtr handle =
            GetWindowHandle();


        if(handle == IntPtr.Zero)
            return Matrix.Identity;


        HwndSource source =
            HwndSource.FromHwnd(
                handle);


        if(source == null ||
           source.CompositionTarget == null)
            return Matrix.Identity;


        return source.CompositionTarget
            .TransformToDevice;

    }


    // 读取窗口真实位置与大小
    // 窗口被原子改过之后
    // WPF 的 Width / Height 属性不再可信

    private bool TryGetWindowBounds(
        out double left,
        out double top,
        out double width,
        out double height)
    {

        left = Left;

        top = Top;

        width = Width;

        height = Height;


        IntPtr handle =
            GetWindowHandle();


        NativeRect rect =
            default;


        bool fromNative =
            handle != IntPtr.Zero &&
            GetWindowRect(
                handle,
                out rect);


        if(fromNative)
        {

            // 子窗口坐标 → 屏幕坐标 → 逻辑坐标

            Point parentOrigin =
                GetParentClientOrigin();


            Matrix toLogical =
                GetDeviceTransform();


            if(toLogical.HasInverse)
                toLogical.Invert();


            Point corner =
                toLogical.Transform(
                    new Point(
                        rect.Left - parentOrigin.X,
                        rect.Top - parentOrigin.Y));


            Point size =
                toLogical.Transform(
                    new Point(
                        rect.Right - rect.Left,
                        rect.Bottom - rect.Top));


            left = corner.X;

            top = corner.Y;

            width = size.X;

            height = size.Y;

        }


        if(double.IsNaN(left))
            left = 0;


        if(double.IsNaN(top))
            top = 0;


        if(double.IsNaN(width) ||
           width <= 0)
            return false;


        if(double.IsNaN(height) ||
           height <= 0)
            return false;


        return true;

    }


    // 一次性设置窗口位置与大小
    // 只有一次窗口矩形变化

    private bool ApplyWindowBounds(
        double left,
        double top,
        double width,
        double height)
    {

        if(double.IsNaN(left) ||
           double.IsNaN(top) ||
           double.IsNaN(width) ||
           double.IsNaN(height))
            return false;


        if(width <= 0 ||
           height <= 0)
            return false;


        IntPtr handle =
            GetWindowHandle();


        if(handle == IntPtr.Zero)
            return false;


        Matrix toDevice =
            GetDeviceTransform();


        Point position =
            toDevice.Transform(
                new Point(
                    left,
                    top));


        Point size =
            toDevice.Transform(
                new Point(
                    width,
                    height));


        // 逻辑坐标 → 物理坐标 → 子窗口坐标

        Point parentOrigin =
            GetParentClientOrigin();


        bool moved =
            SetWindowPos(
                handle,
                IntPtr.Zero,
                (int)Math.Round(
                    position.X +
                    parentOrigin.X),
                (int)Math.Round(
                    position.Y +
                    parentOrigin.Y),
                (int)Math.Round(
                    size.X),
                (int)Math.Round(
                    size.Y),
                SWP_NOZORDER |
                SWP_NOACTIVATE);


        // 物理矩形写完之后
        // 把真实几何值同步回 WPF 自己的属性
        // 否则之后任何一次布局 / 拖动
        // 都会拿旧属性值把窗口改回展开时的大小

        if(moved)
            MirrorWindowProperties(
                left,
                top,
                width,
                height);


        return moved;

    }


    // 把窗口矩形吸附到物理像素网格
    // 宽高取偶数、中心取整
    // 居中布置的胶囊与 Logo
    // 每帧都落在同一个整像素上
    // 否则宽高奇偶交替会让它们
    // 在半像素上反复来回跳
    // 看起来就是明显的抽动
    // 只用于动画 / 同步这两条路径
    // 拖动时不能用，否则位置会被锁住

    private void SnapToPixelGrid(
        ref double left,
        ref double top,
        ref double width,
        ref double height)
    {

        if(double.IsNaN(width) ||
           double.IsNaN(height) ||
           width <= 0 ||
           height <= 0)
            return;


        Matrix toDevice =
            GetDeviceTransform();


        double scaleX =
            toDevice.M11 > 0
            ? toDevice.M11
            : 1;


        double scaleY =
            toDevice.M22 > 0
            ? toDevice.M22
            : 1;


        // 中心与底边吸附到整物理像素
        // 动画期间这两个值本来就不动

        double center =
            Math.Round(
                (left + width / 2) *
                scaleX);


        double bottom =
            Math.Round(
                (top + height) *
                scaleY);


        // 宽高取偶数
        // 中心在整像素上时两边才对称
        // 居中的内容位置才是整数

        double snappedWidth =
            Math.Round(
                width *
                scaleX / 2) * 2;


        double snappedHeight =
            Math.Round(
                height *
                scaleY / 2) * 2;


        if(snappedWidth < 2)
            snappedWidth = 2;


        if(snappedHeight < 2)
            snappedHeight = 2;


        width =
            snappedWidth /
            scaleX;


        height =
            snappedHeight /
            scaleY;


        left =
            (center -
             snappedWidth / 2) /
            scaleX;


        top =
            (bottom -
             snappedHeight) /
            scaleY;

    }


    // 把真实几何值写回 WPF 自己的窗口属性
    // 值先和真实窗口对齐，之后系统再按属性改一次
    // 写进去的也是同样的矩形，不会跳
    // 不写回的话，任何一次布局 / 拖动都会拿旧值
    // 把窗口改回展开时的大小
    // 用 SetCurrentValue 不打断绑定与动画

    private void MirrorWindowProperties(
        double left,
        double top,
        double width,
        double height)
    {

        try
        {

            SetCurrentValue(
                LeftProperty,
                left);


            SetCurrentValue(
                TopProperty,
                top);


            SetCurrentValue(
                WidthProperty,
                width);


            SetCurrentValue(
                HeightProperty,
                height);

        }
        catch
        {

        }

    }


    // ==========================
    // 窗口宽度同步
    // 始终保持Dock中心不动
    // ==========================

    private void ResizeWindowTo(
        double windowWidth)
    {

        if(double.IsNaN(windowWidth) ||
           windowWidth <= 0)
            return;


        // 展板打开期间窗口不能再变窄
        // 否则展板会被窗口裁掉

        if(isLaunchBoardOpen &&
           windowWidth <
           launchBoardWidth +
           LaunchBoardSpringRoom * 2)
            windowWidth =
                launchBoardWidth +
                LaunchBoardSpringRoom * 2;


        double left;

        double top;

        double width;

        double height;


        // 用真实几何值判断
        // 窗口可能被原子改过

        if(!TryGetWindowBounds(
            out left,
            out top,
            out width,
            out height))
            return;


        double center =
            left +
            width / 2;


        double newLeft =
            center -
            windowWidth / 2;


        double newWidth =
            windowWidth;


        double newHeight =
            height;


        // 吸附到像素网格
        // 宽高取偶数、中心取整
        // 居中的胶囊与 Logo 每帧
        // 都落在同一个整像素上
        // 不会出现半像素来回跳

        SnapToPixelGrid(
            ref newLeft,
            ref top,
            ref newWidth,
            ref newHeight);


        // 不越出工作区
        // 工作区是屏幕逻辑坐标
        // 这里要换算成子窗口坐标

        Point parentOrigin =
            GetParentClientOrigin();


        Matrix toLogical =
            GetDeviceTransform();


        if(toLogical.HasInverse)
            toLogical.Invert();


        Point parentOriginLogical =
            toLogical.Transform(
                parentOrigin);


        double minLeft =
            SystemParameters.WorkArea.Left -
            parentOriginLogical.X;


        double maxLeft =
            SystemParameters.WorkArea.Right -
            newWidth -
            parentOriginLogical.X;


        if(maxLeft < minLeft)
            maxLeft = minLeft;


        if(newLeft < minLeft)
            newLeft = minLeft;


        if(newLeft > maxLeft)
            newLeft = maxLeft;


        // 变化太小就不动
        // 避免每帧重复写窗口矩形

        if(Math.Abs(left - newLeft) < 0.5 &&
           Math.Abs(width - newWidth) < 0.5 &&
           Math.Abs(height - newHeight) < 0.5)
            return;


        // 位置与大小一次性写入
        // 避免先改宽度再移动产生闪帧

        ApplyWindowBounds(
            newLeft,
            top,
            newWidth,
            newHeight);

    }




    // ==========================
    // 启动时立即应用宽度
    // 不做动画
    // ==========================

    private void ApplyDockWidthImmediately()
    {

        double target =
            CalculateDockWidth();


        dockWidthTarget =
            target;


        DockBody.BeginAnimation(
            WidthProperty,
            null);


        DockBody.Width =
            target;


        // 窗口宽度走原子写入
        // 直接用 WPF 的 Width 属性会把窗口
        // 按旧的位置与高度一起改回去

        ResizeWindowTo(
            target);

    }




    // ==========================
    // Dock宽度平滑扩展
    // ==========================

    private void AnimateDockWidth()
    {

        double target =
            CalculateDockWidth();


        dockWidthTarget =
            target;


        // 收起 / 收起动画期间
        // 只记录目标宽度
        // 宽度动画交给收起动画接管

        if(isCollapsed ||
           isCollapseAnimating)
            return;


        double current =
            DockBody.Width;


        if(double.IsNaN(current) ||
           current <= 0)
            current = MinDockWidth;


        // 动画期间窗口必须更宽
        // 否则Dock会被窗口裁掉

        ResizeWindowTo(
            Math.Max(
                current,
                target));


        // 宽度没有变化

        if(Math.Abs(
            current - target) < 0.5)
        {

            DockBody.BeginAnimation(
                WidthProperty,
                null);


            DockBody.Width =
                target;


            ResizeWindowTo(
                target);


            return;

        }


        // 动画进行中再次触发时
        // 先固定基值，避免结束后跳变

        if(DockBody.HasAnimatedProperties)
        {

            DockBody.Width =
                target;

        }


        DoubleAnimation animation =
            new DoubleAnimation
            {

                From =
                current,


                To =
                target,


                Duration =
                TimeSpan.FromMilliseconds(
                    DockWidthDuration),


                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseOut

                },


                FillBehavior =
                FillBehavior.HoldEnd

            };


        animation.Completed +=
            DockWidth_Completed;


        DockBody.BeginAnimation(
            WidthProperty,
            animation);

    }




    // ==========================
    // 宽度动画结束
    // ==========================

    private void DockWidth_Completed(
        object? sender,
        EventArgs e)
    {

        // 收起状态下窗口宽度由收起动画管理
        // 这里不能再回改窗口宽度

        if(isCollapsed ||
           isCollapseAnimating)
            return;


        // 先固定基值再移除动画
        // 数值不会跳变

        DockBody.Width =
            dockWidthTarget;


        DockBody.BeginAnimation(
            WidthProperty,
            null);


        // 窗口收回，与Dock等宽

        ResizeWindowTo(
            dockWidthTarget);

    }




    // ==========================
    // 收起 / 展开：箭头按钮
    // ==========================


    // 箭头按下
    // 拦截事件，避免触发 DockBody 的点击逻辑
    // 但手动接上全局拖动判定
    // 因此按住箭头拖动依然可以移动 Dock
    // 只有原地松手才算点击(收起)

    private void CollapseButton_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {

        e.Handled = true;


        DockDrag_Down(
            sender,
            e);

    }


    // 箭头悬停高亮

    private void CollapseButton_MouseEnter(
        object sender,
        MouseEventArgs e)
    {

        FadeElement(
            CollapseArrow,
            0.95,
            0,
            140,
            EasingMode.EaseOut);

    }


    private void CollapseButton_MouseLeave(
        object sender,
        MouseEventArgs e)
    {

        FadeElement(
            CollapseArrow,
            0.55,
            0,
            200,
            EasingMode.EaseOut);

    }


    // 点击箭头：收起 Dock

    private void CollapseButton_Click(
        object sender,
        MouseButtonEventArgs e)
    {

        e.Handled = true;


        // 展板会在 SetCollapsed 里立即收起
        // 避免展板高度被记进 dockWindowHeight

        SetCollapsed(true);

    }


    // 双击小 Logo：展开 Dock

    private void CollapseLogo_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {

        // 拦截事件，避免触发 DockBody 的点击逻辑
        // 但手动接上全局拖动判定
        // 因此按住 Logo 也能拖动整个 Dock
        // 位移没超过系统拖动阈值时仍然是点击

        e.Handled = true;


        DockDrag_Down(
            sender,
            e);


        // 双击展开
        // 该窗口拿不到系统双击消息
        // 因此自行判定双击

        Point now =
            e.GetPosition(
                CollapseLogo);


        bool doubleClicked =
            e.ClickCount >= 2;


        if(!doubleClicked)
        {

            double elapsed =
                (DateTime.Now - lastLogoClickTime)
                .TotalMilliseconds;


            double dx =
                now.X -
                lastLogoClickPoint.X;


            double dy =
                now.Y -
                lastLogoClickPoint.Y;


            doubleClicked =
                elapsed >= 0 &&
                elapsed <= LogoDoubleClickMilliseconds &&
                Math.Abs(dx) <= LogoDoubleClickSlop &&
                Math.Abs(dy) <= LogoDoubleClickSlop;

        }


        if(doubleClicked)
        {

            lastLogoClickTime =
                DateTime.MinValue;


            lastLogoClickPoint =
                new Point(-9999, -9999);


            SetCollapsed(false);


            return;

        }


        lastLogoClickTime =
            DateTime.Now;


        lastLogoClickPoint =
            now;

    }


    // ==========================
    // 收起 / 展开主逻辑
    // 高度全程不变（= 展开高度）
    // 只补间宽度，圆角固定为高度的一半
    // 宽度缩到与高度相等即成正圆
    // 两边同时向内收，中心与底边不动
    // ==========================

    private void SetCollapsed(
        bool collapsed)
    {

        // 展板必须最先立即收起
        // 否则收起时窗口高度会被记成带展板的高度
        // 之后展开就会还原成带展板的尺寸

        CloseLaunchBoard(
            true);


        if(isCollapseAnimating)
            return;


        if(isCollapsed == collapsed)
            return;


        isCollapseAnimating = true;


        isCollapsed = collapsed;


        // 锁定动画期间不动的基准
        // 中心与底边取自当前真实矩形

        double anchorLeft;
        double anchorTop;
        double anchorWidth;
        double anchorHeight;


        if(TryGetWindowBounds(
            out anchorLeft,
            out anchorTop,
            out anchorWidth,
            out anchorHeight))
        {

            collapseAnchorCenterX =
                anchorLeft +
                anchorWidth / 2;


            collapseAnchorBottom =
                anchorTop +
                anchorHeight;

        }

        else
        {

            collapseAnchorCenterX =
                double.NaN;


            collapseAnchorBottom =
                double.NaN;

        }


        double fromWidth =
            DockBody.Width;


        if(double.IsNaN(fromWidth) ||
           fromWidth <= 0)
            fromWidth =
                collapsed
                ? dockWidthTarget
                : CollapseSize;


        // 记录展开状态下的真实高度
        // 展开时按记录值还原
        // 弹性伸展会改变窗口高度，因此每次收起都重新记录

        if(collapsed)
        {

            // 记录窗口真实高度
            // 展开时按此还原
            // 不再依赖 WPF 的 Height 属性

            double windowLeft;

            double windowTop;

            double windowWidth;

            double windowHeight;


            if(TryGetWindowBounds(
                out windowLeft,
                out windowTop,
                out windowWidth,
                out windowHeight) &&
               windowHeight > 0)
                dockWindowHeight =
                    Math.Max(
                        windowHeight,
                        DockExpandedHeight);


            if(DockBody.Height > 0 &&
               !double.IsNaN(
                   DockBody.Height))
                dockBodyHeight =
                    DockBody.Height;

        }


        if(dockBodyHeight <= 0)
            dockBodyHeight =
                DockExpandedHeight;


        if(dockWindowHeight <= 0)
            dockWindowHeight =
                dockBodyHeight;


        // 收起后的圆形直径
        // 就是展开状态下胶囊的真实高度
        // 宽度缩到这个值，长胶囊正好收成正圆

        CollapseSize =
            dockBodyHeight;


        // 圆形里的 Logo 边长跟着直径走
        // 直径的 64% 左右
        // 太小会显得空，太大会顶到圆边

        ApplyCollapseLogoSize(
            CollapseSize);


        double toWidth =
            collapsed
            ? CollapseSize
            : CalculateDockWidth();


        // 高度全程保持不变
        // 收缩只改宽度：宽度缩到与高度相等就是正圆

        double toHeight =
            dockBodyHeight;


        // 展开目标宽度先记住
        // 收起期间图标增减不影响本次动画

        if(!collapsed)
            dockWidthTarget = toWidth;


        if(collapsed)
        {

            // 九点阵 / 图标 / 箭头 在前段平滑淡出
            // 收缩过程中不留任何被挤扁的残影

            FadeElement(
                IconPanel,
                0,
                0,
                CollapseContentFade,
                EasingMode.EaseOut);


            FadeElement(
                LaunchButton,
                0,
                0,
                CollapseContentFade,
                EasingMode.EaseOut);


            FadeElement(
                CollapseArrow,
                0,
                0,
                CollapseContentFade,
                EasingMode.EaseOut);


            // Logo 在尾段淡入
            // 宽度收成正圆的那一刻刚好完全露出

            CollapseLogo.Visibility =
                Visibility.Visible;


            FadeElement(
                CollapseLogo,
                1,
                CollapseDuration -
                CollapseLogoFade,
                CollapseLogoFade,
                EasingMode.EaseOut);


            // 淡入的同时开始匀速自转
            // Logo 露出来的那一刻已经在转，不会先静止再起步

            StartLogoSpin();


            // 收起方向：末尾直接启动宽度动画
            //（展开方向的这一步排在 2 帧之后，见 DockRendering）

            StartDockWidthAnimation(
                fromWidth,
                toWidth,
                toHeight,
                collapsed);

        }
        else
        {

            // =====================================
            // 展开动画开播之前的 Logo 锚定
            // 修复「双击展开时 Logo 在 Dock 左上方闪一下」
            //
            // 顺序（不可调换）：
            //   1) 先让挂起的排版全部落地
            //      Logo / DockBody 的坐标都算完，读到的是真值
            //   2) 第一帧先把 Logo 隐掉
            //      即使坐标有微跳，画面上也完全看不到
            //   3) 把 Logo 的渲染层位移与边距压回
            //      收缩状态的起始坐标（基准 = 无位移）
            //   4) 复位后再把 Logo 恢复成收缩状态的可见值
            //      后面照旧播下面那段原有的淡出动画
            //      （时长 140ms / CubicEase EaseIn 一个都没动）
            //
            // 全程只碰 Logo 自己的 Opacity 与渲染层变换
            // 不改尺寸、不动窗口、不碰命中测试
            // 与图标 / 拖拽 / 九点阵 / 悬停 / 展板 / 右键全无关系
            // =====================================

            // 展开分支：Logo 先出局 + 渲染缓存先摘掉
            // 细节与原因全在 PrepareCollapseLogoForExpand() 里
            // 原来这里那段 140ms 的 Logo 淡出不必再跑：
            // Logo 此刻已经被收掉，画面上根本没有它

            PrepareCollapseLogoForExpand();


            // 图标与箭头延迟淡入

            IconPanel.Visibility =
                Visibility.Visible;


            IconPanel.Opacity =
                0;


            FadeElement(
                IconPanel,
                1,
                120,
                260,
                EasingMode.EaseOut);


            CollapseButton.Visibility =
                Visibility.Visible;


            if(SettingsButton != null)
                SettingsButton.Visibility =
                    Visibility.Visible;


            // 九点阵跟着图标一起淡入
            // 先清掉收起时的淡出动画，从 0 开始

            LaunchButton.BeginAnimation(
                OpacityProperty,
                null);


            LaunchButton.Opacity =
                0;


            LaunchButton.Visibility =
                Visibility.Visible;


            FadeElement(
                LaunchButton,
                1,
                120,
                260,
                EasingMode.EaseOut);


            FadeElement(
                CollapseArrow,
                0.55,
                150,
                220,
                EasingMode.EaseOut);


            // 展开动画开始前
            // 先把窗口一次性撑到最终尺寸
            // 中心与底边锁在动画开始时的锚点上
            // 动画期间窗口矩形完全不动
            // 不逐帧改窗口，就不会被系统拉伸画面
            //
            // 但不能在这一刻立刻改：
            // 窗口改矩形的那一瞬间，屏幕上还是「上一帧」的位图，
            // 而那一帧里 Logo 还亮着，
            // 它会被贴到新窗口的左上角 —— 这就是闪的根源。
            // 所以先把参数记下来，交给「等帧」去做：
            // 让没有 Logo 的两帧先推上去，再改矩形。
            // 见 DockRendering 里的 expandResizePending 分支。

            double expandWindowHeight =
                dockWindowHeight > CollapseSize
                ? dockWindowHeight
                : DockExpandedHeight;


            expandResizeToWidth =
                toWidth;


            expandResizeWindowHeight =
                expandWindowHeight;


            expandResizeFromWidth =
                fromWidth;


            expandResizeToHeight =
                toHeight;


            expandResizeFrameWait =
                2;


            expandResizePending =
                true;

        }


        // 收起时窗口保持展开尺寸不动
        // 圆在宽窗口里居中收缩，不会裁到任何一帧
        // 窗口矩形在 Collapse_Completed 里一次性收成正圆
        // 展开方向的窗口矩形则由 DockRendering 等 2 帧之后再改

    }


    // ==========================
    // DockBody 宽度动画：唯一入口
    // 收起方向由 SetCollapsed 直接调用
    // 展开方向等 2 帧之后由 DockRendering 调用
    // 两处跑的是同一段代码，只有调用时机不同
    // ==========================

    private void StartDockWidthAnimation(
        double fromWidth,
        double toWidth,
        double toHeight,
        bool collapsed)
    {

        // 先固定基值，动画结束后不跳变
        // 策略 2：动画里不写 From
        // 起点在这里写成基值（= 收缩状态下的真实坐标）
        // 动画首帧的起点 / 基准 / 画面三者完全一致

        DockBody.Width =
            fromWidth;


        DockBody.Height =
            toHeight;


        // 高度不做动画
        // 清掉可能残留的高度动画
        // 全程只有宽度在动：胶囊的底边与中心都不动

        DockBody.BeginAnimation(
            HeightProperty,
            null);


        // 缓动：CubicEase + EaseOut
        // 先快后慢地收，不闪烁、不跳动

        CubicEase ease =
            new CubicEase
            {

                EasingMode =
                    EasingMode.EaseOut

            };


        DoubleAnimation widthAnimation =
            new DoubleAnimation
            {

                // 策略 2：动画里不写 From
                // 起点已经写成基值，这里只给终点

                To =
                toWidth,


                Duration =
                TimeSpan.FromMilliseconds(
                    CollapseDuration),


                EasingFunction =
                ease,


                FillBehavior =
                FillBehavior.HoldEnd

            };


        // 圆角固定成高度的一半
        // 高度全程不变：展开 100、收起 100
        // 胶囊两端半圆半径 = 高度 / 2 = 50
        // 收成正圆时半径 = 宽度 / 2 = 高度 / 2 = 50
        // 两个状态的半径本来就是同一个值，全程恒定
        // 另外 WPF 的动画引擎只对 Double / Color / Point / Thickness
        // 这些内置类型做插值，圆角类型补出来的永远是 0
        // 所以圆角不做动画，这里直接把基值固定成正确值

        double toRadius =
            dockBodyHeight / 2;


        DockBody.CornerRadius =
            new CornerRadius(
                toRadius);


        widthAnimation.Completed +=
            (s,e) =>
                Collapse_Completed(
                    collapsed,
                    toWidth);


        DockBody.BeginAnimation(
            WidthProperty,
            widthAnimation);

    }




    // ==========================
    // 收起 / 展开动画结束
    // ==========================

    private void Collapse_Completed(
        bool collapsed,
        double targetWidth)
    {

        // 先固定基值再移除动画
        // 数值不会跳变

        DockBody.Width =
            collapsed
            ? CollapseSize
            : targetWidth;


        DockBody.BeginAnimation(
            WidthProperty,
            null);


        // 收起时高度仍然是胶囊高度
        // 圆形的直径就等于这个高度
        // 也就是只回落到宽度上，高度全程不变

        DockBody.Height =
            collapsed
            ? CollapseSize
            : dockBodyHeight;


        DockBody.BeginAnimation(
            HeightProperty,
            null);


        // 圆角收尾：固定成高度的一半
        // 展开、收起两个状态下半径都是 50，数值不跳变

        DockBody.CornerRadius =
            new CornerRadius(
                dockBodyHeight / 2);


        if(collapsed)
        {

            IconPanel.BeginAnimation(
                OpacityProperty,
                null);


            IconPanel.Opacity =
                0;


            IconPanel.Visibility =
                Visibility.Collapsed;


            CollapseArrow.BeginAnimation(
                OpacityProperty,
                null);


            CollapseArrow.Opacity =
                0.55;


            CollapseButton.Visibility =
                Visibility.Collapsed;


            // 收起状态下齿轮一起藏起来

            if(SettingsButton != null)
                SettingsButton.Visibility =
                    Visibility.Collapsed;


            // 顺便把材质设置面板收掉
            // 不让它孤零零留在已经收起的 Dock 上方

            CloseDockSettingsPanel(false);


            LaunchButton.BeginAnimation(
                OpacityProperty,
                null);


            LaunchButton.Opacity =
                0;


            LaunchButton.Visibility =
                Visibility.Collapsed;


            CollapseLogo.BeginAnimation(
                OpacityProperty,
                null);


            CollapseLogo.Opacity =
                1;


            // 窗口收成和正圆一样大（直径 = 胶囊高度）
            // 底边与中心仍在原处，视觉上就是原地向内收缩
            // 宽度与高度必须一次写入
            // 分两次改会出现中间态，画面会被系统拉伸

            FitWindowToDock(
                CollapseSize,
                CollapseSize);

        }
        else
        {

            // 展开收尾：把展开期间临时摘掉的渲染缓存装回去
            // 缓存内容会在下次用到时自动重画，装回来的过程看不出来

            DockBody.CacheMode =
                new BitmapCache();


            // 展开收尾：停掉首帧那个 0.05 秒的淡入 Storyboard
            // Logo 的可见性在展开状态下就是 Collapsed，
            // 下一轮收起时 SetCollapsed 会照旧把它淡入

            if(logoFirstFrameFadeIn != null)
            {

                logoFirstFrameFadeIn.Stop();

                logoFirstFrameFadeIn =
                    null;

            }


            IconPanel.BeginAnimation(
                OpacityProperty,
                null);


            IconPanel.Opacity =
                1;


            CollapseArrow.BeginAnimation(
                OpacityProperty,
                null);


            CollapseArrow.Opacity =
                0.55;


            CollapseLogo.BeginAnimation(
                OpacityProperty,
                null);


            CollapseLogo.Opacity =
                0;


            CollapseLogo.Visibility =
                Visibility.Collapsed;


            // Logo 已经完全藏住
            // 这时候停转并把角度归零，画面上看不出来
            // 展开状态不跑动画，空闲时也不占 CPU

            StopLogoSpin();

        }


        isCollapseAnimating = false;


        // 展开后按当前图标数量校正宽度
        // 收起期间加入的新图标也能补上

        if(!collapsed)
            AnimateDockWidth();

    }


    // ==========================
    // 展开用的「等帧」状态 + 首个淡入 Storyboard
    // ==========================
    // 展开先把 Logo 隐掉，再等 2 帧，然后才改窗口矩形。
    // 等出来的这 2 帧里，系统拿到的画面已经没有 Logo，
    // 之后改矩形时贴出来的旧位图自然也就没有 Logo。
    // 平时 expandResizePending 一直是 false，一点开销都不占。
    // ==========================

    private bool expandResizePending;

    private int expandResizeFrameWait;

    private double expandResizeToWidth;

    private double expandResizeWindowHeight;

    private double expandResizeFromWidth;

    private double expandResizeToHeight;

    private Storyboard? logoFirstFrameFadeIn;


    // ==========================
    // 展开前的「第一帧兜底」
    // 修复「双击展开时 Logo 在 Dock 左上方闪一下」
    // 只在 SetCollapsed(false) 的展开分支最前面调用一次
    //
    // 顺序（不可调换）：
    //   1) 先把挂起的排版走完，坐标读到真值
    //   2) Logo 立刻出局：Opacity 归 0 并摘掉动画，
    //      连 Visibility 一起收掉 ——
    //      从这一刻起，任何一帧里都不会有 Logo 像素
    //   3) 渲染层位移与边距压回收缩状态的基准 0（策略 2，
    //      动画里一个 From 都不写，起点永远是这里的基值）
    //   4) DockBody 的 BitmapCache 先摘掉（策略 3）：
    //      缓存位图正是「上一帧」里留着 Logo 像素的元凶，
    //      过渡期间不画缓存，系统贴旧帧也不会有 Logo
    //   5) 再用一个 0.05 秒的 Storyboard 把 Opacity 写成 1
    //      （策略 1，动画里不写 From）。
    //      此时 Logo 已被 Visibility 收掉，这一步只给它
    //      一个明确的起点，画面上不会露出来
    //
    // 全程只碰 Logo 自己的可见性与 DockBody 的渲染缓存
    // 不改尺寸、不动窗口矩形、不动任何动画的时长与缓动
    // ==========================

    private void PrepareCollapseLogoForExpand()
    {

        // 控件没取到就直接退出
        // 不抛异常，也不影响后面的展开流程

        if(CollapseLogo == null)
            return;


        // 1) 先把挂起的排版全部走完
        //    这时候读到的坐标才是收缩状态下算过的真值

        RootGrid.UpdateLayout();


        // 2) 上一轮可能还留着一个首帧淡入 Storyboard
        //    先停掉，免得同一个属性上叠两个时钟

        if(logoFirstFrameFadeIn != null)
        {

            logoFirstFrameFadeIn.Stop();

            logoFirstFrameFadeIn =
                null;

        }


        // Logo 从第一帧起就出局
        // 残留的 Opacity 动画必须先摘掉
        // 否则本地值写不进去（动画优先级高于本地值）

        CollapseLogo.BeginAnimation(
            OpacityProperty,
            null);


        CollapseLogo.Opacity =
            0;


        // 连可见性一起收掉：
        // 这样「窗口改尺寸那一帧」无论怎么合成都画不出 Logo
        // 展开结束时 Collapse_Completed 会把状态摆回去

        CollapseLogo.Visibility =
            Visibility.Collapsed;


        // 3) 把渲染层位移复位到收缩状态的起始坐标
        //    收缩状态下 Logo 在圆里居中，基准就是「零位移」
        //    先摘掉可能残留的位移动画，再写回 0
        //    展开期间这个位移只做锚定，不做入场动画
        //    所以全程都是 0，动画跑完也不会留下偏移

        if(CollapseLogoTranslate != null)
        {

            CollapseLogoTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                null);


            CollapseLogoTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                null);


            CollapseLogoTranslate.X =
                0;


            CollapseLogoTranslate.Y =
                0;

        }


        // 4) 边距同样压回收缩状态的基准 0
        //    只要不是 0 就说明有残值，必须清掉
        //    否则 Logo 会带着一个固定偏移进入展开

        Thickness logoMargin =
            CollapseLogo.Margin;


        if(logoMargin.Left != 0 ||
           logoMargin.Top != 0 ||
           logoMargin.Right != 0 ||
           logoMargin.Bottom != 0)
            CollapseLogo.Margin =
                new Thickness(0);


        // 4) 策略 3：DockBody 原本挂着 CacheMode=BitmapCache
        //    它会把整块 DockBody 先画成一张位图再贴出来。
        //    展开期间尺寸每帧都在变，缓存每帧都要重画，
        //    既拖慢「新一帧」的推送，
        //    又让「上一帧的缓存位图」留在屏幕上变成残影。
        //    过渡期间先摘掉，展开结束后在 Collapse_Completed 里装回

        DockBody.CacheMode =
            null;


        // 5) 复位后再同步排版一次
        //    让第 3、4 步写回去的坐标立刻生效

        RootGrid.UpdateLayout();


        // 6) 策略 1：0.05 秒的 Storyboard 淡入
        //    动画里不写 From（起点 = 上面压好的基值 0）
        //    Logo 此刻已被 Visibility 收掉，
        //    这一步只给它一个明确的起点，画面上看不到

        Storyboard fadeIn =
            new Storyboard();


        DoubleAnimation fadeInAnimation =
            new DoubleAnimation
            {

                To =
                1,


                Duration =
                TimeSpan.FromMilliseconds(50),


                FillBehavior =
                FillBehavior.HoldEnd

            };


        Storyboard.SetTarget(
            fadeInAnimation,
            CollapseLogo);


        Storyboard.SetTargetProperty(
            fadeInAnimation,
            new PropertyPath(
                OpacityProperty));


        fadeIn.Children.Add(
            fadeInAnimation);


        fadeIn.Begin();


        logoFirstFrameFadeIn =
            fadeIn;

    }


    // ==========================
    // 窗口矩形一次性收放
    // 宽度与高度必须同一次写入
    // 分两次改会出现窄高 / 宽扁的中间态
    // 中间帧被系统拉伸，看起来就是闪一下
    // 中心与底边保持不动
    // ==========================

    private void FitWindowToDock(
        double targetWidth,
        double targetHeight)
    {

        if(double.IsNaN(targetWidth) ||
           targetWidth <= 0)
            return;


        if(double.IsNaN(targetHeight) ||
           targetHeight <= 0)
            return;


        double left;

        double top;

        double width;

        double height;


        if(!TryGetWindowBounds(
            out left,
            out top,
            out width,
            out height))
            return;


        // 中心与底边在动画期间保持不动
        // 优先用动画开始时锁定的基准

        double center =
            double.IsNaN(collapseAnchorCenterX)
            ? left +
              width / 2
            : collapseAnchorCenterX;


        double bottom =
            double.IsNaN(collapseAnchorBottom)
            ? top +
              height
            : collapseAnchorBottom;


        double newLeft =
            center -
            targetWidth / 2;


        double newTop =
            bottom -
            targetHeight;


        double newWidth =
            targetWidth;


        double newHeight =
            targetHeight;


        // 吸附到像素网格
        // 宽高取偶数、中心取整
        // 居中的胶囊与 Logo 每帧
        // 都落在同一个整像素上
        // 不会出现半像素来回跳

        SnapToPixelGrid(
            ref newLeft,
            ref newTop,
            ref newWidth,
            ref newHeight);


        // 变化太小就不动
        // 避免每帧重复写窗口矩形

        if(Math.Abs(left - newLeft) < 0.5 &&
           Math.Abs(top - newTop) < 0.5 &&
           Math.Abs(width - newWidth) < 0.5 &&
           Math.Abs(height - newHeight) < 0.5)
            return;


        ApplyWindowBounds(
            newLeft,
            newTop,
            newWidth,
            newHeight);

    }


    // ==========================
    // 圆形里的 Logo 尺寸
    // 直径 × 固定比例，居中显示
    // 由 SetCollapsed 在每次收起前调用
    // 圆形变大变小，Logo 都跟着走
    // ==========================

    private void ApplyCollapseLogoSize(
        double circleSize)
    {

        if(double.IsNaN(circleSize) ||
           circleSize <= 0)
            return;


        double logoSize =
            Math.Round(
                circleSize *
                CollapseLogoRatio);


        if(logoSize < 16)
            logoSize = 16;


        CollapseLogo.Width =
            logoSize;


        CollapseLogo.Height =
            logoSize;

    }


    // ==========================
    // 收起圆里 Logo 的缓慢自转
    // 只驱动 RotateTransform.Angle 这一个属性
    // 纯渲染层变换：不触发布局、不重算尺寸
    // 所以每帧成本极低，转起来是丝滑的
    // 展开状态不转：不占空闲 CPU
    // ==========================

    private void StartLogoSpin()
    {

        // XAML 里的 CollapseLogoRotate
        // 万一没取到就直接退出，不抛异常

        if(CollapseLogoRotate == null)
            return;


        // 匀速：不给 EasingFunction
        // 帧与帧之间角度严格等分
        // 没有加速 / 减速，就没有顿挫感

        DoubleAnimation spin =
            new DoubleAnimation
            {

                From =
                0,


                To =
                360,


                Duration =
                TimeSpan.FromMilliseconds(
                    LogoSpinDuration),


                // 无限循环
                // 0° 与 360° 是同一张画面
                // 接缝处不会有跳变，也就不会闪

                RepeatBehavior =
                RepeatBehavior.Forever

            };


        // 动画挂在 Transform 上
        // 不挂 Border 本身
        // Border 的布局、位置、命中测试全程不受影响

        CollapseLogoRotate.BeginAnimation(
            RotateTransform.AngleProperty,
            spin);

    }


    // 展开时停转
    // 由 Collapse_Completed 在 Logo 彻底隐藏后调用
    // 此刻角度归零画面上看不见，不会闪

    private void StopLogoSpin()
    {

        if(CollapseLogoRotate == null)
            return;


        // 先摘掉动画，再把基值写回 0
        // 下一轮收起仍然从 0° 开始转

        CollapseLogoRotate.BeginAnimation(
            RotateTransform.AngleProperty,
            null);


        CollapseLogoRotate.Angle =
            0;

    }


    // ==========================
    // 启动板：九点点阵按钮
    // ==========================


    // 按下九点按钮
    // 拦截事件，避免触发 DockBody 的点击逻辑
    // 但手动接上全局拖动判定
    // 按住按钮同样可以拖动整个 Dock

    private void LaunchButton_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {

        e.Handled = true;


        DockDrag_Down(
            sender,
            e);

    }


    // 悬停高亮

    private void LaunchButton_MouseEnter(
        object sender,
        MouseEventArgs e)
    {

        FadeElement(
            LaunchDots,
            1,
            0,
            140,
            EasingMode.EaseOut);


        ScaleLaunchButton(
            1.08);

    }


    private void LaunchButton_MouseLeave(
        object sender,
        MouseEventArgs e)
    {

        FadeElement(
            LaunchDots,
            0.92,
            0,
            200,
            EasingMode.EaseOut);


        ScaleLaunchButton(
            1);

    }


    // 图标缩放
    // 与 Dock 图标悬停放大的手感保持一致

    private void ScaleLaunchButton(
        double scale)
    {

        DoubleAnimation animation =
            new DoubleAnimation
            {

                To = scale,


                Duration =
                TimeSpan.FromMilliseconds(
                    160),


                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseOut

                },


                FillBehavior =
                FillBehavior.HoldEnd

            };


        LaunchButtonScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            animation);


        LaunchButtonScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            animation);

    }


    // ==========================
    // Dock 材质设置：齿轮按钮
    // ==========================

    // 设置面板当前是否打开

    private bool isDockSettingsOpen = false;


    // 是否已跟随壁纸取色

    private bool isDockTinted = false;


    // 各玻璃层的基准不透明度
    // 必须与 XAML 里写的值一致
    // 透明度滑块拉到 1.00 时，材质和原来完全一样

    private const double DockGlassFrostBaseOpacity = 0.45;
    private const double DockGlassSweepBaseOpacity = 0.38;
    private const double DockGlassRippleBaseOpacity = 0.18;


    // 雾面层最大浓度（模糊拉到 100 时）

    private const double DockGlassFogMaxOpacity = 0.45;


    // 模糊同时把内部透射层加浓一点，更像真正的磨砂玻璃

    private const double DockGlassFrostBlurBoost = 0.17;


    // 模糊同时压掉液态折射波纹

    private const double DockGlassRippleBlurFade = 0.55;


    // 壁纸色调层浓度

    private const double DockGlassTintOpacity = 0.30;


    // 两个滑块的当前值

    private double dockOpacityValue = 1.0;
    private double dockBlurValue = 0.0;


    // 材质预设：0 = 经典亚克力，1 = 极简纯色，2 = 高光质感
    // 数值与 Settings.json 的 materialPreset 完全对应

    private enum DockMaterialPreset
    {
        Acrylic = 0,
        Minimal = 1,
        Glossy = 2
    }


    private DockMaterialPreset dockMaterialPreset =
        DockMaterialPreset.Acrylic;


    // ==========================
    // 展板材质：与 Dock 完全独立
    // ==========================
    // 各层的基准不透明度必须与 MainWindow.xaml 里写的值一致
    // 透明度拉到 1.00 时，展板材质和原来完全一样

    private const double LaunchBoardGlassFrostBaseOpacity = 0.45;

    // 雾面层最大浓度（模糊拉到 100 时）

    private const double LaunchBoardGlassFogMaxOpacity = 0.45;

    // 模糊同时把内部透射层加浓一点，更像真正的磨砂玻璃

    private const double LaunchBoardGlassFrostBlurBoost = 0.17;

    // 展板两个滑块的当前值（默认 0.8 / 30，和设置中心一致）

    private double launchBoardOpacityValue = 0.8;
    private double launchBoardBlurValue = 30.0;


    // ==========================
    // 展板自定义壁纸
    // ==========================
    // 单独存一份 JSON：%AppData%\GlassDock\LaunchBoardWallpaper.json
    // 原因：Settings.json 在整个项目里都按
    //       Dictionary<string, double> 读，
    //       往里塞字符串路径会让旧的数值解析直接失败。
    //       壁纸单独一份，原有材质 / 开关逻辑一行都不用改。
    // 内容：path（绝对路径）+ opacity（壁纸层不透明度）
    // ==========================

    private static readonly string LaunchBoardWallpaperStorePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GlassDock",
            "LaunchBoardWallpaper.json");


    // 当前壁纸：路径为空 = 没选壁纸

    private string launchBoardWallpaperPath = "";


    // 壁纸层自己的不透明度，默认 0.8
    // 与展板材质的 launchBoardOpacityValue 完全独立

    private double launchBoardWallpaperOpacityValue = 0.8;


    // 壁纸裁剪：缩放 + 平移
    // scale 范围 1.0 - 5.0（1.0 = 铺满，不露出空白）；
    // offsetX / offsetY 的单位是“预览框像素”，
    // 与 referenceWidth / referenceHeight 一起换算成
    // ImageBrush.Viewbox 的归一化源图矩形，
    // 这样展板尺寸变化时取景中心与缩放比例仍然一致。

    private double launchBoardWallpaperScaleValue = 1.0;

    private double launchBoardWallpaperOffsetXValue = 0.0;

    private double launchBoardWallpaperOffsetYValue = 0.0;

    private double launchBoardWallpaperCropReferenceWidth =
        LaunchBoardWallpaperReferenceWidth;

    private double launchBoardWallpaperCropReferenceHeight =
        LaunchBoardWallpaperReferenceHeight;

    // 设置中心预览框与展板必须同比例，拖动范围才能覆盖展板裁掉的全部边缘。
    // 展板固定 900 x 650，参考宽度取 300，高度按同一比例换算。

    private const double LaunchBoardWallpaperReferenceWidth = 300.0;

    private const double LaunchBoardWallpaperReferenceHeight =
        300.0 * 650.0 / 900.0;

    private const double LaunchBoardWallpaperCropScaleMin = 1.0;

    private const double LaunchBoardWallpaperCropScaleMax = 5.0;


    // ==========================
    // 两个滑块的持久化
    // 文件：%AppData%\GlassDock\Settings.json
    //       与展板 LaunchBoard.txt、IconMode.txt 同一个应用数据目录
    // 内容：opacity / blur / materialPreset
    //       （同一文件也由设置中心写入 locked / wallpaperTint / 展板两个键）
    // 读与写全部包在 try-catch 里：
    //       文件被手改坏、目录没权限、磁盘满
    //       一律只写调试输出，不弹窗、不崩溃
    //       最坏情况就是这次回到默认值
    // ==========================

    private static readonly string DockSettingsStorePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "GlassDock",
            "Settings.json");


    // 正在把文件里的值灌进滑块
    // 这期间 ValueChanged 不许回写文件
    // 否则启动时会拿着刚读到的值再写一遍（值相同还好，万一被夹过就白读了）

    private bool isLoadingDockSettings = false;


    // 拖动滑块时 ValueChanged 会连着触发几十次
    // 所以攒一下再落盘：最后一次变化之后约 0.35 秒写一次
    // 拖动过程一个字节都不写，手感不受影响

    private DispatcherTimer? dockSettingsSaveTimer = null;


    private const double DockSettingsSaveDelay = 350;



    // 齿轮按下：先交给全局拖动逻辑
    // 和收起按钮一致，按住齿轮也能拖动整个 Dock

    private void SettingsButton_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {

        e.Handled = true;

        DockDrag_Down(
            sender,
            e);

    }


    // 齿轮松开：只有判定为点击，才开关设置面板

    private void SettingsButton_Click(
        object sender,
        MouseButtonEventArgs e)
    {

        e.Handled = true;


        // 刚刚发生的是拖动
        // 本次松手不开关面板

        if(dockDragSuppressClick)
        {

            dockDragSuppressClick = false;

            return;

        }


        // 收起状态没有齿轮

        if(isCollapsed ||
           isCollapseAnimating)
            return;


        // 展板与设置面板不会同时出现

        if(isLaunchBoardOpen)
            CloseLaunchBoard(
                false);


        ToggleDockSettingsPanel();

    }


    // 齿轮悬停：提亮 + 轻微放大

    private void SettingsButton_MouseEnter(
        object sender,
        MouseEventArgs e)
    {

        FadeElement(SettingsGearGroup, 0.95, 0, 140, EasingMode.EaseOut);

        AnimateSettingsButtonScale(1.12);

    }


    private void SettingsButton_MouseLeave(
        object sender,
        MouseEventArgs e)
    {

        FadeElement(SettingsGearGroup, 0.55, 0, 200, EasingMode.EaseOut);

        AnimateSettingsButtonScale(1.0);

    }


    // 齿轮缩放：只动 ScaleTransform
    // 不触发任何布局，所以拖动 / 展开时都不掉帧

    private void AnimateSettingsButtonScale(
        double scale)
    {

        if(SettingsButtonScale == null)
            return;


        CubicEase ease =
            new CubicEase { EasingMode = EasingMode.EaseOut };

        DoubleAnimation scaleX =
            new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = ease
            };

        DoubleAnimation scaleY =
            new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = ease
            };


        SettingsButtonScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            scaleX);

        SettingsButtonScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scaleY);

    }


    // ==========================
    // Dock 材质设置：面板开关与动画
    // ==========================

    // 【本次改动】齿轮入口整体下线：
    // 设置面板不再是 Dock 上弹出的一块，而是托盘右键菜单里的
    // 「AeroDock设置中心」独立窗口（SettingsWindow）
    // XAML 里那个小齿轮元素保留（挤出动画还要读它的位置），
    // 但已经 Opacity=0 + IsHitTestVisible=False：看不见、也点不到
    // 这里再兜一层：万一还有别的入口绕进来，统一转去新窗口

    // 点击齿轮：开 / 关面板（旧面板已停用，见下方兜底）

    private void ToggleDockSettingsPanel()
    {

        if(DockSettingsPanel == null)
            return;


        if(isDockSettingsOpen)
            CloseDockSettingsPanel(true);
        else
            ShowDockSettingsCenter();

    }


    // 打开面板：从底边弹出 + 淡入

    private void OpenDockSettingsPanel()
    {

        if(DockSettingsPanel == null)
            return;


        if(isDockSettingsOpen)
            return;


        isDockSettingsOpen = true;


        DockSettingsPanel.Visibility =
            Visibility.Visible;


        // 打开面板时先把开关对准真实状态
        // 即使 isLocked 在别处被改过
        // 面板上显示的一定是当前生效的状态

        UpdateLockToggleState();


        // 开机自启动开关也同步一次
        // 就算有人手动改过注册表
        // 面板一打开看到的也是真实状态

        UpdateStartupToggleState();


        // 桌面图标处理模式也同步一次

        UpdateIconModeUi();


        AnimateDockSettingsPanel(
            true);

    }


    // 关闭面板
    // animated = false 表示立即关闭（例如收起 Dock 时）

    private void CloseDockSettingsPanel(
        bool animated)
    {

        if(DockSettingsPanel == null)
            return;


        isDockSettingsOpen = false;


        if(!animated)
        {

            DockSettingsPanel.BeginAnimation(
                UIElement.OpacityProperty,
                null);


            DockSettingsPanel.Visibility =
                Visibility.Collapsed;


            return;

        }


        AnimateDockSettingsPanel(
            false);

    }


    // 面板动画：淡入淡出 + 从底边弹出
    // 只动 Opacity 和 ScaleTransform
    // 全程不碰布局，所以没有掉帧和残影

    private void AnimateDockSettingsPanel(
        bool open)
    {

        if(DockSettingsPanel == null)
            return;


        DockSettingsPanel.BeginAnimation(
            UIElement.OpacityProperty,
            null);


        if(DockSettingsScale != null)
        {

            DockSettingsScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                null);

            DockSettingsScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                null);

        }


        double duration =
            open ? 240 : 150;


        CubicEase ease =
            new CubicEase
            {
                EasingMode = open ? EasingMode.EaseOut
                                  : EasingMode.EaseIn
            };


        DoubleAnimation fade =
            new DoubleAnimation
            {
                To = open ? 1.0 : 0.0,
                Duration = TimeSpan.FromMilliseconds(duration),
                EasingFunction = ease
            };


        if(!open)
            fade.Completed += DockSettingsFadeCompleted;


        DockSettingsPanel.BeginAnimation(
            UIElement.OpacityProperty,
            fade);


        if(DockSettingsScale != null)
        {

            DoubleAnimation scaleX =
                new DoubleAnimation
                {
                    To = open ? 1.0 : 0.86,
                    Duration = TimeSpan.FromMilliseconds(duration),
                    EasingFunction = ease
                };

            DoubleAnimation scaleY =
                new DoubleAnimation
                {
                    To = open ? 1.0 : 0.60,
                    Duration = TimeSpan.FromMilliseconds(duration),
                    EasingFunction = ease
                };


            DockSettingsScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                scaleX);

            DockSettingsScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                scaleY);

        }

    }


    // 淡出结束：真正把面板藏起来

    private void DockSettingsFadeCompleted(
        object? sender,
        EventArgs e)
    {

        if(isDockSettingsOpen)
            return;


        if(DockSettingsPanel == null)
            return;


        DockSettingsPanel.Visibility =
            Visibility.Collapsed;

    }


    // ==========================
    // Dock 材质设置：材质刷新与滑块
    // ==========================

    // 滑块 / 启动恢复：直接落到目标值（跟手、不闪）
    // 预设切换：调用 ApplyDockMaterialAnimated() 走 220ms 淡入淡出

    private void ApplyDockMaterial()
    {

        ApplyDockMaterialCore(
            false);

    }


    private void ApplyDockMaterialAnimated()
    {

        ApplyDockMaterialCore(
            true);

    }


    // 三档预设只决定“每一层最终的不透明度”
    // 经典亚克力完全沿用原来的公式，两个滑块手感不变

    private void ApplyDockMaterialCore(
        bool animate)
    {

        double alpha =
            dockOpacityValue;


        if(alpha < 0.05)
            alpha = 0.05;

        if(alpha > 1.0)
            alpha = 1.0;


        double fog =
            dockBlurValue / 100.0;


        if(fog < 0)
            fog = 0;

        if(fog > 1)
            fog = 1;


        // 默认值 = 经典亚克力：与改动前逐项一致

        double baseOpacity =
            alpha;

        double innerShadowOpacity =
            alpha;

        double edgeOpacity =
            alpha;

        double innerLineOpacity =
            alpha;

        double frostOpacity =
            (DockGlassFrostBaseOpacity +
             DockGlassFrostBlurBoost * fog) * alpha;

        double sweepOpacity =
            DockGlassSweepBaseOpacity * alpha;

        double rippleOpacity =
            DockGlassRippleBaseOpacity *
            (1 - DockGlassRippleBlurFade * fog) *
            alpha;

        double fogOpacity =
            DockGlassFogMaxOpacity * fog * alpha;

        double tintOpacity =
            isDockTinted
            ? DockGlassTintOpacity * alpha
            : 0;


        // 预设专属层

        double minimalFillOpacity =
            0;

        double minimalBorderOpacity =
            0;

        double highlightOpacity =
            0;


        if(dockMaterialPreset ==
           DockMaterialPreset.Minimal)
        {

            // 极简纯色：玻璃层全部熄灭 = 关闭亚克力 / 模糊 / 高光
            // 只留深灰纯色底 + 一像素白边，图标照常在最上层

            baseOpacity = 0;
            innerShadowOpacity = 0;
            edgeOpacity = 0;
            innerLineOpacity = 0;
            frostOpacity = 0;
            sweepOpacity = 0;
            rippleOpacity = 0;
            fogOpacity = 0;
            tintOpacity = 0;

            minimalFillOpacity =
                alpha;

            minimalBorderOpacity =
                alpha;

        }
        else if(dockMaterialPreset ==
                DockMaterialPreset.Glossy)
        {

            // 高光质感：保留轻度模糊
            // 模糊最多按 35% 参与，避免整块 Dock 发白发灰；
            // 原有高光 / 波纹压淡，让斜向反光层成为主角

            double lightFog =
                Math.Min(
                    fog,
                    0.35);

            baseOpacity =
                alpha * 0.88;

            innerShadowOpacity =
                alpha * 0.75;

            edgeOpacity =
                alpha * 0.65;

            innerLineOpacity =
                alpha * 0.55;

            frostOpacity =
                (DockGlassFrostBaseOpacity +
                 DockGlassFrostBlurBoost * lightFog) *
                alpha * 0.85;

            sweepOpacity =
                DockGlassSweepBaseOpacity * alpha * 0.35;

            rippleOpacity =
                DockGlassRippleBaseOpacity *
                (1 - DockGlassRippleBlurFade * lightFog) *
                alpha * 0.45;

            fogOpacity =
                DockGlassFogMaxOpacity * lightFog * alpha;

            tintOpacity =
                isDockTinted
                ? DockGlassTintOpacity * alpha * 0.7
                : 0;

            highlightOpacity =
                alpha;

        }


        // 只改各层 Opacity：
        // 拖滑块直接赋值，切预设走动画

        Action<UIElement, double> apply;


        if(animate)
            apply = AnimateLayerOpacity;
        else
            apply = SetLayerOpacity;


        apply(
            DockGlassBase,
            baseOpacity);

        apply(
            DockGlassInnerShadow,
            innerShadowOpacity);

        apply(
            DockGlassEdge,
            edgeOpacity);

        apply(
            DockGlassInnerLine,
            innerLineOpacity);

        apply(
            DockGlassFrost,
            frostOpacity);

        apply(
            DockGlassSweep,
            sweepOpacity);

        apply(
            DockGlassRipple,
            rippleOpacity);

        apply(
            DockGlassFog,
            fogOpacity);

        apply(
            DockGlassTint,
            tintOpacity);

        apply(
            DockMinimalFill,
            minimalFillOpacity);

        apply(
            DockMinimalBorder,
            minimalBorderOpacity);

        apply(
            DockGlassHighlight,
            highlightOpacity);

    }


    // ==========================
    // 材质预设：切换与归一化
    // ==========================
    // 只改玻璃各层 Opacity，不动布局 / 图标 / 拖拽 / 开合动画

    private void ApplyMaterialPreset(
        DockMaterialPreset preset,
        bool animate)
    {

        dockMaterialPreset =
            preset;


        // 经典亚克力：复用现有的 AcrylicHelper
        // 分层窗口上 Helper 会安全跳过系统背板，
        // 外观仍由 XAML 玻璃层绘制（不会刷黑 / 不会崩溃）

        if(preset ==
           DockMaterialPreset.Acrylic)
        {

            try
            {

                AcrylicHelper.EnableMica(
                    this);

            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "应用经典亚克力失败: "
                    + ex.Message);

            }

        }


        if(animate)
            ApplyDockMaterialAnimated();
        else
            ApplyDockMaterial();

    }


    // 0 / 1 / 2 之外的值一律夹回合法范围
    // 手改配置文件塞进 NaN / 99 也不会把界面搞坏

    private static DockMaterialPreset NormalizeMaterialPreset(
        double value)
    {

        if(double.IsNaN(value) ||
           double.IsInfinity(value))
            return DockMaterialPreset.Acrylic;


        int index =
            (int)Math.Round(
                value);


        if(index < 0)
            index = 0;

        if(index > 2)
            index = 2;


        return
            (DockMaterialPreset)index;

    }


    // ==========================
    // 展板材质：透明度 / 模糊度
    // ==========================
    // 与 ApplyDockMaterial 同一套手法：
    // 只改各玻璃层的 Opacity，不动布局、不动图标、不动开合动画
    // 最外层 LaunchBoard.Opacity 是转场动画用的，这里绝对不碰

    private void ApplyLaunchBoardMaterial()
    {

        double alpha =
            launchBoardOpacityValue;


        if(alpha < 0.05)
            alpha = 0.05;

        if(alpha > 1.0)
            alpha = 1.0;


        double fog =
            launchBoardBlurValue / 100.0;


        if(fog < 0)
            fog = 0;

        if(fog > 1)
            fog = 1;


        // 背景主体 / 内部阴影 / 菲涅尔边缘 / 内部高光线

        SetLayerOpacity(
            LaunchBoardGlassBase,
            alpha);

        SetLayerOpacity(
            LaunchBoardGlassInnerShadow,
            alpha);

        SetLayerOpacity(
            LaunchBoardGlassEdge,
            alpha);

        SetLayerOpacity(
            LaunchBoardGlassInnerLine,
            alpha);


        // 内部玻璃透射层：越模糊越浓，和 Dock 的磨砂手感一致

        SetLayerOpacity(
            LaunchBoardGlassFrost,
            (LaunchBoardGlassFrostBaseOpacity +
             LaunchBoardGlassFrostBlurBoost * fog) * alpha);


        // 雾面层：模糊滑块的主输出

        SetLayerOpacity(
            LaunchBoardGlassFog,
            LaunchBoardGlassFogMaxOpacity * fog * alpha);

    }


    // 统一的空引用保护
    // 预设切换会留下 HoldEnd 动画，先摘掉再赋值
    // 否则动画值会盖住这里的直接赋值（滑块拖不动）

    private static void SetLayerOpacity(
        UIElement layer,
        double opacity)
    {

        if(layer == null)
            return;


        if(opacity < 0)
            opacity = 0;


        layer.BeginAnimation(
            UIElement.OpacityProperty,
            null);


        layer.Opacity =
            opacity;

    }


    // 预设切换专用：220ms 缓出，避免生硬跳变

    private static void AnimateLayerOpacity(
        UIElement layer,
        double opacity)
    {

        if(layer == null)
            return;


        if(opacity < 0)
            opacity = 0;


        if(double.IsNaN(opacity) ||
           double.IsInfinity(opacity))
            return;


        try
        {

            layer.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation
                {

                    // 从当前实际值起步：
                    // 连续快速切换预设时不会跳回上一次的直接赋值

                    From = layer.Opacity,

                    To = opacity,

                    Duration =
                        TimeSpan.FromMilliseconds(220),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
                        },

                    FillBehavior =
                        FillBehavior.HoldEnd

                });

        }
        catch
        {

            // 动画异常兜底：先摘掉旧动画，再直接落到目标值

            SetLayerOpacity(
                layer,
                opacity);

        }

    }


    // 透明度滑块：0.1 - 1.0

    private void DockOpacity_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {

        dockOpacityValue =
            e.NewValue;


        if(DockOpacityText != null)
            DockOpacityText.Text =
                e.NewValue.ToString("0.00");


        ApplyDockMaterial();


        // 存起来，下次启动还是这个透明度
        // 读文件期间不回写（isLoadingDockSettings 里挡掉了）

        ScheduleDockSettingsSave();

    }


    // 模糊滑块：0 - 100

    private void DockBlur_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {

        dockBlurValue =
            e.NewValue;


        if(DockBlurText != null)
            DockBlurText.Text =
                ((int)Math.Round(e.NewValue)).ToString();


        ApplyDockMaterial();


        // 存起来，下次启动还是这个模糊度
        // 读文件期间不回写（isLoadingDockSettings 里挡掉了）

        ScheduleDockSettingsSave();

    }


    // ==========================
    // 设置持久化：排队保存
    // ==========================
    // 滑块还在拖的时候只排队，不写盘
    // 350ms 内又变了就重新排，等于"停下来才存"

    private void ScheduleDockSettingsSave()
    {

        // 正在读文件：这次变化是代码自己灌进去的，不存

        if(isLoadingDockSettings)
            return;


        try
        {

            if(dockSettingsSaveTimer == null)
            {

                dockSettingsSaveTimer =
                    new DispatcherTimer();


                dockSettingsSaveTimer.Interval =
                    TimeSpan.FromMilliseconds(
                        DockSettingsSaveDelay);


                dockSettingsSaveTimer.Tick +=
                    DockSettingsSaveTimer_Tick;

            }


            dockSettingsSaveTimer.Stop();

            dockSettingsSaveTimer.Start();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "排队保存 Dock 材质设置失败: "
                +
                ex.Message);

        }

    }


    private void DockSettingsSaveTimer_Tick(
        object? sender,
        EventArgs e)
    {

        if(dockSettingsSaveTimer != null)
            dockSettingsSaveTimer.Stop();


        SaveDockSettings();

    }


    // ==========================
    // 设置持久化：落盘
    // ==========================

    // ==========================
    // Settings.json 现在有两个写入口：
    //   Dock 自己的两个滑块      → opacity / blur / materialPreset
    //   托盘「AeroDock设置中心」  → opacity / blur / materialPreset / LaunchBoardOpacity / LaunchBoardBlur
    //                              / locked / wallpaperTint / closeBoardOnOutsideClick
    // 所以落盘前先读回原文件，只改自己负责的键
    // 设置中心存的其它键原样保留，绝不被这里覆盖掉
    // ==========================


    private static Dictionary<string,double> ReadDockSettingsRaw()
    {

        Dictionary<string,double> data =
            new Dictionary<string,double>();


        try
        {

            if(!File.Exists(DockSettingsStorePath))
                return data;


            string json =
                File.ReadAllText(
                    DockSettingsStorePath);


            if(string.IsNullOrWhiteSpace(json))
                return data;


            Dictionary<string,double>? loaded =
                System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string,double>>(
                        json);


            if(loaded == null)
                return data;


            return loaded;

        }
        catch
        {

            // 文件坏了 / 被锁住 / 不是 JSON：
            // 当成没存过，回空表
            // （后面的整体写入会把文件修回一份合法 JSON）

            return data;

        }

    }


    private void SaveDockSettings()
    {

        try
        {

            string? dir =
                Path.GetDirectoryName(
                    DockSettingsStorePath);


            if(!string.IsNullOrWhiteSpace(dir) &&
               !Directory.Exists(dir))
            {

                Directory.CreateDirectory(
                    dir);

            }


            Dictionary<string,double> data =
                new Dictionary<string,double>();


            // 【本次改动】先把文件里已有的键读回来
            // 设置中心（托盘菜单）把 locked / wallpaperTint 也写在同一个文件
            // 老写法是 new 一个空字典再整体覆盖：那两个键会被顺手抹掉
            // 这里改成读-改-写，下面两行照样覆盖 opacity / blur

            foreach(KeyValuePair<string,double> kv in ReadDockSettingsRaw())
                data[kv.Key] = kv.Value;


            data["opacity"] =
                dockOpacityValue;


            data["blur"] =
                dockBlurValue;


            // 材质预设：0 / 1 / 2
            // 与设置中心写的是同一个键，谁后落盘都是同一个当前值

            data["materialPreset"] =
                (double)dockMaterialPreset;


            string json =
                System.Text.Json.JsonSerializer
                    .Serialize<Dictionary<string,double>>(
                        data);


            File.WriteAllText(
                DockSettingsStorePath,
                json);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "保存 Dock 材质设置失败: "
                +
                ex.Message);

        }

    }


    // ==========================
    // 设置持久化：读取并应用
    // ==========================
    // Window_Loaded 里调用一次
    // 先读文件 → 夹到滑块的合法区间 → 灌进滑块 → 刷一遍材质

    private void LoadDockSettings()
    {

        // 默认值 = XAML 里的设计值

        double opacity =
            dockOpacityValue;


        double blur =
            dockBlurValue;


        // 展板材质：与 Dock 完全独立
        // 键缺失时保持默认 0.8 / 30

        double launchBoardOpacity =
            launchBoardOpacityValue;


        double launchBoardBlur =
            launchBoardBlurValue;


        // 材质预设：默认沿用字段当前值（初始为经典亚克力）

        double materialPreset =
            (double)dockMaterialPreset;


        try
        {

            if(File.Exists(DockSettingsStorePath))
            {

                string json =
                    File.ReadAllText(
                        DockSettingsStorePath);


                if(!string.IsNullOrWhiteSpace(json))
                {

                    Dictionary<string,double>? data =
                        System.Text.Json.JsonSerializer
                            .Deserialize<Dictionary<string,double>>(
                                json);


                    if(data != null)
                    {

                        double value;


                        if(data.TryGetValue(
                            "opacity",
                            out value))
                        {

                            opacity =
                                value;

                        }


                        if(data.TryGetValue(
                            "blur",
                            out value))
                        {

                            blur =
                                value;

                        }


                        if(data.TryGetValue(
                            "LaunchBoardOpacity",
                            out value))
                        {

                            launchBoardOpacity =
                                value;

                        }


                        if(data.TryGetValue(
                            "LaunchBoardBlur",
                            out value))
                        {

                            launchBoardBlur =
                                value;

                        }


                        // 材质预设：0 / 1 / 2
                        // 旧存档没有这个键时保持默认经典亚克力

                        if(data.TryGetValue(
                            "materialPreset",
                            out value))
                        {

                            materialPreset =
                                value;

                        }

                    }

                }

            }

        }
        catch(Exception ex)
        {

            // 文件坏了 / 被锁住 / 不是 JSON：
            // 直接当没存过，回默认值，绝不弹窗

            Debug.WriteLine(
                "读取 Dock 材质设置失败: "
                +
                ex.Message);

        }


        // 先恢复预设，再刷材质：
        // ApplyDockMaterial 会按当前预设计算每一层
        // 启动阶段直接落到目标值，不做动画

        dockMaterialPreset =
            NormalizeMaterialPreset(
                materialPreset);


        ApplyDockSettings(
            opacity,
            blur);


        // 经典亚克力再复用一次 AcrylicHelper
        // 极简 / 高光不调用 = 关闭系统背板

        if(dockMaterialPreset ==
           DockMaterialPreset.Acrylic)
        {

            ApplyMaterialPreset(
                dockMaterialPreset,
                false);

        }


        // 展板材质单独应用一次
        // 与 Dock 的两个滑块互不影响，重启后照样生效

        SettingsCenter_ApplyLaunchBoardMaterial(
            launchBoardOpacity,
            launchBoardBlur);

    }


    private void ApplyDockSettings(
        double opacity,
        double blur)
    {

        try
        {

            // 区间以滑块自己的 Minimum / Maximum 为准
            // 以后改区间只用改 XAML 一处

            opacity =
                ClampDockSetting(
                    opacity,
                    DockOpacitySlider != null
                    ? DockOpacitySlider.Minimum
                    : 0.1,
                    DockOpacitySlider != null
                    ? DockOpacitySlider.Maximum
                    : 1.0,
                    1.0);


            blur =
                ClampDockSetting(
                    blur,
                    DockBlurSlider != null
                    ? DockBlurSlider.Minimum
                    : 0.0,
                    DockBlurSlider != null
                    ? DockBlurSlider.Maximum
                    : 100.0,
                    0.0);


            // 灌进滑块期间不许回写文件

            isLoadingDockSettings =
                true;


            if(DockOpacitySlider != null)
                DockOpacitySlider.Value =
                    opacity;


            if(DockBlurSlider != null)
                DockBlurSlider.Value =
                    blur;


            // 值一模一样时 ValueChanged 不会触发
            // 所以字段和旁边的数字标签再兜一次底

            dockOpacityValue =
                opacity;


            dockBlurValue =
                blur;


            if(DockOpacityText != null)
                DockOpacityText.Text =
                    opacity.ToString("0.00");


            if(DockBlurText != null)
                DockBlurText.Text =
                    ((int)Math.Round(blur)).ToString();


            // 真正把材质刷一遍
            // 只改渲染层不透明度，不动布局与动画

            ApplyDockMaterial();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "应用 Dock 材质设置失败: "
                +
                ex.Message);

        }
        finally
        {

            isLoadingDockSettings =
                false;

        }

    }


    // 手改过的配置文件可能塞进来 NaN / 1000 之类
    // 一律夹回合法区间，不给界面留坑

    private static double ClampDockSetting(
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


    // ==========================
    // Dock 材质设置：壁纸取色
    // ==========================

    // 点一下取色，再点一下取消
    // 只给色调层换颜色，不动 Dock 的任何结构

    private void WallpaperTintButton_Click(
        object sender,
        MouseButtonEventArgs e)
    {

        e.Handled = true;


        if(isDockTinted)
        {

            isDockTinted = false;


            if(WallpaperTintText != null)
                WallpaperTintText.Text = "壁纸取色";


            ApplyDockMaterial();


            return;

        }


        Color? color =
            TryReadWallpaperTintColor();


        if(color == null)
        {

            if(WallpaperTintText != null)
                WallpaperTintText.Text = "取色失败";


            return;

        }


        isDockTinted = true;


        if(DockGlassTintBrush != null)
            DockGlassTintBrush.Color = color.Value;


        if(WallpaperTintText != null)
            WallpaperTintText.Text = "已跟随壁纸";


        ApplyDockMaterial();

    }


    private void WallpaperTintButton_MouseEnter(
        object sender,
        MouseEventArgs e)
    {

        AnimateTintButtonScale(1.06);

    }


    private void WallpaperTintButton_MouseLeave(
        object sender,
        MouseEventArgs e)
    {

        AnimateTintButtonScale(1.0);

    }


    // 取色按钮缩放

    private void AnimateTintButtonScale(
        double scale)
    {

        if(WallpaperTintButtonScale == null)
            return;


        CubicEase ease =
            new CubicEase { EasingMode = EasingMode.EaseOut };


        WallpaperTintButtonScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = ease
            });

        WallpaperTintButtonScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = ease
            });

    }


    // ==========================
    // Dock 材质设置：壁纸颜色提取
    // ==========================

    // 读取壁纸主色
    // 1) 有壁纸文件：缩到 64x64 采样，统计主色
    // 2) 纯色桌面：读注册表里的桌面背景色
    // 全程 try / catch，取不到就返回 null（面板保持原样）

    private Color? TryReadWallpaperTintColor()
    {

        // 1) 壁纸文件

        try
        {

            string? wallpaperPath =
                null;


            using(Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Desktop"))
            {

                if(key != null)
                    wallpaperPath =
                        key.GetValue("Wallpaper") as string;

            }


            if(!string.IsNullOrEmpty(wallpaperPath) &&
               File.Exists(wallpaperPath))
            {

                using(System.Drawing.Bitmap bitmap =
                    new System.Drawing.Bitmap(wallpaperPath))
                {

                    return ToGlassTint(
                        PickDominantColor(bitmap));

                }

            }

        }
        catch
        {

        }


        // 2) 纯色桌面

        try
        {

            using(Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Colors"))
            {

                if(key == null)
                    return null;


                string? solid =
                    key.GetValue("Background") as string;


                if(string.IsNullOrEmpty(solid))
                    return null;


                string[] parts =
                    solid.Split(' ');


                if(parts.Length < 3)
                    return null;


                byte r;
                byte g;
                byte b;


                if(byte.TryParse(parts[0], out r) &&
                   byte.TryParse(parts[1], out g) &&
                   byte.TryParse(parts[2], out b))
                {

                    return ToGlassTint(
                        System.Drawing.Color.FromArgb(
                            r,
                            g,
                            b));

                }

            }

        }
        catch
        {

        }


        return null;

    }


    // 缩略采样 + 分桶统计
    // 跳过太暗、太亮和接近灰色的像素
    // （桌面图标、任务栏、窗口都会干扰主色）

    private static System.Drawing.Color PickDominantColor(
        System.Drawing.Bitmap source)
    {

        const int SampleSize = 64;


        int[] count = new int[64];
        int[] sumR = new int[64];
        int[] sumG = new int[64];
        int[] sumB = new int[64];


        using(System.Drawing.Bitmap small =
            new System.Drawing.Bitmap(
                source,
                SampleSize,
                SampleSize))
        {

            for(int y = 0; y < SampleSize; y++)
            {

                for(int x = 0; x < SampleSize; x++)
                {

                    System.Drawing.Color c =
                        small.GetPixel(x, y);


                    // 太暗或太亮

                    int luma =
                        (c.R * 299 + c.G * 587 + c.B * 114) / 1000;


                    if(luma < 24 ||
                       luma > 238)
                        continue;


                    // 接近灰色

                    int max =
                        Math.Max(c.R, Math.Max(c.G, c.B));

                    int min =
                        Math.Min(c.R, Math.Min(c.G, c.B));


                    if(max - min < 14)
                        continue;


                    // 每通道 2 bit，共 64 个桶

                    int index =
                        ((c.R >> 6) << 4) |
                        ((c.G >> 6) << 2) |
                        (c.B >> 6);


                    count[index]++;

                    sumR[index] += c.R;
                    sumG[index] += c.G;
                    sumB[index] += c.B;

                }

            }

        }


        int bestIndex = -1;
        int bestCount = 0;


        for(int i = 0; i < count.Length; i++)
        {

            if(count[i] > bestCount)
            {

                bestCount = count[i];

                bestIndex = i;

            }

        }


        // 一个有效像素都没有：退回中性灰

        if(bestIndex < 0)
            return System.Drawing.Color.FromArgb(96, 96, 96);


        return System.Drawing.Color.FromArgb(
            sumR[bestIndex] / bestCount,
            sumG[bestIndex] / bestCount,
            sumB[bestIndex] / bestCount);

    }


    // 取到的颜色直接当玻璃色调会太艳 / 太暗
    // 先往白色方向兑一点，做成“牛奶玻璃”的色调
    // 这里顺便调用 WallpaperAnalyzer 判断壁纸明暗：
    // 深色壁纸少兑白，浅色壁纸多兑白

    private Color ToGlassTint(
        System.Drawing.Color color)
    {

        double mix =
            0.40;


        try
        {

            WallpaperAnalyzer analyzer =
                new WallpaperAnalyzer();


            if(analyzer.IsDarkBackground())
                mix = 0.24;

        }
        catch
        {

        }


        byte r =
            (byte)Math.Round(color.R * (1 - mix) + 255 * mix);

        byte g =
            (byte)Math.Round(color.G * (1 - mix) + 255 * mix);

        byte b =
            (byte)Math.Round(color.B * (1 - mix) + 255 * mix);


        return Color.FromRgb(r, g, b);

    }

    // 点击九点按钮：开关展板

    private void LaunchButton_Click(
        object sender,
        MouseButtonEventArgs e)
    {

        e.Handled = true;


        // 刚刚发生的是拖动
        // 本次松手不切换展板

        if(dockDragSuppressClick)
        {

            dockDragSuppressClick = false;


            return;

        }


        // 收起状态没有按钮
        // 这里再兜底一次

        if(isCollapsed ||
           isCollapseAnimating)
            return;


        if(isLaunchBoardOpen)
            CloseLaunchBoard(
                false);

        else
            OpenLaunchBoard();

    }


    // ==========================
    // 点击空白处关闭展板：读取最新配置
    // 每次点击都重新读 Settings.json，设置中心切换后无需重启即可生效
    // 键缺失 / 文件损坏 → 默认 true，保持原有行为
    // ==========================

    private bool IsCloseBoardOnOutsideClickEnabled()
    {

        const bool defaultValue =
            true;


        try
        {

            Dictionary<string,double> data =
                ReadDockSettingsRaw();


            double value;


            if(data.TryGetValue(
                "closeBoardOnOutsideClick",
                out value))
            {

                return value >= 0.5;

            }

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "读取“点击空白处关闭展板”设置失败: "
                +
                ex.Message);

        }


        return defaultValue;

    }


    // ==========================
    // 点击外部关闭（本次新增）
    // 只多加一个监听：窗口根级别 PreviewMouseLeftButtonDown
    // 九点阵原有的点击 / 开关逻辑一行没动
    // 展板打开时点“展板外的空白处” → 复用现有 CloseLaunchBoard(false)
    // 走的就是同一套平滑淡出 / 缩回动画，不是生硬隐藏
    // ==========================

    private void Window_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {

        // 设置中心关掉了“点击空白处关闭展板”：直接放行
        // 只在外层加这一道闸门，下面原有的判断一行没动

        if(!IsCloseBoardOnOutsideClickEnabled())
            return;


        // ==========================
        // 右键拦截（本次修复，第一道闸）
        // 右键绝对不算"点展板外面"
        // 它的作用只有一个：弹右键菜单 / "移除"确认框
        // 所以只要按下的不是左键，立刻 return
        // 只有左键才继续往下做"点在哪"的判断
        // ==========================

        if(e.ChangedButton == MouseButton.Right ||
           e.ChangedButton == MouseButton.Middle)
            return;


        // ==========================
        // "移除"确认框正在弹（本次修复，第二道闸）
        // 弹框期间窗口是失焦状态
        // 这时候任何一次按键都不该被当成"点外面"
        // ==========================

        if(isRightClickMenuOpen)
            return;


        // 展板没打开：什么都不做
        // （点哪儿都没反应，也不会报错）

        if(!isLaunchBoardOpen)
            return;


        // 九点阵悬停会放大到 1.08
        // 边缘多出来的那几像素也要算“点在按钮上”
        // 否则点按钮边缘会被当成点空白：先关板，松手又被按钮重新打开

        const double launchButtonHitTolerance = 6;


        // 判断必须用鼠标在 RootGrid 里的真实坐标
        // e.OriginalSource 不可靠：展板的玻璃层是 IsHitTestVisible=False，
        // 点上去根本命中不到展板本身

        Point inRoot =
            Mouse.GetPosition(
                RootGrid);


        // 1. 点在展板边界内部 → 立刻 return
        //    展板自己的拖动 / 翻页 / 图标交互照旧

        if(IsPointInElement(
            inRoot,
            LaunchBoard,
            0))
            return;


        // 2. 点在九点阵按钮上 → 立刻 return
        //    交给它原本的 MouseLeftButtonUp 逻辑自己开关展板

        if(IsPointInElement(
            inRoot,
            LaunchButton,
            launchButtonHitTolerance))
            return;


        // 3. 点在 Dock 上 → 立刻 return
        //    Dock 里全是原有交互（图标点击 / 齿轮 / 收起箭头 / 按住拖动窗口）
        //    必须让路，否则会和 SetCollapsed 里的“立即收起展板”抢同一块窗口尺寸

        if(IsPointInElement(
            inRoot,
            DockBody,
            DockDropTolerance))
            return;


        // 4. 确认是展板外的空白处 → 才关
        //    immediate = false：沿用原来的淡出 + 缩回动画

        CloseLaunchBoard(
            false);


        // 注意：这里不设置 e.Handled
        // 不拦事件，Dock 的拖动 / 图标点击照常收到

    }


    // ==========================
    // 点击外部关闭（第二个监听）
    // 桌面在窗口矩形之外，鼠标事件永远收不到
    // 所以只能靠"窗口失去焦点"来触发检查：
    // 展板打开着，用户跑到窗口外面点任何地方
    // （桌面 / 别的程序 / 托盘图标）窗口都会先失焦
    // 【本次修复】失焦只是"触发点"，不是"结论"
    // 真正决定关不关的是下面四道闸：
    //   1. 正在退出软件 → 不关
    //   2. 右键菜单 / "移除"确认框正在弹 → 不关
    //      （弹框自己就会抢焦点，不能算"点外面"）
    //   3. 展板没打开 / 正在收起 / 手势进行中 → 不关
    //   4. 鼠标坐标还在窗口矩形里 → 不关
    //      （只有鼠标确实在窗口之外，才是真的点了别处）
    // 过了四道闸才调用现成的 CloseLaunchBoard(false)
    // 走的就是同一套平滑淡出 / 缩回动画
    // 九点阵开关、拖拽、收缩成圆、托盘逻辑一行都没动
    // ==========================

    private void Window_Deactivated(
        object sender,
        EventArgs e)
    {

        // 设置中心关掉了“点击空白处关闭展板”：点窗口外面也不关
        // 只在外层加这一道闸门，下面原有的判断一行没动

        if(!IsCloseBoardOnOutsideClickEnabled())
            return;


        // 1. 正在退出软件：什么都不做
        //    退出流程有自己的节奏，不在这里插一脚

        if(isExiting)
            return;


        // ---------------------------------------------
        // 【本次修复，第三道闸】右键菜单 / "移除"确认框正在弹
        // 弹框 / 弹菜单本身就会抢走窗口焦点
        // 窗口必然失焦 → Deactivated 必然被触发
        // 这跟"用户跑去点窗口外面"完全是两回事
        // 有标记在，这里什么都不做，绝不关展板
        // ---------------------------------------------

        if(isRightClickMenuOpen)
            return;


        // 2. 展板没打开：什么都不做
        //    点托盘图标 / 点 Dock 里的收缩箭头 / 切到别的程序
        //    走到这里就返回了，不会报错，也不会有任何副作用

        if(!isLaunchBoardOpen)
            return;


        // 3. 收起 / 收起动画期间：什么都不做
        //    收起箭头的路已经由 SetCollapsed 立即收起展板了
        //    这里不去抢同一块窗口尺寸

        if(isCollapsed ||
           isCollapseAnimating)
            return;


        // 4. 手势进行中：什么都不做
        //    按住拖动 Dock、或 Dock ↔ 展板内部拖拽图标时
        //    窗口本来就在自己收尾，让路给原有逻辑

        if(dockDragArmed ||
           isDragging ||
           appDragActive)
            return;


        // ---------------------------------------------
        // 【本次修复，第四道闸】鼠标坐标判断
        // 不再只凭"失焦"就关展板
        // 真正的"点窗口外面"：鼠标此刻一定不在窗口矩形里
        // 鼠标还停在窗口矩形里
        //   → 说明这次失焦是窗口自己的弹出物抢走了焦点
        //     （右键菜单 / 确认框 / 系统气泡）
        //   → 不关
        // ---------------------------------------------

        if(IsCursorInsideWindowRect())
            return;


        // 5. 确认是"跑去点窗口外面了" → 才关
        //    immediate = false：沿用原有的淡出 + 缩回动画

        CloseLaunchBoard(
            false);

    }


    // ==========================
    // 鼠标坐标判断（本次修复新增）
    // 用途：判断"这一次失焦"是不是用户真的点到了窗口外面
    // 用鼠标相对本窗口的坐标比对窗口矩形
    //   坐标落在 [0, 0] ~ [窗口宽, 窗口高] 之内 → 在窗口里
    //   鼠标跑到窗口外时，坐标会变成负数或大于窗口宽高
    // 窗口不在前台时这个坐标照样有效：
    //   WPF 直接拿系统光标的屏幕坐标再换算过来
    //   （不是只在窗口内有鼠标事件才更新）
    // 坐标读不出来（NaN）时按"在窗口里"处理：
    //   宁可这次不关，也绝不误关展板
    // ==========================

    private bool IsCursorInsideWindowRect()
    {

        Point cursor =
            Mouse.GetPosition(
                this);


        if(double.IsNaN(cursor.X) ||
           double.IsNaN(cursor.Y))
            return true;


        if(ActualWidth <= 0 ||
           ActualHeight <= 0)
            return true;


        return cursor.X >= 0 &&
               cursor.Y >= 0 &&
               cursor.X <= ActualWidth &&
               cursor.Y <= ActualHeight;

    }


    // ==========================
    // 启动板：打开 / 关闭
    // 一条 Storyboard 同时驱动
    // 宽度 / 高度 / 透明度，BackEase 回弹
    // 窗口向上长高，底边固定
    // Dock 在桌面上的位置不会变
    // ==========================


    private void OpenLaunchBoard()
    {

        // 展板与材质设置面板不会同时出现

        if(isDockSettingsOpen)
            CloseDockSettingsPanel(false);


        if(isLaunchBoardOpen)
            return;


        isLaunchBoardOpen = true;


        // Dock 与展板同时存在
        // 展板只是浮在 Dock 上方的收纳盒
        // 开板前先确保 Dock 处于常显状态，不会被顶掉

        ShowDock();


        // 1. 算尺寸：目标 900 x 650
        //    屏幕放不下就按工作区比例缩

        CalculateLaunchBoardSize(
            out launchBoardWidth,
            out launchBoardHeight);


        // 2. 记下打开前的窗口高度
        //    关闭展板时按此还原
        //    注意"中途反向"：收起还没收完又打开时，
        //    此刻窗口还停在"展板档"的高度上，记下来会让窗口以后再也收不回原样
        //    → 那种情况保留上一次记好的（真正的"打开前高度"）

        if(!isLaunchBoardTransitionRunning)
            launchBoardBaseHeight =
                CaptureWindowHeight();


        // 3. 窗口一次性加高加宽
        //    动画期间窗口不再变化，避免逐帧闪烁
        //    多出来的是透明区域，肉眼看不到窗口变大

        GrowWindowForLaunchBoard(
            launchBoardWidth,
            launchBoardHeight);


        // 4. 先把上一轮的实时值读出来
        //    中途反向（收起没跑完又点了一次）要接着当前位置走，不能跳
        //    必须在 Stop 之前读：Stop 之后属性就回到基值了

        CaptureLaunchBoardTransitionValues();


        // 5. 停掉上一轮残留动画
        //    否则两条动画会抢同一个属性

        StopLaunchBoardStoryboard();


        // 6. 基值先落到最终尺寸
        //    动画结束（HoldEnd）后就停在这里
        //    透明度先写成"当前实时值"：
        //    全新打开 = 0（第一帧还没亮），中途反向 = 面板当下的亮度，接得上不闪
        //    变换基值留给第 8 步统一写（要用刚量到的锚点）

        LaunchBoard.Width =
            launchBoardWidth;


        LaunchBoard.Height =
            launchBoardHeight;


        LaunchBoard.Opacity =
            launchBoardFromBoardOpacity;


        LaunchBoard.Visibility =
            Visibility.Visible;


        // 窗口矩形与展板的基值、可见性都写完了
        // 立刻把布局跑一遍
        // 让"新窗口 + 展板已就位"的第一帧先准备好
        // 系统就没机会先把上一帧的 Dock 拉伸一下
        // 那个拉伸就是看到的闪

        RootGrid.UpdateLayout();


        // 每次打开都从第一页开始
        // 免得上次滚到一半的偏移留着

        BoardScroll.ScrollToTop();


        // 布局跑完，ScrollViewer 的视口有值了
        // 这时算前两页的容量最准

        UpdateBoardPaging();


        // 7. 转场锚点：
        //    RenderTransformOrigin = (0.21, 1)：九点阵那一列 + 展板下边缘
        //    缩放中心就落在九点阵上 → 展板在原地放大，看起来就是从九点阵挤出来
        //    小点比例现场量一遍，只用于兜底与调试输出
        //    布局已经跑完了，这一刻量出来的九点阵位置最准
        //    量不到就退回写死的兜底值，绝不会算歪

        UpdateLaunchBoardTransitionAnchor();


        // 8. 摆好本次转场的起点（= 每条动画 0s 关键帧的值，同时也是基值）
        //    全新打开：九点阵上那个小点（0.1 / 0.01）
        //    中途反向：从当前实时值接着走
        //    必须放在锚点测量之后

        PrepareLaunchBoardTransitionBases(
            true);


        // 9. 从九点阵"挤出"：一条 Storyboard 同时驱动四条关键帧动画
        //    ① 容器 Opacity：0 → 1（0 → 0.25s，第一帧就能看见那个小点）
        //    ② ScaleY：0.01 → 1（0 → 0.25s 竖着拉长成一条窄而高的柱）
        //    ③ ScaleX：0.1 → 1（0.25s → 0.5s 向左右鼓开成完整面板）
        //    ④ 内容层 Opacity：0 → 1（0.3s 之后才淡入图标与文字）
        //    总计 500ms：拉长段（①②）CubicEase(EaseOut)，
        //    鼓开段（③）BackEase(EaseOut) 收尾时轻轻回弹一下
        //    没有平移动画：容器一动不动，缩放原点就压在九点阵上
        //    只动展板最外层容器 + 内容层透明度，内部图标一个都不缩放

        launchBoardStoryboard =
            BuildLaunchBoardStoryboard(
                true);


        // 转场即将开始：标记"正在跑"
        // 中途再点一次九点阵时，那边就能识别出这是"中途反向"
        // （全新打开时这个标记是 false，起点才会落在九点阵那根细线上）

        isLaunchBoardTransitionRunning =
            true;


        // 展开播完：基值归位成"完整展板" + 清标记
        // 中途被收起打断时 Stop() 不会触发 Completed，那一轮由收起那边收尾

        launchBoardStoryboard.Completed +=
            LaunchBoardOpen_Completed;


        // Begin(this, true)：
        //   this → 指定宿主对象（窗口），Storyboard 才拿得到名域
        //   true → 可控，收起 / 中断时可以用 Stop(this) 干净收回
        // 对应的停止在 StopLaunchBoardStoryboard()

        launchBoardStoryboard.Begin(
            this,
            true);

    }


    // 展开动画播完：基值归位成"完整展板" + 清掉"正在跑"的标记
    // 只摆基值，不动可见性 / 尺寸（展板本来就该这么待着）

    private void LaunchBoardOpen_Completed(
        object? sender,
        EventArgs e)
    {

        // 播完前又被收起了 → 这次完成不作数
        // 收起的 Completed 那边会把标记和基值一起收尾

        if(!isLaunchBoardOpen)
            return;


        ResetLaunchBoardTransitionBasesToEndState(
            true);


        isLaunchBoardTransitionRunning =
            false;

    }


    // ==========================
    // 展板缩放动画的锚点（WPF 原生关键帧用）
    // 做三件事：
    //   1) ScaleTransform.CenterX / CenterY 归零
    //      → 缩放中心完全交给下面的 RenderTransformOrigin，不被 Center 抢走
    //   2) 现场量一次"九点阵中心在展板宽度上的比例"
    //      → 写进 RenderTransformOrigin 的 X，量不到就用 LaunchBoardOriginFixedX 兜底
    //   3) 把现场量到的真实值打到"输出"窗口，方便手工微调
    // 锚点含义：
    //   X = 九点阵那一列（展板 800、Dock 560 时 ≈ 0.21，是正数，见常数区说明）
    //   Y = LaunchBoardOriginFixedY = 1 → 展板下边缘（Dock 上沿那条线）
    // 于是缩放永远"从九点阵那一点长出来"，展板位置一步都不移
    // 时机：展板已经 Visible、RootGrid 已经 UpdateLayout 之后
    //       动画还没 Begin，这一刻量出来的坐标才准
    // 测不到：字段保持上一次的好值 / 兜底常数，绝不把错值传给动画
    // 【这个锚点也接管了旧版平移动画的工作】：容器自己不动，
    //   视觉上的"从九点阵挤出来"完全由缩放原点实现
    // ==========================

    private void UpdateLaunchBoardTransitionAnchor()
    {
        try
        {
            if(LaunchBoard == null ||
               BoardScale == null)
                return;


            // 1) 缩放中心归零
            //    缩放中心完全交给 RenderTransformOrigin（九点阵那一列）
            //    这里一旦非 0，就会把锚点从 RenderTransformOrigin 抢走

            BoardScale.CenterX = 0;


            BoardScale.CenterY = 0;


            // 2) 现场量一遍液滴比例 = 10px / 展板实际尺寸（只用于兜底与调试输出）
            //    用 ActualWidth / ActualHeight：真实布局尺寸，
            //    不受自身缩放影响（所以不会越缩越小）

            double realWidth =
                LaunchBoard.ActualWidth > 0
                ? LaunchBoard.ActualWidth
                : launchBoardWidth;


            double realHeight =
                LaunchBoard.ActualHeight > 0
                ? LaunchBoard.ActualHeight
                : launchBoardHeight;


            if(realWidth > 0)
                launchBoardDropletScaleX =
                    LaunchBoardDropletSize / realWidth;


            if(realHeight > 0)
                launchBoardDropletScaleY =
                    LaunchBoardDropletSize / realHeight;


            // 兜底：量出来不是正常数字就退回写死的比例

            if(double.IsNaN(launchBoardDropletScaleX) ||
               launchBoardDropletScaleX <= 0 ||
               double.IsInfinity(launchBoardDropletScaleX))
                launchBoardDropletScaleX =
                    LaunchBoardDropletScaleFallbackX;


            if(double.IsNaN(launchBoardDropletScaleY) ||
               launchBoardDropletScaleY <= 0 ||
               double.IsInfinity(launchBoardDropletScaleY))
                launchBoardDropletScaleY =
                    LaunchBoardDropletScaleFallbackY;


            // 3) 九点阵相对"展板中心"的偏移
            //    量不到就返回 null，字段保持原值（不会写进错值）

            Point? measured =
                CalculateLaunchBoardTransitionOffset();


            if(measured.HasValue)
                launchBoardTransitionOffset =
                    measured.Value;


            // 兜底：偏移不是正常数字就退回写死的常数

            if(double.IsNaN(launchBoardTransitionOffset.X) ||
               double.IsNaN(launchBoardTransitionOffset.Y) ||
               double.IsInfinity(launchBoardTransitionOffset.X) ||
               double.IsInfinity(launchBoardTransitionOffset.Y))
                launchBoardTransitionOffset =
                    new Point(
                        LaunchBoardTranslateFallbackX,
                        LaunchBoardTranslateFallbackY);


            // 4) 锚点 X = 九点阵中心在展板宽度上的比例
            //    推导：偏移量 = 九点阵中心 - 展板中心，而展板中心本身在 0.5 处，
            //          所以 比例 = 0.5 + 偏移X / 展板宽度
            //    展板 800、Dock 560 时 ≈ 0.5 - 231 / 800 ≈ 0.21（正数）
            //    算出来明显不合理（跑到展板外面太远）就退回兜底常数

            double originX =
                LaunchBoardOriginFixedX;


            if(measured.HasValue &&
               realWidth > 0)
            {
                double candidate =
                    0.5 +
                    launchBoardTransitionOffset.X / realWidth;


                if(!double.IsNaN(candidate) &&
                   !double.IsInfinity(candidate) &&
                   candidate >= -0.05 &&
                   candidate <= 0.6)
                    originX = candidate;
            }


            // 5) 写锚点：X = 九点阵那一列，Y = 展板下边缘

            LaunchBoard.RenderTransformOrigin =
                new Point(
                    originX,
                    LaunchBoardOriginFixedY);


            // 6) 打印现场量到的真实值
            //    想手工微调就把这里打印的 X 填进 LaunchBoardOriginFixedX（Y 固定 1）

            Debug.WriteLine(
                "展板挤出动画可调值：小点 ScaleX="
                + launchBoardDropletScaleX.ToString("0.0000")
                + "，ScaleY="
                + launchBoardDropletScaleY.ToString("0.0000")
                + "；九点阵偏移 X="
                + launchBoardTransitionOffset.X.ToString("0")
                + "，Y="
                + launchBoardTransitionOffset.Y.ToString("0")
                + "；缩放原点 RenderTransformOrigin=("
                + originX.ToString("0.0000")
                + ", 1)");
        }
        catch(Exception ex)
        {
            // 赋值失败也绝不让整个程序崩掉

            Debug.WriteLine(
                "设置展板转场锚点失败: "
                +
                ex.Message);
        }
    }


    // ==========================
    // 计算"九点阵中心 - 展板中心"（两个点都换算到 RootGrid 坐标系）
    //   展板：LayoutInformation.GetLayoutSlot → 布局槽
    //         （只拿布局位置，不受展板自己的缩放 / 平移影响）
    //   九点阵：GetElementRectInRoot → 现成的工具方法
    //         （含 Dock 的位移，量到的就是眼睛看到的那个位置）
    // 拿不到 / 明显算歪（NaN、无穷、量级离谱）→ 返回 null
    // 调用处会退回写死的兜底偏移 (-230, 360)
    // ==========================

    private Point? CalculateLaunchBoardTransitionOffset()
    {

        try
        {

            if(LaunchBoard == null ||
               LaunchButton == null ||
               RootGrid == null)
                return null;


            // 展板的布局槽（RootGrid 坐标系）

            Rect boardSlot =
                System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(
                    LaunchBoard);


            if(boardSlot.Width <= 0 ||
               boardSlot.Height <= 0)
                return null;


            Point boardCenter =
                new Point(
                    boardSlot.X + boardSlot.Width / 2,
                    boardSlot.Y + boardSlot.Height / 2);


            // 九点阵的矩形（同样在 RootGrid 坐标系）

            Rect buttonRect =
                GetElementRectInRoot(
                    LaunchButton);


            if(buttonRect.IsEmpty)
                return null;


            Point buttonCenter =
                new Point(
                    buttonRect.X + buttonRect.Width / 2,
                    buttonRect.Y + buttonRect.Height / 2);


            double offsetX =
                buttonCenter.X - boardCenter.X;


            double offsetY =
                buttonCenter.Y - boardCenter.Y;


            if(double.IsNaN(offsetX) ||
               double.IsNaN(offsetY) ||
               double.IsInfinity(offsetX) ||
               double.IsInfinity(offsetY))
                return null;


            if(Math.Abs(offsetX) > 4000 ||
               Math.Abs(offsetY) > 4000)
                return null;


            return new Point(
                offsetX,
                offsetY);

        }

        catch(Exception ex)
        {

            Debug.WriteLine(
                "测量九点阵位置失败: "
                +
                ex.Message);


            return null;

        }

    }


    // 关闭展板
    // immediate = true 时不走动画直接还原
    // 收起 Dock 之前必须走这一条

    private void CloseLaunchBoard(
        bool immediate)
    {

        if(!isLaunchBoardOpen)
            return;


        // 展板关掉了：批量模式也一起退出
        // 下次打开回到普通模式，点图标依然是启动应用

        SetBoardBatchMode(false);


        isLaunchBoardOpen = false;


        // 1. 先把当前实时值读出来
        //    打开动画可能还在跑，中途反向要接着当前位置走，不能跳
        //    必须在 Stop 之前读：Stop 之后属性就回到基值了

        CaptureLaunchBoardTransitionValues();


        // 2. 停掉正在跑的展开动画

        StopLaunchBoardStoryboard();


        if(!immediate)
        {

            // 动画收起：挤出动效的完全反向
            //   ① 内容层 Opacity：1 → 0（0 → 0.1s 就把图标文字清掉，不被挤扁）
            //   ② ScaleX：1 → 0.1（0 → 0.25s 横向收窄成一条柱）
            //   ③ ScaleY：1 → 0.01（0.25s → 0.5s 竖向缩回九点阵那个小点）
            //   ④ 容器 Opacity：1 → 0（最后 0.25s → 0.5s 淡出，融进九点阵）
            // 起点 = 刚读到的实时值（Prepare 里统一摆好，基值 = From）：
            //   面板完整展开时就是 1 / 0 / 1 / 1，
            //   打开还没跑完时就是当下的大小与亮度 → 接着走，不跳帧
            // 结束后才还原窗口高度

            // 窗口 / Dock 宽度都定下来了，再量一次九点阵的位置
            // 量不到就沿用打开时算好的偏移（字段里还留着好值）

            UpdateLaunchBoardTransitionAnchor();


            // 起点写成基值：基值 = From，Stop 之后画面还是起点

            PrepareLaunchBoardTransitionBases(
                false);


            launchBoardStoryboard =
                BuildLaunchBoardStoryboard(
                    false);


            launchBoardStoryboard.Completed +=
                LaunchBoardClose_Completed;


            // 收起转场开始：标记"正在跑"
            // 收了一半又点九点阵时，打开那边才知道要"接着当前位置反向"，
            // 而不是跳回九点阵那根细线重来一遍

            isLaunchBoardTransitionRunning =
                true;


            // 同上：必须带宿主对象起动画

            launchBoardStoryboard.Begin(
                this,
                true);


            return;

        }


        // 立即收起：收起 Dock 时必须走这一条
        // 否则窗口高度会被记错

        // 变换 / 透明度基值一次归位成"隐藏起来的完整展板"
        // 免得下次打开时基值还停在上一轮的小点

        ResetLaunchBoardTransitionBasesToEndState(
            false);


        isLaunchBoardTransitionRunning =
            false;


        LaunchBoard.Visibility =
            Visibility.Collapsed;


        LaunchBoard.Width =
            launchBoardWidth;


        LaunchBoard.Height =
            launchBoardHeight;


        RestoreWindowHeightAfterLaunchBoard();

    }


    // 收起动画播完：隐藏展板并还原窗口

    private void LaunchBoardClose_Completed(
        object sender,
        EventArgs e)
    {

        // 收起动画还没播完又被重新打开
        // 这次完成就不作数了

        if(isLaunchBoardOpen)
            return;


        // 变换 / 透明度基值一次归位（缩放 1 / 平移 0 / 隐藏）
        // 动画的 HoldEnd 还压在上面，这里先把基值摆好
        // 下次打开 Stop 掉动画时，基值就是"完整展板"，不会闪

        ResetLaunchBoardTransitionBasesToEndState(
            false);


        // 转场结束了，下次打开算"全新打开"

        isLaunchBoardTransitionRunning =
            false;


        LaunchBoard.Visibility =
            Visibility.Collapsed;


        LaunchBoard.Width =
            launchBoardWidth;


        LaunchBoard.Height =
            launchBoardHeight;


        RestoreWindowHeightAfterLaunchBoard();

    }


    // 关掉展板后还原窗口
    // 宽度回收成 Dock 需要的宽度
    // 高度还原成打开前的值，底边始终不动

    private void RestoreWindowHeightAfterLaunchBoard()
    {

        double dockWidth =
            DockBody.Width;


        if(double.IsNaN(dockWidth) ||
           dockWidth <= 0)
            dockWidth =
                dockWidthTarget;


        double targetHeight =
            launchBoardBaseHeight > 0
            ? launchBoardBaseHeight
            : dockBodyHeight;


        if(double.IsNaN(targetHeight) ||
           targetHeight <= 0)
            targetHeight =
                DockExpandedHeight;


        // 宽度与高度一次性写完
        // 先改宽度再改高度 = 两次窗口矩形变化
        // 每次都会被系统重绘一帧，看起来就是闪两下
        // 底边与中心都锁住，Dock 不会移位

        SetWindowBoundsKeepingBottom(
            dockWidth,
            targetHeight);


        // 窗口矩形刚写完，立刻把布局跑一遍
        // 这一帧就是"新窗口 + Dock 就位"的完整画面
        // 不会先闪一帧旧的

        RootGrid.UpdateLayout();


        launchBoardBaseHeight =
            0;

    }


    // ==========================
    // 展板尺寸：目标 900 x 650
    // 屏幕放不下就按同一比例整体缩小
    // ==========================

    private void CalculateLaunchBoardSize(
        out double width,
        out double height)
    {

        double workWidth =
            SystemParameters.WorkArea.Width;


        double workHeight =
            SystemParameters.WorkArea.Height;


        // 回弹过冲与上下留白之后剩下的空间

        double maxWidth =
            workWidth -
            LaunchBoardSpringRoom * 2;


        double maxHeight =
            workHeight -
            LaunchBoardBottomMargin -
            LaunchBoardTopSpace -
            LaunchBoardSpringRoom;


        // Dock 被拖高时，窗口上沿可能顶到屏幕
        // 这里按“底边到屏幕上沿”的真实空间再收一次
        // 展板就不会被屏幕裁掉，窗口也不会越出屏幕

        double windowLeft;
        double windowTop;
        double windowWidth;
        double windowHeight;


        if(TryGetWindowBounds(
            out windowLeft,
            out windowTop,
            out windowWidth,
            out windowHeight))
        {

            double spaceAbove =
                windowTop +
                windowHeight -
                LaunchBoardBottomMargin -
                LaunchBoardTopSpace -
                LaunchBoardSpringRoom;


            if(spaceAbove < maxHeight)
                maxHeight = spaceAbove;

        }


        if(maxWidth < 320)
            maxWidth = 320;


        if(maxHeight < 220)
            maxHeight = 220;


        // 放得下就用 900 x 650；
        // 放不下就取宽 / 高两个方向里更严格的比例，整体等比缩小。
        // 不能只缩一个方向：宽高比一变，设置中心预览的拖动范围
        // 就和展板 UniformToFill 裁掉的区域对不上，边上会拖不到。

        double fitScale = 1.0;


        if(LaunchBoardDesiredWidth > maxWidth)
        {

            fitScale =
                Math.Min(
                    fitScale,
                    maxWidth / LaunchBoardDesiredWidth);

        }


        if(LaunchBoardDesiredHeight > maxHeight)
        {

            fitScale =
                Math.Min(
                    fitScale,
                    maxHeight / LaunchBoardDesiredHeight);

        }


        width =
            LaunchBoardDesiredWidth * fitScale;


        height =
            LaunchBoardDesiredHeight * fitScale;

    }


    // ==========================
    // 展板打开时的窗口矩形
    // 底边固定 → Dock 在桌面上不动
    // 左右对称 → 展板围绕 Dock 中心展开
    // 宽高一次写完，避免逐帧闪烁
    // ==========================

    private void GrowWindowForLaunchBoard(
        double boardWidth,
        double boardHeight)
    {

        double left;
        double top;
        double width;
        double height;


        if(!TryGetWindowBounds(
            out left,
            out top,
            out width,
            out height))
            return;


        double targetHeight =
            LaunchBoardBottomMargin +
            boardHeight +
            LaunchBoardTopSpace +
            LaunchBoardSpringRoom;


        double targetWidth =
            width;


        double boardWindowWidth =
            boardWidth +
            LaunchBoardSpringRoom * 2;


        if(targetWidth < boardWindowWidth)
            targetWidth = boardWindowWidth;


        // 底边固定，反推新的顶边

        double newTop =
            top +
            height -
            targetHeight;


        // 中心固定，左右平摊

        double center =
            left +
            width / 2;


        double newLeft =
            center -
            targetWidth / 2;


        double newWidth =
            targetWidth;


        double newHeight =
            targetHeight;


        SnapToPixelGrid(
            ref newLeft,
            ref newTop,
            ref newWidth,
            ref newHeight);


        if(Math.Abs(left - newLeft) < 0.5 &&
           Math.Abs(top - newTop) < 0.5 &&
           Math.Abs(width - newWidth) < 0.5 &&
           Math.Abs(height - newHeight) < 0.5)
            return;


        ApplyWindowBounds(
            newLeft,
            newTop,
            newWidth,
            newHeight);

    }


    // ==========================
    // 展板开 / 关动画：一条 Storyboard（从九点阵那一点"原地挤出"）
    // 四条 DoubleAnimationUsingKeyFrames，全部挂在"展板容器这个 FrameworkElement"上：
    //   ① TransformGroup.Children[0] ScaleTransform.ScaleY：0.01 → 1（竖向拉长）
    //   ② TransformGroup.Children[0] ScaleTransform.ScaleX：0.1  → 1（横向鼓开）
    //   ③ 展板容器 Opacity：0 → 1（0 → 0.25s，第一帧就能看见那个小点）
    //   ④ 内容层 BoardContentLayer.Opacity：0 → 1（0.3s 之后才淡入图标）
    // 【没有任何平移动画】容器位置全程居中不动，
    // "从九点阵挤出来"完全靠 RenderTransformOrigin 把缩放原点压在九点阵上
    // opening = true  打开：0 → 0.25s 拉长 → 0.5s 鼓开（总时长 500ms）
    // opening = false 收起：0 → 0.25s 收窄 → 0.5s 缩回小点（完全反向）
    // 起点全部取 launchBoardFrom*，终点按 opening 反向
    // 展板内部的图标控件一个都不缩放
    // ==========================

    private Storyboard BuildLaunchBoardStoryboard(
        bool opening)
    {

        Storyboard storyboard =
            new Storyboard();


        // 打开：整块容器从九点阵那个小点挤出、鼓开（位置一帧都不动）
        // 收起：完全反向（先收窄成柱 → 再缩回小点 → 最后淡出）
        //
        // 关键修正：动画要作用在"展板容器这个 FrameworkElement"上，
        // 用间接属性路径穿过它的 RenderTransform 落到子变换：
        //     (UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)
        //     (UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)
        // 官方规则：Storyboard 能动画的对象，必须是 FrameworkElement /
        // FrameworkContentElement，或者被用来设置它们的属性；
        // 要动画 ScaleTransform / TranslateTransform 这类 Freezable，
        // 就必须这样“间接定位”。
        // 之前直接 Storyboard.SetTarget(动画, BoardScale)
        // 就是动画在跑、画面上却没有任何缩放的原因（只有 Opacity 生效）。
        //
        // 展板本身的宽高从头到尾都等于最终尺寸
        // → 内部图标的大小与布局一帧都不会重排，画面不发涩
        // → 图标悬停放大用的是它自己的 ScaleTransform，完全不受影响
        //
        // 旧的“长条长大板”尺寸动画（Width / Height）已停用：
        // 它会在动画期间不停改变 ScrollViewer 视口
        // 图标跟着重排，看着就生硬

        // 两个关键帧时刻
        //   打开：0 → 0.25s 竖向拉长，0.25s → 0.5s 横向鼓开（BackEase 弹簧回弹）
        //   收起：完全反向，0 → 0.25s 横向收窄，0.25s → 0.5s 竖向缩回
        // 中间那个关键帧的时刻：打开取 columnTime，收起取 shrinkTime

        TimeSpan columnTime =
            TimeSpan.FromMilliseconds(
                LaunchBoardTransitionDuration *
                LaunchBoardColumnRatio);


        TimeSpan totalTime =
            TimeSpan.FromMilliseconds(
                LaunchBoardTransitionDuration);


        TimeSpan shrinkTime =
            totalTime -
            columnTime;


        TimeSpan midTime =
            opening
            ? columnTime
            : shrinkTime;


        // 内容层（图标 / 文字）的关键帧时刻
        //   打开：0.3s → 0.5s 淡入（展板够宽了才显示图标，避免被挤扁）
        //   收起：0 → 0.1s 淡出（一开始就把内容清掉）

        TimeSpan contentMidTime =
            opening
            ? TimeSpan.FromMilliseconds(
                LaunchBoardTransitionDuration *
                LaunchBoardContentFadeInRatio)
            : TimeSpan.FromMilliseconds(
                LaunchBoardTransitionDuration *
                LaunchBoardContentFadeOutRatio);


        // ==========================
        // 关键帧的起点 / 终点
        // 起点（0s）全部来自 launchBoardFrom*
        //   → 由 PrepareLaunchBoardTransitionBases() 在动画开始前摆好
        //     全新打开 = 写死的小点；中途反向 = 当前正在显示的实时值
        // 终点：
        //   打开 = 完整展板（缩放 1 / 不透明）
        //   收起 = 九点阵上那个小点（读数见 LaunchBoardExtrudeStart* 常数）
        // 【平移已经全部删除】：这里不再有 TranslateX / TranslateY
        // ==========================

        double fromScaleX =
            launchBoardFromScaleX;


        double fromScaleY =
            launchBoardFromScaleY;


        double fromBoardOpacity =
            launchBoardFromBoardOpacity;


        double fromContentOpacity =
            launchBoardFromContentOpacity;


        double endScaleX =
        opening
        ? 1
        : LaunchBoardExtrudeStartScaleX;


        double endScaleY =
        opening
        ? 1
        : LaunchBoardExtrudeStartScaleY;


        double endBoardOpacity =
        opening ? 1 : 0;


        double endContentOpacity =
        opening ? 1 : 0;

        // ==========================
        // ① 竖向缩放 ScaleY：拉长 / 缩回
        //   打开：0.01 → 1（0 → 0.25s 这一段横向只有 0.1 宽，所以是"一根柱"）
        //         0.25s → 0.5s 一直保持 1
        //   收起：1 一直保持到 0.25s，最后 0.25s → 0.5s 缩回 0.01
        //   竖向不开弹簧：它会缩到 0.01，回弹越过 0 就成了负缩放
        // ==========================

        storyboard.Children.Add(
            CreateLaunchBoardKeyFrames(
                LaunchBoard,
                LaunchBoardScalePathY,
                fromScaleY,
                midTime,
                1,
                totalTime,
                endScaleY,
                false));


        // ==========================
        // ② 横向缩放 ScaleX：鼓开 / 收窄（这一段带弹簧回弹）
        //   打开：0 → 0.25s 一直保持 0.1（还是那根窄柱），
        //         0.25s → 0.5s 鼓开到 1，BackEase(EaseOut) 收尾时轻轻过一下终点
        //   收起：0 → 0.25s 收窄成 0.1，之后保持（这一段 CubicEase，弹簧只给打开的鼓开段）
        // 【原来的 ③ 平移 X / ④ 平移 Y 已彻底删除】
        //   容器位置必须永远居中不动，"从九点阵挤出来"全靠缩放原点
        // ==========================

        storyboard.Children.Add(
            CreateLaunchBoardKeyFrames(
                LaunchBoard,
                LaunchBoardScalePathX,
                fromScaleX,
                midTime,
                LaunchBoardExtrudeStartScaleX,
                totalTime,
                endScaleX,
                true));

        // ==========================
        // ③ 容器透明度（只有透明度，不参与缩放）
        //   打开：0 → 1（0 → 0.25s，第一帧就能看见那个小点，不做等待）
        //   收起：1 保持到 0.25s，最后 0.25s → 0.5s 淡出，融进九点阵
        // ==========================

        storyboard.Children.Add(
            CreateLaunchBoardKeyFrames(
                LaunchBoard,
                LaunchBoardOpacityPath,
                fromBoardOpacity,
                midTime,
                1,
                totalTime,
                endBoardOpacity,
                false));


        // ==========================
        // ④ 内容层（图标 / 文字）透明度
        //   打开：0.3s → 0.5s 淡入（展板够宽了才显示图标，避免被挤扁）
        //   收起：0 → 0.1s 淡出（一开始就把内容清掉）
        //   内容层永远只改 Opacity，不缩放 → 图标大小一个像素都不变
        // ==========================

        storyboard.Children.Add(
            CreateLaunchBoardKeyFrames(
                BoardContentLayer,
                LaunchBoardOpacityPath,
                fromContentOpacity,
                contentMidTime,
                0,
                totalTime,
                endContentOpacity,
                false));


        return storyboard;

    }


    // ==========================
    // 一条"三关键帧"动画：0s 起点 → midTime 中间值 → endTime 终点
    // 每一帧都是 EasingDoubleKeyFrame：
    //   第一段（0 → 0.25s）CubicEase(EaseOut)：快速挤出 / 收窄，尾巴平滑
    //   第二段（0.25s → 0.5s）可切成 BackEase(EaseOut)：弹簧回弹（见 springEnd）
    // 帧与帧之间由 WPF 自己插值 → 形变连续，不分段突跳
    //   target      动画作用的对象（必须是 FrameworkElement：
    //               展板容器 LaunchBoard / 内容层 BoardContentLayer）
    //   targetPath  间接属性路径，见 LaunchBoardScalePathX 等常数
    //   fromValue   0s 那一刻的值（来自 launchBoardFrom*，
    //               中途反向时 = 当前正在显示的实时值）
    //   midTime     中间关键帧的时刻（打开 0.25s / 收起 0.25s）
    //   midValue    中间关键帧的值（保持窄柱 / 保持完整展板）
    //   endTime     终点时刻（500ms）
    //   endValue    终点值（打开 = 完整展板，收起 = 九点阵上的细线）
    //   springEnd   true  = 最后一段用 BackEase(EaseOut)：弹簧回弹感
    //               false = 最后一段用 CubicEase(EaseOut)：稳稳落到终点
    // ==========================

    private DoubleAnimationUsingKeyFrames CreateLaunchBoardKeyFrames(
        DependencyObject target,
        string targetPath,
        double fromValue,
        TimeSpan midTime,
        double midValue,
        TimeSpan endTime,
        double endValue,
        bool springEnd)
    {

        DoubleAnimationUsingKeyFrames animation =
            new DoubleAnimationUsingKeyFrames();


        // 最后一段（midTime → endTime）的缓动函数
        //   第一段（0 → midTime）永远是 CubicEase(EaseOut)，写在下面两个关键帧里
        //   竖向 ScaleY 一律不开弹簧：它会缩到 0.01，
        //   回弹越过 0 就成了负缩放，画面会闪一下镜像

        IEasingFunction endEase =
        springEnd
        ? CreateBackEase(
            LaunchBoardExtrudeSpringAmplitude,
            EasingMode.EaseOut)
        : CreateCubicEase(
            EasingMode.EaseOut);


        // 三段关键帧
        //   0   → mid ：拉长（ScaleY） / 收窄（ScaleX） / 保持（另外几个）
        //   mid → end ：鼓开（ScaleX） / 缩回（ScaleY） / 滑回原位（平移）

        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                fromValue,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero))
            {

                EasingFunction =
                CreateCubicEase(
                    EasingMode.EaseOut)

            });


        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                midValue,
                KeyTime.FromTimeSpan(
                    midTime))
            {

                EasingFunction =
                CreateCubicEase(
                    EasingMode.EaseOut)

            });


        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                endValue,
                KeyTime.FromTimeSpan(
                    endTime))
            {

                EasingFunction =
                endEase

            });


        // 动画跑完保持终点值，交给 LaunchBoardOpen / Close_Completed 收尾

        animation.FillBehavior =
            FillBehavior.HoldEnd;


        // 目标 = 展板容器 / 内容层（都是 FrameworkElement）
        // 属性路径穿过 RenderTransform 落到 TransformGroup 的第 n 个子变换
        // → 官方要求的间接定位，动画才会真的重绘画面

        Storyboard.SetTarget(
            animation,
            target);


        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath(
                targetPath));


        return animation;

    }


    // 单条尺寸动画（旧版“长条长大板”用）
    // 现在打开 / 收起改成了 WPF 原生关键帧（缩放 + 平移 + 淡入淡出）
    // 这里已不再调用，保留备用，不影响编译

    private DoubleAnimation CreateLaunchBoardAnimation(
        string property,
        DependencyObject target,
        double from,
        double to,
        double duration,
        bool opening)
    {

        IEasingFunction ease =
            opening
            ? CreateSpringEase()
            : CreateCubicEase(
                EasingMode.EaseIn);


        DoubleAnimation animation =
            new DoubleAnimation
            {

                From = from,


                To = to,


                Duration =
                TimeSpan.FromMilliseconds(
                    duration),


                EasingFunction =
                ease,


                FillBehavior =
                FillBehavior.HoldEnd

            };


        Storyboard.SetTarget(
            animation,
            target);


        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath(
                property));


        return animation;

    }


    // ==========================
    // 【已停用·保留备用】单条属性动画（被 CreateLaunchBoardKeyFrames 取代）
    // 每条动画只负责一个属性，起点 / 终点 / 起跑时刻 / 时长全部显式给出
    //   打开：起点 = 九点阵那颗液滴 → 终点 = 完整面板
    //   收起：起点 = 当下状态 → 终点 = 九点阵那颗液滴
    // 目标写法：容器（FrameworkElement）+ 属性路径
    //   直接属性（Opacity）：target = 控件本身，path = "Opacity"
    //   变换属性：target = 展板容器，
    //             path  = (UIElement.RenderTransform).(TransformGroup.Children)[n].(ScaleTransform.ScaleX)
    //   绝不直接指向里面那个 ScaleTransform / TranslateTransform 对象
    // 展板内部的图标控件一个都不缩放
    // ==========================

    private DoubleAnimation CreateLaunchBoardTransitionAnimation(
        DependencyObject target,
        string targetPath,
        double from,
        double to,
        double beginRatio,
        double durationRatio,
        IEasingFunction easing)
    {

        DoubleAnimation transition =
            new DoubleAnimation
            {

                From = from,


                To = to,


                // 延后起跑：几个属性错开时间，才有"先竖向挤出、再横向展开"的顺序
                // 起跑时刻 / 时长都按总时长比例给，改 LaunchBoardTransitionDuration 就整体变速

                BeginTime =
                TimeSpan.FromMilliseconds(
                    LaunchBoardTransitionDuration * beginRatio),


                Duration =
                TimeSpan.FromMilliseconds(
                    LaunchBoardTransitionDuration * durationRatio),


                EasingFunction = easing,


                FillBehavior =
                FillBehavior.HoldEnd

            };


        // 目标 = 展板容器 / 内容层（都是 FrameworkElement）
        // 属性路径穿过 RenderTransform 落到 TransformGroup 的第 n 个子变换
        // → 官方要求的间接定位，动画才会真的重绘画面

        Storyboard.SetTarget(
            transition,
            target);


        Storyboard.SetTargetProperty(
            transition,
            new PropertyPath(
                targetPath));


        return transition;

    }


    // ==========================
    // 弹簧缓动
    // Amplitude 越大回弹越猛（1.0 大约超出终点 10%）
    // 这里只用 1.03 / 1.04 → 收尾时轻轻过一下，不来回摆
    // ==========================

    private BackEase CreateBackEase(
        double amplitude,
        EasingMode mode)
    {

        return new BackEase
        {

            Amplitude = amplitude,


            EasingMode = mode

        };

    }


    // ==========================
    // 读出"展板此刻正在显示的实时值"
    // 必须在 StopLaunchBoardStoryboard() 之前调用：
    //   Stop 之后属性会回到基值，实时值就丢了
    // 读的是动画的有效值（WPF 的 GetValue 会带上正在跑的动画）
    // 读失败（控件还没就绪）就保留上一次的好值
    // ==========================

    private void CaptureLaunchBoardTransitionValues()
    {

        try
        {

            if(BoardScale != null)
            {

                launchBoardFromScaleX =
                    BoardScale.ScaleX;


                launchBoardFromScaleY =
                    BoardScale.ScaleY;

            }

            if(LaunchBoard != null)
                launchBoardFromBoardOpacity =
                    LaunchBoard.Opacity;


            if(BoardContentLayer != null)
                launchBoardFromContentOpacity =
                    BoardContentLayer.Opacity;

        }

        catch(Exception ex)
        {

            Debug.WriteLine(
                "读取展板转场实时值失败: "
                +
                ex.Message);

        }

    }


    // ==========================
    // 摆好本次转场的起点（= 每条动画 0s 关键帧的值，同时也是属性基值）
    // 打开：
    //   全新打开（上一轮已经收干净）→ 九点阵上那根细线（常数写死）
    //   中途反向（收起还没跑完）    → 从当前实时值接着走，不跳帧
    // 收起：永远从当前实时值接着走（面板完整展开时就是 1 / 0 / 1 / 1）
    // 基值 = From：Stop 掉动画后画面就停在起点本身，中间不会闪一下
    // ==========================

    private void PrepareLaunchBoardTransitionBases(
        bool opening)
    {

        // 中途反向 = 打开 + 收起正在跑 + 展板还看得见（亮度 > 0.01）
        // 这时起点必须是"当前实时值"；直接跳回细线起点会跳帧

        bool continueFromCurrent =
            opening
            && isLaunchBoardTransitionRunning
            && launchBoardFromBoardOpacity > 0.01;


        if(opening &&
           !continueFromCurrent)
        {

            // 全新打开：起点 = 九点阵上那个小点（按需求写死，见常数）
            // 想微调"从多大的点开始长"就改 LaunchBoardExtrudeStartScaleX / ScaleY，
            // "输出"窗口里会打印现场量到的真实值做参考

            launchBoardFromScaleX =
                LaunchBoardExtrudeStartScaleX;


            launchBoardFromScaleY =
                LaunchBoardExtrudeStartScaleY;

            // 容器第一帧就开始淡入：细线要立刻看得见，不做等待

            launchBoardFromBoardOpacity =
                0;


            // 内容层一开始不显示：面板不够宽时不能露出图标

            launchBoardFromContentOpacity =
                0;

        }


        // 起点一次写进真实基值

        ApplyLaunchBoardTransitionBases();

    }


    // 把 launchBoardFrom* 一次写进真实属性基值
    // 基值 = From：动画 Stop 之后画面停在起点，不闪

    private void ApplyLaunchBoardTransitionBases()
    {

        try
        {

            if(BoardScale != null)
            {

                BoardScale.ScaleX =
                    launchBoardFromScaleX;


                BoardScale.ScaleY =
                    launchBoardFromScaleY;

            }

            if(LaunchBoard != null)
                LaunchBoard.Opacity =
                    launchBoardFromBoardOpacity;


            if(BoardContentLayer != null)
                BoardContentLayer.Opacity =
                    launchBoardFromContentOpacity;

        }

        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置展板转场基值失败: "
                +
                ex.Message);

        }

    }


    // ==========================
    // 转场跑完（或立即收起）后把基值归位到"终态"
    //   opened = true ：完整展板（缩放 1 / 不透明 / 内容可见）
    //   opened = false：隐藏起来的完整展板（下次打开好接管，Stop 后也不会闪）
    // 动画的 HoldEnd 还压在上面时写基值，画面不受影响
    // ==========================

    private void ResetLaunchBoardTransitionBasesToEndState(
        bool opened)
    {

        launchBoardFromScaleX = 1;

        launchBoardFromScaleY = 1;

        launchBoardFromBoardOpacity =
        opened
        ? 1
        : 0;

        launchBoardFromContentOpacity = 1;


        ApplyLaunchBoardTransitionBases();

    }


    // ==========================
    // 【已停用·保留备用】旧版关键帧挤压动画（ScaleX / ScaleY）
    // 停用原因：Storyboard.SetTarget 直接指向 ScaleTransform 这种 Freezable，
    //           在 WPF 里不会真的驱动渲染层，画面上只有 Opacity 生效。
    //           现在改用上面的 CreateLaunchBoardKeyFrames（关键帧）
    // ==========================

    // ==========================
    // 打开：挤压弹出（关键帧驱动 ScaleX / ScaleY）
    // 起点 → 峰值：BackEase，冲过峰值一点再退回（Q弹的第一下）
    // 峰值 → 1：ElasticEase，围绕 1 来回摆几下，幅度越来越小
    // 总时长 = LaunchBoardOpenDuration（0.4 秒）
    // 目标是展板自己的 ScaleTransform（BoardScale）
    // 锚点由 UpdateLaunchBoardTransitionAnchor() 在打开展板前现场测量（九点阵位置）
    // ==========================

    private DoubleAnimationUsingKeyFrames CreateLaunchBoardPopAnimation(
        string property,
        double start,
        double peak)
    {

        // 第一阶段的结束时刻
        // 前半段（总时长的 55%）用来“挤出来”
        // 后半段用来 Q 弹回落

        TimeSpan riseTime =
            TimeSpan.FromMilliseconds(
                LaunchBoardOpenDuration *
                LaunchBoardPopRiseRatio);


        // 第一阶段：快速弹到峰值

        EasingDoubleKeyFrame rise =
            new EasingDoubleKeyFrame
            {

                KeyTime =
                KeyTime.FromTimeSpan(
                    riseTime),


                Value =
                peak,


                EasingFunction =
                new BackEase
                {

                    Amplitude =
                    LaunchBoardPopBackAmplitude,


                    EasingMode =
                    EasingMode.EaseOut

                }

            };


        // 第二阶段：回落到 1，并且抖动收敛

        EasingDoubleKeyFrame settle =
            new EasingDoubleKeyFrame
            {

                KeyTime =
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        LaunchBoardOpenDuration)),


                Value =
                1.0,


                EasingFunction =
                new ElasticEase
                {

                    Oscillations =
                    LaunchBoardPopOscillations,


                    Springiness =
                    LaunchBoardPopSpringiness,


                    EasingMode =
                    EasingMode.EaseOut

                }

            };


        // 起始帧：被压扁的起点
        // ScaleY = 0 就是贴底边的一条线

        DoubleKeyFrame first =
            new DiscreteDoubleKeyFrame(
                start,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero));


        DoubleAnimationUsingKeyFrames pop =
            new DoubleAnimationUsingKeyFrames
            {

                FillBehavior =
                FillBehavior.HoldEnd

            };


        pop.KeyFrames.Add(
            first);


        pop.KeyFrames.Add(
            rise);


        pop.KeyFrames.Add(
            settle);


        // 目标写展板自己的 ScaleTransform
        // 不是 Border 本身

        Storyboard.SetTarget(
            pop,
            BoardScale);


        Storyboard.SetTargetProperty(
            pop,
            new PropertyPath(
                property));


        return pop;

    }


    // ==========================
    // 【已停用·保留备用】旧版收起动画
    // 收起：把上面的弹出动画反过来放
    // ScaleY / ScaleX 从 1 收回起点（0 / 0.55）
    // BackEase + EaseIn：起步先微微鼓一下，再“嗖”地缩回去
    // 结束值就是起点值，也就是贴底边的一条线
    // 收完由 LaunchBoardClose_Completed 把展板隐藏并还原窗口
    // （现在收起由 CreateLaunchBoardKeyFrames 负责）
    // ==========================

    private DoubleAnimation CreateLaunchBoardCollapseAnimation(
        string property,
        double collapseTo)
    {

        DoubleAnimation animation =
            new DoubleAnimation
            {

                From =
                1.0,


                To =
                collapseTo,


                Duration =
                TimeSpan.FromMilliseconds(
                    LaunchBoardCloseDuration),


                EasingFunction =
                new BackEase
                {

                    Amplitude =
                    LaunchBoardPopBackAmplitude,


                    EasingMode =
                    EasingMode.EaseIn

                },


                FillBehavior =
                FillBehavior.HoldEnd

            };


        // 目标同样是展板自己的 ScaleTransform
        // 不是 Border 本身

        Storyboard.SetTarget(
            animation,
            BoardScale);


        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath(
                property));


        return animation;

    }


    // 弹簧缓动：旧版尺寸动画的“弹”就来自这里
    // EaseOut + BackEase：先冲过去，再弹回来
    // Amplitude 越大越夸张
    // （现在打开 / 收起走缩放挤压弹出，这里已不再调用，保留备用）

    private BackEase CreateSpringEase()
    {

        return new BackEase
        {

            Amplitude =
            LaunchBoardSpringAmplitude,


            EasingMode =
            EasingMode.EaseOut

        };

    }


    // 普通缓动

    private CubicEase CreateCubicEase(
        EasingMode mode)
    {

        return new CubicEase
        {

            EasingMode =
            mode

        };

    }


    // 停掉展板动画
    // 属性会回到基值（也就是打开时设的最终尺寸）

    private void StopLaunchBoardStoryboard()
    {

        if(launchBoardStoryboard == null)
            return;


        try
        {

            // 起动画用的是 Begin(this, true)（可控 + 指定宿主对象）
            // 停的时候必须带同一个宿主对象，否则停不干净
            // 停掉后 ScaleY / Opacity 回到基值（1 / 0）

            launchBoardStoryboard.Stop(
                this);

        }

        catch(Exception ex)
        {

            Debug.WriteLine(
                "停止展板动画失败: "
                +
                ex.Message);

        }


        launchBoardStoryboard =
            null;

    }


    // ==========================
    // 窗口矩形：宽度与高度一次写完
    // 底边固定 → Dock 在桌面上不移位
    // 中心固定 → 左右对称，不甩动
    // 分两次改（先宽后高）会各重绘一帧
    // 看起来就是 Dock 闪一下
    // ==========================

    private void SetWindowBoundsKeepingBottom(
        double targetWidth,
        double targetHeight)
    {

        if(double.IsNaN(targetWidth) ||
           targetWidth <= 0)
            return;


        if(double.IsNaN(targetHeight) ||
           targetHeight <= 0)
            return;


        double left;
        double top;
        double width;
        double height;


        if(!TryGetWindowBounds(
            out left,
            out top,
            out width,
            out height))
            return;


        // 中心固定，左右平摊

        double center =
            left +
            width / 2;


        double newLeft =
            center -
            targetWidth / 2;


        // 底边固定，反推新的顶边

        double newTop =
            top +
            height -
            targetHeight;


        double newWidth =
            targetWidth;


        double newHeight =
            targetHeight;


        // 吸附后中心与底边仍然锁在整物理像素上

        SnapToPixelGrid(
            ref newLeft,
            ref newTop,
            ref newWidth,
            ref newHeight);


        if(Math.Abs(left - newLeft) < 0.5 &&
           Math.Abs(top - newTop) < 0.5 &&
           Math.Abs(width - newWidth) < 0.5 &&
           Math.Abs(height - newHeight) < 0.5)
            return;


        ApplyWindowBounds(
            newLeft,
            newTop,
            newWidth,
            newHeight);

    }


    // 当前窗口真实高度

    private double CaptureWindowHeight()
    {

        double left;
        double top;
        double width;
        double height;


        if(TryGetWindowBounds(
            out left,
            out top,
            out width,
            out height) &&
           height > 0)
            return height;


        return DockExpandedHeight;

    }


    // ==========================
    // 收放动画期间的窗口矩形
    // 尺寸在动画开始前就已经一次性定好
    // （见 SetCollapsed）
    // 动画进行中不再改窗口
    // 这里只服务"图标数量变化"这类宽度动画
    // ==========================

    private void TrackDockWindow()
    {

        if(dockDragStarted)
            return;


        double width =
            DockBody.Width;


        if(double.IsNaN(width) ||
           width <= 0)
            return;


        // 收放动画期间什么都不做
        // 窗口矩形已经在动画开始前一次性定好
        // 动画过程中绝不能再逐帧改窗口
        // 逐帧改窗口会让系统反复拉伸上一帧画面
        // 肉眼看到的就是抖动

        if(isCollapseAnimating)
            return;


        // 只做宽度动画时
        // 窗口高度不动，宽度跟胶囊

        ResizeWindowTo(
            width);

    }





    // ==========================
    // 元素淡入淡出
    // ==========================

    private void FadeElement(
        UIElement element,
        double to,
        double delayMilliseconds,
        double durationMilliseconds,
        EasingMode mode)
    {

        if(element == null)
            return;


        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {

                To =
                to,


                BeginTime =
                TimeSpan.FromMilliseconds(
                    delayMilliseconds),


                Duration =
                TimeSpan.FromMilliseconds(
                    durationMilliseconds),


                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    mode

                },


                FillBehavior =
                FillBehavior.HoldEnd

            });

    }




    private void Watcher_DockHover(
        bool state)
    {


        // 常驻显示

        ShowDock();


    }












    private void ShowDock()
    {

        // 收起 / 收起动画期间不显示图标与箭头

        if(isCollapsed ||
           isCollapseAnimating)
        {

            isHidden = false;

            return;

        }



        isHidden = false;






        DoubleAnimation iconFade =
            new DoubleAnimation
            {


                To = 1,



                Duration =
                TimeSpan.FromMilliseconds(200),




                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseOut

                }



            };






        IconPanel.BeginAnimation(
            OpacityProperty,
            iconFade);








        DoubleAnimation move =
            new DoubleAnimation
            {


                To = 0,



                Duration =
                TimeSpan.FromMilliseconds(300),




                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseOut

                }



            };






        DockTransform.BeginAnimation(
            TranslateTransform.YProperty,
            move);



    }












    private void HideDock()
    {


        // 保留给未来收缩动画使用

        if(isPinned)
            return;






        isHidden = true;







        DoubleAnimation iconFade =
            new DoubleAnimation
            {


                To = 0,



                Duration =
                TimeSpan.FromMilliseconds(200),




                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseIn

                }



            };







        IconPanel.BeginAnimation(
            OpacityProperty,
            iconFade);



    }












    private void Dock_MouseMove(
        object sender,
        MouseEventArgs e)
    {


        Point p =
            e.GetPosition(
                DockBody);







        if(DockBody.ActualWidth > 0 &&
           DockBody.ActualHeight > 0)
        {



            glassTarget =
                new Point
                (
                    p.X /
                    DockBody.ActualWidth,



                    p.Y /
                    DockBody.ActualHeight
                );



        }








        glassTarget.X =
            Math.Max(
                0,
                Math.Min(
                    1,
                    glassTarget.X));







        glassTarget.Y =
            Math.Max(
                0,
                Math.Min(
                    1,
                    glassTarget.Y));



    }
        // ==========================
    // Liquid Glass + 水凝胶核心
    // ==========================


    private void DockRendering(
        object? sender,
        EventArgs e)
    {


        // ==========================
        // 展开的「等帧」动作
        // ==========================
        // 双击展开时，Logo 已经在 PrepareCollapseLogoForExpand()
        // 里出局了，但那只是一次「改内存」；
        // 要等没有 Logo 的画面真正推给系统（大约 2 帧）之后，
        // 才允许改窗口矩形。
        // 这样系统在改矩形时贴出来的旧位图里没有 Logo，
        // Dock 左上角就不会再闪一下。
        // 平时 expandResizePending 一直是 false，这一小段不走

        if(expandResizePending)
        {

            if(expandResizeFrameWait > 0)
            {

                expandResizeFrameWait--;

            }

            else if(IsLoaded)
            {

                // 标记先清掉，避免任何情况下卡在 pending

                expandResizePending =
                    false;


                // 窗口矩形一次性撑到最终尺寸
                // 中心与底边仍锁在动画开始时的锚点上

                FitWindowToDock(
                    expandResizeToWidth,
                    expandResizeWindowHeight);


                // 窗口刚变大，立刻把排版走完，
                // 免得「窗口已经变大、内容还是旧坐标」的那一帧被合成出去

                RootGrid.UpdateLayout();


                // 起跑：与收起方向调用的是同一个方法

                StartDockWidthAnimation(
                    expandResizeFromWidth,
                    expandResizeToWidth,
                    expandResizeToHeight,
                    false);

            }

        }


        // ==========================
        // 水凝胶弹簧运动
        // ==========================


        double forceX =
            glassTarget.X - gelX;



        double forceY =
            glassTarget.Y - gelY;







        gelVelocityX +=
            forceX *
            gelSpring;




        gelVelocityY +=
            forceY *
            gelSpring;








        gelVelocityX *=
            gelDamping;



        gelVelocityY *=
            gelDamping;








        gelX +=
            gelVelocityX;



        gelY +=
            gelVelocityY;








        gelX =
            Math.Max(
                0,
                Math.Min(
                    1,
                    gelX));





        gelY =
            Math.Max(
                0,
                Math.Min(
                    1,
                    gelY));









        // ==========================
        // 光晕缓慢跟随
        // ==========================


        glassCurrent.X +=
        (
            gelX -
            glassCurrent.X
        )
        *
        0.12;







        glassCurrent.Y +=
        (
            gelY -
            glassCurrent.Y
        )
        *
        0.12;








        if(GlassBrush != null)
        {


            GlassBrush.GradientOrigin =
                glassCurrent;



        }










        // ==========================
        // 液体内部缓慢流动
        // ==========================


        liquidTime +=
            0.012;







        if(GlassBrush != null)
        {


            double liquidX =
                0.35
                +
                Math.Sin(liquidTime)
                *
                0.08;





            double liquidY =
                0.28
                +
                Math.Cos(
                    liquidTime *
                    0.7)
                *
                0.06;







            GlassBrush.GradientOrigin =
                new Point
                (

                    (
                        glassCurrent.X
                        +
                        liquidX
                    )
                    /
                    2,



                    (
                        glassCurrent.Y
                        +
                        liquidY
                    )
                    /
                    2

                );



        }









        // ==========================
        // 玻璃呼吸
        // ==========================


        breathTime +=
            0.018;







        double opacity =
            0.94
            +
            Math.Sin(
                breathTime)
            *
            0.03;







        DockBody.Opacity =
            opacity;









        // ==========================
        // 动态高光流动
        // ==========================


        sweepTime +=
            0.008;







        if(LightSweep != null)
        {


            double offset =
                Math.Sin(
                    sweepTime)
                *
                0.8;







            LightSweep.StartPoint =
                new Point(
                    -1 + offset,
                    0);







            LightSweep.EndPoint =
                new Point(
                    1 + offset,
                    1);



        }




        // ==========================
        // 窗口矩形：精确跟随
        // 收起 / 展开动画期间窗口不动
        // 尺寸已经在动画开始前一次性定好
        // 只有宽度动画（图标增减）才逐帧跟随
        // ==========================

        if(isCollapseAnimating ||
           DockBody.HasAnimatedProperties)
            TrackDockWindow();


    }
        // ==========================
    // 拖入检测
    // ==========================


    private void Dock_DragOver(
        object sender,
        DragEventArgs e)
    {


        // 文件拖入属于外部手势
        // 清掉可能残留的按下状态

        dockDragArmed = false;


        // 内部拖拽：展板 → Dock

        AppDragPayload? payload =
            GetAppDragPayload(
                e);


        if(payload != null)
        {


            // 从展板拖回来的：收下
            // Dock 自己的图标拖回来：本来就在这，忽略

            e.Effects =
                payload.From ==
                    AppDragFromBoard
                ? DragDropEffects.Move
                : DragDropEffects.None;


            e.Handled = true;


            return;


        }


        if(e.Data.GetDataPresent(
            DataFormats.FileDrop))
        {


            e.Effects =
                DragDropEffects.Copy;



            isDragging = true;



            ShowDock();



        }
        else
        {


            e.Effects =
                DragDropEffects.None;



        }





        e.Handled = true;



    }









    // ==========================
    // 放入文件
    // ==========================


    private void Dock_Drop(
        object sender,
        DragEventArgs e)
    {


        // 【排查追踪】Drop 事件最开头：先打印当前模式和拿到的文件路径
        // 如果这里没有输出，说明 Drop 事件根本没触发（例如管理员权限隔离 / 拖错目标）

        string[]? droppedFilesForTrace = null;


        try
        {

            if(e.Data.GetDataPresent(
                DataFormats.FileDrop))
            {

                droppedFilesForTrace =
                    e.Data.GetData(
                        DataFormats.FileDrop) as string[];

            }

        }
        catch(Exception traceEx)
        {

            Debug.WriteLine(
                "[Dock_Drop] 读取拖入文件失败："
                +
                traceEx);

        }


        Debug.WriteLine(
            "[Dock_Drop] 当前模式："
            +
            (dockIconMode == DockIconMode.MoveToStore
                ? "收纳"
                : "保留")
            +
            "，接到的文件路径："
            +
            (droppedFilesForTrace == null ||
             droppedFilesForTrace.Length == 0
                ? "（没有文件路径）"
                : string.Join(
                    " | ",
                    droppedFilesForTrace)));


        isDragging = false;


        // 内部拖拽：展板 → Dock

        AppDragPayload? payload =
            GetAppDragPayload(
                e);


        if(payload != null)
        {


            e.Handled = true;


            if(payload.From ==
                AppDragFromBoard)
            {


                MoveBoardAppToDock(
                    payload.App,
                    payload.SourcePath);


            }


            return;


        }







        if(droppedFilesForTrace == null ||
           droppedFilesForTrace.Length == 0)
        {

            // 收纳模式下，任何“拖进来但没拿到文件”的情况都要提示
            // 否则用户会以为已经收纳成功

            if(dockIconMode == DockIconMode.MoveToStore)
            {

                MessageBox.Show(
                    "没有从拖拽内容中读到文件路径，无法收纳。\n\n"
                    +
                    "请直接从桌面拖动文件或文件夹再试。",
                    "AeroDock",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

            }


            return;

        }







        // 本次拖拽里有没有"因为重复所以被忽略"的
        // 提示只弹一次，免得弹一串

        bool duplicateSkipped =
            false;


        string[] files =
            droppedFilesForTrace;








        foreach(string file in files)
        {


            Debug.WriteLine(
                "[Dock_Drop] 正在处理文件："
                +
                file
                +
                "，当前模式："
                +
                (dockIconMode == DockIconMode.MoveToStore
                    ? "收纳"
                    : "保留"));


            if(File.Exists(file)
            ||
            Directory.Exists(file))
            {


                // 拖拽去重：这个应用已经在 Dock / 展板上了
                // 同一个软件的不同快捷方式也只算一个
                // 直接忽略本次拖拽，不再添加第二个

                if(IsAppAlreadyPresent(file))
                {


                    if(!duplicateSkipped)
                    {


                        duplicateSkipped =
                            true;


                        MessageBox.Show(

                            DuplicateAppMessage,

                            "AeroDock",

                            MessageBoxButton.OK,

                            MessageBoxImage.Information);


                    }


                    continue;


                }




                // Dock 已满 12 个：不再延长
                // 这个应用直接进"收纳盒"（展板）的数据集合

                if(AppManager.Apps.Count >= DockCapacity)
                {


                    // 展板现在全类型通吃
                    // 只有路径真的找不到才会跳过

                    if(!IsBoardAcceptable(file))
                    {


                        ShowBoardStatus(
                            "这个路径找不到，收不进收纳盒");


                        continue;


                    }


                    if(CreateBoardApp(file))
                    {


                        SaveBoardApps();


                        ShowBoardStatus(
                            "Dock 已满 12 个，已收进展板："
                            +
                            Path.GetFileNameWithoutExtension(file));


                    }
                    else
                    {


                        ShowBoardStatus(
                            "这个应用已经在 Dock 或展板上了");


                    }


                    continue;


                }


                // Dock 还有空位
                // 它原来躺在展板上就先取出来
                // 同一个应用只占一个位置

                RemoveBoardAppBySourcePath(file);


                // 桌面图标处理模式 = 收纳至 Dock 时：
                // 桌面上的 .lnk / .exe 先搬进收纳目录
                // Dock 里记录的换成搬过去的新路径
                // 移动失败会弹窗，并按原路径添加（= 模式A 的表现）
                // 注意：只有"真的进 Dock"的文件才搬，
                //       进收纳盒（展板）的保持桌面原样

                string dockPath =
                    PreparePathForDock(file);


                bool addedToDock =
                    AddAppToDock(dockPath);


                // 搬过文件的话让新路径立刻落盘一次
                // （AppManager 自己也会存，这里只是双保险）

                if(addedToDock &&
                   IsInsideShortcutStore(dockPath))
                {

                    try
                    {

                        DockStorage.Save();

                    }
                    catch(Exception ex)
                    {

                        Debug.WriteLine(
                            "保存 Dock 失败: "
                            +
                            ex.Message);

                    }

                }



            }
            else if(dockIconMode ==
                    DockIconMode.MoveToStore)
            {

                // 收纳模式下拖进来一个已经不存在的路径
                // 必须明确告诉用户，不能静默跳过

                MessageBox.Show(
                    "这个路径已经不存在，无法收纳：\n\n"
                    +
                    file,
                    "AeroDock",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

            }


        }







        RefreshIcons();



        ShowDock();



    }

    // ==========================
    // 全文件类型支持：统一启动
    // ==========================
    // 这里【不判断任何后缀】，一个 if (EndsWith) 都没有
    // 一律交给 Windows 外壳（UseShellExecute = true）
    // 由系统自己挑默认程序：
    //   .txt → 记事本        .pdf  → 阅读器
    //   .jpg → 看图工具      .zip  → 解压工具
    //   .url → 浏览器        .exe  → 直接运行
    //   .lnk → 快捷方式目标   文件夹 → 资源管理器
    // ==========================


    private static void OpenByShell(
        string? path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return;


        try
        {


            Process.Start(

                new ProcessStartInfo(
                    path)
                {

                    UseShellExecute =
                        true

                });


        }
        catch(Exception ex)
        {


            // 启动失败绝不抛出
            // 免得把整个 Dock 拖崩

            Debug.WriteLine(
                "启动失败: "
                +
                ex.Message);


            MessageBox.Show(

                "这个文件或文件夹打不开：\n\n"
                +
                path
                +
                "\n\n"
                +
                ex.Message,

                "AeroDock",

                MessageBoxButton.OK,

                MessageBoxImage.Warning);


        }


    }




    // ==========================
    // 全文件类型支持：统一进 Dock
    // ==========================
    // 先走 AppManager（保留它原有的解析 / 落盘逻辑）
    // 它装不下的类型（.txt / .pdf / .zip / .jpg / .url / 文件夹 …）
    // 再由这里直接补进 Dock 列表
    // 不看后缀，只看路径是不是真的存在
    // ==========================


    private static bool AddAppToDock(
        string path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return false;


        if(!File.Exists(path) &&
           !Directory.Exists(path))
            return false;


        // 已经在 Dock 上了：不重复添加

        if(DockHasLaunchPath(path))
            return false;


        int before =
            AppManager.Apps.Count;


        try
        {


            AppManager.AddApp(
                path);


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "AppManager 添加失败: "
                +
                ex.Message);


        }


        if(AppManager.Apps.Count > before)
            return true;


        // 兜底：只要系统外壳打得开的，Dock 都收

        try
        {


            AppManager.Apps.Add(

                new AppItem(

                    GetDisplayName(
                        path),

                    path,

                    AppType.Exe));


            return true;


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "Dock 添加失败: "
                +
                ex.Message);


            return false;


        }


    }




    // Dock 上是不是已经有这个路径

    private static bool DockHasLaunchPath(
        string? path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return false;


        foreach(AppItem item in AppManager.Apps)
        {


            if(string.Equals(
                item.LaunchPath,
                path,
                StringComparison.OrdinalIgnoreCase))
            {


                return true;


            }


        }


        return false;


    }




    // ==========================
    // 拖拽去重：同一个应用只留一个
    // 1) .lnk 比的是解析后的真实目标
    // 2) 普通文件 / 文件夹比的是绝对路径
    // 3) 同一个软件还会比应用名称
    // ==========================

    // 重复拖入时的提示文案

    private const string DuplicateAppMessage =
        "该应用已在 Dock 中，请勿重复添加";


    // 应用类后缀
    // 只有这类路径才会按"应用名称"再判一次
    // 普通文档（.txt / .pdf / .jpg …）不看名字
    // 免得把不同文件夹里的同名文件误判成重复

    private static readonly string[] AppLikeExtensions =
    {

        ".exe",

        ".lnk",

        ".url",

        ".appref-ms",

        ".bat",

        ".cmd",

        ".com",

        ".msi",

        ".msc"

    };


    // 这个路径看着像"应用"吗
    // 有应用类后缀，或者干脆没后缀（文件夹 / AppX）都算

    private static bool IsAppLikePath(
        string? path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return false;


        string extension =
            Path.GetExtension(
                path.Trim());


        if(string.IsNullOrWhiteSpace(extension))
            return true;


        foreach(string known in AppLikeExtensions)
        {


            if(extension.Equals(
                known,
                StringComparison.OrdinalIgnoreCase))
                return true;


        }


        return false;


    }


    // 快捷方式类：这类路径才会按名称再判一次
    // 同一个应用的两份快捷方式（桌面一个、开始菜单一个）靠它认出来

    private static bool IsShortcutLikePath(
        string? path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return false;


        string extension =
            Path.GetExtension(
                path.Trim());


        if(string.IsNullOrWhiteSpace(extension))
            return false;


        return
            extension.Equals(
                ".lnk",
                StringComparison.OrdinalIgnoreCase)
            ||
            extension.Equals(
                ".url",
                StringComparison.OrdinalIgnoreCase)
            ||
            extension.Equals(
                ".appref-ms",
                StringComparison.OrdinalIgnoreCase);


    }


    // AppX 的 LaunchPath 长这样：
    // Microsoft.WindowsCalculator_8wekyb3d8bbwe!App
    // 用 '!' 认它，省得跟普通扩展名纠缠

    private static bool LooksLikeAppUserModelId(
        string? path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return false;


        return
            path.IndexOf('!') >= 0;


    }


    // 取路径的"身份"：
    // .lnk 先解析成真实目标，再换成绝对路径，最后统一小写
    // 同一个软件的不同快捷方式，到这里就变成同一个 key

    private static string GetAppIdentityKey(
        string? path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return "";


        string resolved =
            path.Trim();


        // 快捷方式：优先比解析后的真实目标

        try
        {


            string extension =
                Path.GetExtension(resolved);


            if(!string.IsNullOrWhiteSpace(extension) &&
               extension.Equals(
                   ".lnk",
                   StringComparison.OrdinalIgnoreCase))
            {


                string target =
                    ShellHelper.ResolvePath(resolved);


                if(!string.IsNullOrWhiteSpace(target))
                    resolved = target.Trim();


            }


        }
        catch
        {

            // 解析失败就退回原路径

        }


        // 普通文件 / 文件夹：比绝对路径

        try
        {


            if(File.Exists(resolved) ||
               Directory.Exists(resolved))
                resolved = Path.GetFullPath(resolved);


        }
        catch
        {

            // 拿不到绝对路径就退回原路径

        }


        resolved =
            resolved.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);


        return
            resolved.Length == 0
            ? ""
            : resolved.ToLowerInvariant();


    }


    // 这个已有条目是不是就是被拖进来的那个应用

    private static bool IsSameAppItem(
        AppItem? item,
        string key,
        string displayName,
        bool compareName)
    {


        if(item == null)
            return false;


        // 1) 真实目标 / 绝对路径一致

        if(key.Length > 0)
        {


            string itemKey =
                GetAppIdentityKey(
                    item.LaunchPath);


            if(itemKey.Length > 0 &&
               string.Equals(
                   itemKey,
                   key,
                   StringComparison.OrdinalIgnoreCase))
                return true;


        }


        // 2) 同一个软件：名称一样也算重复
        // 只在"快捷方式 / AppX"这类条目上比名称
        // 这样同一应用的两份快捷方式能认出来
        // 又不会把同名但不同目录的两个 exe 误判成重复

        if(compareName &&
           (IsShortcutLikePath(item.LaunchPath) ||
            LooksLikeAppUserModelId(item.LaunchPath)))
        {


            if(!string.IsNullOrWhiteSpace(item.Name) &&
               string.Equals(
                   item.Name.Trim(),
                   displayName,
                   StringComparison.OrdinalIgnoreCase))
                return true;


        }


        return false;


    }


    // 拖进来的路径是不是已经在 Dock / 展板上了
    // Dock 和展板一起查：同一个应用两边都不能重复

    private bool IsAppAlreadyPresent(
        string path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return false;


        string key =
            GetAppIdentityKey(path);


        string displayName =
            GetDisplayName(path);


        bool compareName =
            IsAppLikePath(path);


        foreach(AppItem item in AppManager.Apps)
        {


            if(IsSameAppItem(
                item,
                key,
                displayName,
                compareName))
                return true;


        }


        foreach(AppItem item in boardApps)
        {


            if(IsSameAppItem(
                item,
                key,
                displayName,
                compareName))
                return true;


        }


        return false;


    }


    // 显示名：中文优先（气泡 / 图标名字都走这里）
    // 顺序（每一步都 try-catch：任何一步失败只是少一条候选，绝不崩）：
    //   1. .lnk：WScript.Shell 的友好名称（Description）
    //          + 快捷方式自己的名字（微信.lnk → 微信）
    //          + 目标程序的 FileDescription / ProductName
    //   2. .exe：FileVersionInfo 的 FileDescription / ProductName
    //          + 系统里指向同一个程序的中文快捷方式
    //            （微信的 Weixin.exe 自带描述是英文，
    //              就靠开始菜单里的"微信.lnk"把中文名捞回来）
    //   3. 上面只要有一条带中文，就用中文那条
    //   4. 一条中文都没有，才退而求其次用文件名（老行为）
    // 只读：不新建、不修改、不删除任何文件 / 快捷方式
    // 文件夹名这一支和以前一字不差，图标 / 拖拽 / 去重都不受影响

    private static string GetDisplayName(
        string? path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return "";


        string trimmed =
            path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);


        if(trimmed.Length == 0)
            trimmed = path;


        // 文件夹：保持原样

        if(Directory.Exists(trimmed))
            return Path.GetFileName(trimmed);


        // 文件：先问系统要"人话名字"（中文优先）

        try
        {

            string smart =
                StripIconTipShortcutSuffix(
                    GetIconTipFriendlyName(
                        trimmed));


            if(smart.Length > 0)
                return smart;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "获取中文名失败: "
                +
                ex.Message);

        }


        // 一点中文都拿不到：退回文件名（原来的兜底）

        string name =
            Path.GetFileNameWithoutExtension(
                trimmed);


        return
            name.Length == 0
            ? Path.GetFileName(trimmed)
            : name;


    }


    // 名字尾巴上的噪音清掉（只动用来显示的字符串，不动文件）
    //   "微信.lnk"        → "微信"
    //   "微信 - 快捷方式"  → "微信"

    private static string StripIconTipShortcutSuffix(
        string? value)
    {

        string text =
            (value ??
             "").Trim();


        if(text.Length > 4 &&
           text.EndsWith(
               ".lnk",
               StringComparison.OrdinalIgnoreCase))
            text = text.Substring(
                0,
                text.Length - 4).Trim();


        string[] tails =
        {
            "- 快捷方式",
            "- shortcut"
        };


        for(int i = 0; i < tails.Length; i++)
        {

            string tail =
                tails[i];


            if(text.Length > tail.Length &&
               text.EndsWith(
                   tail,
                   StringComparison.OrdinalIgnoreCase))
            {

                text = text.Substring(
                    0,
                    text.Length - tail.Length).Trim();


                break;

            }

        }


        return text;

    }















    // ==========================
    // App 拖拽：Dock ↔ 展板
    // 用 WPF 原生的 DoDragDrop
    // 载荷走自定义格式，只在本进程内传
    // 不碰文件系统，也不加全局钩子
    // ==========================


    // 开始一次应用拖拽
    // from 标记来源：dock / board

    private void StartAppDrag(
        AppItem? app,
        string from,
        string? path)
    {


        if(app == null)
            return;


        try
        {

            AppDragPayload payload =
                new AppDragPayload();


            payload.From =
                from;


            payload.App =
                app;


            payload.SourcePath =
                string.IsNullOrWhiteSpace(path)
                ? ""
                : path;


            DataObject data =
                new DataObject();


            data.SetData(
                AppDragFormat,
                payload);


            // 系统光标就是最好的拖拽反馈
            // 放在目标上会自己变成"移动"

            DragDrop.DoDragDrop(
                this,
                data,
                DragDropEffects.Move);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "应用拖拽失败: "
                +
                ex.Message);

        }


    }



    // ==========================
    // Dock 里的 App 拖到展板
    // 只搬这一个，绝对不从展板补人回 Dock
    // ==========================

    private void MoveDockAppToBoard(
        AppItem? app)
    {

        // 没给落点：放到末尾
        // （右键"收进收纳盒"这类入口走这里）

        MoveDockAppToBoard(
            app,
            -1);

    }




    private void MoveDockAppToBoard(
        AppItem? app,
        int insertIndex)
    {

        // placed：到底有没有真的搬过去
        // 没搬成也要把让位收回来，界面不能卡在让位状态

        bool placed = false;


        try
        {


            if(app == null)
                return;


            if(!AppManager.Apps.Contains(app))
                return;


            // 展板存的是路径，读回来得还能认出来
            // AppX / 文件夹这类存不上就先别搬
            // 不然下次启动会在展板上少一个

            if(!IsBoardAcceptable(app.LaunchPath))
            {


                ShowBoardStatus(
                    "找不到这个应用的原始路径，放不进收纳盒");


                return;


            }


            // 两边都不留重复

            if(BoardHasLaunchPath(app.LaunchPath))
            {


                ShowBoardStatus(
                    "收纳盒里已经有这个应用了");


                return;


            }


            // 落点：拖拽进来的按鼠标位置插
            // 其它入口一律放末尾（insertIndex = -1）

            int insertAt =
                insertIndex;


            if(insertAt < 0)
                insertAt = boardApps.Count;


            if(insertAt > boardApps.Count)
                insertAt = boardApps.Count;


            if(insertAt < 0)
                insertAt = 0;


            // 先进收纳盒，再从 Dock 摘掉
            // Dock 自然少一个，不去补人
            // 列表顺序跟着落点走，JSON 才存得对

            boardApps.Insert(
                insertAt,
                app);


            int pathIndex =
                insertAt > boardSourcePaths.Count
                ? boardSourcePaths.Count
                : insertAt;


            boardSourcePaths.Insert(
                pathIndex,
                app.LaunchPath);


            boardArrivalIndex =
                insertAt;


            AppManager.RemoveApp(app);


            SaveBoardApps();


            RefreshIcons();


            ShowBoardStatus(
                "已收进收纳盒："
                +
                app.Name);


            ShowDock();


            placed = true;


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "Dock 搬进展板失败: "
                +
                ex.Message);


        }
        finally
        {


            // 搬成了：让位清零 + 新那格入位动画（等一次布局之后跑）
            // 没搬成：让位原地收回

            if(placed)
            {


                SettleBoardIconLayout();


            }
            else
            {


                ClearBoardMakeWay(true);


            }


        }


    }



    // ==========================
    // 展板内部换位：拖到别的格子上松手
    // 拖动源插进落点槽位，中间的图标整体让一格
    // 让位期间列表一直没动，只有松手这一刻才真的重排
    // ==========================


    private void DropBoardAppOnBoard(
        AppItem? app,
        int sourceIndex,
        int targetIndex)
    {

        bool moved = false;


        try
        {


            if(app == null)
                return;


            int from =
                boardApps.IndexOf(
                    app);


            if(from < 0)
                return;


            // 落点没算出来就退回拖动源原来的槽位（等于原地放下）

            int to =
                targetIndex;


            int last =
                boardApps.Count - 1;


            if(to < 0)
                to = sourceIndex >= 0
                     ? sourceIndex
                     : from;


            if(to > last)
                to = last;


            if(to < 0)
                to = 0;


            // 原地放下：什么都不用动

            if(to == from)
            {


                boardArrivalIndex = -1;


                ClearBoardMakeWay(true);


                return;


            }


            MoveBoardOrder(
                from,
                to);


            boardArrivalIndex =
                to;


            // 顺序落进 JSON：关机 / 重启后还是这个顺序

            SaveBoardApps();


            ShowBoardStatus(
                "展板顺序已更新："
                +
                app.Name);


            moved = true;


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板换位失败: "
                +
                ex.Message);


        }
        finally
        {


            if(moved)
            {


                SettleBoardIconLayout();


            }
            else
            {


                ClearBoardMakeWay(true);


            }


        }


    }




    // 展板换位：图标列表和路径列表一起挪
    // 两个列表必须严格同序，不然 JSON 存下来的顺序就乱了

    private void MoveBoardOrder(
        int from,
        int to)
    {

        try
        {


            int count =
                boardApps.Count;


            if(from < 0 ||
               from >= count)
                return;


            if(to < 0)
                to = 0;


            if(to >= count)
                to = count - 1;


            if(from == to)
                return;


            boardApps.Move(
                from,
                to);


            if(from < boardSourcePaths.Count &&
               to < boardSourcePaths.Count)
            {


                string path =
                    boardSourcePaths[from];


                boardSourcePaths.RemoveAt(
                    from);


                boardSourcePaths.Insert(
                    to,
                    path);


            }


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板顺序同步失败: "
                +
                ex.Message);


        }


    }




    // ==========================
    // 展板里的 App 拖回 Dock
    // 只有 Dock 已经满 12 个，才挤出末尾那一个
    // 没满就直接放回去，展板自然少一个
    // ==========================

    private void MoveBoardAppToDock(
        AppItem? app,
        string? path)
    {


        if(app == null)
            return;


        int index =
            boardApps.IndexOf(app);


        if(index < 0)
            return;


        // 放回 Dock 要的是原始文件路径

        string source =
            string.IsNullOrWhiteSpace(path)
            ? ""
            : path;


        if(!IsBoardAcceptable(source) &&
           index < boardSourcePaths.Count)
        {


            source =
                boardSourcePaths[index];


        }


        if(!IsBoardAcceptable(source))
        {


            ShowBoardStatus(
                "找不到这个应用的原始路径，放不回 Dock");


            return;


        }


        // Dock 满 12 个了
        // 才把末尾那个挤进收纳盒，给新来的腾位置

        if(AppManager.Apps.Count >= DockCapacity)
        {


            AppItem? last =
                AppManager.Apps.Count > 0
                ? AppManager.Apps[AppManager.Apps.Count - 1]
                : null;


            if(last == null ||
               !IsBoardAcceptable(last.LaunchPath))
            {


                ShowBoardStatus(
                    "Dock 已满，末尾的应用存不进收纳盒");


                return;


            }


            // 挤出来的那个排到收纳盒末尾

            if(!BoardHasLaunchPath(last.LaunchPath))
            {


                boardApps.Add(last);


                boardSourcePaths.Add(last.LaunchPath);


            }


            AppManager.RemoveApp(last);


        }


        // 收纳盒里摘掉它，交给 Dock

        boardApps.RemoveAt(index);


        if(index < boardSourcePaths.Count)
        {

            boardSourcePaths.RemoveAt(index);

        }


        // 收纳至 Dock 模式：展板 → Dock 也算“真的进 Dock”
        // 桌面来源的 .lnk / .exe 同样要搬进收纳目录
        // 不搬的话桌面上会留下原文件，而且没有任何提示

        if(dockIconMode == DockIconMode.MoveToStore &&
           IsOnUserDesktop(source))
        {

            source =
                PreparePathForDock(source);

        }


        AddAppToDock(source);


        SaveBoardApps();


        RefreshIcons();


        ShowBoardStatus(
            "已放回 Dock："
            +
            app.Name);


        ShowDock();


    }



    // 展板上是不是已经有这个目标了
    // 和 MigrateDockOverflowToBoard 用同一套比较方式

    private bool BoardHasLaunchPath(
        string? launchPath)
    {


        if(string.IsNullOrWhiteSpace(launchPath))
            return false;


        foreach(AppItem item in boardApps)
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



    // ==========================
    // 拖拽载荷：判断与读取
    // ==========================


    private static bool IsAppDrag(
        DragEventArgs e)
    {


        if(e == null)
            return false;


        return e.Data.GetDataPresent(
            AppDragFormat);


    }



    private static AppDragPayload? GetAppDragPayload(
        DragEventArgs e)
    {


        if(!IsAppDrag(e))
            return null;


        return e.Data.GetData(
            AppDragFormat) as AppDragPayload;


    }



    // ==========================
    // Dock 图标：按下就先记下它
    // 用来判定这次拖动是"移动窗口"还是"拖应用"
    // ==========================


    // 按下的源元素是不是 Dock 图标
    // 图标模板的 DataContext 就是 AppItem

    private static AppItem? GetDockIconApp(
        object source)
    {


        FrameworkElement? element =
            source as FrameworkElement;


        if(element == null)
            return null;


        return element.DataContext as AppItem;


    }



    // 这次手势算不算"把 Dock 里的 App 拖出去"
    // 按在图标上 + 往上拖才算
    // Dock 是贴底的长条，往上正好是展板的方向
    // 横向 / 向下拖还是老样子：移动窗口

    private bool IsDockIconAppDrag(
        double dx,
        double dy)
    {


        AppItem? app =
            dockDragIconSource;


        if(app == null)
            return false;


        // 图标已经不在 Dock 上了就别拖了

        if(!AppManager.Apps.Contains(app))
            return false;


        if(!RequireUpwardDragForApp)
            return true;


        // 必须是往上（dy 为负）
        // 而且垂直分量不能小于水平分量
        // 不然横向移动窗口的手势会被吃掉

        if(dy >= 0)
            return false;


        return -dy >=
            Math.Abs(dx);


    }



    // ==========================
    // 拖拽视觉：跟手的幽灵图标 + Dock 动态让位
    // 内部拖拽（Dock ↔ 展板）改用鼠标捕获自己接管
    // 不用 DoDragDrop：那个循环会把动画冻住
    // 捕获之后每一帧移动都直接进这里
    // 幽灵图标 / 让位 / 挤出 全都跟得上手
    // ==========================


    // 开始一次带视觉的内部拖拽
    // 返回是否真的开始（已经有一次在跑就不重入）

    private bool StartAppDragVisual(
        AppItem? app,
        string from,
        string? path)
    {


        if(app == null)
            return false;


        if(appDragActive)
            return false;


        appDragActive = true;


        appDragFrom =
            from;


        appDragItem =
            app;


        appDragSourcePath =
            string.IsNullOrWhiteSpace(path)
            ? ""
            : path;


        appDragOverDock = false;


        appDragInsertIndex = -1;


        dockMakeWayIndex = -1;


        // 展板让位那一套：让位槽位 / 落点槽位 / 拖动源槽位 全部复位
        // 展板内部换位要用"拖动源在列表里的槽位"，这里先记下来
        // （列表顺序就是槽位顺序，拖动期间都不会变）

        boardMakeWayIndex = -1;


        boardMakeWaySourceIndex = -1;


        boardDragInsertIndex = -1;


        appDragBoardSourceIndex =
            from == AppDragFromBoard
            ? boardApps.IndexOf(
                app)
            : -1;


        // 来源槽位：拖动期间半透明
        // 一眼就能看出"这个位置已经腾出来了"

        appDragSourceContainer =
            FindAppContainer(
                from == AppDragFromDock
                ? IconPanel
                : BoardPanel,
                app);


        appDragSourceContainerOpacity = 1;


        if(appDragSourceContainer != null)
        {


            appDragSourceContainerOpacity =
                appDragSourceContainer.Opacity;


            FadeElement(
                appDragSourceContainer,
                0.3,
                0,
                140,
                EasingMode.EaseOut);


        }


        // 跟手的幽灵图标

        ShowDragGhost(
            app,
            appDragSourceContainer);


        // 本次手势是拖拽：松手不启动应用

        dockDragSuppressClick = true;


        boardIconDragSuppressClick = true;


        // 抓住鼠标
        // 抓不住也能跑，只是窗口外松开时收不到松手

        appDragCaptured =
            Mouse.Capture(this);


        Mouse.OverrideCursor =
            Cursors.Hand;


        return true;


    }


    // 拖拽过程中的每一帧
    // 只做两件事：幽灵图标跟手 + Dock 落点反馈

    private void UpdateAppDrag(
        MouseEventArgs e)
    {


        if(!appDragActive)
            return;


        // 松手事件丢了（在窗口外松开等）
        // 这里补一次收尾，免得界面卡在拖拽状态

        if(e.LeftButton !=
            MouseButtonState.Pressed)
        {


            CancelAppDrag();


            return;


        }


        Point inRoot =
            Mouse.GetPosition(
                RootGrid);


        MoveDragGhost(
            inRoot);


        // Dock 高光跟着鼠标走
        // 拖拽期间 Dock_MouseMove 收不到事件，这里自己算

        bool overDock =
            IsPointInElement(
                inRoot,
                DockBody,
                DockDropTolerance);


        if(overDock)
        {

            UpdateGlassTargetFromPointer();

        }


        appDragOverDock =
            overDock;


        // 从 Dock 往外拖：越过 Dock 上沿就把展板叫出来接住

        if(appDragFrom == AppDragFromDock &&
           !overDock &&
           !isLaunchBoardOpen &&
           inRoot.Y <
                GetElementRectInRoot(DockBody).Top - 12)
        {

            OpenLaunchBoard();

        }


        // ==========================
        // Dock 让位（原逻辑一字未改）
        // 只有"展板 → Dock"才需要在 Dock 上让位
        // ==========================

        if(appDragFrom == AppDragFromBoard)
        {

            if(overDock)
            {


                appDragInsertIndex =
                    ComputeDockInsertIndex();


                ApplyDockMakeWay(
                    appDragInsertIndex);


            }
            else
            {


                appDragInsertIndex = -1;


                ClearDockMakeWay(true);


            }

        }


        // ==========================
        // 展板让位（推挤）
        // 展板 → 展板：网格里换位，中间图标整体侧移一格
        // Dock → 展板：新图标插进落点那一格，后面整体后退一格
        // 压在 Dock 上时不算落在展板里
        // ==========================

        UpdateBoardMakeWay(
            inRoot,
            overDock);


    }




    // 展板让位：每一帧按鼠标落点重算一次
    // 落点变了才重新起动画，没变就什么都不做
    // 整段包了 try-catch：拖拽中途出意外也不能让界面卡在让位状态

    private void UpdateBoardMakeWay(
        Point inRoot,
        bool overDock)
    {

        try
        {


            bool overBoard =
                isLaunchBoardOpen &&
                !overDock &&
                IsPointInElement(
                    inRoot,
                    LaunchBoard,
                    0);


            if(!overBoard)
            {


                boardDragInsertIndex = -1;


                ClearBoardMakeWay(true);


                return;


            }


            // 拖动源在展板里的槽位
            // -1 = 拖动源不在展板里（Dock → 展板，走"新图标插入"规则）

            int sourceIndex = -1;


            if(appDragFrom == AppDragFromBoard)
            {


                sourceIndex =
                    appDragBoardSourceIndex;


                if(sourceIndex < 0 &&
                   appDragItem != null)
                {


                    sourceIndex =
                        boardApps.IndexOf(
                            appDragItem);


                }


            }


            boardDragInsertIndex =
                ComputeBoardInsertIndex();


            ApplyBoardMakeWay(
                boardDragInsertIndex,
                sourceIndex);


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板让位刷新失败: "
                +
                ex.Message);


        }


    }


    // Dock 里的水凝胶高光跟着鼠标

    private void UpdateGlassTargetFromPointer()
    {


        if(DockBody.ActualWidth <= 0 ||
           DockBody.ActualHeight <= 0)
            return;


        Point p =
            Mouse.GetPosition(
                DockBody);


        glassTarget =
            new Point
            (
                Math.Max(
                    0,
                    Math.Min(
                        1,
                        p.X / DockBody.ActualWidth)),


                Math.Max(
                    0,
                    Math.Min(
                        1,
                        p.Y / DockBody.ActualHeight))
            );


    }


    // 松手：决定这次拖拽的结果
    // 1. 展板 → Dock：按落点插入（满员就挤出末尾）
    // 2. Dock → 展板：按落点插进那一格
    // 3. 展板 → 展板：网格内换位
    // 4. 其它位置：原样退回

    private void EndAppDrag()
    {


        if(!appDragActive)
            return;


        AppItem? app =
            appDragItem;


        string from =
            appDragFrom;


        string path =
            appDragSourcePath;


        int index =
            appDragInsertIndex;


        Point inRoot =
            Mouse.GetPosition(
                RootGrid);


        bool overDock =
            IsPointInElement(
                inRoot,
                DockBody,
                DockDropTolerance);


        bool overBoard =
            isLaunchBoardOpen &&
            IsPointInElement(
                inRoot,
                LaunchBoard,
                0);


        bool dropToDock =
            overDock &&
            from == AppDragFromBoard;


        bool dropToBoard =
            overBoard &&
            from == AppDragFromDock;


        // 展板内部换位：拖起和落下都在展板里
        // 压在 Dock 上不算（那种情况是"放回 Dock"）

        bool dropInsideBoard =
            overBoard &&
            !overDock &&
            from == AppDragFromBoard;


        int boardIndex =
            boardDragInsertIndex;


        int boardSourceIndex =
            appDragBoardSourceIndex;


        // 没落在 Dock 上：让位动画收回原状
        // 落在 Dock 上：让位那格正好就是终点的位置
        //              交给落位动画去收，中间不会闪

        if(!dropToDock)
        {

            ClearDockMakeWay(true);

        }


        // 展板让位不在这里收：
        // 重排 / 插入会让图标基准位置整体改变
        // 先把数据挪好，再瞬间清零 + 落位动画，中间看不出跳变
        // 没落进展板（退回原处）才当场收回

        if(!dropInsideBoard &&
           !dropToBoard)
        {

            ClearBoardMakeWay(true);

        }


        FinishAppDrag();


        FadeDragGhostOut();


        if(dropToDock)
        {


            DropBoardAppToDockAt(
                app,
                path,
                index);


            return;


        }


        if(dropInsideBoard)
        {


            DropBoardAppOnBoard(
                app,
                boardSourceIndex,
                boardIndex);


            return;


        }


        if(dropToBoard)
        {

            MoveDockAppToBoard(
                app,
                boardIndex);

        }


    }


    // 意外中断（焦点丢了 / 松手事件丢了）
    // 一切回到原样，什么都不搬

    private void CancelAppDrag()
    {


        if(!appDragActive)
            return;


        ClearDockMakeWay(true);


        // 展板这一套一起收回（没落进任何区域，数据没动，直接滑回去）
        // 拖动源的不透明度由 FinishAppDrag 负责，这里别碰

        ClearBoardMakeWay(true);


        FinishAppDrag();


        FadeDragGhostOut();


    }


    // 拖拽状态统一复位
    // 注意：清了状态就不再读 appDragXXX 了

    private void FinishAppDrag()
    {


        if(appDragCaptured)
        {

            Mouse.Capture(null);


            appDragCaptured = false;

        }


        Mouse.OverrideCursor = null;


        // 来源槽位恢复不透明
        // （被搬走的那个容器会被回收，动画白跑也无所谓）

        if(appDragSourceContainer != null)
        {

            FadeElement(
                appDragSourceContainer,
                appDragSourceContainerOpacity,
                0,
                180,
                EasingMode.EaseOut);

        }


        appDragActive = false;


        appDragFrom = "";


        appDragItem = null;


        appDragSourcePath = "";


        appDragSourceContainer = null;


        appDragSourceContainerOpacity = 1;


        appDragOverDock = false;


        appDragInsertIndex = -1;


        // 展板那一套一起复位

        appDragBoardSourceIndex = -1;


        boardDragInsertIndex = -1;


    }


    // ==========================
    // 幽灵图标：跟着鼠标走的那个"半透明自己"
    // ==========================


    private void ShowDragGhost(
        AppItem app,
        FrameworkElement? source)
    {


        HideDragGhostNow();


        Grid ghost =
            BuildAppVisual(
                FindAppIconSource(source),
                DragGhostSize,
                DragGhostIconSize);


        ghost.Opacity = 0;


        // 起手小一点，弹起来更像"被拎起来"

        EnsureVisualTransform(
            ghost);


        ScaleTransform? scale =
            GetVisualScale(
                ghost);


        if(scale != null)
        {

            scale.ScaleX = 0.62;


            scale.ScaleY = 0.62;

        }


        appDragGhost =
            ghost;


        if(DragOverlay != null)
        {

            DragOverlay.Children.Add(
                ghost);

        }


        // 第一帧就要摆到鼠标下面
        // 不然会先在左上角闪一下

        MoveDragGhost(
            Mouse.GetPosition(
                RootGrid));


        FadeElement(
            ghost,
            0.94,
            0,
            DragGhostRiseDuration,
            EasingMode.EaseOut);


        AnimateVisualScale(
            ghost,
            1,
            DragGhostRiseDuration,
            EasingMode.EaseOut);


    }


    // 跟手：直接改位移，不走布局，所以每一帧都跟得上

    private void MoveDragGhost(
        Point inRoot)
    {


        if(appDragGhost == null)
            return;


        TranslateTransform? translate =
            GetVisualTranslate(
                appDragGhost);


        if(translate == null)
            return;


        translate.X =
            inRoot.X -
            DragGhostSize / 2;


        translate.Y =
            inRoot.Y -
            DragGhostSize / 2;


    }


    // 松手：幽灵在原地缩小淡出

    private void FadeDragGhostOut()
    {


        FrameworkElement? ghost =
            appDragGhost;


        appDragGhost = null;


        if(ghost == null)
            return;


        AnimateVisualScale(
            ghost,
            0.45,
            DragGhostDropDuration,
            EasingMode.EaseOut);


        FadeAndRemove(
            ghost,
            0,
            DragGhostDropDuration,
            0);


    }




    // 淡出并自己从拖拽层摘掉
    // 用 Completed 回调，不需要定时器
    // 幽灵图标 / 被挤出的图标都用它

    private void FadeAndRemove(
        UIElement? element,
        double to,
        double durationMilliseconds,
        double delayMilliseconds)
    {


        if(element == null)
            return;


        if(DragOverlay == null)
            return;


        DoubleAnimation fade =
            new DoubleAnimation
            {

                To =
                to,


                BeginTime =
                TimeSpan.FromMilliseconds(
                    delayMilliseconds),


                Duration =
                TimeSpan.FromMilliseconds(
                    durationMilliseconds),


                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseOut

                },


                FillBehavior =
                FillBehavior.HoldEnd

            };


        fade.Completed +=
            delegate
            {

                if(DragOverlay != null &&
                   DragOverlay.Children.Contains(element))
                {

                    DragOverlay.Children.Remove(
                        element);

                }

            };


        element.BeginAnimation(
            UIElement.OpacityProperty,
            fade);


    }


    // 立刻清掉幽灵

    private void HideDragGhostNow()
    {


        FrameworkElement? ghost =
            appDragGhost;


        appDragGhost = null;


        if(ghost == null)
            return;


        if(DragOverlay != null &&
           DragOverlay.Children.Contains(ghost))
        {

            DragOverlay.Children.Remove(
                ghost);

        }


    }


    // ==========================
    // 变换工具：统一的 [缩放 + 位移] 组合
    // Dock 槽位与幽灵图标共用
    // 位移用 TranslateTransform：纯渲染变换
    // 不触发重新布局，所以每帧改都不会卡
    // ==========================


    private static TransformGroup EnsureVisualTransform(
        FrameworkElement element)
    {


        TransformGroup? group =
            element.RenderTransform as TransformGroup;


        if(group != null &&
           group.Children.Count == 2)
            return group;


        group =
            new TransformGroup();


        group.Children.Add(
            new ScaleTransform(1, 1));


        group.Children.Add(
            new TranslateTransform(0, 0));


        element.RenderTransformOrigin =
            new Point(0.5, 0.5);


        element.RenderTransform =
            group;


        return group;


    }




    private static ScaleTransform? GetVisualScale(
        FrameworkElement element)
    {


        TransformGroup? group =
            element.RenderTransform as TransformGroup;


        if(group == null)
            return null;


        foreach(Transform child in group.Children)
        {


            ScaleTransform? scale =
                child as ScaleTransform;


            if(scale != null)
                return scale;


        }


        return null;


    }




    private static TranslateTransform? GetVisualTranslate(
        FrameworkElement element)
    {


        TransformGroup? group =
            element.RenderTransform as TransformGroup;


        if(group == null)
            return null;


        foreach(Transform child in group.Children)
        {


            TranslateTransform? translate =
                child as TranslateTransform;


            if(translate != null)
                return translate;


        }


        return null;


    }




    // 位移立刻归位
    // 先清掉动画，不然基值会被动画盖住，看不到效果

    private static void SetVisualTranslateX(
        FrameworkElement element,
        double x)
    {


        TranslateTransform? translate =
            GetVisualTranslate(
                element);


        if(translate == null)
            return;


        translate.BeginAnimation(
            TranslateTransform.XProperty,
            null);


        translate.X =
            x;


    }




    private static void AnimateVisualTranslateX(
        FrameworkElement element,
        double to,
        double durationMilliseconds)
    {


        TranslateTransform? translate =
            GetVisualTranslate(
                element);


        if(translate == null)
            return;


        DoubleAnimation slide =
            new DoubleAnimation
            {

                To =
                to,


                Duration =
                TimeSpan.FromMilliseconds(
                    durationMilliseconds),


                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseOut

                },


                FillBehavior =
                FillBehavior.HoldEnd

            };


        translate.BeginAnimation(
            TranslateTransform.XProperty,
            slide);


    }




    private static void AnimateVisualScale(
        FrameworkElement element,
        double to,
        double durationMilliseconds,
        EasingMode mode)
    {


        ScaleTransform? scale =
            GetVisualScale(
                element);


        if(scale == null)
            return;


        DoubleAnimation grow =
            new DoubleAnimation
            {

                To =
                to,


                Duration =
                TimeSpan.FromMilliseconds(
                    durationMilliseconds),


                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    mode

                },


                FillBehavior =
                FillBehavior.HoldEnd

            };


        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            grow);


        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            grow);


    }


    // 位移立刻定位（X / Y 一起）
    // 飞行图标用：从起点直接摆好再起动画

    private static void SetVisualTranslatePosition(
        FrameworkElement element,
        double x,
        double y)
    {


        TranslateTransform? translate =
            GetVisualTranslate(
                element);


        if(translate == null)
            return;


        translate.BeginAnimation(
            TranslateTransform.XProperty,
            null);


        translate.BeginAnimation(
            TranslateTransform.YProperty,
            null);


        translate.X =
            x;


        translate.Y =
            y;


    }




    // X / Y 一起动
    // 飞行轨迹用

    private static void AnimateVisualTranslateXY(
        FrameworkElement element,
        double toX,
        double toY,
        double durationMilliseconds,
        EasingMode mode)
    {


        TranslateTransform? translate =
            GetVisualTranslate(
                element);


        if(translate == null)
            return;


        DoubleAnimation slideX =
            new DoubleAnimation
            {

                To =
                toX,


                Duration =
                TimeSpan.FromMilliseconds(
                    durationMilliseconds),


                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    mode

                },


                FillBehavior =
                FillBehavior.HoldEnd

            };


        DoubleAnimation slideY =
            new DoubleAnimation
            {

                To =
                toY,


                Duration =
                TimeSpan.FromMilliseconds(
                    durationMilliseconds),


                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    mode

                },


                FillBehavior =
                FillBehavior.HoldEnd

            };


        translate.BeginAnimation(
            TranslateTransform.XProperty,
            slideX);


        translate.BeginAnimation(
            TranslateTransform.YProperty,
            slideY);


    }




    // 缩放立刻定位
    // 先清动画，不然动画基值会把直接设的值吃掉

    private static void SetVisualScaleNow(
        FrameworkElement element,
        double scaleValue)
    {


        ScaleTransform? scale =
            GetVisualScale(
                element);


        if(scale == null)
            return;


        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            null);


        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            null);


        scale.ScaleX =
            scaleValue;


        scale.ScaleY =
            scaleValue;


    }




    // ==========================
    // 画一个"应用图标"的视觉
    // 幽灵图标 / 飞回展板的图标共用
    // icon 传 null 就只画玻璃底座
    // ==========================


    private static Grid BuildAppVisual(
        ImageSource? icon,
        double size,
        double iconSize)
    {


        Grid root =
            new Grid
            {

                Width =
                size,


                Height =
                size

            };


        Border plate =
            new Border
            {

                Width =
                size,


                Height =
                size,


                CornerRadius =
                new CornerRadius(
                    size * 0.32),


                Background =
                new SolidColorBrush(
                    Color.FromArgb(
                        0x45,
                        0xFF,
                        0xFF,
                        0xFF)),


                BorderThickness =
                new Thickness(1),


                BorderBrush =
                new SolidColorBrush(
                    Color.FromArgb(
                        0x90,
                        0xFF,
                        0xFF,
                        0xFF))

            };


        plate.Effect =
            new DropShadowEffect
            {

                BlurRadius =
                size * 0.3,


                ShadowDepth =
                size * 0.09,


                Opacity =
                0.35

            };


        root.Children.Add(
            plate);


        if(icon != null)
        {


            Image image =
                new Image
                {

                    Width =
                    iconSize,


                    Height =
                    iconSize,


                    Source =
                    icon,


                    Stretch =
                    Stretch.Uniform

                };


            // 圆角跟 Dock 里的图标保持一致

            image.Clip =
                new RectangleGeometry(
                    new Rect(
                        0,
                        0,
                        iconSize,
                        iconSize),
                    iconSize * 0.32,
                    iconSize * 0.32);


            RenderOptions.SetBitmapScalingMode(
                image,
                BitmapScalingMode.HighQuality);


            root.Children.Add(
                image);


        }


        return root;


    }




    // 应用在列表里的槽位（ContentPresenter）

    private static FrameworkElement? FindAppContainer(
        ItemsControl panel,
        AppItem? app)
    {


        if(panel == null ||
           app == null)
            return null;


        for(int i = 0; i < panel.Items.Count; i++)
        {


            if(!ReferenceEquals(
                panel.Items[i],
                app))
                continue;


            return
                panel.ItemContainerGenerator.ContainerFromIndex(i)
                as FrameworkElement;


        }


        return null;


    }




    // 把槽位里的图标抠出来
    // 幽灵图标要长得跟原图标一模一样

    private static ImageSource? FindAppIconSource(
        FrameworkElement? container)
    {


        Image? image =
            FindIconImage(
                container);


        return
            image == null
            ? null
            : image.Source;


    }




    private static Image? FindIconImage(
        DependencyObject? root)
    {


        if(root == null)
            return null;


        int count =
            VisualTreeHelper.GetChildrenCount(
                root);


        for(int i = 0; i < count; i++)
        {


            DependencyObject child =
                VisualTreeHelper.GetChild(
                    root,
                    i);


            Image? image =
                child as Image;


            if(image != null)
                return image;


            Image? found =
                FindIconImage(
                    child);


            if(found != null)
                return found;


        }


        return null;


    }


    // ==========================
    // 位置换算
    // ==========================


    // 元素在 RootGrid 坐标系里的矩形
    // 空矩形 = 现在还不能当落点（没布局 / 不可见）

    private Rect GetElementRectInRoot(
        FrameworkElement? element)
    {


        if(element == null)
            return Rect.Empty;


        if(element.ActualWidth <= 0 ||
           element.ActualHeight <= 0)
            return Rect.Empty;


        if(!element.IsVisible)
            return Rect.Empty;


        Point point =
            element.TranslatePoint(
                new Point(0, 0),
                RootGrid);


        return
            new Rect(
                point,
                new Size(
                    element.ActualWidth,
                    element.ActualHeight));


    }




    private bool IsPointInElement(
        Point inRoot,
        FrameworkElement? element,
        double tolerance)
    {


        Rect rect =
            GetElementRectInRoot(
                element);


        if(rect.IsEmpty)
            return false;


        if(tolerance > 0)
        {

            rect.Inflate(
                tolerance,
                tolerance);

        }


        return
            rect.Contains(
                inRoot);


    }




    // ==========================
    // 落点 → 插入槽位
    // 槽位是均匀的：面板宽度 = 槽位宽 × 数量
    // 全部用 IconPanel 自己的坐标算
    // 这样 Dock 宽度动画进行中也算得准
    // ==========================


    private int ComputeDockInsertIndex()
    {


        if(IconPanel == null ||
           IconPanel.ActualWidth <= 0)
            return 0;


        int count =
            AppManager.Apps.Count;


        double origin =
            (IconPanel.ActualWidth - count * IconSlotWidth) / 2;


        double x =
            Mouse.GetPosition(
                IconPanel).X
            -
            origin;


        int index =
            (int)Math.Floor(
                (x + IconSlotWidth / 2) / IconSlotWidth);


        if(index < 0)
            index = 0;


        if(index > count)
            index = count;


        // Dock 满员：插入位最多到最后一个槽位
        // 再往右也还是把末尾那个挤出去

        if(count >= DockCapacity &&
           index > DockCapacity - 1)
        {

            index =
                DockCapacity - 1;

        }


        return index;


    }


    // ==========================
    // 【公共推挤核心】Dock 与展板共用
    // 输入：面板 + 列表 + 每个图标"当前槽位序号 / 要让去的槽位序号"
    // 位移全部走 TranslateTransform：纯渲染变换
    // 不触发重新布局，所以能一边跟手一边动
    // columns <= 0：单行均匀槽位（Dock，第 i 个槽位就在 x = i × pitchX）
    // columns > 0 ：网格槽位（展板，一行 columns 个；拐行时位移里天然带斜线）
    // dimIndex：要把不透明度压到 dimOpacity 的那一格（-1 = 不压）
    // applyOpacity：要不要顺带复位不透明度
    //             展板不用（拖动源那格是半透明的，不能被盖掉）
    // ==========================


    private void ApplySlotMakeWay(
        ItemsControl? panel,
        System.Collections.IList? items,
        int[]? fromSlots,
        int[]? toSlots,
        double pitchX,
        double pitchY,
        int columns,
        double durationMilliseconds,
        int dimIndex,
        double dimOpacity,
        bool applyOpacity)
    {

        try
        {


            if(panel == null ||
               items == null ||
               fromSlots == null ||
               toSlots == null)
                return;


            int count =
                items.Count;


            for(int i = 0; i < count; i++)
            {


                if(i >= fromSlots.Length ||
                   i >= toSlots.Length)
                    break;


                AppItem? app =
                    items[i] as AppItem;


                if(app == null)
                    continue;


                FrameworkElement? container =
                    FindAppContainer(
                        panel,
                        app);


                if(container == null)
                    continue;


                EnsureVisualTransform(
                    container);


                // 槽位序号 → 行列 → 位移量
                // 展板拐行时，位移里天然带着上下方向的斜线

                double offsetX = 0;


                double offsetY = 0;


                if(columns > 0)
                {


                    int fromRow =
                        fromSlots[i] / columns;


                    int fromColumn =
                        fromSlots[i] % columns;


                    int toRow =
                        toSlots[i] / columns;


                    int toColumn =
                        toSlots[i] % columns;


                    offsetX =
                        (toColumn - fromColumn) * pitchX;


                    offsetY =
                        (toRow - fromRow) * pitchY;


                }
                else
                {


                    offsetX =
                        (toSlots[i] - fromSlots[i]) * pitchX;


                    offsetY =
                        (toSlots[i] - fromSlots[i]) * pitchY;


                }


                AnimateVisualTranslateXY(
                    container,
                    offsetX,
                    offsetY,
                    durationMilliseconds,
                    EasingMode.EaseOut);


                if(!applyOpacity)
                    continue;


                FadeElement(
                    container,
                    dimIndex == i
                    ? dimOpacity
                    : 1,
                    0,
                    140,
                    EasingMode.EaseOut);


            }


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "让位动画失败: "
                +
                ex.Message);


        }


    }


    // 让位收回：图标滑回自己的槽位
    // animate=false 用在"数据马上要重排"的场合：
    // 那种情况容器基准位置会变，瞬间清零反而看不出跳变
    // restoreOpacity：要不要把不透明度一起复位


    private void ClearSlotMakeWay(
        ItemsControl? panel,
        System.Collections.IList? items,
        bool animate,
        double durationMilliseconds,
        bool restoreOpacity)
    {

        try
        {


            if(panel == null ||
               items == null)
                return;


            int count =
                items.Count;


            for(int i = 0; i < count; i++)
            {


                AppItem? app =
                    items[i] as AppItem;


                if(app == null)
                    continue;


                FrameworkElement? container =
                    FindAppContainer(
                        panel,
                        app);


                if(container == null)
                    continue;


                EnsureVisualTransform(
                    container);


                if(animate)
                {


                    AnimateVisualTranslateXY(
                        container,
                        0,
                        0,
                        durationMilliseconds,
                        EasingMode.EaseOut);


                }
                else
                {


                    SetVisualTranslatePosition(
                        container,
                        0,
                        0);


                }


                if(!restoreOpacity)
                    continue;


                FadeElement(
                    container,
                    1,
                    0,
                    140,
                    EasingMode.EaseOut);


            }


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "让位归位失败: "
                +
                ex.Message);


        }


    }




    // ==========================
    // Dock 让位（make way）
    // 插入位之后的图标整体右移一格
    // 用 TranslateTransform：纯渲染变换
    // 不触发重新布局，所以能一边跟手一边动
    // ==========================


    private void ApplyDockMakeWay(
        int insertIndex)
    {


        if(insertIndex < 0)
            return;


        // 落点没变就不重新起动画
        // 否则每一帧都会重置一次动画，看起来是抖的

        if(dockMakeWayIndex == insertIndex)
            return;


        dockMakeWayIndex =
            insertIndex;


        int count =
            AppManager.Apps.Count;


        // 让位规则（和原来一字未改）：
        //   插入位之后的图标整体右移一格
        //   插入位之前的图标留在原地
        // 交给公共推挤核心执行，展板那一套用的是同一个核心

        int[] fromSlots =
            new int[count];


        int[] toSlots =
            new int[count];


        for(int i = 0; i < count; i++)
        {


            fromSlots[i] = i;


            toSlots[i] =
                i >= insertIndex
                ? i + 1
                : i;


        }


        // 满员时被挤出去的正好是末尾那一个
        // 先淡下去，预告它要让位

        ApplySlotMakeWay(
            IconPanel,
            AppManager.Apps,
            fromSlots,
            toSlots,
            IconSlotWidth,
            0,
            0,
            DockMakeWayDuration,
            count >= DockCapacity
            ? count - 1
            : -1,
            0.18,
            true);


    }




    // 让位收回：图标滑回原来的槽位
    // animate=false 用在"马上要重建列表"的场合
    // 那种情况容器会被回收，动画没有意义

    private void ClearDockMakeWay(
        bool animate)
    {


        if(dockMakeWayIndex < 0)
            return;


        dockMakeWayIndex = -1;


        // 交给公共推挤核心：位移清零 + 不透明度复位

        ClearSlotMakeWay(
            IconPanel,
            AppManager.Apps,
            animate,
            DockMakeWayDuration,
            true);


    }




    // ==========================
    // 展板让位（网格推挤，和 Dock 共用同一个核心）
    // 两种场景：
    //   1. 展板内部换位：拖动源插到落点那一格
    //      中间的图标一个接一个侧移让位，原地那格交给幽灵图标
    //   2. Dock 拖进来：新图标插进落点那一格
    //      落点及之后的图标整体后移一格
    // 让位期间只动渲染位移，列表 / 数据一律不动
    // 松手那一刻才真正重排 + 回写 JSON
    // ==========================


    // 展板一行能放几个槽位
    // 槽位间距 BoardSlotPitch（图标 80 + 左右各 20 外边距）

    private int ComputeBoardColumns()
    {

        try
        {


            if(BoardPanel == null)
                return 1;


            // 优先用视口宽度：和展板分页用的是同一个口径
            // （BoardPanel 是居中的 WrapPanel，图标少的时候
            //   ActualWidth 会小于视口宽，算出来的列数会偏小）
            // 视口取不到再退回面板自身的宽度

            double width =
                BoardScroll != null &&
                BoardScroll.ViewportWidth > 0
                ? BoardScroll.ViewportWidth
                : BoardPanel.ActualWidth;


            if(width <= 0)
                return 1;


            int columns =
                (int)Math.Floor(
                    width /
                    BoardSlotPitch);


            return columns < 1
                   ? 1
                   : columns;


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板槽位数计算失败: "
                +
                ex.Message);


            return 1;


        }


    }




    // 鼠标落点 → 展板插入槽位（0 ~ boardApps.Count）
    // 全部用 BoardPanel 自己的坐标算，滚动过也不会错位
    // 跨过两格中线才换位：跟手但不跳

    private int ComputeBoardInsertIndex()
    {

        try
        {


            int count =
                boardApps.Count;


            if(count == 0)
                return 0;


            if(BoardPanel == null ||
               BoardPanel.ActualWidth <= 0 ||
               BoardPanel.ActualHeight <= 0)
                return 0;


            int columns =
                ComputeBoardColumns();


            Point point =
                Mouse.GetPosition(
                    BoardPanel);


            int column =
                (int)Math.Floor(
                    point.X /
                    BoardSlotPitch);


            int row =
                (int)Math.Floor(
                    point.Y /
                    BoardSlotPitch);


            if(column < 0)
                column = 0;


            if(column > columns - 1)
                column = columns - 1;


            if(row < 0)
                row = 0;


            int index =
                row * columns + column;


            // 最后一行没填满时，落在空位上也当作"放到末尾"

            if(index > count)
                index = count;


            if(index < 0)
                index = 0;


            return index;


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板落点计算失败: "
                +
                ex.Message);


            return boardApps.Count;


        }


    }




    // 展板让位：把"当前槽位 → 目标槽位"交给公共推挤核心
    // sourceIndex >= 0：展板内部换位（拖动源那一格原地不动，幽灵代表它）
    // sourceIndex < 0 ：新图标插入（落点及之后的整体后移一格）

    private void ApplyBoardMakeWay(
        int insertIndex,
        int sourceIndex)
    {

        try
        {


            if(insertIndex < 0)
                return;


            // 落点没变就不重新起动画
            // 否则每一帧都会重置一次动画，看起来是抖的

            if(boardMakeWayIndex == insertIndex &&
               boardMakeWaySourceIndex == sourceIndex)
                return;


            boardMakeWayIndex =
                insertIndex;


            boardMakeWaySourceIndex =
                sourceIndex;


            int count =
                boardApps.Count;


            if(count <= 0)
                return;


            int columns =
                ComputeBoardColumns();


            int[] fromSlots =
                new int[count];


            int[] toSlots =
                new int[count];


            for(int i = 0; i < count; i++)
            {


                fromSlots[i] = i;


                toSlots[i] = i;


                if(sourceIndex < 0)
                {


                    // 新图标插入：落点及之后的整体后移一格

                    if(i >= insertIndex)
                        toSlots[i] = i + 1;


                    continue;


                }


                // 展板内部换位：
                //   拖动源那一格原地不动（鼠标下面就是它的幽灵）
                //   中间的图标朝拖动源的方向整体让一格

                if(i == sourceIndex)
                    continue;


                if(i > sourceIndex &&
                   i <= insertIndex)
                {

                    toSlots[i] = i - 1;

                }
                else if(i >= insertIndex &&
                        i < sourceIndex)
                {

                    toSlots[i] = i + 1;

                }


            }


            ApplySlotMakeWay(
                BoardPanel,
                boardApps,
                fromSlots,
                toSlots,
                BoardSlotPitch,
                BoardSlotPitch,
                columns,
                BoardMakeWayDuration,
                -1,
                1,
                false);


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板让位失败: "
                +
                ex.Message);


        }


    }




    // 展板让位收回
    // 不碰不透明度：拖动源那一格是半透明的，不能被盖掉

    private void ClearBoardMakeWay(
        bool animate)
    {

        try
        {


            if(boardMakeWayIndex < 0)
                return;


            boardMakeWayIndex = -1;


            ClearSlotMakeWay(
                BoardPanel,
                boardApps,
                animate,
                BoardMakeWayDuration,
                false);


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板让位收回失败: "
                +
                ex.Message);


        }


    }




    // ==========================
    // 展板落位收尾
    // 列表重排 / 插入之后调用
    // 排在 Loaded 优先级，正好在一次布局之后跑
    // 让位是渲染层位移，基准位置变了就得重算
    // ==========================


    private void SettleBoardIconLayout()
    {

        try
        {


            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(
                    SettleBoardIconLayoutNow));


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板收尾失败: "
                +
                ex.Message);


        }


    }




    private void SettleBoardIconLayoutNow()
    {

        try
        {


            // 数据已经挪完，容器基准位置跟着变了
            // 这时候瞬间清零看不出跳变，反而最干净
            // （清零后再重排会闪一下，顺序绝不能反）

            ClearBoardMakeWay(false);


            AnimateBoardSlotArrival();


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板收尾失败: "
                +
                ex.Message);


        }


    }




    // 新落进展板的那一格：小一点 + 半透明 → 原尺寸 + 不透明
    // 和 Dock 的入位动画一个套路

    private void AnimateBoardSlotArrival()
    {

        try
        {


            if(boardArrivalIndex < 0)
                return;


            int index =
                boardArrivalIndex;


            boardArrivalIndex = -1;


            if(index >= boardApps.Count)
                return;


            if(BoardPanel == null)
                return;


            // 容器是布局阶段生成的
            // 这里已经排在 Loaded 之后，再补一次布局保险

            BoardPanel.UpdateLayout();


            FrameworkElement? container =
                FindAppContainer(
                    BoardPanel,
                    boardApps[index]);


            if(container == null)
                return;


            EnsureVisualTransform(
                container);


            // 起手：小一点 + 透明

            SetElementOpacityNow(
                container,
                0.35);


            SetVisualTranslatePosition(
                container,
                0,
                0);


            SetVisualScaleNow(
                container,
                0.72);


            // 收进槽位

            FadeElement(
                container,
                1,
                0,
                BoardSettleDuration,
                EasingMode.EaseOut);


            AnimateVisualTranslateXY(
                container,
                0,
                0,
                BoardSettleDuration,
                EasingMode.EaseOut);


            AnimateVisualScale(
                container,
                1,
                BoardSettleDuration,
                EasingMode.EaseOut);


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板入位动画失败: "
                +
                ex.Message);


        }


    }




    // 立刻设不透明度
    // 让位期间要精确控制，不走动画

    private static void SetElementOpacityNow(
        UIElement? element,
        double opacity)
    {


        if(element == null)
            return;


        element.BeginAnimation(
            UIElement.OpacityProperty,
            null);


        element.Opacity =
            opacity;


    }


    // ==========================
    // 新图标入位
    // 列表刚重建过，容器要等一次布局才有
    // 排到 Loaded 优先级，正好在布局之后跑
    // ==========================


    private void SettleDockIconLayout()
    {


        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(
                AnimateDockSlotArrival));


    }




    // 新落位的那一格：从偏右一点滑进来
    // 其余槽位在容器重建后就天然在最终位置了

    private void AnimateDockSlotArrival()
    {


        int index =
            dockArrivalIndex;


        dockArrivalIndex = -1;


        if(index < 0)
            return;


        if(index >= AppManager.Apps.Count)
            return;


        if(IconPanel == null)
            return;


        // 容器是布局阶段生成的
        // 这里已经排在 Loaded 之后，再补一次布局保险

        IconPanel.UpdateLayout();


        FrameworkElement? container =
            FindAppContainer(
                IconPanel,
                AppManager.Apps[index]);


        if(container == null)
            return;


        EnsureVisualTransform(
            container);


        // 起手：偏右 + 小一点 + 透明

        SetElementOpacityNow(
            container,
            0);


        SetVisualTranslatePosition(
            container,
            IconSlotWidth * 0.55,
            0);


        SetVisualScaleNow(
            container,
            0.72);


        // 收进槽位

        FadeElement(
            container,
            1,
            0,
            DockSettleDuration,
            EasingMode.EaseOut);


        AnimateVisualTranslateXY(
            container,
            0,
            0,
            DockSettleDuration,
            EasingMode.EaseOut);


        AnimateVisualScale(
            container,
            1,
            DockSettleDuration,
            EasingMode.EaseOut);


    }




    // 收纳盒的入盒点
    // 展板开着就飞展板，没开就飞"展板按钮"
    // 两个都拿不到（比如整体不在屏幕上）就返回空

    private Point? GetBoardEntryPoint()
    {


        Rect rect =
            GetElementRectInRoot(
                isLaunchBoardOpen
                ? LaunchBoard
                : null);


        if(!rect.IsEmpty)
        {


            // 展板下沿偏里一点：看着像"收进盒子"
            // 而不是穿过整个展板再消失

            return
                new Point(
                    rect.X + rect.Width / 2,
                    rect.Y + rect.Height - rect.Height * 0.18);


        }


        Rect button =
            GetElementRectInRoot(
                LaunchButton);


        if(button.IsEmpty)
            return null;


        return
            new Point(
                button.X + button.Width / 2,
                button.Y + button.Height / 2);


    }




    // ==========================
    // 挤出动画
    // Dock 满员时被淘汰的那个图标
    // 原地缩小 + 飞向收纳盒，最后淡出
    // 数据上它早就进收纳盒了，这里只补视觉
    // ==========================


    private void PlayDockEjectFlight(
        ImageSource? icon,
        FrameworkElement? fromContainer)
    {


        if(DragOverlay == null)
            return;


        Rect from =
            GetElementRectInRoot(
                fromContainer);


        if(from.IsEmpty)
            return;


        Grid flyer =
            BuildAppVisual(
                icon,
                DockIconVisualSize,
                DockIconVisualIconSize);


        EnsureVisualTransform(
            flyer);


        DragOverlay.Children.Add(
            flyer);


        double startX =
            from.X +
            (from.Width - DockIconVisualSize) / 2;


        double startY =
            from.Y +
            (from.Height - DockIconVisualSize) / 2;


        SetVisualTranslatePosition(
            flyer,
            startX,
            startY);


        Point? target =
            GetBoardEntryPoint();


        if(target == null)
        {


            // 展板看不见，原地缩小淡出

            AnimateVisualScale(
                flyer,
                0.5,
                DockEjectDuration,
                EasingMode.EaseOut);


            FadeAndRemove(
                flyer,
                0,
                DockEjectDuration,
                0);


            return;


        }


        double endX =
            target.Value.X -
            DockIconVisualSize / 2;


        double endY =
            target.Value.Y -
            DockIconVisualSize / 2;


        // 飞过去 + 变小
        // 淡出晚一点起步，让人看清它去哪了

        AnimateVisualTranslateXY(
            flyer,
            endX,
            endY,
            DockEjectDuration,
            EasingMode.EaseInOut);


        AnimateVisualScale(
            flyer,
            0.42,
            DockEjectDuration,
            EasingMode.EaseOut);


        FadeAndRemove(
            flyer,
            0,
            DockEjectDuration * 0.5,
            DockEjectDuration * 0.5);


    }




    // ==========================
    // 展板 → Dock：按落点插进去
    // 1. 新图标进 Dock 的指定槽位，后面的整体右移
    // 2. Dock 满员就把末尾那个挤进收纳盒
    //    先放收纳盒再从 Dock 摘，绝不会丢
    // 3. 被挤出的那个飞回收纳盒，纯视觉
    // ==========================


    private void DropBoardAppToDockAt(
        AppItem? app,
        string? path,
        int insertIndex)
    {


        if(app == null)
            return;


        int boardIndex =
            boardApps.IndexOf(
                app);


        if(boardIndex < 0)
            return;


        // 放回 Dock 要的是原始文件路径

        string source =
            string.IsNullOrWhiteSpace(path)
            ? ""
            : path;


        if(!IsBoardAcceptable(source) &&
           boardIndex < boardSourcePaths.Count)
        {


            source =
                boardSourcePaths[boardIndex];


        }


        if(!IsBoardAcceptable(source))
        {


            ShowBoardStatus(
                "找不到这个应用的原始路径，放不回 Dock");


            return;


        }


        // Dock 满员：先确认末尾那个能进收纳盒
        // 进不去就整个放弃，不能让应用凭空消失

        AppItem? eject =
            AppManager.Apps.Count >= DockCapacity &&
            AppManager.Apps.Count > 0
            ? AppManager.Apps[AppManager.Apps.Count - 1]
            : null;


        if(eject != null &&
           !IsBoardAcceptable(eject.LaunchPath))
        {


            ShowBoardStatus(
                "Dock 已满，末尾的应用存不进收纳盒");


            return;


        }


        // 被挤出的那个：记住它的槽位和图标
        // 列表一刷新这两个引用就没了

        FrameworkElement? ejectContainer =
            eject == null
            ? null
            : FindAppContainer(
                IconPanel,
                eject);


        ImageSource? ejectIcon =
            FindAppIconSource(
                ejectContainer);


        // 让位是渲染层位移
        // 数据一变容器就重建，先把它清干净

        ClearDockMakeWay(
            false);


        // 新图标进 Dock
        // AddApp 会重新生成 AppItem 并落盘

        int before =
            AppManager.Apps.Count;


        AddAppToDock(
            source);


        if(AppManager.Apps.Count <= before)
        {


            ShowBoardStatus(
                "Dock 里已经有这个应用了");


            return;


        }


        int last =
            AppManager.Apps.Count - 1;


        if(insertIndex < 0)
            insertIndex = 0;


        if(insertIndex > last)
            insertIndex = last;


        // 挪到落点槽位
        // 后面的图标自然整体右移一格

        AppManager.Apps.Move(
            last,
            insertIndex);


        dockArrivalIndex =
            insertIndex;


        // 被挤出的那个本尊先隐身
        // 后面交给飞行的替身，不会同时看到两个

        if(ejectContainer != null)
            SetElementOpacityNow(
                ejectContainer,
                0);


        if(eject == null)
        {


            // 没满员：数量变了，宽度要跟着动

            DockStorage.Save();


        }
        else
        {


            // 末尾那个进收纳盒（先放后摘，不会丢）

            if(!BoardHasLaunchPath(eject.LaunchPath))
            {


                boardApps.Add(eject);


                boardSourcePaths.Add(eject.LaunchPath);


            }


            AppManager.RemoveApp(eject);


            PlayDockEjectFlight(
                ejectIcon,
                ejectContainer);


        }


        // 收纳盒里摘掉它，交给 Dock

        boardApps.RemoveAt(
            boardIndex);


        if(boardIndex < boardSourcePaths.Count)
        {


            boardSourcePaths.RemoveAt(
                boardIndex);


        }


        SaveBoardApps();


        RefreshIcons();


        ShowDock();


        SettleDockIconLayout();


        ShowBoardStatus(
            "已放回 Dock："
            +
            app.Name);


    }




    // ==========================
    // Dock 自动补位
    // 图标被移除后空出来的位置
    // 从收纳盒拉一个进来填上（优先最早的）
    // 补成功了会顺带刷新 + 落位动画
    // ==========================


    private void RefillDockFromBoard()
    {


        if(AppManager.Apps.Count >= DockCapacity)
            return;


        for(int i = 0;
            i < boardApps.Count;
            i++)
        {


            AppItem candidate =
                boardApps[i];


            string source =
                i < boardSourcePaths.Count
                ? boardSourcePaths[i]
                : "";


            if(!IsBoardAcceptable(source))
            {


                source =
                    candidate.LaunchPath;


            }


            if(!IsBoardAcceptable(source))
                continue;


            // 先试着加进 Dock
            // 加不进去（重复 / 认不出来）就换下一个
            // 这样收纳盒里那个不会丢

            int before =
                AppManager.Apps.Count;


            AddAppToDock(
                source);


            if(AppManager.Apps.Count <= before)
                continue;


            // 进 Dock 成功，收纳盒里摘掉它

            boardApps.RemoveAt(
                i);


            if(i < boardSourcePaths.Count)
            {


                boardSourcePaths.RemoveAt(
                    i);


            }


            SaveBoardApps();


            dockArrivalIndex =
                AppManager.Apps.Count - 1;


            RefreshIcons();


            ShowDock();


            SettleDockIconLayout();


            ShowBoardStatus(
                "已从展板补位："
                +
                candidate.Name);


            return;


        }


    }




    // ==========================
    // 展板图标：拖动判定
    // 按住图标拖出展板 = 放回 Dock
    // ==========================


    // 任意一次左键按下
    // 先把展板的拖动状态清干净
    // 免得上一次手势的残留标记误触发

    private void BoardDrag_Press(
        object sender,
        MouseButtonEventArgs e)
    {


        // 上一次手势可能被系统打断（丢了捕获 / 焦点切走）
        // 新的按下说明旧手势已经结束
        // 先把残留的拖拽状态收干净，免得幽灵卡在屏幕上

        if(appDragActive)
            CancelAppDrag();


        boardDragArmed = false;


        boardDragStarted = false;


        // 一次新的按下 = 上一次手势已经结束
        // 拖动后的"抑制启动"标记到这里就该失效
        // 万一上次松手事件丢了（Esc 取消拖动也会丢）
        // 也不会吃掉这一次的点击

        boardIconDragSuppressClick = false;


    }



    // 展板图标按下：记住是哪一个

    private void BoardIcon_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {


        boardIconDragSuppressClick = false;


        // 批量模式：点击图标只切换选中状态
        // 这里不武装拖动，避免把“选择”误拖成排序 / 跨栏移动
        // 普通模式完全照旧：下面原有的拖动逻辑一行没改

        if(isBoardBatchMode)
        {

            boardDragSource = null;

            boardDragArmed = false;

            boardDragStarted = false;

            return;

        }


        boardDragSource =
            GetBoardApp(sender);


        boardDragArmed =
            boardDragSource != null;


        boardDragStarted = false;


        // 屏幕坐标：展板弹出时窗口矩形会变

        boardDragStartPoint =
            PointToScreen(
                e.GetPosition(
                    this));


    }



    // 展板上的移动
    // 超过系统阈值就当拖动，交给 DoDragDrop
    // 松手前不会启动应用

    private void BoardDrag_Move(
        object sender,
        MouseEventArgs e)
    {


        if(!boardDragArmed)
            return;


        if(e.LeftButton !=
            MouseButtonState.Pressed)
        {

            boardDragArmed = false;

            return;

        }


        AppItem? app =
            boardDragSource;


        if(boardDragStarted ||
           app == null)
            return;


        Point now =
            PointToScreen(
                e.GetPosition(
                    this));


        double dx =
            now.X -
            boardDragStartPoint.X;


        double dy =
            now.Y -
            boardDragStartPoint.Y;


        // 没超过系统阈值：还是点击
        // 交给展板图标的左键启动

        if(Math.Abs(dx) <
                SystemParameters.MinimumHorizontalDragDistance &&
           Math.Abs(dy) <
                SystemParameters.MinimumVerticalDragDistance)
        {

            return;

        }


        boardDragStarted = true;


        boardDragArmed = false;


        // 本次手势是拖动
        // 松手后不再启动应用

        boardIconDragSuppressClick = true;


        // 拖出展板时要用的原始文件路径

        string path =
            "";


        int index =
            boardApps.IndexOf(app);


        if(index >= 0 &&
           index < boardSourcePaths.Count)
        {

            path =
                boardSourcePaths[index];

        }


        StartAppDragVisual(
            app,
            AppDragFromBoard,
            path);


        boardDragStarted = false;


    }



    private void RefreshIcons()
    {


        // Dock 要重建了：正显示的气泡立刻收掉
        // 图标被删 / 被挪走时 MouseLeave 不一定来
        // 不收的话气泡会僵在原地

        DismissIconTip();


        IconPanel.ItemsSource = null;



        IconPanel.ItemsSource =
            AppManager.Apps;



    }












    // ==========================
    // 左键打开
    // ==========================


    private void Icon_Click(
        object sender,
        MouseButtonEventArgs e)
    {


        e.Handled = true;


        // 刚刚发生的是拖动
        // 本次松手不启动应用

        if(dockDragSuppressClick)
        {

            dockDragSuppressClick = false;


            return;

        }







        FrameworkElement element =
            sender as FrameworkElement;







        if(element == null)
            return;







        AppItem app =
            element.DataContext as AppItem;







        if(app == null)
            return;







        // 统一交给 Windows 外壳启动
        // 不判断后缀：.txt 用记事本 / .pdf 用阅读器 / 文件夹用资源管理器 / .url 用浏览器 / .exe 直接运行

        OpenByShell(
            app.LaunchPath);



    }












    // ==========================
    // 右键删除
    // ==========================


    private void Icon_Delete(
        object sender,
        MouseButtonEventArgs e)
    {


        e.Handled = true;







        FrameworkElement element =
            sender as FrameworkElement;







        if(element == null)
            return;







        AppItem app =
            element.DataContext as AppItem;







        if(app == null)
            return;









        // 【本次修复】弹框前打标记、弹完清标记
        // Dock 图标右键同样会弹确认框，也会抢走焦点
        // 标记期间"点外部关闭"逻辑一律不动手，展板保持打开
        // 用 try / finally：点"是"、"否"还是直接关掉框，标记都会被清干净

        MessageBoxResult result;

        isRightClickMenuOpen = true;

        try
        {

            result =
                MessageBox.Show(
                    "是否移除该 Dock 图标？\n\n不会删除电脑中的文件。",
                    "AeroDock",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

        }
        finally
        {

            isRightClickMenuOpen = false;

        }








        if(result ==
            MessageBoxResult.Yes)
        {

            // 桌面图标处理模式 = 收纳至 Dock 时：
            // 先把收纳目录里的源文件用 File.Move 搬回桌面
            // 搬不回去就保留这个图标，绝不 File.Delete
            // 模式A 下这个方法什么都不做，直接返回 true

            if(!TryMoveBackToDesktop(app))
                return;


            AppManager.RemoveApp(app);



            RefreshIcons();



            // 空出来的位置：从收纳盒自动补一个进来
            // 补位成功会自己再刷一次图标 + 做落位动画

            RefillDockFromBoard();


        }







        ShowDock();



    }












    // ==========================
    // 图标放大
    // ==========================


    private void Icon_MouseEnter(
        object sender,
        MouseEventArgs e)
    {


        Border border =
            sender as Border;







        if(border == null)
            return;







        ScaleTransform scale =
            new ScaleTransform(
                1,
                1);







        border.RenderTransform =
            scale;







        DoubleAnimation animation =
            new DoubleAnimation
            {


                To = 1.25,



                Duration =
                TimeSpan.FromMilliseconds(180),





                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseOut

                }



            };







        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            animation);






        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            animation);



    }












    private void Icon_MouseLeave(
        object sender,
        MouseEventArgs e)
    {


        Border border =
            sender as Border;






        if(border == null)
            return;







        ScaleTransform scale =
            new ScaleTransform(
                1.25,
                1.25);







        border.RenderTransform =
            scale;







        DoubleAnimation animation =
            new DoubleAnimation
            {


                To = 1,



                Duration =
                TimeSpan.FromMilliseconds(180),





                EasingFunction =
                new CubicEase
                {

                    EasingMode =
                    EasingMode.EaseOut

                }



            };







        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            animation);






        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            animation);



    }


    // ==========================
    // 图标悬停名称气泡（自定义 ToolTip）
    // ==========================
    // Dock 图标与展板图标共用这一套
    // 不用 WPF 默认 ToolTip：外观、动画、延迟全部自己控制
    // ==========================


    // 悬停多久才弹出气泡(毫秒)
    // 鼠标快速划过时根本来不及弹，所以不会闪

    private const double IconTipShowDelay = 500;


    // 气泡刚刚还亮着时，滑到隔壁图标只等这么久(毫秒)
    // 既跟手，又不会疯狂闪烁

    private const double IconTipSwitchDelay = 140;


    // 距上一次点亮气泡多久算“气泡还亮着”(毫秒)

    private const double IconTipChainWindow = 1500;


    // 淡入 / 淡出时长(毫秒)

    private const double IconTipFadeInDuration = 150;

    private const double IconTipFadeOutDuration = 120;


    // 气泡与图标之间的空隙(像素)

    private const double IconTipGap = 9;


    // 气泡离窗口边缘的安全距离(像素)

    private const double IconTipEdge = 4;


    // 气泡宽度的设计上限（和 XAML 里 MaxWidth="320" 对应）
    // XAML 读不到的时候用这个兜底

    private const double IconTipDefaultMaxWidth = 320;


    // 为了"绝对居中"最多能把气泡收多窄
    // 比这还窄就读不出字了：宁可退回设计上限，让后面的左右夹兜底

    private const double IconTipMinWidth = 170;


    // 设计上限的缓存值：只从 XAML 读一次
    // 后面每次都从这个基准往下限
    // 否则第二次会拿上一次收紧过的值再收紧，气泡越缩越小

    private double iconTipBaseMaxWidth = -1;


    // 延迟计时器
    // MouseEnter 开始计时，MouseLeave 立刻取消

    private DispatcherTimer? iconTipTimer = null;


    // 正在等待 / 正在显示气泡的那个图标

    private FrameworkElement? iconTipTarget = null;


    // 上一次真正把气泡点亮的时刻

    private DateTime iconTipLastShown = DateTime.MinValue;


    // 气泡当前是否亮着

    private bool iconTipVisible = false;


    // 气泡的“代”号：每显示一次 +1
    // 淡出动画结束时要对比它
    // 防止把新一代气泡的淡入动画误清掉

    private int iconTipGeneration = 0;


    // 已经为哪一代排过"显示之后再对一次位"
    // 每代只排一次，重复调用不会排一堆回调

    private int iconTipResyncGeneration = -1;



    // ==========================
    // Dock 图标悬停入口
    // 原来挂在 MouseEnter / MouseLeave 上的逻辑先原样跑
    // 再补气泡：原有行为一个字节都没改
    // ==========================

    private void Icon_MouseEnterWithTip(
        object sender,
        MouseEventArgs e)
    {

        Icon_MouseEnter(
            sender,
            e);


        QueueIconTip(
            sender);

    }



    private void Icon_MouseLeaveWithTip(
        object sender,
        MouseEventArgs e)
    {

        HideIconTip(
            sender);


        Icon_MouseLeave(
            sender,
            e);

    }



    // ==========================
    // 展板图标悬停入口
    // 同上：原逻辑照跑，只是多挂一个气泡
    // ==========================

    private void BoardIcon_MouseEnterWithTip(
        object sender,
        MouseEventArgs e)
    {

        BoardIcon_MouseEnter(
            sender,
            e);


        // 批量模式下不弹应用名称气泡
        // 鼠标是用来勾选的，气泡会挡住右上角的选择标记

        if(!isBoardBatchMode)
        {

            QueueIconTip(
                sender);

        }

    }



    private void BoardIcon_MouseLeaveWithTip(
        object sender,
        MouseEventArgs e)
    {

        HideIconTip(
            sender);


        BoardIcon_MouseLeave(
            sender,
            e);

    }



    // ==========================
    // 排一个气泡：先等延迟
    // 时间到了鼠标还停在图标上，才真的弹出来
    // ==========================

    private void QueueIconTip(
        object sender)
    {

        FrameworkElement? element =
            sender as FrameworkElement;


        if(element == null)
            return;


        // 没名字又没路径：没东西可显示，不弹

        if(GetIconTipText(element).Length == 0)
        {

            HideIconTip(
                sender);


            return;

        }


        EnsureIconTipTimer();


        if(iconTipTimer == null)
            return;


        // 换图标：上一个撤销，计时器从头开始计

        iconTipTimer.Stop();


        iconTipTarget =
            element;


        // 气泡刚还亮着：只等一小会儿，跟手
        // 隔了很久：老老实实等满 0.5 秒

        bool chained =
            DateTime.UtcNow -
            iconTipLastShown <
            TimeSpan.FromMilliseconds(
                IconTipChainWindow);


        iconTipTimer.Interval =
            TimeSpan.FromMilliseconds(
                chained
                ? IconTipSwitchDelay
                : IconTipShowDelay);


        iconTipTimer.Start();

    }



    private void EnsureIconTipTimer()
    {

        if(iconTipTimer != null)
            return;


        iconTipTimer =
            new DispatcherTimer(
                DispatcherPriority.Normal,
                Dispatcher);


        iconTipTimer.Tick +=
            IconTipTimer_Tick;

    }



    private void StopIconTipTimer()
    {

        if(iconTipTimer == null)
            return;


        iconTipTimer.Stop();

    }



    // ==========================
    // 延迟到点：再确认一次鼠标确实还停在图标上
    // ==========================

    private void IconTipTimer_Tick(
        object? sender,
        EventArgs e)
    {

        StopIconTipTimer();


        FrameworkElement? target =
            iconTipTarget;


        if(target == null)
            return;


        // 图标已经没了（收起圆 / 翻页 / 被删掉）
        // 或者鼠标已经划走：不弹

        if(!target.IsVisible ||
           !target.IsMouseOver)
        {

            iconTipTarget =
                null;


            return;

        }


        // 正按着鼠标拖 Dock：别弹

        if(Mouse.LeftButton ==
           MouseButtonState.Pressed)
        {

            iconTipTarget =
                null;


            return;

        }


        ShowIconTip(
            target);

    }




    // ==========================
    // 鼠标离开：还没弹就取消，已经弹了就淡出
    // ==========================

    private void HideIconTip(
        object sender)
    {

        StopIconTipTimer();


        FrameworkElement? element =
            sender as FrameworkElement;


        // 鼠标只是从图标滑到隔壁图标：
        // 交给下一个图标的 QueueIconTip 接管，这里什么都不动

        if(element != null &&
           !ReferenceEquals(
                element,
                iconTipTarget))
        {

            return;

        }


        iconTipTarget =
            null;


        if(!iconTipVisible)
            return;


        StartIconTipFadeOut();

    }



    // ==========================
    // 真正把气泡点亮
    // 已经亮着就只换字、重新摆位，不重播淡入
    // ==========================

    private void ShowIconTip(
        FrameworkElement target)
    {

        string text =
            GetIconTipText(
                target);


        // 没东西可显示：不弹

        if(text.Length == 0)
        {

            iconTipTarget =
                null;


            return;

        }


        iconTipTarget =
            target;


        if(IconTipText != null)
            IconTipText.Text =
                text;


        // 每次换字都重量一次
        // Measure 之后 DesiredSize 立刻就是新的
        // 不用等下一帧排版，气泡不会先错位再跳回来
        // 量之前先按“图标能不能绝对居中”限一个最大宽度：
        // 宽度没超标，后面的居中摆位就永远用不到左右夹，
        // 也就不会出现"偏左 / 偏右"

        MeasureIconTipForTarget(
            target);


        PositionIconTip(
            target);


        iconTipVisible =
            true;


        // 每点亮一次换一代
        // 上一次淡出的收尾回调看到代号变了会自己放弃
        // 不会误关这一代的气泡

        iconTipGeneration++;


        iconTipLastShown =
            DateTime.UtcNow;


        StartIconTipFadeIn();


        // ==========================
        // 首次悬停错位修复：显示之后再对一次位
        // ==========================
        // 上面这一次摆位，用的是"当前这一帧"的排版结果。
        // 第一次悬停时气泡刚换上文字、刚改过 MaxWidth，
        // 真实宽高要等这一帧的排版跑完才准，
        // 拿旧宽高算 x = 图标中心 - 气泡宽 / 2 就会偏左 / 偏右；
        // 第二次悬停时上一帧早就排完版了，所以看起来"第二次才对"。
        // 这里排在渲染之前用真实宽高再对一次，眼睛看不到这一步。

        QueueIconTipResync(
            target);

    }



    // ==========================
    // 显示完之后再对一次位（首次悬停错位的修复）
    // ==========================
    // 排队在 DispatcherPriority.Render：
    // 那时这一帧的排版一定已经跑完，
    // 用真实 ActualWidth 算出来的居中是对的，
    // 而这一帧用户还没看到，所以不会有任何跳动。
    // 顺便也把"窗口刚改过宽度、图标坐标还是旧的"这种情况兜住。
    // 只在气泡还亮着、还是同一个目标、代号没变时才动手，
    // 不会影响收起圆 / 翻页 / 点别处等原有逻辑。

    private void QueueIconTipResync(
        FrameworkElement target)
    {

        if(target == null)
            return;


        int generation =
            iconTipGeneration;


        // 这一代已经排过就不用再排

        if(iconTipResyncGeneration ==
           generation)
            return;


        iconTipResyncGeneration =
            generation;


        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(
                () =>
                {

                    // 这一代已经作废（收起圆 / 翻页 / 点别处）
                    // 或者气泡已经收掉、目标已经换了：什么都不做

                    if(generation !=
                       iconTipGeneration)
                        return;


                    if(!iconTipVisible)
                        return;


                    if(!ReferenceEquals(
                        iconTipTarget,
                        target))
                        return;


                    if(!target.IsVisible)
                        return;


                    // 先强制排版，再按真实宽高重新居中

                    MeasureIconTipForTarget(
                        target);


                    PositionIconTip(
                        target);

                }));

    }



    // ==========================
    // 气泡排版完成：真实宽高到手的这一刻
    // ==========================
    // SizeChanged 是在排版过程里发的，
    // 所以这里只排一次"渲染前重摆"，不在排版里直接动手，
    // 免得又触发一轮布局。
    // 文字很短、宽高跟上一次一模一样时不会发这个事件，
    // 那种情况已经由 UpdateLayout + QueueIconTipResync 兜住了。

    private void IconTip_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {

        if(!iconTipVisible)
            return;


        FrameworkElement? target =
            iconTipTarget;


        if(target == null)
            return;


        QueueIconTipResync(
            target);

    }



    // ==========================
    // 把气泡摆到图标正上方居中
    // 上方空间不够就翻到图标下面
    // 左右夹在窗口里，绝不被裁掉
    // ==========================

    private void PositionIconTip(
        FrameworkElement target)
    {

        if(IconTipLayer == null ||
           IconTip == null ||
           !IconTipLayer.IsVisible)
        {

            return;

        }


        // 用真实宽高：
        // 排版跑完就是 ActualWidth / ActualHeight，
        // 万一还没排（Actual 还是 0）才退回 DesiredSize

        double tipWidth =
            GetIconTipRealWidth();


        double tipHeight =
            GetIconTipRealHeight();


        // 量不出来就先不摆，免得闪到左上角

        if(tipWidth <= 0 ||
           tipHeight <= 0)
        {

            return;

        }


        double iconWidth =
            target.ActualWidth;


        double iconHeight =
            target.ActualHeight;


        if(iconWidth <= 0 ||
           iconHeight <= 0)
            return;


        // 图标在整窗坐标系里的矩形：取对角线两个角
        // 悬停放大时拿到的是放大后的真实外框

        Point? topLeft =
            target.TranslatePoint(
                new Point(
                    0,
                    0),
                IconTipLayer);


        Point? bottomRight =
            target.TranslatePoint(
                new Point(
                    iconWidth,
                    iconHeight),
                IconTipLayer);


        if(topLeft == null ||
           bottomRight == null)
            return;


        Rect iconRect =
            new Rect(
                topLeft.Value,
                bottomRight.Value);


        // 默认：正上方、水平绝对居中
        // 居中线 = 图标水平中心：x = 图标中心 - 气泡宽 / 2
        // 中心优先取"布局中心"（悬停放大是绕图标中心缩放的，
        // 布局中心不跟着跑，比放大后的外框中心更稳）
        // 最后取整：气泡中心线与图标中心线落在同一个像素上，
        // 不会因为半像素误差看起来偏了半格

        double iconCenterX =
            GetIconTipTargetCenterX(
                target);


        if(iconCenterX <= 0)
            iconCenterX =
                iconRect.Left +
                (iconRect.Width / 2);


        double x =
            Math.Round(
                iconCenterX -
                (tipWidth / 2));


        double y =
            Math.Round(
                iconRect.Top -
                IconTipGap -
                tipHeight);


        // 上面放不下：翻到图标下面

        if(y <
           GetIconTipTopMargin())
        {

            double below =
                iconRect.Bottom +
                IconTipGap;


            // 下面也挤不下就别翻了
            // 宁可压在图标上方，也不要盖住图标本身

            if(below + tipHeight <=
               IconTipLayer.ActualHeight)
            {

                y =
                    below;

            }

        }


        // 左右夹住，不越界
        // 气泡层宽度读不到（还没排版）就先不夹：
        // 硬夹会按"层宽 = 0"来算，把气泡顶到最左边，
        // 那比稍微偏一像素难看得多

        double layerWidth =
            GetIconTipLayerWidth();


        if(layerWidth > 0)
        {

            double minX =
                IconTipEdge;


            double maxX =
                layerWidth -
                tipWidth -
                IconTipEdge;


            if(maxX < minX)
                maxX =
                    minX;


            if(x > maxX)
                x =
                    maxX;


            if(x < minX)
                x =
                    minX;

        }


        Canvas.SetLeft(
            IconTip,
            x);


        Canvas.SetTop(
            IconTip,
            y);

    }



    // ==========================
    // 窗口顶部的安全高度
    // 窗口贴着屏幕顶边时，最上面一小段是系统死区
    // 气泡留点余量，免得被压掉一截
    // ==========================

    private double GetIconTipTopMargin()
    {

        double margin =
            IconTipEdge;


        if(Top <= 0)
            margin += 10;


        return margin;

    }



    // ==========================
    // 气泡限宽：保证"绝对居中"一定摆得下
    // ==========================
    // 居中的数学很简单：x = 图标水平中心 - 气泡宽 / 2
    // 只要气泡宽 ≤ 图标中心到窗口左右边的距离的两倍，
    // 这个 x 就天然落在 [IconTipEdge, 层宽 - 气泡宽 - IconTipEdge] 里，
    // 一个像素都不用左右夹，也就永远不会"偏左 / 偏右"
    // 所以量的这一步先按这个上限收紧 MaxWidth
    // 只有图标几乎贴着窗口边（两边都没空间）时才放宽，
    // 退回设计上限，交给后面的左右夹兜底

    private void MeasureIconTipForTarget(
        FrameworkElement target)
    {

        if(IconTip == null)
            return;


        // 设计上限只从 XAML 读一次，后面都从这个基准算

        if(iconTipBaseMaxWidth <= 0)
        {

            double design =
                IconTip.MaxWidth;


            iconTipBaseMaxWidth =
                (double.IsNaN(design) ||
                 double.IsInfinity(design) ||
                 design <= 0)
                ? IconTipDefaultMaxWidth
                : design;

        }


        double limit =
            iconTipBaseMaxWidth;


        double centerX =
            GetIconTipTargetCenterX(
                target);


        double layerAvailWidth =
            GetIconTipLayerWidth();


        if(centerX > 0 &&
           layerAvailWidth > 0)
        {

            double layerWidth =
                layerAvailWidth;


            double leftRoom =
                centerX -
                IconTipEdge;


            double rightRoom =
                layerWidth -
                IconTipEdge -
                centerX;


            double room =
                Math.Min(
                    leftRoom,
                    rightRoom);


            if(room > 0 &&
               (room * 2) < limit)
            {

                limit =
                    room * 2;

            }

        }


        // 收得太狠就读不出字了：
        // 宁可让它偏一点，也不能只剩省略号

        if(limit < IconTipMinWidth)
            limit =
                iconTipBaseMaxWidth;


        IconTip.MaxWidth =
            limit;


        IconTip.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));


        // ==========================
        // 关键修复：显示之前，先把这一帧的排版跑完
        // ==========================
        // 换了字、改过 MaxWidth 之后，
        // 气泡真正的宽高要等排版（Measure + Arrange）跑完才是准的。
        // 不跑这一步，PositionIconTip 读到的还是上一次的旧宽高，
        // 算出来的 x 就会偏左 / 偏右（第一次悬停最明显）。
        // UpdateLayout 会把树上所有待排版的元素一次跑完，
        // 所以图标坐标和气泡宽高都是最新值。

        IconTip.UpdateLayout();

    }


    // ==========================
    // 图标水平中心（气泡层坐标系）
    // ==========================
    // 取"布局宽度的一半"再翻译坐标：
    // 悬停放大是绕图标中心缩放的，中心点不跟着跑，
    // 所以这里比用放大后的外框中心更稳
    // 取不到（还没排版 / 不在同一棵树）就返回 0，调用方自己兜底

    private double GetIconTipTargetCenterX(
        FrameworkElement target)
    {

        if(target == null ||
           IconTipLayer == null)
            return 0;


        double width =
            target.ActualWidth;


        if(width <= 0)
            return 0;


        try
        {

            Point center =
                target.TranslatePoint(
                    new Point(
                        width / 2,
                        0),
                    IconTipLayer);


            return center.X;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "取图标水平中心失败: "
                +
                ex.Message);


            return 0;

        }

    }



    // ==========================
    // 气泡的真实宽度 / 高度
    // ==========================
    // 排版跑完之后用 ActualWidth / ActualHeight
    // （这是它真正占据的位置，居中必须用它），
    // 万一还没排（Actual 还是 0）才退回 DesiredSize。
    // 两个都读不出来就返回 0，调用方自己放弃这次摆位。

    private double GetIconTipRealWidth()
    {

        if(IconTip == null)
            return 0;


        double width =
            IconTip.ActualWidth;


        if(double.IsNaN(width) ||
           width <= 0)
        {

            width =
                IconTip.DesiredSize.Width;

        }


        if(double.IsNaN(width) ||
           width <= 0)
            return 0;


        return width;

    }



    private double GetIconTipRealHeight()
    {

        if(IconTip == null)
            return 0;


        double height =
            IconTip.ActualHeight;


        if(double.IsNaN(height) ||
           height <= 0)
        {

            height =
                IconTip.DesiredSize.Height;

        }


        if(double.IsNaN(height) ||
           height <= 0)
            return 0;


        return height;

    }



    // ==========================
    // 气泡层的真实宽度
    // ==========================
    // 正常情况下就是窗口客户区宽度。
    // 万一还没排好版（ActualWidth = 0），
    // 退回根 Grid 的宽度，
    // 绝不让左右夹按"层宽 = 0"把气泡顶到最左边。

    private double GetIconTipLayerWidth()
    {

        if(IconTipLayer != null &&
           IconTipLayer.ActualWidth > 0)
            return IconTipLayer.ActualWidth;


        if(RootGrid != null &&
           RootGrid.ActualWidth > 0)
            return RootGrid.ActualWidth;


        return 0;

    }



    // ==========================
    // 淡入：透明度 0 → 1，加一点点放大
    // 锚点在底边中心，看起来是从图标里长出来的
    // ==========================

    private void StartIconTipFadeIn()
    {

        Duration span =
            TimeSpan.FromMilliseconds(
                IconTipFadeInDuration);


        double from =
            IconTip.Opacity;


        if(from >= 0.99)
        {

            // 已经全亮：只把可能还在跑的淡出动画清掉
            // 不重播 0 → 1，否则中间会闪一下

            IconTip.BeginAnimation(
                UIElement.OpacityProperty,
                null);


            IconTip.Opacity =
                1;

        }
        else
        {

            DoubleAnimation fade =
                new DoubleAnimation(
                    from,
                    1,
                    span);


            fade.EasingFunction =
                new CubicEase
                {

                    EasingMode =
                        EasingMode.EaseOut

                };


            IconTip.BeginAnimation(
                UIElement.OpacityProperty,
                fade);

        }


        // 缩放跟着一起长回来

        if(IconTipScale != null)
        {

            DoubleAnimation grow =
                new DoubleAnimation(
                    0.94,
                    1,
                    span);


            grow.EasingFunction =
                new CubicEase
                {

                    EasingMode =
                        EasingMode.EaseOut

                };


            IconTipScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                grow);


            IconTipScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                grow);

        }

    }



    // ==========================
    // 淡出：动画跑完才真正收掉
    // ==========================

    private void StartIconTipFadeOut()
    {

        int generation =
            iconTipGeneration;


        DoubleAnimation fade =
            new DoubleAnimation(
                1,
                0,
                TimeSpan.FromMilliseconds(
                    IconTipFadeOutDuration));


        fade.EasingFunction =
            new CubicEase
            {

                EasingMode =
                    EasingMode.EaseIn

            };


        fade.Completed +=
            (s, e) =>
            {

                IconTipFadeOut_Completed(
                    generation);

            };


        IconTip.BeginAnimation(
            UIElement.OpacityProperty,
            fade);

    }



    // ==========================
    // 淡出收尾
    // 这段时间里没有别的地方重新点亮，才真的收掉
    // ==========================

    private void IconTipFadeOut_Completed(
        int generation)
    {

        // 这段时间里气泡又被点亮了：代号变了
        // 这一代不归我管，直接放手
        // 否则会把新的淡入动画清掉，气泡就再也亮不起来

        if(generation !=
           iconTipGeneration)
            return;


        iconTipVisible =
            false;


        if(IconTip == null)
            return;


        IconTip.BeginAnimation(
            UIElement.OpacityProperty,
            null);


        IconTip.Opacity =
            0;

    }



    // ==========================
    // 立刻收掉气泡：收起圆、翻页、点别处都调它
    // ==========================

    private void DismissIconTip()
    {

        StopIconTipTimer();


        iconTipTarget =
            null;


        iconTipVisible =
            false;


        // 这一代作废
        // 还在跑的淡出收尾回调看到代号变了会自己放弃

        iconTipGeneration++;


        if(IconTip == null)
            return;


        IconTip.BeginAnimation(
            UIElement.OpacityProperty,
            null);


        IconTip.Opacity =
            0;

    }



    // 展板里滚轮翻页时气泡必须马上消失
    // 否则它会僵在原地

    private void IconTip_MouseWheelDismiss(
        object sender,
        MouseWheelEventArgs e)
    {

        DismissIconTip();

    }



    // ==========================
    // 气泡里写什么字
    //   1) 中文优先：GetDisplayName 拿到的中文名（"微信"）
    //   2) 系统给的友好名字（.lnk 友好名 / 文件描述 / 资源管理器显示名）
    //   3) AppItem.Name（应用名 / 文件名）
    //   4) 按路径推出来的名字
    //   5) 再不行就拿启动路径本身顶上
    // 全程只读：只算名字，不碰图标 / 拖拽 / 任何文件
    // ==========================

    private static string GetIconTipText(
        FrameworkElement element)
    {

        if(element.DataContext
            is AppItem app)
        {

            // 1) 中文优先：
            //    GetDisplayName 会依次问
            //      .lnk 的友好名称 / 快捷方式自己的名字
            //      目标程序的 FileDescription / ProductName
            //      系统里指向这个程序的中文快捷方式（微信.lnk）
            //    只要其中有一条是中文，就显示中文（微信），不是 weixin
            // 只读不改，不动图标、不动快捷方式文件本身

            string display =
                GetDisplayName(
                    app.LaunchPath);


            if(display.Length > 0 &&
               ContainsIconTipChinese(display))
                return display;


            // 2) 系统给的友好名字（可能是英文，但比文件名像人话）

            string friendly =
                StripIconTipShortcutSuffix(
                    GetIconTipFriendlyName(
                        app.LaunchPath));


            if(friendly.Length > 0)
                return friendly;


            // 3) AppManager 给的名字（AppX / 本地化名字）

            string name =
                (app.Name ??
                 "").Trim();


            if(name.Length > 0)
                return name;


            // 4) 最后才是文件名兜底

            if(display.Length > 0)
                return display;


            return (app.LaunchPath ??
                    "").Trim();

        }


        return "";

    }



    // ==========================
    // 悬停气泡的"人话名字"
    // ==========================
    // 目标：拖进来的中文快捷方式显示"微信"，不是 weixin
    // 做法：按优先级收集候选，再从里面挑最好的一个
    //   1. .lnk 的 FriendlyName（快捷方式描述 / 友好名称）
    //   2. 快捷方式自己的文件名（微信.lnk → 微信）
    //   3. 快捷方式在资源管理器里显示的名字（shell 的本地化名）
    //   4. 目标程序的文件描述 FileVersionInfo.FileDescription / 产品名
    //   5. 目标程序在资源管理器里显示的名字
    //   6. 系统里指向这个程序的中文快捷方式
    //      （Weixin.exe 自己不带中文，靠"微信.lnk"把名字捞回来）
    //   7. 文件名本身（去掉扩展名，等价于老逻辑）
    // 候选里只要有中文，就优先用中文那条
    // 全程 try-catch：任何一步失败都只是少一条候选，
    // 绝不弹窗、绝不崩，也绝不改变图标本身
    // 结果按路径缓存：同一个图标第二次悬停不再走这套慢 API

    private static readonly object iconTipNameCacheLock =
        new object();


    private static readonly Dictionary<string,string> iconTipNameCache =
        new Dictionary<string,string>(
            StringComparer.OrdinalIgnoreCase);


    private static string GetIconTipFriendlyName(
        string? path)
    {

        string key =
            (path ?? "").Trim();


        if(key.Length == 0)
            return "";


        lock(iconTipNameCacheLock)
        {

            // out 用可空类型接：Dictionary.TryGetValue 的
            // MaybeNullWhen(false) 注解不会让编译器报 CS8600

            if(iconTipNameCache.TryGetValue(
                key,
                out string? cached))
                return cached ??
                       "";

        }


        string name =
            ResolveIconTipFriendlyName(
                key);


        lock(iconTipNameCacheLock)
        {

            iconTipNameCache[key] =
                name;

        }


        return name;

    }


    private static string ResolveIconTipFriendlyName(
        string path)
    {

        try
        {

            // 路径不存在：一个候选都拿不出来，让调用方退回 AppItem.Name

            if(!File.Exists(path) &&
               !Directory.Exists(path))
                return "";


            List<string> candidates =
                new List<string>();


            string extension =
                Path.GetExtension(
                    path);


            if(extension.Equals(
                ".lnk",
                StringComparison.OrdinalIgnoreCase))
            {

                string friendly;
                string target;


                ReadIconTipShortcutInfo(
                    path,
                    out friendly,
                    out target);


                // 1. 快捷方式自己的友好名称（Description）
                //    很多安装程序根本不写，这一步经常是空的

                AddIconTipCandidate(
                    candidates,
                    friendly);


                // 2. 快捷方式自己的文件名（去掉 .lnk）
                //    中文快捷方式（微信.lnk）靠这一步就拿得到"微信"，
                //    而且必须排在英文的目标程序描述前面

                AddIconTipCandidate(
                    candidates,
                    Path.GetFileNameWithoutExtension(
                        path));


                // 3. 快捷方式在资源管理器里显示的名字
                //    安装程序写出来的 .lnk 通常就是本地化名字

                AddIconTipCandidate(
                    candidates,
                    GetIconTipShellDisplayName(
                        path));


                if(!string.IsNullOrWhiteSpace(target))
                {

                    // 4. 目标程序的文件描述
                    //    中文版 Windows 上常常就是"微信""记事本"

                    AddIconTipCandidate(
                        candidates,
                        GetIconTipFileDescription(
                            target));


                    // 5. 目标程序在资源管理器里显示的名字

                    AddIconTipCandidate(
                        candidates,
                        GetIconTipShellDisplayName(
                            target.Trim()));

                }

            }
            else
            {

                // 1. 非快捷方式：先看文件描述
                //    .exe 的文件描述往往就是本地化的程序名

                AddIconTipCandidate(
                    candidates,
                    GetIconTipFileDescription(
                        path));


                // .exe / 无扩展名这种值得再问两处：
                //   系统里有没有指向这个程序的中文快捷方式
                //   资源管理器里的显示名
                // 普通文档的显示名是"带后缀的文件名"，
                // 后缀是噪音，不如后面直接用去掉后缀的文件名

                if(IsIconTipExecutableLike(
                    extension))
                {

                    // 2. 微信这种：Weixin.exe 自带描述是英文，
                    //    但开始菜单 / 桌面上有"微信.lnk"
                    //    就取那个中文名字

                    AddIconTipCandidate(
                        candidates,
                        GetIconTipNameFromShortcuts(
                            path));


                    // 3. 程序在资源管理器里显示的名字

                    AddIconTipCandidate(
                        candidates,
                        GetIconTipShellDisplayName(
                            path));

                }

            }


            // 兜底：文件名本身（去掉扩展名）

            AddIconTipCandidate(
                candidates,
                Path.GetFileNameWithoutExtension(
                    path));


            return PickIconTipName(
                candidates);

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "解析悬停提示名称失败: "
                +
                ex.Message);


            return "";

        }

    }



    // ==========================
    // 资源管理器里的"显示名"
    // ==========================
    // SHGFI_DISPLAYNAME 拿到的就是资源管理器里显示的那个名字：
    //   程序 → 程序自己的本地化资源名（记事本 / 画图）
    //   .lnk → 去掉后缀的快捷方式名
    // 只读名字，不取图标索引，不会让 Dock 上的图标变样
    // 失败就返回空串，调用方自己跳过这条候选

    private static string GetIconTipShellDisplayName(
        string? path)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(path))
                return "";


            IconTipShellFileInfo info =
                new IconTipShellFileInfo();


            IntPtr handle =
                SHGetFileInfoForIconTip(
                    path,
                    0,
                    ref info,
                    (uint)Marshal.SizeOf(
                        typeof(IconTipShellFileInfo)),
                    IconTipShgfiDisplayName);


            if(handle == IntPtr.Zero)
                return "";


            return (info.szDisplayName ??
                    "").Trim();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "读取资源管理器显示名失败: "
                +
                ex.Message);


            return "";

        }

    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconTipShellFileInfo
    {

        public IntPtr hIcon;

        public int iIcon;

        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string? szTypeName;

    }


    // SHGFI_DISPLAYNAME = 0x00000200

    private const uint IconTipShgfiDisplayName = 0x00000200;


    // 名字用 EntryPoint 显式指向 SHGetFileInfoW：
    // 既避免和上面图标提取用的那个 SHGetFileInfo 重名，
    // 又不依赖 CompilerServices 的自动加后缀

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfoForIconTip(
        string pszPath,
        uint dwFileAttributes,
        ref IconTipShellFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);



    // ==========================
    // 文件描述 / 产品名
    // ==========================
    // FileVersionInfo 从 PE 资源里读：
    //   .exe 大多有 FileDescription，中文版系统上常是"微信""记事本"
    // 普通文档 / 图片没有版本资源，返回空串
    // 只读文件本身的资源，不碰 .lnk，也不改任何东西

    private static string GetIconTipFileDescription(
        string? path)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(path) ||
               !File.Exists(path))
                return "";


            FileVersionInfo info =
                FileVersionInfo.GetVersionInfo(
                    path);


            string text =
                (info.FileDescription ??
                 "").Trim();


            if(text.Length > 0)
                return text;


            // 文件描述没有：退一步用产品名
            // 有些程序的产品名才是给人看的名字

            return (info.ProductName ??
                    "").Trim();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "读取文件描述失败: "
                +
                ex.Message);


            return "";

        }

    }


    // ==========================
    // 读 .lnk：友好名称 + 目标路径
    // ==========================
    // 用 WScript.Shell 的 COM 接口（迟绑定，不引任何包）
    //   FriendlyName → shortcut.Description
    //   TargetPath   → shortcut.TargetPath
    // 目标路径是给"读目标程序的文件描述"那一步用的
    // COM 可能被安全策略禁掉：catch 住，返回两个空串
    // 用完立刻 ReleaseComObject，不给进程留 COM 计数

    private static void ReadIconTipShortcutInfo(
        string shortcutPath,
        out string friendlyName,
        out string targetPath)
    {

        friendlyName = "";
        targetPath = "";


        object? shell = null;
        object? shortcut = null;


        try
        {

            Type? shellType =
                Type.GetTypeFromProgID(
                    "WScript.Shell");


            if(shellType == null)
                return;


            shell =
                Activator.CreateInstance(
                    shellType);


            if(shell == null)
                return;


            shortcut =
                shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[]
                    {
                        shortcutPath
                    });


            if(shortcut == null)
                return;


            Type shortcutType =
                shortcut.GetType();


            object? friendly =
                shortcutType.InvokeMember(
                    "Description",
                    System.Reflection.BindingFlags.GetProperty,
                    null,
                    shortcut,
                    null);


            friendlyName =
                (friendly as string ??
                 "").Trim();


            object? target =
                shortcutType.InvokeMember(
                    "TargetPath",
                    System.Reflection.BindingFlags.GetProperty,
                    null,
                    shortcut,
                    null);


            targetPath =
                (target as string ??
                 "").Trim();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "解析快捷方式失败: "
                +
                ex.Message);

        }
        finally
        {

            ReleaseIconTipComObject(
                shortcut);


            ReleaseIconTipComObject(
                shell);

        }

    }


    private static void ReleaseIconTipComObject(
        object? comObject)
    {

        try
        {

            if(comObject != null &&
               Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(
                    comObject);

        }
        catch
        {

            // 释放失败无所谓：进程退出时系统会收
            // 这里绝不能再抛出去

        }

    }



    private static void AddIconTipCandidate(
        List<string> candidates,
        string? value)
    {

        string text =
            (value ??
             "").Trim();


        if(text.Length == 0)
            return;


        candidates.Add(
            text);

    }


    // .exe / .com / .msc / 没后缀（可能是文件夹）
    // 这几种才值得问资源管理器的显示名

    private static bool IsIconTipExecutableLike(
        string extension)
    {

        if(extension.Length == 0)
            return true;


        return
            extension.Equals(
                ".exe",
                StringComparison.OrdinalIgnoreCase)
            ||
            extension.Equals(
                ".com",
                StringComparison.OrdinalIgnoreCase)
            ||
            extension.Equals(
                ".msc",
                StringComparison.OrdinalIgnoreCase);

    }


    // ==========================
    // 从候选里挑一个名字
    // ==========================
    // 先挑带中文的：用户要的是"微信"，不是 weixin
    // 都没有中文才按原优先级取第一个
    // 一个都没有就返回空串，让调用方退回 AppItem.Name

    private static string PickIconTipName(
        List<string> candidates)
    {

        for(int i = 0; i < candidates.Count; i++)
        {

            string text =
                (candidates[i] ??
                 "").Trim();


            if(text.Length > 0 &&
               ContainsIconTipChinese(text))
                return text;

        }


        for(int i = 0; i < candidates.Count; i++)
        {

            string text =
                (candidates[i] ??
                 "").Trim();


            if(text.Length > 0)
                return text;

        }


        return "";

    }


    // 汉字判断：CJK 统一表意文字 4E00-9FFF + 扩展 A 3400-4DBF
    // 不判断标点，够用了

    private static bool ContainsIconTipChinese(
        string text)
    {

        if(string.IsNullOrEmpty(text))
            return false;


        for(int i = 0; i < text.Length; i++)
        {

            char c =
                text[i];


            if((c >= 0x4E00 && c <= 0x9FFF) ||
               (c >= 0x3400 && c <= 0x4DBF))
                return true;

        }


        return false;

    }



    // ==========================
    // 顺着"中文快捷方式"把中文名捞回来
    // ==========================
    // 微信这类程序：Weixin.exe 自带的描述是英文（Weixin），
    // .lnk 的 Description 又是空的，
    // 但系统开始菜单 / 桌面上躺着一个"微信.lnk"
    // 这里就去标准快捷方式目录里找：
    //   名字带中文 且 目标就是这个程序 的 .lnk
    // 找到就用它的中文名（微信），而不是 Weixin
    // 全程只读：绝不新建 / 修改 / 删除任何快捷方式
    // 先看名字，名字里没中文的直接跳过，
    // 所以绝大多数 .lnk 都不会触发 COM 调用，扫一遍很快
    // 结果按目标路径缓存：第二次悬停直接命中
    // ==========================

    private static readonly object iconTipShortcutNameCacheLock =
        new object();


    private static readonly Dictionary<string,string> iconTipShortcutNameCache =
        new Dictionary<string,string>(
            StringComparer.OrdinalIgnoreCase);


    // 一次最多看多少个 .lnk
    // 防止极端机器上目录巨大拖慢第一次悬停

    private const int IconTipShortcutScanLimit = 600;


    // 去哪儿找中文快捷方式
    // 顺序 = 优先级：开始菜单（安装程序写的官方名字）在前，
    // 快速启动 / 桌面（用户自己起的名字）在后

    private static readonly string[] IconTipShortcutSearchDirs =
    {
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.StartMenu),
            "Programs"),
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonStartMenu),
            "Programs"),
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Internet Explorer",
            "Quick Launch"),
        Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(
            Environment.SpecialFolder.CommonDesktopDirectory),
        ShortcutStoreDir
    };


    // 拿不到就返回空串：调用方继续用后面的候选

    private static string GetIconTipNameFromShortcuts(
        string? targetPath)
    {

        try
        {

            string target =
                (targetPath ??
                 "").Trim();


            // 只对真实存在的文件做这件事
            // 文件夹 / AppX 的虚拟 ID 一律不碰

            if(target.Length == 0 ||
               !File.Exists(target))
                return "";


            lock(iconTipShortcutNameCacheLock)
            {

                if(iconTipShortcutNameCache.TryGetValue(
                    target,
                    out string? cached))
                    return cached ??
                           "";

            }


            string found =
                ScanIconTipShortcuts(
                    target);


            lock(iconTipShortcutNameCacheLock)
            {

                iconTipShortcutNameCache[target] =
                    found;

            }


            return found;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "按快捷方式找中文名失败: "
                +
                ex.Message);


            return "";

        }

    }



    private static string ScanIconTipShortcuts(
        string targetPath)
    {

        string wanted =
            TrimIconTipQuotes(
                targetPath);


        int scanned = 0;


        for(int d = 0; d < IconTipShortcutSearchDirs.Length; d++)
        {

            string dir =
                IconTipShortcutSearchDirs[d];


            if(string.IsNullOrWhiteSpace(dir))
                continue;


            string[] files;


            try
            {

                if(!Directory.Exists(dir))
                    continue;


                files =
                    Directory.GetFiles(
                        dir,
                        "*.lnk",
                        SearchOption.AllDirectories);

            }
            catch(Exception ex)
            {

                // 目录不存在 / 没权限：跳过这一处，绝不崩

                Debug.WriteLine(
                    "扫描快捷方式目录失败: "
                    +
                    ex.Message);


                continue;

            }


            for(int i = 0; i < files.Length; i++)
            {

                if(scanned >= IconTipShortcutScanLimit)
                    return "";


                scanned++;


                string file =
                    files[i];


                string stem;


                try
                {

                    stem =
                        Path.GetFileNameWithoutExtension(
                            file);

                }
                catch
                {

                    continue;

                }


                // 先看名字：名字里没中文的直接跳过
                // 省掉绝大多数 COM 调用

                if(stem.Length == 0 ||
                   !ContainsIconTipChinese(stem))
                    continue;


                // 卸载 / 帮助 / 说明 这类不是应用名字

                if(IsIconTipNoiseShortcutName(stem))
                    continue;


                string friendly;
                string target;


                ReadIconTipShortcutInfo(
                    file,
                    out friendly,
                    out target);


                if(!IsIconTipSameTargetAlias(
                    target,
                    wanted))
                    continue;


                // 名字带中文 + 目标就是它：中文名到手
                // 快捷方式自己写了很短的中文描述就优先用它，
                // 否则就用快捷方式的名字

                string picked =
                    (friendly.Length > 0 &&
                     friendly.Length <= 16 &&
                     ContainsIconTipChinese(friendly))
                    ? friendly
                    : stem;


                return StripIconTipShortcutSuffix(
                    picked);

            }

        }


        return "";

    }


    // 快捷方式目标路径比对：
    // 去掉可能带上的引号、去掉首尾空白、大小写不敏感

    private static bool IsIconTipSameTargetAlias(
        string? shortcutTarget,
        string wanted)
    {

        try
        {

            string a =
                TrimIconTipQuotes(
                    shortcutTarget ??
                    "");


            if(a.Length == 0 ||
               wanted.Length == 0)
                return false;


            return string.Equals(
                a,
                wanted,
                StringComparison.OrdinalIgnoreCase);

        }
        catch
        {

            return false;

        }

    }


    private static string TrimIconTipQuotes(
        string? value)
    {

        string text =
            (value ??
             "").Trim();


        if(text.Length >= 2 &&
           text.StartsWith(
               "\"",
               StringComparison.Ordinal) &&
           text.EndsWith(
               "\"",
               StringComparison.Ordinal))
            text = text.Substring(
                1,
                text.Length - 2).Trim();


        return text;

    }


    // 卸载、帮助、说明、官网…… 这些不是应用名

    private static bool IsIconTipNoiseShortcutName(
        string name)
    {

        string[] noise =
        {
            "卸载",
            "uninstall",
            "帮助",
            "help",
            "readme",
            "说明",
            "关于",
            "about",
            "官网",
            "主页",
            "更新",
            "升级"
        };


        for(int i = 0; i < noise.Length; i++)
        {

            if(name.IndexOf(
                noise[i],
                StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

        }


        return false;

    }



    // 点击空白区域
    // ==========================


    private void Dock_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {


        // 点击图标不处理

        if(e.OriginalSource
            is FrameworkElement element)
        {


            if(element.DataContext
                is AppItem)
            {


                return;


            }


        }





        // 当前版本永久显示

        isPinned = true;


        ShowDock();



    }












    // ==========================
    // 全局拖动
    // 判定点击 / 拖动
    // ==========================



    private void DockDrag_Down(
        object sender,
        MouseButtonEventArgs e)
    {



        // 新的按下
        // 清除上一次拖动的残留标记

        dockDragSuppressClick = false;


        // 记住这次是按在哪个图标上
        // 按在空白处 / 顶部细条上就还是移动窗口

        dockDragIconSource =
            GetDockIconApp(
                e.OriginalSource);


        dockDragArmed = true;


        dockDragStarted = false;


        // 用屏幕坐标记录按下点
        // 窗口在动画期间会改变矩形
        // 相对窗口的坐标会跟着变
        // 那样会把点击误判成拖动

        dockDragStartPoint =
            PointToScreen(
                e.GetPosition(
                    this));


    }




    private void DockDrag_Move(
        object sender,
        MouseEventArgs e)
    {


        // 内部拖拽（Dock ↔ 展板）进行中
        // 每一帧都交给视觉引擎：幽灵跟手 + 让位
        // 这里挂在窗口的 PreviewMouseMove 上
        // 鼠标被窗口捕获后，子元素收不到移动事件

        if(appDragActive)
        {


            UpdateAppDrag(
                e);


            return;


        }


        if(!dockDragArmed)
            return;


        if(e.LeftButton !=
            MouseButtonState.Pressed)
        {

            dockDragArmed = false;


            return;

        }


        if(dockDragStarted)
            return;


        Point now =
            PointToScreen(
                e.GetPosition(
                    this));


        double dx =
            now.X -
            dockDragStartPoint.X;


        double dy =
            now.Y -
            dockDragStartPoint.Y;


        // 未超过系统拖动阈值
        // 说明只是点击，交给图标处理

        if(Math.Abs(dx) <
                SystemParameters.MinimumHorizontalDragDistance &&
           Math.Abs(dy) <
                SystemParameters.MinimumVerticalDragDistance)
        {

            return;

        }


        // 超过系统阈值了
        // 第一次判方向：按在图标上 + 往上拖
        // 这次就是把 Dock 里的 App 拖进收纳盒

        if(IsDockIconAppDrag(dx, dy))
        {


            dockDragStarted = true;


            dockDragArmed = false;


            // 本次手势是拖动
            // 松手后不再启动图标

            dockDragSuppressClick = true;


            AppItem? app =
                dockDragIconSource;


            dockDragIconSource = null;


            StartAppDragVisual(
                app,
                AppDragFromDock,
                app == null
                ? ""
                : app.LaunchPath);


            dockDragStarted = false;


            return;


        }


        // 按在图标上、方向朝上
        // 但位移还小，方向可能还没定型
        // 先不急着移动窗口，再等一小段
        // 出了这个小范围就按老逻辑：移动窗口

        if(dockDragIconSource != null &&
           dy < 0 &&
           Math.Abs(dx) < DockAppDragUpDistance &&
           Math.Abs(dy) < DockAppDragUpDistance)
        {

            return;

        }


        // ==========================
        // 已锁定位置
        // 走到这里说明手势已经超过拖动阈值
        // 但锁定时绝不移动窗口：直接 return
        // DragMove() 不会被调用
        // 图标拖出到展板 / 后续点击启动应用都不受影响
        // ==========================

        if(isLocked)
        {

            dockDragStarted = true;


            dockDragArmed = false;


            // 本次手势已经是拖动
            // 松手后不再启动图标

            dockDragSuppressClick = true;


            return;

        }


        dockDragStarted = true;


        dockDragArmed = false;


        // 本次手势已经是拖动
        // 松手后不再启动图标

        dockDragSuppressClick = true;


        try
        {

            DragMove();

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "拖动失败: "
                +
                ex.Message);

        }


        dockDragStarted = false;


    }




    private void DockDrag_Up(
        object sender,
        MouseButtonEventArgs e)
    {

        // 内部拖拽：这一下松手就是落点判定
        // 要在清标记之前跑，它得读这次手势的数据

        EndAppDrag();


        dockDragArmed = false;


        // 展板图标的拖动状态一起清掉

        boardDragArmed = false;


        boardDragStarted = false;


        dockDragStarted = false;

    }




    // ==========================
    // Dock拖动
    // ==========================


    private void Dock_Move(
        object sender,
        MouseEventArgs e)
    {


        // 顶部拖动区域与图标区域
        // 统一走同一套判定

        DockDrag_Move(
            sender,
            e);

    }




    // ==========================
    // 位置锁定：设置面板里的玻璃开关
    // 原来的右键菜单已移除
    // 锁定状态只由 DockSettingsPanel 的 LockToggle 控制
    // ==========================


    // 开关是否正在被代码回写
    // 防止 Checked / Unchecked 再绕一圈回来

    private bool isSyncingLockToggle = false;


    // 开关拨动：勾选 / 取消共用同一个回调
    // 立即写入 isLocked，实时生效
    // 只改状态标记，不碰任何布局与动画

    private void LockToggle_Changed(
        object sender,
        RoutedEventArgs e)
    {

        // 代码正在回写开关：忽略这次回调

        if(isSyncingLockToggle)
            return;


        if(LockToggle == null)
            return;


        SetDockLocked(
            LockToggle.IsChecked == true);

    }


    // 把 isLocked 的真实状态写回开关
    // 面板打开时同步一次
    // 无论状态从哪里被改，面板显示都跟着走

    private void UpdateLockToggleState()
    {

        if(LockToggle == null)
            return;


        bool locked =
            isLocked;


        // 已经一致就不动
        // 免得白白触发一次 Checked / Unchecked

        if(LockToggle.IsChecked == locked)
            return;


        isSyncingLockToggle = true;


        LockToggle.IsChecked =
            locked;


        isSyncingLockToggle = false;

    }


    // 切换锁定状态
    // 顺手把手势标记清干净
    // 免得切换时残留上一次的拖动判定

    private void SetDockLocked(
        bool locked)
    {

        isLocked = locked;


        dockDragArmed = false;


        dockDragStarted = false;


        // 锁定 / 解锁本身不是拖动
        // 不要吞掉随后的图标点击

        dockDragSuppressClick = false;


        // 开关跟着刷新：任何入口改状态
        // 面板上的开关都保持同步

        UpdateLockToggleState();

    }




    // ==========================
    // 桌面图标处理模式：面板上的两个玻璃药丸
    // 纯设置逻辑：读 / 写一个小文本文件
    // 不碰任何图标提取、图标渲染、去重代码
    // ==========================


    // "保留在桌面" 被选中

    private void IconModeKeep_Checked(
        object sender,
        RoutedEventArgs e)
    {

        // 代码正在回写面板：忽略这次回调

        if(isSyncingIconMode)
            return;


        SetDockIconMode(
            DockIconMode.KeepOnDesktop);

    }


    // "收纳至 Dock" 被选中

    private void IconModeStore_Checked(
        object sender,
        RoutedEventArgs e)
    {

        if(isSyncingIconMode)
            return;


        SetDockIconMode(
            DockIconMode.MoveToStore);

    }


    // 切换模式：先记住，变了才落盘，最后回写面板

    private void SetDockIconMode(
        DockIconMode mode)
    {

        bool changed =
            dockIconMode != mode;


        dockIconMode =
            mode;


        if(changed)
            SaveIconMode();


        UpdateIconModeUi();

    }


    // 按当前模式回写两个药丸
    // isSyncingIconMode 保证不会反过来再触发一次保存

    private void UpdateIconModeUi()
    {

        if(IconModeKeepRadio == null ||
           IconModeStoreRadio == null)
            return;


        bool keep =
            dockIconMode ==
            DockIconMode.KeepOnDesktop;


        isSyncingIconMode =
            true;


        IconModeKeepRadio.IsChecked =
            keep;


        IconModeStoreRadio.IsChecked =
            !keep;


        isSyncingIconMode =
            false;

    }


    // 读模式配置
    // 文件不存在 / 内容不认识 / 读失败：一律回到模式A
    // 读配置失败绝不弹窗，也不影响启动

    private void LoadIconMode()
    {

        dockIconMode =
            DockIconMode.KeepOnDesktop;


        try
        {

            if(!File.Exists(IconModeStorePath))
                return;


            string text =
                File.ReadAllText(IconModeStorePath);


            if(!string.IsNullOrWhiteSpace(text) &&
               text.Trim().Equals(
                   "MoveToStore",
                   StringComparison.OrdinalIgnoreCase))
            {

                dockIconMode =
                    DockIconMode.MoveToStore;

            }

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "读取桌面图标处理模式失败: "
                +
                ex.Message);

        }

    }


    // 写模式配置
    // 整段包在 try 里：写不进去最多下次启动回到模式A
    // 不弹窗、不崩溃

    private void SaveIconMode()
    {

        try
        {

            // 模式B 才需要收纳目录，切回模式A 不建目录

            if(dockIconMode ==
               DockIconMode.MoveToStore &&
               !Directory.Exists(ShortcutStoreDir))
            {

                Directory.CreateDirectory(
                    ShortcutStoreDir);

            }


            File.WriteAllText(
                IconModeStorePath,
                dockIconMode == DockIconMode.MoveToStore
                ? "MoveToStore"
                : "KeepOnDesktop");

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "保存桌面图标处理模式失败: "
                +
                ex.Message);

        }

    }


    // ==========================
    // 模式B：桌面文件 ⇄ 收纳目录
    // 全部只用 File.Move
    // 任何一步失败都不 File.Delete，也不让程序崩
    // ==========================


    // 只搬".lnk / .exe 文件"
    // 文件夹、其它后缀、以及路径已经不存在的，一律不动

    private static bool IsShortcutOrExeFile(
        string path)
    {

        if(string.IsNullOrWhiteSpace(path))
            return false;


        if(!File.Exists(path))
            return false;


        string ext =
            Path.GetExtension(path);


        return ext.Equals(
                   ".lnk",
                   StringComparison.OrdinalIgnoreCase)
               ||
               ext.Equals(
                   ".exe",
                   StringComparison.OrdinalIgnoreCase);

    }


    // 是不是"桌面根目录里的文件"
    // 只搬桌面上的东西：
    // 从 D:\下载 之类的地方拖进来的文件不动，
    // 免得把用户别处的程序搬走

    // 用户桌面 + 公共桌面，两份路径全都要认
    //   以前只比 DesktopDirectory 一个值：
    //   公共桌面（C:\Users\Public\Desktop）上的图标会被判成"不在桌面"
    //   → 静悄悄跳过搬家 → 桌面上的原文件还在，而且一声不吭
    //   现在把这些候选目录都比一遍，认不出才返回 false

    private static bool IsOnUserDesktop(
        string path)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(path))
                return false;


            string dir =
                Path.GetDirectoryName(path) ?? "";


            if(string.IsNullOrWhiteSpace(dir))
                return false;


            string full;


            try
            {

                full =
                    TrimDirEnd(
                        Path.GetFullPath(dir));

            }
            catch
            {

                return false;

            }


            if(full.Length == 0)
                return false;


            // 候选一：系统给的桌面目录（用户桌面 / 公共桌面 / OneDrive 兜底）

            foreach(string desktop in GetDesktopDirsForCompare())
            {

                if(string.Equals(
                    full,
                    desktop,
                    StringComparison.OrdinalIgnoreCase))
                    return true;

            }


            // 候选二：目录名就叫 Desktop / 桌面，而且位于用户文件夹那一带
            // （C:\Users\...），比如
            //   C:\Users\X\OneDrive - 公司\Desktop
            //   C:\Users\Public\Desktop
            // 专治 OneDrive / 各种重定向导致特殊文件夹拿不到真实桌面的情况
            // 其它分区根目录（D:\Desktop）不认，免得把别处的文件搬错

            string leaf =
                Path.GetFileName(full);


            bool leafLooksLikeDesktop =
                string.Equals(
                    leaf,
                    "Desktop",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    leaf,
                    "桌面",
                    StringComparison.OrdinalIgnoreCase);


            if(!leafLooksLikeDesktop)
                return false;


            string profile =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);


            if(string.IsNullOrWhiteSpace(profile))
                return false;


            string usersRoot;


            try
            {

                usersRoot =
                    TrimDirEnd(
                        Path.GetFullPath(
                            Path.Combine(
                                profile,
                                "..")));

            }
            catch
            {

                return false;

            }


            if(usersRoot.Length == 0)
                return false;


            return full.StartsWith(
                usersRoot
                +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        }
        catch
        {

            return false;

        }

    }


    // ==========================
    // 桌面的候选目录（全部来自系统 API / 环境变量，零硬编码）
    //   1) Desktop                当前用户桌面（OneDrive 重定向后 = 实际在用的那个）
    //   2) DesktopDirectory       个别机器上它和上面不一样，兜一下
    //   3) CommonDesktopDirectory 公共桌面（All Users 桌面）
    //   4) %OneDrive%\Desktop
    //   5) %OneDriveCommercial%\Desktop
    //   6) %USERPROFILE%\Desktop
    // 这套候选只用来"认路径"，绝对不往里面写任何东西
    // ==========================

    private static string[] GetDesktopDirsForCompare()
    {

        List<string> dirs =
            new List<string>();


        AddDesktopDirCandidate(
            dirs,
            Environment.GetFolderPath(
                Environment.SpecialFolder.Desktop));


        AddDesktopDirCandidate(
            dirs,
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory));


        AddDesktopDirCandidate(
            dirs,
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonDesktopDirectory));


        AddDesktopDirCandidate(
            dirs,
            BuildDesktopDirFromEnv("OneDrive"));


        AddDesktopDirCandidate(
            dirs,
            BuildDesktopDirFromEnv("OneDriveCommercial"));


        AddDesktopDirCandidate(
            dirs,
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                "Desktop"));


        return dirs.ToArray();

    }


    // 收一个候选目录：空值 / 非法路径 / 重复的都直接丢掉
    // 统一走 GetFullPath，C:\A\Desktop 和 C:\A\Desktop\ 算同一个

    private static void AddDesktopDirCandidate(
        List<string> dirs,
        string? dir)
    {

        if(dirs == null ||
           string.IsNullOrWhiteSpace(dir))
            return;


        string full;


        try
        {

            full =
                TrimDirEnd(
                    Path.GetFullPath(dir));

        }
        catch
        {

            return;

        }


        if(full.Length == 0)
            return;


        foreach(string exist in dirs)
        {

            if(string.Equals(
                exist,
                full,
                StringComparison.OrdinalIgnoreCase))
                return;

        }


        dirs.Add(full);

    }


    // OneDrive 这类环境变量里存的是"云盘根目录"，
    // 桌面就挂在它下面的 Desktop

    private static string BuildDesktopDirFromEnv(
        string variableName)
    {

        try
        {

            string root =
                Environment.GetEnvironmentVariable(
                    variableName) ?? "";


            if(string.IsNullOrWhiteSpace(root))
                return "";


            return Path.Combine(
                root,
                "Desktop");

        }
        catch
        {

            return "";

        }

    }


    // 去掉结尾的斜杠再比较
    // C:\Users\x\Desktop  和  C:\Users\x\Desktop\  视为同一个

    private static string TrimDirEnd(
        string dir)
    {

        if(string.IsNullOrEmpty(dir))
            return "";


        return dir.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

    }


    // 在目标目录里取一个不重名的路径
    // 重名就加 (2)、(3) …
    // 绝不覆盖已经存在的文件

    private static string BuildUniqueTargetPath(
        string directory,
        string fileName)
    {

        string target =
            Path.Combine(
                directory,
                fileName);


        if(!File.Exists(target))
            return target;


        string baseName =
            Path.GetFileNameWithoutExtension(fileName);


        string ext =
            Path.GetExtension(fileName);


        for(int i = 2; i < 1000; i++)
        {


            target =
                Path.Combine(
                    directory,
                    baseName
                    +
                    " (" + i + ")"
                    +
                    ext);


            if(!File.Exists(target))
                return target;


        }


        return Path.Combine(
            directory,
            baseName
            +
            " (" + Guid.NewGuid().ToString("N") + ")"
            +
            ext);

    }


    // 路径是不是躺在收纳目录里

    private static bool IsInsideShortcutStore(
        string path)
    {

        try
        {

            string full =
                Path.GetFullPath(path);


            string dir =
                TrimDirEnd(
                    Path.GetFullPath(ShortcutStoreDir))
                +
                Path.DirectorySeparatorChar;


            return full.StartsWith(
                dir,
                StringComparison.OrdinalIgnoreCase);

        }
        catch
        {

            return false;

        }

    }


    // ==========================
    // 真正干活的"移动文件"：目标里已有同名文件也能搬过去
    // 1) 目标不存在 → 普通 File.Move
    // 2) 目标存在 → 优先 File.Move(源, 目标, true) 覆盖模式
    //    （.NET Core 3.0 / .NET 5+ 才有这个三重载）
    //    老框架（.NET Framework 4.x）拿不到这个重载：
    //    先安全删掉目标文件，再 File.Move
    // 不管走哪条路：
    //   抛异常时【源文件一定还在原地】，绝不丢
    // 用反射去"找"三重载，而不是直接写 File.Move(a,b,true)：
    //   这样 .NET Framework 4.x 也能编过，不用动项目文件
    // ==========================

    private static void MoveFileForceOverwrite(
        string source,
        string destination)
    {

        if(string.IsNullOrWhiteSpace(source))
            throw new ArgumentException(
                "源文件路径为空",
                "source");


        if(string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException(
                "目标路径为空",
                "destination");


        // 源和目标完全一样：不需要移动

        if(string.Equals(
            Path.GetFullPath(source),
            Path.GetFullPath(destination),
            StringComparison.OrdinalIgnoreCase))
            return;


        Exception? moveError = null;


        try
        {

            // 目标不存在：普通移动，不去碰覆盖那一套

            if(!File.Exists(destination))
            {

                File.Move(
                    source,
                    destination);

                return;

            }


            // 目标存在：先看当前框架认不认"覆盖模式"这个重载
            // （var = 反射查出来的方法，查不到就是 null）

            var moveWithOverwrite =
                typeof(File).GetMethod(
                    "Move",
                    new Type[]
                    {
                        typeof(string),
                        typeof(string),
                        typeof(bool)
                    });


            if(moveWithOverwrite != null)
            {

                try
                {

                    moveWithOverwrite.Invoke(
                        null,
                        new object[]
                        {
                            source,
                            destination,
                            true
                        });

                }
                catch(System.Reflection.TargetInvocationException tie)
                {

                    // 反射会把真异常包一层
                    // 拆开重新抛，好让外面的 catch 认得出
                    // UnauthorizedAccessException / IOException / PathTooLongException

                    if(tie.InnerException != null)
                    {

                        System.Runtime.ExceptionServices
                            .ExceptionDispatchInfo
                            .Capture(tie.InnerException)
                            .Throw();

                    }


                    throw;

                }


                return;

            }


            // 老框架兜底：目标文件已确认存在，先删掉它再移动
            // 走到这一步说明源文件已经确认存在：
            // 就算接下来移动失败，源文件依然完整地躺在原地

            File.Delete(
                destination);


            File.Move(
                source,
                destination);

            return;

        }
        catch(Exception ex)
        {

            // File.Move 失败：记下原因，马上走 Copy + Delete 兜底
            // 跨盘符（C: → D:）、目标被占用、部分网络盘都会走到这里

            moveError = ex;


            Debug.WriteLine(
                "[收纳] File.Move 失败，改用 Copy + Delete 兜底："
                +
                source
                +
                " -> "
                +
                destination
                +
                "，原因："
                +
                ex.Message);

        }


        // 极少数情况下异常抛出时文件其实已经移动成功
        // （源文件没了、目标文件在了）→ 直接当成成功，不再重复复制

        if(!File.Exists(source) &&
           File.Exists(destination))
            return;


        bool copied = false;


        try
        {

            // 强制降级：先复制（覆盖目标），复制成功后再删除源文件
            // File.Copy 支持跨盘符，不会因为 C: → D: 失败

            File.Copy(
                source,
                destination,
                true);

            copied = true;


            File.Delete(
                source);

        }
        catch(Exception fallbackEx)
        {

            // 绝不静默：把 File.Move 的原始错误和兜底错误一起抛出去
            // 上层 PreparePathForDock 会弹窗给用户看

            string detail;

            if(copied && File.Exists(destination))
            {

                detail =
                    "文件已复制到收纳目录，但删除桌面原文件失败"
                    +
                    "（文件可能被占用、只读或权限不足）。";

            }
            else
            {

                detail =
                    "File.Move 失败，Copy + Delete 兜底也失败。";

            }


            throw new IOException(
                detail
                +
                " 移动错误："
                +
                (moveError != null
                    ? moveError.Message
                    : "无")
                +
                "；兜底错误："
                +
                fallbackEx.Message,
                new AggregateException(
                    moveError ?? fallbackEx,
                    fallbackEx));

        }

    }


    // 收纳目录里这个路径现在还被别的 Dock / 展板图标用着吗？
    // 用着 → 覆盖会把那个图标的文件弄丢，这时改成另起一个名字
    // 只做只读比对：不碰图标数据、不碰拖拽、不碰图标提取

    private bool IsStoreFileUsedByOtherIcon(
        string storePath)
    {

        if(string.IsNullOrWhiteSpace(storePath))
            return false;


        try
        {

            foreach(AppItem item in AppManager.Apps)
            {

                if(item == null)
                    continue;


                if(IsSamePath(
                    item.LaunchPath,
                    storePath))
                    return true;

            }


            foreach(string boardPath in boardSourcePaths)
            {

                if(IsSamePath(
                    boardPath,
                    storePath))
                    return true;

            }


            return false;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "检查收纳目录里的同名文件是否还在用: "
                +
                ex.Message);


            // 判断不出来就保守处理：当成"还在用"，
            // 宁可另起一个名字，也绝不冒险覆盖别人的文件

            return true;

        }

    }


    // 两个路径是不是同一个文件
    // 先规整成完整路径，再大小写不敏感地比

    private static bool IsSamePath(
        string? a,
        string? b)
    {

        if(string.IsNullOrWhiteSpace(a) ||
           string.IsNullOrWhiteSpace(b))
            return false;


        try
        {

            return string.Equals(
                Path.GetFullPath(a),
                Path.GetFullPath(b),
                StringComparison.OrdinalIgnoreCase);

        }
        catch
        {

            return false;

        }

    }


    // 拖入 Dock 之前调用
    // 模式B：把桌面上的 .lnk / .exe（用户桌面 + 公共桌面）搬进收纳目录
    // 返回 Dock 该记录的路径：
    //     搬成功 = 收纳目录里的新路径
    //     没搬 / 搬失败 = 原路径（= 模式A 的表现，文件绝不丢）
    // 任何一步失败都会弹窗说明原因，绝不静默失败

    private string PreparePathForDock(
        string path)
    {

        // 把底层异常翻译成用户能看懂的原因
        // 只用于本方法的弹窗，不改任何其它逻辑

        static string DescribeMoveFailure(
            Exception ex)
        {

            // 有些错误会被包一层（反射 / IOException / AggregateException）
            // 往里找最多 5 层，争取把真正的原因翻译出来

            Exception? cursor =
                ex;


            for(int depth = 0;
                depth < 5 &&
                cursor != null;
                depth++)
            {

                if(cursor is UnauthorizedAccessException)
                    return "权限不足（可能是公共桌面权限、系统保护或安全软件拦截）";


                if(cursor is PathTooLongException)
                    return "文件路径过长";


                if(cursor is FileNotFoundException)
                    return "源文件不存在（可能已被删除或移走）";


                if(cursor is DirectoryNotFoundException)
                    return "目标目录不存在或无法访问";


                cursor =
                    cursor.InnerException;

            }


            if(ex is IOException)
                return "文件被占用 / 跨盘移动失败 / 磁盘或 OneDrive 同步锁定";


            return ex.GetType().Name;

        }


        // 模式A：桌面文件一动不动

        if(dockIconMode != DockIconMode.MoveToStore)
        {

            Debug.WriteLine(
                "[收纳] 跳过移动：当前模式=保留在桌面，路径："
                +
                path);


            return path;

        }


        // 模式B：从这里开始，任何"没有搬走"的分支都必须明确提示
        // 绝对不能再静默 return

        Debug.WriteLine(
            "[收纳] 开始移动：当前模式=收纳至 Dock，路径："
            +
            path
            +
            "，目标目录："
            +
            ShortcutStoreDir);


        if(string.IsNullOrWhiteSpace(path))
        {

            MessageBox.Show(
                "拖入的路径为空，无法收纳。",
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return path;

        }


        // 文件夹不能用 File.Move：明确告诉用户，不静默跳过

        if(Directory.Exists(path))
        {

            MessageBox.Show(
                "当前为“收纳至 Dock”模式，但文件夹不能移动。\n\n"
                +
                "文件夹仍保留在原位置：\n"
                +
                path
                +
                "\n\nDock 中将记录原路径，可以正常打开。",
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);


            return path;

        }


        // 【移动前检查】源文件真的还在吗？
        // 桌面上的文件可能已经被删掉 / 被别的程序搬走了
        // 这种情况不搬，并且明确告诉用户，绝不静默跳过

        if(!File.Exists(path))
        {

            MessageBox.Show(
                "这个文件已经不在桌面上了，没法收纳"
                +
                "（被删除或被移走了）。\n\n"
                +
                "Dock 里记录的是原路径：\n"
                +
                path,
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return path;

        }


        // 已经躺在收纳目录里的文件：本来就不需要再搬
        // 这是唯一允许静默放行的"不需要移动"分支

        if(IsInsideShortcutStore(path))
        {

            Debug.WriteLine(
                "[收纳] 文件已在收纳目录，无需移动："
                +
                path);


            return path;

        }


        // 桌面以外的文件（从 D:\下载 之类拖进来的）：按设计不搬
        // 但必须弹窗说明，不能让人以为收纳成功了

        if(!IsOnUserDesktop(path))
        {

            MessageBox.Show(
                "当前为“收纳至 Dock”模式，但这个文件不在桌面目录，"
                +
                "按设计不会移动它。\n\n"
                +
                "文件位置：\n"
                +
                path
                +
                "\n\nDock 中将记录原路径，可以正常使用。",
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);


            return path;

        }


        // 只搬桌面上的 .lnk / .exe 文件
        // 其它后缀保持桌面原样（这是当前收纳模式的设计）
        // 但必须弹窗告知，不能再静默跳过

        if(!IsShortcutOrExeFile(path))
        {

            MessageBox.Show(
                "当前“收纳至 Dock”模式只移动桌面上的 .lnk / .exe 文件。\n\n"
                +
                "这个文件类型不会被移动，仍保留在桌面：\n"
                +
                path
                +
                "\n\nDock 中将记录原路径，可以正常使用。",
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);


            return path;

        }


        string target = "";


        try
        {

            // 先判断目标目录（AppData 下的 Shortcuts）是否存在
            // 不存在立刻创建

            if(!Directory.Exists(ShortcutStoreDir))
            {

                Debug.WriteLine(
                    "[收纳] 收纳目录不存在，创建："
                    +
                    ShortcutStoreDir);

                Directory.CreateDirectory(
                    ShortcutStoreDir);

            }


            string fileName =
                Path.GetFileName(path);


            // 【同名文件覆盖】收纳目录里已经有同名文件 → 直接覆盖，
            // 不再加 (2)、(3) 那种去重后缀，避免因为同名卡住 / 越攒越多
            // 只有"这个同名文件还被别的 Dock / 展板图标用着"时才另起名字：
            // 那种情况下覆盖会把那个图标的文件弄丢

            target =
                Path.Combine(
                    ShortcutStoreDir,
                    fileName);


            if(File.Exists(target) &&
               IsStoreFileUsedByOtherIcon(target))
            {

                target =
                    BuildUniqueTargetPath(
                        ShortcutStoreDir,
                        fileName);

            }

        }
        catch(Exception ex)
        {

            // 目录都建不出来 / 目标路径算不出来：明确弹窗

            MessageBox.Show(
                "收纳失败："
                +
                DescribeMoveFailure(ex)
                +
                "\n\n无法创建收纳目录或准备目标路径。\n"
                +
                "收纳目录：\n"
                +
                ShortcutStoreDir
                +
                "\n\n文件仍在原位置，没有被移动：\n"
                +
                path
                +
                "\n\n详细错误：\n"
                +
                ex.Message,
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return path;

        }


        // 真正的移动：MoveFileForceOverwrite 内部先 File.Move，
        // 失败后自动用 File.Copy + File.Delete 兜底。
        // 这里再包一层最外层 try-catch，保证任何异常都会弹窗。

        try
        {

            MoveFileForceOverwrite(
                path,
                target);

        }
        catch(Exception ex)
        {

            // 走到这里 = File.Move 和 Copy + Delete 兜底都失败
            // 文件还在桌面，必须把具体原因告诉用户

            MessageBox.Show(
                "收纳失败："
                +
                DescribeMoveFailure(ex)
                +
                "\n\n文件仍在原位置，没有被移动：\n"
                +
                path
                +
                "\n\n目标位置：\n"
                +
                target
                +
                "\n\n详细错误：\n"
                +
                ex.Message
                +
                "\n\nDock 将记录原路径，可以正常使用。",
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return path;

        }


        // 【结果校验】源文件必须真的不在了，目标文件必须真的存在
        // 防止底层 API"没报错但也没搬"的极端情况

        if(File.Exists(path) ||
           !File.Exists(target))
        {

            MessageBox.Show(
                "移动后校验失败：源文件仍在原位置，或目标文件不存在。\n\n"
                +
                "源文件：\n"
                +
                path
                +
                "\n\n目标位置：\n"
                +
                target
                +
                "\n\n文件没有被安全移走，Dock 将继续记录原路径。",
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return path;

        }


        Debug.WriteLine(
            "[收纳] 移动成功："
            +
            path
            +
            " -> "
            +
            target);


        return target;

    }


    // ==========================
    // 收纳目录的候选位置
    // 第一个 = 搬入时真正写入的目录（和模式B 完全一致）
    // 第二个只是"查找兜底"：
    // 万一老版本的收纳目录叫 AeroDock\Shortcuts，
    // 里面的文件照样能被找回来
    // 这里只读、只找，绝不往第二个目录里写任何东西
    // ==========================

    private static readonly string[] ShortcutStoreLookupDirs =
    {
        ShortcutStoreDir,
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "AeroDock",
            "Shortcuts")
    };


    // ==========================
    // 找出这个 Dock / 展板 图标在收纳目录里对应的源文件
    // 只做"路径比对"：不碰图标提取、不碰拖拽、不碰显示
    // ==========================
    // 顺序：
    //   1) 记录里的路径本身就在收纳目录里 —— 直接用
    //   2) 收纳目录里连后缀都同名的文件（比如记录里是 chrome.lnk）
    //   3) 收纳目录里主文件名同名的文件
    //      （.lnk 被解析成 .exe 目标时，靠这一步也能找回来）
    //   4) 再和界面上显示的名字比一次
    //      名字结尾被加了 (2)、(3) 去重后缀的，去后缀后再比
    // 一个都找不到就返回空串：
    // 说明这个图标没被搬过，照原样删图标，不动任何文件

    private static string FindStoreFileForApp(
        AppItem? app)
    {

        try
        {

            if(app == null)
                return "";


            string launch =
                app.LaunchPath ?? "";


            // 1) 路径本身就在收纳目录里

            if(!string.IsNullOrWhiteSpace(launch) &&
               IsInsideShortcutStore(launch) &&
               File.Exists(launch))
                return launch;


            string fileName =
                string.IsNullOrWhiteSpace(launch)
                ? ""
                : Path.GetFileName(launch);


            string stem =
                string.IsNullOrWhiteSpace(fileName)
                ? ""
                : Path.GetFileNameWithoutExtension(fileName);


            string appName =
                (app.Name ?? "").Trim();


            // 2) ~ 4) 逐个候选目录找（正常只会有第一个）

            foreach(string storeDir in ShortcutStoreLookupDirs)
            {

                string found =
                    FindStoreFileInDir(
                        storeDir,
                        fileName,
                        stem,
                        appName);


                if(found.Length > 0)
                    return found;

            }


            return "";

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "查找收纳目录里的源文件失败: "
                +
                ex.Message);


            return "";

        }

    }


    // 在一个收纳目录里按 文件名 → 主文件名 → 界面名字 的顺序找一个文件
    // 只比对路径文字，不碰图标、不碰拖拽、不碰显示

    private static string FindStoreFileInDir(
        string storeDir,
        string fileName,
        string stem,
        string appName)
    {

        try
        {

            if(string.IsNullOrWhiteSpace(storeDir) ||
               !Directory.Exists(storeDir))
                return "";


            string byFullName = "";

            string byStem = "";

            string byAppName = "";


            foreach(string file in Directory.GetFiles(storeDir))
            {

                string storeName =
                    Path.GetFileName(file);


                if(string.IsNullOrWhiteSpace(storeName))
                    continue;


                // 2) 连后缀都一模一样

                if(byFullName.Length == 0 &&
                   !string.IsNullOrWhiteSpace(fileName) &&
                   string.Equals(
                       storeName,
                       fileName,
                       StringComparison.OrdinalIgnoreCase))
                {

                    byFullName = file;

                    continue;

                }


                string storeStem =
                    Path.GetFileNameWithoutExtension(storeName);


                // 3) 只比主文件名
                //    .lnk 被系统解析成 .exe 目标时也能找回来

                if(byStem.Length == 0 &&
                   !string.IsNullOrWhiteSpace(stem) &&
                   IsSameFileStem(
                       storeStem,
                       stem))
                    byStem = file;


                // 4) 再和界面上的名字比一次

                if(byAppName.Length == 0 &&
                   !string.IsNullOrWhiteSpace(appName) &&
                   IsSameFileStem(
                       storeStem,
                       appName))
                    byAppName = file;

            }


            if(byFullName.Length > 0)
                return byFullName;


            if(byStem.Length > 0)
                return byStem;


            if(byAppName.Length > 0)
                return byAppName;


            return "";

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "查找收纳目录里的源文件失败: "
                +
                ex.Message);


            return "";

        }

    }


    // 比较两个主文件名（都不带后缀）
    // 去重后可能变成 "名字 (2)"，所以先把结尾的 " (数字)" 去掉再比

    private static bool IsSameFileStem(
        string a,
        string b)
    {

        if(string.IsNullOrWhiteSpace(a) ||
           string.IsNullOrWhiteSpace(b))
            return false;


        return string.Equals(
            TrimUniqueSuffix(a.Trim()),
            TrimUniqueSuffix(b.Trim()),
            StringComparison.OrdinalIgnoreCase);

    }


    // 去掉 BuildUniqueTargetPath 加上的 " (2)" / " (3)" 结尾
    // 不是这个形状就原样返回

    private static string TrimUniqueSuffix(
        string name)
    {

        if(string.IsNullOrEmpty(name))
            return "";


        int end =
            name.Length;


        if(name[end - 1] != ')')
            return name;


        int open =
            name.LastIndexOf('(');


        if(open <= 0)
            return name;


        // 括号前面必须是空格，括号里必须全是数字

        if(name[open - 1] != ' ')
            return name;


        string digits =
            name.Substring(
                open + 1,
                end - open - 2);


        if(digits.Length == 0)
            return name;


        foreach(char c in digits)
        {

            if(c < '0' || c > '9')
                return name;

        }


        return name.Substring(
            0,
            open - 1).TrimEnd();

    }


    // ==========================
    // 从 Dock / 展板 删除图标之前调用
    // 模式B：把收纳目录里的源文件搬回桌面（原名 + 原路径）
    //        桌面上已有同名文件 → 直接覆盖，不加 (2)、(3) 后缀
    // 返回 true  = 可以继续删这个图标
    // 返回 false = 搬回失败，图标先留着（文件绝不丢）
    // 模式A 一律返回 true，什么都不做
    // 只在"覆盖桌面同名文件"时 File.Delete 一次，
    // 收纳目录里的源文件在 Move 成功前绝不动，任何情况下都不丢
    // ==========================

    private bool TryMoveBackToDesktop(
        AppItem app)
    {

        if(app == null)
            return true;


        if(dockIconMode != DockIconMode.MoveToStore)
            return true;


        // 先找出这个图标在收纳目录里对应的那个文件
        // 找不到 = 它没被搬进来过：照原样删图标，不动任何文件

        string storeFile =
            FindStoreFileForApp(app);


        if(string.IsNullOrWhiteSpace(storeFile))
            return true;


        // 【移动前检查】这个文件真的还在收纳目录里吗？
        // 用户可能自己在资源管理器里把它挪走 / 删掉了
        // 没东西可搬，但绝不静默：告诉用户，然后照常把图标从 Dock 拿掉
        // （这里不能返回 false，否则这个图标会永远删不掉）

        if(!File.Exists(storeFile))
        {

            MessageBox.Show(
                "收纳目录里已经找不到这个文件，没法搬回桌面"
                +
                "（可能被手动移动或删除）。\n\n"
                +
                "本次只从 Dock 里移除图标，不会删除任何文件。\n\n"
                +
                storeFile,
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);


            return true;

        }


        // 桌面上本来就有同名文件吗？
        // true  = 这次是"覆盖"操作，覆盖失败时按这个弹提示
        // 在 try 外面声明，好让 catch 也看得到

        bool hadSameName =
            false;


        // 弹窗里要告诉用户"文件现在在哪儿"，先算好
        // （在 try 外面算，4 个 catch 都能用）

        string storeFolder =
            Path.GetDirectoryName(storeFile) ?? "";


        if(string.IsNullOrWhiteSpace(storeFolder))
            storeFolder = ShortcutStoreDir;


        try
        {

            // 真实桌面路径
            // 严格用 Environment.GetFolderPath 拿，不写死任何路径
            // 用 Desktop：桌面被 OneDrive 重定向时也能拿到用户实际在用的那个桌面

            string desktop =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Desktop);


            if(string.IsNullOrWhiteSpace(desktop))
                throw new InvalidOperationException(
                    "取不到桌面路径");


            if(!Directory.Exists(desktop))
            {

                Directory.CreateDirectory(
                    desktop);

            }


            // 原名 + 原路径搬回桌面
            // 绝不加 (2)、(3) 这种去重后缀

            string target =
                Path.Combine(
                    desktop,
                    Path.GetFileName(storeFile));


            hadSameName =
                File.Exists(target);


            // 桌面已有同名文件 → 直接覆盖（同名不算失败）
            // 走覆盖模式：目标被占用 / 没权限会抛异常，交给下面的 catch
            // 收纳目录里的源文件在移动成功之前一直躺着，绝不会凭空丢

            MoveFileForceOverwrite(
                storeFile,
                target);


            return true;

        }
        catch(UnauthorizedAccessException ex)
        {

            // 搬不回去（权限不足）：弹窗把源文件位置告诉用户
            // 不崩溃、不删文件、也不把这个图标拿掉

            MessageBox.Show(
                BuildMoveBackFailureText(
                    hadSameName
                    ?
                    "桌面已存在同名文件且无法覆盖，请手动处理。"
                    :
                    "文件自动移回桌面失败。",
                    "权限不足，无法移动此文件（可能是公共桌面或系统权限限制）",
                    storeFolder,
                    ex),
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return false;

        }
        catch(PathTooLongException ex)
        {

            // PathTooLongException 是 IOException 的子类
            // 必须写在 IOException 前面，否则永远轮不到它

            MessageBox.Show(
                BuildMoveBackFailureText(
                    hadSameName
                    ?
                    "桌面已存在同名文件且无法覆盖，请手动处理。"
                    :
                    "文件自动移回桌面失败。",
                    "文件路径过长",
                    storeFolder,
                    ex),
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return false;

        }
        catch(IOException ex)
        {

            MessageBox.Show(
                BuildMoveBackFailureText(
                    hadSameName
                    ?
                    "桌面已存在同名文件且无法覆盖，请手动处理。"
                    :
                    "文件自动移回桌面失败。",
                    "文件被占用或OneDrive锁定，无法移动",
                    storeFolder,
                    ex),
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return false;

        }
        catch(Exception ex)
        {

            // 兜底：认不出来的错误也要弹窗，绝不静默失败
            // 不崩溃、不删文件、也不把这个图标拿掉

            MessageBox.Show(
                BuildMoveBackFailureText(
                    hadSameName
                    ?
                    "桌面已存在同名文件且无法覆盖，请手动处理。"
                    :
                    "文件自动移回桌面失败。",
                    "移动失败，未知错误："
                    +
                    ex.Message,
                    storeFolder,
                    ex),
                "AeroDock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);


            return false;

        }

    }


    // 搬回桌面失败时，弹窗里要说的话
    // 4 种失败原因共用同一套排版，只有"原因"那一行不同

    private static string BuildMoveBackFailureText(
        string head,
        string reason,
        string storeFolder,
        Exception ex)
    {

        if(string.IsNullOrWhiteSpace(storeFolder))
            storeFolder = ShortcutStoreDir;


        return head
               +
               "\n\n原因："
               +
               reason
               +
               "\n\n原文件仍完整地躺在收纳目录里：\n"
               +
               storeFolder
               +
               "\n\n请手动移回桌面。\n\n"
               +
               ex.Message;

    }


    // ==========================
    // 开机自启动：设置面板里的玻璃开关
    // 勾选 = 往注册表写入程序路径，随系统一起启动
    // 取消 = 从注册表里删掉这一项
    // 只用 .NET 自带的 Microsoft.Win32.Registry
    // 不引入任何第三方库
    //
    // 注册表位置（HKCU 只影响当前用户，不需要管理员权限）：
    // HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
    // 键名固定：AeroDock（和托盘提示文字一致）
    // 键值 = "程序完整路径"（带英文双引号）
    //        路径里带空格时，没有引号系统会启动失败
    // ==========================


    private const string StartupRunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";


    private const string StartupValueName =
        "AeroDock";


    // 开关是否正在被代码回写
    // 防止 Checked / Unchecked 再绕一圈回来

    private bool isSyncingStartupToggle = false;


    // 开关拨动：勾选 / 取消共用同一个回调

    private void StartupToggle_Changed(
        object sender,
        RoutedEventArgs e)
    {

        // 代码正在回写开关：忽略这次回调

        if(isSyncingStartupToggle)
            return;


        if(StartupToggle == null)
            return;


        SetStartupEnabled(
            StartupToggle.IsChecked == true);

    }


    // 写入 / 删除注册表
    // 整段包在 try 里：注册表被组策略锁住时不让程序崩
    // 最后无论成败，都按注册表的真实状态回写开关

    private void SetStartupEnabled(
        bool enabled)
    {

        try
        {

            if(enabled)
            {

                string exePath =
                    GetExecutablePath();


                if(string.IsNullOrEmpty(exePath))
                    throw new InvalidOperationException(
                        "取不到程序路径");


                // CreateSubKey：键不存在时自动创建
                // 第二个参数 true = 以可写方式打开

                using(Microsoft.Win32.RegistryKey? key =
                    Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                        StartupRunKeyPath,
                        true))
                {

                    if(key != null)
                    {

                        // 值必须带英文双引号
                        // 形如 "C:\Program Files\AeroDock\AeroDock.exe"
                        // 不然路径里的空格会把启动参数拆断

                        key.SetValue(
                            StartupValueName,
                            "\"" + exePath + "\"",
                            Microsoft.Win32.RegistryValueKind.String);

                    }

                }

            }
            else
            {

                // 只删自己这一个键名
                // Run 里别人的启动项一律不碰

                using(Microsoft.Win32.RegistryKey? key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        StartupRunKeyPath,
                        true))
                {

                    if(key != null &&
                       key.GetValue(StartupValueName) != null)
                    {

                        key.DeleteValue(
                            StartupValueName,
                            false);

                    }

                }

            }

        }
        catch(Exception ex)
        {

            // 写失败不静默：弹一次提示
            // 下面回写开关会把它拨回真实状态

            MessageBox.Show(
                "开机自启动设置失败：" + ex.Message,
                "AeroDock");

        }


        // 成功 / 失败都回写开关
        // 面板上显示的一定是注册表里的真实状态

        UpdateStartupToggleState();

    }


    // 注册表里有没有 AeroDock 这一项

    private bool IsStartupEnabledInRegistry()
    {

        try
        {

            using(Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    StartupRunKeyPath,
                    false))
            {

                if(key == null)
                    return false;


                return key.GetValue(StartupValueName) != null;

            }

        }
        catch
        {

            // 读不到就当作没设置
            // 读注册表失败不影响软件启动

            return false;

        }

    }


    // 把注册表里的真实状态写回开关
    // 窗口加载时同步一次，面板打开时也同步一次

    private void UpdateStartupToggleState()
    {

        if(StartupToggle == null)
            return;


        bool enabled =
            IsStartupEnabledInRegistry();


        // 已经一致就不动
        // 免得白白触发一次 Checked / Unchecked

        if(StartupToggle.IsChecked == enabled)
            return;


        isSyncingStartupToggle = true;


        StartupToggle.IsChecked =
            enabled;


        isSyncingStartupToggle = false;

    }


    // 当前程序的可执行文件路径
    // MainModule.FileName 就是真实的 exe 路径
    // （路径里有没有空格都不影响，引号在写注册表时加）

    private string GetExecutablePath()
    {

        try
        {

            using(Process current =
                Process.GetCurrentProcess())
            {

                if(current.MainModule != null &&
                   !string.IsNullOrEmpty(current.MainModule.FileName))
                    return current.MainModule.FileName;

            }

        }
        catch
        {

            // 极少数环境拿不到 MainModule
            // 走下面的兜底

        }


        // 兜底：程序集所在目录里的同名 .exe
        // （.NET Framework 下本来就是 exe 路径）

        string? entry =
            System.Reflection.Assembly.GetEntryAssembly()?.Location;


        if(string.IsNullOrEmpty(entry))
            return "";


        return Path.ChangeExtension(
            entry,
            ".exe");

    }












    // ==========================
    // 窗口激活
    // ==========================


    private void MainWindow_Activated(
        object sender,
        EventArgs e)
    {


        if(!isPinned)
        {


            Keyboard.ClearFocus();



        }



    }












    // ==========================
    // 展板：初始化
    // ==========================


    // ==========================
    // 旧配置兼容：Dock 里存了超过 12 个
    // 多出来的挪进展板（收纳盒）
    // 展板现在全类型通吃，只有路径真的找不到才留在 Dock
    // 宁可多一个，也不丢应用
    // ==========================

    private void MigrateDockOverflowToBoard()
    {


        if(AppManager.Apps.Count <= DockCapacity)
            return;


        bool changed = false;


        // 从后往前摘，摘掉后面的不影响前面的下标

        for(int i = AppManager.Apps.Count - 1;
            i >= DockCapacity;
            i--)
        {


            AppItem app =
                AppManager.Apps[i];


            // 展板存的是路径，读回来得还能认出来

            if(!IsBoardAcceptable(app.LaunchPath))
                continue;


            // 展板上已经有同一个目标就不重复放

            bool exists = false;

            foreach(AppItem item in boardApps)
            {


                if(string.Equals(
                    item.LaunchPath,
                    app.LaunchPath,
                    StringComparison.OrdinalIgnoreCase))
                {


                    exists = true;


                    break;


                }


            }


            if(!exists)
            {


                boardApps.Add(app);


                boardSourcePaths.Add(app.LaunchPath);


            }


            // 从 Dock 摘掉（内部会落盘 dock 配置）

            AppManager.RemoveApp(app);


            changed = true;


        }


        if(changed)
        {


            // 收纳盒配置落盘

            SaveBoardApps();


        }


    }




    private void InitializeBoard()
    {


        // 展板用自己的一份列表
        // 和 Dock 图标互不干扰
        // 放再多也不会把 Dock 撑宽

        BoardPanel.ItemsSource =
            boardApps;


        boardApps.CollectionChanged +=
            BoardApps_CollectionChanged;


        // 从本地配置恢复

        LoadBoardApps();


        // 空态先对齐一次

        UpdateBoardEmptyHint();


    }




    // 展板列表变化
    // 空提示和前两页指示点都可能要重算

    private void BoardApps_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {


        // 展板内容 / 分页变了：气泡的位置已经不准了
        // 直接收掉，免得僵在原地

        DismissIconTip();


        UpdateBoardEmptyHint();


        UpdateBoardPaging();


    }




    // ==========================
    // 展板：拖入任意文件 / 文件夹
    // ==========================


    private void Board_DragOver(
        object sender,
        DragEventArgs e)
    {


        // 和 Dock 一样，文件拖入属于外部手势
        // 顺手清掉可能残留的按下状态

        dockDragArmed = false;


        // 内部拖拽：Dock → 展板

        AppDragPayload? payload =
            GetAppDragPayload(
                e);


        if(payload != null)
        {


            // 从 Dock 拖过来的：收下
            // 展板里自己拖自己：不做排序，先忽略

            e.Effects =
                payload.From ==
                    AppDragFromDock
                ? DragDropEffects.Move
                : DragDropEffects.None;


            e.Handled = true;


            return;


        }


        if(HasBoardDropCandidate(e))
        {

            e.Effects =
                DragDropEffects.Copy;

        }
        else
        {

            e.Effects =
                DragDropEffects.None;

        }


        e.Handled = true;


    }




    // 拖进来的文件里有没有展板能收的

    private bool HasBoardDropCandidate(
        DragEventArgs e)
    {


        string[]? files =
            GetDropFiles(e);


        if(files == null)
            return false;


        foreach(string file in files)
        {

            if(IsBoardAcceptable(file))
                return true;

        }


        return false;


    }




    private static string[]? GetDropFiles(
        DragEventArgs e)
    {


        if(e == null)
            return null;


        if(!e.Data.GetDataPresent(
            DataFormats.FileDrop))
            return null;


        return e.Data.GetData(
            DataFormats.FileDrop) as string[];


    }




    // 展板收什么：全文件类型
    // 不判断后缀，只看路径是不是真的存在
    // .txt / .pdf / .zip / .jpg / .url / .exe / .lnk / 文件夹 一视同仁

    private static bool IsBoardAcceptable(
        string path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return false;


        return
            File.Exists(path)
            ||
            Directory.Exists(path);


    }


    private void Board_Drop(
        object sender,
        DragEventArgs e)
    {


        // 放下就标记已处理
        // 免得冒泡到外层再收一次

        e.Handled = true;


        // 内部拖拽：Dock → 展板
        // 不走文件的流程

        AppDragPayload? payload =
            GetAppDragPayload(
                e);


        if(payload != null)
        {


            if(payload.From ==
                AppDragFromDock)
            {


                MoveDockAppToBoard(
                    payload.App);


            }


            return;


        }


        string[]? files =
            GetDropFiles(e);


        if(files == null)
            return;


        int added = 0;


        int repeat = 0;


        foreach(string file in files)
        {

            if(!IsBoardAcceptable(file))
                continue;


            // 拖拽去重：已经在 Dock / 展板上的不再收
            // 同一个软件的不同快捷方式也只算一个
            // 直接忽略本次拖拽，不再添加第二个

            if(IsAppAlreadyPresent(file))
            {


                if(repeat == 0)
                {


                    MessageBox.Show(

                        DuplicateAppMessage,

                        "AeroDock",

                        MessageBoxButton.OK,

                        MessageBoxImage.Information);


                }


                repeat++;


                continue;


            }


            if(CreateBoardApp(file))
            {

                added++;

            }
            else
            {

                repeat++;

            }

        }


        if(added > 0)
        {

            SaveBoardApps();


            ShowBoardStatus(
                "已放入展板："
                +
                added
                +
                " 个");


            return;

        }


        if(repeat > 0)
        {

            ShowBoardStatus(
                "这个应用已经在 Dock 或展板上了");


            return;

        }


        ShowBoardStatus(
            "拖进来的路径找不到");


    }




    // 造一个展板用的 AppItem
    // 全文件类型：不判断后缀，拿到路径就建条目
    // 启动交给 Windows 外壳，图标交给系统（ShellIconConverter）
    // 只进 boardApps，不进 Dock

    private bool CreateBoardApp(
        string file)
    {


        try
        {


            if(string.IsNullOrWhiteSpace(file))
                return false;


            if(!File.Exists(file) &&
               !Directory.Exists(file))
                return false;


            // 直接用原始路径
            // .lnk 不在这里解析：UseShellExecute 自己会找到快捷方式的目标

            string launchPath =
                file;


            string name =
                GetDisplayName(
                    file);


            AppType type =
                AppType.Exe;


            // Dock 上已经有的应用不进收纳盒
            // 收纳盒只放 Dock 装不下的那部分

            foreach(AppItem dockItem in AppManager.Apps)
            {


                if(string.Equals(
                    dockItem.LaunchPath,
                    launchPath,
                    StringComparison.OrdinalIgnoreCase))
                {


                    return false;


                }


            }




            // 展板上已经有了就不再重复

            foreach(AppItem item in boardApps)
            {

                if(string.Equals(
                    item.LaunchPath,
                    launchPath,
                    StringComparison.OrdinalIgnoreCase))
                {

                    return false;

                }

            }


            AppItem app =
                new AppItem(
                    name,
                    launchPath,
                    type);


            // 图标兜底：拿不到也没关系
            // 界面真正显示的是 ShellIconConverter 从 LaunchPath 取的系统高清图标

            try
            {


                app.IconPath =
                    ShellHelper.ExtractIcon(
                        launchPath);


            }
            catch(Exception ex)
            {


                Debug.WriteLine(
                    "图标提取失败: "
                    +
                    ex.Message);


            }


            boardApps.Add(app);


            boardSourcePaths.Add(file);


            return true;


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板添加失败: "
                +
                ex.Message);


            return false;


        }


    }


    // ==========================
    // 按原始路径把应用从展板上摘掉
    // Dock 收下它之后调用，避免同一个应用两边都占位
    // ==========================

    private void RemoveBoardAppBySourcePath(
        string file)
    {


        if(string.IsNullOrWhiteSpace(file))
            return;


        for(int i = 0;
            i < boardApps.Count &&
            i < boardSourcePaths.Count;
            i++)
        {


            if(!string.Equals(
                boardSourcePaths[i],
                file,
                StringComparison.OrdinalIgnoreCase))
                continue;


            boardApps.RemoveAt(i);


            boardSourcePaths.RemoveAt(i);


            SaveBoardApps();


            return;


        }


    }




    // ==========================
    // 展板：左键启动
    // ==========================


    private void BoardIcon_Click(
        object sender,
        MouseButtonEventArgs e)
    {


        e.Handled = true;


        // 刚刚拖过一次
        // 本次松手不启动应用

        if(boardIconDragSuppressClick)
        {

            boardIconDragSuppressClick = false;

            return;

        }


        AppItem? app =
            GetBoardApp(sender);


        if(app == null)
            return;


        // ==========================
        // 批量模式：点图标 = 切换选中 / 取消选中
        // 绝不启动应用
        // 普通模式：继续走下面的 OpenByShell，原有启动逻辑不变
        // ==========================

        if(isBoardBatchMode)
        {

            ToggleBoardSelection(app);

            return;

        }


        // 统一交给 Windows 外壳启动
        // 不判断后缀：txt / pdf / zip / jpg / url / 文件夹 / exe 都走这里

        OpenByShell(
            app.LaunchPath);


        ShowBoardStatus(
            "正在启动 "
            +
            app.Name);


    }




    // ==========================
    // 展板：右键移除
    // ==========================


    private void BoardIcon_Delete(
        object sender,
        MouseButtonEventArgs e)
    {


        e.Handled = true;


        // 批量模式下右键不参与删除
        // 避免和“删除选中”抢同一套确认流程

        if(isBoardBatchMode)
            return;


        AppItem? app =
            GetBoardApp(sender);


        if(app == null)
            return;


        int index =
            boardApps.IndexOf(app);


        if(index < 0)
            return;


        // 右键移除先确认
        // 和 Dock 图标一致：只从展板拿掉，不删电脑里的文件
        // 【本次修复】弹框前打标记、弹完清标记
        // 弹框会抢走窗口焦点 → 触发 Window_Deactivated
        // 有标记在，展板这次绝对不会被误关
        // 用 try / finally：点"是"、"否"还是直接关掉框，标记都会被清干净

        MessageBoxResult result;

        isRightClickMenuOpen = true;

        try
        {

            result =
                MessageBox.Show(
                    "是否从展板移除「"
                    +
                    app.Name
                    +
                    "」？\n\n不会删除电脑中的文件。",
                    "AeroDock",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

        }
        finally
        {

            isRightClickMenuOpen = false;

        }


        if(result !=
            MessageBoxResult.Yes)
        {


            return;


        }


        // 展板和 Dock 一个规矩：
        // 模式B 下先把收纳目录里的源文件搬回桌面
        // 搬不回去就保留这个展板图标，绝不丢文件
        // 模式A 下这个方法什么都不做，直接返回 true

        if(!TryMoveBackToDesktop(app))
            return;


        boardApps.RemoveAt(index);


        if(index < boardSourcePaths.Count)
        {

            boardSourcePaths.RemoveAt(index);

        }


        SaveBoardApps();


        ShowBoardStatus(
            "已从展板移除 "
            +
            app.Name);


    }




    // 槽位的 DataContext 就是 AppItem

    private static AppItem? GetBoardApp(
        object sender)
    {


        FrameworkElement? element =
            sender as FrameworkElement;


        if(element == null)
            return null;


        return element.DataContext as AppItem;


    }




    // ==========================
    // 展板：悬停放大 + 玻璃高光
    // 和 Dock 图标一个套路
    // 每次换一个新的 ScaleTransform 再动到目标值
    // ==========================


    private void BoardIcon_MouseEnter(
        object sender,
        MouseEventArgs e)
    {


        Border? border =
            sender as Border;


        if(border == null)
            return;


        ScaleTransform scale =
            new ScaleTransform(
                1,
                1);


        border.RenderTransform =
            scale;


        AnimateBoardSlot(
            scale,
            BoardHoverScale,
            BoardHoverInDuration);


        Border? glow =
            FindBoardGlow(border);


        if(glow != null)
        {

            FadeElement(
                glow,
                1,
                0,
                BoardHoverInDuration,
                EasingMode.EaseOut);

        }


    }




    private void BoardIcon_MouseLeave(
        object sender,
        MouseEventArgs e)
    {


        Border? border =
            sender as Border;


        if(border == null)
            return;


        ScaleTransform scale =
            new ScaleTransform(
                BoardHoverScale,
                BoardHoverScale);


        border.RenderTransform =
            scale;


        AnimateBoardSlot(
            scale,
            1,
            BoardHoverOutDuration);


        Border? glow =
            FindBoardGlow(border);


        if(glow != null)
        {

            FadeElement(
                glow,
                0,
                0,
                BoardHoverOutDuration,
                EasingMode.EaseOut);

        }


    }




    // 一条 CubicEase 同时驱动 X / Y
    // 两个属性各给一条动画，互不共用时钟

    private static void AnimateBoardSlot(
        ScaleTransform scale,
        double to,
        double durationMilliseconds)
    {


        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreateBoardScaleAnimation(
                to,
                durationMilliseconds));


        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateBoardScaleAnimation(
                to,
                durationMilliseconds));


    }




    private static DoubleAnimation CreateBoardScaleAnimation(
        double to,
        double durationMilliseconds)
    {


        return new DoubleAnimation
        {


            To =
            to,


            Duration =
            TimeSpan.FromMilliseconds(
                durationMilliseconds),


            EasingFunction =
            new CubicEase
            {

                EasingMode =
                EasingMode.EaseOut

            }


        };


    }




    // 模板里的 x:Name 不会生成字段
    // 只能从槽位根节点往下找高光层

    private static Border? FindBoardGlow(
        DependencyObject? root)
    {


        if(root == null)
            return null;


        int count =
            VisualTreeHelper.GetChildrenCount(
                root);


        for(int i = 0; i < count; i++)
        {


            DependencyObject child =
                VisualTreeHelper.GetChild(
                    root,
                    i);


            Border? border =
                child as Border;


            if(border != null &&
               border.Name == "BoardIconGlow")
            {

                return border;

            }


            Border? found =
                FindBoardGlow(child);


            if(found != null)
                return found;


        }


        return null;


    }


    // ==========================
    // 展板：本地配置
    // 一行一个原始路径
    // ==========================


    private void LoadBoardApps()
    {


        try
        {


            if(!File.Exists(BoardStorePath))
                return;


            string[] lines =
                File.ReadAllLines(BoardStorePath);


            foreach(string line in lines)
            {

                string path =
                    line.Trim();


                if(path.Length == 0)
                    continue;


                if(!IsBoardAcceptable(path))
                    continue;


                CreateBoardApp(path);

            }


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板读取失败: "
                +
                ex.Message);


        }


    }




    private void SaveBoardApps()
    {


        try
        {


            string? directory =
                Path.GetDirectoryName(
                    BoardStorePath);


            if(!string.IsNullOrEmpty(directory) &&
               !Directory.Exists(directory))
            {

                Directory.CreateDirectory(
                    directory);

            }


            File.WriteAllLines(
                BoardStorePath,
                boardSourcePaths);


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "展板保存失败: "
                +
                ex.Message);


        }


    }




    // ==========================
    // 展板：空态引导
    // ==========================


    private void UpdateBoardEmptyHint()
    {


        bool empty =
            boardApps.Count == 0;


        FadeElement(
            BoardEmptyHint,
            empty ? 1 : 0,
            0,
            empty ? 160 : 240,
            EasingMode.EaseOut);


    }


    // ==========================
    // 展板：分页与指示点
    // 只做前两页
    // ==========================


    // 一个槽位实际占 120 x 120
    // 用真实视口算出每页几列几行

    private void UpdateBoardPaging()
    {


        double viewportWidth =
            BoardScroll.ViewportWidth > 0
            ?
            BoardScroll.ViewportWidth
            :
            BoardFallbackPageWidth;


        double viewportHeight =
            BoardScroll.ViewportHeight > 0
            ?
            BoardScroll.ViewportHeight
            :
            BoardFallbackPageHeight;


        double slot =
            BoardSlotSize
            +
            BoardSlotMargin
            *
            2;


        int columns =
            (int)Math.Floor(
                viewportWidth
                /
                slot);


        int rows =
            (int)Math.Floor(
                viewportHeight
                /
                slot);


        int perPage =
            columns
            *
            rows;


        // 兜底不小于 4 x 5
        // 布局还没跑完时 Viewport 是 0，别算出个 0

        boardItemsPerPage =
            perPage > 20
            ?
            perPage
            :
            20;


        UpdateBoardDots();


    }




    // 内容一共用了几页（最多前两页）

    private int GetBoardUsedPages()
    {


        int perPage =
            boardItemsPerPage > 0
            ?
            boardItemsPerPage
            :
            20;


        int pages =
            (boardApps.Count + perPage - 1)
            /
            perPage;


        if(pages < 1)
            pages = 1;


        if(pages > BoardPageCount)
            pages = BoardPageCount;


        return pages;


    }




    // 视口现在停在第几页
    // 按可滚动范围均分：滑过一半就算翻到下一页

    private int GetBoardVisiblePage()
    {


        int pages =
            GetBoardUsedPages();


        if(pages <= 1)
            return 1;


        double scrollable =
            BoardScroll.ExtentHeight
            -
            BoardScroll.ViewportHeight;


        if(scrollable <= 0)
            return 1;


        double span =
            scrollable
            /
            (pages - 1);


        int page =
            1
            +
            (int)Math.Round(
                BoardScroll.VerticalOffset
                /
                span);


        if(page < 1)
            page = 1;


        if(page > BoardPageCount)
            page = BoardPageCount;


        return page;


    }




    // 一页装得下就整组淡出
    // 超过一页才亮出来

    private void UpdateBoardDots()
    {


        int pages =
            GetBoardUsedPages();


        bool show =
            pages >= 2;


        if(pages != boardLastPages)
        {

            boardLastPages =
                pages;


            FadeElement(
                BoardPageDots,
                show ? 1 : 0,
                0,
                220,
                EasingMode.EaseOut);

        }


        BoardPageDot2.Visibility =
            show
            ?
            Visibility.Visible
            :
            Visibility.Collapsed;


        UpdateBoardActiveDot(
            GetBoardVisiblePage());


    }




    // 当前页那颗点实心，另一颗半透明

    private void UpdateBoardActiveDot(
        int page)
    {


        if(page == boardLastActivePage)
            return;


        boardLastActivePage =
            page;


        FadeElement(
            BoardPageDot1,
            page <= 1 ? 1 : 0.32,
            0,
            160,
            EasingMode.EaseOut);


        FadeElement(
            BoardPageDot2,
            page >= 2 ? 1 : 0.32,
            0,
            160,
            EasingMode.EaseOut);


    }




    // 滚轮往下滑：只更新哪颗点是实心的
    // 不做翻页动画也不吸页，保持原生滚动的手感

    private void BoardScroll_ScrollChanged(
        object sender,
        ScrollChangedEventArgs e)
    {


        if(e.ViewportHeightChange != 0 ||
           e.ExtentHeightChange != 0)
        {

            UpdateBoardPaging();


            return;

        }


        UpdateBoardActiveDot(
            GetBoardVisiblePage());


    }


    // ==========================
    // 展板：一句话反馈
    // 淡入 → 停留 → 自动淡出
    // 一条 Storyboard 搞定，不用定时器
    // ==========================


    private void ShowBoardStatus(
        string message)
    {


        BoardStatus.Text =
            message;


        DoubleAnimationUsingKeyFrames fade =
            new DoubleAnimationUsingKeyFrames
            {


                FillBehavior =
                FillBehavior.Stop


            };


        fade.KeyFrames.Add(
            new DiscreteDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        fade.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(220))));


        fade.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(1700))));


        fade.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(2300))));


        Storyboard.SetTarget(
            fade,
            BoardStatus);


        Storyboard.SetTargetProperty(
            fade,
            new PropertyPath(
                UIElement.OpacityProperty));


        Storyboard storyboard =
            new Storyboard();


        storyboard.Children.Add(
            fade);


        storyboard.Begin();


    }


    // ==========================
    // ==========================
    // 创建系统托盘图标
    // 图标：Assets\app.ico
    // 提示文字：AeroDock
    // 右键菜单：显示 Dock / 退出软件
    // ==========================

    private void InitializeTrayIcon()
    {


        WinForms.ContextMenuStrip menu =
            new WinForms.ContextMenuStrip();


        WinForms.ToolStripMenuItem showItem =
            new WinForms.ToolStripMenuItem("显示 Dock");


        showItem.Click +=
            TrayShow_Click;


        WinForms.ToolStripMenuItem exitItem =
            new WinForms.ToolStripMenuItem("退出软件");


        exitItem.Click +=
            TrayExit_Click;


        menu.Items.Add(showItem);


        // 【本次新增】AeroDock设置中心
        // 点它打开独立的设置窗口
        // （原来 Dock 齿轮面板里的滑块 / 开关全部搬到了那里）

        WinForms.ToolStripMenuItem settingsItem =
            new WinForms.ToolStripMenuItem(
                "AeroDock设置中心");


        settingsItem.Click +=
            TraySettings_Click;


        menu.Items.Add(settingsItem);


        menu.Items.Add(exitItem);


        trayMenu = menu;


        trayIcon =
            new WinForms.NotifyIcon();


        trayIcon.Text = "AeroDock";


        trayIcon.Icon = LoadTrayIcon();


        trayIcon.ContextMenuStrip = menu;


        trayIcon.Visible = true;


    }




    // ==========================
    // 取托盘图标
    // 优先读工程里现有的 Assets\app.ico（WPF 资源）
    // 读不到就退回可执行文件自带图标
    // 再不行用系统默认图标
    // 全程不抛异常：托盘出问题也不能拖垮主程序
    // ==========================

    private System.Drawing.Icon LoadTrayIcon()
    {

        try
        {

            Uri uri =
                new Uri(
                    "pack://application:,,,/Assets/app.ico");


            System.Windows.Resources.StreamResourceInfo? info =
                Application.GetResourceStream(uri);


            if(info != null)
            {

                // 流保持打开，退出时统一释放

                Stream stream =
                    info.Stream;


                trayIconStream = stream;


                return new System.Drawing.Icon(
                    stream);

            }

        }
        catch
        {

        }




        try
        {

            string? exePath =
                Environment.ProcessPath;


            if(!string.IsNullOrEmpty(exePath))
            {

                System.Drawing.Icon? exeIcon =
                    System.Drawing.Icon.ExtractAssociatedIcon(
                        exePath);


                if(exeIcon != null)
                    return exeIcon;

            }

        }
        catch
        {

        }




        return System.Drawing.SystemIcons.Application;

    }




    // ==========================
    // 释放托盘资源
    // 不释放的话图标会残留在通知区域
    // ==========================

    private void DisposeTrayIcon()
    {

        if(trayIcon != null)
        {

            trayIcon.Visible = false;


            trayIcon.Dispose();


            trayIcon = null;

        }




        if(trayMenu != null)
        {

            trayMenu.Dispose();


            trayMenu = null;

        }




        if(trayIconStream != null)
        {

            trayIconStream.Dispose();


            trayIconStream = null;

        }




    }




    // ==========================
    // 托盘菜单：显示 Dock
    // ==========================

    private void TrayShow_Click(
        object? sender,
        EventArgs e)
    {

        Show();


        Activate();


        // 重新贴回桌面层
        // 与启动时保持一致
        // 免得从托盘回来以后窗口压在别的窗口上面

        DesktopLayerManager.Refresh(
            this);

    }




    // ==========================
    // 托盘菜单：退出软件
    // 只有这里是真退出
    // 走 Application.Current.Shutdown()
    // 这样原有的 OnClosed 释放逻辑照常执行
    // ==========================

    private void TrayExit_Click(
        object? sender,
        EventArgs e)
    {

        isExiting = true;


        DisposeTrayIcon();


        // 【本次改动】设置中心窗口如果开着，先真正关掉再退出
        // 它在 OnClosing 里默认只隐藏（Cancel = true），
        // 可见窗口拒绝关闭会让 Application.Shutdown 卡住
        // CloseForShutdown() 会放行这次关闭（顺手把设置补存一遍）

        if(settingsCenterWindow != null)
            settingsCenterWindow.CloseForShutdown();


        Application.Current.Shutdown();

    }




    // ==========================
    // 关闭窗口 = 隐藏窗口
    // 程序继续在后台运行（托盘图标还在）
    // 想彻底退出请用托盘菜单的“退出软件”
    // ==========================

    protected override void OnClosing(
        System.ComponentModel.CancelEventArgs e)
    {

        if(!isExiting)
        {

            e.Cancel = true;


            Hide();

        }




        base.OnClosing(e);

    }




    // ==========================
    // 关闭释放
    // ==========================


    protected override void OnClosed(
        EventArgs e)
    {



        if(watcher != null)
        {


            watcher.Stop();



            watcher = null;



        }







        CompositionTarget.Rendering -=
            DockRendering;







        // 托盘图标必须显式释放
        // 否则图标会赖在通知区域不走

        DisposeTrayIcon();




        // 展板配置落盘

        SaveBoardApps();


        // 【本次改动】兜底：不论哪条路径关掉 Dock，都顺手收拾设置中心
        // 正常退出走 TrayExit_Click 已经关过一次（重复 Close 是空操作）

        if(settingsCenterWindow != null)
            settingsCenterWindow.CloseForShutdown();


        base.OnClosed(e);



    }






    // ==========================================================
    // 【本次新增】托盘 →「AeroDock设置中心」
    // ==========================================================
    // 只做两件事：
    //   1) 打开 / 前置那个独立的设置窗口（SettingsWindow）
    //   2) 把窗口上的值转发给原来就有的私有方法
    // 图标提取 / 拖拽 / 九点阵 / 悬停提示 / 桌面双模式 /
    // 挤出动画 / 右键删除：一行都没有改
    // ==========================================================


    // 设置中心窗口（单例，重复点托盘不会开出第二个）

    private SettingsWindow? settingsCenterWindow = null;


    // 托盘菜单：AeroDock设置中心

    private void TraySettings_Click(
        object? sender,
        EventArgs e)
    {

        ShowDockSettingsCenter();

    }


    private void ShowDockSettingsCenter()
    {

        try
        {

            if(settingsCenterWindow == null)
                settingsCenterWindow =
                    new SettingsWindow(this);


            // 每次打开都重新读一遍本地存档
            // 界面上显示的一定是当前真正生效的值

            settingsCenterWindow.ReloadFromStore();


            if(!settingsCenterWindow.IsVisible)
                settingsCenterWindow.Show();


            settingsCenterWindow.Activate();


            // 托盘菜单刚关掉，前台还挂在系统托盘上
            // 这里顶一下保证窗口显示在最前面，顶完立刻取消置顶

            settingsCenterWindow.Topmost =
                true;

            settingsCenterWindow.Topmost =
                false;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "打开设置中心失败: "
                +
                ex.Message);

        }

    }


    // ----------------------------------------------------------
    // 端口：材质两个滑块（背景透明度 / 背景模糊度）
    // ----------------------------------------------------------

    public void SettingsCenter_ApplyMaterial(
        double opacity,
        double blur)
    {

        // 原来就有的：夹区间 → 灌进 Dock 那两个滑块 → 刷材质
        // 存档由设置中心窗口按同一套写法落到同一个文件

        ApplyDockSettings(
            opacity,
            blur);

    }


    // ----------------------------------------------------------
    // 端口：材质预设（0 = 经典亚克力 / 1 = 极简纯色 / 2 = 高光质感）
    // 设置中心下拉框切换时调用；opacity / blur 两个滑块的值保持不变
    // ----------------------------------------------------------

    public int SettingsCenter_MaterialPreset
    {

        get
        {

            return
                (int)dockMaterialPreset;

        }

    }


    public void SettingsCenter_ApplyMaterialPreset(
        int preset)
    {

        ApplyMaterialPreset(
            NormalizeMaterialPreset(
                preset),
            true);

    }


    // ----------------------------------------------------------
    // 端口：展板材质两个滑块（展板透明度 / 展板模糊度）
    // 与 Dock 的 SettingsCenter_ApplyMaterial 完全独立
    // ----------------------------------------------------------

    public double SettingsCenter_LaunchBoardOpacity
    {

        get
        {

            return launchBoardOpacityValue;

        }

    }


    public double SettingsCenter_LaunchBoardBlur
    {

        get
        {

            return launchBoardBlurValue;

        }

    }


    public void SettingsCenter_ApplyLaunchBoardMaterial(
        double opacity,
        double blur)
    {

        // 手改过的配置文件可能塞进来 NaN / 1000 之类
        // 一律夹回合法区间，不给界面留坑

        launchBoardOpacityValue =
            ClampDockSetting(
                opacity,
                0.1,
                1.0,
                0.8);


        launchBoardBlurValue =
            ClampDockSetting(
                blur,
                0.0,
                100.0,
                30.0);


        ApplyLaunchBoardMaterial();

    }


    // ----------------------------------------------------------
    // 端口：展板自定义壁纸
    // 与材质滑块完全独立：
    //   材质 = 玻璃本身（透明度 / 模糊度）
    //   壁纸 = 玻璃之上、内容之下的独立一层
    // 清除壁纸只清空 ImageBrush / 收起这一层，
    // 不碰 launchBoardOpacityValue / launchBoardBlurValue
    // ----------------------------------------------------------

    public string SettingsCenter_LaunchBoardWallpaperPath
    {

        get
        {

            return launchBoardWallpaperPath;

        }

    }


    public double SettingsCenter_LaunchBoardWallpaperOpacity
    {

        get
        {

            return launchBoardWallpaperOpacityValue;

        }

    }


    public bool SettingsCenter_ApplyLaunchBoardWallpaper(
        string? path,
        double opacity)
    {

        launchBoardWallpaperOpacityValue =
            ClampDockSetting(
                opacity,
                0.0,
                1.0,
                0.8);


        return ApplyLaunchBoardWallpaper(
            path);

    }


    public void SettingsCenter_ApplyLaunchBoardWallpaperOpacity(
        double opacity)
    {

        try
        {

            launchBoardWallpaperOpacityValue =
                ClampDockSetting(
                    opacity,
                    0.0,
                    1.0,
                    0.8);


            if(LaunchBoardWallpaper != null)
            {

                LaunchBoardWallpaper.Opacity =
                    launchBoardWallpaperOpacityValue;

                LaunchBoardWallpaper.InvalidateVisual();

            }

        }
        catch(Exception ex)
        {

            ShowLaunchBoardWallpaperError(
                "不透明度设置失败",
                "调整展板壁纸不透明度时出错。",
                ex);

        }

    }


    // 设置中心拖动 / 滚轮时实时调用：
    // 只改 ImageBrush.Viewbox，不重建画刷、不重新解码。
    // Viewbox 直接框源图，UniformToFill 原本裁掉的边缘也能拖回来。
    // 参数全部夹到安全区间，参考尺寸为 0 时回退 300 x 216.67
    public void SettingsCenter_ApplyLaunchBoardWallpaperCrop(
        double scale,
        double offsetX,
        double offsetY,
        double referenceWidth,
        double referenceHeight)
    {

        try
        {

            double safeScale =
                ClampDockSetting(
                    scale,
                    LaunchBoardWallpaperCropScaleMin,
                    LaunchBoardWallpaperCropScaleMax,
                    1.0);


            double safeReferenceWidth =
                ClampDockSetting(
                    referenceWidth,
                    1.0,
                    10000.0,
                    LaunchBoardWallpaperReferenceWidth);

            double safeReferenceHeight =
                ClampDockSetting(
                    referenceHeight,
                    1.0,
                    10000.0,
                    LaunchBoardWallpaperReferenceHeight);


            double referenceAspect =
                safeReferenceWidth /
                safeReferenceHeight;


            ImageBrush? brush =
                LaunchBoardWallpaper != null
                ? LaunchBoardWallpaper.Background as ImageBrush
                : null;

            BitmapSource? source =
                brush != null
                ? brush.ImageSource as BitmapSource
                : null;

            double sourceAspect =
                source != null &&
                source.PixelWidth > 0 &&
                source.PixelHeight > 0
                ? source.PixelWidth / (double)source.PixelHeight
                : referenceAspect;


            // 与设置中心完全相同的边界规则：
            // 1.0 缩放时先算出整张图铺满参考框后的显示尺寸，
            // 超出的部分就是允许拖动的范围

            double displayedWidth =
                Math.Max(
                    safeReferenceWidth,
                    safeReferenceHeight * sourceAspect);

            double displayedHeight =
                Math.Max(
                    safeReferenceHeight,
                    safeReferenceWidth / sourceAspect);


            double maxOffsetX =
                Math.Max(
                    0.0,
                    (displayedWidth * safeScale - safeReferenceWidth) /
                    2.0);

            double maxOffsetY =
                Math.Max(
                    0.0,
                    (displayedHeight * safeScale - safeReferenceHeight) /
                    2.0);


            double safeOffsetX =
                ClampDockSetting(
                    offsetX,
                    -maxOffsetX,
                    maxOffsetX,
                    0.0);

            double safeOffsetY =
                ClampDockSetting(
                    offsetY,
                    -maxOffsetY,
                    maxOffsetY,
                    0.0);


            launchBoardWallpaperScaleValue =
                safeScale;

            launchBoardWallpaperOffsetXValue =
                safeOffsetX;

            launchBoardWallpaperOffsetYValue =
                safeOffsetY;

            launchBoardWallpaperCropReferenceWidth =
                safeReferenceWidth;

            launchBoardWallpaperCropReferenceHeight =
                safeReferenceHeight;


            if(brush == null)
                return;


            if(LaunchBoardWallpaper == null)
                return;

            var wallpaperLayer = LaunchBoardWallpaper;


            // 设置中心预览的取景框比例与展板一致（900:650），
            // 偏移量先按它换算成源图里的归一化中心，
            // 展板再按自己的实际宽高比取同样中心的窗口

            double previewBaseWidth =
                Math.Min(1.0, referenceAspect / sourceAspect);

            double previewBaseHeight =
                Math.Min(1.0, sourceAspect / referenceAspect);


            double centerX =
                0.5 -
                (safeOffsetX / safeReferenceWidth) *
                previewBaseWidth / safeScale;

            double centerY =
                0.5 -
                (safeOffsetY / safeReferenceHeight) *
                previewBaseHeight / safeScale;


            double boardWidth =
                wallpaperLayer.ActualWidth;

            double boardHeight =
                wallpaperLayer.ActualHeight;


            // 展板折叠时 ActualWidth / Height 还是 0，
            // 依次退回控件声明的 900 x 650，最后才用参考尺寸

            if(!(boardWidth > 0) ||
               double.IsNaN(boardWidth) ||
               double.IsInfinity(boardWidth))
            {

                double declaredWidth =
                    wallpaperLayer.Width;

                if(!(declaredWidth > 0) ||
                   double.IsNaN(declaredWidth) ||
                   double.IsInfinity(declaredWidth))
                {

                    declaredWidth =
                        LaunchBoard != null
                        ? LaunchBoard.Width
                        : double.NaN;

                }


                boardWidth =
                    declaredWidth > 0 &&
                    !double.IsNaN(declaredWidth) &&
                    !double.IsInfinity(declaredWidth)
                    ? declaredWidth
                    : safeReferenceWidth;

            }


            if(!(boardHeight > 0) ||
               double.IsNaN(boardHeight) ||
               double.IsInfinity(boardHeight))
            {

                double declaredHeight =
                    wallpaperLayer.Height;

                if(!(declaredHeight > 0) ||
                   double.IsNaN(declaredHeight) ||
                   double.IsInfinity(declaredHeight))
                {

                    declaredHeight =
                        LaunchBoard != null
                        ? LaunchBoard.Height
                        : double.NaN;

                }


                boardHeight =
                    declaredHeight > 0 &&
                    !double.IsNaN(declaredHeight) &&
                    !double.IsInfinity(declaredHeight)
                    ? declaredHeight
                    : safeReferenceHeight;

            }


            double boardAspect =
                boardWidth /
                boardHeight;


            double boardBaseWidth =
                Math.Min(1.0, boardAspect / sourceAspect);

            double boardBaseHeight =
                Math.Min(1.0, sourceAspect / boardAspect);


            double windowWidth =
                boardBaseWidth / safeScale;

            double windowHeight =
                boardBaseHeight / safeScale;


            centerX =
                ClampDockSetting(
                    centerX,
                    windowWidth / 2.0,
                    1.0 - windowWidth / 2.0,
                    0.5);

            centerY =
                ClampDockSetting(
                    centerY,
                    windowHeight / 2.0,
                    1.0 - windowHeight / 2.0,
                    0.5);


            brush.Stretch =
                Stretch.UniformToFill;

            brush.AlignmentX =
                AlignmentX.Center;

            brush.AlignmentY =
                AlignmentY.Center;

            brush.ViewboxUnits =
                BrushMappingMode.RelativeToBoundingBox;

            brush.Viewbox =
                new Rect(
                    centerX - windowWidth / 2.0,
                    centerY - windowHeight / 2.0,
                    windowWidth,
                    windowHeight);


            // 旧版本用的是 RelativeTransform；这里显式清掉，
            // 避免读到旧对象 / 旧代码留下的缩放和位移

            brush.RelativeTransform =
                Transform.Identity;


            wallpaperLayer.InvalidateVisual();

        }
        catch(Exception ex)
        {

            // 实时拖动期间绝不弹窗，只写调试输出
            // 失败时保持上一次成功的变换，界面不会闪

            Debug.WriteLine(
                "展板壁纸：应用裁剪参数失败: " +
                ex.Message);

        }

    }


    // 展板尺寸不总是 900 x 650：小屏幕打开时会按工作区整体缩放，
    // 但始终保持 900:650。尺寸一变就按当前控件实际宽高重新套一次
    // Viewbox，确保打开 / 收起 / 改 DPI / 换显示器后取景窗口仍然精确。

    private void LaunchBoard_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {

        if(string.IsNullOrWhiteSpace(launchBoardWallpaperPath))
            return;


        if(LaunchBoardWallpaper == null ||
           !(LaunchBoardWallpaper.Background is ImageBrush))
            return;


        SettingsCenter_ApplyLaunchBoardWallpaperCrop(
            launchBoardWallpaperScaleValue,
            launchBoardWallpaperOffsetXValue,
            launchBoardWallpaperOffsetYValue,
            launchBoardWallpaperCropReferenceWidth,
            launchBoardWallpaperCropReferenceHeight);

    }


    public bool SettingsCenter_ClearLaunchBoardWallpaper()
    {

        // 只有真正清干净了才把内存里的路径置空
        // 这样清除失败时设置中心可以恢复上一张，不会出现
        // “配置已清空、画面上却还留着图”的错位状态

        bool cleared =
            ClearLaunchBoardWallpaperVisual();


        if(cleared)
        {

            launchBoardWallpaperPath =
                "";

        }


        return cleared;

    }


    // 应用路径：路径为空 / 文件不存在 / 解码失败 → 收起壁纸层
    // 成功返回 true，设置中心可以据此提示用户

    private bool ApplyLaunchBoardWallpaper(
        string? path)
    {

        string value =
            (path ?? string.Empty).Trim();


        // 空路径 = 用户明确清除壁纸，不是错误

        if(value.Length == 0)
        {

            bool cleared =
                ClearLaunchBoardWallpaperVisual();


            if(cleared)
            {

                launchBoardWallpaperPath =
                    "";

            }


            return cleared;

        }


        try
        {

            if(LaunchBoardWallpaper == null)
            {

                throw new InvalidOperationException(
                    "XAML 中没有找到 LaunchBoardWallpaper 控件，" +
                    "壁纸没有可以绘制的目标层。");

            }


            if(!File.Exists(value))
            {

                throw new FileNotFoundException(
                    "找不到壁纸文件，文件可能已被移动、重命名或删除。",
                    value);

            }


            // 读取 + 解码：失败会带着真实原因抛到下面的 catch
            // 这里不再返回 null 静默结束

            BitmapImage image =
                LoadBitmapWithoutLock(
                    value);


            if(image.PixelWidth <= 0 ||
               image.PixelHeight <= 0)
            {

                throw new InvalidOperationException(
                    "图片解码后的尺寸为 0×0，文件可能已损坏。");

            }


            // 关键修复：真正生成一个新的 ImageBrush 并赋给 Background
            // 不再依赖 XAML 里那个可能被冻结 / 没有接到控件上的旧画刷

            ImageBrush brush =
                new ImageBrush(
                    image)
                {

                    Stretch =
                        Stretch.UniformToFill,

                    AlignmentX =
                        AlignmentX.Center,

                    AlignmentY =
                        AlignmentY.Center

                };


            // 不冻结画刷：设置中心拖动 / 滚轮时要实时修改
            // Viewbox。冻结后这些属性全部只读。
            // 图片本身已经由 LoadBitmapWithoutLock 冻结，
            // 不解码新文件时不会产生额外的线程 / 文件占用。

            brush.RelativeTransform =
                Transform.Identity;


            LaunchBoardWallpaper.Background =
                brush;


            // 显式层级：壁纸在玻璃层（默认 0）之上
            // 图标 / 文字 / 按钮等浮层统一为 2

            Panel.SetZIndex(
                LaunchBoardWallpaper,
                1);

            if(BoardContentLayer != null)
            {

                Panel.SetZIndex(
                    BoardContentLayer,
                    2);

            }


            LaunchBoardWallpaper.Opacity =
                launchBoardWallpaperOpacityValue;

            LaunchBoardWallpaper.Visibility =
                Visibility.Visible;


            // 只有真正赋值成功后才记住路径

            launchBoardWallpaperPath =
                value;


            // 强制走一次布局 / 重绘
            // 展板折叠时调用也安全，下次打开会直接拿到新图

            LaunchBoardWallpaper.InvalidateVisual();

            LaunchBoard?.UpdateLayout();


            // 换图 / 启动恢复时立刻套用当前裁剪参数
            // 新画刷默认是未变换的，这里把它拉回用户上次的位置

            SettingsCenter_ApplyLaunchBoardWallpaperCrop(
                launchBoardWallpaperScaleValue,
                launchBoardWallpaperOffsetXValue,
                launchBoardWallpaperOffsetYValue,
                launchBoardWallpaperCropReferenceWidth,
                launchBoardWallpaperCropReferenceHeight);


            Debug.WriteLine(
                "展板壁纸：已应用 " +
                value +
                "，尺寸 " +
                image.PixelWidth +
                "×" +
                image.PixelHeight +
                "，Opacity=" +
                launchBoardWallpaperOpacityValue);


            return true;

        }
        catch(Exception ex)
        {

            // 失败时把壁纸层收干净，绝不留半张图或不可见状态
            // 这里不再弹第二次窗，真正的错误由下面统一弹

            ClearLaunchBoardWallpaperVisual(
                false);

            launchBoardWallpaperPath =
                "";


            ShowLaunchBoardWallpaperError(
                "加载失败",
                "图片解码失败，请确保图片未损坏。\n\n" +
                "无法把壁纸铺到展板上。\n" +
                "文件：" +
                value +
                "\n" +
                "如果图片是 WebP / HEIC 等格式，" +
                "当前系统可能没有对应解码器，" +
                "请先用画图或看图软件另存为 PNG / JPG 再试。",
                ex);


            return false;

        }

    }


    private bool ClearLaunchBoardWallpaperVisual(
        bool showErrors = true)
    {

        try
        {

            // 直接断开 Background，避免旧图残留在画刷里

            if(LaunchBoardWallpaper != null)
            {

                LaunchBoardWallpaper.Background =
                    null;

                LaunchBoardWallpaper.Visibility =
                    Visibility.Collapsed;

                LaunchBoardWallpaper.InvalidateVisual();

                LaunchBoard?.UpdateLayout();

            }


            return true;

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "展板壁纸：清除壁纸层失败: " +
                ex.Message);


            if(showErrors)
            {

                ShowLaunchBoardWallpaperError(
                    "清除失败",
                    "清空展板壁纸层时出错。",
                    ex);

            }


            return false;

        }

    }


    // 关键：ReadAllBytes + MemoryStream + BitmapCacheOption.OnLoad
    // 不再让 WPF 通过 FileStream / Uri 直接接触磁盘文件：
    //   1. File.ReadAllBytes 一次性把图片读成 byte[]
    //   2. byte[] 塞进 MemoryStream，BitmapImage 只从内存解码
    //   3. OnLoad 让 EndInit 时就把像素全部读进内存
    //   4. Freeze 后图片可跨线程使用，且不会占用原文件
    // 这样图片文件从头到尾都没有被打开句柄，用户随时可以
    // 删除 / 重命名 / 替换；即使解码失败也不会锁死文件

    private static BitmapImage LoadBitmapWithoutLock(
        string path)
    {

        byte[] bytes;

        try
        {

            bytes =
                File.ReadAllBytes(
                    path);

        }
        catch(Exception ex)
        {

            // 读取阶段失败（没权限 / 文件被占用 / 中途被删等）
            // 统一转成明确的用户提示，原始异常保留为 InnerException

            throw new InvalidOperationException(
                "图片解码失败，请确保图片未损坏。\n" +
                "读取文件时出错：" +
                ex.Message,
                ex);

        }


        if(bytes.Length == 0)
        {

            throw new InvalidOperationException(
                "图片解码失败，请确保图片未损坏。\n" +
                "文件内容为空（0 字节）。");

        }


        try
        {

            using MemoryStream memoryStream =
                new MemoryStream(
                    bytes,
                    writable: false);


            BitmapImage image =
                new BitmapImage();

            image.BeginInit();

            image.CacheOption =
                BitmapCacheOption.OnLoad;

            // 注意：这里绝对不能设置 BitmapCreateOptions.IgnoreImageCache。
            // 该选项会让 BitmapImage 在 EndInit 内部调用
            // ImagingCache.RemoveFromImageCache(UriSource)；
            // 而用 StreamSource 加载时 UriSource 为 null，
            // Hashtable 收到 null key 就会抛
            // “Value cannot be null. (Parameter 'key')”。
            // 内存流本来就是全新解码，不需要再走 Uri 缓存。

            image.StreamSource =
                memoryStream;

            image.EndInit();

            // OnLoad + Freeze：像素已经在内存里，
            // MemoryStream 离开 using 后图片仍然可用
            image.Freeze();


            return image;

        }
        catch(Exception ex)
        {

            // 不返回 null：把真实原因抛给 ApplyLaunchBoardWallpaper，
            // 由它统一提示用户，避免再次出现“没图也没报错”

            throw new InvalidOperationException(
                "图片解码失败，请确保图片未损坏。\n" +
                "BitmapImage 解码时出错：" +
                ex.Message,
                ex);

        }

    }


    // 所有壁纸相关错误统一从这里弹出，绝不只写 Debug

    private static void ShowLaunchBoardWallpaperError(
        string title,
        string detail,
        Exception ex)
    {

        string message =
            detail +
            "\n\n错误类型：" +
            ex.GetType().Name +
            "\n详细原因：" +
            ex.Message;


        Debug.WriteLine(
            "展板壁纸：" +
            title +
            " - " +
            message);


        MessageBox.Show(
            message,
            "展板壁纸 - " +
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    }


    // 从 JSON 里读一个裁剪数字：
    // 键缺失 / 类型不对 / NaN / Infinity 全部回退默认值
    private static double ReadLaunchBoardWallpaperCropNumber(
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
                "展板壁纸：读取裁剪参数失败: " +
                ex.Message);

            return fallback;

        }

    }


    // 启动时自动恢复：独立 JSON，损坏 / 没权限一律回默认
    // 必须等 InitializeComponent 之后调用（控件字段已就绪）

    private void LoadLaunchBoardWallpaperFromStore()
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


        try
        {

            if(File.Exists(LaunchBoardWallpaperStorePath))
            {

                string json =
                    File.ReadAllText(
                        LaunchBoardWallpaperStorePath);

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
                            ReadLaunchBoardWallpaperCropNumber(
                                root,
                                "scale",
                                scale);

                        offsetX =
                            ReadLaunchBoardWallpaperCropNumber(
                                root,
                                "offsetX",
                                offsetX);

                        offsetY =
                            ReadLaunchBoardWallpaperCropNumber(
                                root,
                                "offsetY",
                                offsetY);

                        referenceWidth =
                            ReadLaunchBoardWallpaperCropNumber(
                                root,
                                "referenceWidth",
                                referenceWidth);

                        referenceHeight =
                            ReadLaunchBoardWallpaperCropNumber(
                                root,
                                "referenceHeight",
                                referenceHeight);

                    }

                }

            }

        }
        catch(Exception ex)
        {

            ShowLaunchBoardWallpaperError(
                "配置读取失败",
                "读取展板壁纸配置失败，将使用默认外观。\n" +
                "配置文件：" +
                LaunchBoardWallpaperStorePath,
                ex);

        }


        launchBoardWallpaperOpacityValue =
            ClampDockSetting(
                opacity,
                0.0,
                1.0,
                0.8);


        // 裁剪参数：缩放与参考尺寸夹回安全区间
        // 位移的实际边界依赖缩放值，交给
        // SettingsCenter_ApplyLaunchBoardWallpaperCrop 统一处理

        launchBoardWallpaperScaleValue =
            ClampDockSetting(
                scale,
                LaunchBoardWallpaperCropScaleMin,
                LaunchBoardWallpaperCropScaleMax,
                1.0);

        launchBoardWallpaperOffsetXValue =
            offsetX;

        launchBoardWallpaperOffsetYValue =
            offsetY;

        launchBoardWallpaperCropReferenceWidth =
            ClampDockSetting(
                referenceWidth,
                1.0,
                10000.0,
                LaunchBoardWallpaperReferenceWidth);

        launchBoardWallpaperCropReferenceHeight =
            ClampDockSetting(
                referenceHeight,
                1.0,
                10000.0,
                LaunchBoardWallpaperReferenceHeight);


        // 旧版本参考框是 300x200（3:2），与展板 900x650 不同比例，
        // 拖到边上仍会差一截。这里统一换算到与展板同比例的新参考框；
        // 位移按旧参考尺寸等比缩放，用户原来的取景中心基本不变。

        launchBoardWallpaperOffsetXValue *=
            LaunchBoardWallpaperReferenceWidth /
            launchBoardWallpaperCropReferenceWidth;

        launchBoardWallpaperOffsetYValue *=
            LaunchBoardWallpaperReferenceHeight /
            launchBoardWallpaperCropReferenceHeight;


        launchBoardWallpaperCropReferenceWidth =
            LaunchBoardWallpaperReferenceWidth;

        launchBoardWallpaperCropReferenceHeight =
            LaunchBoardWallpaperReferenceHeight;


        launchBoardWallpaperPath =
            path.Trim();


        // 文件不存在 / 解码失败时：
        // ApplyLaunchBoardWallpaper 会清空壁纸层、弹窗说明原因，
        // 展板仍保持原来的毛玻璃 / Acrylic 外观

        ApplyLaunchBoardWallpaper(
            launchBoardWallpaperPath);

    }


    // ----------------------------------------------------------
    // 端口：位置锁定
    // ----------------------------------------------------------

    public bool SettingsCenter_IsLocked
    {

        get
        {

            return isLocked;

        }

    }


    public void SettingsCenter_SetLocked(
        bool locked)
    {

        SetDockLocked(
            locked);

    }


    // ----------------------------------------------------------
    // 端口：开机自启动（注册表就是它的记忆）
    // ----------------------------------------------------------

    public bool SettingsCenter_IsStartupEnabled
    {

        get
        {

            return IsStartupEnabledInRegistry();

        }

    }


    public void SettingsCenter_SetStartupEnabled(
        bool enabled)
    {

        SetStartupEnabled(
            enabled);

    }


    // ----------------------------------------------------------
    // 端口：桌面图标处理模式（IconMode.txt 就是它的记忆）
    // ----------------------------------------------------------

    public bool SettingsCenter_IsIconModeStore
    {

        get
        {

            return dockIconMode ==
                DockIconMode.MoveToStore;

        }

    }


    public void SettingsCenter_SetIconModeKeepOnDesktop()
    {

        SetDockIconMode(
            DockIconMode.KeepOnDesktop);

    }


    public void SettingsCenter_SetIconModeMoveToStore()
    {

        SetDockIconMode(
            DockIconMode.MoveToStore);

    }


    // ----------------------------------------------------------
    // 端口：壁纸取色
    // ----------------------------------------------------------

    public bool SettingsCenter_IsWallpaperTinted
    {

        get
        {

            return isDockTinted;

        }

    }


    public void SettingsCenter_ToggleWallpaperTint()
    {

        // 直接把原来的“壁纸取色”按钮处理逻辑跑一遍
        // 取色 / 取消取色 / 取色失败的表现与在 Dock 面板上点完全一样

        try
        {

            WallpaperTintButton_Click(
                this,
                new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    0,
                    MouseButton.Left));

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "设置中心：壁纸取色失败: "
                +
                ex.Message);

        }

    }


    // ==========================================================
    // 【同一区的下一段】重启恢复「锁定位置 / 壁纸取色」
    // ==========================================================
    // Dock 面板那两个滑块（opacity / blur）由 LoadDockSettings 恢复
    // 剩下的两个开关原来不落盘，重启就丢，这次一并存进同一份
    // %AppData%\GlassDock\Settings.json：
    //       locked        1 = 锁定位置
    //       wallpaperTint 1 = 壁纸取色
    // 设置的写入方是托盘「AeroDock设置中心」（SettingsWindow）
    // 这里只负责启动时把值读回来，并走原来就有的那两条逻辑：
    //       SetDockLocked          绝对值直接设，顺手清干净拖动标记
    //       WallpaperTintButton_Click 取色按钮的原处理，取色/失败/刷材质全复用
    // 全程 try-catch：文件缺失 / 坏 JSON / 取色失败
    // 一律只是回默认（不锁定、不取色），不弹窗、不挡启动
    // ==========================================================


    private void LoadExtraDockSettingsFromStore()
    {

        bool locked = false;

        bool tinted = false;


        try
        {

            if(File.Exists(DockSettingsStorePath))
            {

                string json =
                    File.ReadAllText(
                        DockSettingsStorePath);


                if(!string.IsNullOrWhiteSpace(json))
                {

                    Dictionary<string,double>? data =
                        System.Text.Json.JsonSerializer
                            .Deserialize<Dictionary<string,double>>(
                                json);


                    if(data != null)
                    {

                        double value;


                        if(data.TryGetValue(
                            "locked",
                            out value))
                        {

                            locked =
                                value >= 0.5;

                        }


                        if(data.TryGetValue(
                            "wallpaperTint",
                            out value))
                        {

                            tinted =
                                value >= 0.5;

                        }

                    }

                }

            }

        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "读取额外 Dock 设置失败: "
                +
                ex.Message);

        }


        // 1) 锁定位置：绝对值直接设
        //    与存档不同才动，避免无谓地清一遍拖动标记

        if(locked != isLocked)
        {

            try
            {

                SetDockLocked(
                    locked);

            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "恢复锁定位置失败: "
                    +
                    ex.Message);

            }

        }


        // 2) 壁纸取色：它是个开关，只有“存档与当前不一致”才点一下
        //    取色成功后 isDockTinted 会自己变 true，文字与材质同时刷新
        //    取色失败就保持原样（下一次启动还会再试）

        if(tinted != isDockTinted)
        {

            try
            {

                WallpaperTintButton_Click(
                    this,
                    new MouseButtonEventArgs(
                        Mouse.PrimaryDevice,
                        0,
                        MouseButton.Left));

            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "恢复壁纸取色失败: "
                    +
                    ex.Message);

            }

        }

    }


    // ==========================
    // 展板：进入 / 退出批量管理模式
    // ==========================

    private void SetBoardBatchMode(
        bool active)
    {

        if(isBoardBatchMode == active)
            return;


        isBoardBatchMode = active;

        BoardBatchMode = active;


        // 进出批量模式都清掉展板拖动状态
        // 免得上一次按下留下“已武装”的拖拽标记

        boardDragSource = null;

        boardDragArmed = false;

        boardDragStarted = false;


        // 每次状态切换都从“全不选”开始
        // 避免上次留下的选中项被误删

        ClearBoardSelection();


        if(BoardManageButton != null)
        {

            BoardManageButton.Content =
                active ? "完成" : "管理";

        }


        if(BoardBatchBar != null)
        {

            BoardBatchBar.Visibility =
                active ? Visibility.Visible : Visibility.Collapsed;

            BoardBatchBar.Opacity =
                active ? 1 : 0;

        }


        // 批量操作栏在底部，状态文字往上让一点
        // 退出批量模式时恢复原来的位置

        if(BoardStatus != null)
        {

            BoardStatus.Margin =
                active
                ? new Thickness(0, 0, 0, 64)
                : new Thickness(0, 0, 0, 18);

        }


        if(active)
        {

            // 收起上一秒还悬停着的应用名称气泡
            DismissIconTip();


            // 先让容器全部生成，选择标记才能立刻定位
            BoardPanel?.UpdateLayout();

            ShowBoardStatus(
                "批量模式：点击图标选择，完成后点右上角“完成”");

        }

    }


    // ==========================
    // 展板：全选 / 取消全选 / 切换选中
    // ==========================

    private void BoardManageButton_Click(
        object sender,
        RoutedEventArgs e)
    {

        e.Handled = true;

        SetBoardBatchMode(!isBoardBatchMode);

    }


    private void BoardSelectAllButton_Click(
        object sender,
        RoutedEventArgs e)
    {

        e.Handled = true;

        if(boardApps.Count == 0)
        {

            ShowBoardStatus("展板里还没有图标");

            return;

        }


        BoardPanel?.UpdateLayout();


        foreach(AppItem app in boardApps)
        {

            boardSelectedApps.Add(app);

            SetBoardSelectionVisual(app, true);

        }


        ShowBoardStatus(
            "已全选 "
            +
            boardApps.Count
            +
            " 个图标");

    }


    private void BoardSelectNoneButton_Click(
        object sender,
        RoutedEventArgs e)
    {

        e.Handled = true;

        ClearBoardSelection();

        ShowBoardStatus("已取消全选");

    }


    private void ToggleBoardSelection(
        AppItem app)
    {

        if(app == null)
            return;


        bool selected =
            !boardSelectedApps.Contains(app);


        if(selected)
        {

            boardSelectedApps.Add(app);

        }
        else
        {

            boardSelectedApps.Remove(app);

        }


        SetBoardSelectionVisual(
            app,
            selected);


        ShowBoardStatus(
            selected
            ? "已选择 "
              +
              app.Name
            : "已取消选择 "
              +
              app.Name);

    }


    private void ClearBoardSelection()
    {

        boardSelectedApps.Clear();


        foreach(AppItem app in boardApps)
        {

            SetBoardSelectionVisual(app, false);

        }

    }


    // 把“选中”写进模板根 Border 的附加属性
    // 模板里的遮罩 / 右上角对勾会自动跟着显示

    private void SetBoardSelectionVisual(
        AppItem app,
        bool selected)
    {

        if(app == null)
            return;


        Border? border =
            FindBoardIconBorder(app);


        if(border != null)
        {

            SetIsSelected(border, selected);

        }

    }


    private Border? FindBoardIconBorder(
        AppItem app)
    {

        if(BoardPanel == null)
            return null;


        DependencyObject? container =
            BoardPanel.ItemContainerGenerator
                     .ContainerFromItem(app)
            as DependencyObject;


        if(container == null)
            return null;


        return FindBoardIconBorderInVisualTree(container);

    }


    private static Border? FindBoardIconBorderInVisualTree(
        DependencyObject root)
    {

        if(root is Border border)
            return border;


        int count =
            VisualTreeHelper.GetChildrenCount(root);


        for(int i = 0;
            i < count;
            i++)
        {

            Border? found =
                FindBoardIconBorderInVisualTree(
                    VisualTreeHelper.GetChild(root, i));


            if(found != null)
                return found;

        }


        return null;

    }



    // ==========================
    // 展板：删除选中的图标
    // 核心安全规则：
    //   1. 只从 boardApps 里移除，不 File.Delete
    //   2. 收纳模式下先把源文件移回桌面
    //      TryMoveBackToDesktop 失败就保留图标，绝不丢文件
    //   3. 全部处理完再保存 LaunchBoard.txt
    // ==========================

    private void BoardDeleteSelectedButton_Click(
        object sender,
        RoutedEventArgs e)
    {

        e.Handled = true;


        int selectedCount =
            boardSelectedApps.Count;


        if(selectedCount <= 0)
        {

            ShowBoardStatus("请先选择要移除的图标");

            return;

        }


        // 弹确认框期间打标记
        // 防止弹框抢焦点触发 Window_Deactivated，把展板误关

        MessageBoxResult result;

        isRightClickMenuOpen = true;

        try
        {

            result =
                MessageBox.Show(
                    "确定移除选中的 "
                    +
                    selectedCount
                    +
                    " 个图标吗？\n\n不会删除电脑中的文件。",
                    "AeroDock",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

        }
        finally
        {

            isRightClickMenuOpen = false;

        }


        if(result != MessageBoxResult.Yes)
            return;


        // 先拍快照：删除过程中 boardApps 会变
        // 直接遍历 boardSelectedApps 会抛集合已修改异常

        List<AppItem> selected =
            new List<AppItem>(boardSelectedApps);


        int removed = 0;

        int failed = 0;


        // 收纳模式下 TryMoveBackToDesktop 可能再弹提示框
        // 整个循环都打标记，展板不会被这些弹框误关

        isRightClickMenuOpen = true;

        try
        {

            foreach(AppItem app in selected)
            {

                int index =
                    boardApps.IndexOf(app);


                if(index < 0)
                    continue;


                // 模式A：什么都不做，直接返回 true
                // 模式B：把收纳目录里的文件移回桌面
                // 失败 → 保留这个图标和它的选中状态，绝不丢文件

                if(!TryMoveBackToDesktop(app))
                {

                    failed++;

                    continue;

                }


                // 只从数据集合里移除，绝不做 File.Delete

                boardApps.RemoveAt(index);


                if(index < boardSourcePaths.Count)
                {

                    boardSourcePaths.RemoveAt(index);

                }


                boardSelectedApps.Remove(app);

                removed++;

            }

        }
        finally
        {

            isRightClickMenuOpen = false;

        }


        // 保存 JSON / 文本配置并刷新 UI
        // boardApps 的 CollectionChanged 已经负责更新空态与分页

        if(removed > 0)
        {

            SaveBoardApps();

        }


        if(removed > 0)
        {

            ShowBoardStatus(
                failed > 0
                ? "已移除 "
                  +
                  removed
                  +
                  " 个，失败 "
                  +
                  failed
                  +
                  " 个（图标已保留）"
                : "已移除 "
                  +
                  removed
                  +
                  " 个图标");

        }
        else if(failed > 0)
        {

            ShowBoardStatus("移除失败，图标已保留");

        }


        // 全部成功 → 自动回到普通模式
        // 还有失败项 → 留在批量模式，方便用户直接重试

        if(failed == 0)
        {

            SetBoardBatchMode(false);

        }

    }



}



// ==========================
// 全文件类型支持：统一图标提取
// ==========================
// 不判断任何后缀
// 直接问 Windows："这个路径关联哪个图标？"
// SHGetFileInfo 拿图标索引 → SHGetImageList(SHIL_JUMBO) → 256x256 高清 HICON
// 文件夹自然拿到文件夹图标，txt / pdf / zip / jpg / url 各拿各的关联图标
// ==========================

internal static class ShellIconProvider
{


    // 图像列表尺寸
    // JUMBO = 256x256（首选）
    // 系统给不出来再退到 EXTRA LARGE / LARGE

    private const int SHIL_LARGE = 0;

    private const int SHIL_EXTRALARGE = 2;

    private const int SHIL_JUMBO = 4;


    // 只问"图标索引"，图标本体从图像列表里取

    private const uint SHGFI_SYSICONINDEX = 0x000004000;


    // 取图标时保留透明通道

    private const int ILD_TRANSPARENT = 0x1;


    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;


    // 借一个不存在的 .exe 名字去问系统要"普通程序"图标
    // 带 SHGFI_USEFILEATTRIBUTES，不会真的去访问磁盘

    private const string DefaultAppProbe =
        "GlassDock.default.app.exe";


    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;


    // 体检门槛：小于 32 的一律不要
    // 避免把 16x16 的小图标拉大成马赛克

    private const int MinIconSize =
        IconExtractor.MinSize;


    // 优先门槛：能到 48 以上最好（外置 .ico 常常是 256）

    private const int GoodIconSize =
        48;


    // ==========================
    // 画布归一化参数
    // ==========================
    // 系统图像列表的 256 槽位对"没带 256 图标的程序"是这么给的：
    // 256x256 的画布，内容（48x48 甚至 16x16）贴在左上角，其余全透明
    // 直接交给 50x50 的 Image 渲染出来就是左上角一个小点
    // 所以要裁到内容本身；内容占满画布的正常软件完全不受影响
    // ==========================

    private const int ContentProbeLimit =
        64;


    // 算内容范围时只认真正不透明的像素
    // 16 起步可以避开抗锯齿边缘，又不至于漏掉深色图标

    private const int ContentAlpha =
        16;


    // 内容两条边都还不到画布的 60% 才算"大画布小内容"

    private const int ContentFillPercent =
        60;


    // IImageList 的 IID

    private static readonly Guid IID_IImageList =
        new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");


    // 同一个路径只取一次
    // 展板翻页 / Dock 重排都不该反复问系统

    private static readonly Dictionary<string, ImageSource?> iconCache =
        new Dictionary<string, ImageSource?>(
            StringComparer.OrdinalIgnoreCase);


    // IconPath 兜底文件也只读一次
    // 不然每次刷新都要重新解码一遍

    private static readonly Dictionary<string, ImageSource?> fileCache =
        new Dictionary<string, ImageSource?>(
            StringComparer.OrdinalIgnoreCase);


    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {

        public IntPtr hIcon;

        public int iIcon;

        public uint dwAttributes;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 80)]
        public string szTypeName;

    }


    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGELISTDRAWPARAMS
    {

        public int cbSize;

        public IntPtr himl;

        public int i;

        public IntPtr hdcDst;

        public int x;

        public int y;

        public int cx;

        public int cy;

        public int xBitmap;

        public int yBitmap;

        public int rgbBk;

        public int rgbFg;

        public int fStyle;

        public int dwRop;

        public int fState;

        public int Frame;

        public int crEffect;

    }


    // 只用到 GetIcon，但 vtable 顺序必须完整
    // 少一个方法，GetIcon 就会跳到错误的地址

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(
        ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {

        [PreserveSig]
        int Add(
            IntPtr hbmImage,
            IntPtr hbmMask,
            ref int pi);

        [PreserveSig]
        int ReplaceIcon(
            int i,
            IntPtr hicon,
            ref int pi);

        [PreserveSig]
        int SetOverlayImage(
            int iImage,
            int iOverlay);

        [PreserveSig]
        int Replace(
            int i,
            IntPtr hbmImage,
            IntPtr hbmMask);

        [PreserveSig]
        int AddMasked(
            IntPtr hbmImage,
            int crMask,
            ref int pi);

        [PreserveSig]
        int Draw(
            ref IMAGELISTDRAWPARAMS pimldp);

        [PreserveSig]
        int Remove(
            int i);

        [PreserveSig]
        int GetIcon(
            int i,
            int flags,
            out IntPtr picon);

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


    // shell32 的序号导出：拿系统图像列表

    [DllImport(
        "shell32.dll",
        EntryPoint = "#727")]
    private static extern int SHGetImageList(
        int iImageList,
        ref Guid riid,
        out IImageList ppv);


    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(
        IntPtr hIcon);


    // 对外唯一入口
    // 全类型通吃：exe / lnk / txt / pdf / zip / jpg / url / 文件夹
    // 系统给不出来就返回 null，交给调用方兜底

    internal static ImageSource? Get(
        string? path)
    {


        if(string.IsNullOrWhiteSpace(path))
            return null;


        string key =
            path.Trim();


        lock(iconCache)
        {

            if(iconCache.TryGetValue(
                key,
                out ImageSource? cached))
            {

                return cached;

            }

        }


        ImageSource? icon =
            BuildVerified(key);


        lock(iconCache)
        {

            iconCache[key] =
                icon;

        }


        return icon;


    }




    // ==========================
    // 带体检的入口
    // ==========================
    // 1) 老逻辑原样保留（系统图像列表：256 → 48 → 32）
    //    正常软件（微信 / VS Code / txt / pdf / 文件夹）走这里，行为不变
    // 2) 老逻辑给不出合格图标（空白 / 全黑 / 全白 / 小于 32）才走兜底链
    //    .lnk 真实目标 → ExtractIconEx → 外置 .ico → 系统关联图标
    // 3) 都不行返回 null，由 ShellIconConverter 用系统默认程序图标收尾
    // 体检标准：尺寸 >= 32 且不是空白 / 全黑 / 全白
    // 全程不做任何拉伸，避免把小图标放大成马赛克
    // ==========================

    private static ImageSource? BuildVerified(
        string path)
    {


        // 老逻辑先跑，再统一过一道画布归一化：
        // 256 画布里只有一个 48 的图案时先裁到图案，
        // 否则 50x50 的 Image 会把它渲染成左上角的一个小点

        ImageSource? shell =
            NormalizeIcon(
                Build(path));


        bool shellOk =
            IsUsable(
                shell,
                MinIconSize);


        // 高清图（256 / 48）直接放行
        // 老逻辑对绝大多数软件都会走到这里，行为完全不变

        if(shellOk &&
           PixelSize(shell) >= GoodIconSize)
        {

            return shell;

        }


        ImageSource? fallback =
            NormalizeIcon(
                BuildFromFallback(path));


        bool fallbackOk =
            IsUsable(
                fallback,
                MinIconSize);


        if(shellOk &&
           fallbackOk)
        {

            // 两个都能用：老的优先（保持原样）
            // 老的是 32 这种小图、兜底能给出更大更清楚的时候才换
            // 这样"16x16 硬拉伸出来的模糊图标"也能被真·256 替换掉

            if(PixelSize(fallback) > PixelSize(shell))
            {

                return fallback;

            }


            return shell;

        }


        if(shellOk)
            return shell;


        if(fallbackOk)
            return fallback;


        return null;


    }


    // ==========================
    // 兜底链
    // ==========================
    // 1) exe 旁边的外置 .ico（打包工具很喜欢把 256x256 的图标放外面）
    // 2) .lnk 解析出的真实目标 → IconExtractor 的多路提取
    // ==========================

    private static ImageSource? BuildFromFallback(
        string path)
    {

        try
        {


            string iconFile =
                IconExtractor.FindExternalIcon(path);


            if(iconFile.Length > 0)
            {

                ImageSource? fromFile =
                    LoadIconFile(
                        iconFile,
                        GoodIconSize);


                if(fromFile != null)
                {

                    return fromFile;

                }

            }


            using(GdiIcon? icon =
                IconExtractor.GetBestIcon(path))
            {

                return FromIcon(icon);

            }


        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "图标兜底失败: "
                +
                ex.Message);


            return null;

        }

    }


    // ==========================
    // 解码 .ico 文件（外置图标用）
    // WPF 的 ICO 解码器能吃 PNG 压缩的 256x256 帧
    // 只取最大的那一帧，小于 32 的直接判不合格
    // ==========================

    private static ImageSource? LoadIconFile(
        string iconPath,
        int minSize)
    {

        try
        {


            if(!File.Exists(iconPath))
                return null;


            BitmapDecoder decoder;


            using(FileStream stream =
                new FileStream(
                    iconPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite))
            {

                decoder =
                    BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

            }


            BitmapFrame? best =
                null;


            int bestSize =
                0;


            foreach(BitmapFrame frame in decoder.Frames)
            {

                int size =
                    Math.Min(
                        frame.PixelWidth,
                        frame.PixelHeight);


                if(size <= bestSize)
                    continue;


                bestSize =
                    size;


                best =
                    frame;

            }


            if(best == null ||
               bestSize < minSize)
                return null;


            if(best.CanFreeze)
                best.Freeze();


            return best;


        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "外置图标读取失败: "
                +
                ex.Message);


            return null;

        }

    }


    // ==========================
    // GDI 图标 → WPF 图像
    // CreateBitmapSourceFromHIcon 会把像素拷一份出来
    // 所以调用方随后 Dispose 图标是安全的
    // ==========================

    private static ImageSource? FromIcon(
        GdiIcon? icon)
    {

        if(icon == null)
            return null;


        try
        {


            BitmapSource source =
                Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());


            if(source.CanFreeze)
                source.Freeze();


            return source;


        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "图标转换失败: "
                +
                ex.Message);


            return null;

        }

    }


    // ==========================
    // 图像列表取图（按索引）
    // 和 Build 里那段是同一套 API，只是把索引交出来给别人用
    // 256 → 48 → 32 依次试，第一个达到门槛的直接返回
    // ==========================

    private static ImageSource? IconFromImageList(
        int index,
        int minSize)
    {

        if(index < 0)
            return null;


        int[] sizes =
        {

            SHIL_JUMBO, SHIL_EXTRALARGE, SHIL_LARGE

        };


        foreach(int size in sizes)
        {

            IntPtr hIcon =
                IntPtr.Zero;


            try
            {


                // 先按尺寸门槛筛掉明显不合格的
                // 免得白跑一趟 COM 调用

                int nominal =
                    size == SHIL_JUMBO
                        ? 256
                        : size == SHIL_EXTRALARGE
                            ? 48
                            : 32;


                if(nominal < minSize)
                    continue;


                Guid iid =
                    IID_IImageList;


                IImageList? list;


                if(SHGetImageList(
                    size,
                    ref iid,
                    out list) != 0 ||
                   list == null)
                    continue;


                if(list.GetIcon(
                    index,
                    ILD_TRANSPARENT,
                    out hIcon) != 0 ||
                   hIcon == IntPtr.Zero)
                {

                    hIcon =
                        IntPtr.Zero;


                    continue;

                }


                ImageSource? source =
                    FromHIcon(hIcon);


                DestroyIcon(hIcon);


                hIcon =
                    IntPtr.Zero;


                if(source == null)
                    continue;


                return source;


            }
            catch(Exception ex)
            {

                Debug.WriteLine(
                    "图像列表取图失败: "
                    +
                    ex.Message);


            }
            finally
            {

                if(hIcon != IntPtr.Zero)
                {

                    DestroyIcon(hIcon);

                }

            }

        }


        return null;

    }


    // ==========================
    // 系统图标句柄 → WPF 图像
    // 像素拷完立刻释放句柄，避免 GDI 泄漏
    // ==========================

    private static ImageSource? FromHIcon(
        IntPtr hIcon)
    {

        if(hIcon == IntPtr.Zero)
            return null;


        try
        {


            BitmapSource source =
                Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());


            if(source.CanFreeze)
                source.Freeze();


            return source;


        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "系统图标句柄转换失败: "
                +
                ex.Message);


            return null;

        }

    }


    // ==========================
    // IconPath 兜底文件（png / ico）
    // 同一个文件只解码一次，之后直接拿缓存
    // 体检不过（空白 / 小于 32）返回 null，让调用方继续往下兜底
    // ==========================

    internal static ImageSource? GetFromFile(
        string? iconPath)
    {

        if(string.IsNullOrWhiteSpace(iconPath))
            return null;


        string key =
            iconPath.Trim();


        lock(fileCache)
        {

            if(fileCache.TryGetValue(
                key,
                out ImageSource? cached))
            {

                return cached;

            }

        }


        ImageSource? source =
            LoadBitmapFile(key);


        if(!IsUsable(source))
            source =
                null;


        lock(fileCache)
        {

            fileCache[key] =
                source;

        }


        return source;

    }


    // 走流式读取：不占文件句柄，也不进 WPF 的图像缓存
    // 所以换图标之后不会被旧图粘住

    private static ImageSource? LoadBitmapFile(
        string iconPath)
    {

        try
        {


            string fullPath =
                Path.IsPathRooted(iconPath)
                    ? iconPath
                    : Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        iconPath);


            if(!File.Exists(fullPath))
                return null;


            BitmapImage bitmap =
                new BitmapImage();


            using(FileStream stream =
                new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite))
            {

                bitmap.BeginInit();


                bitmap.CacheOption =
                    BitmapCacheOption.OnLoad;


                bitmap.CreateOptions =
                    BitmapCreateOptions.PreservePixelFormat;


                bitmap.StreamSource =
                    stream;


                bitmap.EndInit();

            }


            bitmap.Freeze();


            return bitmap;


        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "兜底图标文件加载失败: "
                +
                ex.Message);


            return null;

        }

    }


    // ==========================
    // 画布归一化
    // ==========================
    // 系统图像列表的 256x256 槽位有个坑：
    // 程序自己不带 256 图标时（很多国产小工具都这样），
    // 系统会把 48x48（甚至 16x16）的内容贴在 256 画布的左上角，
    // 其余位置全是透明 —— 直接丢给 50x50 的 Image（Stretch=Uniform），
    // 渲染出来就是左上角一个 9px 的小点，看着就是"空白 / 图标很小"
    // 这里把这种"大画布小内容"裁到内容本身：
    // 内容本来就占满画布的正常软件原样返回，渲染结果一个像素都不变
    // ==========================

    private static ImageSource? NormalizeIcon(
        ImageSource? source)
    {


        BitmapSource? bitmap =
            source as BitmapSource;


        if(bitmap == null)
            return source;


        int width =
            bitmap.PixelWidth;


        int height =
            bitmap.PixelHeight;


        // 小画布不用管：本来就是内容自己的尺寸

        if(width < ContentProbeLimit ||
           height < ContentProbeLimit)
            return source;


        try
        {


            BitmapSource read =
                bitmap.Format == PixelFormats.Bgra32
                ||
                bitmap.Format == PixelFormats.Pbgra32
                    ? bitmap
                    : new FormatConvertedBitmap(
                        bitmap,
                        PixelFormats.Bgra32,
                        null,
                        0);


            int stride =
                width * 4;


            byte[] pixels =
                new byte[stride * height];


            read.CopyPixels(
                pixels,
                stride,
                0);


            int minX =
                width;


            int minY =
                height;


            int maxX =
                -1;


            int maxY =
                -1;


            for(int y = 0;
                y < height;
                y++)
            {


                int row =
                    y * stride;


                for(int x = 0;
                    x < width;
                    x++)
                {


                    if(pixels[row + x * 4 + 3] < ContentAlpha)
                        continue;


                    if(x < minX)
                        minX =
                            x;


                    if(y < minY)
                        minY =
                            y;


                    if(x > maxX)
                        maxX =
                            x;


                    if(y > maxY)
                        maxY =
                            y;


                }


            }


            // 全透明：不动，交给体检去否决

            if(maxX < minX ||
               maxY < minY)
                return source;


            int contentWidth =
                maxX - minX + 1;


            int contentHeight =
                maxY - minY + 1;


            // 内容已经占满画布：正常软件（微信 / VS Code / txt / 文件夹）走这里

            if(contentWidth * 100 >
                   width * ContentFillPercent
               &&
               contentHeight * 100 >
                   height * ContentFillPercent)
                return source;


            CroppedBitmap cropped =
                new CroppedBitmap(
                    bitmap,
                    new Int32Rect(
                        minX,
                        minY,
                        contentWidth,
                        contentHeight));


            cropped.Freeze();


            return cropped;


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "图标画布归一化失败: "
                +
                ex.Message);


            return source;


        }


    }


    // 图标实际像素边长（短边）
    // 测不出来返回 0

    private static int PixelSize(
        ImageSource? source)
    {

        BitmapSource? bitmap =
            source as BitmapSource;


        if(bitmap == null)
            return 0;


        int width =
            bitmap.PixelWidth;


        int height =
            bitmap.PixelHeight;


        if(width <= 0 ||
           height <= 0)
            return 0;


        return Math.Min(
            width,
            height);

    }


    // ==========================
    // 体检
    // ==========================
    // 尺寸 >= 32（不做拉伸，所以宁可换一个图标）
    // 且不是"全透明 / 不透明全黑 / 不透明全白"
    // 这两个条件正好能抓住无畏契约 / 图吧工具箱那种空白图标
    // ==========================

    internal static bool IsUsable(
        ImageSource? source)
    {

        return IsUsable(
            source,
            MinIconSize);

    }


    internal static bool IsUsable(
        ImageSource? source,
        int minSize)
    {

        if(source == null)
            return false;


        BitmapSource? bitmap =
            source as BitmapSource;


        // 测不出像素尺寸的来源（比如矢量图）不拦

        if(bitmap == null)
            return true;


        if(PixelSize(bitmap) < minSize)
            return false;


        return !IsBlankOrBlack(bitmap);

    }


    // 全透明 / 不透明全黑 / 不透明全白 → 都算坏图
    // 透明底上有图案的（绝大多数图标）不会被误杀

    private static bool IsBlankOrBlack(
        BitmapSource source)
    {

        try
        {


            // 统一成 Bgra32，省得按格式分支

            BitmapSource read =
                source.Format == PixelFormats.Bgra32
                ||
                source.Format == PixelFormats.Pbgra32
                    ? source
                    : new FormatConvertedBitmap(
                        source,
                        PixelFormats.Bgra32,
                        null,
                        0);


            int width =
                read.PixelWidth;


            int height =
                read.PixelHeight;


            if(width <= 0 ||
               height <= 0)
                return true;


            int stride =
                width * 4;


            byte[] pixels =
                new byte[stride * height];


            read.CopyPixels(
                pixels,
                stride,
                0);


            bool anyVisible =
                false;


            bool allOpaque =
                true;


            bool allBlack =
                true;


            bool allWhite =
                true;


            for(int i = 0; i < pixels.Length; i += 4)
            {

                // Bgra32 的内存顺序：B G R A

                byte b =
                    pixels[i];


                byte g =
                    pixels[i + 1];


                byte r =
                    pixels[i + 2];


                byte a =
                    pixels[i + 3];


                if(a != 0)
                    anyVisible = true;


                if(a != 255)
                    allOpaque = false;


                if(r > 8 || g > 8 || b > 8)
                    allBlack = false;


                if(r < 247 || g < 247 || b < 247)
                    allWhite = false;

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
        catch
        {

            // 判不出来就当它是好的，别误杀正常图标

            return false;

        }

    }


    // ==========================
    // 系统默认"普通程序"图标（最后一层兜底）
    // 借一个假的 .exe 名字问系统：exe 文件类型的默认大图标
    // 带 SHGFI_USEFILEATTRIBUTES，不会真的去访问磁盘
    // 先试系统图像列表（256 → 48 → 32），再退 GDI 的 32x32
    // 宁可给一个通用图标，也不留白框
    // ==========================

    internal static ImageSource? GetDefaultAppIcon()
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
                    SHGFI_SYSICONINDEX |
                    SHGFI_USEFILEATTRIBUTES);


            if(result != IntPtr.Zero)
            {

                ImageSource? fromList =
                    IconFromImageList(
                        info.iIcon,
                        MinIconSize);


                if(fromList != null)
                {

                    return fromList;

                }

            }


            using(GdiIcon? icon =
                IconExtractor.GetDefaultAppIcon())
            {

                return FromIcon(icon);

            }


        }
        catch(Exception ex)
        {

            Debug.WriteLine(
                "默认程序图标提取失败: "
                +
                ex.Message);


            return null;

        }

    }


    private static ImageSource? Build(
        string path)
    {


        // 路径不存在就走兜底
        // 不去猜后缀，也不伪造图标

        if(!File.Exists(path) &&
           !Directory.Exists(path))
            return null;


        IntPtr hIcon =
            IntPtr.Zero;


        try
        {


            // 1) 问系统要这个路径的图标索引

            SHFILEINFO info =
                new SHFILEINFO();


            IntPtr result =
                SHGetFileInfo(

                    path,

                    FILE_ATTRIBUTE_NORMAL,

                    ref info,

                    (uint)Marshal.SizeOf(
                        typeof(SHFILEINFO)),

                    SHGFI_SYSICONINDEX);


            if(result == IntPtr.Zero)
                return null;


            // 2) 优先 256x256（SHIL_JUMBO），拿不到再降级

            int[] sizes =
            {

                SHIL_JUMBO,

                SHIL_EXTRALARGE,

                SHIL_LARGE

            };


            foreach(int size in sizes)
            {


                Guid iid =
                    IID_IImageList;


                IImageList? list;


                if(SHGetImageList(
                    size,
                    ref iid,
                    out list) != 0 ||
                   list == null)
                    continue;


                if(list.GetIcon(
                    info.iIcon,
                    ILD_TRANSPARENT,
                    out hIcon) != 0 ||
                   hIcon == IntPtr.Zero)
                {


                    hIcon =
                        IntPtr.Zero;


                    continue;

                }


                // 3) HICON → WPF 图像
                // 转完立刻把系统句柄还回去，不然会泄漏

                ImageSource? source =
                    Imaging.CreateBitmapSourceFromHIcon(

                        hIcon,

                        Int32Rect.Empty,

                        BitmapSizeOptions.FromEmptyOptions());


                DestroyIcon(
                    hIcon);


                hIcon =
                    IntPtr.Zero;


                if(source == null)
                    continue;


                source.Freeze();


                return source;


            }


        }
        catch(Exception ex)
        {


            Debug.WriteLine(
                "系统图标提取失败: "
                +
                ex.Message);


        }
        finally
        {


            if(hIcon != IntPtr.Zero)
            {

                DestroyIcon(
                    hIcon);

            }


        }


        return null;


    }


}





// ==========================
// Dock / 展板 共用的图标转换器
// ==========================
// 输入（MultiBinding 两个参数）：
//   1) LaunchPath —— 真实路径，首选
//   2) IconPath   —— 原来的图标，兜底
// 输出：系统给出的高清关联图标
// 不判断后缀：exe / lnk / txt / pdf / zip / jpg / url / 文件夹 一律交给系统
// 注意：MultiBinding.Converter 要的是 IMultiValueConverter
//       写成 IValueConverter 只对单值 Binding 有效
//       放进 MultiBinding 会在模板实例化时抛 InvalidCastException
// ==========================

public sealed class ShellIconConverter : IMultiValueConverter
{


    public object? Convert(
        object?[] values,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {


        // values 就是 MultiBinding 传进来的多路值
        // 已经是数组，不需要再 as object[]


        string? launchPath =
            null;


        object? fallback =
            null;


        if(values != null)
        {


            if(values.Length > 0)
                launchPath =
                    values[0] as string;


            if(values.Length > 1)
                fallback =
                    values[1];


        }


        // 1) 首选：让系统按路径给图标
        //    文件夹也会拿到文件夹图标

        ImageSource? icon =
            ShellIconProvider.Get(
                launchPath);


        if(icon != null)
            return icon;


        // 2) 系统给不出来：回退到原来的 IconPath（图片走图片，路径走路径）
        //    同样要体检：空白 / 小于 32 的不放行

        if(fallback is ImageSource image &&
           ShellIconProvider.IsUsable(image))
        {

            return image;

        }


        string? iconPath =
            fallback as string;


        ImageSource? fromFile =
            ShellIconProvider.GetFromFile(iconPath);


        if(fromFile != null)
        {

            return fromFile;

        }


        // 3) 兜底兜到底：给"普通程序"默认图标
        //    宁可给一个通用图标，也不要在 Dock 上留白框
        //    但真的什么路径都没有（比如占位项）就还是返回空

        if(string.IsNullOrWhiteSpace(launchPath) &&
           string.IsNullOrWhiteSpace(iconPath))
            return null;


        return ShellIconProvider.GetDefaultAppIcon();


    }




    public object?[] ConvertBack(
        object? value,
        Type[] targetTypes,
        object? parameter,
        CultureInfo culture)
    {

        return new object?[]
        {
            Binding.DoNothing,
            Binding.DoNothing
        };

    }


}

}
