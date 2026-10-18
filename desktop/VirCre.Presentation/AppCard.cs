namespace VirCre.Presentation;

/// <summary>Immutable display data; the shell owns changes to its sample catalog.</summary>
public sealed record AppCard(string Name, string PackageName, string Glyph, string Color, bool IsFavorite = false) {
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
    public string FavoriteLabel => IsFavorite ? $"取消收藏{Name}" : $"收藏{Name}";
}
