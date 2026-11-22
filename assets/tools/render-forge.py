"""Rasterize the original vector painting with deterministic paper grain (PySide6, Pillow)."""
from pathlib import Path
from random import Random
from io import BytesIO
from PIL import Image
from PySide6.QtCore import QByteArray, QBuffer, QIODevice, Qt
from PySide6.QtGui import QImage, QPainter
from PySide6.QtSvg import QSvgRenderer

here = Path(__file__).resolve().parent
source = QSvgRenderer(str(here / 'ember-transfer.svg'))
if not source.isValid():
    raise ValueError('Invalid ember-transfer.svg')
canvas = QImage(1024, 676, QImage.Format.Format_ARGB32_Premultiplied)
canvas.fill(Qt.GlobalColor.transparent)
painter = QPainter(canvas)
painter.setRenderHint(QPainter.RenderHint.Antialiasing)
source.render(painter)
painter.end()
data = QByteArray()
buffer = QBuffer(data)
buffer.open(QIODevice.OpenModeFlag.WriteOnly)
canvas.save(buffer, 'PNG')
painting = Image.open(BytesIO(data.data())).convert('RGBA').resize((256, 169), Image.Resampling.LANCZOS)
rng = Random(1728)
pixels = painting.load()
for y in range(painting.height):
    for x in range(painting.width):
        r, g, b, a = pixels[x, y]
        delta = round(rng.gauss(0, 3))
        pixels[x, y] = (r + delta, g + delta, b + delta, a)
painting.save(here.parent / 'icons/forge_rest_site_big.png')
painting.resize((128, 85), Image.Resampling.LANCZOS).save(here.parent / 'icons/forge_rest_site.png')
