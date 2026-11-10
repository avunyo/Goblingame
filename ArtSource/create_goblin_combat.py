"""Retarget all supplied _combat FBX animations to the existing goblin rig."""
import bpy,json,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
root=Path(__file__).resolve().parent.parent
directory=Path('C:/Users/Administrator/Desktop/Game_textures/animations')
mapping={'block_combat':'Block','dying_combat':'Death','funny_head_butt_combat':'Headbutt',
         'gobli_funny_combat_waiting':'CombatIdle','punching_combat':'Punch','standing_melle_combat':'Melee'}
reports=[]
for filename,label in mapping.items():
    bpy.ops.wm.open_mainfile(filepath=str(root/'ArtSource/Goblin_Walk_Fixed.blend'))
    target=bpy.data.objects['Armature'];mesh=bpy.data.objects['Cube'];scene=bpy.context.scene;scene.render.fps=30
    existing=set(bpy.data.objects);input_file=directory/(filename+'.fbx');original_hash=hashlib.sha256(input_file.read_bytes()).hexdigest()
    bpy.ops.import_scene.fbx(filepath=str(input_file))
    source=next(o for o in bpy.data.objects if o not in existing and o.type=='ARMATURE')
    key=lambda n:n.split(':')[-1]
    src={key(b.name):b for b in source.pose.bones}
    missing=[b.name for b in target.pose.bones if key(b.name) not in src]
    assert not missing,missing
    start,end=map(int,source.animation_data.action.frame_range)
    poses={};locations={}
    for frame in range(start,end+1):
        scene.frame_set(frame);poses[frame]={n:b.matrix.to_quaternion().copy() for n,b in src.items()};locations[frame]=src['Hips'].head.copy()
    target.animation_data_clear();target.animation_data_create().action=bpy.data.actions.new('Goblin_'+label)
    target.data.pose_position='POSE'
    rest={b.name:b.matrix_local.copy() for b in target.data.bones};sr={key(b.name):b.matrix_local.copy() for b in source.data.bones}
    ratio=sum(target.data.bones['mixamorig:'+n].length for n in ('LeftUpLeg','LeftLeg'))/sum(source.data.bones[src[n].name].length for n in ('LeftUpLeg','LeftLeg'))
    up=(target.matrix_world.inverted().to_3x3()@Vector((0,0,1))).normalized()
    baseline=locations[start].dot(up)
    for out_frame,frame in enumerate(range(start,end+1),1):
        for bone in target.pose.bones:bone.rotation_mode='QUATERNION';bone.matrix_basis=Matrix.Identity(4)
        bpy.context.view_layer.update()
        for bone in target.pose.bones:
            n=key(bone.name);q=poses[frame][n]@sr[n].to_quaternion().inverted()@rest[bone.name].to_quaternion()
            p=bone.head.copy()
            if n=='Hips':p=rest[bone.name].translation+up*((locations[frame].dot(up)-baseline)*ratio)
            bone.matrix=Matrix.Translation(p)@q.to_matrix().to_4x4();bpy.context.view_layer.update()
            for channel in ('location','rotation_quaternion','scale'):bone.keyframe_insert(data_path=channel,frame=out_frame,group=bone.name)
    for o in list(bpy.data.objects):
        if o not in existing:bpy.data.objects.remove(o,do_unlink=True)
    scene.frame_start=1;scene.frame_end=end-start+1;scene.frame_set(1)
    output=root/'Assets/GoblinArt/Combat';output.mkdir(exist_ok=True)
    blend=root/f'ArtSource/Goblin_{label}.blend';fbx=output/f'Goblin_{label}.fbx'
    assert not blend.exists() and not fbx.exists()
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    bpy.ops.object.select_all(action='DESELECT');target.select_set(True);bpy.context.view_layer.objects.active=target
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,
        bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,bake_anim_step=1.,bake_anim_simplify_factor=0.,
        bake_anim_force_startend_keying=True,axis_forward='-Z',axis_up='Y')
    reports.append({'name':label,'source_bones':len(src),'target_bones':len(rest),'source_frames':[start,end],
                    'seconds':(end-start)/30,'source_unchanged':original_hash==hashlib.sha256(input_file.read_bytes()).hexdigest()})
    (root/'ArtSource/combat_report.json').write_text(json.dumps(reports,indent=2))
