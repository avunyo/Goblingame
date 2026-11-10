import bpy,json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent.parent
result={}
for label in ('Punch','Melee','Headbutt','Block'):
    bpy.ops.wm.open_mainfile(filepath=str(root/f'ArtSource/Goblin_{label}.blend'))
    r=bpy.data.objects['Armature'];scene=bpy.context.scene
    up=(r.matrix_world.inverted().to_3x3()@Vector((0,0,1))).normalized()
    scene.frame_set(1)
    f=sum((r.pose.bones['mixamorig:'+side+'ToeBase'].head-r.pose.bones['mixamorig:'+side+'Foot'].head for side in ('Left','Right')),Vector())
    f=(f-up*f.dot(up)).normalized();values=[]
    for frame in range(1,scene.frame_end+1):
        scene.frame_set(frame);hips=r.pose.bones['mixamorig:Hips'].head;head=r.pose.bones['mixamorig:Head'].head
        hands=[r.pose.bones['mixamorig:'+s+'Hand'].head for s in ('Left','Right')]
        score=(head-hips).dot(f) if label=='Headbutt' else (max((h-hips).dot(f) for h in hands))
        if label=='Block':score=-sum((h-head).length for h in hands)
        values.append((score,frame))
    peak=max(values)[1]
    if label=='Block':start=max(1,peak-2);end=min(scene.frame_end,peak+2)
    else:start=max(1,peak-(18 if label=='Headbutt' else 14));end=min(scene.frame_end,peak+(24 if label=='Headbutt' else 20))
    result[label]={'first':start,'last':end,'impact':(peak-start)/max(1,end-start),'peak':peak}
(root/'ArtSource/combat_timing.json').write_text(json.dumps(result,indent=2))
