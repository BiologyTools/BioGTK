#!/usr/bin/env python3
"""Build the Apple Silicon or Intel BioGTK DMG on macOS."""

import argparse
import json
import os
from pathlib import Path
import platform
import plistlib
import re
import shutil
import subprocess
import tempfile


PACKAGING = Path(__file__).resolve().parent
REPO = PACKAGING.parent.parent
APP_PROJECT = REPO.parent.parent / "BioGTKApp/BioGTKApp/BioGTKApp/BioGTKApp.csproj"
APP_NAME = "BioGTKApp"
MACH_O_ARCHITECTURES = {"arm64": "arm64", "x64": "x86_64"}
GTK_LIBRARIES = (
    "libglib-2.0.0.dylib", "libgobject-2.0.0.dylib", "libgio-2.0.0.dylib",
    "libcairo.2.dylib", "libatk-1.0.0.dylib", "libpango-1.0.0.dylib",
    "libpangocairo-1.0.0.dylib", "libgdk_pixbuf-2.0.dylib",
    "libgdk-3.0.dylib", "libgtk-3.0.dylib",
)
MACH_O_MAGIC = {
    bytes.fromhex(magic) for magic in
    ("feedface", "cefaedfe", "feedfacf", "cffaedfe", "cafebabe", "bebafeca", "cafebabf", "bfbafeca")
}
DYLIB_LOAD_COMMANDS = {
    "LC_LOAD_DYLIB", "LC_LOAD_WEAK_DYLIB", "LC_REEXPORT_DYLIB",
    "LC_LOAD_UPWARD_DYLIB", "LC_LAZY_LOAD_DYLIB",
}


def run(*command, capture=False):
    return subprocess.run(
        command, check=True, text=True, stdout=subprocess.PIPE if capture else None
    ).stdout


def is_system_library(name):
    return name.startswith(("/usr/lib/", "/System/Library/"))


def load_commands(binary, architecture):
    output = run("otool", "-arch", architecture, "-l", binary, capture=True)
    for block in re.split(r"^Load command \d+$", output, flags=re.MULTILINE)[1:]:
        command = re.search(r"^\s*cmd (LC_\w+)$", block, re.MULTILINE)
        payload = re.search(r"^\s*(?:name|path) (.+) \(offset \d+\)$", block, re.MULTILINE)
        if command and payload:
            yield command[1], payload[1]


def require_architecture(binary, architecture):
    if architecture not in run("lipo", "-archs", binary, capture=True).split():
        raise ValueError(f"Missing {architecture} slice: {binary}")


def configure_imagej_dialogs(publish, args, architecture):
    toolkit = publish / f"ikvm/osx-{args.arch}/bin/libawt_lwawt.dylib"
    if args.ikvm_awt_library:
        require_architecture(args.ikvm_awt_library, architecture)
        if not toolkit.is_file():
            raise ValueError(f"Publish is missing {toolkit.relative_to(publish)}")
        shutil.copyfile(args.ikvm_awt_library, toolkit)

    symbols = run("nm", "-arch", architecture, "-gU", toolkit, capture=True) if toolkit.is_file() else ""
    has_initializer = any(line.endswith(" T _JNI_OnLoad") for line in symbols.splitlines())
    if args.ikvm_awt_library and not has_initializer:
        raise ValueError("The supplied IKVM AWT library is missing the macOS JNI_OnLoad initializer")
    if not has_initializer:
        return

    runtime_config = publish / f"{APP_NAME}.runtimeconfig.json"
    config = json.loads(runtime_config.read_text(encoding="utf-8"))
    properties = config["runtimeOptions"].setdefault("configProperties", {})
    properties["BioLib.EnableMacOSImageJDialogs"] = True
    runtime_config.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")


class GtkBundle:
    def __init__(self, prefix, contents, architecture):
        self.prefix = prefix
        self.architecture = architecture
        self.libraries = contents / "Frameworks/Gtk"
        self.resources = contents / "Resources/Gtk"
        self.libraries.mkdir(parents=True)
        self.resources.mkdir(parents=True)
        self.binaries = {}
        self.names = {}

    def copy_binary(self, source, name=None):
        original = source.resolve(strict=True)
        name = name or original.name
        for alias in (name, original.name):
            owner = self.names.setdefault(alias, original)
            if owner != original:
                raise ValueError(f"GTK library name collision for {alias}: {owner} and {original}")

        destination = self.libraries / original.name
        if original not in self.binaries:
            require_architecture(original, self.architecture)
            shutil.copy(original, destination)
            self.binaries[original] = destination
        alias_path = self.libraries / name
        if alias_path != destination and not alias_path.is_symlink():
            alias_path.symlink_to(destination.name)
        return name

    def resolve_dependency(self, binary, dependency, rpaths):
        if dependency.startswith("@rpath/"):
            relative = dependency.removeprefix("@rpath/")
            candidates = [
                Path(path.replace("@loader_path", str(binary.parent))) / relative for path in rpaths
            ]
            candidates.extend((binary.parent / relative, self.prefix / "lib" / relative))
        elif dependency.startswith("@loader_path/"):
            candidates = [binary.parent / dependency.removeprefix("@loader_path/")]
        else:
            candidates = [Path(dependency)]
        for candidate in candidates:
            if candidate.is_absolute() and candidate.is_file():
                return candidate
        raise ValueError(f"Could not resolve {dependency} required by {binary}")

    def relocate(self):
        pending = list(self.binaries)
        processed = set()
        while pending:
            original = pending.pop()
            if original in processed:
                continue
            processed.add(original)
            commands = list(load_commands(original, self.architecture))
            rpaths = list(dict.fromkeys(value for kind, value in commands if kind == "LC_RPATH"))
            changes = []
            for kind, value in commands:
                if kind == "LC_ID_DYLIB":
                    changes.extend(("-id", f"@rpath/{original.name}"))
                elif kind in DYLIB_LOAD_COMMANDS and not is_system_library(value):
                    dependency = self.resolve_dependency(original, value, rpaths)
                    name = self.copy_binary(dependency, Path(value).name)
                    changes.extend(("-change", value, f"@loader_path/{name}"))
                    resolved = dependency.resolve()
                    if resolved not in processed:
                        pending.append(resolved)
            for rpath in rpaths:
                changes.extend(("-delete_rpath", rpath))
            if changes:
                run("install_name_tool", *changes, self.binaries[original])

    def package_root(self, formula):
        keg = self.prefix / "opt" / formula
        return keg if keg.is_dir() else self.prefix

    def copy_module_cache(self, formula, pattern, query_name, cache_name):
        package = self.package_root(formula)
        modules = sorted(package.glob(pattern))
        modules = [module for module in modules if module.suffix in (".so", ".dylib")]
        cache = run("/usr/bin/arch", f"-{self.architecture}",
                    package / "bin" / query_name, *modules, capture=True) if modules else ""
        cache = "\n".join(line for line in cache.splitlines() if not line.startswith("#")) + "\n"
        for module in modules:
            alias = self.copy_binary(module, module.name)
            for path in (module, module.resolve(), module.relative_to(package)):
                quoted = json.dumps(str(path), ensure_ascii=False)
                if quoted in cache:
                    cache = cache.replace(quoted, f'"@GTK_LIB_DIR@/{alias}"')
                    break
            else:
                raise ValueError(f"{query_name} did not register {module}. Cache output: {cache!r}")
        (self.resources / cache_name).write_text(cache, encoding="utf-8")

    def stage(self):
        for name in GTK_LIBRARIES:
            source = self.prefix / "lib" / name
            if name == "libgdk_pixbuf-2.0.dylib" and not source.is_file():
                source = source.with_name("libgdk_pixbuf-2.0.0.dylib")
            self.copy_binary(source, name)

        self.copy_module_cache("gdk-pixbuf", "lib/gdk-pixbuf-2.0/*/loaders/*",
                               "gdk-pixbuf-query-loaders", "loaders.cache")
        self.copy_module_cache("gtk+3", "lib/gtk-3.0/*/immodules/*",
                               "gtk-query-immodules-3.0", "immodules.cache")
        self.relocate()

        share = self.resources / "share"
        schemas = share / "glib-2.0/schemas"
        schema_packages = {self.package_root(formula) for formula in ("gtk+3", "gsettings-desktop-schemas")}
        for package in sorted(schema_packages):
            source = package / "share/glib-2.0/schemas"
            if source.is_dir():
                shutil.copytree(source, schemas, dirs_exist_ok=True,
                                ignore=shutil.ignore_patterns("gschemas.compiled"))
        if not any(schemas.glob("*.gschema.xml")):
            raise ValueError(f"No GTK settings schemas found under {self.prefix}")
        run(self.package_root("glib") / "bin/glib-compile-schemas", "--strict", schemas)
        for relative in ("icons/hicolor", "icons/Adwaita", "mime"):
            source = self.prefix / "share" / relative
            if source.is_dir():
                shutil.copytree(source, share / relative)
        (self.resources / "gio-modules").mkdir()
        self.copy_notices()

    def copy_notices(self):
        # Carry upstream notices from each Homebrew keg whose binaries we ship.
        cellar = (self.prefix / "Cellar").resolve()
        kegs = {
            cellar.joinpath(*binary.relative_to(cellar).parts[:2])
            for binary in self.binaries if binary.is_relative_to(cellar)
        }
        for formula in ("gsettings-desktop-schemas", "hicolor-icon-theme", "adwaita-icon-theme", "shared-mime-info"):
            keg = (self.prefix / "opt" / formula).resolve()
            if keg.is_dir() and keg.is_relative_to(cellar):
                kegs.add(keg)
        for keg in sorted(kegs):
            for notice in keg.iterdir():
                if notice.is_file() and notice.name.upper().startswith(("COPYING", "LICENSE", "NOTICE")):
                    target = self.resources / "Notices" / keg.parent.name / keg.name / notice.name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(notice, target)

        for record in sorted((self.prefix / "conda-meta").glob("*.json")):
            package = json.loads(record.read_text(encoding="utf-8"))
            cache = package.get("extracted_package_dir") or (package.get("link") or {}).get("source")
            if cache:
                notices = Path(cache) / "info/licenses"
                if notices.is_dir():
                    shutil.copytree(notices, self.resources / "Notices" / record.stem)


def stage_dotnet(publish, contents, architecture):
    macos = contents / "MacOS"
    resources = contents / "Resources/Dotnet"
    native = []
    for source in sorted(publish.rglob("*")):
        if not source.is_file():
            continue
        relative = source.relative_to(publish)
        entry = macos / relative
        resource = resources / relative
        entry.parent.mkdir(parents=True, exist_ok=True)
        resource.parent.mkdir(parents=True, exist_ok=True)
        with source.open("rb") as stream:
            is_native = stream.read(4) in MACH_O_MAGIC and source.suffix != ".class"
        if is_native:
            require_architecture(source, architecture)
            shutil.copy(source, entry)
            native.append(entry)
            # The apphost resolves BioGTKApp.dll before locating hostfxr, so
            # the resolved managed directory also needs the native runtime.
            if relative != Path(APP_NAME):
                resource.symlink_to(os.path.relpath(entry, resource.parent))
        else:
            shutil.copy(source, resource)
            # BioGTK also opens assets relative to Environment.ProcessPath.
            entry.symlink_to(os.path.relpath(resource, entry.parent))

    executable = macos / APP_NAME
    if executable not in native:
        raise ValueError(f"Missing {architecture} apphost: {executable}")
    executable.chmod(0o755)
    # GtkSharp calls dlopen with a bare dylib name. dyld searches this runpath.
    run("install_name_tool", "-add_rpath", "@executable_path/../Frameworks/Gtk", executable)
    for binary in native:
        relocate_dotnet_binary(binary, architecture)
    return executable, native


def relocate_dotnet_binary(binary, architecture):
    commands = list(load_commands(binary, architecture))
    rpaths = {value for kind, value in commands if kind == "LC_RPATH"}
    build_rpaths = {path for path in rpaths if path.startswith("/") and not is_system_library(path)}
    changes = []
    for kind, value in commands:
        if not value.startswith("/") or is_system_library(value):
            continue
        if kind == "LC_ID_DYLIB":
            changes.extend(("-id", f"@rpath/{binary.name}"))
        elif kind in DYLIB_LOAD_COMMANDS:
            # LibTorch references Homebrew's libomp by absolute install name
            # even though its NuGet package supplies a copy beside libtorch.
            name = Path(value).name
            if not binary.with_name(name).is_file():
                raise ValueError(f"Missing bundled dependency {value} required by {binary}")
            changes.extend(("-change", value, f"@loader_path/{name}"))

    if build_rpaths:
        for path in sorted(build_rpaths):
            changes.extend(("-delete_rpath", path))
        if not rpaths.intersection(("@loader_path", "@loader_path/.")):
            changes.extend(("-add_rpath", "@loader_path"))
    if changes:
        run("install_name_tool", *changes, binary)


def sign_app(app, executable, native, identity, work):
    entitlement_file = work / "entitlements.plist"
    entitlement_file.write_bytes(plistlib.dumps({"com.apple.security.cs.allow-jit": True}))
    common = ["--force", "--sign", identity or "-"]
    if identity:
        common += ["--timestamp", "--options", "runtime"]
    else:
        common += ["--timestamp=none"]
    for binary in native:
        if binary != executable:
            run("codesign", "--remove-signature", binary)
            run("codesign", *common, binary)
    run("codesign", *common, "--entitlements", entitlement_file, app)
    run("codesign", "--verify", "--deep", "--strict", app)


def build(args, gtk_prefix):
    architecture = MACH_O_ARCHITECTURES[args.arch]
    rid = f"osx-{args.arch}"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".biogtk-dmg-", dir=args.output.parent) as temp:
        work = Path(temp)
        publish = work / "publish"
        source_options = [f"-p:BioGTKMacBioLibProject={args.biolib_project}"] if args.biolib_project else []
        run("dotnet", "publish", args.app_project, "-c", "Release", "-r", rid,
            "--self-contained", "true", "--artifacts-path", work / "artifacts", "-o", publish,
            f"-p:RuntimeIdentifiers={rid}", "-p:PublishSingleFile=false", "-p:PublishTrimmed=false",
            "-p:GeneratePackageOnBuild=false",
            f"-p:CustomAfterMicrosoftCommonTargets={PACKAGING / 'BioGTK.MacDmg.targets'}", *source_options)
        for required in ("libcoreclr.dylib", "libhostfxr.dylib", "Glade/NodeView.glade",
                         "macro-functions.txt", "macro-commands.csv"):
            if not (publish / required).is_file():
                raise ValueError(f"Publish is missing {required}")
        configure_imagej_dialogs(publish, args, architecture)

        image = work / "image"
        app = image / "BioGTK.app"
        contents = app / "Contents"
        executable, native = stage_dotnet(publish, contents, architecture)
        gtk = GtkBundle(gtk_prefix, contents, architecture)
        gtk.stage()
        native += gtk.binaries.values()
        for binary in native:
            for kind, value in load_commands(binary, architecture):
                if kind in DYLIB_LOAD_COMMANDS | {"LC_RPATH", "LC_ID_DYLIB"}:
                    if value.startswith("/") and not is_system_library(value):
                        raise ValueError(f"External path in {binary}: {value}")

        deps = json.loads((publish / f"{APP_NAME}.deps.json").read_text(encoding="utf-8"))
        version = next((key.split("/", 1)[1] for key in deps["libraries"]
                        if key.startswith(APP_NAME + "/")), "")
        display_version = re.match(r"\d+(?:\.\d+){0,2}", version)
        if not display_version:
            raise ValueError(f"Invalid BioGTKApp version: {version}")
        (contents / "Info.plist").write_bytes(plistlib.dumps({
            "CFBundleExecutable": APP_NAME,
            "CFBundleIdentifier": "com.BioGTK",
            "CFBundleIconFile": "bio.icns",
            "CFBundleName": "BioGTK",
            "CFBundleDisplayName": "BioGTK",
            "CFBundlePackageType": "APPL",
            "CFBundleShortVersionString": display_version[0],
            "CFBundleVersion": display_version[0],
            "NSHighResolutionCapable": True,
        }))
        shutil.copyfile(REPO / "bio.icns", contents / "Resources/bio.icns")
        shutil.copyfile(REPO / "LICENSE", contents / "Resources/BioGTK-LICENSE.txt")
        (image / "Applications").symlink_to("/Applications")

        sign_app(app, executable, native, args.sign_identity, work)
        dmg = work / args.output.name
        run("hdiutil", "create", "-volname", "BioGTK", "-srcfolder", image, "-format", "UDZO", dmg)
        if args.sign_identity:
            run("codesign", "--sign", args.sign_identity, "--timestamp",
                "--identifier", "com.BioGTK.diskimage", dmg)
        if args.notary_profile:
            run("xcrun", "notarytool", "submit", dmg, "--keychain-profile", args.notary_profile, "--wait")
            run("xcrun", "stapler", "staple", dmg)
            run("xcrun", "stapler", "validate", dmg)
        run("hdiutil", "verify", dmg)
        dmg.replace(args.output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app-project", type=Path, default=APP_PROJECT, metavar="BIOGTKAPP_CSPROJ",
                        help="path to the BioGTKApp executable project (defaults to the sibling checkout)")
    parser.add_argument("--biolib-project", type=Path, metavar="BIOLIB_CSPROJ",
                        help="build BioLib from this project for the DMG")
    parser.add_argument("--ikvm-awt-library", type=Path, metavar="LIBAWT_LWAWT_DYLIB",
                        help="bundle a corrected IKVM AWT library for the target architecture")
    parser.add_argument("--arch", choices=MACH_O_ARCHITECTURES, help="target architecture (defaults to the host)")
    parser.add_argument("--gtk-prefix", type=Path, help="GTK 3 install prefix for the target architecture")
    parser.add_argument("--output", type=Path, help="DMG path (defaults to BioGTK-macos-<arch>.dmg)")
    parser.add_argument("--sign-identity", help="Developer ID Application signing identity")
    parser.add_argument("--notary-profile", help="keychain profile set up with notarytool store-credentials")
    args = parser.parse_args()
    if args.notary_profile and not args.sign_identity:
        parser.error("--notary-profile requires --sign-identity")
    host = platform.machine()
    if platform.system() != "Darwin" or host not in MACH_O_ARCHITECTURES.values():
        parser.error("run this script on macOS")

    args.arch = args.arch or ("x64" if host == "x86_64" else "arm64")
    args.app_project = args.app_project.resolve()
    args.output = (args.output or Path(f"BioGTK-macos-{args.arch}.dmg")).resolve()
    if args.app_project.name != "BioGTKApp.csproj" or not args.app_project.is_file():
        parser.error(f"BioGTKApp.csproj was not found at {args.app_project}. Supply --app-project.")
    for option in ("biolib_project", "ikvm_awt_library"):
        path = getattr(args, option)
        if path is not None:
            if not path.is_file():
                parser.error(f"File not found: {path}")
            setattr(args, option, path.resolve())
    if args.output.suffix != ".dmg":
        parser.error("--output must end in .dmg")
    try:
        if args.gtk_prefix:
            prefix = args.gtk_prefix.resolve()
        else:
            brew = shutil.which("brew")
            if not brew:
                brew = "/opt/homebrew/bin/brew" if host == "arm64" else "/usr/local/bin/brew"
            if not Path(brew).is_file():
                parser.error("install GTK 3 with Homebrew or pass --gtk-prefix")
            prefix = Path(run(brew, "--prefix", capture=True).strip())
        gtk = prefix / "lib/libgtk-3.0.dylib"
        if not gtk.is_file():
            parser.error(f"GTK 3 was not found under {prefix}. Install it or pass --gtk-prefix.")
        require_architecture(gtk, MACH_O_ARCHITECTURES[args.arch])
        build(args, prefix)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Could not build the BioGTK DMG: {error}\n")

    print(f"Created {args.output}")
    if not args.notary_profile:
        print("This build is for local testing. Use --sign-identity and --notary-profile for distribution.")


if __name__ == "__main__":
    main()
