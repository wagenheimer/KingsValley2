#!/usr/bin/env python3
"""Generate a 256x256 32-bit BGRA Icon.bmp with a real alpha channel from a PNG master.

Why alpha matters: SDL uses Icon.bmp as the window icon, and on macOS that icon is
propagated to the Dock. An opaque-black BMP therefore paints a black Dock tile even
when the bundle's AppIcon.icns has transparency. This script uses only the Python 3
standard library (zlib + PNG filter reconstruction), so it also works on macOS where
PowerShell/System.Drawing may be unavailable.
"""

import argparse
import struct
import sys
import zlib
from pathlib import Path

ICON_SIZE = 256
RED_MASK = 0x00FF0000
GREEN_MASK = 0x0000FF00
BLUE_MASK = 0x000000FF
ALPHA_MASK = 0xFF000000
DIB_SIZE = 108  # BITMAPV4HEADER: holds all four channel masks.
LCS_SRGB = 0x73524742
PIXELS_PER_METER = 3780


def _png_chunks(data):
    """Yield (type, payload) for every chunk in a PNG byte string."""
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG file")
    pos = 8
    while pos < len(data):
        if pos + 8 > len(data):
            raise ValueError("truncated PNG chunk header")
        length = struct.unpack_from(">I", data, pos)[0]
        kind = data[pos + 4:pos + 8]
        end = pos + 8 + length
        if end + 4 > len(data):
            raise ValueError("truncated PNG chunk data")
        payload = data[pos + 8:end]
        expected_crc = struct.unpack_from(">I", data, end)[0]
        actual_crc = zlib.crc32(kind + payload) & 0xFFFFFFFF
        if expected_crc != actual_crc:
            raise ValueError("bad PNG CRC for chunk %r" % kind)
        yield kind, payload
        pos = end + 4


def read_png_rgba(path):
    """Decode an 8-bit non-interlaced PNG into (width, height, RGBA bytearray)."""
    data = Path(path).read_bytes()
    ihdr = None
    idat = bytearray()
    for kind, payload in _png_chunks(data):
        if kind == b"IHDR":
            ihdr = struct.unpack(">IIBBBBB", payload)
        elif kind == b"IDAT":
            idat.extend(payload)
        elif kind == b"IEND":
            break
    if ihdr is None:
        raise ValueError("PNG has no IHDR chunk")

    width, height, depth, color_type, compression, filtering, interlace = ihdr
    if depth != 8 or color_type not in (0, 2, 4, 6) or compression or filtering or interlace:
        raise ValueError("unsupported PNG format: depth=%d color=%d interlace=%d"
                         % (depth, color_type, interlace))

    channels = {0: 1, 2: 3, 4: 2, 6: 4}[color_type]
    stride = width * channels
    raw = zlib.decompress(bytes(idat))
    if len(raw) != height * (stride + 1):
        raise ValueError("unexpected PNG scanline count")

    rows = []
    previous = bytearray(stride)
    pos = 0
    for _ in range(height):
        filter_type = raw[pos]
        pos += 1
        row = bytearray(raw[pos:pos + stride])
        pos += stride
        if filter_type == 0:
            pass
        elif filter_type == 1:  # Sub
            for i in range(channels, stride):
                row[i] = (row[i] + row[i - channels]) & 0xFF
        elif filter_type == 2:  # Up
            for i in range(stride):
                row[i] = (row[i] + previous[i]) & 0xFF
        elif filter_type == 3:  # Average
            for i in range(stride):
                left = row[i - channels] if i >= channels else 0
                row[i] = (row[i] + ((left + previous[i]) >> 1)) & 0xFF
        elif filter_type == 4:  # Paeth
            for i in range(stride):
                left = row[i - channels] if i >= channels else 0
                up = previous[i]
                upper_left = previous[i - channels] if i >= channels else 0
                predicted_value = left + up - upper_left
                pa = abs(predicted_value - left)
                pb = abs(predicted_value - up)
                pc = abs(predicted_value - upper_left)
                predictor = left if pa <= pb and pa <= pc else (up if pb <= pc else upper_left)
                row[i] = (row[i] + predictor) & 0xFF
        else:
            raise ValueError("unknown PNG filter %d" % filter_type)
        rows.append(row)
        previous = row

    rgba = bytearray(width * height * 4)
    out = 0
    for row in rows:
        for x in range(width):
            i = x * channels
            if color_type == 6:
                r, g, b, a = row[i], row[i + 1], row[i + 2], row[i + 3]
            elif color_type == 2:
                r, g, b, a = row[i], row[i + 1], row[i + 2], 255
            elif color_type == 0:
                r = g = b = row[i]
                a = 255
            else:  # grayscale + alpha
                r = g = b = row[i]
                a = row[i + 1]
            rgba[out:out + 4] = bytes((r, g, b, a))
            out += 4
    return width, height, rgba


def downscale_rgba(width, height, rgba, target_width, target_height):
    """Box-filter with premultiplied alpha so transparent borders stay clean."""
    result = bytearray(target_width * target_height * 4)
    for y in range(target_height):
        y0 = y * height // target_height
        y1 = max(y0 + 1, (y + 1) * height // target_height)
        for x in range(target_width):
            x0 = x * width // target_width
            x1 = max(x0 + 1, (x + 1) * width // target_width)
            red = green = blue = alpha_sum = 0
            for source_y in range(y0, y1):
                row_base = source_y * width * 4
                for source_x in range(x0, x1):
                    p = row_base + source_x * 4
                    alpha = rgba[p + 3]
                    red += rgba[p] * alpha
                    green += rgba[p + 1] * alpha
                    blue += rgba[p + 2] * alpha
                    alpha_sum += alpha
            count = (x1 - x0) * (y1 - y0)
            out = (y * target_width + x) * 4
            if alpha_sum:
                result[out] = (red + alpha_sum // 2) // alpha_sum
                result[out + 1] = (green + alpha_sum // 2) // alpha_sum
                result[out + 2] = (blue + alpha_sum // 2) // alpha_sum
            result[out + 3] = (alpha_sum + count // 2) // count
    return result


def write_bgra_bmp(path, width, height, rgba):
    """Write a bottom-up 32bpp BITMAPV4HEADER BMP with BGRA byte order and four masks."""
    pixel_offset = 14 + DIB_SIZE
    image_size = width * height * 4
    header = bytearray()
    header += b"BM"
    header += struct.pack("<IHHI", pixel_offset + image_size, 0, 0, pixel_offset)
    header += struct.pack("<IiiHHIIiiII", DIB_SIZE, width, height, 1, 32, 3,
                          image_size, PIXELS_PER_METER, PIXELS_PER_METER, 0, 0)
    for mask in (RED_MASK, GREEN_MASK, BLUE_MASK, ALPHA_MASK):
        header += struct.pack("<I", mask)
    header += struct.pack("<I", LCS_SRGB)  # CIEXYZTRIPLE endpoints are ignored for sRGB.
    header += bytes(36)
    header += struct.pack("<III", 0, 0, 0)  # red/green/blue gamma
    if len(header) != pixel_offset:
        raise AssertionError("unexpected BMP header size")

    with open(path, "wb") as output:
        output.write(header)
        row = bytearray(width * 4)
        for y in range(height - 1, -1, -1):
            for x in range(width):
                source = (y * width + x) * 4
                target = x * 4
                row[target] = rgba[source + 2]      # blue
                row[target + 1] = rgba[source + 1]  # green
                row[target + 2] = rgba[source]      # red
                row[target + 3] = rgba[source + 3]  # alpha
            output.write(row)


def main(argv=None):
    repo_root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path,
                        default=repo_root / "src" / "KingsValley.Desktop" / "Icon.png",
                        help="source PNG with alpha (default: src/KingsValley.Desktop/Icon.png)")
    parser.add_argument("--output", type=Path,
                        default=repo_root / "src" / "KingsValley.Desktop" / "Icon.bmp",
                        help="destination BMP (default: src/KingsValley.Desktop/Icon.bmp)")
    args = parser.parse_args(argv)

    width, height, rgba = read_png_rgba(args.source)
    if width < ICON_SIZE or height < ICON_SIZE:
        raise ValueError("source PNG must be at least %dx%d" % (ICON_SIZE, ICON_SIZE))
    scaled = downscale_rgba(width, height, rgba, ICON_SIZE, ICON_SIZE)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    write_bgra_bmp(args.output, ICON_SIZE, ICON_SIZE, scaled)
    print("Wrote %s (%dx%d, 32bpp BGRA, alpha mask 0x%08X)"
          % (args.output, ICON_SIZE, ICON_SIZE, ALPHA_MASK))
    return 0


if __name__ == "__main__":
    sys.exit(main())
