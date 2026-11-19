$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$source = [IO.File]::ReadAllText((Join-Path $sourceRoot 'ImageView.cs'))
$start = $source.IndexOf('        private static Bitmap NormalizeOverviewBitmap(')
$end = $source.IndexOf('        #region Handlers', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Overview conversion methods were not found.' }

# Compile the production conversion methods with real AForge bitmaps, without GTK UI.
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('BioGTK-overview-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $testDir | Out-Null
$assetsPath = Join-Path $sourceRoot '..\obj\project.assets.json'
$assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
$aforgePackage = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'AForgeBio/*' } | Select-Object -First 1
if (!$aforgePackage) { throw 'Restore BioGTK dependencies before running this regression check.' }
$version = $aforgePackage.Split('/')[1]
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="AForgeBio" Version="$version" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $testDir 'OverviewRegression.csproj')

$checks = @'
using AForge;
using Bitmap = AForge.Bitmap;
static class OverviewRegression
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static Bitmap Raw16(ushort[] samples, bool littleEndian)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            bytes[i * 2] = (byte)(littleEndian ? samples[i] & 255 : samples[i] >> 8);
            bytes[i * 2 + 1] = (byte)(littleEndian ? samples[i] >> 8 : samples[i] & 255);
        }
        var bitmap = new Bitmap("", samples.Length, 1, PixelFormat.Format16bppGrayScale,
            bytes, new ZCT(), 0, null, true, true);
        bitmap.LittleEndian = littleEndian;
        return bitmap;
    }
    static void Main()
    {
        foreach (var format in new[] { PixelFormat.Format24bppRgb, PixelFormat.Format32bppArgb,
            PixelFormat.Format32bppPArgb, PixelFormat.Format32bppRgb })
        {
            byte[] bytes = format == PixelFormat.Format24bppRgb
                ? new byte[] { 20, 128, 255 } : new byte[] { 20, 128, 255, 255 };
            using var source = new Bitmap(1, 1, format, bytes, new ZCT(), "");
            byte[] original = (byte[])source.Bytes.Clone();
            foreach (int maximum in new[] { 4095, 65535 })
            {
                var result = NormalizeOverviewBitmap(source, 100, maximum);
                Check(ReferenceEquals(result, source), $"Already mapped {format} was converted again.");
                Check(result.Bytes.SequenceEqual(original), $"Already mapped {format} lost brightness.");
            }
        }
        foreach (bool littleEndian in new[] { true, false })
        {
            using var source = Raw16(new ushort[] { 0, 1000, 2000, 3000, 65535 }, littleEndian);
            byte[] original = (byte[])source.Bytes.Clone();
            using var result = NormalizeOverviewBitmap(source, 1000, 3000);
            byte[] expected = { 0, 0, 127, 255, 255 };
            for (int pixel = 0; pixel < expected.Length; pixel++)
            {
                int offset = pixel * 4;
                Check(result.Bytes[offset] == expected[pixel] &&
                    result.Bytes[offset + 1] == expected[pixel] &&
                    result.Bytes[offset + 2] == expected[pixel] && result.Bytes[offset + 3] == 255,
                    $"Incorrect 16-bit mapping at pixel {pixel}, littleEndian={littleEndian}.");
            }
            Check(source.Bytes.SequenceEqual(original), "Normalization mutated raw image pixels.");
        }
        using (var source = Raw16(new ushort[] { 0, 4095 }, true))
        using (var result = NormalizeOverviewBitmap(source, 0, 0))
            Check(result.Bytes[4] == 255, "Raw preview did not use a fallback display range.");
        using (var source = Raw16(new ushort[] { 0, 0 }, true))
        using (var result = NormalizeOverviewBitmap(source, 0, 0))
            Check(result.Bytes[0] == 0 && result.Bytes[3] == 255, "Empty preview mapping failed.");
        foreach (bool littleEndian in new[] { true, false })
        {
            ushort[] rgb = { 1000, 2000, 3000 };
            byte[] bytes = rgb.SelectMany(v => littleEndian
                ? new byte[] { (byte)(v & 255), (byte)(v >> 8) }
                : new byte[] { (byte)(v >> 8), (byte)(v & 255) }).ToArray();
            using var source = new Bitmap("", 1, 1, PixelFormat.Format48bppRgb,
                bytes, new ZCT(), 0, null, true, true);
            source.LittleEndian = littleEndian;
            using var result = NormalizeOverviewBitmap(source, 1000, 3000);
            Check(result.Bytes.SequenceEqual(new byte[] { 255, 127, 0, 255 }),
                $"Incorrect 48-bit RGB mapping, littleEndian={littleEndian}.");
        }
        Console.WriteLine($"PASS: {checks} overview threshold regression checks.");
    }
'@
[IO.File]::WriteAllText((Join-Path $testDir 'Program.cs'), $checks + "`n" + $source.Substring($start, $end - $start) + "`n}")

# An empty local source ensures this uses restored packages without network access.
dotnet restore (Join-Path $testDir 'OverviewRegression.csproj') --source $testDir --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Offline regression project restore failed.' }
dotnet run --project (Join-Path $testDir 'OverviewRegression.csproj') --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Overview threshold regression checks failed.' }
