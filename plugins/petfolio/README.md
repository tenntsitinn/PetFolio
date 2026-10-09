# PetFolio Codex plugin — Windows x64

This plugin starts, stops, and diagnoses the PetFolio desktop companion. Quota Bubble follows an existing Codex Pet and displays quota for the **Codex CLI login account**, which may differ from the desktop account.

Requirements: Windows x64, .NET Framework 4.8, Codex desktop with a Pet enabled, and a logged-in Codex CLI. No Python, compiler, or MCP server is needed to use this package.

After installing, start a new chat and ask:

- “Open PetFolio Quota Bubble.”
- “Check PetFolio status.”
- “Stop PetFolio.”

Setup checks local paths and dependencies. It does not log in, query quota, or automatically launch the companion. A successful start confirms the companion initialized; show your Pet in Codex to see the bubble. Click the bubble to refresh quota, or use the system tray to adjust appearance and restore a hidden bubble.

You can also invoke the bundled scripts directly from this directory:

```powershell
& .\scripts\status.ps1 -CheckEnvironment
& .\scripts\start.ps1
& .\scripts\stop.ps1
```

For a custom installation, pass both `-CodexExecutable 'C:\path\to\codex.exe'` and `-DataDirectory 'C:\path\to\.codex'` to start or the environment check. The data directory controls pet discovery; CLI authentication is inherited from the launch environment.

Settings and local records live in `%LOCALAPPDATA%\PetFolio`. Set `PETFOLIO_DATA_DIR` to an absolute path before launching to use another directory. Old preferences and palette metadata beside the executable are copied on first launch if the destination files are absent. Existing destination files take priority. If an older portable copy lives elsewhere, copy only its `appearance.json` and optional `pet-palettes.json` into the data directory while PetFolio is stopped. Raw quota snapshots and logs are not required for migration.

Stop PetFolio before upgrading or uninstalling. Plugin removal preserves the data directory and does not automatically stop the desktop process. Removing a marketplace is separate from uninstalling its plugin. To reset all local data, stop the companion first and delete the data directory only if you want to discard preferences, caches, and diagnostic records.

The Windows application uses Codex client internals for pet tracking, so client updates can affect compatibility. No macOS/Linux implementation, automatic updates, or lifecycle hooks are included. Installation does not register Windows startup. Users can opt into "Start when Codex opens" from the application tray menu; disable it before uninstalling, as its independent watcher copy remains in the user data directory. This package is intended for local/Git marketplace testing; public-directory review has not been completed. Licensing is still undecided.
