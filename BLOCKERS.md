# Legacy Hardware Deployment Blockers and Verified Facts

## Verified from the supplied working AUSKiosk installation

- `EMVRdr35Lib.dll`, `KioskPrint.dll` and the whitelisted card libraries are PE32/x86.
- The installed kiosk configuration identifies the NFC reader as `COM4`.
- Metadata from the supplied `AUSKiosk.exe` version `5.2.0.4` confirms the native method names, return/parameter types, `StdCall` calling convention and ANSI character set used by `EmvRdr35NativeMethods`.
- The same metadata confirms these explicit x86 layouts:
  - `CARD_LAYOUT`: pack 1, size 27.
  - `AV2_LAYOUT_EXT`: pack 1, size 62.
  - `AV2_LAYOUT`: pack 1, size 13.
  - `TOffCardInf`: pack 1, size 18.
- `KioskPrint.dll` exports the print functions used by the read-only bridge:
  `PrinterBeginDoc`, `PrinterEndDoc`, `PrinterTextOut`, `PrinterCenteredTextOut`,
  `PrinterBitmap`, `PrinterSetFont`, `GetSpoolerJobCount`, `PurgeJobsOnCurrentPrinter`,
  `TestPrinterFonts`. None of them accepts a printer name.
- `KioskPrint.dll` is a Delphi/VCL library (`TPrinter`/`TPrinterCanvas` RTTI, `Printers`
  unit) that resolves its target through `GetProfileStringA` on `[windows] device`, i.e.
  the Windows default printer, and imports `gdi32!CreateDCA/StartDocA/EndDoc` plus
  `winspool!OpenPrinterA/EnumPrintersA/EnumJobsA`. Receipt routing is therefore only
  controllable through the Windows default queue, and only before the first vendor call
  of the hosting process.
- Decompiled `AUSKiosk.exe` 5.2.0.4 confirms the working call order
  (`PrinterBeginDoc` → `PrinterSetFont("Tahoma", 8)` → `PrinterTextOut` → `PrinterEndDoc`,
  y start 5, step 30) and that its only printer-readiness gate is
  `kpf.GetSpoolerJobCount() <= 3`. It never inspects the default printer name.

These checks establish binary-contract compatibility. They do not replace a physical hardware-in-the-loop test.

## Blocking before the first physical test

1. Build `IzbanKiosk.LegacyHardwareBridge` and `IzbanKiosk.Win7Prototype` as Release/x86/net48 on Windows with the .NET Framework 4.8 targeting pack.
2. Prepare the package with `tools/Prepare-Win7HardwareTestPackage.ps1`; do not copy DLLs recursively from `AUSKiosk\Temp` because it contains multiple historical versions.
3. Set a Base64-encoded `IZBAN_HMAC_SECRET` containing at least 32 random bytes.
4. Stop `AUSKiosk.exe` during testing so it releases `COM4`.
5. Verify that the termal printer is installed in Windows and that its spooler name is known.
   Run `IzbanKiosk.LegacyHardwareBridge.exe --list-printers` on the kiosk and copy the exact
   queue name into `ThermalPrinterName`. The value currently committed in
   `KioskHardware.config.json` ("Trentino Printer Driver 56mm") reads like a driver name and
   has not been confirmed against an installed queue.

## Must remain unverified until physical comparison

- `TOffCardInf.balance` is read successfully only after the real reader and SAM accept the card.
- The assumed money scale of 100 must be compared against at least three known card balances. The removed `--verify-scale` shortcut cannot mark this as verified.
- `IsAuthoritative` must remain false until the card/SAM result and balance scale are accepted by the authorized İzmirim Kart integration owner.
- Printer API success means the job was submitted; the first test still requires checking that paper was physically produced and correctly formatted.

## Operating-system boundary

- The whole product targets .NET Framework 4.0 Client Profile / x86: `IzbanKiosk.Win7Prototype`
  (WPF kiosk UI) and `IzbanKiosk.LegacyHardwareBridge` (hardware process). This is the only
  combination the Windows 7 Embedded machine can run, and both build with the .NET SDK on any
  host OS.
- The former .NET 8 / Avalonia stack was removed. `tests/IzbanKiosk.Tests` still targets net8.0
  because it is a developer-machine test host that links the net40 sources; it never ships to
  the kiosk.
- The implemented pipe clients connect to `.` and are local-machine only. No network bridge is implemented.
- Bridge and client must run in the same Windows user session because the named-pipe ACL is restricted to the current user.

## Safety boundary for this phase

- Card top-up/write, ticket charge and auto-top-up are refused by the bridge with
  `ERR_ACCESS_DENIED`.
- POS payment has a real seam but no implementation: `IPosTerminal` is the single interface a
  certified bank SDK plugs into, and `NotConfiguredPosTerminal` refuses every charge with
  `ERR_POS_NOT_CONFIGURED` until one is registered in `Program.Main`. No passenger can be
  charged in the meantime.
- The test package must not include the legacy EXE, database, logs, PDB files or certificates.
- Vendor DLL redistribution, production key management and İzmirim Kart authorization/licensing remain organizational requirements.
