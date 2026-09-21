from pathlib import Path
from PIL import Image
import numpy as np,json,hashlib,math,shutil
base=Path('/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows11-remote-suite')
run=base/'s02-visible-20260921-190545';rp=run/'results/report.json';r=json.loads(rp.read_text())
assert r['completed'] and r['error'] is None and r['nativeWindow'] and r['windowsExecuted']
assert len(r['trials'])==62 and r['renderScaling']==1.5
assert all(x['correctness']=='settled replay/immutable source/undo/redo exact' for x in r['trials'])
review={k:r[k] for k in ['completed','nativeWindow','windowsExecuted','renderScaling','sampledPrivatePeak']}
review.update({'reportSha256':hashlib.sha256(rp.read_bytes()).hexdigest(),'accepted':False,'reason':'Unobscured Windows native window observed at start/middle; no concurrent Windows tests or file transfer during measured run. Both update P95 values exceed 16.7 ms. Physical presentation is outside this harness.','exitCode':None,'exitCodeNote':'PowerShell process object reported HasExited true but ExitCode was null; completed=true and 62 trial records establish harness completion, not a captured zero exit code.','percentileMethod':'nearest rank ceil(.95*n), excluding warmup and pointer-down','scenarios':[]})
for s in ['empty','existing']:
 t=[x for x in r['trials'] if x['scenario']==s and not x['warmup']];assert len(t)==30
 u=[y['UpdateToCanvasLeaseReleasedMilliseconds'] for x in t for y in x['updates']];assert len(u)==3600
 p95=lambda a:sorted(a)[math.ceil(.95*len(a))-1]
 prev=base/'remote-s02-20260921-183729/s02-window'/(s+'-final.png');now=run/'results'/(s+'-final.png')
 assert np.array_equal(np.array(Image.open(prev).convert('RGBA')),np.array(Image.open(now).convert('RGBA')))
 review['scenarios'].append({'name':s,'measuredTrials':30,'updates':3600,'updateP95Milliseconds':p95(u),'commitP95Milliseconds':p95([x['commitMilliseconds'] for x in t]),'finalPixelsExactWithFirstRun':True})
e=Path('docs/windows/evidence/windows11-remote-suite');(e/'s02-visible-review.json').write_text(json.dumps(review,indent=2)+'\n')
shutil.copyfile(run/'execution.json',e/'s02-visible-execution.json')
for f in ['s02-visible-190545.png','s02-visible-midrun-190545.png']:shutil.copyfile(base/'screenshots'/f,e/f)
a=json.loads((e/'received-archives.json').read_text());p=run.with_suffix('.zip');a.append({'path':str(p),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'crcVerified':True,'remoteHashReadbackPending':False,'remoteHashMatches':True});(e/'received-archives.json').write_text(json.dumps(a,indent=2)+'\n')
print(json.dumps(review,indent=2))
