namespace VirCre.Presentation;

/// <summary>Illustrative apps only. These names never claim to come from a connected device.</summary>
internal static class DemoCatalog {
    public static AppCard[] Create() => [
        new("相册", "demo.gallery", "\uEB9F", "#507C93"),
        new("备忘录", "demo.notes", "\uE70B", "#B58A4C"),
        new("日历", "demo.calendar", "\uE787", "#A86F73"),
        new("音乐", "demo.music", "\uE8D6", "#8576A6"),
        new("浏览器", "demo.browser", "\uE774", "#548D8A"),
        new("文件", "demo.files", "\uE8B7", "#AB8F5A"),
        new("时钟", "demo.clock", "\uE823", "#697E99"),
        new("计算器", "demo.calculator", "\uE8EF", "#768B6F")
    ];
}
