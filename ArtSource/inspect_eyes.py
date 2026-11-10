import bpy,json
from pathlib import Path
p=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(p/'Goblin_Walk_Fixed.blend'))
result=[]
for o in bpy.data.objects:
 if o.type!='MESH':continue
 m=o.data;adj=[[] for v in m.vertices]
 for e in m.edges:
  a,b=e.vertices;adj[a].append(b);adj[b].append(a)
 seen=set();parts=[]
 for v in m.vertices:
  if v.index in seen:continue
  stack=[v.index];seen.add(v.index);ids=[]
  while stack:
   i=stack.pop();ids.append(i)
   for n in adj[i]:
    if n not in seen:seen.add(n);stack.append(n)
  coords=[m.vertices[i].co for i in ids]
  parts.append({'count':len(ids),'first':ids[:5],'min':[min(v[k] for v in coords) for k in range(3)],'max':[max(v[k] for v in coords) for k in range(3)]})
 result.append({'object':o.name,'materials':[s.name for s in m.materials],'parts':parts})
(p/'eyes_inspection.json').write_text(json.dumps(result,indent=2))
