"""Génère l'icône de l'application (src/InfraVault.Presentation/Resources/infravault.ico).

Dessin procédural du cadran de coffre, sans dépendance externe : python scripts/make-icon.py
"""
import math
import os
import struct
import zlib

BG = (14, 20, 18)
ACCENT = (233, 165, 60)
SS = 4  # suréchantillonnage pour l'anticrénelage


def coverage(size, x, y):
    """Renvoie (alpha_fond, alpha_accent) pour le point (x, y) en coordonnées 0..1."""
    cx = cy = 0.5
    dx, dy = x - cx, y - cy
    d = math.hypot(dx, dy)

    # Fond : carré arrondi.
    r = 0.22
    qx, qy = abs(dx) - (0.5 - r), abs(dy) - (0.5 - r)
    outside = math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - r
    bg = 1.0 if outside <= 0 else 0.0

    accent = 0.0
    if 0.300 <= d <= 0.360:  # anneau extérieur
        accent = 1.0
    if d <= 0.105:  # moyeu
        accent = 1.0
    if size >= 32 and 0.190 <= d <= 0.222:  # graduations
        angle = math.degrees(math.atan2(dy, dx)) % 30
        if angle < 11:
            accent = 1.0
    if abs(dx) <= 0.030 and -0.36 <= dy <= -0.16:  # repère à midi
        accent = 1.0
    return bg, accent * bg


def render(size):
    rows = []
    for py in range(size):
        row = bytearray([0])
        for px in range(size):
            bg_sum = ac_sum = 0.0
            for sy in range(SS):
                for sx in range(SS):
                    b, a = coverage(size, (px + (sx + 0.5) / SS) / size, (py + (sy + 0.5) / SS) / size)
                    bg_sum += b
                    ac_sum += a
            n = SS * SS
            alpha = bg_sum / n
            mix = ac_sum / bg_sum if bg_sum else 0.0
            rgb = [round(BG[i] * (1 - mix) + ACCENT[i] * mix) for i in range(3)]
            row.extend(rgb + [round(alpha * 255)])
        rows.append(bytes(row))
    return b"".join(rows)


def png(size):
    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(render(size), 9)) + chunk(b"IEND", b""))


def main():
    sizes = [16, 24, 32, 48, 64, 256]
    images = [png(s) for s in sizes]
    header = struct.pack("<HHH", 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    entries = b""
    for size, data in zip(sizes, images):
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    target = os.path.join(os.path.dirname(__file__), "..", "src", "InfraVault.Presentation", "Resources", "infravault.ico")
    with open(target, "wb") as f:
        f.write(header + entries + b"".join(images))
    print("écrit :", os.path.normpath(target), sum(map(len, images)), "octets")


if __name__ == "__main__":
    main()
