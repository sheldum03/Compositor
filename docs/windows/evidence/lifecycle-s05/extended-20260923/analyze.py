"""Independent resource observations and exact decoded PNG checks; no acceptance threshold."""
import hashlib,json,sys
from pathlib import Path
from PIL import Image
run=Path(sys.argv[1]); baseline=Path(sys.argv[2]); output=Path(sys.argv[3])
assert not output.exists(), 'Refusing to replace a review'
r=json.loads((run/'report.json').read_text(encoding='utf-8-sig'))
assert r['completed'] and r['error'] is None and len(r['rounds'])==27
sha=lambda b:hashlib.sha256(b).hexdigest()
with Image.open(baseline) as im:
 assert im.size==(4000,4000)
 expected=sha(im.convert('RGBA').tobytes())
pixels=[]
for i in range(27):
 for name in ['final.png','reopened.png']:
  p=run/f'round-{i}'/name
  with Image.open(p) as im:
   assert im.size==(4000,4000)
   actual=sha(im.convert('RGBA').tobytes())
  assert actual==expected, 'RGBA differs: '+str(p)
  pixels.append({'path':str(p.relative_to(run)),'width':4000,'height':4000,'rgbaSha256':actual})
samples=[r['baseline']]
for row in r['rounds']:
 samples.extend(t['resources'] for t in r['trials'] if t['round']==row['round'])
 samples.extend([row['beforeClose'],row['afterClose']])
samples.extend(s['Resources'] for s in r['idleSamples'][1:])
post=[v['afterClose'] for v in r['rounds']]
cohorts=[]
for start in [0,9,18]:
 rows=post[start:start+9]
 cohorts.append({'rounds':[start+1,start+9],**{key:[v[key] for v in rows] for key in ['PrivateBytes','ManagedBytes','LastGcCommittedBytes','LastGcHeapSizeBytes','Gen2Collections','Handles']}})
private=[s['PrivateBytes'] for s in samples if s['PrivateBytes'] is not None]
result={'windowsExecuted':r['windowsExecuted'],'nativeWindow':r['nativeWindow'],'scenario':r['scenario'],'elapsedSeconds':r['elapsedMilliseconds']/1000,'totalAllocatedBytesEstimate':samples[-1]['TotalAllocatedBytesEstimate']-samples[0]['TotalAllocatedBytesEstimate'],'sampledPrivatePeakBytes':max(private) if private else None,'resourceAccepted':False,'cohorts':cohorts,'retainedDocumentsByRound':[v['retainedDocuments'] for v in r['rounds']],'idleSamples':r['idleSamples'],'finalDiagnosticCollection':r['rounds'][-1]['afterDiagnosticCollection'],'decodedPixels':pixels,'limitations':'GC snapshots and process samples are not simultaneous; their subtraction is not native-memory ownership. Diagnostic frame records remain retained. A finite run does not establish a universal bound or physical-input acceptance.'}
output.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'output':str(output),'exactImages':len(pixels),'elapsedSeconds':result['elapsedSeconds'],'sampledPrivatePeakBytes':result['sampledPrivatePeakBytes'],'resourceAccepted':False}))
