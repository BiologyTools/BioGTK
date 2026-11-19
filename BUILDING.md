# Building BioGTK

- Clone the BioGTK repository then create a new .NET10 project named "BioGTKApp"
	- Add the following code to Program.cs
 - Then install NuGet package https://github.com/egramtel/dotnet-bundle ```<PackageReference Include="Dotnet.Bundle" Version="*" /> ``` and
```<PackageReference Include="Packaging.Targets" Version="*" />```
 - Then add this to the project file.
```
<PropertyGroup>
 <ErrorOnDuplicatePublishOutputFiles>false</ErrorOnDuplicatePublishOutputFiles>
</PropertyGroup>
```

```
using Gtk;
using BioGTK;
using Application = Gtk.Application;

namespace BioGTKApp
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Initializing GTK.");
            Application.Init();
            Console.WriteLine("Creating NodeView.");
            BioGTK.NodeView node = BioGTK.NodeView.Create(args);
            Console.WriteLine("Show NodeView.");
            node.Show();
            Console.WriteLine("Application.Run()");
            Application.Run();
            Console.WriteLine("Application Started.");
        }
    }
}
```
-   Then run the following dotnet build script based on your platform and CPU.
	- dotnet msbuild -t:BundleApp -p:RuntimeIdentifier=osx-x64 -p:Configuration=Release
	- dotnet msbuild -t:BundleApp -p:RuntimeIdentifier=osx-arm64 -p:Configuration=Release
	- dotnet msbuild BioGTKApp.csproj /t:CreateTarball /p:RuntimeIdentifier=linux-arm64 /p:Configuration=Release
	- dotnet msbuild BioGTKApp.csproj /t:CreateTarball /p:RuntimeIdentifier=linux-x64 /p:Configuration=Release
	- dotnet publish -c Release -r win-x64 --self-contained false /p:Platform="Any CPU" -o ./publish 	

## Setting up segmentation models

### Segment Anything 2

Download the `encoder-quant.onnx` and `decoder-quant.onnx` pair from the [SAM2 ONNX release](https://github.com/ibaiGorordo/ONNX-SAM2-Segment-Anything/releases/tag/0.2.0) and place the files alongside `BioGTK.dll`. Open an image and choose **Run SAM**.

### Micro-SAM2

BioGTK uses the SAM2.1-based microscopy checkpoints from [micro-sam 2](https://github.com/computational-cell-analytics/micro-sam/releases/tag/v2.0.1). Export the checkpoint once in a Python environment with `micro_sam` 2, PyTorch, ONNX, and ONNX Runtime installed. Python is only needed for export.

```sh
conda create -n biogtk-microsam2 -c conda-forge "micro_sam>=2,<3" python=3.12 onnx onnxruntime
conda activate biogtk-microsam2
python Source/ML/export_micro_sam2.py ./microsam2-models
```

For pip-based setup in an activated Python 3.12 environment, install with `python -m pip install micro-sam==2.0.1 onnx onnxruntime` and run the same export command.

The exporter downloads the upstream `hvit_t_cells` microscopy model by default and checks the exported encoder and decoder against PyTorch. Use `--model` to select another supported Micro-SAM2 checkpoint. Keep both files from the same export.

Copy `micro-sam2-encoder.onnx` and `micro-sam2-decoder.onnx` to the application directory alongside `BioGTK.dll`. For the packaged macOS application, put them directly in `~/Library/Application Support/BioGTK`. The older `micro-sam-encoder.onnx` and `micro-sam-decoder.onnx` models must be replaced with the new pair.

Open a microscopy image and choose **Run Micro-SAM2**. Use the point or rectangle tool to prompt a mask on the current plane, or use automatic segmentation to sample a grid of points across the stack. **Run SAM Encode** refreshes the currently selected plane after editing it.
