# virCre

把 Android 手机上的应用带到 Windows 桌面：实时读取已连接手机的应用目录和真实图标，在独立的 Windows 窗口里打开它们，并生成可双击的桌面快捷方式。

> 当前主线是 **Windows 桌面启动器**。仓库里还保留了一套基于 Android 16 虚拟设备（VDM）与 Camera2 的虚拟摄像头实验，属于内部实验入口，不是日常使用路径。

## 功能

- **实时应用目录**：通过 `adb` 发现手机，通过官方 `scrcpy --list-apps` 读取可启动应用，支持按名称或包名搜索。
- **真实应用图标**：通过 ADB 临时推送一个只读 dex 组件，用 `app_process` 读取 `PackageManager` 图标并渲染为 PNG；不需要安装 APK，也不需要 root。
- **独立应用窗口**：双击卡片或点击“打开应用”，由 `scrcpy --new-display` 在新的 Android 显示上启动该 App，并提供独立 Windows 窗口。
- **窗口管理**：同一设备、同一包名只保留一个窗口；重复打开会切回已有窗口；“运行中”页面可以切回或关闭窗口。
- **桌面快捷方式**：从卡片的图标、名称或空白区域拖到桌面，或右键选择“创建桌面快捷方式”，生成带真实图标的 `.lnk`；双击后自动匹配手机并打开对应窗口。
- **收藏与主题**：星标收藏、系统/浅色/深色主题，跨重启保留。
- **单实例与参数转发**：主程序只运行一个实例；快捷方式通过数据参数唤起已有进程，不拼接可执行 shell 文本。
- **安全边界**：命令使用参数数组并设置超时；设备序列号和包名有严格校验；快捷方式参数是 Base64 编码的 JSON 数据，不是命令行脚本。

## 工作原理

1. `adb devices -l` 获取在线、未授权、离线设备。
2. `scrcpy -s <serial> --list-apps` 获取应用名称、包名和系统/用户标记。
3. 图标组件通过 ADB 推送到 `/data/local/tmp`，由 `app_process` 执行，逐批返回 PNG；命令结束后删除临时 jar。
4. 打开应用时执行 `scrcpy -s <serial> --new-display=1080x1920/420 --start-app=<package>`，并解析 scrcpy 输出的 display id。
5. 快捷方式只保存设备稳定标识（`ro.serialno`）、设备名、包名和应用名；手机无线 ADB 地址变化后仍能按硬件标识重新匹配。

整个日常路径不会安装用户 App、不会获取 root、不会把手机应用安装到 Windows，也不会复制应用账号或数据。日常桌面功能**不需要**安装 `android/` 下的探针 APK；该 APK 只用于虚拟摄像头实验。

## 仓库结构

```
desktop/
  VirCre.Desktop/        WinUI 3 主程序（默认入口）
  VirCre.Presentation/   界面状态：目录、搜索、收藏、运行中列表
  VirCre.Launcher/       adb/scrcpy 调用、图标、窗口、快捷方式、设置
  VirCam.Core/           Windows → 手机虚拟相机的帧协议（内部实验）
  VirCam.Desktop/        WinForms 摄像头实验控制台（-CameraLab）
  VirCam.Cli/            图片注入命令行工具（内部实验）
  *.Tests/               控制台断言测试（成功打印 PASS）
android/                 Android 16 探针 App（Camera2 读回）
android-agent/           图标导出 dex 的 Java 源码
tools/                   构建、启动、设备检查、证据与会话脚本
docs/                    HTML 设计、计划与真实验证记录
```

## 环境要求

| 用途 | 要求 |
| --- | --- |
| 运行桌面主程序 | Windows 10 2004（19041）或更高 / Windows 11，x64 |
| 构建桌面主程序 | .NET SDK 8，PowerShell 7；首次还原需要访问 NuGet |
| 构建图标组件 | 桌面构建会调用 `tools/Build-IconAgent.ps1`，需要 Android SDK API 36、Build Tools 和 JDK 11+（默认使用 Android Studio 自带 JBR） |
| 手机连接 | 已授权的 ADB（USB 或无线调试）；当前完整验证环境为 Android 16（API 36） |
| 窗口与目录 | PATH 中可找到 `adb` 和 `scrcpy` 5.0.1 或更高版本 |
| 构建 Android App | Android SDK API 36、Build Tools、JDK 11+ |

`adb` 和 `scrcpy` 没有随程序打包；发布目录自带 .NET、Windows App SDK 运行依赖和图标组件。

## 快速开始

```powershell
cd <仓库目录>
pwsh -File .\tools\Start-Desktop.ps1
```

脚本会构建 `desktop/VirCre.Desktop`（同时检查并编译图标组件）并启动程序；已构建时可以跳过编译：

```powershell
pwsh -File .\tools\Start-Desktop.ps1 -NoBuild
```

使用步骤：

1. 连接手机并在手机上允许此电脑的 USB/无线调试授权。
2. 启动程序，在左侧选择设备；只有一台在线设备时会自动选中。
3. 等待应用列表和真实图标加载，用搜索框按名称或包名过滤。
4. 双击卡片或点击“打开应用”，程序会为该 App 打开独立窗口。
5. 从卡片的图标、名称或空白区域拖到桌面，或右键选择“创建桌面快捷方式”。
6. 在“运行中”页面切回或关闭本工具打开的窗口。

### 发布

```powershell
pwsh -File .\tools\Publish-Desktop.ps1
# 或指定目录
pwsh -File .\tools\Publish-Desktop.ps1 -OutputDirectory D:\Apps\virCre
```

输出默认为 `out\desktop-release\VirCre.Desktop.exe`，并附带 `使用说明.html`。发布目录自带 .NET 和 Windows App SDK 运行依赖，但 `adb` 与 `scrcpy` 仍需在 PATH 中。移动发布目录后，已有快捷方式需要重新创建。

## 测试

桌面测试都是控制台断言程序，成功时打印 `PASS`，失败时返回非零退出码：

```powershell
dotnet run --project desktop\VirCre.Presentation.Tests
dotnet run --project desktop\VirCre.Launcher.Tests
dotnet run --project desktop\VirCre.WindowTests
dotnet run --project desktop\VirCre.FeatureTests
dotnet run --project desktop\VirCre.DesktopTests
dotnet run --project desktop\VirCam.Tests
pwsh -File .\tools\Test-Tools.ps1
```

可选的真机集成检查：

```powershell
dotnet run --project desktop\VirCre.Launcher.Tests -- --device <serial>
dotnet run --project desktop\VirCre.FeatureTests -- --device <serial> android-agent\build\icon-agent.jar
```

Android 构建与单元测试：

```powershell
pwsh -File .\tools\Build.ps1
# 跳过 lint
pwsh -File .\tools\Build.ps1 -SkipLint

.\android\gradlew.bat -p android :app:testDebugUnitTest :app:lintDebug
```

APK 输出在 `android\app\build\outputs\apk\debug\app-debug.apk`。这是本地 debug 签名的实验 APK；lint 通过不代表真机行为通过。

## 数据与缓存

| 位置 | 用途 |
| --- | --- |
| `%LOCALAPPDATA%\virCre\settings.json` | 收藏包名、主题和格式版本；原子写入，文件损坏时保留原文件并使用默认值 |
| `%LOCALAPPDATA%\virCre\icons\<设备哈希>\<代次>` | 从手机读取的 96×96 PNG 图标；每台手机保留有限代次并清理旧代次 |
| `%LOCALAPPDATA%\virCre\shortcut-icons\` | 快捷方式引用的 ICO，按内容哈希去重并持久保存 |
| `%LOCALAPPDATA%\virCre\drag\` | 拖拽生成的临时 `.lnk`，有数量与时间上限 |
| `out\<session>\` | 虚拟摄像头实验的设备信息、日志和 `result.json`，默认不提交 Git |

日常桌面目录只在内存中使用，不写出完整应用清单。收藏跨手机共用，只展示当前手机上已安装的匹配应用。

## 内部实验：Android 16 虚拟摄像头

这条支线用于验证 Android 16 的虚拟设备与虚拟相机通路：通过官方 `cmd companiondevice associate` 调试入口建立临时关联，用 ADB 以 `app_process` 运行 shell 侧代码，创建带自定义相机策略的虚拟显示和虚拟后置相机；Camera2 探针从 Y 平面读取带同步头的标记帧，Windows 还可以把图片或固定桌面区域经本地 socket 送入虚拟相机。

```powershell
# 查看设备与虚拟相机能力
adb devices -l
pwsh -File .\tools\Inspect-Device.ps1 -Serial '<serial>'

# 运行一次 P0（概念验证）会话，时长 5–600 秒
pwsh -File .\tools\Start-P0.ps1 -Serial '<serial>' -Seconds 30

# 旧的 WinForms 摄像头实验入口
pwsh -File .\tools\Start-Desktop.ps1 -CameraLab
```

边界与事实：

- 只适用于 Android 16（API 36），当前只支持主用户（user 0）。
- 不提供 root，不做全局物理相机替换，不会自动确认系统权限或配对。
- 手机端不开 TCP 监听；PC 输入通过 ADB 本地转发和带会话校验的 64 字节大端协议传输。
- 实验日志会记录帧元数据、显示/设备 ID 和二维码内容摘要，但不持久化相机像素、桌面截图、二维码正文或账号令牌。
- 需要完整设计和实测记录时，从 [`docs/index.html`](docs/index.html) 进入。

## 文档

| 文档 | 内容 |
| --- | --- |
| [`docs/index.html`](docs/index.html) | 工程首页、快速入口和当前状态 |
| [`docs/app-windows.html`](docs/app-windows.html) | 独立 App 窗口的使用与真机实测 |
| [`docs/app-catalog.html`](docs/app-catalog.html) | 真实应用目录读取与验证 |
| [`docs/desktop-shortcuts.html`](docs/desktop-shortcuts.html) | 图标、收藏、桌面快捷方式与缓存 |
| [`docs/pc-input.html`](docs/pc-input.html) | Windows 图片/桌面区域输入虚拟相机的使用与实测 |
| [`docs/verification.html`](docs/verification.html) | 真实验证记录汇总 |
| [`docs/p0-design.html`](docs/p0-design.html) · [`docs/p0-plan.html`](docs/p0-plan.html) | 虚拟摄像头 P0 设计与计划 |
| [`docs/app-launcher-design.html`](docs/app-launcher-design.html) · [`docs/app-launcher-plan.html`](docs/app-launcher-plan.html) | 桌面启动器设计与分阶段计划 |

## 已知限制

- 主要真机验证环境为 vivo V2458A / Android 16；多手机并发、长时间运行和所有第三方 App 的副屏兼容性尚未全面验收。
- 独立窗口依赖 scrcpy 的新显示能力；不同 App 在副屏上的行为可能不同，当前关闭音频转发。
- 快捷方式依赖手机稳定硬件标识；无法读取 `ro.serialno` 的手机不能创建自动连接快捷方式，避免误连另一台手机。
- 小屏高 DPI 下首次窗口高度可能超过工作区，这是已知的布局问题。
- 发布程序未做代码签名；Android APK 为 debug 签名，不用于正式分发。
- 仓库目前没有附带开源许可证文件；对外分发前请先补充许可证。

## 贡献

提交前建议至少运行与改动相关的桌面测试和 `pwsh -File .\tools\Test-Tools.ps1`；涉及 Android 端时再运行 `tools\Build.ps1`。设计文档和验证记录使用 HTML，新增行为请同步更新 [`docs/`](docs/) 中对应页面。
