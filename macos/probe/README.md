# PetFolio native macOS development preview

This isolated Swift/AppKit preview uses a simulated pet. It provides a borderless,
nonactivating quota panel, menu-bar controls, click refresh, drag placement, four
preferred corners with screen-edge fallback, hide/restore, background opacity,
five-minute refresh, and persistent preferences. This is not full macOS support.

Quota protocol and process ownership are separate from the UI. Set
`PETFOLIO_CODEX_EXECUTABLE` to an absolute CLI executable to use stdio quota reads;
without it, the preview shows synthetic data. CI always uses a fake CLI and tests
success, errors, EOF and timeout without credentials or network queries. The current
development parser requires both quota windows, labels them 5h/weekly, and does not
yet expose their actual durations or reset times. Real CLI compatibility is untested.

Preferences default to `~/Library/Application Support/PetFolioMacDev/appearance.json`,
overridden by `PETFOLIO_DATA_DIR`. CI uses an isolated temporary directory. This
preview does not yet implement single-instance control, real Pet discovery, or
complete transparent-corner click-through. Drag/click semantics need event-level
tests; CI currently verifies host transitions and layout, not physical mouse input.

The workflow runs on `macos-15` on the `codex/macos-runner-probe` branch and uploads
PNG files, environment details, logs, a JSON result, and the app ZIP as an artifact.
Desktop capture may fail or omit windows; inspect the actual image before claiming
desktop capture works. View snapshots do not prove backdrop composition, real Pet
tracking, permissions, input handling, or multi-display compatibility.

On a Mac: `swiftc macos/probe/Model.swift macos/probe/main.swift -framework AppKit -o /tmp/petfolio-probe`,
then `/tmp/petfolio-probe /tmp/petfolio-probe-output`. It remains running until Exit.
Add `--ci` only for the automated synthetic verification mode.
