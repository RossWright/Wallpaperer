# Wallpaperer v1

A .NET 10 Avalonia desktop app that shows the current monitors, lets the user drop a picture on each one, and applies those pictures as a single GNOME spanned wallpaper.

This document is the contract for the first version. Later work (one picture spanning several monitors, collection management, saved setups) is out of scope here and must not be built ahead of it.

## Environment

- Ubuntu with GNOME on Wayland. The development machine is Ubuntu 26.04, GNOME Shell 50, .NET 10.
- The app runs inside the user session so it can read `org.gnome.Mutter.DisplayConfig` and write `org.gnome.desktop.background`.
- Target framework `net10.0`. Create the project from the current stable Avalonia app template. Use SkiaSharp, which Avalonia already brings in, for decoding, scaling, and encoding. Use CommunityToolkit.Mvvm for the window view model.

## What the user sees

The window is titled Wallpaperer. It opens immediately, then fills in.

```
+----------------------------------------------------------+
| Wallpaperer                                   [ Apply ]  |
+----------------------------------------------------------+
|                                                          |
|            monitor arrangement (relative)                |
|                                                          |
+----------------------------------------------------------+
|  thumbnails, wrapping, scrolling                         |
|                                                          |
+----------------------------------------------------------+
| status line                                              |
+----------------------------------------------------------+
```

- The top region is a fixed-height panel (about 280px) that shows one rectangle per connected monitor. Rectangles keep their real relative positions, sizes, and the gaps between them. The whole arrangement is scaled uniformly to fit inside the panel with a margin.
- The bottom region fills the remaining window and scrolls. It shows image thumbnails in a wrapping flow.
- Apply sits in the title row, always visible. It is enabled only when at least one monitor has a picture and no apply is in progress.
- A status line reports scanning, image count, apply progress, and errors.

Each monitor rectangle shows:

- the display name from Mutter (for example `Samsung Electric Company 32"`), the connector (`DP-3`), and the pixel size
- a "Primary" mark on the primary monitor
- the assigned picture, drawn with the same cover crop that will be baked into the wallpaper
- an empty state, "Drop an image", when nothing is assigned
- a way to clear the assignment (a small clear control on the rectangle)

The arrangement for the current machine looks like this. Coordinates are the Mutter logical layout, all at scale 1, each mode 3840×2160. The desktop bounding box is 11520×4320. The two top corners are empty and must show as empty space, not as monitors.

```
(0,0)                         (3840,0) HDMI-2              (7680,0)
+-----------------------------+----------------------------+-----------------------------+
|                             |                            |                             |
|         (no monitor)        |         HDMI-2             |         (no monitor)        |
|                             |                            |                             |
+-----------------------------+----------------------------+-----------------------------+
| DP-4                        | DP-3 (primary)             | DP-5                        | (11520,4320)
| (0,2160)                    | (3840,2160)                | (7680,2160)                 |
+-----------------------------+----------------------------+-----------------------------+
```

## Monitor geometry

Mutter is the source of truth, because spanned mode maps the wallpaper in Mutter's layout space. Avalonia `Screens` is only a fallback.

Read layout by calling the session bus:

- destination `org.gnome.Mutter.DisplayConfig`
- path `/org/gnome/Mutter/DisplayConfig`
- method `org.gnome.Mutter.DisplayConfig.GetCurrentState`
- signature `u a((ssss)a(siiddada{sv})a{sv}) a(iiduba(ssss)a{sv}) a{sv}`

Interpretation:

1. `serial` is ignored, except that a later refresh may call the method again.
2. Each physical monitor has a connector, vendor, product, serial, a list of modes, and a property map. The current mode is the mode whose property `is-current` is true. `display-name` comes from the monitor property map.
3. Each logical monitor has `x`, `y`, `scale`, `transform`, `primary`, and the physical monitors that belong to it. v1 assumes one physical monitor per logical monitor. If a logical monitor lists more than one, use the first and record the others only as a label suffix.
4. Logical size is the current mode's width and height divided by `scale`. For transforms 90, 270, flipped-90, and flipped-270, swap width and height. Other transforms keep the mode's width and height. The wallpaper is painted in layout space, so the image itself is not rotated; only the rectangle's dimensions change.
5. Round logical width and height to the nearest integer (midpoint away from zero).

Reload when Avalonia raises `Screens.Changed`, and once at startup. Match existing assignments by connector name. Drop assignments whose connector disappeared. New connectors start empty.

If the Mutter call fails, build the same model from Avalonia `TopLevel.Screens`: `Bounds` is the rectangle, `Scaling` is the scale, `IsPrimary` and `DisplayName` map directly, and the connector id is `DisplayName` plus the bounds origin. The status line must say that layout came from Avalonia and may not match GNOME.

## Picture library

On startup, scan `$HOME` on a background thread and add thumbnails as files are found. Closing the window cancels the scan.

Include a file when all of these are true:

- the extension is `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.webp`, `.tif`, or `.tiff`, compared case-insensitively
- no ancestor directory has a name starting with `.`
- no ancestor directory is named `node_modules`, `bin`, `obj`, `target`, `__pycache__`, or `venv`
- the file is not reached through a symlink (do not follow symlinks, so a link cycle cannot hang the scan)

Skip directories that cannot be read. Do not fail the scan because one directory is denied.

This excludes `~/.cache`, `~/.local`, and `~/.config` on purpose. Those trees are application data, not the user's pictures. `~/Pictures`, `~/Downloads`, and other visible folders are included.

Sort the grid by last-write time, newest first. When two files share a timestamp, sort by full path.

Thumbnails:

- decode off the UI thread, at most four at a time
- max edge 256 pixels, aspect ratio preserved
- cache the result as JPEG under `~/.cache/wallpaperer/thumbnails/`, keyed by full path, file length, and last-write time in UTC ticks
- the scrolling grid virtualizes by rows: measure how many thumbnails fit in the current width, group the sorted list into rows, and host those rows in a `VirtualizingStackPanel` inside a `ScrollViewer`
- a tooltip shows the file name and parent folder

HEIC and other extensions are ignored in v1.

## Assigning a picture

Dragging a thumbnail onto a monitor assigns that file to that monitor. A later drop replaces the assignment. The drag payload is the absolute file path. The monitor is a drop target; the thumbnail is a drag source. Start the drag only after the pointer moves past a small threshold so a click does not begin a drag.

The same drop handler accepts a file drop from the file manager. Use the first path whose extension is in the supported list. Ignore the drop if none qualify, and say so in the status line.

Clearing a monitor removes its assignment and restores the empty state.

The preview inside a monitor uses cover placement, defined below, so what is inside the rectangle is what that monitor will show after Apply.

## Composing the spanned image

Apply builds one image that covers the bounding box of every logical monitor, including gaps where no monitor sits.

Let `S` be the maximum monitor scale (1.0 on the current machine). Let the logical bounds run from `(minX, minY)` to `(maxX, maxY)`.

- Canvas pixel size is `(round((maxX - minX) * S), round((maxY - minY) * S))`.
- A monitor's destination rectangle, in canvas pixels, is:

```
left   = round((x - minX) * S)
top    = round((y - minY) * S)
right  = round((x + width - minX) * S)
bottom = round((y + height - minY) * S)
```

Rounding each edge this way keeps monitors that share a logical edge sharing a canvas edge.

Fill the canvas with black `#000000` first. Unassigned monitors and the gaps stay black.

For each assigned monitor, paint its image with cover placement:

- scale uniformly so both destination axes are filled
- center the scaled image on the destination rectangle
- clip to the rectangle
- do not stretch, letterbox, or change aspect ratio

Decode one source at a time, draw it, and dispose it before opening the next. The full canvas is one RGBA buffer. On this machine that buffer is 11520×4320, about 200 MB, which is acceptable. Do not keep a second full-size copy.

Write the canvas as JPEG quality 92 to a new file:

`~/.local/share/wallpaperer/span-<yyyyMMdd-HHmmss>.jpg`

Create the directory if needed. After GNOME has been pointed at the new file, delete older `span-*.jpg` files in that directory. If setting the wallpaper fails, keep the new file so the error can be diagnosed, and do not delete the previous span file.

If any assigned file is missing or cannot be decoded, cancel the apply, leave the current wallpaper untouched, and name the file in the status line.

`S = max(scale)` matches the pixel density Mutter uses when it samples a spanned background across monitors that do not share a scale. GNOME then stretches this image uniformly across the logical desktop. Relative placement stays correct, and a higher-scale monitor is not painted from a 1× source. When every scale is 1, the canvas is the framebuffer size in real pixels.

## Setting the wallpaper

The session is using `prefer-dark`, and GNOME shows `picture-uri-dark` in that mode. Set both URI keys so the result does not depend on the color scheme.

Run, in order, and require each exit code to be 0:

```
gsettings set org.gnome.desktop.background picture-uri      'file://<absolute-path-percent-encoded>'
gsettings set org.gnome.desktop.background picture-uri-dark 'file://<absolute-path-percent-encoded>'
gsettings set org.gnome.desktop.background picture-options  'spanned'
```

Use a new path for every apply. GNOME caches wallpaper bytes by URI, so overwriting one path would keep the previous picture on screen.

Do not change the lock screen, the shading colors, or `picture-opacity`.

Apply runs off the UI thread. The button stays disabled until it finishes. On success the status line says the wallpaper was applied and gives the output path. On failure it shows the command's error text.

## Project shape

```
Wallpaperer.sln
src/Wallpaperer/
  Wallpaperer.csproj
  App.axaml
  Views/MainWindow.axaml
  ViewModels/MainWindowViewModel.cs
  Models/MonitorDevice.cs
  Models/PictureItem.cs
  Services/MutterMonitorLayoutSource.cs
  Services/AvaloniaMonitorLayoutSource.cs
  Services/PictureLibrary.cs
  Services/ThumbnailCache.cs
  Services/WallpaperComposer.cs
  Services/GnomeWallpaperApplier.cs
tests/Wallpaperer.Tests/
```

View models call services. Services do not reference Avalonia controls, except `AvaloniaMonitorLayoutSource`, which is the fallback and may read `Screens`. The composer and the Mutter parser are pure and are what the tests cover.

Suggested build order:

1. Solution, window chrome, empty panels, disabled Apply.
2. Mutter layout source and the arrangement panel, including the fallback.
3. Library scan, thumbnail cache, virtualized grid.
4. Drag, drop, clear, and cover previews on the monitors.
5. Composer and GNOME apply.
6. Unit tests alongside the composer and the parser, not after the fact.

## Error handling

| Situation | Behavior |
| --- | --- |
| No monitors | Arrangement shows a short message. Apply stays disabled. |
| Mutter unavailable | Use Avalonia screens. Status line warns that alignment is unverified. |
| Unreadable directory while scanning | Skip it. |
| Drop of an unsupported file | Leave the monitor unchanged. Status line says it was ignored. |
| Assigned file missing at apply time | Do not change GNOME settings. Status line names the file. |
| `gsettings` fails | Do not delete the previous span file. Status line shows stderr. |
| User closes the window mid-scan or mid-apply | Cancel the scan. Let an in-flight apply finish or abandon its temp work without writing settings if encoding has not completed. |

## Tests

Unit tests, no display required:

- Parse a captured `GetCurrentState` fixture that includes this machine's four logical monitors, and produce rectangles `(3840,0,3840,2160)`, `(0,2160,3840,2160)`, `(3840,2160,3840,2160)`, and `(7680,2160,3840,2160)`, with DP-3 primary.
- A monitor with scale 2 and mode 3840×2160 has logical size 1920×1080.
- A 90° transform swaps logical width and height.
- Negative logical origins are normalized so the canvas origin is the top-left of the bounding box.
- Canvas size and destination edges follow the rounding rules above, including two monitors that share an edge.
- Cover math: a wide image overflows left and right equally; a tall image overflows top and bottom equally; the scale is `max(destWidth/srcWidth, destHeight/srcHeight)`.
- Composer: given two monitors and two solid-color sources, the center pixel of each destination rectangle is that monitor's color, and a pixel in the gap is black.

## Acceptance

- Opening the app shows four rectangles in the arrangement drawn above, primary marked, gaps preserved.
- Thumbnails from visible folders under the home directory appear without freezing the window, newest first.
- Dropping a thumbnail on a monitor shows a cover preview in that rectangle. Dropping another replaces it. Clear empties it.
- Apply produces a 11520×4320 JPEG (while scales stay at 1), sets both picture URIs to that file, and sets `picture-options` to `spanned`.
- After apply, each monitor shows the picture that was dropped on it, filling the panel, with no letterboxing. Monitors that were left empty stay black. The previous wallpaper is gone in both light and dark mode.
- A second apply uses a new file and removes the previous `span-*.jpg`.

## Out of scope

- One image spanning a group of monitors.
- Editing the library: folders, exclusions in the UI, ratings, search, albums.
- Saving and loading setups.
- Per-monitor fit choices. v1 is cover only.
- Lock screen, slideshow, and background color picking.
- Packaging, a `.desktop` file, and an installer. Run with `dotnet run`.
