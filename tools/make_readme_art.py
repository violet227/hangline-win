"""Draws every image in Hangline's README: the hero, the demo loop and the
gesture grid, each in light and dark, in English and Chinese.

Everything is drawn in code, as the original app's README art is, so it can
be redrawn after any change. Run from anywhere:

    python tools/make_readme_art.py

Writes into docs/images (English) and docs/images/zh-CN.
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "images")
FONTS = r"C:\Windows\Fonts"

WORDS = {
    "en": {
        "tagline": "Screenshots, hung out to dry. On Windows.",
        "copied": "Copied",
        "bento": [
            ("Click to copy.", "Paste it anywhere, instantly."),
            ("Hold to mark up.", "Opens in Snipping Tool, saves in place."),
            ("Drag to share.", "Apps get a copy. Folders keep it."),
            ("Let it go.", "The cross or the Recycle Bin. That's it."),
        ],
    },
    "zh-CN": {
        "tagline": "把截图晾在屏幕上方，Windows 版。",
        "copied": "已复制",
        "bento": [
            ("单击复制。", "随处粘贴，即刻可用。"),
            ("按住标注。", "在截图工具里打开，原地保存。"),
            ("拖出分享。", "拖进应用是副本，拖进文件夹就留下。"),
            ("放手即走。", "点叉或扔进回收站，就这么简单。"),
        ],
    },
}


def font(size, weight="regular", lang="en"):
    if lang == "zh-CN":
        name = {"bold": "msyhbd.ttc", "semibold": "msyhbd.ttc", "light": "msyhl.ttc"}.get(weight, "msyh.ttc")
    else:
        name = {"bold": "segoeuib.ttf", "semibold": "seguisb.ttf", "light": "segoeuil.ttf"}.get(weight, "segoeui.ttf")
    return ImageFont.truetype(os.path.join(FONTS, name), int(size))


# MARK: Themes

THEMES = {
    "light": dict(
        page=(255, 255, 255), title=(28, 28, 30), sub=(110, 110, 115),
        desk_a=(214, 240, 238), desk_b=(222, 232, 250), desk_c=(236, 232, 248),
        bar=(246, 248, 250, 235), bar_ink=(120, 124, 132),
        rope=(140, 140, 146), glass=(250, 250, 252, 225), edge=(255, 255, 255, 200),
        shadow=0.20, tile=(244, 244, 247), accent=(32, 150, 170), pill=(246, 246, 248, 245),
    ),
    "dark": dict(
        page=(13, 17, 23), title=(240, 240, 245), sub=(150, 150, 158),
        desk_a=(18, 46, 52), desk_b=(22, 30, 54), desk_c=(34, 26, 52),
        bar=(32, 34, 40, 235), bar_ink=(150, 154, 162),
        rope=(150, 150, 156), glass=(52, 52, 58, 225), edge=(255, 255, 255, 70),
        shadow=0.45, tile=(28, 30, 36), accent=(80, 200, 210), pill=(48, 48, 52, 245),
    ),
}

S = 2  # Everything is drawn at twice its size, for sharp screens.


def lerp(a, b, t):
    return a + (b - a) * t


def lerpc(a, b, t):
    return tuple(int(lerp(a[i], b[i], t)) for i in range(len(a)))


def ease(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


def ease_cubic(x):
    x = max(0.0, min(1.0, x))
    return 4 * x * x * x if x < 0.5 else 1 - (-2 * x + 2) ** 3 / 2


def rounded_mask(size, radius):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius, fill=255)
    return m


def gradient(size, a, b, c):
    """A soft diagonal gradient through three colours."""
    w, h = size
    small = Image.new("RGB", (64, 64))
    px = small.load()
    for y in range(64):
        for x in range(64):
            t = (x / 63) * 0.7 + (y / 63) * 0.3
            px[x, y] = lerpc(a, b, t / 0.5) if t < 0.5 else lerpc(b, c, (t - 0.5) / 0.5)
    return small.resize(size, Image.BICUBIC).convert("RGBA")


# MARK: What hangs: little pictures of screenshots

def content(kind, w, h, theme):
    img = Image.new("RGBA", (w, h))
    d = ImageDraw.Draw(img)
    if kind == 0:
        # A document window.
        d.rectangle([0, 0, w, h], fill=(250, 250, 252))
        d.rectangle([0, 0, w, int(h * 0.13)], fill=(236, 238, 242))
        for i, col in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
            r = h * 0.025
            cx, cy = w * 0.05 + i * r * 3, h * 0.065
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col)
        d.rounded_rectangle([w * 0.08, h * 0.22, w * 0.5, h * 0.29], h * 0.03, fill=(64, 150, 220))
        for i in range(6):
            y = h * 0.38 + i * h * 0.095
            d.rounded_rectangle([w * 0.08, y, w * (0.85 - (i % 3) * 0.12), y + h * 0.04], h * 0.02, fill=(208, 212, 220))
    elif kind == 1:
        # A sunset photo.
        for y in range(h):
            d.line([(0, y), (w, y)], fill=lerpc((255, 190, 120), (240, 100, 120), y / h))
        r = h * 0.14
        d.ellipse([w * 0.52 - r, h * 0.45 - r, w * 0.52 + r, h * 0.45 + r], fill=(255, 238, 180))
        pts = [(0, h)] + [(x, h * 0.66 - math.sin(x / w * math.pi * 1.4) * h * 0.1) for x in range(0, w + 1, 4)] + [(w, h)]
        d.polygon(pts, fill=(30, 70, 110))
    elif kind == 2:
        # A dark chart.
        d.rectangle([0, 0, w, h], fill=(26, 30, 40))
        d.rounded_rectangle([w * 0.07, h * 0.1, w * 0.36, h * 0.15], h * 0.02, fill=(150, 156, 170))
        bars = [0.35, 0.55, 0.42, 0.7, 0.58, 0.88, 0.66]
        bw = w * 0.86 / len(bars)
        for i, v in enumerate(bars):
            x = w * 0.07 + i * bw
            col = (64, 190, 200) if i == 5 else (56, 90, 120)
            d.rounded_rectangle([x + bw * 0.15, h * 0.92 - v * h * 0.68, x + bw * 0.85, h * 0.92], bw * 0.12, fill=col)
    else:
        # A terminal.
        d.rectangle([0, 0, w, h], fill=(18, 22, 28))
        rnd = random.Random(3)
        for i in range(9):
            y = h * 0.1 + i * h * 0.09
            col = [(80, 200, 120), (200, 200, 210), (120, 170, 240)][i % 3]
            d.rounded_rectangle([w * 0.06, y, w * (0.25 + rnd.random() * 0.6), y + h * 0.035], h * 0.015, fill=col)
    return img


def card(kind, w, h, theme, lift=0.0):
    """A photo in its glass frame, with the clip, ready to paste with its
    top centre at the clip's grip."""
    inset, radius = 4 * S, 16 * S
    pad = 40 * S
    clip_h, overlap = 26 * S, 12 * S
    W, H = w + pad * 2, h + pad * 2 + clip_h
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ox, oy = pad, pad + clip_h - overlap
    # Shadow.
    sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle([ox, oy + (5 + 3 * lift) * S, ox + w, oy + h + (5 + 3 * lift) * S], radius,
                                         fill=(0, 0, 0, int(255 * theme["shadow"] * (1 + 0.3 * lift))))
    sh = sh.filter(ImageFilter.GaussianBlur((10 + 4 * lift) * S))
    layer.alpha_composite(sh)
    # Glass.
    g = ImageDraw.Draw(layer)
    g.rounded_rectangle([ox, oy, ox + w, oy + h], radius, fill=theme["glass"])
    photo = content(kind, w - inset * 2, h - inset * 2, theme)
    layer.paste(photo, (ox + inset, oy + inset), rounded_mask(photo.size, radius - inset))
    g.rounded_rectangle([ox, oy, ox + w, oy + h], radius, outline=theme["edge"], width=max(1, int(0.75 * S)))
    # The clip.
    cx = ox + w // 2
    metal = Image.new("RGBA", (9 * S, clip_h))
    md = ImageDraw.Draw(metal)
    for x in range(metal.width):
        t = x / (metal.width - 1)
        stops = [(0, 0.70), (0.35, 0.93), (0.65, 0.82), (1, 0.62)]
        for (a, va), (b, vb) in zip(stops, stops[1:]):
            if a <= t <= b:
                v = lerp(va, vb, (t - a) / (b - a))
        md.line([(x, 0), (x, clip_h)], fill=(int(v * 255),) * 3 + (255,))
    mm = rounded_mask(metal.size, int(3.5 * S))
    csh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(csh).rounded_rectangle([cx - 4.5 * S, pad + 1.5 * S, cx + 4.5 * S, pad + clip_h + 1.5 * S], 3.5 * S, fill=(0, 0, 0, 80))
    layer.alpha_composite(csh.filter(ImageFilter.GaussianBlur(2 * S)))
    layer.paste(metal, (int(cx - 4.5 * S), pad), mm)
    g.rounded_rectangle([cx - 2.5 * S, pad + 8.5 * S, cx + 2.5 * S, pad + 9.9 * S], S, fill=(0, 0, 0, 90))
    return layer, (cx, pad)


def paste_rotated(canvas, layer, anchor, at, angle, alpha=1.0):
    """Pastes the layer so its anchor lands on at, turned about the anchor."""
    rotated = layer.rotate(-angle, resample=Image.BICUBIC, center=anchor)
    if alpha < 1:
        a = rotated.split()[3].point(lambda v: int(v * alpha))
        rotated.putalpha(a)
    canvas.alpha_composite(rotated, (int(at[0] - anchor[0]), int(at[1] - anchor[1])))


def pill(canvas, text, center, theme, lang, alpha=1.0):
    f = font(11.5 * S * 1.15, "semibold", lang)
    tw = f.getlength(text)
    w, h = tw + 34 * S, 26 * S
    x0, y0 = center[0] - w / 2, center[1] - h / 2
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.rounded_rectangle([x0, y0, x0 + w, y0 + h], h / 2, fill=theme["pill"], outline=theme["edge"], width=S)
    ink = theme["title"]
    cx, cy = x0 + 13 * S, center[1]
    d.line([(cx - 4 * S, cy), (cx - 1 * S, cy + 3 * S), (cx + 4 * S, cy - 3.5 * S)], fill=ink, width=int(1.6 * S), joint="curve")
    d.text((x0 + 21 * S, center[1]), text, font=f, fill=ink, anchor="lm")
    if alpha < 1:
        layer.putalpha(layer.split()[3].point(lambda v: int(v * alpha)))
    canvas.alpha_composite(layer)


def cursor(canvas, at, theme):
    x, y = at
    pts = [(0, 0), (0, 17), (4.2, 13), (7.2, 19.5), (9.6, 18.4), (6.6, 12), (12, 12)]
    sc = 1.5 * S
    poly = [(x + px * sc, y + py * sc) for px, py in pts]
    d = ImageDraw.Draw(canvas)
    d.polygon(poly, fill=(20, 20, 22), outline=(255, 255, 255))
    d.line(poly + [poly[0]], fill=(255, 255, 255), width=int(1.2 * S))


# MARK: The scene: a desktop with a taskbar and the line across the top

def scene(size, theme, photos, reveal=1.0, gust=0.0, copied=None, hover=None, cursor_at=None, flying=None, lang="en",
          radius=28, k=1.0, xs=None):
    """photos: list of (kind, aspect, tilt, swing). flying: (kind, aspect, k, tilt) for a capture on its way."""
    W, H = size
    img = gradient(size, theme["desk_a"], theme["desk_b"], theme["desk_c"])
    d = ImageDraw.Draw(img)

    # A few faint windows on the desktop, so it reads as a screen.
    for (x0, y0, x1, y1) in [(0.08, 0.42, 0.42, 0.95), (0.55, 0.36, 0.93, 0.88)]:
        box = [W * x0, H * y0, W * x1, H * y1]
        win = Image.new("RGBA", size, (0, 0, 0, 0))
        wd = ImageDraw.Draw(win)
        wd.rounded_rectangle(box, 12 * S, fill=(255, 255, 255, 60) if theme is THEMES["light"] else (255, 255, 255, 14))
        img.alpha_composite(win)

    # The taskbar along the bottom.
    tk = min(1.0, k * 1.2)
    tb = 44 * S * tk
    bar = Image.new("RGBA", size, (0, 0, 0, 0))
    bd = ImageDraw.Draw(bar)
    bd.rectangle([0, H - tb, W, H], fill=theme["bar"])
    for i in range(6):
        cx = W / 2 + (i - 2.5) * 40 * S * tk
        bd.rounded_rectangle([cx - 11 * S * tk, H - tb / 2 - 11 * S * tk, cx + 11 * S * tk, H - tb / 2 + 11 * S * tk], 5 * S * tk,
                             fill=theme["accent"] + (255,) if i == 0 else theme["bar_ink"] + (110,))
    # The tray, with the shirt.
    sx, sy = W - 120 * S * tk, H - tb / 2
    shirt = [(-4.4, -5.5), (-8.7, -3.4), (-7.4, -0.4), (-5.7, -1.1), (-5.7, 5.6), (5.7, 5.6), (5.7, -1.1),
             (7.4, -0.4), (8.7, -3.4), (4.4, -5.5), (0, -3.6)]
    bd.polygon([(sx + x * 1.3 * S * tk, sy + y * 1.3 * S * tk) for x, y in shirt], outline=theme["title"], width=max(1, int(1.3 * S * tk)))
    bd.text((W - 30 * S * tk, sy), "10:24", font=font(11 * S * tk, "regular"), fill=theme["sub"], anchor="rm")
    img.alpha_composite(bar)

    # The line and what hangs from it, slid up when tucked away.
    panel_h = 240 * S
    line = Image.new("RGBA", (W, panel_h), (0, 0, 0, 0))
    top, sag = 10 * S, min(30 * S, W * 0.018) * (1.6 if k > 1 else 1)

    def rope_y(x):
        f = x / W
        return top + 4 * sag * f * (1 - f)

    ld = ImageDraw.Draw(line)
    pts = [(x, rope_y(x)) for x in range(-20, W + 21, 6)]
    sh = Image.new("RGBA", (W, panel_h), (0, 0, 0, 0))
    ImageDraw.Draw(sh).line([(x, y + 1.2 * S) for x, y in pts], fill=(0, 0, 0, 70), width=int(1.4 * S))
    line.alpha_composite(sh.filter(ImageFilter.GaussianBlur(1.2 * S)))
    ld.line(pts, fill=theme["rope"], width=int(1.2 * S))
    # The rope fades at both ends.
    fade = Image.new("L", (W, panel_h), 255)
    fd = ImageDraw.Draw(fade)
    edge = int(W * 0.08)
    for x in range(edge):
        v = int(255 * x / edge)
        fd.line([(x, 0), (x, panel_h)], fill=v)
        fd.line([(W - 1 - x, 0), (W - 1 - x, panel_h)], fill=v)
    line.putalpha(Image.composite(line.split()[3], Image.new("L", line.size, 0), fade))

    card_w = 150 * S * k
    spacing = card_w + 24 * S * k
    n = len(photos)
    for i, (kind, aspect, tilt, swing) in enumerate(photos):
        x = xs[i] if xs else W / 2 - (n - 1) * spacing / 2 + i * spacing
        maxw, maxh = card_w - 14 * S, 104 * S * k
        pw, ph = (maxw, maxw / aspect) if maxw / aspect <= maxh else (maxh * aspect, maxh)
        w, h = int(pw + 8 * S), int(ph + 8 * S)
        lift = 1.0 if hover == i else 0.0
        layer, anchor = card(kind, w, h, theme, lift)
        if lift:
            layer = layer.resize((int(layer.width * 1.035), int(layer.height * 1.035)), Image.BICUBIC)
            anchor = (anchor[0] * 1.035, anchor[1] * 1.035)
        angle = tilt + swing + gust * (1.6 + i * 0.7) * math.sin(i * 1.7 + 1)
        paste_rotated(line, layer, anchor, (x, rope_y(x) - 9.5 * S), angle)
        if copied is not None and copied[0] == i:
            pill(line, WORDS[lang]["copied"], (x, rope_y(x) - 9.5 * S + 14 * S + h + 16 * S), theme, lang, copied[1])
    slide = (1 - reveal) * (panel_h + 12 * S)
    img.alpha_composite(line, (0, int(-slide)))

    if flying is not None:
        kind, aspect, k, tilt, to_x = flying
        e = ease_cubic(k)
        fw0, fh0 = W * 0.34, W * 0.34 / aspect
        maxw, maxh = card_w - 14 * S, 104 * S * k
        pw, ph = (maxw, maxw / aspect) if maxw / aspect <= maxh else (maxh * aspect, maxh)
        tw, th = pw + 8 * S, ph + 8 * S
        w, h = int(lerp(fw0, tw, e)), int(lerp(fh0, th, e))
        x = lerp(W / 2, to_x, e)
        y = lerp(H * 0.42 - fh0 / 2, rope_y(to_x) - 9.5 * S + 14 * S, e) - math.sin(math.pi * e) * 30 * S
        chrome = ease(max(0, (e - 0.35) / 0.65))
        if chrome > 0.98:
            layer, anchor = card(kind, w, h, theme)
            paste_rotated(img, layer, anchor, (x, y - 14 * S), tilt * e)
        else:
            ph_img = content(kind, w, h, theme)
            layer = Image.new("RGBA", (w + 80 * S, h + 80 * S), (0, 0, 0, 0))
            shl = Image.new("RGBA", layer.size, (0, 0, 0, 0))
            ImageDraw.Draw(shl).rounded_rectangle([40 * S, 45 * S, 40 * S + w, 45 * S + h], 16 * S * e, fill=(0, 0, 0, 70))
            layer.alpha_composite(shl.filter(ImageFilter.GaussianBlur(10 * S)))
            r = max(1, int(16 * S * e))
            layer.paste(ph_img, (40 * S, 40 * S), rounded_mask(ph_img.size, r))
            if chrome > 0:
                framed, anchor = card(kind, w, h, theme)
                framed.putalpha(framed.split()[3].point(lambda v: int(v * chrome)))
                paste_rotated(img, framed, anchor, (x, y - 14 * S), tilt * e)
            paste_rotated(img, layer, (40 * S + w / 2, 40 * S), (x, y), tilt * e, alpha=1 - chrome)

    if cursor_at is not None:
        cursor(img, cursor_at, theme)

    # The screen itself has rounded corners and sits on the page.
    framed = Image.new("RGBA", size, (0, 0, 0, 0))
    framed.paste(img, (0, 0), rounded_mask(size, radius * S))
    return framed


def on_page(theme, page, content_img, offset, shadow=True):
    size = page.size
    if shadow:
        sh = Image.new("RGBA", size, (0, 0, 0, 0))
        w, h = content_img.size
        ImageDraw.Draw(sh).rounded_rectangle([offset[0], offset[1] + 14 * S, offset[0] + w, offset[1] + h + 14 * S],
                                             28 * S, fill=(0, 0, 0, int(255 * theme["shadow"] * 0.6)))
        page.alpha_composite(sh.filter(ImageFilter.GaussianBlur(26 * S)))
    page.alpha_composite(content_img, offset)
    return page


PHOTOS = [(0, 1.45, -1.8, 0), (1, 1.5, 1.2, 0), (2, 1.35, -0.8, 0)]


# MARK: Hero

def hero(theme_name, lang):
    t = THEMES[theme_name]
    W, H = 1200 * S, 640 * S
    page = Image.new("RGBA", (W, H), t["page"] + (255,))
    d = ImageDraw.Draw(page)
    title = "Hangline" if lang == "en" else "Hangline 晾衣绳"
    d.text((W / 2, 84 * S), title, font=font(76 * S, "bold", lang), fill=t["title"], anchor="mm")
    d.text((W / 2, 168 * S), WORDS[lang]["tagline"], font=font(28 * S, "regular", lang), fill=t["sub"], anchor="mm")
    sc = scene((1100 * S, 400 * S), t, PHOTOS, cursor_at=(790 * S, 3 * S), lang=lang, k=1.7)
    page = on_page(t, page, sc, (50 * S, 215 * S))
    page = page.convert("RGB")
    page.save(os.path.join(dir_for(lang), f"hero-{theme_name}.png"), optimize=True)


# MARK: Demo loop

def demo(theme_name, lang):
    """The pointer rests at the top, the line slides down, a capture flies
    in and swings into place, a click copies it, the line tucks away."""
    t = THEMES[theme_name]
    W, H = 600 * S, 200 * S
    K = 0.62
    sp = (150 * K + 24 * K) * S

    def row(n, count):
        return [W / 2 - (count - 1) * sp / 2 + j * sp for j in range(n)]
    size = (W * 2 // S, H * 2 // S)  # drawn at S, saved at half
    frames, durations = [], []
    fps = 25
    total = 6.4
    count = int(total * fps)
    for f in range(count):
        s = f / fps
        photos = [list(p) for p in PHOTOS[:2]]
        reveal = 0.0
        cursor_at = None
        flying = None
        copied = None
        hover = None
        # 0.0-0.6 pointer drifts to the top; 0.6-1.1 line slides down.
        if s < 0.6:
            cursor_at = (lerp(W * 0.62, W * 0.6, ease(s / 0.6)), lerp(H * 0.7, 2 * S, ease(s / 0.6)))
        elif s < 1.1:
            cursor_at = (W * 0.6, 2 * S)
            k = (s - 0.6) / 0.5
            reveal = 1 - (1 - k) ** 3
        else:
            reveal = 1.0
        # 1.4-2.05 a new capture flies in; then it swings.
        n_hung = 2
        if s >= 1.4:
            k = (s - 1.4) / 0.65
            if k < 1:
                to_x = row(3, 3)[2]
                flying = (2, 1.35, k, -0.8, to_x)
                photos = [list(p) for p in PHOTOS[:2]]
                n_hung = 2
            else:
                photos = [list(p) for p in PHOTOS]
                n_hung = 3
                since = s - 2.05
                photos[2][3] = 2.2 * math.exp(-since * 2.4) * math.cos(since * 6.2)
        if 1.1 <= s < 1.4:
            cursor_at = (W * 0.6, lerp(2 * S, H * 0.55, ease((s - 1.1) / 0.3)))
        elif 1.4 <= s < 2.6:
            cursor_at = (W * 0.6, H * 0.55)
        # The two already up slide over to make room as it arrives.
        if s < 1.4:
            xs = row(2, 2)
        elif s < 1.95:
            m = ease((s - 1.4) / 0.55)
            xs = [lerp(a, b, m) for a, b in zip(row(2, 2), row(2, 3))]
        else:
            xs = row(3, 3)
        xs = xs[:len(photos)]
        # 2.6-3.2 pointer moves onto the middle photo; click at 3.4.
        mid_x = W / 2
        if 2.6 <= s < 3.2:
            cursor_at = (lerp(W * 0.6, mid_x + 12 * S, ease((s - 2.6) / 0.6)), lerp(H * 0.55, 52 * S, ease((s - 2.6) / 0.6)))
        elif 3.2 <= s < 4.8:
            cursor_at = (mid_x + 12 * S, 52 * S)
            hover = 1
            if s >= 3.4:
                since = s - 3.4
                a = min(1, since / 0.2) if since < 1.2 else max(0, 1 - (since - 1.2) / 0.2)
                copied = (1, a)
                photos[1][3] = 3 * math.exp(-since * 2.6) * math.cos(since * 6) if since > 0.3 else 3 * ease(since / 0.3)
        # 4.8-5.4 pointer leaves, line tucks away at 5.4.
        if 4.8 <= s < 5.4:
            cursor_at = (lerp(mid_x + 12 * S, W * 0.75, ease((s - 4.8) / 0.6)), lerp(52 * S, H * 0.7, ease((s - 4.8) / 0.6)))
        elif s >= 5.4:
            cursor_at = (W * 0.75, H * 0.7)
            k = min(1, (s - 5.4) / 0.25)
            reveal = 1 - k * k
        gust = 0.0
        if n_hung == 2 and 1.4 <= s < 2.05:
            # Photos slide to make room; approximate by easing their spacing.
            pass
        sc = scene((W, H), t, photos, reveal=reveal, gust=gust, copied=copied, hover=hover,
                   cursor_at=cursor_at, flying=flying, lang=lang, radius=18, k=K, xs=xs)
        frame = Image.new("RGBA", (W, H), t["page"] + (255,))
        frame.alpha_composite(sc)
        frames.append(frame.convert("RGB").resize((W // S, H // S), Image.LANCZOS))
        durations.append(int(1000 / fps))
    durations[-1] = 900
    # One shared palette keeps the gradient smooth and the file small.
    pal = frames[len(frames) // 2].quantize(colors=200, method=Image.Quantize.MEDIANCUT)
    q = [fr.quantize(palette=pal, dither=Image.Dither.NONE) for fr in frames]
    q[0].save(os.path.join(dir_for(lang), f"demo-{theme_name}.gif"), save_all=True, append_images=q[1:],
              duration=durations, loop=0, optimize=True, disposal=1)


# MARK: Bento

ICONS = ["copy", "pen", "arrow", "cross"]


def icon(d, kind, x, y, s, col):
    w = int(4.2 * S)
    if kind == "copy":
        d.rounded_rectangle([x + s * 0.28, y, x + s, y + s * 0.72], s * 0.12, outline=col, width=w)
        d.rounded_rectangle([x, y + s * 0.28, x + s * 0.72, y + s], s * 0.12, outline=col, width=w,
                            fill=None)
    elif kind == "pen":
        d.ellipse([x, y, x + s, y + s], outline=col, width=w)
        cx = x + s / 2
        d.line([(cx - s * 0.17, y + s * 0.78), (cx, y + s * 0.2), (cx + s * 0.17, y + s * 0.78)], fill=col, width=w, joint="curve")
        d.line([(cx - s * 0.1, y + s * 0.56), (cx + s * 0.1, y + s * 0.56)], fill=col, width=w)
    elif kind == "arrow":
        d.rounded_rectangle([x, y, x + s, y + s], s * 0.22, outline=col, width=w)
        d.line([(x + s * 0.3, y + s * 0.7), (x + s * 0.7, y + s * 0.3)], fill=col, width=w)
        d.line([(x + s * 0.42, y + s * 0.3), (x + s * 0.7, y + s * 0.3), (x + s * 0.7, y + s * 0.58)], fill=col, width=w, joint="curve")
    else:
        d.ellipse([x, y, x + s, y + s], outline=col, width=w)
        d.line([(x + s * 0.32, y + s * 0.32), (x + s * 0.68, y + s * 0.68)], fill=col, width=w)
        d.line([(x + s * 0.68, y + s * 0.32), (x + s * 0.32, y + s * 0.68)], fill=col, width=w)


def bento(theme_name, lang):
    t = THEMES[theme_name]
    W, H = 1200 * S, 700 * S
    page = Image.new("RGBA", (W, H), t["page"] + (255,))
    d = ImageDraw.Draw(page)
    gap, m = 20 * S, 20 * S
    tw, th = (W - m * 2 - gap) / 2, (H - m * 2 - gap) / 2
    for i, (head, sub) in enumerate(WORDS[lang]["bento"]):
        x = m + (i % 2) * (tw + gap)
        y = m + (i // 2) * (th + gap)
        d.rounded_rectangle([x, y, x + tw, y + th], 34 * S, fill=t["tile"])
        icon(d, ICONS[i], x + 44 * S, y + 50 * S, 46 * S, t["accent"])
        d.text((x + 44 * S, y + th - 104 * S), head, font=font(36 * S, "bold", lang), fill=t["title"], anchor="ls")
        d.text((x + 44 * S, y + th - 52 * S), sub, font=font(19 * S, "regular", lang), fill=t["sub"], anchor="ls")
    page.convert("RGB").save(os.path.join(dir_for(lang), f"bento-{theme_name}.png"), optimize=True)


def dir_for(lang):
    d = OUT if lang == "en" else os.path.join(OUT, lang)
    os.makedirs(d, exist_ok=True)
    return d


if __name__ == "__main__":
    only = [a for a in sys.argv[1:] if not a.startswith("--")] or ["hero", "bento", "demo"]
    quick = "--quick" in sys.argv
    for lang in (["en"] if quick else WORDS):
        for theme in (["light"] if quick else THEMES):
            if "hero" in only:
                hero(theme, lang)
            if "bento" in only:
                bento(theme, lang)
            if "demo" in only:
                demo(theme, lang)
            print("drew", lang, theme)
