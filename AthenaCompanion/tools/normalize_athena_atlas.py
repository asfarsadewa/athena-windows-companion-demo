#!/usr/bin/env python3
"""Normalize Athena's generated sprite layout into fixed atlas cells."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="Transparent generated layout PNG.")
    parser.add_argument("--out", required=True, help="Final fixed-cell atlas PNG.")
    parser.add_argument("--preview", help="Optional preview PNG with a dark background and grid.")
    parser.add_argument("--cell-size", type=int, default=256)
    parser.add_argument("--columns", type=int, default=10)
    parser.add_argument("--rows", type=int, default=4)
    parser.add_argument("--walk-frames", type=int, default=30)
    parser.add_argument("--pose-frames", type=int, default=9)
    parser.add_argument("--alpha-threshold", type=int, default=16)
    parser.add_argument("--padding", type=int, default=6)
    return parser.parse_args()


def find_bands(axis_len: int, has_content) -> list[tuple[int, int]]:
    bands: list[tuple[int, int]] = []
    in_band = False
    start = 0

    for index in range(axis_len):
        if has_content(index) and not in_band:
            start = index
            in_band = True
        elif not has_content(index) and in_band:
            bands.append((start, index - 1))
            in_band = False

    if in_band:
        bands.append((start, axis_len - 1))

    return bands


def extract_frames(source: Image.Image, alpha_threshold: int) -> list[Image.Image]:
    alpha = source.getchannel("A")

    row_bands = find_bands(
        source.height,
        lambda y: any(alpha.getpixel((x, y)) > alpha_threshold for x in range(source.width)),
    )

    frames: list[Image.Image] = []
    for y0, y1 in row_bands:
        col_bands = find_bands(
            source.width,
            lambda x, y0=y0, y1=y1: any(
                alpha.getpixel((x, y)) > alpha_threshold for y in range(y0, y1 + 1)
            ),
        )

        for x0, x1 in col_bands:
            slot = source.crop((x0, y0, x1 + 1, y1 + 1))
            mask = slot.getchannel("A").point(
                lambda value: 255 if value > alpha_threshold else 0
            )
            bbox = mask.getbbox()
            if bbox is not None:
                frames.append(slot.crop(bbox))

    return frames


def normalize_frames(
    frames: list[Image.Image],
    *,
    columns: int,
    rows: int,
    cell_size: int,
    padding: int,
) -> Image.Image:
    content_max = cell_size - padding * 2
    max_width = max(frame.width for frame in frames)
    max_height = max(frame.height for frame in frames)
    scale = min(content_max / max_width, content_max / max_height, 1.0)

    atlas = Image.new("RGBA", (columns * cell_size, rows * cell_size), (0, 0, 0, 0))

    for index, frame in enumerate(frames):
        if scale < 1.0:
            frame = frame.resize(
                (
                    max(1, round(frame.width * scale)),
                    max(1, round(frame.height * scale)),
                ),
                Image.Resampling.LANCZOS,
            )

        column = index % columns
        row = index // columns
        x = column * cell_size + (cell_size - frame.width) // 2
        y = row * cell_size + cell_size - frame.height - padding
        atlas.alpha_composite(frame, (x, y))

    return atlas


def write_preview(atlas: Image.Image, path: Path, cell_size: int) -> None:
    preview = Image.new("RGBA", atlas.size, (24, 24, 24, 255))
    preview.alpha_composite(atlas)
    draw = ImageDraw.Draw(preview)

    for x in range(0, preview.width + 1, cell_size):
        draw.line((x, 0, x, preview.height), fill=(255, 255, 255, 70), width=1)
    for y in range(0, preview.height + 1, cell_size):
        draw.line((0, y, preview.width, y), fill=(255, 255, 255, 70), width=1)

    path.parent.mkdir(parents=True, exist_ok=True)
    preview.save(path)


def main() -> None:
    args = parse_args()
    source = Image.open(args.input).convert("RGBA")
    extracted = extract_frames(source, args.alpha_threshold)
    expected = args.walk_frames + args.pose_frames

    if len(extracted) < expected:
        raise SystemExit(
            f"Expected at least {expected} frames, but only extracted {len(extracted)}."
        )

    selected = extracted[:expected]
    atlas = normalize_frames(
        selected,
        columns=args.columns,
        rows=args.rows,
        cell_size=args.cell_size,
        padding=args.padding,
    )

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(out)

    if args.preview:
        write_preview(atlas, Path(args.preview), args.cell_size)

    print(
        f"extracted={len(extracted)} selected={len(selected)} "
        f"output={out} size={atlas.size}"
    )


if __name__ == "__main__":
    main()
