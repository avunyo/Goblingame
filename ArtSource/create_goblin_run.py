"""Retarget the supplied Mixamo run onto the goblin's fitted skeleton."""
import bpy,math,json,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
root=Path(__file__).resolve().parent.parent
blend_source=root/'ArtSource/Goblin_Walk_Fixed.blend'
run_source=Path('C:/Users/Administrator/Desktop/Game_textures/animations/goblin_running_bone.fbx')
hashes=[hashlib.sha256(p.read_bytes()).hexdigest() for p in (blend_source,run_source)]
bpy.ops.wm.open_mainfile(filepath=str(blend_source))
target=bpy.data.objects['Armature']; mesh=bpy.data.objects['Cube']; scene=bpy.context.scene
existing=set(bpy.data.objects);scene.render.fps=30
bpy.ops.import_scene.fbx(filepath=str(run_source))
source=next(o for o in bpy.data.objects if o not in existing and o.type=='ARMATURE')
def key(name):return name.split(':')[-1]
src={key(b.name):b for b in source.pose.bones}
assert len(src)==len(target.pose.bones)==25
assert all(key(b.name) in src for b in target.pose.bones)
first,last=map(int,source.animation_data.action.frame_range)
poses={}; locations={}
for frame in range(first,last+1):
    scene.frame_set(frame)
    poses[frame]={n:b.matrix.to_quaternion().copy() for n,b in src.items()}
    locations[frame]=src['Hips'].head.copy()
names=list(src)
def difference(a,b):
    return sum(min(q.rotation_difference(poses[b][n]).angle,2*math.pi-q.rotation_difference(poses[b][n]).angle)**2 for n,q in poses[a].items())/len(names)
matches=sorted([(difference(a,b),a,b) for a in range(first,last-14) for b in range(a+15,min(a+41,last+1))])
error,start,end=matches[0]
target.animation_data_clear();target.animation_data_create().action=bpy.data.actions.new('Goblin_Run')
target.data.pose_position='POSE'
rest={b.name:b.matrix_local.copy() for b in target.data.bones}
src_rest={key(b.name):b.matrix_local.copy() for b in source.data.bones}
leg_ratio=sum(target.data.bones['mixamorig:'+n].length for n in ('LeftUpLeg','LeftLeg'))/sum(source.data.bones[src[n].name].length for n in ('LeftUpLeg','LeftLeg'))
up=(target.matrix_world.inverted().to_3x3()@Vector((0,0,1))).normalized()
mean_height=sum(locations[f].dot(up) for f in range(start,end+1))/(end-start+1)
for out_frame,frame in enumerate(range(start,end+1),1):
    # Explicit global orientation deltas preserve fitted bone lengths/rest axes.
    for bone in target.pose.bones:
        bone.rotation_mode='QUATERNION';bone.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    for bone in target.pose.bones:
        n=key(bone.name)
        desired=poses[frame][n] @ src_rest[n].to_quaternion().inverted() @ rest[bone.name].to_quaternion()
        position=bone.head.copy()
        if n=='Hips':position=rest[bone.name].translation+up*((locations[frame].dot(up)-mean_height)*leg_ratio)
        bone.matrix=Matrix.Translation(position)@desired.to_matrix().to_4x4()
        bpy.context.view_layer.update()
        bone.keyframe_insert(data_path='location',frame=out_frame,group=bone.name)
        bone.keyframe_insert(data_path='rotation_quaternion',frame=out_frame,group=bone.name)
        bone.keyframe_insert(data_path='scale',frame=out_frame,group=bone.name)
for obj in list(bpy.data.objects):
    if obj not in existing:bpy.data.objects.remove(obj,do_unlink=True)
scene.frame_start=1;scene.frame_end=end-start+1;scene.frame_set(1)
assert all((target.data.bones[n].matrix_local-rest[n]).to_translation().length<1e-6 for n in rest)
blend=root/'ArtSource/Goblin_Run.blend';fbx=root/'Assets/GoblinArt/Goblin_Run.fbx'
assert not blend.exists() and not fbx.exists()
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
bpy.ops.object.select_all(action='DESELECT');target.select_set(True);bpy.context.view_layer.objects.active=target
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,
    bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,bake_anim_step=1.,
    bake_anim_simplify_factor=0.,bake_anim_force_startend_keying=True,axis_forward='-Z',axis_up='Y')
report={'bones':25,'source_cycle':[start,end],'frame_count':end-start+1,'seconds':(end-start)/30,
    'mean_squared_seam_angle':error,'source_unchanged':hashes==[hashlib.sha256(p.read_bytes()).hexdigest() for p in (blend_source,run_source)]}
(root/'Temp/GoblinExport/run_report.json').write_text(json.dumps(report,indent=2))
# Render a contact pose for inspection, without saving cameras in the source copy.
scene.frame_set(max(1,scene.frame_end//3));dg=bpy.context.evaluated_depsgraph_get();obj=mesh.evaluated_get(dg);data=obj.to_mesh()
points=[mesh.matrix_world@v.co for v in data.vertices];obj.to_mesh_clear()
lo=Vector(tuple(min(v[i] for v in points) for i in range(3)));hi=Vector(tuple(max(v[i] for v in points) for i in range(3)));center=(lo+hi)/2;h=hi.z-lo.z
camdata=bpy.data.cameras.new('Preview');cam=bpy.data.objects.new('Preview',camdata);scene.collection.objects.link(cam)
cam.location=center+Vector((h*.8,-h*2.5,h*.4));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();camdata.type='ORTHO';camdata.ortho_scale=h*1.5;scene.camera=cam
scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='TEXTURE'
scene.render.resolution_x=640;scene.render.resolution_y=640;scene.render.resolution_percentage=100
scene.render.filepath=str(root/'Temp/GoblinExport/run_preview.png');bpy.ops.render.render(write_still=True)
