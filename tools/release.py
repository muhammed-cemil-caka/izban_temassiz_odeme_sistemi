#!/usr/bin/env python3
"""Cuts a kiosk release: bumps every version marker, builds, tests and packages.

The release number lives in four files and the bridge protocol version in two
more. Editing them by hand is what produced the two field failures this project
has had: a package whose tag ran ahead of the assembly (kiosks download it
forever and never move), and a terminal that expected a bridge version its own
package did not carry (kiosks refuse to start). Both are invisible until a
machine in a station stops working.

Usage:
    python3 tools/release.py --bump patch
    python3 tools/release.py 1.1.0
    python3 tools/release.py --bump patch --dry-run

Add --bridge-version when the bridge protocol itself changes; it is written to
both sides at once, which is the only way they can disagree.
"""
import argparse
import os
import re
import subprocess
import sys

REPOSITORY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TERMINAL_CSPROJ = "src/IzbanKiosk.Terminal/IzbanKiosk.Terminal.csproj"
MAIN_WINDOW = "src/IzbanKiosk.Terminal/MainWindow.xaml.cs"
PIPE_SERVER = "src/IzbanKiosk.LegacyHardwareBridge/Transport/NamedPipeHardwareServer.cs"
READ_ME = "tools/win7-package/OKU-BENI.txt"

# Each marker is (file, regex with one capture group around the value alone).
# The group is what gets replaced, so surrounding syntax cannot drift.
RELEASE_VERSION = (TERMINAL_CSPROJ, r"<Version>([^<]+)</Version>")
PACKAGE_LABEL = (MAIN_WINDOW, r'PackageVersion = "R(\d+)"')
READ_ME_LABEL = (READ_ME, r"^IZBAN KIOSK R(\d+) -")
BRIDGE_EXPECTED = (MAIN_WINDOW, r'ExpectedBridgeVersion = "([^"]+)"')
BRIDGE_REPORTED = (PIPE_SERVER, r'Version = "(\d[^"]*-net40)"')


def path_of(relative):
    return os.path.join(REPOSITORY_ROOT, relative)


def read(relative):
    with open(path_of(relative), encoding="utf-8", newline="") as handle:
        return handle.read()


def read_marker(marker):
    relative, pattern = marker
    match = re.search(pattern, read(relative), re.MULTILINE)
    if not match:
        raise SystemExit("Version marker not found in %s: %s" % (relative, pattern))
    return match.group(1)


def write_marker(marker, new_value, dry_run):
    relative, pattern = marker
    text = read(relative)
    match = re.search(pattern, text, re.MULTILINE)
    if not match:
        raise SystemExit("Version marker not found in %s: %s" % (relative, pattern))

    if match.group(1) == new_value:
        return False

    start, end = match.span(1)
    if not dry_run:
        with open(path_of(relative), "w", encoding="utf-8", newline="") as handle:
            handle.write(text[:start] + new_value + text[end:])
    return True


def next_version(current, bump):
    parts = current.split(".")
    if len(parts) != 3 or not all(part.isdigit() for part in parts):
        raise SystemExit("Current version is not major.minor.patch: " + current)
    major, minor, patch = (int(part) for part in parts)

    if bump == "major":
        return "%d.0.0" % (major + 1)
    if bump == "minor":
        return "%d.%d.0" % (major, minor + 1)
    return "%d.%d.%d" % (major, minor, patch + 1)


def check_bridge_versions(requested):
    """Refuses to build while the two halves of the bridge handshake disagree.

    The terminal will not talk to a bridge whose version differs by a single
    character, so a mismatch here is a kiosk that cannot start at all.
    """
    expected = read_marker(BRIDGE_EXPECTED)
    reported = read_marker(BRIDGE_REPORTED)

    if requested:
        return expected, reported, True

    if expected != reported:
        raise SystemExit(
            "Bridge version mismatch in source:\n"
            "  %s expects '%s'\n"
            "  %s reports '%s'\n"
            "Pass --bridge-version to set both, or fix one by hand."
            % (MAIN_WINDOW, expected, PIPE_SERVER, reported))
    return expected, reported, False


def run(command, dry_run):
    print("  $ " + " ".join(command))
    if dry_run:
        return
    result = subprocess.run(command, cwd=REPOSITORY_ROOT)
    if result.returncode != 0:
        raise SystemExit("Command failed: " + " ".join(command))


def main():
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("version", nargs="?",
                        help="Explicit release version, e.g. 1.1.0. Omit and use --bump.")
    parser.add_argument("--bump", choices=["major", "minor", "patch"],
                        help="Derive the next version from the current one.")
    parser.add_argument("--bridge-version",
                        help="New bridge protocol version, e.g. 2.6.0-net40. Written to "
                             "both the terminal's expectation and the bridge's reply.")
    parser.add_argument("--vendor-source", default="~/Desktop/AUSKiosk",
                        help="AUSKiosk install holding the x86 vendor DLLs.")
    parser.add_argument("--dotnet-installer", default="~/Desktop/NDP48-x86-x64-AllOS-ENU.exe",
                        help="Offline .NET 4.8 installer for the USB setup archive. "
                             "Pass an empty string to skip that archive.")
    parser.add_argument("--output-directory", default="~/Desktop",
                        help="Where the .zip files are written.")
    parser.add_argument("--skip-tests", action="store_true")
    parser.add_argument("--dry-run", action="store_true",
                        help="Show every change and command without making any.")
    args = parser.parse_args()

    if bool(args.version) == bool(args.bump):
        raise SystemExit("Give exactly one of: a version argument, or --bump.")

    current = read_marker(RELEASE_VERSION)
    version = args.version or next_version(current, args.bump)
    if not re.match(r"^\d+\.\d+\.\d+$", version):
        raise SystemExit("Version must be major.minor.patch: " + version)

    label = int(read_marker(PACKAGE_LABEL))
    read_me_label = int(read_marker(READ_ME_LABEL))
    if label != read_me_label:
        raise SystemExit(
            "Package label disagrees: %s says R%d, %s says R%d."
            % (MAIN_WINDOW, label, READ_ME, read_me_label))

    expected, reported, bridge_changing = check_bridge_versions(args.bridge_version)
    new_label = label + 1

    print("Release")
    print("  Version : %s -> %s" % (current, version))
    print("  Label   : R%d -> R%d" % (label, new_label))
    if bridge_changing:
        print("  Bridge  : %s -> %s  (terminal and bridge together)" % (expected, args.bridge_version))
    else:
        print("  Bridge  : %s (unchanged, both sides agree)" % expected)
    if args.dry_run:
        print("  DRY RUN - nothing is written or built.")
    print("")

    print("Updating version markers")
    for marker, value in (
            (RELEASE_VERSION, version),
            (PACKAGE_LABEL, str(new_label)),
            (READ_ME_LABEL, str(new_label))):
        changed = write_marker(marker, value, args.dry_run)
        print("  %-58s %s" % (marker[0], "yazildi" if changed else "degismedi"))

    if bridge_changing:
        for marker in (BRIDGE_EXPECTED, BRIDGE_REPORTED):
            write_marker(marker, args.bridge_version, args.dry_run)
            print("  %-58s %s" % (marker[0], "kopru surumu yazildi"))
    print("")

    print("Building")
    for project in ("src/IzbanKiosk.LegacyHardwareBridge/IzbanKiosk.LegacyHardwareBridge.csproj",
                    "src/IzbanKiosk.Terminal/IzbanKiosk.Terminal.csproj"):
        run(["dotnet", "build", project, "-c", "Release", "-p:Platform=x86"], args.dry_run)

    if not args.skip_tests:
        print("Testing")
        run(["dotnet", "test", "tests/IzbanKiosk.Tests/IzbanKiosk.Tests.csproj"], args.dry_run)

    print("Packaging")
    output = os.path.expanduser(args.output_directory)
    package = ["python3", "tools/Prepare-Win7HardwareTestPackage.py",
               "--vendor-source", os.path.expanduser(args.vendor_source),
               "--zip-path", os.path.join(output, "IZBAN-Kiosk-v%s.zip" % version)]
    if args.dotnet_installer:
        package += ["--dotnet-installer", os.path.expanduser(args.dotnet_installer)]
    run(package, args.dry_run)

    print("")
    print("Yayin adimlari")
    print("  1. git commit -am \"...\"  (surum isaretleri de degisti)")
    print("  2. GitHub Releases -> etiket ve baslik: v%s" % version)
    print("  3. Yuklenecek: IZBAN-Kiosk-v%s.zip ve .sha256  (KURULUM zip'i YUKLENMEZ)" % version)
    print("  4. Draft/pre-release ISARETLEMEYIN - kod /releases/latest sorguluyor.")


if __name__ == "__main__":
    main()
