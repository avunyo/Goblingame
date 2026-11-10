import bpy,json
from pathlib import Path
p=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(p/'Goblin_Walk_Fixed.blend'))
m=bpy.data.objects['Cube'].data;uv=m.uv_layers.active.data
rows=[]
for poly in m.polygons:
 coords=[uv[i].uv for i in poly.loop_indices]
 if any((.41<u.x<.48 or .54<u.x<.60) and .75<u.y<.80 for u in coords):
  rows.append({'face':poly.index,'uv':[list(v) for v in coords],'pos':[list(m.vertices[m.loops[i].vertex_index].co) for i in poly.loop_indices]})
(p/'eye_uv.json').write_text(json.dumps(rows))
