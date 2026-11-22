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

# Exercise first launch without GTK, .NET or ImageJ on PATH or any preseeded preferences.
$env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
$log = Join-Path $env:RUNNER_TEMP 'biogtk.stdout'
$errors = Join-Path $env:RUNNER_TEMP 'biogtk.stderr'
$bio = Start-Process $shortcut.TargetPath -WorkingDirectory $shortcut.WorkingDirectory -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $errors
try {
    $ready = $false
    for ($second = 0; $second -lt 120; $second++) {
        if ($bio.HasExited) { break }
        if (Select-String -LiteralPath $log -Pattern 'NodeView initialized.' -SimpleMatch -Quiet) {
            $ready = $true
            break
        }
        Start-Sleep -Seconds 1
    }
    if (!$ready) {
        Get-Content -LiteralPath $log,$errors
        throw 'BioGTK did not reach its main window.'
    }
    $modules = (Get-Process -Id $bio.Id).Modules
    foreach ($dll in 'coreclr.dll','libgtk-3-0.dll') {
        if ((Join-Path $bin $dll) -notin $modules.FileName) { throw "$dll was not loaded from the installed app." }
    }
} finally {
    if (!$bio.HasExited) { $bio.Kill(); $bio.WaitForExit() }
}

$uninstall = Start-Process (Join-Path $install 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
if ($uninstall.ExitCode -ne 0 -or (Test-Path $shortcut.TargetPath) -or (Test-Path $link)) {
    throw 'Uninstall did not remove the program and Start menu shortcut.'
}
