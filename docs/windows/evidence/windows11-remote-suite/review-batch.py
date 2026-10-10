from pathlib import Path
import json,zipfile,hashlib,collections
import numpy as np
from PIL import Image
art=Path('/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows11-remote-suite')
base=art/'remote-batch-20260921-183510';repo=Path('/Users/admin/.codex/worktrees/192b/Compositor')
dec=lambda b:b.decode('utf-16' if b.startswith(b'\xff\xfe') else 'utf-8-sig')
load=lambda p:json.loads(dec(p.read_bytes()))
sha=lambda b:hashlib.sha256(b).hexdigest()
selection=[json.loads(x) for x in dec((base/'selection.log').read_bytes()).splitlines() if x.startswith('{')]
assert len(selection)==48 and len({(x['suffix'],x['zoom'],x['flipped'],x['angle'],x['reverse']) for x in selection})==48
assert all(all(x[k] for k in ['selected','replaced','undone','redone']) and x['changed']>0 for x in selection)
counts={}
for folder,expected in [('composition',20),('text',12)]:
 files=list((base/folder).glob('*-preview.png'));assert len(files)==expected
 for p in files:
  a=np.array(Image.open(p).convert('RGBA'));b=np.array(Image.open(p.with_name(p.name.replace('-preview','-export'))).convert('RGBA'));assert np.array_equal(a,b),p
 counts[folder+'PreviewExportExact']=len(files)
for p in (base/'text').glob('*-cancelled.png'):
 assert np.array_equal(np.array(Image.open(p).convert('RGBA')),np.array(Image.open(p.with_name(p.name.replace('-cancelled','-export'))).convert('RGBA')))
counts['textCancelExact']=12
report={'scope':'Windows 11 independent raw review; preparation evidence, no product acceptance','selectionCasesPassed':48,**counts}
for path in base.glob('*.zip'):
 with zipfile.ZipFile(path) as z:
  assert z.testzip() is None
  summary=json.loads(dec(z.read('summary.json')))
  if path.name.startswith('ai-'):
   if not summary['completed']:
    assert not summary['invocations'];report['aiDownload']={'passed':False,**summary['download'],'error':summary['error']};continue
   assert len(summary['invocations'])==8
   masks=[n for n in z.namelist() if n.endswith('mask.f32')];assert len(masks)==2
   raw=z.read(masks[0]);assert raw==z.read(masks[1]) and len(raw)==409600
   values=np.frombuffer(raw,dtype='<f4');assert np.isfinite(values).all() and values.min()>=0 and values.max()<=1
   providers={}
   for n in z.namelist():
    if 'cpu-profile' in n and (n.startswith('native\\') or n.startswith('推理')):
     data=json.loads(z.read(n));counts=collections.Counter(e['args']['provider'] for e in data if e.get('cat')=='Node' and e.get('args',{}).get('provider'))
     assert set(counts)=={'CPUExecutionProvider'} and counts['CPUExecutionProvider']==1460;providers[n]=dict(counts)
   assert sha(raw)==summary['native']['rawSha256']
   report['aiLocalModel']={'passed':True,'invocations':8,'maskSha256':sha(raw),'nativeUnicodeExact':True,'providers':providers,'milliseconds':summary['native']['inference']['inferenceMilliseconds'],'modelQualityAccepted':False}
  else:
   assert summary['completed'] and len(summary['results'])==16 and len(summary['invocations'])==23
   for item in summary['results']:
    raw=z.read(item['case']+'\\rgba.bin');assert sha(raw)==item['rgbaSha256']
    shape=(item['decode']['height'],item['decode']['width'],4)
    a=np.frombuffer(raw,dtype=np.uint8).reshape(shape).astype(np.int32);b=np.array(Image.open(repo/'docs/windows/fixtures/heic'/(item['case']+'-mac.png')).convert('RGBA')).astype(np.int32)
    a[:,:,:3]=(a[:,:,:3]*a[:,:,3:4]+127)//255;b[:,:,:3]=(b[:,:,:3]*b[:,:,3:4]+127)//255
    diff=np.abs(a-b);assert diff.max()==item['comparison']['maximumChannelError'] and diff[:,:,3].max()==0
    assert np.count_nonzero(diff.max(axis=2))==item['comparison']['differentPixels'] and float(diff.mean())==item['comparison']['meanAbsoluteChannelError']
    assert raw==(repo/'docs/windows/evidence/heic-windows-server/original-results'/item['case']/'rgba.bin').read_bytes()
   report['heic']={'passed':True,'samples':16,'invocations':23,'maximumMacPremultipliedRgbError':1,'alphaExact':True,'toleranceAccepted':False,'allRawEqualVerifiedServerOutput':True,'allReportedMetricsRecomputed':True}
old=art.parent/'text-grayscale-windows11/results'
oldFiles=list(old.glob('*.png'));assert len(oldFiles)==60 and all(p.read_bytes()==(base/'text'/p.name).read_bytes() for p in oldFiles)
report['all60TextPngsEqualPreviousVerifiedWindowsRepair']=True
(base/'independent-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print(json.dumps(report,ensure_ascii=False,indent=2))
