# AeroDock

**AeroDock** 是一款面向 Windows 10/11 的桌面 Dock，使用 WPF (.NET 8) 构建，以液态玻璃质感的动画交互，把常用应用、文件夹和快捷方式集中到屏幕边缘。

[简体中文](#简体中文) | [English](#english)

---

## 简体中文

### 简介

AeroDock（工程名为 `GlassDock`，程序名为 `AeroDock.exe`）是一款带液态玻璃动画的 Windows Dock。

它支持应用收纳、图标动画、壁纸取色、独立设置与材质预设，让 Dock 与桌面壁纸自然融合。

### 功能

- **应用收纳**：把常用应用、文件夹与快捷方式集中到 Dock；支持“保留在桌面 / 收纳至 Dock”两种桌面图标处理模式，收纳后桌面更清爽。
- **流畅动画**：Dock 图标悬停放大、点击反馈，以及展板（Launch Board）的开合动画。
- **壁纸取色**：截取当前屏幕或分析壁纸提取主色，实时应用到 Dock 玻璃；即使壁纸文件失效，仍可使用屏幕取色。
- **独立设置**：Dock 与展板的透明度、模糊强度、背景壁纸等分别独立调节，修改即时生效并自动保存。
- **材质预设**：经典亚克力 (Acrylic)、极简纯色 (Minimal)、高光质感 (Glossy) 三种风格一键切换。
- **本地配置**：设置保存在 `%AppData%\GlassDock\`，重启不丢失，也不会上传云端。

### 运行便携版（ZIP）

1. 下载 `AeroDock-Windows-x64-v1.0.0.zip`。
2. 解压到任意本地文件夹（请勿直接在压缩包内双击运行）。
3. 双击 `AeroDock.exe` 启动。

说明：

- 系统要求：**Windows 10 / 11 x64**。
- 便携版为 `win-x64` 自包含发布，**无需安装 .NET 运行时**。
- 应用暂未进行代码签名，首次运行可能出现 Windows SmartScreen 提示，选择“更多信息 → 仍要运行”即可。
- 压缩包内的 `AeroDock.pdb` 为调试符号文件，可以删除，不影响程序运行。

### 从源码构建

环境要求：Windows 10/11 x64、[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
dotnet publish .\GlassDock.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

输出目录：`bin\Release\net8.0-windows\win-x64\publish\`。

将 `publish` 目录中的全部文件复制到独立文件夹后压缩，即可得到便携版 ZIP。

> 部分功能依赖 Windows 桌面能力（桌面图层、Shell 图标、AppX 应用枚举等），请在真实的 Windows 桌面会话中运行。

### 配置与数据

配置保存在当前用户的 `%AppData%\GlassDock\` 目录：

| 文件 / 目录 | 用途 |
| --- | --- |
| `dock.json` | Dock 应用列表 |
| `Settings.json` | Dock 与展板的设置 |
| `LaunchBoardWallpaper.json` | 展板壁纸配置 |
| `Shortcuts\` | 收纳的快捷方式 |

AeroDock 不会上传这些本地数据。

---

## English

### Overview

AeroDock (project name `GlassDock`, executable `AeroDock.exe`) is a Windows Dock with liquid-glass animations, built with WPF on .NET 8.

It brings app collection, animated interactions, wallpaper color extraction, independent settings and material presets together, so the Dock blends naturally with your desktop wallpaper.

### Features

- **App collection**: keep frequently used apps, folders and shortcuts in the Dock. Two desktop icon modes are available: keep icons on the desktop, or store them into the Dock for a cleaner desktop.
- **Smooth animations**: hover magnification and click feedback on Dock icons, plus open/close animations for the launch board.
- **Wallpaper color extraction**: sample the current screen or analyze the wallpaper to extract a dominant color and apply it to the Dock glass in real time. Screen sampling still works when the wallpaper file is unavailable.
- **Independent settings**: opacity, blur strength and background wallpaper can be tuned separately for the Dock and the launch board. Changes apply instantly and are saved automatically.
- **Material presets**: switch between Acrylic, Minimal and Glossy with one click.
- **Local configuration**: settings live in `%AppData%\GlassDock\`, survive restarts and are never uploaded.

### Run the portable build (ZIP)

1. Download `AeroDock-Windows-x64-v1.0.0.zip`.
2. Extract it to any local folder (do not run it directly from inside the archive).
3. Double-click `AeroDock.exe`.

Notes:

- **Windows 10 / 11 x64** is required.
- The portable build is published for `win-x64` as self-contained, so **no .NET runtime installation is needed**.
- The executable is not code-signed yet. Windows SmartScreen may warn on first launch; choose "More info → Run anyway".
- `AeroDock.pdb` in the ZIP is a debug symbol file. You can delete it without affecting the app.

### Build from source

Requirements: Windows 10/11 x64 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet publish .\GlassDock.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `bin\Release\net8.0-windows\win-x64\publish\`.

Copy everything from the `publish` folder into a standalone folder and zip it to produce the portable package.

> Some features rely on Windows desktop capabilities (desktop layers, Shell icons, AppX app enumeration, etc.), so run AeroDock in a real interactive Windows desktop session.

### Configuration and data

Configuration is stored in the current user's `%AppData%\GlassDock\` folder:

| File / folder | Purpose |
| --- | --- |
| `dock.json` | Dock app list |
| `Settings.json` | Dock and launch board settings |
| `LaunchBoardWallpaper.json` | Launch board wallpaper configuration |
| `Shortcuts\` | Stored shortcuts |

AeroDock never uploads this local data.

### Project structure

```text
.
├─ GlassDock.csproj          # WPF project (AssemblyName: AeroDock, RootNamespace: GlassDock)
├─ App.xaml / App.xaml.cs    # Application entry
├─ MainWindow.xaml(.cs)      # Dock window, interactions and animations
├─ SettingsWindow.xaml(.cs)  # Settings UI
├─ AcrylicHelper.cs          # Acrylic / glass material helpers
├─ WallpaperAnalyzer.cs      # Wallpaper reading and color analysis
├─ ColorAnalyzer.cs          # Color utilities
├─ DockStorage.cs            # Local JSON persistence
├─ DockConfig.cs             # Dock data model
├─ Assets/app.ico            # Application icon
└─ ...                       # Watchers, icon extraction, Shell/AppX helpers
```

### License

No `LICENSE` file has been added to this repository yet. Choose a license (for example MIT or Apache-2.0) and add it before publishing the project as open source.
