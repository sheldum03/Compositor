from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np,json
b=Path('/Users/admin/.codex/visualizations/2026/09/20/01a0beb1-da38-7b00-9183-d65bff121d78/qt-windows-cross-build')
r=b/'server-results';f=b/'CompositorQtProbe/fixtures'
def rgba(p): return np.asarray(Image.open(p).convert('RGBA')).astype(np.int32)
def premul(p):
 a=rgba(p);a[:,:,:3]=(a[:,:,:3]*a[:,:,3:4]+127)//255;return a

def metrics(a,c):
 d=np.abs(a-c);m=d.max(axis=2)
 return {'DifferentPixels':int(np.count_nonzero(m)),'MaximumChannelError':int(d.max()),'MaximumAlphaError':int(d[:,:,3].max()),'MeanAbsoluteChannelError':float(d.mean()),'PixelsWithErrorAbove1':int(np.count_nonzero(m>1))}
def ink(p):
 a=premul(p);mask=((a[:,:,2]-a[:,:,0])*255>20*a[:,:,3])&((a[:,:,2]-a[:,:,1])*255>10*a[:,:,3]);return float(a[:,:,3][mask].sum()/255)
verified=[];ink_results=[];matching=[]
for kind,file in [('composite','qt-report.json'),('text','text-report.json')]:
 report=json.loads((r/kind/file).read_text())
 for item in report['results']:
  n=item['fixture'];root=f if kind=='composite' else f/'extended'
  actual=metrics(premul(r/kind/(n+'-export.png')),premul(root/(n+'-mac.png')))
  assert actual==item['macReference'],(n,actual,item['macReference'])
  assert np.array_equal(rgba(r/kind/(n+'-export.png')),rgba(r/kind/(n+'-preview.png')))
  verified.append({'fixture':n,'macReference':actual,'previewExportExactRGBA':True})
  if kind=='text':
   expected=ink(root/(n+'-mac.png'));value=ink(r/kind/(n+'-export.png'))
   ink_results.append({'fixture':n,'ratio':value/expected,'ordinaryInk':value,'referenceInk':expected,'catastrophicLossGuardPassed':value/expected>=.5})
   assert np.array_equal(rgba(r/kind/(n+'-cancelled.png')),rgba(r/kind/(n+'-export.png')))
  local=Path('/tmp/compositor-qt-compositor-03' if kind=='composite' else '/tmp/compositor-qt-text-08')/(n+'-export.png')
  if local.exists(): matching.append({'fixture':n,'macQtPixelsEqual':bool(np.array_equal(rgba(local),rgba(r/kind/(n+'-export.png'))))})
report=json.loads((r/'brush/qt-brush-report.json').read_text())
for item in report['comparisons']:
 n=item['stage']
 for label,ref in [('cpuReference','cpu'),('metalReference','metal')]:
  candidates=list((f/'brush').glob('*'+n+'*'+ref+'*.png'))+list((f/'brush').glob('*'+ref+'*'+n+'*.png'))
  if not candidates:
   print('brush files',sorted(p.name for p in (f/'brush').glob('*.png')));continue
  actual=metrics(premul(r/'brush'/(n+'.png')),premul(candidates[0]));assert actual==item[label],(n,label,actual,item[label]);verified.append({'fixture':'brush-'+n+'-'+ref,**actual})
for n in ('first','final'):
 matching.append({'fixture':'brush-'+n,'macQtPixelsEqual':bool(np.array_equal(rgba('/tmp/compositor-qt-brush-02/'+n+'.png'),rgba(r/'brush'/(n+'.png'))))})
summary={'windowsExecuted':True,'node':'tencent-cpu-01 Windows Server2022','referenceComparisonCount':len(verified),'independentComparisons':verified,'ordinaryTextInk':ink_results,'macQtComparisons':matching,'brushTimings':[{'stroke':x['stroke'],'updateAndPreviewP95':x['updateAndPreviewP95'],'commitMilliseconds':x['commitMilliseconds']} for x in report['timings']],'acceptance':False}
(b/'evidence/server-independent-review.json').write_text(json.dumps(summary,indent=2))
canvas=Image.new('RGB',(1500,1220),'#eeeeee');draw=ImageDraw.Draw(canvas)
names=['F11-72-point-right','F11-300-box-center']
for row,n in enumerate(names):
 for col,(label,p) in enumerate([('Mac reference',f/'extended'/(n+'-mac.png')),('Mac Qt',Path('/tmp/compositor-qt-text-08')/(n+'-export.png')),('Windows Server Qt',r/'text'/(n+'-export.png'))]):
  im=Image.open(p).convert('RGBA');im.thumbnail((470,555))
  bg=Image.new('RGBA',im.size,'white');bg.alpha_composite(im)
  canvas.paste(bg.convert('RGB'),(col*500+15,row*610+50));draw.text((col*500+15,row*610+10),label+' / '+n,fill='black')
canvas.save(b/'evidence/server-text-contact.png')
print(json.dumps({'independentReferences':len(verified),'inkRange':[min(x['ratio'] for x in ink_results),max(x['ratio'] for x in ink_results)],'all12InkPassed':all(x['catastrophicLossGuardPassed'] for x in ink_results),'macQtEqual':sum(x['macQtPixelsEqual'] for x in matching),'macQtCompared':len(matching),'brushTimings':summary['brushTimings']},indent=2))
