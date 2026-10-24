using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>
/// Shared eligibility check for acting on a visible app card. The card may outlive
/// the device or catalog it came from, so callers pass the device they started with.
/// </summary>
public static class AppActionGuard {
    public static bool CanActOn(AppCard card, PhoneDevice? device, ShellViewModel model, DeviceSelectionState selection, bool stopping) {
        if(stopping || selection.Scanning || model.IsLoading || !model.HasDevice)return false;
        if(device is not {State:DeviceState.Online})return false;
        // The device selected at the start must still be the selected one now.
        if(selection.Selected?.Serial != device.Serial)return false;
        return card.SourceSerial==device.Serial &&
            model.CurrentSerial==device.Serial &&
            model.Apps.Any(app=>app.PackageName==card.PackageName);
    }
}
