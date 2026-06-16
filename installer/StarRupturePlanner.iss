#define MyAppName "StarRupture Planner"
#define MyAppExeName "StarRupturePlanner.exe"
#define MyAppPublisher "StarRupture Planner"
#define MyAppVersion GetEnv("APP_VERSION")
#define PackageRoot GetEnv("PACKAGE_ROOT")
#define InstallerOutputDir GetEnv("INSTALLER_OUTPUT_DIR")
#define InstallerBaseName GetEnv("INSTALLER_BASE_NAME")
#define RepoRoot SourcePath + "\.."

[Setup]
AppId={{8F93A0B5-9E75-4B49-A6AB-8C73C55A06B2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\StarRupture Planner
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename={#InstallerBaseName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
SetupIconFile={#RepoRoot}\src\StarRupturePlanner\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PackageRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
