import hashlib,json,os,platform,shutil,subprocess,sys,time,zipfile
from pathlib import Path
if sys.platform!='win32':raise SystemExit('Actual Windows required')
k=Path(__file__).resolve().parent;root=k.parent;m=json.loads((k/'manifest.json').read_text());sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
prior=json.loads((root/'s05-extended-20260923-180340/identity.json').read_text())
assert prior['nativeExit'] is not None and prior['reviewExit'] is not None, 'Wait for the existing native long run to finish'
for n,h in m['payload'].items():assert sha(k/n)==h,'Payload changed: '+n
base=root/'s02-lifecycle-r9-app';assert sha(base/'SkiaSharp.dll')==m['skiaSharpSha256'],'SkiaSharp changed'
out=root/('s05-core-'+time.strftime('%Y%m%d-%H%M%S'));out.mkdir(exist_ok=False);app=out/'app';app.mkdir()
for n in ['CoreControl.dll','CoreControl.deps.json','CoreControl.runtimeconfig.json']:shutil.copyfile(k/n,app/n)
shutil.copyfile(base/'SkiaSharp.dll',app/'SkiaSharp.dll')
identity={'platform':platform.platform(),'baseSourceCommit':m['baseSourceCommit'],'dllSha256':sha(app/'CoreControl.dll'),'priorNativeRun':str(root/'s05-extended-20260923-180340'),'gcEnvironment':{k:v for k,v in os.environ.items() if k.upper().startswith(('DOTNET_GC','COMPLUS_GC'))},'exitCode':None,'resourceAccepted':False}
with (out/'stdout.log').open('wb') as stdout,(out/'stderr.log').open('wb') as stderr:
 p=subprocess.Popen([str(root/'pinvoke-test/runtime/dotnet.exe'),str(app/'CoreControl.dll'),str(out/'report.json')],cwd=app,stdout=stdout,stderr=stderr)
 identity['pid']=p.pid;(out/'identity.json').write_text(json.dumps(identity,indent=2)+'\n');print('CORE CONTROL PID '+str(p.pid)+' OUTPUT '+str(out),flush=True);identity['exitCode']=p.wait()
(out/'identity.json').write_text(json.dumps(identity,indent=2)+'\n')
archive=out.with_suffix('.zip')
with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED) as z:
 for f in sorted(out.iterdir()):
  if f.is_file():z.write(f,f.name)
print(json.dumps({'archive':str(archive),'sha256':sha(archive),'bytes':archive.stat().st_size,'exitCode':identity['exitCode']}),flush=True)
sys.exit(identity['exitCode'])
