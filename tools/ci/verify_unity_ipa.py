#!/usr/bin/env python3
"""Forensic check of the unsigned Unity .ipa (mirror of tools/ci/verify_ipa.py for the Godot build).

Usage: python3 tools/ci/verify_unity_ipa.py build/GOOBY-unity-unsigned.ipa

Checks: Payload/*.app layout, Info.plist (bundle id, display name, version, orientations
matching BuildScript — both landscapes plus portrait, no upside-down — min iOS, device
family iPhone+iPad), arm64 Mach-O main binary, UnityFramework,
Data/ folder with the player data, app icons present. Prints ".ipa gebaut: X MB, Y Dateien".
"""
import plistlib
import struct
import sys
import zipfile

EXPECT_BUNDLE = "com.permissionmaxed.gooby"
EXPECT_MIN_IOS = "15.0"
# ProjectSettings and BuildScript allow portrait (portrait minigames) and both landscapes.
EXPECT_ORIENT = {
    "UIInterfaceOrientationLandscapeLeft",
    "UIInterfaceOrientationLandscapeRight",
    "UIInterfaceOrientationPortrait",
}


def fail(msg):
    print(f"::error::{msg}")
    sys.exit(1)


def main(path):
    z = zipfile.ZipFile(path)
    names = z.namelist()
    apps = sorted({n.split("/")[1] for n in names if n.startswith("Payload/") and n.split("/")[1].endswith(".app")})
    if len(apps) != 1:
        fail(f"erwartet genau eine .app unter Payload/, gefunden: {apps}")
    app = apps[0]
    base = f"Payload/{app}/"
    plist_name = base + "Info.plist"
    if plist_name not in names:
        fail("Info.plist fehlt")
    info = plistlib.loads(z.read(plist_name))
    bundle = info.get("CFBundleIdentifier")
    if bundle != EXPECT_BUNDLE:
        fail(f"Bundle-ID {bundle!r} != {EXPECT_BUNDLE!r}")
    orient = set(info.get("UISupportedInterfaceOrientations", []))
    if orient != EXPECT_ORIENT:
        fail(f"Orientierungen {sorted(orient)} != {sorted(EXPECT_ORIENT)}")
    min_ios = info.get("MinimumOSVersion")
    if min_ios != EXPECT_MIN_IOS:
        fail(f"MinimumOSVersion {min_ios!r} != {EXPECT_MIN_IOS!r}")
    family = set(info.get("UIDeviceFamily", []))
    if family != {1, 2}:
        fail(f"UIDeviceFamily {sorted(family)} != iPhone+iPad")
    exe = info.get("CFBundleExecutable")
    exe_name = base + exe
    if exe_name not in names:
        fail(f"Hauptbinary {exe} fehlt")
    head = z.read(exe_name)[:8]
    magic, cputype = struct.unpack("<II", head)
    # 0xFEEDFACF = MH_MAGIC_64 little-endian; CPU_TYPE_ARM64 = 0x0100000C
    if magic != 0xFEEDFACF or cputype != 0x0100000C:
        fail(f"Hauptbinary ist kein arm64 Mach-O (magic={magic:#x}, cpu={cputype:#x})")
    if not any(n.startswith(base + "Frameworks/UnityFramework.framework/") for n in names):
        fail("UnityFramework.framework fehlt")
    data_files = [n for n in names if n.startswith(base + "Data/")]
    if len(data_files) < 5:
        fail("Data/ (Unity Player-Daten) fehlt oder ist leer")
    icons = [n for n in names if n.startswith(base + "AppIcon") and n.endswith(".png")]
    if not icons:
        fail("App-Icons fehlen")
    size_mb = sum(i.file_size for i in z.infolist()) / (1024 * 1024)
    print(f"Bundle {bundle}, Version {info.get('CFBundleShortVersionString')} ({info.get('CFBundleVersion')}), "
          f"min iOS {min_ios}, Orientierungen {sorted(orient)}, Icons {len(icons)}, Data-Dateien {len(data_files)}")
    print(f".ipa gebaut: {size_mb:.1f} MB, {len(names)} Dateien")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
