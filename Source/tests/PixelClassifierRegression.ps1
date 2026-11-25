$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$assets = Get-Content (Join-Path $sourceRoot '../obj/project.assets.json') -Raw | ConvertFrom-Json
$projectDir = Join-Path ([IO.Path]::GetTempPath()) ('BioGTK-pixels-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $projectDir | Out-Null
$project = Join-Path $projectDir 'PixelRegression.csproj'
$references = foreach ($name in @('AForgeBio', 'Microsoft.ML')) {
    $package = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like "$name/*" } | Select-Object -First 1
    if (!$package) { throw "Restore BioGTK before running this check. Missing $name." }
    '<PackageReference Include="{0}" Version="{1}" />' -f $name, $package.Split('/')[1]
}
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup>$($references -join [Environment]::NewLine)</ItemGroup>
</Project>
"@ | Set-Content $project
Copy-Item -LiteralPath (Join-Path $sourceRoot 'ML/PixelClassifier.cs') -Destination $projectDir
@'
using System.Buffers.Binary;
using AForge;
using BioGTK.ML;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}

const int w = 31, h = 12;
var labels = new[] { new PixelRegion(-5, 0, 9, h, false), new PixelRegion(20, 0, w + 2, h, true) };
var token = CancellationToken.None;
string path = Path.Combine(AppContext.BaseDirectory, "trained.zip");
using var bitmap = new Bitmap(w, h, PixelFormat.Format16bppGrayScale);
// Non-square, strided rows. Training pixels have a low dynamic range relative to 16-bit storage.
int stride = w * 2 + 2;
bitmap.Bytes = new byte[stride * h];
foreach (bool little in new[] { true, false })
{
    bitmap.LittleEndian = little;
    void SetSample(int x, int y, ushort value)
    {
        var sample = bitmap.Bytes.AsSpan(y * stride + 2 * x);
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(sample, value);
        else BinaryPrimitives.WriteUInt16BigEndian(sample, value);
    }
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            SetSample(x, y, (ushort)((x < 15 ? 1010 : 1040) + y % 4));
    var plane = new PixelPlane(bitmap);
    var model = PixelClassifier.Train(plane, labels, token);
    model.Save(path);
    // Move the foreground to a different row and column pattern and use intermediate intensities.
    var expected = new byte[w * h];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool foreground = (x + y) % 4 == 0;
            SetSample(x, y, (ushort)(foreground ? 1038 : 1014));
            expected[y * w + x] = foreground ? (byte)255 : (byte)0;
        }
    Check(model.Predict(plane, token).SequenceEqual(expected), "Wrong grayscale prediction.");
    Check(PixelClassifier.Load(path).Predict(plane, token).SequenceEqual(expected), "Saved prediction changed.");
}

using var rgb = new Bitmap(w, h, PixelFormat.Format24bppRgb);
int rgbStride = w * 3 + 3;
rgb.Bytes = new byte[h * rgbStride];
for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
        rgb.Bytes[y * rgbStride + 3 * x + (x < 15 ? 0 : 2)] = 255;
var colorPlane = new PixelPlane(rgb);
var color = PixelClassifier.Train(colorPlane, labels, token);
color.Save(path);
using var rgb48 = new Bitmap(2, 1, PixelFormat.Format48bppRgb);
rgb48.LittleEndian = false;
rgb48.Bytes = new byte[] { 255, 255, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255 };
Check(PixelClassifier.Load(path).Predict(new PixelPlane(rgb48), token).SequenceEqual(new byte[] { 255, 0 }),
    "Packed BGR and 48-bit RGB disagree.");

void Reject(PixelRegion[] regions, string issue)
{
    try { PixelClassifier.Train(colorPlane, regions, token); }
    catch (ArgumentException) { return; }
    throw new Exception(issue);
}
Reject(new[] { labels[0] }, "Accepted a missing class.");
Reject(new[] { labels[0], labels[0] with { Foreground = true } }, "Accepted conflicting labels.");
try
{
    color.Predict(colorPlane, new CancellationToken(true));
    throw new Exception("Prediction ignored cancellation.");
}
catch (OperationCanceledException) { }
Console.WriteLine("Passed pixel classifier training, persistence, prediction, input validation and cancellation checks.");
'@ | Set-Content (Join-Path $projectDir 'Program.cs')

dotnet restore $project --source $projectDir --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Offline regression project restore failed.' }
dotnet run --project $project --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Pixel classifier regression checks failed.' }
