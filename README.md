# Wallpaperer

Version 0.1.0. Copyright © 2026 Pross Co.

Wallpaperer shows your monitors, lets you place a picture on each one, and applies them as a single spanned wallpaper on GNOME.

## Run

```bash
dotnet run --project src/Wallpaperer.csproj
```

## Packages

`packaging/build.sh` publishes a self-contained linux-x64 build and writes versioned files plus two stable names:

- `dist/wallpaperer_amd64.deb`
- `dist/Wallpaperer-x86_64.AppImage`

Create a [GitHub Release](https://github.com/RossWright/Wallpaperer/releases) and attach those two files. The website downloads them from `/releases/latest/download/`. They are not committed.

## Site

`docs/` is the GitHub Pages site. In the repository settings, set Pages to deploy from the `master` branch folder `/docs`. Drop screenshots in `docs/screenshots/` and add them to `docs/index.html`.

## Layout

```
src/                  app
tests/Wallpaperer.Tests/
packaging/            desktop entry, icons, package build
docs/                 website
dodalu.json           board
```
