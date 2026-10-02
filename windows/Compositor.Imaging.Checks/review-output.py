"""Independently decode exported files with Pillow; no Skia code is loaded."""
import gzip
import json
from pathlib import Path
import sys
from PIL import Image

fixtures, output = map(Path, sys.argv[1:])
results = []
for case in json.loads((fixtures / 'cases.json').read_text()):
    name = case['file'] + '.png'
    with Image.open(output / name) as image:
        assert image.size == (case['width'], case['height']), name
        pixels = bytearray(image.convert('RGBA').tobytes())
        for i in range(0, len(pixels), 4):
            for channel in range(3):
                pixels[i + channel] = (pixels[i + channel] * pixels[i + 3] + 127) // 255
        expected = gzip.decompress((fixtures / case['expected']).read_bytes())
        assert len(pixels) == len(expected), name
        maximum = max(abs(a - b) for a, b in zip(pixels, expected))
        assert maximum <= case['tolerance'], (name, maximum)
        has_color_metadata = 'icc_profile' in image.info or 'srgb' in image.info
        assert has_color_metadata, name
        results.append(dict(file=name, maximum=maximum, colorMetadataPresent=has_color_metadata))
for name, expected in [('transparent.jpg', (20, 40, 60)), ('half-red.jpg', (138, 20, 30))]:
    with Image.open(output / name) as image:
        assert image.size == (32, 32), name
        maximum = max(abs(a - b) for pixel in image.convert('RGB').getdata() for a, b in zip(pixel, expected))
        assert maximum <= 3, (name, maximum)
        results.append(dict(file=name, maximum=maximum))
report = dict(passed=True, decoder='Pillow', cases=results)
(output / 'pillow-review.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report))
