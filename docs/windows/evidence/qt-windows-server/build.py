from pathlib import Path
import subprocess, json
b=Path(__file__).resolve().parent
cmake='/tmp/compositor-windows-cmake-venv/bin/cmake'
commands=[
[cmake,'-S',str(b/'source/experiments/windows/qt'),'-B',str(b/'build'),'-G','Unix Makefiles','-DCMAKE_BUILD_TYPE=Release','-DCMAKE_TOOLCHAIN_FILE='+str(b/'toolchain.cmake'),'-DCMAKE_PREFIX_PATH='+str(b/'qt'),'-DQT_HOST_PATH=/tmp/compositor-qt-6.11.2/6.11.2/macos'],
[cmake,'--build',str(b/'build'),'--parallel','4']]
(b/'commands.json').write_text(json.dumps(commands,indent=2))
for index,command in enumerate(commands):
 with (b/('configure.log' if index==0 else 'build.log')).open('w') as log:
  code=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT).returncode
 print('configure' if index==0 else 'build',code,flush=True)
 if code: raise SystemExit(code)
