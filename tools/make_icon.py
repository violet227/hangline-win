"""Draws Hangline's icon: two photos clipped to a sagging line on a teal
tile. Its own artwork, not the original app's. Writes a multi-size .ico."""
import sys
from PIL import Image, ImageDraw, ImageFilter

S = 1024

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))

def tile():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    top, bottom = (64, 200, 190, 255), (32, 110, 180, 255)
    d = ImageDraw.Draw(grad)
    for y in range(S):
        d.line([(0, y), (S, y)], fill=lerp(top, bottom, y / S))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], 220, fill=255)
    img.paste(grad, (0, 0), mask)
    return img

def card(img, cx, top, w, h, angle, kind):
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x0, y0 = cx - w / 2, top
    d.rounded_rectangle([x0, y0, x0 + w, y0 + h], 46, fill=(250, 250, 252, 255))
    inset = 22
    px0, py0, px1, py1 = x0 + inset, y0 + inset, x0 + w - inset, y0 + h - inset
    if kind == "sun":
        photo = Image.new("RGBA", (int(px1 - px0), int(py1 - py0)))
        pd = ImageDraw.Draw(photo)
        for y in range(photo.height):
            pd.line([(0, y), (photo.width, y)], fill=lerp((255, 196, 120, 255), (250, 120, 110, 255), y / photo.height))
        pd.ellipse([photo.width * 0.55, photo.height * 0.2, photo.width * 0.85, photo.height * 0.5], fill=(255, 245, 200, 255))
        pd.polygon([(0, photo.height), (0, photo.height * 0.7), (photo.width * 0.4, photo.height * 0.5),
                    (photo.width, photo.height * 0.78), (photo.width, photo.height)], fill=(40, 70, 120, 255))
        m = Image.new("L", photo.size, 0)
        ImageDraw.Draw(m).rounded_rectangle([0, 0, photo.width - 1, photo.height - 1], 26, fill=255)
        layer.paste(photo, (int(px0), int(py0)), m)
    else:
        d.rounded_rectangle([px0, py0, px1, py1], 26, fill=(236, 240, 246, 255))
        d.rounded_rectangle([px0 + 24, py0 + 30, px0 + (px1 - px0) * 0.6, py0 + 58], 14, fill=(64, 150, 220, 255))
        for i in range(4):
            yy = py0 + 96 + i * 44
            d.rounded_rectangle([px0 + 24, yy, px1 - 24 - (i % 2) * 60, yy + 18], 9, fill=(190, 198, 210, 255))
    # The clip.
    d.rounded_rectangle([cx - 18, top - 52, cx + 18, top + 48], 12, fill=(214, 216, 220, 255), outline=(150, 152, 158, 255), width=3)
    layer = layer.rotate(angle, center=(cx, top - 20), resample=Image.BICUBIC)
    shadow = layer.split()[3].point(lambda a: a * 0.35).filter(ImageFilter.GaussianBlur(18))
    sh = Image.new("RGBA", (S, S), (0, 20, 40, 0))
    sh.putalpha(shadow)
    img.alpha_composite(sh, (0, 18))
    img.alpha_composite(layer)

def main(out):
    img = tile()
    d = ImageDraw.Draw(img)
    # The sagging line.
    pts = []
    for i in range(101):
        t = i / 100
        x = 60 + (S - 120) * t
        y = 300 + 4 * 70 * t * (1 - t)
        pts.append((x, y))
    d.line(pts, fill=(235, 245, 248, 255), width=10, joint="curve")
    card(img, 330, 330, 300, 380, 5, "doc")
    card(img, 700, 330, 330, 270, -4, "sun")
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    img.save(out, sizes=[(s, s) for s in sizes])
    img.resize((256, 256), Image.LANCZOS).save(out.replace(".ico", ".png"))

if __name__ == "__main__":
    main(sys.argv[1])
