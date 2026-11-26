"""Mount a BioGTK DMG and test the installed app on a native macOS runner."""

import json
import os
from pathlib import Path
import platform
import plistlib
import shutil
import subprocess
import sys
import tempfile
import time


def wait_for_startup(executable, log):
    with log.open("w", encoding="utf-8") as output:
        process = subprocess.Popen([executable], stdout=output, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 90
            while time.monotonic() < deadline:
                if "NodeView initialized." in log.read_text(errors="replace"):
                    print("The packaged app initialized its main window.", flush=True)
                    return
                if process.poll() is not None:
                    raise RuntimeError(f"BioGTK exited with status {process.returncode}.")
                time.sleep(1)
            raise TimeoutError("BioGTK did not initialize within 90 seconds.")
        except Exception:
            print(log.read_text(errors="replace"), file=sys.stderr, flush=True)
            raise
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


def verify_imagej(contents, architecture, rid, work):
    managed = contents / "MacOS"
    for name in ("IKVM.Java.dll", "IKVM.Runtime.dll", "IKVM.CoreLib.dll"):
        if not (managed / name).stat().st_size:
            raise ValueError(f"Empty managed runtime assembly {name}")

    toolkit = managed / "ikvm" / rid / "bin" / "libawt_lwawt.dylib"
    symbols = subprocess.check_output(["nm", "-arch", architecture, "-gU", toolkit], text=True)
    if not any(line.endswith(" T _JNI_OnLoad") for line in symbols.splitlines()):
        raise ValueError("The ImageJ AWT initializer is missing from the packaged runtime.")

    config = json.loads((managed / "BioGTKApp.runtimeconfig.json").read_text())
    if not config["runtimeOptions"]["configProperties"].get("BioLib.EnableMacOSImageJDialogs"):
        raise ValueError("The app did not enable the bundled ImageJ dialog runtime.")

    deps = json.loads((managed / "BioGTKApp.deps.json").read_text())
    target = deps["targets"][deps["runtimeTarget"]["name"]]
    for name in ("BioLib", "Fiji.NET", "IKVM11"):
        package = next((key for key in target if key.startswith(name + "/")), None)
        if package is None:
            raise ValueError(f"{name} is missing from the runtime dependency graph.")
        print(f"Bundled {package}", flush=True)

    project = work / "probe"
    output = work / "probe-build"
    project.mkdir()
    shutil.copyfile(Path(__file__).with_name("StartupHook.cs"), project / "StartupHook.cs")
    project_file = project / "DialogProbe.csproj"
    project_file.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
        '<Nullable>enable</Nullable></PropertyGroup></Project>',
        encoding="utf-8",
    )
    subprocess.run(["dotnet", "build", project_file, "-c", "Release", "-o", output], check=True)
    subprocess.run(
        [managed / "BioGTKApp"],
        check=True,
        timeout=120,
        env={**os.environ, "DOTNET_STARTUP_HOOKS": str(output / "DialogProbe.dll")},
    )


def verify(dmg, arch, version):
    architecture = {"arm64": "arm64", "x64": "x86_64"}[arch]
    if platform.machine() != architecture:
        raise ValueError(f"Expected a native {architecture} runner, got {platform.machine()}.")
    dialogs_required = tuple(int(part) for part in version.split(".")[:3]) >= (6, 8, 1)

    subprocess.run(["hdiutil", "verify", dmg], check=True)
    with tempfile.TemporaryDirectory(prefix="biogtk-verify-") as temp:
        work = Path(temp)
        mount = work / "mounted"
        mount.mkdir()
        subprocess.run(["hdiutil", "attach", dmg, "-mountpoint", mount, "-nobrowse", "-readonly"], check=True)
        try:
            app = mount / "BioGTK.app"
            contents = app / "Contents"
            executable = contents / "MacOS" / "BioGTKApp"
            subprocess.run(["codesign", "--verify", "--deep", "--strict", app], check=True)
            slices = subprocess.check_output(["lipo", "-archs", executable], text=True).split()
            if slices != [architecture]:
                raise ValueError(f"Unexpected BioGTK launcher architecture: {slices}")
            if (mount / "Applications").readlink() != Path("/Applications"):
                raise ValueError("The DMG is missing its Applications shortcut.")
            plist = plistlib.loads((contents / "Info.plist").read_bytes())
            for key in ("CFBundleShortVersionString", "CFBundleVersion"):
                if plist[key] != version:
                    raise ValueError(f"Unexpected {key}: {plist[key]}")
            if dialogs_required and not plist.get("CFBundleDocumentTypes"):
                raise ValueError("The app is missing its Finder document associations.")
            settings = Path.home() / "Library/Application Support/BioGTK/Settings.txt"
            settings.parent.mkdir(parents=True, exist_ok=True)
            settings.write_text("ImageJPath=\n" if dialogs_required else "ImageJPath=/usr/bin/true\n")
            wait_for_startup(executable, work / "startup.log")
            if dialogs_required:
                verify_imagej(contents, architecture, f"osx-{arch}", work)
                subprocess.run([sys.executable, Path(__file__).parent / "open-images/verify.py", app], check=True)
        finally:
            subprocess.run(["hdiutil", "detach", mount], check=True)


if __name__ == "__main__":
    verify(Path(sys.argv[1]).resolve(), sys.argv[2], sys.argv[3])
