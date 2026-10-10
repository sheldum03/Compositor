from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image,ImageDraw
root=Path(__file__).resolve().parents[1]
run=root/'server-fixed-results'; kit=root/'CompositorHeicProbe'
s=json.loads((run/'summary.json').read_text(encoding='utf-8-sig'))
assert s['completed'] and s['windowsExecuted'] and s['error'] is None
assert len(s['results'])==16 and len(s['invocations'])==23
codes=[x['exitCode'] for x in s['invocations']]
assert codes.count(0)==17 and codes.count(1)==6
rows=[]; sheet=Image.new('RGB',(720,16*112),'#d8d8d8');draw=ImageDraw.Draw(sheet)
for i,result in enumerate(s['results']):
 name=result['case']; report=json.loads((run/name/'decode.json').read_text())
 raw=(run/name/'rgba.bin').read_bytes(); assert hashlib.sha256(raw).hexdigest()==result['rgbaSha256']
 image=Image.frombytes('RGBA',(report['width'],report['height']),raw)
 ref=Image.open(kit/'fixtures'/f'{name}-mac.png').convert('RGBA')
 assert image.size==ref.size
 a=np.asarray(image).astype(np.int32); b=np.asarray(ref).astype(np.int32)
 assert np.array_equal(a[:,:,3],b[:,:,3])
 pa=(a[:,:,:3]*a[:,:,3:4]+127)//255; pb=(b[:,:,:3]*b[:,:,3:4]+127)//255; d=np.abs(pa-pb)
 metrics={'differentPixels':int(np.count_nonzero(np.any(d,axis=2))),'maximumChannelError':int(d.max()),'maximumAlphaError':0,'meanAbsoluteChannelError':float(d.sum()/a.size)}
 assert metrics==result['comparison'],(name,metrics,result['comparison'])
 macraw=Path('/tmp/compositor-heic-run-02')/name/'rgba.bin'; assert raw==macraw.read_bytes()
 assert report['sourceIccBytes']==0 and report['warnings']==0
 image.save(run/f'{name}.png')
 y=i*112; draw.text((8,y+5),name,fill='black')
 for x,im in [(220,ref),(410,image)]:
  im=im.copy(); im.thumbnail((170,96)); sheet.paste(im,(x,y+8),im)
 draw.text((570,y+20),'max RGB: '+str(metrics['maximumChannelError']),fill='black')
 rows.append({'case':name,'rgbaSha256':result['rgbaSha256'],'matchesMacLibheifRaw':True,'metrics':metrics,'decodeMilliseconds':report['readDecodeCopyMilliseconds']})
unicode=list((run/'路径 空格 🧪').rglob('rgba.bin'));assert len(unicode)==1 and unicode[0].read_bytes()==(run/'orientation-6-alpha/rgba.bin').read_bytes()
assert len(s['failureChecks'])==4 and all(x['noOutputPublished'] and x['exitCode']==1 for x in s['failureChecks'])
for inv in s['invocations']:
 log=run/Path(inv['log'].replace('\\','/')).name;assert log.is_file()
review={'host':'tencent-cpu-01','windowsServerExecuted':True,'windows11Executed':False,'powershell':s['powershell'],'samples':16,'nativeInvocations':23,'successes':17,'expectedRejections':6,'allMetricsIndependentlyVerified':True,'allRawEqualMacLibheif':True,'alphaExactVsMacImageIO':True,'maxPremultipliedRgbDifferenceVsMacImageIO':max(r['metrics']['maximumChannelError'] for r in rows),'imageToleranceAccepted':False,'iccHdrValidated':False,'productAccepted':False,'results':rows}
(root/'evidence/server-independent-review.json').write_text(json.dumps(review,indent=2))
sheet.save(root/'evidence/server-contact-sheet.png')
print(json.dumps({k:v for k,v in review.items() if k!='results'},indent=2))
