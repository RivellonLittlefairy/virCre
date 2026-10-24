using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>
/// Camera settings depend on the selected phone, not the other way round: an
/// unsupported phone disables the controls without rewriting the saved default.
/// </summary>
public sealed record CameraCompatibilityState(bool ToggleChecked, bool ToggleEnabled, bool FrameRateEnabled, string Description) {
    public static CameraCompatibilityState Create(PhoneDevice? device, bool preferredEnabled, bool hasCameraWindow, bool stopping, IShellText? text = null) {
        var wording=text??ShellText.Default;
        var online=device is {State:DeviceState.Online};
        var unavailable=online && !device!.SupportsVirtualCamera;
        return new(
            ToggleChecked:preferredEnabled && !unavailable,
            ToggleEnabled:!stopping && !unavailable,
            FrameRateEnabled:!stopping && (!unavailable || hasCameraWindow),
            Description:!online ? wording[TextKeys.CameraRequirements] :
                device!.ApiLevel is null ? wording[TextKeys.CameraUnknownVersion] :
                device.ApiLevel<AndroidCompatibility.MinimumAppApi ? wording.Format(TextKeys.CameraUnsupported, device.AndroidVersionLabel) :
                unavailable ? wording.Format(TextKeys.CameraDisabled, device.AndroidVersionLabel) :
                wording.Format(TextKeys.CameraSupported, device.AndroidVersionLabel));
    }
}
