# virCre UI 与逻辑解耦改造计划

> 执行方式：使用 `superpowers:executing-plans` 逐项实施。每项完成后更新复选框。任务 01–07 已执行，08–09 待用户确认后继续。会话生命周期和隐藏主窗口改造已移出本轮范围。

**Goal：** 减少主窗口中的业务编排和重复状态判断，让设备刷新、应用启动、快捷方式等流程可以脱离 WinUI 控件复用。

**Architecture：** 保留 Desktop、Presentation、Launcher、Camera 分层，在现有接口上渐进拆分。先处理局部通知和重复判断，再提取流程，最后迁移复杂绑定。

**Tech Stack：** C#、.NET 8、WinUI 3、现有自定义 scrcpy；不新增 MVVM 框架或依赖注入容器。

**Spec：** 本文“改造边界”与“必须保留的行为”。依据当前源码及本次对话中的两份架构评估编写，基线提交为 `bef3cd39`。执行前只核对相关文件是否发生变化，不重新开展全项目评估。

## 改造边界

- 按单人项目维护，不重写整个应用，不以“清空 code-behind”作为目标。
- 布局、窗口尺寸、DPI、焦点、指针捕获、原生拖拽、弹窗及 UI 线程派发保留在 Desktop。
- Presentation 允许保留展示文案、颜色和图标；本轮不做多语言或完整资源化。
- Launcher、Camera、Android agent、自定义 scrcpy 的现有能力继续复用。
- 不修改缓存格式、快捷方式参数格式、设置文件格式和发布脚本。
- 在 `main` 上开发；每项是一个可独立审阅的提交单位，推送和发版按当次用户要求执行。
- 成本按影响范围和回归风险估计，不是工时承诺。同一档内差异较小，实际执行还需满足依赖。

## 必须保留的行为

1. 有有效缓存时，启动或切换手机不全量扫描应用；点击刷新才主动更新。
2. USB 与无线连接按硬件标识合并；设备变化后旧卡片不能操作另一部手机。
3. 普通窗口最低 Android 10，虚拟摄像头最低 Android 16；设置不强行改变已有窗口模式。
4. 卡片和快捷方式打开同一设备、同一应用时，复用已有窗口；快捷方式不要求用户先打开主界面。
5. 中文自然输入、应用图标、快捷方式图标、整卡点击与独立收藏、拖拽导出继续可用。
6. 关闭仍等待后台任务和子窗口收尾；超时不遗弃活跃进程，允许重试关闭。
7. 虚拟摄像头保持现有采集、帧率、暂停恢复和拖动刷新行为，不增加会话时长限制。

## 验证与成本控制

- 本文任务中的“验收”是执行时应覆盖的行为，目前均未验证。
- 延续用户要求：不自动启动桌面应用、不操作手机、不安装 APK、不运行测试程序；界面与真机验证由用户完成。
- 每个逻辑单元完成后统一做静态检查和相关项目构建，不每改一个文件就构建。
- 新增业务判断的验证场景优先放入现有控制台验证项目，不引入测试框架；涉及文件写入或进程启动时使用假实现。运行这些程序需用户后续明确授权。
- 纯展示绑定无需编写逐个控件属性断言的测试；构建加用户确认即可。
- 不因本次 C# 重构重编 scrcpy、不打安装包、不运行完整发布流程。
- 无新修改或失败时不重复验证同一组内容。

静态检查命令：`git diff --check`。

构建只选受影响项目，例如：

```powershell
dotnet build desktop/VirCre.Presentation/VirCre.Presentation.csproj -c Release
dotnet build desktop/VirCre.Desktop/VirCre.Desktop.csproj -c Release -p:ScrcpyRuntimeVariant=Custom
```

Desktop 构建包含其依赖，不需要先把依赖全部再构建一遍。本文只检查 Markdown 和路径，不运行上述构建。

## 任务总览：成本从低到高

| 任务 | 内容 | 成本 | 前置任务 | 建议 |
|---|---|---|---|---|
| 01 | 补齐设置属性通知 | 低 | 无 | 做 |
| 02 | 统一卡片操作校验 | 低 | 无 | 做 |
| 03 | 提取相机兼容性展示状态 | 低至中 | 01 | 做 |
| 04 | 迁移简单文本与计数绑定 | 低至中 | 01 | 做 |
| 05 | 提取设置保存队列 | 中 | 01 | 做 |
| 06 | 统一卡片与快捷方式的应用启动服务 | 中 | 02 | 做 |
| 07 | 提取快捷方式解析与启动流程 | 中至高 | 06 | 做 |
| 08 | 提取设备刷新和选择控制器 | 高 | 02 | 核心改造 |
| 09 | 迁移剩余业务状态绑定 | 高 | 03、04、08 | 做 |

## 任务 01：补齐设置属性通知

涉及文件：`desktop/VirCre.Presentation/ShellViewModel.cs`、`desktop/VirCre.Presentation.Tests/Program.cs`。

交付：`Theme`、`VirtualCameraEnabled`、`CameraFrameRate` 变化时发出对应 `PropertyChanged`；保留 `SettingsChanged` 作为保存请求，两类通知各司其职。

- [x] 补齐三个设置方法的属性通知，值未变化时不重复请求保存。
- [x] 在现有验证项目补充场景：变化通知一次、相同值不重复、非法帧率仍按现有规则拒绝、通知发生时新值已可读。
- [x] 统一检查通知与保存的调用关系，避免通过 `NotifyState()` 广播全部属性来掩盖遗漏。
- [x] 静态检查并构建 Presentation；单独提交。

验收：后续改成 `OneWay` 绑定时，设置变化能够刷新界面，保存次数不增加。

## 任务 02：统一卡片操作校验

新增：`desktop/VirCre.Presentation/AppActionGuard.cs`。

修改：`MainWindow.Windows.cs`、`MainWindow.Shortcuts.cs`、`MainWindow.Drag.cs`，以及 `desktop/VirCre.Presentation.Tests/Program.cs`。

建议接口：`AppActionGuard.CanActOn(AppCard card, PhoneDevice? device, ShellViewModel model, bool scanning, bool stopping)`，返回 `bool`。参数均为数据，不接收控件。

- [x] 集中判断停止状态、设备在线、扫描或加载状态、卡片序列号、当前列表归属与包名是否仍存在。
- [x] 打开应用、准备快捷方式和拖拽使用同一守卫；错误提示仍由调用方决定。
- [x] 拖拽或快捷方式准备跨越 `await` 时，使用原操作的设备与卡片快照，并在产生下一步副作用前重新检查，不能只在最开始检查一次。
- [x] 补充离线、切换设备、旧卡片、加载中、退出中、正常操作场景；静态检查并构建 Desktop，单独提交。

验收：同一张失效卡片，在三个入口都会被拒绝；不把普通卡片守卫用于快捷方式直启，后者不依赖可见应用列表。

## 任务 03：提取相机兼容性展示状态

新增：`desktop/VirCre.Presentation/CameraCompatibilityState.cs`。

修改：`MainWindow.Settings.cs`、`desktop/VirCre.Presentation.Tests/Program.cs`。

建议接口：不可变记录 `CameraCompatibilityState`，提供 `ToggleChecked`、`ToggleEnabled`、`FrameRateEnabled`、`Description`；静态 `Create(PhoneDevice? device, bool preferredEnabled, bool hasCameraWindow, bool stopping)` 计算结果。

- [x] 将 Android 版本、相机开关和帧率控件的派生判断移入该类型，保留当前文案。
- [x] `RenderCameraCompatibility()` 暂时只负责把结果应用到控件，保留防止程序赋值触发设置保存的保护。
- [x] 补充未连接、版本未知、API 28、29、35、36，以及另一手机仍有相机会话的场景。
- [x] 静态检查并构建 Desktop；单独提交。

验收：不支持相机的手机只影响当前可用状态，不覆盖用户保存的默认开关；已有相机会话仍可调整帧率。

## 任务 04：迁移简单文本与计数绑定

修改：`desktop/VirCre.Desktop/MainWindow.xaml`、`MainWindow.xaml.cs`。

- [x] 为 XAML 暴露只读 `public ShellViewModel Model => model`；模型仍在 `InitializeComponent()` 前创建。
- [x] 将页标题、应用计数、收藏计数、运行窗口计数改为 `x:Bind Model.<Property>, Mode=OneWay`；空状态标题和描述仍含扫描提示优先级，按下一项留待任务 08、09。
- [x] 迁移过程中保留现有扫描提示优先级：仍含窗口本地条件的字段先不绑定，留待任务 08、09 处理。
- [x] 删除已迁移字段的手工赋值，确保同一属性只有一个写入来源；静态检查并构建 Desktop，单独提交。

验收：搜索、收藏、页面切换及窗口开关后的文本和计数更新保持一致。页脚消息、主题刷色和交互禁用暂不迁移。

## 任务 05：提取设置保存队列

新增：`desktop/VirCre.Launcher/SettingsSaveQueue.cs`。

修改：`MainWindow.Settings.cs`、主窗口退出等待清单、`desktop/VirCre.FeatureTests/Program.cs`。

建议接口：构造时传入 `Action<UserSettings> save`；提供 `Task EnqueueAsync(UserSettings snapshot)`、`Task StopAsync()`。窗口把 `settingsStore.Save` 传入，验证代码使用假保存函数。

- [x] 移出串行写入、任务追踪与停止接收新请求的逻辑；每次请求持有设置快照。
- [x] 单次保存失败能由调用方观察，但不能阻断后续保存；错误显示和 UI 线程派发仍由 Desktop 完成。
- [x] `StopAsync()` 幂等，等待已接收请求，关闭后的新请求明确拒绝；退出清单等待队列而非窗口内的 `settingsTask`。
- [x] 补充保存顺序、一次失败后继续、停止等待和停止后拒绝场景；静态检查并构建 Desktop，单独提交。

验收：快速切换设置时最终文件对应最后一次请求，错误提示仍显示，关闭不丢弃已接收的写入。

## 任务 06：统一应用启动服务

新增：`desktop/VirCre.Launcher/AppLaunchService.cs`。

修改：`MainWindow.Windows.cs`、`MainWindow.Shortcuts.cs`、`desktop/VirCre.WindowTests/Program.cs`。

建议接口：构造时接收同一个 `AppWindowManager`；提供 `Task<AppOpenResult> OpenAsync(PhoneDevice device, string packageName, string name, bool virtualCameraEnabled, int frameRate, CancellationToken token)`。

- [x] 将现有 `CreateLaunchRequest()` 的窗口复用与兼容性处理移入服务。
- [x] 卡片和现有快捷方式启动流程都调用该服务，设置值在发起操作时作为快照传入。
- [x] 保持设备硬件标识、已有窗口模式和 `AndroidCompatibility` 规则；不修改 scrcpy 参数与 Camera 实现。
- [x] 使用现有假窗口进程补充同一应用复用、改变默认模式后复用、不同手机同包名分别启动场景；静态检查并构建 Desktop，单独提交。

验收：两个入口的窗口复用和 Android 版本兼容行为一致，仍只有一个窗口管理器拥有进程。

## 任务 07：提取快捷方式解析与启动流程

新增：`desktop/VirCre.Launcher/ShortcutLauncher.cs`。

修改：`MainWindow.Shortcuts.cs`、主窗口依赖装配位置、`desktop/VirCre.FeatureTests/Program.cs`。

建议接口：构造时接收 `IPhoneCatalog`、`ICommandRunner`、`AppLaunchService`，以及 `Func<string,string,CancellationToken,Task<bool>> canLaunch`；提供 `Task<AppOpenResult> OpenAsync(string payload, UserSettings settings, CancellationToken token)`。

- [x] 移入参数解码、设备查询、硬件身份解析、应用入口校验和启动调用；继续复用 `ShortcutRequest`。
- [x] 在统一装配位置将 `PhoneCatalog.CanLaunchAsync` 作为委托传入；不为了单个调用增加一整套仓储接口，也不在点击处理函数内创建依赖。
- [x] 窗口保留连续快捷方式激活的排队、提示及显示主窗口逻辑；服务不引用 `Window`、Dispatcher 或控件。
- [x] 补充无线地址变化、地址被另一手机复用、设备未授权、应用已卸载、无可见列表时直启场景；静态检查并构建 Desktop，单独提交。

验收：快捷方式业务流程可用假目录和假命令运行；所有入口继续使用同一个 `AppLaunchService` 和 `AppWindowManager`。此时仍允许保留隐藏主窗口。

## 任务 08：提取设备刷新与选择控制器

新增：`desktop/VirCre.Presentation/DeviceController.cs`、`DeviceSelectionState.cs`。

修改：`MainWindow.Devices.cs`、`ShellViewModel.cs`、任务 02 守卫的调用处、`desktop/VirCre.Presentation.Tests/Program.cs`。

建议边界：`DeviceSelectionState` 保存设备快照、选中设备、扫描状态和设备读取错误。`DeviceController` 接收 `IPhoneCatalog`、现有 `CatalogController`；公开只读 `State`、`Changed` 事件、`RefreshAsync(bool forceRefresh, CancellationToken token)`、`SelectAsync(PhoneDevice? device, CancellationToken token)`、`StopAsync()`。

- [ ] 移入扫描代次、取消令牌、选中设备恢复、设备查询失败处理；保留“硬件身份优先，其次连接序列号，只有一台时自动选择”的规则。
- [ ] 扫描状态与选中设备只有控制器一个来源；ViewModel 只接收当前快照并派生展示属性，窗口不再用 `scanning` 或 `SelectedItem` 决定业务权限。
- [ ] 扫描或选择变更取消旧请求，旧设备与旧应用结果不得覆盖新选择；停止时等待所有尚未完成的请求，不能只追踪最后一个任务。
- [ ] 保持原有错误恢复和缓存保留策略；把 `TryRecoverCatalogFormatError()` 的业务恢复部分收进控制器，Desktop 只保留异常入口和提示。
- [ ] 明确线程约束：控制器在 UI 同步上下文调用并发布状态；底层 I/O 可在后台完成，回写可观察集合前必须回到所属上下文。
- [ ] 用可控完成顺序的假目录补充旧扫描晚返回、切换设备时旧列表晚返回、离线保留缓存但禁止操作、授权恢复、关闭中取消场景；静态检查并构建 Desktop，单独提交。

验收：`MainWindow.Devices.cs` 只处理刷新和选中事件、控件同步及派发；不再拥有设备扫描的并发规则。

## 任务 09：迁移剩余业务状态绑定

修改：`ShellViewModel.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`MainWindow.Settings.cs`。

新增派生属性按用途命名，例如 `IsSearchEnabled`、`IsDevicePickerEnabled`、`IsRefreshEnabled`、`IsAppGridEnabled`、`ShowEmptyState`、`ShowAppGrid`、`ShowRunningList`、`ShowSettings`、`IsBusy`。不引入 WinUI `Visibility` 或 `Brush` 到 Presentation。

- [ ] 将设备、列表、页面及任务 03 相机状态组合成只读展示属性；停止与清理状态通过明确的更新入口传入，不让模型读取窗口。
- [ ] 用 `OneWay` 绑定替代可用状态和业务可见性赋值；继续复用现有布尔可见性转换器。
- [ ] 主题色、系统高对比度、DPI、布局和页脚消息计时仍留在 View；将剩余 `RenderState()` 拆成名字准确的小型 UI 方法。
- [ ] 设置控件保留用户输入处理和防回写循环；不强行把所有事件换成命令。
- [ ] 补充派生状态的组合场景，尤其扫描中、加载失败、无设备、空收藏和清理重试；静态检查并构建 Desktop，单独提交。

验收：常规业务状态刷新不依赖整块控件赋值；同一控件属性没有绑定与手工赋值同时写入。底部消息到时仍恢复最新设备状态。

## 分批执行建议

- 第一批：01–04。成本较低，便于确认通知、守卫和绑定方向。
- 第二批：05–07。统一保存与启动流程，减少视图中的业务编排。
- 第三批：08–09。完成设备状态收拢，解决主要耦合问题。

每批结束只需交付：完成任务编号、提交、构建结果、未验证的用户操作及剩余任务。不默认发版，不重复做全项目评审。

## 计划自检

- [x] 任务按相对成本由低到高排列，依赖均指向前面的任务。
- [x] 低成本清理、业务提取、状态绑定迁移可以分批停止。
- [x] 设备扫描、卡片操作、快捷方式直启采用各自正确的校验边界。
- [x] 保留已有缓存、身份识别、输入、相机和退出规则。
- [x] 不将 UI 原生行为强行移进 ViewModel，不增加新的框架。
- [x] 区分计划中的验收与已经完成的验证。
