param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$OutputZip
)

$ErrorActionPreference = 'Stop'
# Use the actual installed payload, including the private GTK prefix and Visual C++ runtime.
# Silent setup skips the optional application launch, so the ZIP never includes a user's preferences.
$staging = Join-Path $env:RUNNER_TEMP 'BioGTK portable payload'
if (Test-Path -LiteralPath $staging) { throw "Staging folder already exists: $staging" }
$argsForSetup = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="{0}"' -f $staging))
$setup = Start-Process $Installer -ArgumentList $argsForSetup -WindowStyle Hidden -Wait -PassThru
if ($setup.ExitCode -ne 0) { throw "Staging setup failed with code $($setup.ExitCode)." }

$cache = Join-Path $staging 'lib\gdk-pixbuf-2.0\2.10.0\loaders.cache'
$lines = foreach ($line in [IO.File]::ReadAllLines($cache)) {
    if ($line -match '^# LoaderDir =') { continue }
    if ($line -match '^"(.+\.dll)"$') {
        $name = [IO.Path]::GetFileName($Matches[1])
        $loader = "lib\gdk-pixbuf-2.0\2.10.0\loaders\$name"
        if (!(Test-Path -LiteralPath (Join-Path $staging $loader))) { throw "Missing GTK loader $name" }
        '"' + $loader.Replace('\','\\') + '"'
    } else {
        $line
    }
}
[IO.File]::WriteAllLines($cache, [string[]]$lines)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start BioGTK.cmd') -Destination $staging
Push-Location -LiteralPath $staging
try {
    & 7z a -tzip -mx=5 -bso0 '-x!unins000.*' $OutputZip '.\*'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create the portable ZIP.' }
} finally {
    Pop-Location
}
