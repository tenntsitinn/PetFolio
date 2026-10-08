---
name: petfolio
description: Start, stop, check, or troubleshoot PetFolio and its Quota Bubble beside an existing Codex Pet on Windows. Use for PetFolio companion controls and setup.
---

# PetFolio

PetFolio is a Windows x64 desktop companion. It displays quota from the Codex CLI login account beside an existing Codex desktop Pet. CLI and desktop accounts can differ. It reads pet settings and window state; it does not modify Codex settings or pet assets.

Resolve the plugin root from this skill's location: two parent directories above the folder containing this `SKILL.md`. Use absolute script paths, quoted when needed. Do not assume the user's working directory is the plugin directory.

Run the bundled PowerShell scripts for the requested operation:

| Request | Script relative to the plugin root |
| --- | --- |
| Open, show, or restore Quota Bubble | `scripts/start.ps1` |
| Stop or close PetFolio | `scripts/stop.ps1` |
| Is PetFolio running? | `scripts/status.ps1` |
| Setup or diagnose startup | `scripts/status.ps1 -CheckEnvironment` |

For example, invoke `& '<absolute-plugin-root>\scripts\start.ps1'` in PowerShell. Scripts return JSON; check `ok`, `running`, and `ready` rather than treating process launch alone as success. `ready` means the companion initialized, not that a Pet is visible or that quota has been fetched.

For setup without an explicit launch request, check the environment and explain the result. When the user requests opening the bubble, run start; it validates the environment and starts or restores the single instance. Users with custom installations can supply both `-CodexExecutable '<absolute-codex.exe>'` and `-DataDirectory '<absolute-desktop-data-directory>'` to start or the environment check. The latter identifies desktop pet data; it does not switch the CLI login account.

The environment check does not query quota, validate login, or verify Pet visibility. If the application starts but no bubble appears, ask the user to show their Pet in Codex and check the tray's **Show Quota Bubble** / **Features → Quota Bubble** controls. If quota fails, check CLI login and network access. Clicking the bubble refreshes quota; opacity and placement are controlled through its tray menu. This plugin has no MCP interface for refresh or appearance changes.

Settings, palette metadata, quota snapshots, and diagnostics live in `%LOCALAPPDATA%\PetFolio`, or the absolute directory specified by `PETFOLIO_DATA_DIR`. Configuration survives plugin updates. Stop PetFolio before updating or uninstalling its plugin. Uninstalling does not delete user data. For a full reset, explain the affected data and remove it only when requested; do not dump raw logs, palette metadata, or account data into the chat.

If Windows x64 or .NET Framework 4.8 is unavailable, report the requirement. If the packaged executable is missing, report an incomplete plugin package; do not compile sources or download an executable implicitly. Installation does not automatically start PetFolio or register Windows startup.
