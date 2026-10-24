using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>
/// Immutable snapshot of device discovery and selection. The selected device and the
/// scanning flag live here only, so views never have to agree on their own copy.
/// </summary>
public sealed record DeviceSelectionState(
    IReadOnlyList<PhoneDevice> Devices,
    PhoneDevice? Selected,
    bool Scanning,
    string? Error = null) {
    public PhoneDevice? OnlineDevice => Selected is {State:DeviceState.Online} ? Selected : null;
}
