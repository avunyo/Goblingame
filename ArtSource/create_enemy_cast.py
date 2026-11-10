"""Four original low-poly interpretations of the supplied enemy concept sheet.
Each .blend is independently editable, with weighted mesh, named rig and two actions.
Existing art is never overwritten. Blender 4/5 compatible.
"""
import bpy, math, random, json
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'Assets/EnemyCast'; OUT.mkdir(exist_ok=True)
SRC=ROOT/'ArtSource/EnemyCast'; SRC.mkdir(exist_ok=True)
random.seed(41)
parts=[]; mats={}; bones={}
PALETTE={'fur':(.27,.19,.12),'furLight':(.43,.33,.22),'pink':(.46,.22,.17),
 'scarf':(.48,.12,.065),'leather':(.17,.105,.058),'metal':(.27,.29,.30),'edge':(.46,.47,.43),
 'dark':(.045,.042,.032),'bone':(.72,.64,.43),'ivory':(.88,.79,.57),'cloth':(.095,.15,.20),
 'cap':(.67,.225,.055),'capLight':(.92,.46,.16),'spot':(.92,.70,.38),'stem':(.51,.46,.30),
 'moss':(.235,.32,.065),'mossLight':(.39,.45,.095),'boar':(.44,.19,.115),
 'boarLight':(.61,.29,.17),'wood':(.28,.17,.075),'eye':(.90,.66,.18)}

def material(key):
 if key in mats:return mats[key]
 c=PALETTE[key];m=bpy.data.materials.new(key);m.diffuse_color=(*c,1);m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Roughness'].default_value=.82
 if key in ('metal','edge'):p.inputs['Metallic'].default_value=.6
 mats[key]=m;return m

def finish(o,name,key,bone):
 o.name=name;o.data.materials.append(material(key))
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 g=o.vertex_groups.new(name=bone);g.add(list(range(len(o.data.vertices))),1,'REPLACE');parts.append(o)
 return o

def ico(name,pos,scale,key,bone='Spine',sub=1):
 bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub,radius=1,location=pos)
 o=bpy.context.object;o.scale=scale;return finish(o,name,key,bone)

def box(name,pos,scale,key,bone='Spine',rot=None):
 bpy.ops.mesh.primitive_cube_add(size=1,location=pos);o=bpy.context.object;o.scale=scale
 if rot:o.rotation_euler=rot
 return finish(o,name,key,bone)

def rod(name,a,b,r1,r2,key,bone='Spine',sides=7):
 a,b=Vector(a),Vector(b);d=b-a
 bpy.ops.mesh.primitive_cone_add(vertices=sides,radius1=r1,radius2=r2,depth=d.length,location=(a+b)/2)
 o=bpy.context.object;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();return finish(o,name,key,bone)

def meshpart(name,verts,faces,key,bone='Head'):
 m=bpy.data.meshes.new(name);m.from_pydata(verts,[],faces);m.update()
 o=bpy.data.objects.new(name,m);bpy.context.collection.objects.link(o)
 bpy.context.view_layer.objects.active=o;o.select_set(True);return finish(o,name,key,bone)

def eye(x,y,z,size=.055,bone='Head'):
 ico('Eye socket',(x,y,z),(size*1.55,.035,size*1.35),'dark',bone)
 ico('Amber eye',(x,y-.03,z),(size,.028,size*.85),'eye',bone,2)
 ico('Pupil',(x,y-.055,z),(size*.36,.015,size*.68),'dark',bone)

def rig_points(hips=1.0,shoulder=1.65,head=1.85,width=.45,feet=.3,elbow=1.22):
 global bones
 bones={'Root':((0,0,0),(0,0,.2),None),'Hips':((0,0,hips),(0,0,hips+.2),'Root'),
        'Spine':((0,0,hips+.2),(0,0,shoulder),'Hips'),'Head':((0,0,shoulder),(0,-.03,head+.35),'Spine')}
 for s,side in ((-1,'L'),(1,'R')):
  bones['UpperArm.'+side]=((s*width,0,shoulder),(s*(width+.14),-.03,elbow),'Spine')
  bones['Forearm.'+side]=((s*(width+.14),-.03,elbow),(s*(width+.22),-.16,hips-.04),'UpperArm.'+side)
  bones['Hand.'+side]=((s*(width+.22),-.16,hips-.04),(s*(width+.22),-.2,hips-.22),'Forearm.'+side)
  bones['Thigh.'+side]=((s*feet,0,hips),(s*feet,-.025,hips*.51),'Hips')
  bones['Shin.'+side]=((s*feet,-.025,hips*.51),(s*feet,0,.15),'Thigh.'+side)
  bones['Foot.'+side]=((s*feet,0,.15),(s*feet,-.27,.1),'Shin.'+side)

def limbs(key,thickness=.12,boots=True):
 for side in ('L','R'):
  for part,factor in (('UpperArm',1.1),('Forearm',.85),('Thigh',1.2),('Shin',.82)):
   b=part+'.'+side;a,c,_=bones[b];rod(part,a,c,thickness*factor,thickness*factor*.8,key,b)
   ico('Joint',c,(thickness*.87,)*3,key,b)
  b='Hand.'+side;p=Vector(bones[b][0]);ico('Hand',p,(thickness,thickness*.8,thickness*1.4),key,b)
  if boots:
   b='Foot.'+side;p=Vector(bones[b][0]);p.y-=.13;p.z=.12
   ico('Boot',p,(thickness*1.35,.27,.16),'leather',b,2)

def belt(z,width):
 box('Leather belt',(0,-.005,z),(width,.53,.14),'leather','Hips')
 box('Buckle rim',(0,-.30,z),(.21,.055,.19),'edge','Hips')
 box('Buckle inset',(0,-.334,z),(.125,.015,.10),'dark','Hips')

def rat():
 rig_points(.85,1.43,1.62,.29,.20,1.05);limbs('fur',.095)
 ico('Hunched torso',(0,.055,1.18),(.35,.26,.49),'fur','Spine',2)
 ico('Chest vest',(0,-.10,1.21),(.295,.22,.33),'leather','Spine',1)
 ico('Rat skull',(0,-.09,1.70),(.29,.26,.31),'furLight','Head',2)
 rod('Long tapered muzzle',(0,-.21,1.65),(0,-.63,1.51),.19,.04,'furLight','Head')
 ico('Nose',(0,-.64,1.51),(.07,.045,.055),'pink','Head')
 for s in (-1,1):
  meshpart('Pointed ear',[(s*.16,0,1.88),(s*.47,.03,2.18),(s*.34,-.03,1.72),(s*.26,.09,1.87)],[(0,1,2),(0,3,1),(1,3,2),(2,3,0)],'fur','Head')
  meshpart('Ear pink',[(s*.20,-.012,1.87),(s*.42,.005,2.10),(s*.32,-.042,1.78)],[(0,1,2)],'pink','Head')
  eye(s*.145,-.306,1.76,.034)
  rod('Brow',(s*.09,-.31,1.81),(s*.23,-.25,1.85),.035,.042,'fur','Head')
 ico('Red scarf collar',(0,-.015,1.49),(.35,.27,.16),'scarf','Spine',1)
 meshpart('Hanging scarf',[(-.12,-.25,1.44),(.10,-.25,1.43),(.18,-.28,1.02),(-.02,-.29,1.1)],[(0,1,2),(0,2,3)],'scarf','Spine')
 belt(.88,.57)
 for s in (-1,1):box('Pouch',(s*.26,-.16,.80),(.18,.20,.22),'leather','Hips')
 # A segmented, curling pink tail with individually editable bones.
 pts=[(0,.18,.9),(0,.46,.77),(-.2,.67,.58),(-.48,.77,.55),(-.66,.73,.8),(-.75,.6,1.05),(-.65,.46,1.2)]
 for i in range(len(pts)-1):
  b='Tail'+str(i);bones[b]=(pts[i],pts[i+1],'Hips' if i==0 else 'Tail'+str(i-1));rod('Tail segment',pts[i],pts[i+1],.055*(1-i*.12),.048*(1-i*.14),'pink',b)
 p=Vector(bones['Hand.R'][0]);rod('Dagger grip',p+Vector((0,0,.05)),p+Vector((0,0,-.19)),.043,.043,'leather','Hand.R')
 box('Dagger guard',p+Vector((0,0,-.15)),(.24,.065,.035),'edge','Hand.R')
 rod('Dagger blade',p+Vector((0,0,-.18)),p+Vector((.17,-.10,-.55)),.09,0,'edge','Hand.R',4)

def mushroom():
 rig_points(.85,1.52,1.73,.51,.31,1.1);limbs('stem',.21)
 ico('Stout trunk',(0,.02,1.21),(.62,.43,.67),'stem','Spine',2)
 ico('Pale belly',(0,-.29,1.13),(.40,.21,.42),'bone','Spine',2)
 ico('Stem head',(0,0,1.69),(.42,.32,.32),'stem','Head',2)
 # Broad layered cap, deliberately asymmetric profile and triangulated facets.
 rings=[(0,2.57),(.27,2.54),(.40,2.32),(.53,2.20),(.86,2.08),(1.0,1.91),(.91,1.82),(.39,1.85)]
 verts=[];N=13
 for j,(radius,z) in enumerate(rings):
  for i in range(N):
   a=i*2*math.pi/N;r=radius*(1+.035*math.sin(i*3+j));verts.append((r*math.cos(a),r*math.sin(a)*.79,z+.026*math.sin(i*2)))
 faces=[]
 for j in range(len(rings)-1):
  for i in range(N):a=j*N+i;b=j*N+(i+1)%N;c=(j+1)*N+i;d=(j+1)*N+(i+1)%N;faces.extend([(a,b,d),(a,d,c)])
 faces.append(tuple(range((len(rings)-1)*N,len(rings)*N)))
 cap=meshpart('Orange mushroom cap',verts,faces,'cap','Head');cap.data.materials.append(material('capLight'));cap.data.materials.append(material('stem'))
 for poly in cap.data.polygons:poly.material_index=2 if poly.center.z<1.88 else (1 if random.random()<.3 else 0)
 for p,sc in [((-.18,-.16,2.55),(.17,.12,.022)),((.39,-.19,2.32),(.13,.10,.024)),((-.59,-.27,2.17),(.18,.10,.022)),((.65,-.44,2.02),(.14,.12,.02))]:ico('Cap patch',p,sc,'spot','Head',1)
 for s in (-1,1):eye(s*.13,-.315,1.74,.04)
 for i in range(25):
  side=-1 if i%2 else 1;b='UpperArm.L' if side<0 else 'UpperArm.R'
  pos=(side*(.49+random.uniform(-.13,.12)),random.uniform(-.15,.13),random.uniform(1.08,1.63))
  ico('Moss cluster',pos,(.095,.075,.10),'mossLight' if i%4==0 else 'moss',b)
 for i in range(10):ico('Cap moss',(-.6+random.uniform(-.12,.18),-.25+random.uniform(-.18,.18),2.13+random.uniform(-.03,.07)),(.10,.065,.04),'moss','Head')

def skeleton():
 rig_points(1.02,1.75,2.02,.33,.23,1.33);limbs('bone',.058)
 rod('Spinal column',(0,0,1.1),(0,0,1.79),.065,.065,'bone')
 for z,w in [(1.64,.27),(1.51,.29),(1.38,.25)]:
  for s in (-1,1):rod('Rib',(0,-.13,z),(s*w,-.055,z-.055),.042,.032,'ivory')
 ico('Pelvis',(0,0,1.03),(.26,.16,.16),'bone','Hips',1)
 ico('Skull',(0,-.02,2.04),(.245,.19,.30),'bone','Head',2)
 ico('Jaw',(0,-.08,1.88),(.17,.14,.115),'bone','Head',1)
 for s in (-1,1):
  ico('Deep eye socket',(s*.092,-.181,2.055),(.075,.04,.072),'dark','Head',1)
  ico('Ember',(s*.092,-.214,2.054),(.019,.011,.022),'eye','Head',1)
 ico('Nasal cavity',(0,-.206,1.974),(.037,.025,.055),'dark','Head')
 for i in range(6):box('Tooth',((i-2.5)*.037,-.211,1.901),(.026,.035,.059),'ivory','Head')
 ico('Helmet crown',(0,.027,2.235),(.29,.225,.27),'metal','Head',1)
 for s in (-1,1):
  box('Helmet cheek guard',(s*.224,-.01,2.09),(.09,.26,.33),'metal','Head',rot=(0,s*.13,0))
  ico('Pauldron',(s*.39,0,1.78),(.18,.22,.19),'metal','UpperArm.L' if s<0 else 'UpperArm.R',1)
 rod('Helmet ridge',(0,-.21,2.22),(0,.18,2.47),.035,.045,'edge','Head',4)
 meshpart('Torn blue tabard',[(-.26,-.18,1.72),(.26,-.18,1.72),(.17,-.20,1.30),(.24,-.21,.91),(.08,-.22,1.01),(0,-.22,.86),(-.10,-.22,1.02),(-.22,-.21,.91),(-.16,-.20,1.30)],[(0,1,2),(0,2,8),(8,2,3),(8,3,4),(8,4,5),(8,5,6),(8,6,7)],'cloth','Spine')
 p=Vector(bones['Hand.L'][0]);p+=Vector((-.10,-.16,.18))
 verts=[tuple(p+Vector((x,0,z))) for x,z in [(-.29,.50),(.22,.50),(.31,.28),(.23,-.30),(0,-.52),(-.30,-.28),(-.34,.27)]]
 meshpart('Wooden kite shield',verts,[tuple(range(7))],'wood','Forearm.L')
 for i in range(7):rod('Shield iron rim',verts[i],verts[(i+1)%7],.045,.045,'edge','Forearm.L',4)
 rod('Shield spine',p+Vector((0,-.02,.48)),p+Vector((0,-.02,-.45)),.04,.04,'metal','Forearm.L',4)
 p=Vector(bones['Hand.R'][0]);rod('Sword grip',p+Vector((0,0,.13)),p+Vector((0,0,-.15)),.045,.045,'leather','Hand.R')
 box('Sword guard',p+Vector((0,0,-.11)),(.29,.075,.05),'edge','Hand.R')
 rod('Sword blade',p+Vector((0,0,-.15)),p+Vector((.10,-.02,-.80)),.10,0,'edge','Hand.R',4)

def boar():
 rig_points(1.1,2.08,2.35,.62,.38,1.53);limbs('boar',.235)
 ico('Massive torso',(0,.02,1.70),(.73,.45,.69),'boar','Spine',2)
 ico('Chest',(0,-.22,1.83),(.56,.30,.45),'boarLight','Spine',1)
 ico('Boar skull',(0,-.02,2.36),(.43,.34,.42),'boar','Head',2)
 ico('Heavy muzzle',(0,-.35,2.26),(.28,.30,.23),'boarLight','Head',1)
 ico('Snout disc',(0,-.60,2.25),(.245,.065,.15),'pink','Head',2)
 for s in (-1,1):
  ico('Nostril',(s*.10,-.664,2.27),(.045,.017,.065),'dark','Head')
  eye(s*.195,-.32,2.49,.042)
  rod('Angry brow',(s*.07,-.33,2.55),(s*.29,-.23,2.60),.065,.055,'boar','Head')
  rod('Tusk base',(s*.20,-.40,2.13),(s*.34,-.53,2.32),.085,.055,'ivory','Head')
  rod('Tusk tip',(s*.34,-.53,2.32),(s*.30,-.52,2.61),.055,0,'ivory','Head')
  meshpart('Boar ear',[(s*.27,.01,2.59),(s*.60,-.05,2.80),(s*.46,-.11,2.46),(s*.37,.10,2.55)],[(0,1,2),(0,3,1),(1,3,2),(2,3,0)],'boar','Head')
 for y,z in [(0,2.74),(.16,2.68),(.27,2.49),(.35,2.30)]:rod('Bristle mane',(0,y,z-.18),(0,y+.11,z+.14),.13,0,'scarf','Head',4)
 belt(1.16,1.04)
 box('Chest harness',(-.18,-.43,1.80),(.20,.065,.79),'leather','Spine',rot=(0,-.35,0))
 for z in (1.50,1.70,1.90,2.08):ico('Harness rivet',(-.18+(z-1.8)*-.35,-.473,z),(.036,.018,.036),'edge','Spine')
 ico('Spiked shoulder armour',(.65,.025,2.13),(.37,.40,.27),'metal','UpperArm.R',1)
 for x,y,z in [(.50,-.17,2.29),(.74,0,2.36),(.83,.18,2.24)]:rod('Armour spike',(x,y,z),(x+.06,y,z+.27),.09,0,'edge','UpperArm.R',5)
 for side in ('L','R'):
  a,b,_=bones['Forearm.'+side];rod('Wrist bracer',Vector(a).lerp(Vector(b),.55),b,.24,.21,'leather','Forearm.'+side,8)
 p=Vector(bones['Hand.R'][0]);a=p+Vector((-.45,-.02,.08));b=p+Vector((.55,-.02,-.16));rod('Hammer handle',a,b,.075,.075,'wood','Hand.R')
 c=b+Vector((.06,0,-.03));box('War hammer head',c,(.43,.42,.59),'wood','Hand.R',rot=(0,.2,0))
 for z in (-.22,.22):box('Hammer iron band',c+Vector((0,-.012,z)),(.48,.46,.075),'metal','Hand.R',rot=(0,.2,0))
 for y in (-.24,.24):ico('Hammer stud',c+Vector((0,y,0)),(.06,.025,.06),'edge','Hand.R')

def build(label,builder):
 global parts,mats,bones
 if (SRC/(label+'.blend')).exists():raise RuntimeError('Refusing to overwrite '+label)
 bpy.ops.wm.read_factory_settings(use_empty=True);parts=[];mats={};bones={};builder()
 bpy.ops.object.select_all(action='DESELECT');arm=bpy.data.armatures.new(label+' Skeleton');rig=bpy.data.objects.new('Armature',arm);bpy.context.collection.objects.link(rig)
 rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
 for name,(a,b,parent) in bones.items():
  bone=arm.edit_bones.new(name);bone.head=a;bone.tail=b
  if parent:bone.parent=arm.edit_bones[parent]
 bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True
 bpy.ops.object.select_all(action='DESELECT')
 for o in parts:o.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();body=bpy.context.object;body.name=label+' Body'
 mod=body.modifiers.new('Skin','ARMATURE');mod.object=rig;body.parent=rig
 # Keep material facets flat; all vertices have a deterministic bone assignment.
 assert all(len(v.groups)>0 for v in body.data.vertices)
 scene=bpy.context.scene;scene.render.fps=30
 for action_name,frames in [('Idle',121),('Walk',31)]:
  rig.animation_data_create();action=bpy.data.actions.new(action_name);action.use_fake_user=True;rig.animation_data.action=action
  for f in range(1,frames+1):
   phase=(f-1)/(frames-1)*2*math.pi
   for bone in rig.pose.bones:bone.rotation_mode='XYZ';bone.rotation_euler=(0,0,0);bone.location=(0,0,0)
   rig.pose.bones['Spine'].rotation_euler.x=.014*math.sin(phase)
   if action_name=='Walk':
    rig.pose.bones['Hips'].location.y=.035*(1-math.cos(phase*2)) # bone local Y points up
    for side,offset in [('L',0),('R',math.pi)]:
     t=phase+offset;s=math.sin(t)
     rig.pose.bones['Thigh.'+side].rotation_euler.x=.34*s
     rig.pose.bones['Shin.'+side].rotation_euler.x=-.40*max(0,-s)
     rig.pose.bones['Foot.'+side].rotation_euler.x=.12*s
     rig.pose.bones['UpperArm.'+side].rotation_euler.x=-.25*s
     rig.pose.bones['Forearm.'+side].rotation_euler.x=-.10-.07*s
    rig.pose.bones['Spine'].rotation_euler.z=.025*math.sin(phase)
   for bone in rig.pose.bones:
    if bone.name.startswith('Tail'):bone.rotation_euler.z=.09*math.sin(phase+int(bone.name[4:])*.6)
    bone.keyframe_insert('rotation_euler',frame=f);bone.keyframe_insert('location',frame=f)
  # Explicit cyclic interpolation with matching first/last poses.
  try:
   curves=action.fcurves
  except AttributeError:curves=action.layers[0].strips[0].channelbag(action.slots[0]).fcurves
  for curve in curves:
   for key in curve.keyframe_points:key.interpolation='LINEAR'
 scene.frame_start=1;scene.frame_end=31;scene.frame_set(1)
 # Studio preview retained in the .blend, excluded from FBX.
 height=max((body.matrix_world@v.co).z for v in body.data.vertices)
 bpy.ops.object.camera_add(location=(3.7,-6.6,height*1.12));cam=bpy.context.object;cam.name='Preview Camera';cam.rotation_euler=(Vector((0,0,height*.51))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=height*1.32;scene.camera=cam
 for name,pos,power,size in [('Key',(-3,-4,6),750,5),('Rim',(3,2,4),950,3),('Fill',(2,-4,2),220,4)]:
  bpy.ops.object.light_add(type='AREA',location=pos);l=bpy.context.object;l.name=name;l.data.energy=power;l.data.shape='DISK';l.data.size=size;l.rotation_euler=(Vector((0,0,1.2))-l.location).to_track_quat('-Z','Y').to_euler()
 scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.10,.12,.14,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
 scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=640;scene.render.resolution_y=800;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=True
 bpy.ops.object.select_all(action='DESELECT');body.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
 bpy.ops.wm.save_as_mainfile(filepath=str(SRC/(label+'.blend')))
 bpy.ops.export_scene.fbx(filepath=str(OUT/(label+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
 scene.render.filepath=str(SRC/(label+'.png'));bpy.ops.render.render(write_still=True)
 return {'name':label,'vertices':len(body.data.vertices),'bones':len(arm.bones),'actions':['Idle','Walk'],'height':height}

report=[]
for label,fn in [('RatRogue',rat),('MossGourmand',mushroom),('BoneSentinel',skeleton),('IronTusk',boar)]:
 report.append(build(label,fn));(SRC/'report.json').write_text(json.dumps(report,indent=2))

