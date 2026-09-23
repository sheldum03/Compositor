import hashlib,json,os,subprocess,time,zipfile
from pathlib import Path
root=Path(__file__).resolve().parent.parent
app=root/'qt-ime-fix-20260923-170518/app'; exe=app/'qt_probe.exe'
assert hashlib.sha256(exe.read_bytes()).hexdigest()=='3d06ab384242609cdede03900803d992f725c767c4ac2ee35790e85a186b2263'
out=root/('qt-ime-trace-'+time.strftime('%Y%m%d-%H%M%S'));out.mkdir(exist_ok=False)
env=os.environ.copy();env['QT_QPA_PLATFORM']='windows';env['QT_PLUGIN_PATH']=str(app);env['QT_LOGGING_RULES']='qt.qpa.input.methods.debug=true'
with (out/'stdout.log').open('wb') as stdout,(out/'ime-debug.log').open('wb') as stderr:
 p=subprocess.Popen([str(exe),'--window',str(root/'remote-suite/fixtures'),str(out/'native')],cwd=app,env=env,stdout=stdout,stderr=stderr)
 print('TRACE PID '+str(p.pid)+' OUTPUT '+str(out),flush=True);code=p.wait()
(out/'identity.json').write_text(json.dumps({'exitCode':code,'pid':p.pid,'exeSha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'loggingRules':env['QT_LOGGING_RULES'],'scope':'Diagnostic only; candidate position not accepted'},indent=2))
archive=out.with_suffix('.zip')
with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED) as z:
 for f in out.rglob('*'):
  if f.is_file():z.write(f,f.relative_to(out))
print(json.dumps({'archive':str(archive),'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest()}),flush=True)
