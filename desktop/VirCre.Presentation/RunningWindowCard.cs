using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>Window controls with artwork already available in the selected phone's catalog.</summary>
public sealed record RunningWindowCard(AppWindowInfo Window, string? IconPath, string Color) {
    public string Glyph => "\uE8A9";
}
