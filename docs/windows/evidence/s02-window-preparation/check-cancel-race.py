from pathlib import Path
import shutil,subprocess,os,json
src=Path('/private/tmp/compositor-parent-s02-window'); old=Path('/Users/admin/.codex/visualizations/2026/09/20/01a0beb1-da38-7b00-9183-d65bff121d78/s02-window-preparation/source/BrushPerformanceProbe.cs').read_text()
fixed=(src/'experiments/windows/avalonia/BrushPerformanceProbe.cs').read_text();records=[]
for name,code in [('before',old),('after',fixed)]:
 root=Path('/private/tmp/compositor-parent-s02-cancel-'+name);root.mkdir(exist_ok=False)
 for directory in ['experiments/windows/avalonia','experiments/windows/dotnet-bridge','Compositor/Resources']:
  shutil.copytree(src/directory,root/directory,ignore=shutil.ignore_patterns('bin','obj'))
 p=root/'experiments/windows/avalonia/BrushPerformanceProbe.cs';needle='pending = null; completion.SetResult(frame);';assert code.count(needle)==1
 p.write_text(code.replace(needle,needle+'\n                if (sequence == 484) Stop.Cancel(); // test-only: complete the final frame, then cancel before await resumes'))
 cwd=root/'experiments/windows/avalonia';env=os.environ.copy();env['NUGET_PACKAGES']='/tmp/compositor-nuget-packages'
 build=subprocess.run(['/tmp/compositor-dotnet-10.0.401/dotnet','build','-c','Release','--disable-build-servers','--nologo'],cwd=cwd,env=env,capture_output=True,text=True)
 (root/'build.log').write_text(build.stdout+build.stderr);assert build.returncode==0,build.stdout+build.stderr
 result=subprocess.run(['/tmp/compositor-dotnet-10.0.401/dotnet','bin/Release/net10.0/Compositor.AvaloniaProbe.dll','--s02-check',str(src/'docs/windows/fixtures/brush'),str(root/'results'),'/tmp/compositor-windows-native-release/libcompositor_native.dylib'],cwd=cwd,capture_output=True,text=True)
 (root/'run.log').write_text(result.stdout+result.stderr);report=json.loads((root/'results/report.json').read_text())
 record={'variant':name,'buildExit':build.returncode,'runExit':result.returncode,'completed':report['completed'],'trials':len(report['trials']),'error':report['error'],'root':str(root)};records.append(record);print(json.dumps(record),flush=True)
 assert report['completed']==(name=='before')
 assert (result.returncode==0)==(name=='before')
(src/'cancel-race-results.json').write_text(json.dumps(records,indent=2))
