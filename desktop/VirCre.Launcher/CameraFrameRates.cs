namespace VirCre.Launcher;

/// <summary>Shared bounds for persisted preferences and the desktop camera pump.</summary>
public static class CameraFrameRates {
    public const int Minimum=1,Maximum=30,Default=15;
    public static int Normalize(int value)=>value>=Minimum && value<=Maximum?value:Default;
    public static int Validate(int value)=>value>=Minimum && value<=Maximum?value:
        throw new ArgumentOutOfRangeException(nameof(value),"相机目标帧率必须在1–30fps之间。");
}
