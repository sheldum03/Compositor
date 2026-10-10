import datetime,hashlib,json,os,platform,shutil,subprocess,sys,time,zipfile
from pathlib import Path
if sys.platform != 'win32': raise SystemExit('Actual Windows required')
kit=Path(__file__).resolve().parent; root=kit.parent; base=root/'s02-lifecycle-r9-app'; fixtures=root/'remote-suite/fixtures/brush'
m=json.loads((kit/'manifest.json').read_text()); sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(base/'Compositor.AvaloniaProbe.dll')==m['baseDllSha256'], 'Base DLL changed'
for name,digest in m['payload'].items():
 assert sha(kit/name)==digest, 'Payload identity: '+name
for name,digest in m['dependencies'].items():
 assert sha(base/name)==digest, 'Dependency identity: '+name
assert sha(fixtures/'checksums.json')==m['fixtureManifestSha256'], 'Fixture manifest changed'
for row in json.loads((fixtures/'checksums.json').read_text()):
 assert sha(fixtures/row['path'])==row['sha256'], 'Fixture changed: '+row['path']
native=root/'native-run-20260921-111947/compositor_native.dll'
assert sha(native)=='046ad66d8da01625985f29f59fd5b39035adc0483dc43eb141d678b4e931320f', 'Native DLL changed'
runtime=root/'pinvoke-test/runtime/dotnet.exe'
out=root/('s05-extended-'+time.strftime('%Y%m%d-%H%M%S')); out.mkdir(exist_ok=False)
app=out/'app'; shutil.copytree(base,app)
for name in m['payload']:
 if name.startswith('Compositor.AvaloniaProbe.'): shutil.copyfile(kit/name,app/name)
identity={'baseSourceCommit':m['baseSourceCommit'],'diagnosticPatchSha256':m['payload']['diagnostic.patch'],'dllSha256':sha(app/'Compositor.AvaloniaProbe.dll'),'platform':platform.platform(),'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'nativeExit':None,'reviewExit':None,'resourceAccepted':False,'gcEnvironment':{k:v for k,v in os.environ.items() if k.upper().startswith(('DOTNET_GC','COMPLUS_GC'))}}
def save(): (out/'identity.json').write_text(json.dumps(identity,indent=2)+'\n')
save()
try:
 with (out/'stdout.log').open('wb') as stdout,(out/'stderr.log').open('wb') as stderr:
  p=subprocess.Popen([str(runtime),str(app/'Compositor.AvaloniaProbe.dll'),'--s05-extended-window',str(fixtures),str(out/'run'),str(native)],cwd=app,stdout=stdout,stderr=stderr)
  identity['pid']=p.pid; save()
  print('RUNNING 27 ROUNDS PID '+str(p.pid)+' OUTPUT '+str(out),flush=True)
  identity['nativeExit']=p.wait(); save()
 with (out/'review.json').open('wb') as stdout,(out/'review-error.log').open('wb') as stderr:
  identity['reviewExit']=subprocess.run([sys.executable,str(kit/'review-s05.py'),str(out/'run/report.json'),'--extended'],stdout=stdout,stderr=stderr).returncode
 save()
finally:
 files=[p for p in sorted(out.rglob('*')) if p.is_file() and app not in p.parents]
 (out/'files.json').write_text(json.dumps({str(p.relative_to(out)):sha(p) for p in files},indent=2)+'\n')
 archive=out.with_suffix('.zip')
 with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED) as z:
  for path in [*files,out/'files.json']: z.write(path,path.relative_to(out))
 print(json.dumps({'archive':str(archive),'bytes':archive.stat().st_size,'sha256':sha(archive),'nativeExit':identity['nativeExit'],'reviewExit':identity['reviewExit']}),flush=True)
sys.exit(0 if identity['nativeExit']==identity['reviewExit']==0 else 1)
