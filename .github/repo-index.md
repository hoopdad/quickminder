# Repository index

## Functional areas
- `src\Quickminder`: native Windows tray lifecycle, WPF notes popup, clipboard, folder selection and preferences.
- `src\Quickminder.Core`: plain-text note persistence, content versions, OneDrive discovery and safe leading-URL parsing. No UI dependencies.
- `tests\Quickminder.Tests`: dependency-free executable storage and hidden WPF interaction tests using isolated temporary directories.

## Layers and entry points
- `App.xaml.cs` owns the tray icon, single-instance activation and application lifetime.
- `MainWindow.xaml` / `.xaml.cs` owns the bottom-aligned note stack/editor, per-line copy/copy-and-hide/URL actions, saves and filesystem events.
- `NoteStore.cs` reads UTF-8 text files and performs version-checked atomic replacements; the filesystem is the source of truth.
- Preferences live outside the repository in LocalAppData; notes default to an existing OneDrive root's `Quickminder` directory.

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
- No third-party packages; .NET 10 SDK and Windows Desktop runtime required.

## Freshness
Built from baseline `487033e` (README-only repository) and the initial app structure above.
Added solution, app/core/test projects (including `NoteLinks.cs`, `NoteLine.cs` and `Styles.xaml`), README usage instructions and `.gitignore`.
