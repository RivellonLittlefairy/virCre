namespace VirCre.Presentation;

/// <summary>Immutable display data; missing device icons retain a recognizable fallback.</summary>
public sealed record AppCard(string Name, string PackageName, string Glyph, string Color, bool IsFavorite = false, bool IsSystem = false, string SourceSerial = "", string? IconPath = null, bool IsRunning = false, string WindowState = "") {
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
    public string FavoriteLabel => IsFavorite ? $"取消收藏{Name}" : $"收藏{Name}";
    public string KindLabel => IsSystem ? "系统应用" : "已安装";
    public string OpenLabel => IsRunning ? $"切回{Name}" : $"打开{Name}";
    public string OpenHint => WindowState.Length > 0 ? WindowState : "点击打开";
    public string InactiveHint => IsRunning ? "" : OpenHint;
    public override string ToString() => Name;
}
