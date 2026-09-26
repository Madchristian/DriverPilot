"""Regenerate committed icon assets: uv run --with resvg-py --with pillow python tools/build_icon.py."""
from io import BytesIO
from pathlib import Path

from PIL import Image
import resvg_py

ASSETS = Path(__file__).resolve().parents[1] / "Assets"
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def main():
    png = resvg_py.svg_to_bytes(svg_path=str(ASSETS / "DriverPilot.svg"), width=1024, height=1024)
    image = Image.open(BytesIO(png)).convert("RGBA")
    image.resize((512, 512), Image.Resampling.LANCZOS).save(ASSETS / "DriverPilot.png")
    frames = []
    for size in SIZES:
        frame = image.resize((size, size), Image.Resampling.LANCZOS)
        # Remove near-transparent Lanczos ringing outside the rounded silhouette.
        frame.putalpha(frame.getchannel("A").point(lambda a: 0 if a < 3 else a))
        frames.append(frame)
    frames[-1].save(ASSETS / "DriverPilot.ico", format="ICO",
                    sizes=[(n, n) for n in SIZES], append_images=frames[:-1])
    with Image.open(ASSETS / "DriverPilot.ico") as icon:
        assert icon.ico.sizes() == {(n, n) for n in SIZES}
        for size in icon.ico.sizes():
            frame = icon.ico.getimage(size).convert("RGBA")
            assert frame.getpixel((0, 0))[3] == 0, "Icon corners must stay transparent"
            assert frame.getbbox(), "Empty icon frame"
    print("PASS icon frames:", ", ".join(map(str, SIZES)))


if __name__ == "__main__":
    main()
