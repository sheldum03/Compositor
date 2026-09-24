import hashlib,json,platform,time
from pathlib import Path
import numpy as np
from PIL import Image
import onnxruntime as ort
root=Path(__file__).resolve().parent
model=root/'BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx'
assert model.stat().st_size==224005088
sample=root.parent/'ai-image-windows-preparation/kit/astronaut.png'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(model)=='5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333'
assert sha(sample)=='88431cd9653ccd539741b555fb0a46b61558b301d4110412b5bc28b5e3ea6cb5'
r=dict(platform=platform.platform(),windowsExecuted=False,onnxruntime=ort.__version__,modelSha256=sha(model),sampleSha256=sha(sample),passed=False)
try:
 options=ort.SessionOptions();options.intra_op_num_threads=4
 start=time.monotonic();session=ort.InferenceSession(str(model),sess_options=options,providers=['CPUExecutionProvider'])
 r.update(loadSeconds=time.monotonic()-start,providers=session.get_providers(),inputs=[dict(name=x.name,shape=x.shape,type=x.type) for x in session.get_inputs()],outputs=[dict(name=x.name,shape=x.shape,type=x.type) for x in session.get_outputs()])
 x=session.get_inputs()[0];assert x.shape==[1,3,1024,1024] and x.type=='tensor(float)'
 with Image.open(sample) as image:
  source=image.convert('RGB');pixels=np.asarray(source.resize((1024,1024),Image.Resampling.BILINEAR),dtype=np.float32)/np.float32(255)
 data=((pixels-np.array([.485,.456,.406],dtype=np.float32))/np.array([.229,.224,.225],dtype=np.float32)).transpose(2,0,1)[None].copy()
 start=time.monotonic();values=session.run(None,{x.name:data});r['inferenceSeconds']=time.monotonic()-start
 prediction=values[-1];assert prediction.shape==(1,1,1024,1024) and np.isfinite(prediction).all()
 probability=1/(1+np.exp(-np.clip(prediction[0,0],-80,80)))
 mask=Image.fromarray((probability*255).astype(np.uint8)).resize(source.size,Image.Resampling.BICUBIC)
 mask.save(root/'mask.png');source.putalpha(mask);source.save(root/'cutout.png')
 r.update(passed=True,logitRange=[float(prediction.min()),float(prediction.max())],probabilityRange=[float(probability.min()),float(probability.max())],predictionSha256=hashlib.sha256(prediction.tobytes()).hexdigest(),maskSha256=sha(root/'mask.png'),scope='Single-image local CPU feasibility only; no quality, Windows, native C ABI, cancellation or release acceptance')
except Exception as e:r['error']=str(e)
(root/'local-inspection.json').write_text(json.dumps(r,indent=2)+'\n');print(json.dumps(r,indent=2))
raise SystemExit(0 if r['passed'] else 1)
