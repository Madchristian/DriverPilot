# DriverPilot icon

Original app artwork: blue hardware chip with mint checkmark on a rounded dark-navy tile (selected design 02).

- `DriverPilot.svg`: editable vector source, no external images or fonts.
- `DriverPilot.png`: 512 × 512 RGBA preview/header artwork.
- `DriverPilot.ico`: embedded Windows icon with 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel RGBA frames.

Regenerate from the repository root:

```sh
uv run --with resvg-py --with pillow python tools/build_icon.py
```

The application build consumes committed assets and does not need Python or SVG rendering dependencies. Do not change only one generated file: regenerate both PNG and ICO from the SVG.
