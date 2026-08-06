#!/usr/bin/env python3
"""Assembles the Windows 7 / Embedded kiosk hardware-test package and zips it.

Cross-platform counterpart of Prepare-Win7HardwareTestPackage.ps1, which needs
Windows and MSBuild. The net40/x86 targets build fine with the .NET SDK on macOS
and Linux, so the whole package can be produced off-Windows.

Usage:
    dotnet build src/IzbanKiosk.LegacyHardwareBridge/IzbanKiosk.LegacyHardwareBridge.csproj -c Release -p:Platform=x86
    dotnet build src/IzbanKiosk.Terminal/IzbanKiosk.Terminal.csproj -c Release -p:Platform=x86
    python3 tools/Prepare-Win7HardwareTestPackage.py --vendor-source ~/Desktop/AUSKiosk

The package never contains the legacy EXE, database, logs, PDB files or
certificates: only the whitelisted x86 vendor DLLs, with a SHA-256 manifest that
the bridge verifies at startup.
"""
import argparse
import hashlib
import re
import json
import os
import shutil
import struct
import tempfile
from datetime import datetime, timezone

WHITELIST = [
    "EMVRdr35Lib.dll",
    "KioskPrint.dll",
    "QAsisIzmirimKartLibW.dll",
    "QAsisIzmirimKartLibWNet.dll",
    "QSmartCardLibW.dll",
    "QSmartCardLibWNet.dll",
    "CardLibW.dll",
    "CardLibWNet.dll",
    "QT5Core.dll",
    "libeay32.dll",
]

BRIDGE_RUNTIME = [
    "IzbanKiosk.LegacyHardwareBridge.exe",
    "IzbanKiosk.LegacyHardwareBridge.exe.config",
    "IzbanKiosk.LegacyHardware.Contracts.dll",
    "Newtonsoft.Json.dll",
]

PROTOTYPE_RUNTIME = [
    "IZBAN-Kiosk.exe",
    "IZBAN-Kiosk.exe.config",
    "IzbanKiosk.LegacyHardware.Contracts.dll",
    "Newtonsoft.Json.dll",
    "KioskHardware.config.json",
]

REPOSITORY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def pe_machine(path):
    with open(path, "rb") as handle:
        data = handle.read(4096)
    if data[:2] != b"MZ":
        raise SystemExit("Not a PE file: " + path)
    offset = struct.unpack_from("<I", data, 0x3C)[0]
    if data[offset:offset + 4] != b"PE\0\0":
        raise SystemExit("Invalid PE signature: " + path)
    return struct.unpack_from("<H", data, offset + 4)[0]


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def copy_runtime(source_dir, names, dest_dir):
    for name in names:
        src = os.path.join(source_dir, name)
        if not os.path.isfile(src):
            raise SystemExit(
                "Missing build output: " + src + "\nBuild Release/x86 first (see module docstring).")
        shutil.copy2(src, os.path.join(dest_dir, name))


def copy_operator_helpers(dest_dir):
    """Copies the .bat entry points and OKU-BENI.txt, normalised to CRLF."""
    helper_dir = os.path.join(REPOSITORY_ROOT, "tools", "win7-package")
    for name in sorted(os.listdir(helper_dir)):
        with open(os.path.join(helper_dir, name), "r", encoding="utf-8", newline="") as handle:
            text = handle.read().replace("\r\n", "\n").replace("\n", "\r\n")
        with open(os.path.join(dest_dir, name), "w", encoding="utf-8", newline="") as handle:
            handle.write(text)


def import_vendor_dlls(vendor_source, bridge_dir):
    files = []
    for name in WHITELIST:
        src = os.path.join(vendor_source, name)
        if not os.path.isfile(src):
            raise SystemExit("Required x86 vendor DLL missing from " + vendor_source + ": " + name)
        machine = pe_machine(src)
        if machine != 0x014C:
            raise SystemExit("Vendor file is not PE32/x86 (machine=0x%04X): %s" % (machine, name))
        dest = os.path.join(bridge_dir, name)
        shutil.copy2(src, dest)
        files.append({"filename": name, "sha256": sha256(dest), "size": os.path.getsize(dest)})

    manifest = {
        "generatedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "sourceFolder": os.path.basename(vendor_source.rstrip("/\\")),
        "files": files,
    }
    write_json(os.path.join(bridge_dir, "vendor-manifest.local.json"), manifest)


def read_release_version(terminal_build):
    """Reads the version the terminal executable actually carries.

    The release tag and the assembly version have to agree: the kiosk compares the
    tag against its own version, so a tag that runs ahead of the build makes every
    kiosk download a package that does not move it forward. Reading it from the
    compiled output removes the chance of copying the wrong number by hand.
    """
    csproj = os.path.join(REPOSITORY_ROOT, "src/IzbanKiosk.Terminal/IzbanKiosk.Terminal.csproj")
    with open(csproj, encoding="utf-8") as handle:
        match = re.search(r"<Version>([^<]+)</Version>", handle.read())
    if not match:
        raise SystemExit("IzbanKiosk.Terminal.csproj has no <Version> element.")
    version = match.group(1).strip()

    padded = version if version.count(".") >= 3 else version + ".0" * (3 - version.count("."))
    with open(os.path.join(terminal_build, "IZBAN-Kiosk.exe"), "rb") as handle:
        image = handle.read()
    if image.find(padded.encode("utf-16-le")) < 0:
        raise SystemExit(
            "IZBAN-Kiosk.exe does not carry version " + padded + ". Rebuild Release/x86 after "
            "changing <Version>, otherwise the published tag and the package disagree.")
    return version


def write_json(path, payload):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, ensure_ascii=False)


def write_package_manifest(output_dir):
    files = []
    for root, _, names in os.walk(output_dir):
        for name in sorted(names):
            full = os.path.join(root, name)
            files.append({
                "path": os.path.relpath(full, output_dir).replace("/", "\\"),
                "sha256": sha256(full),
                "size": os.path.getsize(full),
            })
    write_json(os.path.join(output_dir, "package-manifest.json"), {
        "generatedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "purpose": "Windows 7 read-only NFC/SAM and thermal printer hardware test",
        "files": files,
    })


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--vendor-source", required=True,
                        help="AUSKiosk installation root holding the x86 vendor DLLs.")
    parser.add_argument("--output-directory",
                        default=os.path.join(tempfile.gettempdir(), "izban-win7-package"),
                        help="Staging directory for the package layout. Defaults outside the "
                             "repository so build output never clutters the source tree.")
    parser.add_argument("--zip-path", default="",
                        help="Destination .zip. Defaults to IZBAN-Kiosk-v<version>.zip beside "
                             "the staging directory.")
    args = parser.parse_args()

    output_dir = os.path.abspath(args.output_directory)
    zip_path = os.path.abspath(args.zip_path) if args.zip_path else output_dir + ".zip"
    if zip_path.lower().endswith(".zip"):
        zip_base = zip_path[:-4]
    else:
        zip_base = zip_path

    bridge_build = os.path.join(REPOSITORY_ROOT, "src/IzbanKiosk.LegacyHardwareBridge/bin/x86/Release/net40")
    prototype_build = os.path.join(REPOSITORY_ROOT, "src/IzbanKiosk.Terminal/bin/x86/Release/net40")

    if os.path.isdir(output_dir):
        shutil.rmtree(output_dir)
    bridge_dir = os.path.join(output_dir, "Bridge")
    os.makedirs(bridge_dir)

    version = read_release_version(prototype_build)
    copy_runtime(prototype_build, PROTOTYPE_RUNTIME, output_dir)
    copy_runtime(bridge_build, BRIDGE_RUNTIME, bridge_dir)
    copy_operator_helpers(output_dir)
    import_vendor_dlls(os.path.abspath(os.path.expanduser(args.vendor_source)), bridge_dir)
    write_package_manifest(output_dir)

    archive = shutil.make_archive(zip_base, "zip", output_dir)

    checksum = sha256(archive)
    with open(archive + ".sha256", "w", encoding="ascii") as handle:
        handle.write(checksum + "  " + os.path.basename(archive) + "\n")

    print("Package : " + output_dir)
    print("Archive : %s (%d bytes)" % (archive, os.path.getsize(archive)))
    print("Sha256  : " + os.path.basename(archive) + ".sha256")
    print("")
    print("Publish this release as tag and title:  v" + version)
    print("Upload both files as assets. Bump <Version> in IzbanKiosk.Terminal.csproj")
    print("before the next release or kiosks will not move forward.")
    print("No card write/top-up or POS operation is enabled in this package.")


if __name__ == "__main__":
    main()
