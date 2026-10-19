# virCre

把 Android 手机上的应用带到 Windows 桌面：应用目录与真实图标缓存、独立窗口、桌面快捷方式、电脑中文输入，以及窗口下方桌面输入虚拟摄像头。

## 安装和使用

Windows 10 2004（19041）或更高版本，x64；手机需要 Android 16（API 36）和已授权的 USB/无线 ADB。

当前版本为 **0.1**，安装器和便携包位于 `out/installers/`。安装版默认安装到当前用户的 `%LOCALAPPDATA%\Programs\virCre`，无需管理员权限。便携包需完整解压到固定目录，再启动 `VirCre.Desktop.exe`。

ADB、修改版 scrcpy、.NET 和 WinUI 运行依赖已内置，用户不需要自行安装 scrcpy、Android Studio 或开发工具。USB 驱动与手机调试授权仍需按设备情况完成。

- [Windows 打包、安装与验证边界](docs/windows-packaging.html)
- [虚拟摄像头、默认开关与帧率设置](docs/virtual-camera-window.html)
- [应用图标和桌面快捷方式](docs/desktop-shortcuts.html)
- [内置 scrcpy 与来源](docs/bundled-scrcpy.html)

## 当前行为

- 启动先读手机应用缓存，点击刷新时才重新读取；保留真实图标、收藏和主题。
- 点击打开应用、双击卡片或使用快捷方式，均遵循设置中的相机开关，默认开启。
- 虚拟摄像头输入与虚拟显示分辨率一致，默认1440×2560；随投屏窗口移动、缩放更新下层桌面。
- 设置内可调1–30fps，默认15fps，目标帧率可在运行中调整；实际性能由电脑、手机和连接决定。
- 当前同时仅支持一个相机窗口，每次最长10分钟。关闭全局开关后可新建普通窗口；已有窗口保持当前模式。
- 不需要探针APK、不获取root、不替换手机主屏上的物理相机，不保存相机图像、输入文字或剪贴板内容。

## 源码与构建

```text
desktop/VirCre.Desktop/       WinUI 3 主程序
desktop/VirCre.Presentation/  界面状态与设置
desktop/VirCre.Launcher/      设备、目录、缓存、窗口、快捷方式与设置存储
desktop/VirCre.Camera/        桌面采集与虚拟相机生命周期
android-agent/               手机图标导出DEX源码，无APK工程
camera-agent/                正式相机Java源码，嵌入scrcpy-server
third_party/scrcpy/           配套客户端与服务端源码
packaging/virCre.iss          EXE安装器定义
tools/Build-Installer.ps1     Release安装器、ZIP和校验值构建入口
docs/                        HTML说明与历史验证记录
```

原 `android/` 探针APK工程和根目录Gradle遗留项已清理；其中9个正式功能依赖的Java文件已迁移到camera-agent并核对哈希。原P0和WinForms实验入口不再由主启动脚本提供。

构建机器需要 .NET SDK 8、PowerShell 7、Android SDK API 36、Build Tools、JDK，以及自定义scrcpy使用的GCC/Meson/Ninja工具链；制作安装器还需Inno Setup 6。详见上方HTML打包说明。

```powershell
# 启动开发版（使用已有自定义运行时）
pwsh -NoProfile -File .\tools\Start-Desktop.ps1

# 一键重编译release运行时，发布并生成安装器与便携包
pwsh -NoProfile -File .\tools\Build-Installer.ps1 -Version 0.1
```

打包脚本只编译和检查文件，不自动运行测试、安装成品或操作手机。安装、卸载、覆盖升级、干净系统启动及设备功能由用户手动验收；不能把编译成功等同于这些项目已通过。

## 数据与分发边界

设置、应用缓存和持久快捷方式图标保存在当前用户的 `%LOCALAPPDATA%\virCre`；安装器卸载默认保留这些数据。移动便携程序目录后，指向旧目录的手机应用快捷方式需重新创建。

首版成品未做代码签名。内置第三方许可位于 `runtime/scrcpy/licenses` 和 `runtime/scrcpy/LICENSE.txt`；本仓库尚未指定主项目开源许可证，公开分发前需明确授权及第三方分发要求。
