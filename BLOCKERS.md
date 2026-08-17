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

## Deployment requirements on each kiosk

The first physical test is long past: card reading, balance display, receipt printing,
the shell takeover and a real card load have all run on the bench kiosk. What follows is
what each machine still needs, not what blocks a first attempt.

1. Build both projects as Release/x86/**net40** (`TargetFrameworkProfile` Client). They
   compile with the .NET SDK on any host OS, macOS included; a Windows toolchain is not
   required.
2. Prepare the package with `tools/Prepare-Win7HardwareTestPackage.py`; do not copy DLLs
   recursively from `AUSKiosk\Temp` because it contains multiple historical versions.
3. Set a Base64-encoded `IZBAN_HMAC_SECRET` containing at least 32 random bytes.
4. Stop `AUSKiosk.exe` during testing so it releases `COM4`.
5. Enable "bidirectional support" on the thermal printer queue (Printer properties, Ports
   tab). With it off, Windows never queries the device and every status flag reads 0: the
   queue accepts jobs and no paper appears, with no error anywhere. The Alsancak kiosk
   reported PRINTER_STATUS_DOOR_OPEN (0x400000) the moment it was enabled, after days of
   silent failure — the underlying fault there was no 24 V supply to the print head.
   `KURULUM.bat` now ticks the box itself, and the diagnostics screen writes the queue the
   device actually answers on, so the value committed in `KioskHardware.config.json`
   ("Trentino Printer Driver 56mm") is a starting guess, not a claim about any machine.
6. The system clock must be right, and on hardware this old it frequently is not. GitHub's
   certificate is validated against it, so a kiosk whose CMOS battery has died stops
   receiving updates while serving passengers perfectly — the only symptom is a TLS error
   that mentions neither the date nor the remedy. The kiosk now checks and corrects its own
   clock (`KioskClock`), reports the verdict on the diagnostics screen and names the clock
   in the updater's failure message; `KURULUM.bat` settles the date before it tests GitHub
   access, and `6-Saat-Duzelt.bat` is the manual route. A clock that resets at every boot is
   a dead CR2032, not a software fault.

## Must remain unverified until the integration owner accepts it

- `TOffCardInf.balance` reads correctly through the real reader and SAM — confirmed on the
  bench kiosk on 2026-08-10.
- The money scale is settled for writing: a 1,00 TL load passed `100` to the vendor and the
  balance read back exactly, so the vendor `amount` is kuruş and the read path agrees with
  it. It has not been compared against a wide spread of pre-existing card balances.
- Authorisation to load value in production is an **organisational** approval from the
  İzmirim Kart scheme owner, not anything missing in code. Nothing cryptographic is
  outstanding: the SAM installed in the machine already carries write authority, proven
  by a 1,00 TL load that read back exactly. What is outstanding is permission to use
  their keys, against their cards, for real passengers — plus their sign-off on what the
  kiosk reports for end-of-day reconciliation.

  Do not confuse this with the `IsAuthoritative` flag on `CardSnapshotResponse`, which is
  unrelated: it means "this balance came off the real card through a verified SAM rather
  than from a cache or a simulator" and is already true whenever the SAM verifies. It
  gates whether the kiosk trusts a reading enough to show and print it. An earlier
  revision of this file implied the flag was held false pending the approval; it never
  was, and reading it that way sends someone looking for a switch in the code that does
  not exist.
- Printer API success means the job was submitted; that paper is physically produced and
  correctly formatted has been verified by hand on the bench kiosk and must be re-checked
  on each machine, because it depends on that machine's queue and print-head power.

## Operating-system boundary

- The whole product targets .NET Framework 4.0 Client Profile / x86: `IzbanKiosk.Terminal`
  (WPF kiosk UI) and `IzbanKiosk.LegacyHardwareBridge` (hardware process). This is the only
  combination the Windows 7 Embedded machine can run, and both build with the .NET SDK on any
  host OS.
- The former .NET 8 / Avalonia stack was removed. `tests/IzbanKiosk.Tests` still targets net8.0
  because it is a developer-machine test host that links the net40 sources; it never ships to
  the kiosk.
- The implemented pipe clients connect to `.` and are local-machine only. No network bridge is implemented.
- Bridge and client must run in the same Windows user session because the named-pipe ACL is restricted to the current user.

## Safety boundary for this phase

- Ticket charge, auto-top-up and raw block writes are refused by the bridge with
  `ERR_ACCESS_DENIED`.
- The vendor write call is now reachable: `EMVRdr35Lib.dll` exports `Topup`, and the
  exact signature `Topup(hComp, termNo, termUId, companyId, dbRefNo, amount, av2P)` was
  read out of the deployed AUSKiosk's own metadata. `LegacyCardLoader` calls it, but
  refuses until the deployment sets `CardWriteEnabled`, `TerminalNo`, `TerminalUid`,
  `CompanyId` and `CardWriteAmountUnit`. Every default is the refusing one.
- RESOLVED on 2026-08-10 by a real 1,00 TL test load whose balance read back exactly:
  the vendor `amount` argument is in **kuruş**, so `CardWriteAmountUnit` is `Minor`.
  The same test settled `TerminalNo` = `TerminalUid` = the kiosk's own number from
  `setup.ini`, `CompanyId` = 1, and proved that the SAM already in the machine carries
  write authority — no new DLL or SAM was ever needed.
- Card top-up now runs through `TopUpSaga`, which sequences charge -> load -> read-back
  and reverses the payment when the load fails. It still completes nothing: `ICardLoader`
  is `NotAuthorisedCardLoader` until a write-capable SAM, its keys and scheme
  authorisation are delivered, and the saga refuses before it reaches the payment
  terminal so no passenger is charged for value the kiosk cannot deliver.
- `IPosTerminal` now requires `Reverse`. Confirm the bank SDK supports void/reversal
  before integration starts; without it the charge-then-write order is unsafe.
- POS payment has a real seam but no implementation: `IPosTerminal` is the single interface a
  certified bank SDK plugs into, and `NotConfiguredPosTerminal` refuses every charge with
  `ERR_POS_NOT_CONFIGURED` until one is registered in `Program.Main`. No passenger can be
  charged in the meantime.
- The test package must not include the legacy EXE, database, logs, PDB files or certificates.
- Vendor DLL redistribution, production key management and İzmirim Kart authorization/licensing remain organizational requirements.
