# Changelog

All notable changes to AeroDock are documented in this file.

AeroDock 的所有重要变更都会记录在此文件中。

## [1.0.0] - 2026-10-19

### Added / 新增

- AeroDock 首个公开版本发布。  
  First public release of AeroDock.
- **应用收纳**：将常用应用、文件夹与快捷方式集中到 Dock；支持“保留在桌面 / 收纳至 Dock”两种桌面图标处理模式。  
  **App collection**: keep frequently used apps, folders and shortcuts in the Dock, with two desktop icon modes (keep on desktop / store into Dock).
- **液态玻璃动画**：Dock 图标悬停放大、点击反馈与展板开合动画。  
  **Liquid-glass animations**: hover magnification, click feedback and launch-board open/close animations.
- **壁纸取色**：截取当前屏幕或分析壁纸提取主色，实时应用到 Dock 玻璃；壁纸文件失效时仍可使用屏幕取色。  
  **Wallpaper color extraction**: sample the current screen or analyze the wallpaper and apply the dominant color to the Dock glass.
- **独立设置**：Dock 与展板的透明度、模糊强度、壁纸等分别独立调节，修改即时生效并自动保存。  
  **Independent settings**: Dock and launch board can be tuned separately (opacity, blur, wallpaper) with instant apply and auto-save.
- **材质预设**：经典亚克力 (Acrylic)、极简纯色 (Minimal)、高光质感 (Glossy) 三种风格一键切换。  
  **Material presets**: one-click switch between Acrylic, Minimal and Glossy.
- **本地配置持久化**：设置保存在 `%AppData%\GlassDock\`，重启不丢失。  
  **Local persistence**: settings are stored in `%AppData%\GlassDock\` and survive restarts.
- **Windows x64 自包含单文件发布**：无需目标机预装 .NET 运行时。  
  **Self-contained single-file Windows x64 distribution**: no .NET runtime installation required on the target machine.

### Distribution / 发布形态

- `AeroDock-Windows-x64-v1.0.0.zip`：Windows 10/11 x64 便携版，解压后运行 `AeroDock.exe`。  
  Portable Windows 10/11 x64 build; unzip and run `AeroDock.exe`.
- `win-x64`、`--self-contained`、`PublishSingleFile=true` 发布；压缩包内包含运行所需的原生 DLL。  
  Published with `win-x64`, `--self-contained` and `PublishSingleFile=true`; required native DLLs are bundled in the ZIP.

### Notes / 说明

- 配置文件位置：`%AppData%\GlassDock\`（`dock.json`、`Settings.json`、`LaunchBoardWallpaper.json`、`Shortcuts\`）。  
  Configuration location: `%AppData%\GlassDock\` (`dock.json`, `Settings.json`, `LaunchBoardWallpaper.json`, `Shortcuts\`).
- 可执行文件暂未进行代码签名，首次运行时 Windows SmartScreen 可能提示；请选择“更多信息 → 仍要运行”。  
  The executable is not code-signed yet; Windows SmartScreen may warn on first launch. Choose "More info → Run anyway".
- 压缩包内的 `AeroDock.pdb` 为调试符号文件，可安全删除，不影响程序运行。  
  `AeroDock.pdb` in the ZIP is a debug symbol file and can be safely removed.
