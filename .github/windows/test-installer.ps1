param([Parameter(Mandatory)][string]$Installer)

$ErrorActionPreference = 'Stop'
$install = Join-Path $env:RUNNER_TEMP 'Installed BioGTK'
$bin = Join-Path $install 'bin'
$programs = [Environment]::GetFolderPath('Programs')
$link = Join-Path $programs 'BioGTK.lnk'
$settings = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BioGTK\Settings.txt'
if ((Test-Path $install) -or (Test-Path $link) -or (Test-Path $settings)) {
    throw 'Run this test in a fresh Windows user profile.'
}

$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="{0}"' -f $install))
$setup = Start-Process $Installer -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($setup.ExitCode -ne 0) { throw "Setup exited with code $($setup.ExitCode)." }
foreach ($file in 'bin\BioGTKApp.exe', 'bin\IKVM.Java.dll', 'bin\ikvm\win-x64\bin\jvm.dll',
                  'bin\Glade\NodeView.glade', 'share\glib-2.0\schemas\gschemas.compiled') {
    if (!(Test-Path -LiteralPath (Join-Path $install $file) -PathType Leaf)) { throw "Missing $file" }
}
if (!(Test-Path -LiteralPath $link)) { throw 'Missing Start menu shortcut.' }
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($link)
if ($shortcut.TargetPath -ne (Join-Path $bin 'BioGTKApp.exe')) { throw 'Incorrect shortcut target.' }

& "$PSScriptRoot/test-startup.ps1" -Bin $shortcut.WorkingDirectory

$uninstall = Start-Process (Join-Path $install 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
if ($uninstall.ExitCode -ne 0 -or (Test-Path $shortcut.TargetPath) -or (Test-Path $link)) {
    throw 'Uninstall did not remove the program and Start menu shortcut.'
}
