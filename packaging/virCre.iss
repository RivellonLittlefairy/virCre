#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef SetupDir
  #error SetupDir is required
#endif
#ifndef AppVersion
  #define AppVersion "0.1.3"
#endif
#ifndef AppFileVersion
  #define AppFileVersion "0.1.3.0"
#endif

[Setup]
AppId={{69B8728E-504C-4DE2-A978-E9C597B592DB}
AppName=virCre
AppVersion={#AppVersion}
VersionInfoVersion={#AppFileVersion}
AppPublisher=virCre
LicenseFile={#PublishDir}\compliance\THIRD-PARTY-TERMS.rtf
DefaultDirName={localappdata}\Programs\virCre
DefaultGroupName=virCre
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir={#SetupDir}
OutputBaseFilename=virCre-{#AppVersion}-win-x64-setup
SetupIconFile={#PublishDir}\Assets\Brand\vircre.ico
UninstallDisplayIcon={app}\VirCre.Desktop.exe
CloseApplications=yes
CloseApplicationsFilter=VirCre.Desktop.exe
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\virCre\virCre"; Filename: "{app}\VirCre.Desktop.exe"; WorkingDir: "{app}"
Name: "{autoprograms}\virCre\使用说明"; Filename: "{app}\使用说明.html"
Name: "{autoprograms}\virCre\开源许可与源码"; Filename: "{app}\THIRD-PARTY-NOTICES.html"
Name: "{autodesktop}\virCre"; Filename: "{app}\VirCre.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\VirCre.Desktop.exe"; Description: "启动 virCre"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
