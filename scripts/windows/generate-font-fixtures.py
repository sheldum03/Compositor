"""Generate original geometric font fixtures, not subsets of third-party fonts.

Requires fonttools==4.59.2. Refuses to overwrite an existing output directory.
"""
import hashlib
import json
import sys
from pathlib import Path

import fontTools
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTCollection

if fontTools.__version__ != "4.59.2":
    raise SystemExit("Use fonttools==4.59.2 for byte-identical fixture generation")
output = Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=False)


def font(family, style="Regular", cff=False, wide=False):
    builder = FontBuilder(1000, isTTF=not cff)
    order = [".notdef", "space", "A", "B"]
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap({32: "space", 65: "A", 66: "B"})
    glyphs = {}
    for name in order:
        pen = T2CharStringPen(800 if wide else 600, None) if cff else TTGlyphPen(None)
        if name != "space":
            # Original triangle and rectangle outlines; these are not production text fonts.
            points = [(50, 0), (300, 700), (550, 0)] if name == "A" else [(50, 0), (50, 600), (500, 600), (500, 0)]
            pen.moveTo(points[0])
            for point in points[1:]:
                pen.lineTo(point)
            pen.closePath()
        glyphs[name] = pen.getCharString() if cff else pen.glyph()
    postscript = family.replace(" ", "") + "-" + style
    if cff:
        builder.setupCFF(postscript, {"FullName": family + " " + style, "FamilyName": family,
                                     "Weight": style}, glyphs, {})
    else:
        builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics({name: (800 if wide else 600, 50 if name != "space" else 0) for name in order})
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable({"familyName": family, "styleName": style,
                           "uniqueFontIdentifier": "CompositorFixture1:" + postscript,
                           "fullName": family + " " + style, "psName": postscript,
                           "version": "Version 1.000", "licenseDescription": "MIT; see accompanying LICENSE.txt"})
    builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200,
                     usWeightClass=700 if style == "Bold" else 400,
                     fsSelection=32 if style == "Bold" else 64)
    builder.setupPost()
    builder.setupMaxp()
    builder.font["head"].created = builder.font["head"].modified = 3800000000
    builder.font["head"].macStyle = 1 if style == "Bold" else 0
    builder.font.recalcTimestamp = False
    return builder.font


font("Compositor Fixture TTF").save(output / "fixture.ttf")
font("Compositor Fixture OTF", cff=True).save(output / "fixture.otf")
font("Compositor Fixture TTF", wide=True).save(output / "conflict.ttf")
collection = TTCollection()
collection.fonts = [font("Compositor Fixture Collection"), font("Compositor Fixture Collection", "Bold", wide=True)]
collection.save(output / "two-faces.ttc")
(output / "duplicate.ttf").write_bytes((output / "fixture.ttf").read_bytes())
(output / "wrong-extension.zip").write_bytes((output / "fixture.ttf").read_bytes())
(output / "empty.otf").write_bytes(b"")
(output / "damaged.ttf").write_bytes(b"This is not a font.\n")
(output / "truncated.ttc").write_bytes((output / "two-faces.ttc").read_bytes()[:16])
(output / "LICENSE.txt").write_bytes(Path(__file__).resolve().parents[2].joinpath("LICENSE").read_bytes())
cases = {
    "fixture.ttf": {"faces": ["CompositorFixtureTTF-Regular"], "result": "import"},
    "fixture.otf": {"faces": ["CompositorFixtureOTF-Regular"], "result": "import"},
    "two-faces.ttc": {"faces": ["CompositorFixtureCollection-Regular", "CompositorFixtureCollection-Bold"], "result": "import"},
    "duplicate.ttf": {"after": "fixture.ttf", "result": "deduplicate"},
    "conflict.ttf": {"after": "fixture.ttf", "result": "registration"},
    "wrong-extension.zip": {"result": "unsupported"},
    "empty.otf": {"result": "empty"},
    "damaged.ttf": {"result": "damaged"},
    "truncated.ttc": {"result": "damaged"},
}
(output / "cases.json").write_text(json.dumps(cases, indent=2) + "\n")
checksums = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(output.iterdir())}
(output / "checksums.json").write_text(json.dumps(checksums, indent=2) + "\n")
print(json.dumps({"output": str(output), "files": len(checksums), "fonttools": fontTools.__version__}))
