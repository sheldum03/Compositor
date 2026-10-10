import datetime,hashlib,json,os,platform,shutil,subprocess,sys,time,zipfile
from pathlib import Path
if sys.platform != 'win32': raise SystemExit('Actual Windows required')
kit=Path(__file__).resolve().parent; root=kit.parent; base=root/'qt-remote-suite/window-app'; fixtures=root/'remote-suite/fixtures'
m=json.loads((kit/'manifest.json').read_text()); sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
for name,digest in m['payload'].items():
 if sha(kit/name)!=digest: raise RuntimeError('Payload identity: '+name)
for name,digest in m['dependencies'].items():
 if sha(base/name)!=digest: raise RuntimeError('Dependency identity: '+name)
for name,digest in m['fixtures'].items():
 if sha(fixtures/name)!=digest: raise RuntimeError('Fixture identity: '+name)
out=root/('qt-ime-fix-'+time.strftime('%Y%m%d-%H%M%S')); out.mkdir(exist_ok=False)
app=out/'app'; shutil.copytree(base,app)
for name in ('qt_probe.exe','libcompositor_native.dll'): shutil.copyfile(kit/name,app/name)
env=os.environ.copy(); env['QT_QPA_PLATFORM']='windows'; env['QT_PLUGIN_PATH']=str(app)
for name in ('QT_SCALE_FACTOR','QT_WIDGETS_RHI','QT_WIDGETS_RHI_BACKEND'): env.pop(name,None)
identity={'sourceCommit':m['sourceCommit'],'exeSha256':sha(app/'qt_probe.exe'),'platform':platform.platform(),'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'results':[],'nativeImeAccepted':False}
(out/'identity.json').write_text(json.dumps(identity,indent=2))
try:
 for tag,mode,fixture in [('synthetic','--window-check',fixtures),('text','--text',fixtures/'extended'),('native','--window',fixtures)]:
  with (out/(tag+'-stdout.log')).open('wb') as stdout,(out/(tag+'-stderr.log')).open('wb') as stderr:
   p=subprocess.Popen([str(app/'qt_probe.exe'),mode,str(fixture),str(out/tag)],cwd=app,env=env,stdout=stdout,stderr=stderr)
   print('RUNNING '+tag+' PID '+str(p.pid)+' OUTPUT '+str(out),flush=True)
   code=p.wait()
  identity['results'].append({'stage':tag,'pid':p.pid,'exitCode':code})
  (out/'identity.json').write_text(json.dumps(identity,indent=2))
  if code: raise RuntimeError(tag+' failed: '+str(code))
finally:
 archive=out.with_suffix('.zip')
 with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED) as z:
  for path in sorted(out.rglob('*')):
   if path.is_file() and app not in path.parents: z.write(path,path.relative_to(out))
 print(json.dumps({'archive':str(archive),'bytes':archive.stat().st_size,'sha256':sha(archive)}),flush=True)
