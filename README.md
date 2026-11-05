# Wallpaperer

Version 1.0.0. Copyright © 2026 Pross Co.

Wallpaperer shows your monitors, lets you place a picture on each one, and applies them as a single spanned wallpaper on GNOME.

## Run

```bash
dotnet run --project src/Wallpaperer.csproj
```

## Packages

`packaging/build.sh` publishes a self-contained linux-x64 build, packs it with Velopack, and writes:

- `dist/wallpaperer_amd64.deb`
- `dist/Wallpaperer-x86_64.AppImage`
- `dist/releases.linux.json`
- the Velopack `.nupkg`

Create a [GitHub Release](https://github.com/RossWright/Wallpaperer/releases) and attach those files. The website downloads the stable `.deb` and AppImage names. An installed copy checks that release and shows **Update available** when a newer Velopack package is attached. The files are not committed.

## Site

`docs/` is the GitHub Pages site. Pages deploys from the `main` branch folder `/docs`. Drop screenshots in `docs/screenshots/` and add them to `docs/index.html`.

## Layout

```
src/                  app
tests/Wallpaperer.Tests/
packaging/            desktop entry, icons, package build
docs/                 website
dodalu.json           board
```
