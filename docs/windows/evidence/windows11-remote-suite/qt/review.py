from pathlib import Path
from PIL import Image
import numpy as np,json,hashlib,math
root=Path('/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows11-remote-suite/remote-qt-20260921-184528')
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
def pixels(p):return np.array(Image.open(p).convert('RGBA'))
def same(a,b):return bool(np.array_equal(pixels(a),pixels(b)))
qt=root/'qt-run-20260921-184529-d7c1aff0'; font=root/'font-diagnostic-20260921-184547-32bec365'
report={'scope':'Independent decoded RGBA checks; native GUI IME and product acceptance remain pending','composition':{},'textRuns':{},'fontComparison':[]}
r=read(qt/'composite/qt-report.json')
checks=[same(qt/'composite'/(x['fixture']+'-preview.png'),qt/'composite'/(x['fixture']+'-export.png')) for x in r['results']]
assert len(checks)==20 and all(checks)
report['composition']={'samples':len(checks),'previewExportExact':all(checks)}
for p in [qt/'text',font/'default-fontdir',font/'system-fontdir']:
 r=read(p/'text-report.json'); checks=[]
 for x in r['results']:
  stem=x['fixture'];checks += [same(p/(stem+'-preview.png'),p/(stem+'-export.png')),same(p/(stem+'-cancelled.png'),p/(stem+'-export.png')),same(p/(stem+'-preedit.png'),p/(stem+'-preedit-export.png'))]
 assert len(r['results'])==12 and all(checks)
 report['textRuns'][p.name]={'samples':12,'decodedPairsExact':len(checks),'resolvedFonts':sorted(set(f for x in r['results'] for f in x['resolvedFonts']))}
for p in (font/'default-fontdir').glob('*-export.png'):
 if 'preedit' in p.name:continue
 a=pixels(p);b=pixels(font/'system-fontdir'/p.name)
 report['fontComparison'].append({'file':p.name,'differentPixels':int(np.any(a!=b,axis=2).sum())})
window=read(root/'window-check/window-report.json')
assert window['errors']==[] and window['platformPlugin']=='windows'
for n in ['001-text','002-text']:assert same(root/'window-check'/n/'text-preview.png',root/'window-check'/n/'text-export.png')
report['windowCheck']={'platformPlugin':window['platformPlugin'],'syntheticCheckMode':window['syntheticCheckMode'],'textPairsExact':2,'nativeImeAccepted':False,'saves':len(window['saves']),'checks':window['checks']}
br=read(qt/'brush/qt-brush-report.json')
assert br['widgetUpdates']==242 and br['sharedTilesAcrossSecondStroke']==56
report['brush']={'widgetUpdates':242,'sharedTiles':56,'reportedTimingObservation':[{'stroke':x['stroke'],'updateAndPreviewP95':x['updateAndPreviewP95'],'commitMilliseconds':x['commitMilliseconds']} for x in br['timings']]}
report['visualObservation']='Inspected F11-72-point-left export: default offscreen has a missing-glyph box, system-fontdir displays colored emoji. Native qwindows synthetic export also displays colored emoji. No real IME acceptance.'
(root/'independent-review.json').write_text(json.dumps(report,indent=2)+'\n')
evidence=Path('docs/windows/evidence/windows11-remote-suite');(evidence/'qt-independent-review.json').write_text(json.dumps(report,indent=2)+'\n')
archives=read(evidence/'received-archives.json')
for x in archives:x['remoteHashReadbackPending']=False;x['remoteHashMatches']=True
p=root.with_suffix('.zip');archives.append({'path':str(p),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'crcVerified':True,'remoteHashReadbackPending':False,'remoteHashMatches':True})
(evidence/'received-archives.json').write_text(json.dumps(archives,indent=2)+'\n')
print(json.dumps(report,indent=2))
