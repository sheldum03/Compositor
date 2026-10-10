from pathlib import Path
import hashlib,json,sys
import numpy as np
import onnx
from PIL import Image
assets=Path(sys.argv[1]);output=Path(sys.argv[2]);output.mkdir()
model=assets/'u2netp.onnx';photo=assets/'astronaut.png'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(model)=='309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8'
assert sha(photo)=='88431cd9653ccd539741b555fb0a46b61558b301d4110412b5bc28b5e3ea6cb5'
graph=onnx.load(model,load_external_data=False);onnx.checker.check_model(graph,full_check=True)
assert not graph.functions and all(not t.external_data for t in graph.graph.initializer)
assert all(n.domain in ('','ai.onnx') for n in graph.graph.node)
assert [(x.domain,x.version) for x in graph.opset_import]==[('',11)]
source=Image.open(photo).convert('RGB');assert source.size==(512,512)
rgb=np.asarray(source.resize((320,320),Image.Resampling.LANCZOS))
normalized=(rgb/max(float(rgb.max()),1e-6)-(0.485,0.456,0.406))/(0.229,0.224,0.225)
tensor=normalized.transpose(2,0,1)[None].astype('<f4')
assert tensor.shape==(1,3,320,320) and np.isfinite(tensor).all()
tensor.tofile(output/'input.f32');(output/'astronaut.png').write_bytes(photo.read_bytes())
expected=Path('/tmp/compositor-ai-run-02/input.f32');assert sha(output/'input.f32')==sha(expected)
(output/'input-identity.json').write_text(json.dumps({'modelSha256':sha(model),'photoSha256':sha(photo),'tensorSha256':sha(output/'input.f32'),'tensorBytes':tensor.nbytes,'shape':list(tensor.shape),'onnxNodes':len(graph.graph.node),'onnxIrVersion':graph.ir_version,'opset':11,'matchesExistingMacInput':True,'graphCheckedWith':onnx.__version__,'numpy':np.__version__,'platform':'Mac preparation only'},indent=2))
