# Accessibility

This page says plainly what SpaceSharp does and does not do for people who use a screen reader, keyboard only, high contrast, large text, or who see color differently. It is written to be checked against the current release and updated when something changes; if you find it wrong, that is a bug worth reporting (see below).

## What works today

**Text size and scaling.** SpaceSharp is a WPF app and follows the Windows display scaling (125 %, 150 %, and so on) for every window. Inside the map, Settings › Appearance › Label size has five steps from Smallest to Larger, and Settings › Treemap › Font changes the typeface; Outlined labels adds a contrasting outline so text stays readable on any box color.

**Color.** The Color-blind safe palette uses the Okabe and Ito colors, chosen to stay distinguishable with the common kinds of color vision deficiency. Color is never the only carrier of information: every box has a label with its name and size, the legend names each category, and the filter dims non-matches rather than only recoloring them. Light and dark themes follow Windows or can be fixed.

**Keyboard.** Most of the app is reachable without a mouse: F5 rescans, Backspace and Home move around the map, Ctrl+F opens the filter, Ctrl+A selects matches, Ctrl+I opens Inspect, Del deletes, Ctrl+S and Ctrl+O save and open scans, Ctrl+, opens Settings, F1 opens About, and S, K, G, L switch styles, colors, density and the side panel. The full list is in About › Keyboard shortcuts. Dialogs answer to Enter and Escape and keep focus inside them.

**Standard controls.** The side panel, toolbar, filter panel, Settings, Inspect, About and all dialogs are built from ordinary WPF controls, so Narrator, NVDA and JAWS read their text, state and roles, and Tab moves through them in a sensible order.

**Language.** English and Norwegian bokmål; the app follows the Windows language by default.

## What does not work yet

**The map itself is not accessible to screen readers.** The treemap is a custom-drawn surface with no UI Automation tree behind it: a screen reader sees one large control and nothing inside it, and arrow keys do not move between boxes. The information in the map is available elsewhere (the side panel lists the largest files, folders and types; Inspect describes any selected item; the filter's match count is spoken), but there is no way yet to walk the map's folders with a keyboard or have them read out. This is the largest gap and the one we most want to close.

**High-contrast themes.** The app uses its own colors rather than the system high-contrast palette, so turning on a Windows high-contrast theme does not restyle SpaceSharp. The dark and light themes have strong contrast on their own, but they are not the user's chosen colors.

**Motion.** The zoom animation respects the app's own Animate zoom setting (Settings › Map) but not yet the Windows "Show animations" preference; turn Animate zoom off if motion is a problem.

**Hover-only information.** The hover card shows details for the item under the mouse; the same details are reachable with Ctrl+I on a selection, but there is no keyboard way to point at an arbitrary box.

## Planned

In rough order:

1. A UI Automation peer for the map so each visible box is an element with its name, size and position in the hierarchy, and arrow keys move between siblings, Enter zooms in, Backspace goes up.
2. Honoring the Windows "Show animations" setting and high-contrast mode.
3. A keyboard focus indicator inside the map, drawn like the selection frame.

If you depend on one of these, say so in an issue; it helps decide what comes first.

## Reporting a problem

Open a bug report through the issue chooser, or use About › Report a bug, and start the title with "Accessibility:" so it is easy to find. Please include which assistive technology you use (and its version), what you expected, and what happened. Keyboard-only and low-vision reports are as welcome as screen-reader ones.

If you would rather not report publicly, use the contact on the maintainer's GitHub profile at [github.com/ClearanceClarence](https://github.com/ClearanceClarence).

## For contributors

- New controls must be reachable with Tab and have a name a screen reader can speak; set `AutomationProperties.Name` on anything whose purpose is only shown by an icon.
- Do not communicate state by color alone; add text, a glyph or a tooltip.
- Keep every string in `Resources/Strings.resx` so screen readers get the user's language.
- Test at 150 % scaling at least once; layouts that only work at 100 % are a bug.
