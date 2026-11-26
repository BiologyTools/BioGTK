"""Check that a mounted BioGTK bundle opens stack and pyramidal test images."""

import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

import numpy as np
import tifffile


def create_images(folder):
    stack = np.stack([np.full((96, 128), value, dtype=np.uint16) for value in (1000, 2000)])
    metadata = {"axes": "ZYX", "PhysicalSizeX": 0.5, "PhysicalSizeY": 0.5}
    tifffile.imwrite(folder / "Two planes.ome.tif", stack, ome=True, photometric="minisblack", metadata=metadata)
    tifffile.imwrite(folder / "ImageJ stack.tif", stack, imagej=True, photometric="minisblack", metadata={"axes": "ZYX"})
    rgb = np.zeros((512, 512, 3), dtype=np.uint8)
    rgb[:, :, 0] = 90
    rgb[192:320, 192:320, 1] = 180
    aperio = "Aperio Image Library v12.0.0\n"
    layout = "512x512 [0,0 512x512] (128x128) RAW/RGB|AppMag = 20|MPP = 0.5"
    with tifffile.TiffWriter(folder / "Åperio slide.svs") as writer:
        writer.write(rgb, tile=(128, 128), photometric="rgb", description=aperio + layout, metadata=None)
        writer.write(rgb[::8, ::8], photometric="rgb", description=aperio + "thumbnail", metadata=None)
        writer.write(rgb[::2, ::2], tile=(128, 128), photometric="rgb", subfiletype=1, metadata=None)
    return (("Two planes.ome.tif", "stack"), ("ImageJ stack.tif", "stack"), ("Åperio slide.svs", "pyramid"))


def verify(app):
    executable = app / "Contents/MacOS/BioGTKApp"
    with tempfile.TemporaryDirectory(prefix="biogtk-open-images-") as temp:
        work = Path(temp)
        checks = create_images(work)
        probe = work / "probe"
        probe.mkdir()
        shutil.copyfile(Path(__file__).with_name("StartupHook.cs"), probe / "StartupHook.cs")
        project = probe / "OpenProbe.csproj"
        project.write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<Nullable>enable</Nullable></PropertyGroup></Project>',
            encoding="utf-8",
        )
        binaries = work / "binaries"
        subprocess.run(["dotnet", "build", project, "-c", "Release", "-o", binaries], check=True)
        settings = Path.home() / "Library/Application Support/BioGTK/Settings.txt"
        settings.parent.mkdir(parents=True, exist_ok=True)
        settings.write_text("ImageJPath=\n")
        failures = []
        for filename, expected in checks:
            print(f"Testing {filename}", flush=True)
            env = {**os.environ, "DOTNET_STARTUP_HOOKS": str(binaries / "OpenProbe.dll"),
                   "BIOGTK_OPEN_PROBE_PATH": str(work / filename), "BIOGTK_OPEN_PROBE_EXPECTED": expected}
            try:
                subprocess.run([executable], check=True, timeout=95, env=env)
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                print(f"Could not open {filename}: {error}", flush=True)
                failures.append(filename)
        if failures:
            raise RuntimeError(f"Failed image-opening checks: {', '.join(failures)}")


if __name__ == "__main__":
    verify(Path(sys.argv[1]).resolve())
