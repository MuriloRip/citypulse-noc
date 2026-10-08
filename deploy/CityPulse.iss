#define PublishDir GetEnv("CITYPULSE_PUBLISH_DIR")
#define ArtifactDir GetEnv("CITYPULSE_ARTIFACT_DIR")
#define AppVersion GetEnv("CITYPULSE_VERSION")

[Setup]
AppId={{A7191F20-1BBE-4BC2-A1FE-5FC8CB1E68D7}
AppName=CityPulse NOC
AppVersion={#AppVersion}
AppPublisher=CityPulse
DefaultDirName={autopf}\CityPulse
DefaultGroupName=CityPulse
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
OutputDir={#ArtifactDir}
OutputBaseFilename=CityPulse-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\CityPulse.Api.exe

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Run-CityPulse-Windows.cmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\CityPulse NOC"; Filename: "{app}\Run-CityPulse-Windows.cmd"; WorkingDir: "{app}"
Name: "{autodesktop}\CityPulse NOC"; Filename: "{app}\Run-CityPulse-Windows.cmd"; WorkingDir: "{app}"

[Run]
Filename: "{app}\Run-CityPulse-Windows.cmd"; Description: "Iniciar CityPulse NOC"; Flags: postinstall nowait skipifsilent
