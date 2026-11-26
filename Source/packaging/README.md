# Build the macOS BioGTK DMGs

On macOS, install the .NET 10 SDK, Xcode Command Line Tools, Python 3.9 or newer, GTK 3, and OpenSlide for your target architecture. Use the `BioGTKApp.csproj` executable project from the separate BioGTKApp checkout. That project's references should point to the BioGTK and BioLib source checkouts you want to publish.

For a native build with Homebrew, run these commands from the BioGTK repository root.

```sh
brew install gtk+3 openslide
python3 Source/packaging/build-macos-dmg.py \
  --app-project /path/to/BioGTKApp.csproj
```

If the checkouts follow the layout referenced by `BioGTK.sln`, the script finds `BioGTKApp.csproj` automatically. It defaults to the host architecture and writes `BioGTK-macos-arm64.dmg` or `BioGTK-macos-x64.dmg` in the current directory. Use `--output` to choose another location. To build the Intel DMG on Apple Silicon, install Rosetta and pass `--arch x64 --gtk-prefix /path/to/intel-gtk-prefix`.

Each DMG contains `BioGTK.app` and an Applications shortcut. The app includes GTK 3, the .NET runtime, and Fiji.NET with IKVM. BioGTK runs its ImageJ commands and macros through the bundled runtime, so macOS users can install BioGTK without installing Fiji. Scripts, functions, tools, plugins, and models for these builds are stored under `~/Library/Application Support/BioGTK`. The current ONNX Runtime and TorchSharp packages supply macOS native libraries for Apple Silicon, so their model execution features are unavailable in the Intel build.

The packager checks that the publish output contains the IKVM11 assemblies used by Bio-Formats and the native libraries for the target architecture. If your BioGTK or BioLib checkout uses packages from a development feed, set `RestoreAdditionalProjectSources` to that feed or local package directory before running the packager.

For a build with interactive ImageJ dialogs, use BioLib with macOS dialog support and rebuild IKVM's `libawt_lwawt` target with OpenJDK's `awt.m` included. Match the bundled IKVM version and the target architecture. Pass `--biolib-project` with the path to `BioLib.csproj` and `--ikvm-awt-library` with the path to the rebuilt `libawt_lwawt.dylib`. The packager enables the shared ImageJ dialog path when that library exports `JNI_OnLoad`. Apple Silicon uses the bundled Swing look and feel because that IKVM image omits Aqua.

## Build in GitHub Actions

[Build macOS DMG](../../.github/workflows/build-macos-dmg.yml) runs on pushes to `main` and can be started manually from the Actions tab. Both jobs run on Apple Silicon. The Intel job installs x64 GTK from conda-forge and runs its cache tools through Rosetta. The workflow uploads separate ad hoc-signed `BioGTK-macos-arm64.dmg` and `BioGTK-macos-x64.dmg` files to the run's Artifacts section.

## Sign a download for distribution

The basic command ad hoc signs the app for local testing. For a public download, pass an Apple Developer ID Application signing identity and a keychain profile configured with `xcrun notarytool store-credentials`.

```sh
python3 Source/packaging/build-macos-dmg.py \
  --app-project /path/to/BioGTKApp.csproj \
  --sign-identity 'Developer ID Application: Your Name (TEAMID)' \
  --notary-profile biogtk-notary
```

The script signs the app and DMG, waits for notarization, and staples the ticket before placing the image at the output path. Test each downloaded DMG on a Mac of the matching architecture without Homebrew or .NET. Drag the app into Applications, launch it from Finder, and open an image.
