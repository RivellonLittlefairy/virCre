namespace VirCre.Presentation;

/// <summary>Immutable display data; app icons are placeholders until icon extraction is implemented.</summary>
public sealed record AppCard(string Name, string PackageName, string Glyph, string Color, bool IsFavorite = false, bool IsSystem = false, string SourceSerial = "") {
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
    public string FavoriteLabel => IsFavorite ? $"取消收藏{Name}" : $"收藏{Name}";
    public string KindLabel => IsSystem ? "系统应用" : "已安装";
    public override string ToString() => Name;
}
