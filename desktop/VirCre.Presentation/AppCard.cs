namespace VirCre.Presentation;

/// <summary>Immutable display data; missing device icons retain a recognizable fallback.</summary>
public sealed record AppCard(string Name, string PackageName, string Glyph, string Color, bool IsFavorite = false, bool IsSystem = false, string SourceSerial = "", string? IconPath = null, bool IsRunning = false, string WindowState = "") {
    /// <summary>Wording catalog of the shell that built this card; sample and test cards use the default.</summary>
    public IShellText Text {get;init;}=ShellText.Default;
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
    public string FavoriteLabel => Text.Format(IsFavorite ? TextKeys.CardUnfavorite : TextKeys.CardFavorite, Name);
    public string KindLabel => Text[IsSystem ? TextKeys.CardKindSystem : TextKeys.CardKindInstalled];
    public string OpenLabel => Text.Format(IsRunning ? TextKeys.CardSwitchBack : TextKeys.CardOpen, Name);
    public string OpenHint => WindowState.Length > 0 ? WindowState : Text[TextKeys.CardOpenHint];
    public string InactiveHint => IsRunning ? "" : OpenHint;
    public override string ToString() => Name;
}
