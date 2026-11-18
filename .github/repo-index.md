# Repository index

## Functional areas
- `src\Quickminder`: native Windows tray lifecycle, WPF notes popup, clipboard, folder selection and preferences.
- `src\Quickminder.Core`: plain-text note persistence, content versions, OneDrive discovery and safe leading-URL parsing. No UI dependencies.
- `tests\Quickminder.Tests`: dependency-free executable storage and hidden WPF interaction tests using isolated temporary directories.
- `installer`: per-user Windows x64 MSI definition; `scripts\Build-Release.ps1` builds and verifies self-contained release assets.
- `docs\screenshots`: live demo stack/editor PNGs embedded in the README.

## Layers and entry points
- `App.xaml.cs` owns the tray icon, single-instance activation and application lifetime.
- `MainWindow.xaml` / `.xaml.cs` owns the bottom-aligned note stack/editor, per-line copy/copy-and-hide/URL actions, saves and filesystem events.
- `NoteStore.cs` reads UTF-8 text files and performs version-checked atomic replacements; the filesystem is the source of truth.
- Preferences live outside the repository in LocalAppData; notes default to an existing OneDrive root's `Quickminder` directory.
- `--demo` uses separate single-instance handles and LocalAppData notes, never normal preferences. `SampleNotes.cs` seeds only an empty demo store.
- Optional `--demo --screenshots <absolute folder>` enables F12 export of the current WPF view; screenshot export is disabled in normal mode.

## Navigation and verification
- Tray behavior / folder switching: `App.xaml.cs`.
- Notes UI / copy / save / external changes: `MainWindow.xaml.cs`.
- Shared WPF theme resources: `Styles.xaml` (also loaded by isolated UI tests).
- Storage / conflicts / discovery: `Quickminder.Core\NoteStore.cs`.
- Default-browser URL token parsing: `Quickminder.Core\NoteLinks.cs`.
- Logical line offsets across newline formats: `Quickminder.Core\NoteLine.cs`; avoids WPF's top-alignment assumptions for line actions.
- Copy-and-hide sound preference (default on): `App.xaml.cs` preferences and tray menu.
- Build: `dotnet build Quickminder.slnx`.
- Regression tests: `dotnet run --project tests\Quickminder.Tests`.
- App and tests have no third-party packages; MSI packaging restores WiX SDK 5.0.2. Self-contained release packages include the runtime.
- UI-change screenshot requirement: `.github\copilot-instructions.md`; capture steps and release procedure: `README.md`.
- Release build: `.\scripts\Build-Release.ps1 -Version <major.minor.patch>`; validates tests, MSI and extracted payload hashes, writes checksums and source/configuration record.
- `.github\workflows\release.yml`: PR package checks; manual publication from clean reviewed `main`, with full ICE validation and artifact integrity checks.

## Freshness
Refreshed from app baseline `c8669fb` and this sample-data/screenshots/release change.
Considered added `SampleNotes.cs`, `.github\copilot-instructions.md`, `docs\screenshots`,
`installer\Package.wxs`, `installer\Quickminder.Installer.wixproj`, `scripts\Build-Release.ps1`
and `.github\workflows\release.yml`.
