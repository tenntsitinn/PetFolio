# PetFolio native macOS development preview

This isolated Swift/AppKit preview uses a simulated pet. It provides a borderless,
nonactivating quota panel, menu-bar controls, click refresh, drag placement, four
preferred corners with screen-edge fallback, hide/restore, background opacity,
five-minute refresh, and persistent preferences. This is not full macOS support.

Pet palette follows a source image, with the hue-bin sampling, transparent/dark
pixel rejection, luminance and text contrast rules ported from Windows `PetTheme`.
The Demo pet menu switches orange/blue/purple/white source PNGs. An absolute
`PETFOLIO_PET_SPRITE` path can point to a real local sprite sheet; file size/mtime
changes trigger resampling. This is a manual source-image integration, not automatic
Codex pet selection/discovery. Every request carries a generation so a slow old
A response cannot overwrite B or a later A. Missing/invalid images use the dark
default; colors are not saved as user appearance overrides. Source and correction
are separate: tint retains source RGB, text picks the higher-contrast black/white.
Contrast is calculated against tint RGB, not every possible scene visible through
low-opacity glass. Extremely transparent panels can still be hard to read.

The content layout follows Windows 1.1.1's `QuotaLabel.cs`: 190 × 96 logical
points, 32-point corners, 24-point horizontal insets, a regular 13-point
`Quota remaining` title, two bold 13-point rows with right-aligned percentages,
and an 11-point status line. AppKit's system font replaces Segoe UI. The circular
close button appears on hover outside the upper-left corner. Native materials
and font rendering still differ from Windows; this is layout parity, not a claim
of identical glass composition or pet colors.

Material can be switched between Frosted glass (`NSVisualEffectView` with live
behind-window blending) and Transparent tint without blur. Opacity 10–100% means
background-layer opacity: glass mode fades the native popover material and adds
source-color tint at 75% of the requested opacity; tint mode fades source-color fill
without blur. Text and window alpha
remain 100%. Native glass at 100% retains its system translucency, while tint at
100% is opaque. These are AppKit materials, not
macOS 26 Liquid Glass. Material and opacity survive restarts; older preferences
without a material field retain their corner/opacity and default to glass.

CI first validates four source-image color changes, then places a sharp patterned window behind the panel and captures 12 actual
desktop images: glass/tint × light/dark appearance × 10/60/100% opacity. View-cache
PNGs are only layout references and cannot validate behind-window blur.

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

On a Mac: `swiftc macos/probe/Model.swift macos/probe/PetPalette.swift macos/probe/Surface.swift macos/probe/main.swift -framework AppKit -o /tmp/petfolio-probe`,
then `/tmp/petfolio-probe /tmp/petfolio-probe-output`. It remains running until Exit.
Add `--ci` only for the automated synthetic verification mode.
