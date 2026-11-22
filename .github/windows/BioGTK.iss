[Setup]
AppId={{5FD317C6-972D-4FFA-B534-7DC4E513E2AD}
AppName=BioGTK
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=BiologyTools
AppPublisherURL=https://github.com/BiologyTools/BioGTK
DefaultDirName={localappdata}\Programs\BioGTK
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\bin\BioGTKApp.exe
SetupIconFile={#BioGTKRoot}\Bio.ico
OutputBaseFilename=BioGTK-win-x64-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}\bin"; Excludes: "*.pdb"; Flags: recursesubdirs ignoreversion
Source: "{#BioGTKRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#CrtDir}\*.dll"; DestDir: "{app}\bin"; Flags: ignoreversion
; Keep the GTK prefix layout so GLib can locate schemas, icon themes and loaders after installation.
Source: "{#GtkDir}\bin\*.dll"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#GtkDir}\bin\gdk-pixbuf-query-loaders.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#GtkDir}\lib\gdk-pixbuf-2.0\*"; DestDir: "{app}\lib\gdk-pixbuf-2.0"; Excludes: "*.a"; Flags: recursesubdirs ignoreversion
Source: "{#GtkDir}\lib\gtk-3.0\*"; DestDir: "{app}\lib\gtk-3.0"; Excludes: "*.a"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#GtkDir}\lib\gio\modules\*"; DestDir: "{app}\lib\gio\modules"; Excludes: "*.a"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#GtkDir}\etc\fonts\*"; DestDir: "{app}\etc\fonts"; Flags: recursesubdirs ignoreversion
Source: "{#GtkDir}\share\glib-2.0\schemas\*"; DestDir: "{app}\share\glib-2.0\schemas"; Flags: recursesubdirs ignoreversion
Source: "{#GtkDir}\share\icons\*"; DestDir: "{app}\share\icons"; Flags: recursesubdirs ignoreversion
Source: "{#GtkDir}\share\themes\*"; DestDir: "{app}\share\themes"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#GtkDir}\share\fontconfig\*"; DestDir: "{app}\share\fontconfig"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#GtkDir}\share\locale\*"; DestDir: "{app}\share\locale"; Flags: recursesubdirs ignoreversion
Source: "{#GtkDir}\share\mime\*"; DestDir: "{app}\share\mime"; Flags: recursesubdirs ignoreversion
Source: "{#GtkDir}\share\licenses\*"; DestDir: "{app}\share\licenses"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\BioGTK"; Filename: "{app}\bin\BioGTKApp.exe"; WorkingDir: "{app}\bin"

[Run]
Filename: "{app}\bin\gdk-pixbuf-query-loaders.exe"; Parameters: "--update-cache"; Flags: runhidden
Filename: "{app}\bin\BioGTKApp.exe"; Description: "Launch BioGTK"; WorkingDir: "{app}\bin"; Flags: postinstall nowait skipifsilent
