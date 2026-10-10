import json, math, sys
from pathlib import Path
p=Path(sys.argv[1]);d=json.loads(p.read_text(encoding='utf-8-sig'))
assert d['completed'] and d['error'] is None, 'Incomplete or failed run'
assert d['diameter']==800 and d['hardness']==0 and d['opacity']==1
n=d['measuredCountPerScenario'];assert len(d['trials'])==2*(n+1)
sequence=[]
for t in d['trials']:
 frames=[t['pointerDown']]+t['updates']; assert len(frames)==121 and len(t['privateBytes'])==121
 sequence.extend(x['Sequence'] for x in frames)
 for f in frames:
  for k in ('AppendMilliseconds','PaintMilliseconds','UpdateToCanvasLeaseReleasedMilliseconds'):
   assert math.isfinite(f[k]) and f[k]>=0
  assert f['UpdateToCanvasLeaseReleasedMilliseconds']>=f['AppendMilliseconds']
  assert f['NativePixelCopyBytes']>0
assert sequence==list(range(1,len(sequence)+1))
def p95(xs):return sorted(xs)[math.ceil(.95*len(xs))-1]
summary=[]
for name in ('empty','existing'):
 all_trials=[t for t in d['trials'] if t['scenario']==name]
 assert [t['trial'] for t in all_trials]==list(range(n+1))
 assert [t['warmup'] for t in all_trials]==[True]+[False]*n
 assert len({t['digest'] for t in all_trials})==1
 measured=all_trials[1:];frames=[f for t in measured for f in t['updates']]
 summary.append({'scenario':name,'measuredStrokes':len(measured),'measuredUpdates':len(frames),
 'appendP95':p95([f['AppendMilliseconds'] for f in frames]),
 'paintP95':p95([f['PaintMilliseconds'] for f in frames]),
 'updateToCanvasLeaseReleaseP95':p95([f['UpdateToCanvasLeaseReleasedMilliseconds'] for f in frames]),
 'commitP95':p95([t['commitMilliseconds'] for t in measured])})
print(json.dumps({'nativeWindow':d['nativeWindow'],'windowsExecuted':d['windowsExecuted'],
 'completedCallbackCount':len(sequence),'sampledPrivatePeak':d['sampledPrivatePeak'],
 'nominalS02TrialCount':n==30,'performanceAccepted':False,'scenarios':summary},indent=2))
