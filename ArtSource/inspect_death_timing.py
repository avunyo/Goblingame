import bpy,json
from pathlib import Path
p=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(p/'Goblin_Death.blend'))
r=bpy.data.objects['Armature'];s=bpy.context.scene
rows=[]
for f in range(1,s.frame_end+1,3):
 s.frame_set(f)
 rows.append({'frame':f,'hips':list(r.matrix_world@r.pose.bones['mixamorig:Hips'].head),'head':list(r.matrix_world@r.pose.bones['mixamorig:Head'].head)})
(p/'death_timing.json').write_text(json.dumps(rows))
