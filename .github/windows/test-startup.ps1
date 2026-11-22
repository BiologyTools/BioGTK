param(
    [Parameter(Mandatory)][string]$Bin,
    [string]$PortableLauncher
)

$ErrorActionPreference = 'Stop'
$settings = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BioGTK\Settings.txt'
if (Test-Path -LiteralPath $settings) { throw 'First launch requires a fresh Windows profile.' }
$env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
$app = Join-Path $Bin 'BioGTKApp.exe'
$log = Join-Path $env:RUNNER_TEMP 'biogtk.stdout'
$errors = Join-Path $env:RUNNER_TEMP 'biogtk.stderr'
$startOptions = @{PassThru=$true; WindowStyle='Hidden'; RedirectStandardOutput=$log; RedirectStandardError=$errors}
if ($PortableLauncher) {
    $shell = Start-Process $env:ComSpec -ArgumentList ('/d /c ""{0}""' -f $PortableLauncher) @startOptions
    for ($second = 0; $second -lt 30; $second++) {
        $bio = Get-Process -Name BioGTKApp -ErrorAction SilentlyContinue | Where-Object Path -eq $app | Select-Object -First 1
        if ($bio) { break }
        if ($shell.HasExited -and $shell.ExitCode -ne 0) { break }
        Start-Sleep -Seconds 1
    }
    if (!$bio) { Get-Content -LiteralPath $errors; throw 'The portable launcher did not start BioGTK.' }
} else {
    $bio = Start-Process $app -WorkingDirectory $Bin @startOptions
}

try {
    $ready = $false
    for ($second = 0; $second -lt 120; $second++) {
        $bio.Refresh()
        if ($bio.HasExited) { break }
        if ($bio.MainWindowTitle -eq 'BioGTK - NodeView' -or (Select-String -LiteralPath $log -Pattern 'NodeView initialized.' -SimpleMatch -Quiet)) {
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
        if ((Join-Path $Bin $dll) -notin $modules.FileName) { throw "$dll was not loaded from the package." }
    }
} finally {
    if (!$bio.HasExited) { $bio.Kill(); $bio.WaitForExit() }
}
