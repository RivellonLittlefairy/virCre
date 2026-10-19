using System.Text.Json;
using VirCre.Launcher;

namespace VirCre.Camera;

/// <summary>Ordinary windows keep their existing launch path. One camera session is reserved until cleanup completes.</summary>
public sealed class DesktopWindowHost(Action<string>? inputDiagnostic=null,Action<string>? notice=null,Action<string>? cameraDiagnostic=null) : IAppWindowHost {
    private readonly BundledTools tools=new();
    private readonly object sync=new();
    private VirtualCameraSession? camera;
    private (string Serial,string Package) owner;
    public IAppWindowProcess Start(AppLaunchRequest request) {
        if(!request.UseVirtualCamera)return new ScrcpyWindowHost(tools,inputDiagnostic).Start(request);
        RequireCaptureRuntime();
        lock(sync) {
            if(camera is {HasExited:false})throw new InvalidOperationException("实验版每次只能打开一个虚拟摄像头应用，请先关闭当前相机窗口。");
            owner=(request.Serial,request.PackageName);
            camera=new(request,tools,inputDiagnostic,notice,cameraDiagnostic);
            return camera;
        }
    }
    public CameraStatus GetStatus(AppLaunchRequest request) {
        lock(sync)return Match(request)?.Status??new(true,"此相机会话已结束。");
    }
    public void SetPaused(AppLaunchRequest request,bool paused) {
        lock(sync) {
            var active=Match(request)??throw new IOException("此相机会话已结束，请重新打开应用。");
            active.SetPaused(paused);
        }
    }
    public void SetTargetFrameRate(int frameRate) {
        frameRate=CameraFrameRates.Validate(frameRate);
        lock(sync) {
            if(camera is {HasExited:false})camera.SetTargetFrameRate(frameRate);
        }
    }
    private VirtualCameraSession? Match(AppLaunchRequest request)=>owner==(request.Serial,request.PackageName) && camera is {HasExited:false} ? camera : null;
    private void RequireCaptureRuntime() {
        var path=Path.Combine(tools.DirectoryPath,"runtime-manifest.json");
        if(!File.Exists(path))throw new IOException("此发布包不包含虚拟摄像头组件，请使用 camera-preview 完整目录。");
        using var manifest=JsonDocument.Parse(File.ReadAllText(path));
        var root=manifest.RootElement;
        if(!root.TryGetProperty("distribution",out var distribution) || distribution.GetString()!="vircre-source-build" ||
            !root.TryGetProperty("features",out var features) || features.ValueKind!=JsonValueKind.Array ||
            !features.EnumerateArray().Any(item=>item.GetString()=="window-capture-exclusion-v1") ||
            !features.EnumerateArray().Any(item=>item.GetString()=="vdm-direct-encoder-v1") ||
            !features.EnumerateArray().Any(item=>item.GetString()=="camera-hd-jpeg-v1") ||
            !features.EnumerateArray().Any(item=>item.GetString()=="camera-display-size-v1") ||
            !features.EnumerateArray().Any(item=>item.GetString()=="camera-live-move-v1"))
            throw new IOException("内置相机组件尚不支持拖动时连续刷新，请使用新版 camera-drag 发布包的完整目录。");
    }
}
