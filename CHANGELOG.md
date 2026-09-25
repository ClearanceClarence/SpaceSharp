# Changelog

All notable changes to SpaceSharp. The newest release is at the top.

## 1.2.0

The theme of 1.2.0 is finding things. Earlier versions showed you the map; this one lets you ask it questions, pick out what you want to remove, and clear it in one go. It also brings six map styles, a redesigned side panel with your drives, and a set of readability options.

### Filter and highlight
- A filter box sits above the map. Everything that doesn't match is blended toward the background; the layout stays put, so you can see *where* the matches are. Folders that contain a match stay lit, so the path to every hit remains visible.
- The status line explains what was understood in plain words, for example "1,204 files · 48 GB: video, over 500 MB, untouched for 2 years", so a misread query is obvious right away.
- **Plain-language terms.** Type it the way you'd say it: `videos over 500MB not touched in 2 years`, `photos larger than 10 MB`, `.iso`, `installer`. Everything you type must match.
  - Name: any text (`report`), a pattern (`*.mp4 *.mkv`, patterns are OR-ed), or an extension (`.iso`).
  - File type: `videos`, `photos`, `music`, `archives`, `programs`, `documents`, `code`, or `type:video,audio`.
  - Minimum size: `>1GB`, `over 500 MB`, `larger than 2 GB`, `at least 100MB`. A bare number means megabytes.
  - Maximum size: `<10MB`, `under 1 GB`, `smaller than 500MB`.
  - Age: `older than 2 years`, `not modified in 6 months`, `unused for 1 year`, `over 3 years old`, `newer than 30 days`, `modified in the last week`, `last 2 months`.
  - Kind: `files`, `folders`, `is:file`, `is:folder`.
  - Filler words such as "and", "with", "that" and "show" are ignored.
- **Filter panel.** The funnel button in the box opens a panel for people who'd rather click: quick filters (Large files, Big videos, Installers & archives, Untouched for 2 years, Big and old, Recently changed), a name field, file type chips, Larger than / Smaller than drop-downs from 1 MB to 50 GB, a Not modified for drop-down from 1 month to 5 years, and a files/folders switch. Every control writes the filter text into the box, and opening the panel reads the current text back, so the two never disagree.
- **Select matches** (Ctrl+A) selects every matching file; Del then sends them all to the Recycle Bin.
- Ctrl+F focuses the box, Esc clears it, Enter applies immediately. Typing is debounced so the map doesn't redraw on every keystroke.
- Files now record their last modified date during the scan (folders take the newest date inside), which the age terms rely on.

### Side panel with drives and largest items
- A panel on the left of the map, open by default, toggled with the panel button in the toolbar or **L**.
- **Drives** replaces the drive drop-down. Every ready drive is listed with its letter and label, free space, a usage bar, "used of total" and the file system (or Removable, Network, Optical). Click a drive to scan it. The list refreshes after scans and deletes so free space stays current.
- **Largest items** has three tabs. Files and Folders show the 200 largest with a size bar under each row; clicking a row selects it and zooms the map to it. Types shows space per file extension with the file-type color; clicking a type puts `*.ext` into the filter box.
- The lists are built with a priority queue in one pass over the tree, so they appear instantly even on drives with millions of files, and they follow the current size measure.
- The "Scan drive" toolbar button is gone; "Scan folder" is now the highlighted button.

### Multi-select and batch delete
- Ctrl+click adds or removes items; Shift+click selects a range of siblings.
- The status bar shows "12 items selected · 4.2 GB · 318 files".
- Del, or the right-click menu, moves everything selected in a single Recycle Bin operation with one confirmation that lists the first eight paths. If a folder and something inside it are both selected, only the folder is sent. Items that fail to delete (for example, a file in use) are reported, and everything that did go is removed from the map.
- Ctrl+C copies all selected paths, one per line.
- Right-clicking an item outside the selection selects just that item; inside the selection keeps it.

### Hover details
- After the mouse rests on a box for about half a second, a card appears beside it with the name, folder, size, size on disk when it differs, file and folder counts, type, last modified date with a relative age such as "3 years ago", share of the current folder, and a note when content couldn't be read.
- Can be turned off in Settings → Map → Hover details.

### Map styles
- Six styles, chosen from the new **Style** drop-down in the toolbar, in Settings, or by pressing **S** to cycle:
  - **Classic**: title bars, 1 px borders, cushion shading (the previous look).
  - **Tiles**: flat colors, 3 px gaps, softly rounded, folder names as small uppercase captions.
  - **Cards**: folders as raised cards with a soft shadow and bold title, files as flat rounded chips.
  - **Bands**: a deep title band with bold light text, lighter folder body, thin light borders, no shading.
  - **Terraces**: each top-level folder takes one palette color and everything inside gets darker with depth. Works with every palette.
  - **Soft**: rounded pastel blocks with gaps and a gentle top-to-bottom sheen.
- Spacing is tuned so the gap between siblings matches the space between a folder's edge and its children.
- Cushion shading and its C shortcut now apply to the Classic style only.

### Readability
- **Label size**: Normal, Large or Larger. Title bars, file labels and the room a folder needs before it gets a title all scale with it.
- **Outlined labels**: a thin outline in the opposite tone around every label, so text stays readable on any color in any style.
- Label text picks dark or light automatically per box; this now covers the darker fills of Terraces and Bands.

### Layout
- **Folder chains merge more often.** A chain like `Steam › steamapps › common` used to merge only when each folder had exactly one child. Now the child needs to hold at least 97% of the folder, so a few stray files no longer break the merge into stacked title bars.
- **Small items are grouped.** Children that would get less than about 30 × 22 pixels are replaced by a single darker box labeled "812 files" with their combined size. Zooming in gives them more room and they appear individually again. Hovering the group explains what it is; double-clicking zooms into its folder. Toggle with **G** or in Settings.

### Scanning dialog
- Redesigned: the app icon and a title such as "Scanning C: Windows · 63%", three large counters (size found, files, folders), a rate line ("12.4k files per second") and elapsed time.
- For whole-drive scans the progress bar is real, based on the drive's used space; for folder scans it sweeps.
- Long paths are shortened in the middle so the drive and the last folders stay visible.

### About window and feedback
- Shortcuts are listed in two columns, and the window is wider.
- New Feedback section: **Report a bug** opens the GitHub bug form with the version, Windows details and install type already filled in; **Suggest a feature** opens the feature form; **GitHub** opens the repository.
- Issue forms for bugs and feature requests live in the repository, so new issues start with the right questions.

### Settings
- New rows: Map style, Label size, Outlined labels, Hover details, Show side panel, Group small items.
- The side panel setting replaces the earlier "Show largest items panel".

### Shortcuts added
| Key | Action |
|---|---|
| Ctrl+F | Filter the map |
| Ctrl+A | Select every file matching the filter |
| Ctrl+click | Add to selection |
| Shift+click | Select a range |
| L | Side panel |
| S | Next map style |
| Ctrl+C | Copy the selected paths (now several) |
| Del | Move the selected items to the Recycle Bin (now several) |

### Fixes
- The drive list turned white while a scan was running. The list is no longer disabled during scans and has its own template, so the system's white disabled look can't appear.
- The filter box placed the caret about 30 px too far right because the padding was applied twice.
- Filter panel chips were pill-shaped; they now use the same 6 px corners as everything else.

## 1.1.1
- Installers: a Windows Installer (`.msi`) with a choice between per-user and per-machine, and a one-click `Setup.exe`, both with automatic updates via Velopack
- Update notice in the app, **Check for updates** in the About window, and a setting for the startup check
- Branded installer pages and images

## 1.1.0
- Size on disk as an alternative measure
- Optional hard-link detection so files with several names are counted once
- Free-space block for whole-drive scans, growing when you delete files
- Notice for protected folders with one-click restart as administrator
- Cushion shading
- Small items grouped into one "N files" box
- Settings window (Ctrl+,) with all options
- Shortcuts: C toggles cushion shading, G toggles grouping

## 1.0.0
- First release
