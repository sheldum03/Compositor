from pathlib import Path
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
import threading,time,subprocess,shutil,json,hashlib,zipfile
root=Path('/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-download-fix-integration');original=Path('/Users/admin/.codex/visualizations/2026/09/20/01a0beb1-da38-7b00-9183-d65bff121d78/ai-windows-cross-build/download-timeout-fix/CompositorAiProbe');model=Path('/tmp/compositor-ai-screening/u2netp.onnx').read_bytes();assert hashlib.sha256(model).hexdigest()=='309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8'
class Handler(BaseHTTPRequestHandler):
 def do_GET(self):
  self.send_response(200);self.send_header('Content-Length',str(len(model)));self.end_headers()
  if self.path=='/stall':self.wfile.write(model[:1024]);self.wfile.flush();time.sleep(6)
  else:self.wfile.write(model if self.path=='/model' else bytes([model[0]^1])+model[1:])
 def log_message(self,*args):pass
server=ThreadingHTTPServer(('127.0.0.1',0),Handler);threading.Thread(target=server.serve_forever,daemon=True).start();records=[]
try:
 for name,route in [('success','model'),('timeout','stall'),('wrong-hash','wrong')]:
  kit=Path('/private/tmp/compositor-ai-download-integration-'+name);shutil.copytree(original,kit)
  shutil.copy2('/tmp/compositor-parent-ai-windows/runner-maccheck/app/ai_probe.exe',kit/'app/ai_probe.exe')
  p=kit/'run-ai.ps1';s=p.read_text(encoding='utf-8-sig')
  s=s.replace("if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitProcess) { throw 'Windows x64 PowerShell required' }","if ([Environment]::OSVersion.Platform -ne 'Unix') { throw 'Mac-only adapted wrapper test' }")
  s=s.replace("Join-Path ([Environment]::SystemDirectory) $name","Join-Path '/tmp/compositor-ai-mac-runtime-not-applicable' $name")
  s=s.replace("$curl = Join-Path ([Environment]::SystemDirectory) 'curl.exe'","$curl = '/usr/bin/curl'")
  s=s.replace('https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx',f'http://127.0.0.1:{server.server_port}/{route}')
  if name=='timeout':s=s.replace('--max-time 120','--max-time 2').replace('maximumSeconds=120','maximumSeconds=2')
  p.write_text(s,encoding='utf-8-sig')
  m=json.loads((kit/'files.json').read_text())
  for e in m:
   f=kit/e['path'];e.update(bytes=f.stat().st_size,sha256=hashlib.sha256(f.read_bytes()).hexdigest())
  (kit/'files.json').write_text(json.dumps(m,indent=2))
  begin=time.monotonic();result=subprocess.run(['/tmp/compositor-powershell-7.6.0/pwsh','-NoProfile','-File',str(p)],capture_output=True,text=True,timeout=25);elapsed=time.monotonic()-begin
  out=root/name;out.mkdir();(out/'run.log').write_text(result.stdout+result.stderr);shutil.copy2(p,out/'adapted-run-ai.ps1')
  zips=list(kit.glob('ai-run-*.zip'));assert len(zips)==1;shutil.copy2(zips[0],out/zips[0].name)
  with zipfile.ZipFile(zips[0]) as z:
   assert z.testzip() is None and not any(n.endswith('.onnx') for n in z.namelist());summary=json.loads(z.read('summary.json').decode('utf-8-sig'))
  (out/'summary.json').write_text(json.dumps(summary,indent=2,ensure_ascii=False))
  record={'case':name,'exitCode':result.returncode,'elapsedSeconds':elapsed,'completed':summary['completed'],'error':summary['error'],'download':summary['download'],'invocations':len(summary['invocations']),'windowsExecuted':summary['windowsExecuted']};records.append(record);print(json.dumps(record),flush=True)
  assert not summary['windowsExecuted']
  if name=='success':assert result.returncode==0 and summary['completed'] and len(summary['invocations'])==8
  else:assert result.returncode==1 and not summary['completed'] and len(summary['invocations'])==0
  if name=='timeout':assert summary['download']['exitCode']==28 and elapsed<10 and summary['download']['receivedBytes']==1024
  if name=='wrong-hash':assert summary['download']['exitCode']==0 and summary['error']=='Downloaded model identity mismatch'
finally:server.shutdown();server.server_close()
(root/'checks.json').write_text(json.dumps(records,indent=2))
