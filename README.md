# Quickminder

A small native Windows tray app for keeping frequently used text one click away.
Click the green note icon to open a stack of notes, add a note, or open one in place.
Each logical line has a clipboard button; the editor also supports multiline paste,
normal text editing, undo, and copying the entire note.
The second clipboard button on each line copies that line and hides the popup back
to the tray. It plays a Windows confirmation sound by default; uncheck **Sound on
copy and hide** in the tray menu to turn it off. This preference survives restarts.
Clipboard failures or unsaved drafts keep the popup open rather than hiding errors.
Lines starting with a valid `http://` or `https://` URL also get a launch button.
It opens the text up to the first space in your default browser, so a line like
`https://example.com a useful label` opens only `https://example.com`.
Other URL schemes are not launched.

The note stack is anchored to the bottom of the popup, with status information
and controls above it. Editor text also grows upward from the bottom, leaving
one blank line of visual space beneath the last line. Long notes scroll upward as
you append text; editing earlier lines does not force the cursor back to the end.
Opening a note or reopening the tray popup resets horizontal scrolling to the
left edge and places the caret at the start of its line, preventing Windows from
automatically scrolling back to the right.
The blank space is layout padding, not extra text saved to your files.

## Run

Requires Windows 10/11 and the .NET 10 SDK to build:

```powershell
dotnet build Quickminder.slnx
dotnet run --project src\Quickminder
```

The popup opens at launch. Close it or press Escape to hide it, not quit.
Click the tray icon to show/hide it; right-click for folder settings or **Exit**.
The popup stays above other windows while open. `Ctrl+N` creates a note.
Launch again to bring the existing instance forward.

**Keep the tray icon visible:** Windows owns notification-area pinning. Drag the
Quickminder icon out of the tray overflow onto the taskbar, or use the tray menu's
**Tray visibility settings** and enable Quickminder under other system tray icons.
The app cannot override this Windows preference.

## Your notes

- Every text change is saved immediately to an individual `.txt` file, including blank notes.
- Default folder: `Quickminder` inside an existing OneDrive root (work, personal,
  then generic OneDrive environment setting). If OneDrive is unavailable, you
  must choose a folder; there is no silent local-only fallback.
- Use **Choose notes folder** in the tray menu to use a different local or
  synced directory. Existing files are not moved or deleted when switching.
- The first nonempty line is the note's display title. Stable generated filenames
  avoid renaming files on every title edit. Existing `.txt` files are included.
- Writes use UTF-8 and atomic replacement. OneDrive itself owns cloud sync;
  "Saved to disk" does not claim that cloud upload has completed.
- External edits refresh from filesystem events. Version checks detect changes
  before saves/deletes; an unsaved draft blocks hiding, switching notes and exit.
  Use **Save a copy** to preserve it, or explicitly discard it with **Reload from disk**.
  As with other filesystem-based editors, there is no distributed lock with
  OneDrive: simultaneous writes on different devices can still create sync conflicts.
- **Delete** asks for confirmation and permanently removes the `.txt` file,
  rather than sending it to the local Recycle Bin. OneDrive retention policies may differ.
- Folder preferences are in `%LOCALAPPDATA%\Quickminder\settings.json`.
  No account login, cloud API, database or third-party runtime is used by this app.

## Build and regression checks

```powershell
dotnet build Quickminder.slnx
dotnet run --project tests\Quickminder.Tests
```

The dependency-free Windows test executable uses an isolated temporary folder to cover
keystroke persistence, exact multiline text, restart, imported notes, conflicts,
deletion, path validation, and OneDrive discovery. Hidden WPF controls also exercise
note selection, reopening, keystroke saves, conflict warnings and draft recovery.
They measure the bottom-aligned stack, one-line editor inset, scrolling and
per-line anchors, and cover URL-token parsing and mixed newline formats.
It does not touch real notes or display test windows.

For a distributable framework-dependent build (requires .NET 10 Windows Desktop Runtime):

```powershell
dotnet publish src\Quickminder -c Release -o artifacts\Quickminder
```

Run `artifacts\Quickminder\Quickminder.exe`. No installation or login-startup entry
is made automatically. To launch at sign-in, create a shortcut to your published
executable in the folder opened by `shell:startup`.
