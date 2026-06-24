[Setup]
AppName=XIVTheCalamity
AppVersion={#AppVersion}
DefaultDirName={pf}\XIVTheCalamity
DefaultGroupName=XIVTheCalamity
UninstallDisplayIcon={app}\XIVTheCalamity.exe
Compression=lzma2
SolidCompression=yes
OutputDir=Release
OutputBaseFilename=XIVTheCalamity-{#AppVersion}-win-x64-installer
SetupIconFile=frontend\build\icon.ico
DisableProgramGroupPage=yes
DisableWelcomePage=no

[Files]
Source: "Release\win-out\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "shared\resources\*"; DestDir: "{app}\resources"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\XIVTheCalamity"; Filename: "{app}\XIVTheCalamity.exe"
Name: "{commondesktop}\XIVTheCalamity"; Filename: "{app}\XIVTheCalamity.exe"

[Run]
Filename: "{app}\XIVTheCalamity.exe"; Description: "Launch XIVTheCalamity"; Flags: nowait postinstall skipifsilent
