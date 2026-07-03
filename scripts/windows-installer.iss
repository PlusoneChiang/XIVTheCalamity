[Setup]
AppName=XIVTheCalamity
AppVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\XIVTheCalamity
DefaultGroupName=XIVTheCalamity
UninstallDisplayIcon={app}\XIVTheCalamity.exe
Compression=lzma2
SolidCompression=yes
OutputDir=..\Release
OutputBaseFilename=XIVTheCalamity-{#AppVersion}-win-x64-installer
SetupIconFile=..\frontend\build\icon.ico
DisableProgramGroupPage=yes
DisableWelcomePage=no

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\Release\win-out\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\shared\resources\*"; DestDir: "{app}\resources"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\XIVTheCalamity"; Filename: "{app}\XIVTheCalamity.exe"
Name: "{userdesktop}\XIVTheCalamity"; Filename: "{app}\XIVTheCalamity.exe"

[Run]
Filename: "{app}\XIVTheCalamity.exe"; Description: "Launch XIVTheCalamity"; Flags: nowait postinstall skipifsilent
