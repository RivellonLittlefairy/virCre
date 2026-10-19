using VirCre.Launcher;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private static readonly string PreferredDataDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"virCre");
    internal static readonly DataLocation StorageLocation=DataLocation.Resolve(PreferredDataDirectory,Path.Combine(AppContext.BaseDirectory,"virCre-data"));
    internal static readonly string DataDirectory=StorageLocation.Path;
    private readonly SettingsStore settingsStore=new(Path.Combine(DataDirectory,"settings.json"));
    private Task settingsTask=Task.CompletedTask;
    private SettingsLoad LoadSettings() {
        if(StorageLocation.UsedFallback && !File.Exists(Path.Combine(DataDirectory,"settings.json")))
            return new SettingsStore(Path.Combine(PreferredDataDirectory,"settings.json")).Load();
        return settingsStore.Load();
    }
    private void SaveSettings() {
        var snapshot=model.ExportSettings();
        settingsTask=SaveAfterAsync(settingsTask,snapshot);
    }
    private async Task SaveAfterAsync(Task previous, UserSettings settings) {
        await previous;
        try {await Task.Run(()=>settingsStore.Save(settings));}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {ShowAction("设置保存失败，本次更改暂留内存："+error.Message,true);}
    }
}
