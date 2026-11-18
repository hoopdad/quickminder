# Project agent instructions

- Read `README.md` and `.github/repo-index.md` before making changes.
- Keep personal notes, folder paths, clipboard contents and account data out of
  screenshots, fixtures and commits. Use the isolated `--demo` app for examples.
- **Whenever the UI changes, update every affected README screenshot in the same
  change.** Run the real WPF app with fictional sample notes, inspect the affected
  views, export fresh PNGs with F12 and review them before committing. Do not
  substitute mockups or reuse screenshots that no longer match the UI.
  Follow the README's screenshot procedure. If capture is blocked, report the
  blocker rather than claiming the screenshots are current.
- Run `dotnet build Quickminder.slnx` and
  `dotnet run --project tests\Quickminder.Tests` for code changes.
- Release from exact committed, reviewed `main` source. Use
  `scripts\Build-Release.ps1`, retain the source commit, effective configuration,
  verification results and artifact SHA-256s in the published release record.
  Never publish a dirty validation build. Keep the MSI upgrade code stable;
  increment the three-part version for a new release. Exit Quickminder before
  installing/upgrading; uninstall must leave user notes and preferences intact.
