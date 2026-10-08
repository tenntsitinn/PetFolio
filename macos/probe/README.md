# macOS runner feasibility probe

This isolated Swift/AppKit experiment does not port PetFolio or access a Codex account.
It builds an ad-hoc signed app, displays synthetic quota and pet windows, captures
three AppKit view states, moves both windows, and attempts a desktop screenshot.

The workflow runs on `macos-15` on the `codex/macos-runner-probe` branch and uploads
PNG files, environment details, logs, a JSON result, and the app ZIP as an artifact.
Desktop capture may fail or omit windows; inspect the actual image before claiming
desktop capture works. View snapshots do not prove backdrop composition, real Pet
tracking, permissions, input handling, or multi-display compatibility.

On a Mac: `swiftc macos/probe/main.swift -framework AppKit -o /tmp/petfolio-probe`,
then `/tmp/petfolio-probe /tmp/petfolio-probe-output`.
