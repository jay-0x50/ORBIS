"""Non-destructive Humanoid rig prototypes for supplied hero derivatives.

Owns only --output (RigReview), never overwrites the input blend or Unity Assets.
Rigging preparation is not STEP 1 mesh acceptance or game-ready certification.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector, Quaternion, Matrix

sys.path.insert(0, str(Path(__file__).resolve().parent))


def digest(path):
    h = hashlib.sha256()
    with open(path, 'rb') as stream:
        for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')


def mesh_arrays(obj):
    vertices = np.empty((len(obj.data.vertices), 3), np.float32)
    obj.data.vertices.foreach_get('co', vertices.ravel())
    matrix = np.asarray(obj.matrix_world, dtype=np.float64)
    world = vertices @ matrix[:3, :3].T + matrix[:3, 3]
    obj.data.calc_loop_triangles()
    triangles = np.empty((len(obj.data.loop_triangles), 3), np.int32)
    obj.data.loop_triangles.foreach_get('vertices', triangles.ravel())
    edges = np.empty((len(obj.data.edges), 2), np.int32)
    obj.data.edges.foreach_get('vertices', edges.ravel())
    return world, triangles, edges


def integrity(obj):
    vertices = np.empty(len(obj.data.vertices)*3, np.float32)
    obj.data.vertices.foreach_get('co', vertices)
    loops = np.empty(len(obj.data.loops), np.int32)
    obj.data.loops.foreach_get('vertex_index', loops)
    material_indices = np.empty(len(obj.data.polygons), np.int32)
    obj.data.polygons.foreach_get('material_index', material_indices)
    uv = {}
    for layer in obj.data.uv_layers:
        values = np.empty(len(layer.data)*2, np.float32)
        layer.data.foreach_get('uv', values)
        uv[layer.name] = hashlib.sha256(values.tobytes()).hexdigest()
    return {'vertices_sha256': hashlib.sha256(vertices.tobytes()).hexdigest(),
            'loop_indices_sha256': hashlib.sha256(loops.tobytes()).hexdigest(),
            'material_indices_sha256': hashlib.sha256(material_indices.tobytes()).hexdigest(),
            'uv_sha256': uv,
            'material_names': [m.name if m else None for m in obj.data.materials],
            'world_matrix': [list(row) for row in obj.matrix_world]}


def inspect(source, output, label, audit):
    meshes = [o for o in bpy.data.objects if o.type == 'MESH' and not o.hide_render]
    if len(meshes) != 1:
        raise ValueError('The current raw hero contract expects exactly one visible mesh.')
    obj = meshes[0]
    world, faces, edges = mesh_arrays(obj)
    lo, hi = np.array(audit['render_bounds']['min']), np.array(audit['render_bounds']['max'])
    height = hi[2]-lo[2]
    origin = (hi+lo)*.5
    origin[2] = lo[2]
    normalized = (world-origin)/height
    np.savez_compressed(output/(label+'_geometry.npz'), world=world, normalized=normalized,
                        triangles=faces, edges=edges)
    # Cross-sections of the actual hand, not legacy finger proportions.
    sections = []
    for x in np.arange(.27, .405, .01):
        mask = (normalized[:, 0] > x-.004) & (normalized[:, 0] < x+.004)
        mask &= normalized[:, 2] > .69
        points = normalized[mask]
        if len(points):
            sections.append({'x':float(x),'vertices':len(points),
                             'y_minmax':[float(points[:,1].min()),float(points[:,1].max())],
                             'z_minmax':[float(points[:,2].min()),float(points[:,2].max())]})
    report = {'label':label,'source':str(source),'source_sha256':digest(source),
              'status':'Geometry inspection only; source unchanged; not skinning or import approval',
              'mesh':obj.name,'vertices':len(world),'triangles':len(faces),
              'normalization_origin':origin.tolist(),'height':float(height),
              'integrity':integrity(obj),'positive_x_hand_sections':sections}
    write_json(output/(label+'_inspection.json'), report)
    return obj, normalized, world, faces, edges, report


def render_inspection(output, label, audit, report):
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from audit_raw_meshes import render_studio
    render_record = {'label':label+'_Unrigged'}
    render_studio(output, render_record, 1024, fixed_bounds=audit['render_bounds'])
    scene, camera = bpy.context.scene, bpy.context.scene.camera
    origin, height = Vector(report['normalization_origin']), report['height']
    target = origin+Vector((.350,-.084,.738) if label=='Stella' else (.347,-.044,.758))*height
    for suffix, direction in [('HandFront',(0,-1,.13)),('HandTop',(.08,-.15,1))]:
        camera.location = target+Vector(direction).normalized()*height*2
        camera.rotation_euler = (target-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale = height*.18
        scene.render.filepath = str(output/(label+'_'+suffix+'.png'))
        bpy.ops.render.render(write_still=True)
    write_json(output/(label+'_inspection_render.json'),render_record)


def smooth(a, b, value):
    t = min(1., max(0., (value-a)/(b-a)))
    return t*t*(3-2*t)


def mix(a, b, amount):
    result = {k:v*(1-amount) for k,v in a.items()}
    for k,v in b.items():
        result[k] = result.get(k,0)+v*amount
    return result


def calibrate_bones(label):
    if label=='Stella':return calibrate_stella_bones()
    # Normalized SOURCE coordinates after checking front/side and actual hand
    # closeups. These replace the earlier coarse planning anchors, not geometry.
    records = [
        ('Root',(0,-.035,0),(0,-.035,.10),None),
        ('Hips',(0,-.035,.543),(0,-.043,.633),'Root'),
        ('Spine',(0,-.043,.633),(0,-.048,.740),'Hips'),
        ('Chest',(0,-.048,.740),(0,-.048,.831),'Spine'),
        ('Neck',(0,-.048,.831),(0,-.052,.861),'Chest'),
        ('Head',(0,-.052,.861),(0,-.045,.960),'Neck')]
    for sign,side in [(1,'Left'),(-1,'Right')]:
        records.extend([
            (side+'Shoulder',(0,-.047,.804),(sign*.104,-.042,.799),'Chest'),
            (side+'UpperArm',(sign*.104,-.042,.799),(sign*.222,-.045,.776),side+'Shoulder'),
            (side+'LowerArm',(sign*.222,-.045,.776),(sign*.300,-.041,.763),side+'UpperArm'),
            (side+'Hand',(sign*.300,-.041,.763),(sign*.346,-.040,.758),side+'LowerArm'),
            (side+'UpperLeg',(sign*.048,-.033,.536),(sign*.051,-.052,.365),'Hips'),
            (side+'LowerLeg',(sign*.051,-.052,.365),(sign*.054,-.024,.077),side+'UpperLeg'),
            (side+'Foot',(sign*.054,-.024,.077),(sign*.054,-.087,.024),side+'LowerLeg'),
            (side+'Toes',(sign*.054,-.087,.024),(sign*.054,-.121,.017),side+'Foot')])
        # Four fingers were visually confirmed in the top render. Separate
        # transverse lanes follow the source's palm; thumb is below/front.
        for finger,y,tip in [('Index',-.054,.391),('Middle',-.040,.397),
                             ('Ring',-.028,.392),('Little',-.017,.373)]:
            start=.341 if finger!='Little' else .336
            end=tip
            xs=[start,start+(end-start)*.43,start+(end-start)*.75,end]
            zs=[.759,.755,.753,.751]
            for j,suffix in enumerate(['Proximal','Intermediate','Distal']):
                parent=side+'Hand' if j==0 else side+finger+['Proximal','Intermediate'][j-1]
                records.append((side+finger+suffix,(sign*xs[j],y,zs[j]),
                                (sign*xs[j+1],y,zs[j+1]),parent))
        thumb=[(sign*.322,-.053,.756),(sign*.333,-.058,.746),
               (sign*.347,-.060,.736),(sign*.353,-.060,.731)]
        for j,suffix in enumerate(['Proximal','Intermediate','Distal']):
            records.append((side+'Thumb'+suffix,thumb[j],thumb[j+1],
                            side+'Hand' if j==0 else side+'Thumb'+['Proximal','Intermediate'][j-1]))
        cape=[(sign*.092,.023,.790),(sign*.125,.052,.605),
              (sign*.159,.067,.391),(sign*.187,.048,.166)]
        for j in range(3):
            records.append((side+'Cape'+str(j+1),cape[j],cape[j+1],
                            'Chest' if j==0 else side+'Cape'+str(j)))
        coat=[(sign*.036,-.095,.646),(sign*.039,-.104,.555),(sign*.045,-.097,.443)]
        for j in range(2):
            records.append((side+'Coat'+str(j+1),coat[j],coat[j+1],
                            'Hips' if j==0 else side+'Coat'+str(j)))
    records.extend([('HairBack1',(0,.004,.950),(0,.012,.887),'Head'),
                    ('HairBack2',(0,.012,.887),(0,.022,.817),'HairBack1')])
    assert len(records)==64 and len({r[0] for r in records})==64
    return records


def calibrate_stella_bones():
    # Measured on Stella400kDirect source projections: narrower stance, more
    # anterior body/hand centres and much longer posterior hair than Polaris.
    # These are independent bind anchors; no Polaris weights are transferred.
    records=[('Root',(0,-.075,0),(0,-.075,.10),None),
        ('Hips',(0,-.074,.548),(0,-.070,.624),'Root'),
        ('Spine',(0,-.070,.624),(0,-.078,.726),'Hips'),
        ('Chest',(0,-.078,.726),(0,-.072,.830),'Spine'),
        ('Neck',(0,-.072,.830),(0,-.070,.858),'Chest'),
        ('Head',(0,-.070,.858),(0,-.056,.969),'Neck')]
    for sign,side in [(1,'Left'),(-1,'Right')]:
        records.extend([
            (side+'Shoulder',(0,-.075,.805),(sign*.095,-.074,.800),'Chest'),
            (side+'UpperArm',(sign*.095,-.074,.800),(sign*.223,-.078,.771),side+'Shoulder'),
            (side+'LowerArm',(sign*.223,-.078,.771),(sign*.307,-.080,.748),side+'UpperArm'),
            (side+'Hand',(sign*.307,-.080,.748),(sign*.347,-.083,.736),side+'LowerArm'),
            (side+'UpperLeg',(sign*.044,-.077,.542),(sign*.033,-.090,.338),'Hips'),
            (side+'LowerLeg',(sign*.033,-.090,.338),(sign*.027,-.067,.079),side+'UpperLeg'),
            (side+'Foot',(sign*.027,-.067,.079),(sign*.027,-.117,.028),side+'LowerLeg'),
            (side+'Toes',(sign*.027,-.117,.028),(sign*.027,-.143,.017),side+'Foot')])
        for finger,y,tip in [('Index',-.097,.385),('Middle',-.085,.385),
                             ('Ring',-.073,.380),('Little',-.064,.364)]:
            start=.346 if finger!='Little' else .340
            xs=[start,start+(tip-start)*.43,start+(tip-start)*.75,tip]
            zs=[.740,.736,.732,.730]
            for j,suffix in enumerate(['Proximal','Intermediate','Distal']):
                records.append((side+finger+suffix,(sign*xs[j],y,zs[j]),(sign*xs[j+1],y,zs[j+1]),
                    side+'Hand' if j==0 else side+finger+['Proximal','Intermediate'][j-1]))
        thumb=[(sign*.320,-.095,.741),(sign*.330,-.103,.728),
               (sign*.343,-.106,.718),(sign*.350,-.107,.714)]
        for j,suffix in enumerate(['Proximal','Intermediate','Distal']):
            records.append((side+'Thumb'+suffix,thumb[j],thumb[j+1],
                side+'Hand' if j==0 else side+'Thumb'+['Proximal','Intermediate'][j-1]))
        cape=[(sign*.085,.010,.805),(sign*.130,.030,.590),
              (sign*.155,.045,.370),(sign*.185,.050,.150)]
        for j in range(3):records.append((side+'Cape'+str(j+1),cape[j],cape[j+1],
            'Chest' if j==0 else side+'Cape'+str(j)))
        coat=[(sign*.037,-.134,.628),(sign*.040,-.143,.533),(sign*.044,-.125,.432)]
        for j in range(2):records.append((side+'Coat'+str(j+1),coat[j],coat[j+1],
            'Hips' if j==0 else side+'Coat'+str(j)))
    hair=[(0,.020,.948),(0,.025,.848),(0,.065,.740),(0,.105,.630),(0,.122,.555)]
    for j in range(4):records.append(('HairBack'+str(j+1),hair[j],hair[j+1],
        'Head' if j==0 else 'HairBack'+str(j)))
    assert len(records)==66 and len({r[0] for r in records})==66
    return records


def build_rig(obj, records, inspection, label):
    arm=bpy.data.armatures.new(label+'_RawHumanoid')
    rig=bpy.data.objects.new(label+'_Rig',arm)
    bpy.context.scene.collection.objects.link(rig)
    origin=Vector(inspection['normalization_origin']);height=inspection['height']
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    for name,head,tail,parent in records:
        bone=arm.edit_bones.new(name)
        bone.head=origin+Vector(head)*height;bone.tail=origin+Vector(tail)*height
        if parent:bone.parent=arm.edit_bones[parent]
        bone.use_deform=name!='Root'
        # Preserve consistent bend axes without changing the source rest silhouette.
        if any(part in name for part in ('UpperLeg','LowerLeg','Foot','Toes')):
            bone.align_roll(Vector((0,1,0)))
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.show_in_front=True
    # Identity rig parent leaves the original object matrix and all vertex/UV
    # buffers untouched. Unity receives one real skinned mesh plus named bones.
    original=obj.matrix_world.copy();obj.parent=rig;obj.matrix_world=original
    mod=obj.modifiers.new('Orbis Source Skin','ARMATURE');mod.object=rig
    mod.use_deform_preserve_volume=False  # Match Unity's linear skinning in review.
    return rig


def digit_weights(side, x,y,z, records,label='Polaris'):
    # Geographical hand-only segment selection. No torso/cloth vertex enters this
    # calculation, and no global inverse-nearest-bone assignment is used.
    thumb_test=(z<.729 and y<-.097 and x>.318) if label=='Stella' else (z<.746 and y<-.046 and x>.318)
    if thumb_test:
        finger='Thumb'
    else:
        finger=min(['Index','Middle','Ring','Little'],key=lambda f:
            abs(y-next(r[1][1] for r in records if r[0]==side+f+'Proximal')))
    chain=[r for r in records if r[0].startswith(side+finger)]
    start=abs(chain[0][1][0]);end=abs(chain[-1][2][0])
    amount=smooth(start-.009,start+.011,x)
    t=(x-start)/max(end-start,.001)
    if t<.55:
        digit=mix({side+finger+'Proximal':1},{side+finger+'Intermediate':1},smooth(.28,.55,t))
    else:
        digit=mix({side+finger+'Intermediate':1},{side+finger+'Distal':1},smooth(.60,.84,t))
    return mix({side+'Hand':1},digit,amount)


def lower_connected_regions(normalized,edges,ceiling=.520):
    """Separate actual below-waist surface components, without editing topology."""
    mask=normalized[:,2]<ceiling
    parent=np.arange(len(normalized))
    def find(i):
        while parent[i]!=i:
            parent[i]=parent[parent[i]];i=parent[i]
        return i
    for a,b in edges[mask[edges].all(axis=1)]:
        a,b=find(a),find(b)
        if a!=b:parent[b]=a
    ids=np.flatnonzero(mask);roots=np.array([find(i) for i in ids])
    labels=np.full(len(normalized),'',dtype='<U16');records=[];leg_count=0
    for root in np.unique(roots):
        members=ids[roots==root];points=normalized[members];lo=points.min(0);hi=points.max(0)
        # Source inspection confirms two components reaching the boot sole; all
        # other below-waist surfaces are suspended clothing / adornments.
        is_leg=lo[2]<.005 and max(abs(lo[0]),abs(hi[0]))<.10
        if is_leg:
            region='LeftLeg' if points[:,0].mean()>0 else 'RightLeg';leg_count+=1
        else:region='LowerCloth'
        labels[members]=region
        records.append({'root_vertex':int(root),'vertices':len(members),'region':region,
            'bounds_min_normalized':lo.tolist(),'bounds_max_normalized':hi.tolist()})
    assert leg_count==2,'Expected exactly two independently connected below-waist boot/leg surfaces.'
    return labels,{'ceiling_normalized':ceiling,'components':records,
        'method':'Connected components on original mesh edges below the waist; leg identity requires a continuous surface reaching an actual boot sole. No geometry changes.'}


def coherent_cloth_weights(side,x,y,z):
    if z>.565:back=mix({'Chest':1},{side+'Cape1':1},1-smooth(.65,.79,z))
    elif z>.355:back=mix({side+'Cape1':1},{side+'Cape2':1},1-smooth(.44,.62,z))
    else:back=mix({side+'Cape2':1},{side+'Cape3':1},1-smooth(.20,.43,z))
    front=mix({'Hips':1},{side+'Coat1':1},1-smooth(.596,.654,z))
    front=mix(front,{side+'Coat2':1},1-smooth(.47,.575,z))
    # Smooth surface-space transition between rear cape and front coat chains;
    # no moving knee is introduced simply because cloth crosses its coordinates.
    front_amount=(1-smooth(-.080,-.045,y))*(1-smooth(.075,.110,x))
    result=mix(back,front,front_amount)
    if x<.020:
        other='Right' if side=='Left' else 'Left'
        opposite={n.replace(side,other):v for n,v in result.items()}
        result=mix(opposite,result,.5+.5*smooth(0,.02,x))
    return result


def leg_weights(side,y,z,coherent=False):
    if z>.408:
        a,b=(.470,.526) if coherent else (.493,.558)
        return mix({'Hips':1},{side+'UpperLeg':1},1-smooth(a,b,z))
    if z>.127:
        return mix({side+'UpperLeg':1},{side+'LowerLeg':1},1-smooth(.323,.411,z))
    result=mix({side+'LowerLeg':1},{side+'Foot':1},1-smooth(.077,.135,z))
    return mix(result,{side+'Toes':1},(1-smooth(.026,.052,z))*(1-smooth(-.104,-.074,y)))


def stella_cloth_weights(side,x,y,z):
    if z>.575:back=mix({'Chest':1},{side+'Cape1':1},1-smooth(.650,.805,z))
    elif z>.365:back=mix({side+'Cape1':1},{side+'Cape2':1},1-smooth(.445,.620,z))
    else:back=mix({side+'Cape2':1},{side+'Cape3':1},1-smooth(.210,.445,z))
    front=mix({'Hips':1},{side+'Coat1':1},1-smooth(.585,.642,z))
    front=mix(front,{side+'Coat2':1},1-smooth(.470,.565,z))
    result=mix(back,front,(1-smooth(-.135,-.095,y))*(1-smooth(.082,.125,x)))
    if x<.02:
        other='Right' if side=='Left' else 'Left'
        result=mix({n.replace(side,other):v for n,v in result.items()},result,.5+.5*smooth(0,.02,x))
    return result


def stella_leg_weights(side,y,z):
    if z>.390:return mix({'Hips':1},{side+'UpperLeg':1},1-smooth(.488,.546,z))
    if z>.125:return mix({side+'UpperLeg':1},{side+'LowerLeg':1},1-smooth(.292,.382,z))
    result=mix({side+'LowerLeg':1},{side+'Foot':1},1-smooth(.069,.126,z))
    return mix(result,{side+'Toes':1},(1-smooth(.021,.045,z))*(1-smooth(-.131,-.104,y)))


def stella_hair_weights(z):
    if z>.840:return mix({'Head':1},{'HairBack1':1},1-smooth(.88,.955,z))
    if z>.735:return mix({'HairBack1':1},{'HairBack2':1},1-smooth(.755,.875,z))
    if z>.625:return mix({'HairBack2':1},{'HairBack3':1},1-smooth(.645,.755,z))
    return mix({'HairBack3':1},{'HairBack4':1},1-smooth(.570,.665,z))


def stella_weight_at(px,y,z,records,lower_label):
    side='Left' if px>=0 else 'Right';x=abs(px)
    if lower_label.endswith('Leg'):
        # The cleaned boots intentionally meet/cross the centre plane by fractions
        # of a millimetre. Their connected-surface identity determines side.
        return stella_leg_weights(lower_label[:-3],y,z),'leg'
    if z>=.844:
        result=mix({'Neck':1},{'Head':1},smooth(.835,.866,z))
        if y>.006:result=mix(result,stella_hair_weights(z),smooth(.006,.031,y))
        return result,'head'
    # Source-specific posterior hair envelope: the long strands lie behind the
    # cape, progressively farther backwards towards their waist-length tips.
    hair_separator=.044+max(0,.81-z)*.235
    if .510<z<.844 and y>hair_separator-.012:
        amount=smooth(hair_separator-.012,hair_separator+.012,y)
        return mix(stella_cloth_weights(side,x,y,z),stella_hair_weights(z),amount),'hair'
    if lower_label=='LowerCloth':return stella_cloth_weights(side,x,y,z),'lower_cloth'
    if z>.653 and x>.091:
        line=.800-(x-.095)*.238
        amount=smooth(line-.105,line-.044,z)*(1-smooth(-.046,.017,y))
        if amount<1e-5:return stella_cloth_weights(side,x,y,z),'cape'
        if x<.143:result=mix({'Chest':1},{side+'UpperArm':1},smooth(.090,.149,x))
        elif x<.283:result=mix({side+'UpperArm':1},{side+'LowerArm':1},smooth(.200,.246,x))
        elif x<.335:result=mix({side+'LowerArm':1},{side+'Hand':1},smooth(.292,.319,x))
        else:result=digit_weights(side,x,y,z,records,'Stella')
        result=mix(result,{'Chest':1},smooth(-.045,.010,y)*(1-smooth(.12,.19,x)))
        return mix({side+'Cape1':1},result,amount),'arm' if amount>.999 else 'sleeve_attachment'
    if z<.765 and (y>-.005 or x>.112):return stella_cloth_weights(side,x,y,z),'cape'
    if z<.647 and y<-.116:return stella_cloth_weights(side,x,y,z),'front_coat'
    if z<.555:return stella_leg_weights(side,y,z),'leg'
    if z<.702:return mix({'Hips':1},{'Spine':1},smooth(.616,.700,z)),'torso'
    if z<.816:return mix({'Spine':1},{'Chest':1},smooth(.700,.768,z)),'torso'
    return mix({'Chest':1},{'Neck':1},smooth(.815,.856,z)),'torso'


def stella_connected_arms(normalized,edges):
    # The white bell sleeve and its hanging star are one surface with the hand.
    # Follow that connection instead of treating the low sleeve hem as a cape.
    mask=(np.abs(normalized[:,0])>.14)&(normalized[:,1]<-.02)&(normalized[:,2]>.56)
    parent=np.arange(len(normalized))
    def find(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    for a,b in edges[mask[edges].all(1)]:
        a,b=find(a),find(b)
        if a!=b:parent[b]=a
    ids=np.flatnonzero(mask);roots=np.array([find(i) for i in ids]);labels=np.full(len(normalized),'',dtype='<U12')
    records=[]
    for root in np.unique(roots):
        members=ids[roots==root];pts=normalized[members]
        if np.abs(pts[:,0]).max()<.35:continue
        side='Left' if pts[:,0].mean()>0 else 'Right';labels[members]=side
        records.append({'side':side,'vertices':len(members),'root_vertex':int(root),
            'bounds_min_normalized':pts.min(0).tolist(),'bounds_max_normalized':pts.max(0).tolist()})
    assert len(records)==2
    return labels,{'components':records,'method':'Actual hand-connected surfaces beyond the upper-arm transition, including sleeve hems and hanging stars; source geometry is unchanged.'}


def stella_arm_weights(side,x,y,z,records):
    if x<.143:return mix({'Chest':1},{side+'UpperArm':1},smooth(.090,.149,x))
    if x<.283:return mix({side+'UpperArm':1},{side+'LowerArm':1},smooth(.200,.246,x))
    if x<.335:return mix({side+'LowerArm':1},{side+'Hand':1},smooth(.292,.319,x))
    return digit_weights(side,x,y,z,records,'Stella')


def assign_weights(obj, normalized, records, edges, output, label,profile='v4'):
    if label=='Stella':profile='v6'
    names=[r[0] for r in records if r[0]!='Root'];indices={n:i for i,n in enumerate(names)}
    weights=np.zeros((len(normalized),len(names)),np.float64)
    regions=[]
    lower_labels=np.full(len(normalized),'',dtype='<U16');topology_report=None
    arm_labels=np.full(len(normalized),'',dtype='<U12');arm_report=None
    if profile=='v6':
        lower_labels,topology_report=lower_connected_regions(normalized,edges,.540 if label=='Stella' else .520)
        write_json(output/(label+'_cloth_components.json'),topology_report)
    if label=='Stella':
        arm_labels,arm_report=stella_connected_arms(normalized,edges)
        write_json(output/(label+'_arm_components.json'),arm_report)
    for i,(px,y,z) in enumerate(normalized):
        side='Left' if px>=0 else 'Right';x=abs(px)
        if label=='Stella':
            w,region=stella_weight_at(px,y,z,records,lower_labels[i])
            if arm_labels[i]:
                amount=smooth(.140,.172,x)
                w=mix(w,stella_arm_weights(str(arm_labels[i]),x,y,z,records),amount)
                region='arm' if amount>.999 else 'sleeve_attachment'
        elif lower_labels[i].endswith('Leg'):
            region='leg';w=leg_weights(side,y,z,True)
        elif lower_labels[i]=='LowerCloth':
            region='lower_cloth';w=coherent_cloth_weights(side,x,y,z)
        elif z>=.844:
            w=mix({'Neck':1},{'Head':1},smooth(.844,.870,z));region='head'
            # Only posterior hair below the cranium receives secondary bones;
            # face, ears and forehead retain Head, avoiding facial rubber motion.
            if y>.009 and z>.860 and z<.950:
                hair=mix({'HairBack1':1},{'HairBack2':1},1-smooth(.855,.91,z))
                w=mix(w,hair,smooth(.009,.025,y)*smooth(.855,.875,z))
        elif z>.660 and x>.103:
            arm_line=.799-(x-.104)*.18
            arm_amount=smooth(arm_line-.100,arm_line-.048,z)*(1-smooth(.008,.047,y))
            if arm_amount>1e-5:
                region='arm' if arm_amount>.999 else 'sleeve_attachment'
                if x<.148:
                    w=mix({'Chest':1},{side+'UpperArm':1},smooth(.099,.153,x))
                elif x<.277:
                    w=mix({side+'UpperArm':1},{side+'LowerArm':1},smooth(.195,.247,x))
                elif x<.326:
                    w=mix({side+'LowerArm':1},{side+'Hand':1},smooth(.282,.308,x))
                else:w=digit_weights(side,x,y,z,records)
                # Rear collar mantle should stay on the torso as arms lower.
                mantle=smooth(.004,.029,y)*(1-smooth(.13,.19,x))
                w=mix(w,{'Chest':1},mantle)
                # A short analytic attachment strip follows the actual sleeve /
                # posterior mantle seam. Deep hanging cape remains independent;
                # this strip avoids an abrupt skin-weight cut through fused cloth.
                w=mix({side+'Cape1':1},w,arm_amount)
            else:
                w={side+'Cape1':1};region='cape'
        elif z<.760 and ((z>.160 and y>.025) or (z>.160 and x>.080 and y>.012)
                          or x>.110):
            region='cape'
            if z>.565:w=mix({'Chest':1},{side+'Cape1':1},1-smooth(.65,.79,z))
            elif z>.355:w=mix({side+'Cape1':1},{side+'Cape2':1},1-smooth(.44,.62,z))
            else:w=mix({side+'Cape2':1},{side+'Cape3':1},1-smooth(.20,.43,z))
        elif .435<z<.652 and y<-.080:
            region='front_coat'
            w=mix({'Hips':1},{side+'Coat1':1},1-smooth(.596,.654,z))
            w=mix(w,{side+'Coat2':1},1-smooth(.47,.575,z))
        elif z<.562:
            region='leg'
            if x>.094 and y>-.012 and z>.22:
                # Outer hanging cloth never inherits a nearby moving knee.
                w={side+('Cape2' if z>.35 else 'Cape3'):1};region='cape'
            else:
                w=leg_weights(side,y,z,profile=='v6')
        else:
            region='torso'
            if z<.70:w=mix({'Hips':1},{'Spine':1},smooth(.625,.698,z))
            elif z<.814:w=mix({'Spine':1},{'Chest':1},smooth(.698,.759,z))
            else:w=mix({'Chest':1},{'Neck':1},smooth(.814,.857,z))
        regions.append(region)
        for n,value in w.items():weights[i,indices[n]]=value
    # Smooth across actual shared edges only, never across spatially nearby but
    # disconnected garments/limbs. A small pass softens geographic boundaries.
    count=np.bincount(edges.ravel(),minlength=len(weights)).astype(float)
    safe=np.maximum(count,1)
    arm_cols=[i for i,n in enumerate(names) if any(s in n for s in ['UpperArm','LowerArm','Hand','Proximal','Intermediate','Distal'])]
    leg_cols=[i for i,n in enumerate(names) if any(s in n for s in ['UpperLeg','LowerLeg','Foot','Toes'])]
    cloth=np.isin(regions,['cape','front_coat','lower_cloth','hair'])
    protected_legs=np.char.endswith(lower_labels,'Leg')
    garment_cols=[i for i,n in enumerate(names) if 'Cape' in n or 'Coat' in n]
    for _ in range(8):
        summed=np.zeros_like(weights)
        np.add.at(summed,edges[:,0],weights[edges[:,1]])
        np.add.at(summed,edges[:,1],weights[edges[:,0]])
        weights=.72*weights+.28*summed/safe[:,None]
        # The raw garment is topologically fused in some places. Do not allow
        # graph smoothing to transfer a moving knee/wrist onto hanging fabric.
        forbidden=arm_cols+leg_cols
        leaked=weights[cloth][:,forbidden].sum(axis=1)
        weights[np.ix_(cloth,forbidden)]=0
        cloth_rows=np.flatnonzero(cloth)
        for row,value in zip(cloth_rows,leaked):
            target='Head' if regions[row]=='hair' else ('Hips' if regions[row]=='front_coat' else 'Chest')
            weights[row,indices[target]]+=value
        if profile=='v6':
            # Garment/body barriers are actual separate lower surface components,
            # not arbitrary Euclidean proximity to a knee or an ankle.
            leaked=weights[protected_legs][:,garment_cols].sum(axis=1)
            weights[np.ix_(protected_legs,garment_cols)]=0
            weights[protected_legs,indices['Hips']]+=leaked
        keep=np.argpartition(weights,-4,axis=1)[:,-4:]
        mask=np.zeros_like(weights,dtype=bool);mask[np.arange(len(weights))[:,None],keep]=True
        weights[~mask]=0;weights/=weights.sum(axis=1)[:,None]
    report=write_weights(obj,weights,names,regions,output,label,
        'Source-space anatomical volumes, '+('actual connected below-waist surfaces with garment/body barriers, ' if profile=='v6' else '')+
        'separate cloth/hair regions, analytical joint blends, eight constrained topology-adjacent smoothing iterations, maximum four normalized influences.')
    report['weight_profile']=profile
    if topology_report:report['connected_component_classification']=topology_report
    if arm_report:report['connected_arm_classification']=arm_report
    return report


def write_weights(obj,weights,names,regions,output,label,method):
    groups=[obj.vertex_groups.new(name=str(n)) for n in names]
    for bone_index,group in enumerate(groups):
        for vertex_index in np.flatnonzero(weights[:,bone_index]>1e-7):
            group.add([int(vertex_index)],float(weights[vertex_index,bone_index]),'REPLACE')
    actual_count=np.count_nonzero(weights>1e-7,axis=1)
    np.savez_compressed(output/(label+'_weights.npz'),weights=weights,names=np.array(names),regions=np.array(regions))
    regions=list(regions)
    cloth=np.isin(regions,['cape','front_coat','lower_cloth','hair'])
    arm_cols=[i for i,n in enumerate(names) if any(s in n for s in ['UpperArm','LowerArm','Hand','Proximal','Intermediate','Distal'])]
    leg_cols=[i for i,n in enumerate(names) if any(s in n for s in ['UpperLeg','LowerLeg','Foot','Toes'])]
    return {'vertices':len(weights),'influence_count_max':int(actual_count.max()),
            'unweighted_vertices':int(np.count_nonzero(weights.sum(axis=1)<.99999)),
            'sum_error_max':float(np.abs(weights.sum(axis=1)-1).max()),
            'nonfinite_weights':int(np.count_nonzero(~np.isfinite(weights))),
            'region_counts':{r:regions.count(r) for r in sorted(set(regions))},
            'cloth_arm_weight_max':float(weights[cloth][:,arm_cols].sum(axis=1).max()) if cloth.any() else 0,
            'cloth_leg_weight_max':float(weights[cloth][:,leg_cols].sum(axis=1).max()) if cloth.any() else 0,
            'method':method,
            'finger_chain_weighted_vertex_counts':{
                side+finger:int(np.count_nonzero(weights[:,[j for j,n in enumerate(names) if n.startswith(side+finger)]].sum(axis=1)>1e-7))
                for side in ['Left','Right'] for finger in ['Thumb','Index','Middle','Ring','Little']}}


def inherit_weights(obj,world,records,weights_path,map_path,output,label):
    # Fan splitting changes only vertex indexing/connectivity. Preserve the
    # visually reviewed bind exactly; smoothing again would change its weights.
    old=np.load(weights_path);names=old['names'];weights=old['weights'];regions=old['regions']
    source_geometry=np.load(weights_path.parent/(label+'_geometry.npz'))
    repair=json.loads(map_path.read_text(encoding='utf-8'))
    mapping=np.array(repair['vertex_fan_split']['new_to_source_vertex_indices'],dtype=np.int64)
    assert len(mapping)==len(world) and mapping.min()>=0 and mapping.max()<len(weights)
    assert list(names)==[r[0] for r in records if r[0]!='Root']
    assert np.array_equal(world,source_geometry['world'][mapping]),'Fan transfer changed source-space positions.'
    report=write_weights(obj,weights[mapping].copy(),names,regions[mapping],output,label,
        'Exact previously reviewed V4 weights; new fan-split vertices copy their explicit source vertex weights. No new smoothing or geographic threshold changes.')
    report['transfer']={'weights_source':str(weights_path),'weights_sha256':digest(weights_path),
        'vertex_map':str(map_path),'vertex_map_sha256':digest(map_path),
        'original_vertex_count':len(weights),'new_vertex_count':len(world),
        'duplicated_vertices':repair['vertex_fan_split']['duplicated_vertices'],
        'all_mapped_positions_exact':True,'all_mapped_weights_exact':True}
    return report


def rotate_world(rig, name, axis, degrees):
    bone=rig.pose.bones[name];bone.rotation_mode='QUATERNION'
    rest=bone.bone.matrix_local.to_quaternion()
    local=rest.inverted()@Quaternion(Vector(axis),math.radians(degrees))@rest
    bone.rotation_quaternion=bone.rotation_quaternion@local


def reset_pose(rig):
    for bone in rig.pose.bones:
        bone.rotation_mode='QUATERNION';bone.rotation_quaternion=Quaternion();bone.location=Vector()


def test_pose(rig, name):
    reset_pose(rig)
    if name=='Rest':return
    for side,sign in [('Left',1),('Right',-1)]:
        rotate_world(rig,side+'UpperArm',(0,1,0),sign*69)
        rotate_world(rig,side+'LowerArm',(0,0,1),-sign*7)
    if name=='WalkContact':
        rotate_world(rig,'Hips',(0,0,1),4)
        rotate_world(rig,'Chest',(0,0,1),-8)
        rotate_world(rig,'LeftUpperLeg',(1,0,0),-22)
        rotate_world(rig,'LeftLowerLeg',(1,0,0),25)
        rotate_world(rig,'LeftFoot',(1,0,0),-3)
        rotate_world(rig,'RightUpperLeg',(1,0,0),18)
        rotate_world(rig,'RightLowerLeg',(1,0,0),9)
        rotate_world(rig,'RightFoot',(1,0,0),-27)
        rotate_world(rig,'LeftUpperArm',(1,0,0),14)
        rotate_world(rig,'RightUpperArm',(1,0,0),-12)
        rotate_world(rig,'LeftCape1',(1,0,0),-5)
        rotate_world(rig,'RightCape1',(1,0,0),-3)
    elif name=='JointStress':
        rotate_world(rig,'LeftLowerArm',(0,0,1),-80)
        rotate_world(rig,'RightLowerArm',(0,0,1),80)
        rotate_world(rig,'LeftUpperLeg',(1,0,0),-45)
        rotate_world(rig,'LeftLowerLeg',(1,0,0),80)
        rotate_world(rig,'Chest',(0,0,1),18)
        rotate_world(rig,'Head',(0,0,1),-23)
    bpy.context.view_layer.update()


def pose_metrics(obj, baseline, edges):
    deps=bpy.context.evaluated_depsgraph_get();evaluated=obj.evaluated_get(deps)
    mesh=evaluated.to_mesh()
    values=np.empty((len(mesh.vertices),3),np.float32);mesh.vertices.foreach_get('co',values.ravel())
    matrix=np.asarray(obj.matrix_world,dtype=np.float64)
    points=values@matrix[:3,:3].T+matrix[:3,3]
    base_lengths=np.linalg.norm(baseline[edges[:,1]]-baseline[edges[:,0]],axis=1)
    lengths=np.linalg.norm(points[edges[:,1]]-points[edges[:,0]],axis=1)
    ratio=lengths/np.maximum(base_lengths,1e-8)
    elongation=lengths-base_lengths
    evaluated.to_mesh_clear()
    worst=np.argsort(-ratio)[:12]
    visible=np.flatnonzero((ratio>3)&(elongation>.005))
    visible=visible[np.argsort(-elongation[visible])][:20]
    def edge_record(e):
        return {'indices':edges[e].tolist(),'ratio':float(ratio[e]),
                'rest_length_source_units':float(base_lengths[e]),
                'posed_length_source_units':float(lengths[e]),
                'added_length_source_units':float(elongation[e]),
                'rest_points':baseline[edges[e]].tolist(),'posed_points':points[edges[e]].tolist()}
    return {'nonfinite_positions':int(np.count_nonzero(~np.isfinite(points))),
            'maximum_displacement_source_units':float(np.linalg.norm(points-baseline,axis=1).max()),
            'edge_stretch_p99':float(np.quantile(ratio,.99)),
            'edge_stretch_max':float(ratio.max()),'edges_stretched_over_3x':int(np.count_nonzero(ratio>3)),
            'edge_elongation_max_source_units':float(elongation.max()),
            'edges_elongated_over_003_source_units':int(np.count_nonzero(elongation>.03)),
            'edges_over_3x_and_added_0005_source_units':int(np.count_nonzero((ratio>3)&(elongation>.005))),
            'edges_over_3x_and_added_001_source_units':int(np.count_nonzero((ratio>3)&(elongation>.01))),
            'absolute_threshold_note':'0.005 / 0.01 source units equal 5 / 10 mm only at one source unit per metre. Apply destination character scale before treating these as physical tolerances.',
            'bounds_min':points.min(axis=0).tolist(),'bounds_max':points.max(axis=0).tolist(),
            'worst_edges':[edge_record(e) for e in worst],
            'largest_visible_stretched_edges':[edge_record(e) for e in visible]}


def foot_contacts(rig, normalized, world, report):
    result={};origin=Vector(report['normalization_origin']);height=report['height']
    for side,sign in [('Left',1),('Right',-1)]:
        mask=(normalized[:,0]*sign>.008)&(normalized[:,0]*sign<.099)&(normalized[:,2]<.12)
        pts=world[mask];foot=rig.data.bones[side+'Foot'];toes=rig.data.bones[side+'Toes']
        floor=float(pts[:,2].min());point=Vector((foot.head_local.x,foot.head_local.y,floor))
        local=foot.matrix_local.inverted()@point
        normal=foot.matrix_local.to_quaternion().inverted()@Vector((0,0,1))
        forward=Vector((toes.tail_local.x-toes.head_local.x,toes.tail_local.y-toes.head_local.y,0)).normalized()
        toe_points=pts[pts[:,1]<foot.head_local.y-height*.035]
        heel_points=pts[pts[:,1]>foot.head_local.y-height*.006]
        def contact(points):
            z=float(points[:,2].min());near=points[points[:,2]<z+height*.002]
            return [float(near[:,0].mean()),float(near[:,1].mean()),z]
        result[side]={'foot_rest_world_blender':list(foot.head_local),'toe_rest_world_blender':list(toes.head_local),
                     'sole_floor_z':floor,'sole_plane_point_world_blender':list(point),
                     'sole_point_foot_local':list(local),'sole_normal_foot_local':list(normal),
                     'foot_bone_height_above_sole_source_units':float(foot.head_local.z-floor),
                     'forward_world_blender':list(forward),'toe_contact_world_blender':contact(toe_points),
                     'heel_contact_world_blender':contact(heel_points),'sample_vertices':len(pts),
                     'coordinate_note':'Blender source units/world with identity rig. +Z up, -Y forward. Convert via imported foot transform and destination mesh scale; do not reuse as a Unity Y offset without conversion.'}
    return result


def diagnose_existing(obj,normalized,world,edges,output,label,source,render=False,audit=None):
    rigs=[o for o in bpy.data.objects if o.type=='ARMATURE']
    if len(rigs)!=1:raise ValueError('Existing-rig diagnostics require one armature.')
    rig=rigs[0];cut=.540 if label=='Stella' else .520
    lower,components=lower_connected_regions(normalized,edges,cut)
    mid=normalized[edges].mean(axis=1)
    areas=np.full(len(edges),'torso',dtype='<U24')
    areas[mid[:,2]>.84]='head_and_hair'
    areas[(mid[:,2]>.66)&(mid[:,2]<.84)&(np.abs(mid[:,0])>.103)]='sleeve_and_shoulder'
    areas[(mid[:,2]>cut)&(mid[:,2]<=.66)]='waist_and_upper_coat'
    areas[mid[:,2]<=cut]='lower_legs'
    areas[(mid[:,2]<=cut)&np.any(lower[edges]=='LowerCloth',axis=1)]='lower_clothing'
    if label=='Stella':
        hair_separator=.044+np.maximum(0,.81-mid[:,2])*.235
        areas[(mid[:,2]>.51)&(mid[:,2]<.844)&(mid[:,1]>hair_separator-.012)]='long_hair'
    base=np.linalg.norm(world[edges[:,1]]-world[edges[:,0]],axis=1)
    reports={};positions={}
    for pose in ['Rest','Relaxed','WalkContact','JointStress']:
        test_pose(rig,pose);bpy.context.view_layer.update()
        evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        points,_,_=mesh_arrays(evaluated)
        lengths=np.linalg.norm(points[edges[:,1]]-points[edges[:,0]],axis=1)
        ratio=lengths/np.maximum(base,1e-8);added=lengths-base
        positions[pose]=points
        parts={}
        for area in np.unique(areas):
            rows=np.flatnonzero(areas==area);worst=rows[np.argsort(-added[rows])[:8]]
            parts[str(area)]={'edge_count':len(rows),'stretch_p99':float(np.quantile(ratio[rows],.99)),
                'maximum_added_source_units':float(added[rows].max()),
                'over_3x_plus_0005':int(np.count_nonzero((ratio[rows]>3)&(added[rows]>.005))),
                'over_3x_plus_001':int(np.count_nonzero((ratio[rows]>3)&(added[rows]>.01))),
                'worst_edges':[{'indices':edges[e].tolist(),'rest_normalized':normalized[edges[e]].tolist(),
                    'ratio':float(ratio[e]),'rest_length':float(base[e]),'posed_length':float(lengths[e]),
                    'added_length':float(added[e])} for e in worst]}
        reports[pose]=parts
        if render:
            from audit_raw_meshes import render_studio
            render_studio(output,{'label':label+'_'+pose},1024,fixed_bounds=audit['render_bounds'])
    reset_pose(rig);bpy.context.view_layer.update()
    hand_renders=[]
    if render:
        scene=bpy.context.scene;camera=scene.camera
        lo,hi=Vector(audit['render_bounds']['min']),Vector(audit['render_bounds']['max'])
        height=hi.z-lo.z;origin=(lo+hi)*.5;origin.z=lo.z
        target=origin+Vector((.350,-.084,.738) if label=='Stella' else (.347,-.044,.758))*height
        for curled in [False,True]:
            reset_pose(rig)
            if curled:
                for side,sign in [('Left',1),('Right',-1)]:
                    for finger in ['Thumb','Index','Middle','Ring','Little']:
                        for suffix,angle in [('Proximal',30),('Intermediate',40),('Distal',25)]:
                            rotate_world(rig,side+finger+suffix,(0,1,0),sign*angle*(.65 if finger=='Thumb' else 1))
                bpy.context.view_layer.update()
            for view,direction in [('Front',(0,-1,.13)),('Top',(.08,-.15,1))]:
                camera.location=target+Vector(direction).normalized()*height*2
                camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
                camera.data.ortho_scale=height*.18
                path=output/(label+'_Hand'+('Curl' if curled else 'Rest')+view+'.png')
                scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
                hand_renders.append(str(path))
        reset_pose(rig)
    np.savez_compressed(output/(label+'_diagnostic_pose_positions.npz'),**positions)
    write_json(output/(label+'_semantic_diagnostics.json'),{
        'source':str(source),'source_sha256':digest(source),'read_only_existing_rig':True,
        'source_unit_note':'Apply actual Unity import / character-fit scale; one source unit per metre means .005 is 5mm.',
        'semantic_classification':components,'poses':reports,'hand_test_renders':hand_renders,
        'hand_test_note':'Diagnostic flexion only, not a final weapon grip animation. Source blend is not saved or modified.'})


def export_grounded_existing(obj,world,output,label,source,inspection):
    """Controlled export-only origin test. Never saves or rewrites the source rig."""
    rigs=[o for o in bpy.data.objects if o.type=='ARMATURE']
    if len(rigs)!=1 or obj.parent!=rigs[0]:
        raise ValueError('Origin test expects the reviewed mesh parented directly to one armature.')
    rig=rigs[0];reset_pose(rig);bpy.context.view_layer.update()
    before=integrity(obj);original=rig.matrix_world.copy()
    def bone_signature():
        return hashlib.sha256(np.array([list(row) for bone in rig.data.bones
            for row in bone.matrix_local],dtype=np.float64).tobytes()).hexdigest()
    def weight_signature():
        values=[(v.index,g.group,g.weight) for v in obj.data.vertices for g in v.groups]
        return hashlib.sha256(np.asarray(values,dtype=np.float64).tobytes()).hexdigest()
    bone_hash=bone_signature();weight_hash=weight_signature()
    delta=Vector((0,0,-float(world[:,2].min())))
    anchors_before={n:list(rig.matrix_world@rig.data.bones[n].head_local)
        for n in ['Root','Hips','LeftFoot','RightFoot']}
    fbx=output/(label+'_grounded_object_offset.fbx')
    try:
        rig.matrix_world=Matrix.Translation(delta)@original
        bpy.context.view_layer.update()
        translated,_,_=mesh_arrays(obj)
        translation_error=float(np.abs(translated-world-np.array(delta)).max())
        assert translation_error<1e-6
        assert bone_signature()==bone_hash and weight_signature()==weight_hash
        bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);rig.select_set(True)
        bpy.context.view_layer.objects.active=rig
        bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','ARMATURE'},
            axis_forward='-Z',axis_up='Y',add_leaf_bones=False,primary_bone_axis='Y',secondary_bone_axis='X',
            bake_anim=False,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
            use_mesh_modifiers=True,mesh_smooth_type='FACE',path_mode='COPY',embed_textures=True)
        anchors_export={n:list(rig.matrix_world@rig.data.bones[n].head_local)
            for n in anchors_before}
        bounds_export={'min':translated.min(0).tolist(),'max':translated.max(0).tolist()}
    finally:
        rig.matrix_world=original;bpy.context.view_layer.update()
    assert integrity(obj)==before and bone_signature()==bone_hash and weight_signature()==weight_hash
    write_json(output/(label+'_grounded_export.json'),{
        'status':'Controlled origin hypothesis test only; Unity Avatar/humanScale effect is not yet validated.',
        'source':str(source),'source_sha256':digest(source),'fbx':str(fbx),'fbx_sha256':digest(fbx),
        'method':'Apply a uniform world-space translation to the entire armature object and its child mesh for FBX export; immediately restore it without saving the source blend.',
        'offset_blender_world':list(delta),'source_floor_before':float(world[:,2].min()),
        'source_audit_floor':inspection['normalization_origin'][2],
        'bounds_at_export':bounds_export,'anchors_before_world':anchors_before,
        'anchors_at_export_world':anchors_export,'translation_only_error_max':translation_error,
        'bone_local_matrix_sha256_before_and_after':bone_hash,
        'weights_sha256_before_and_after':weight_hash,'original_mesh_uv_material_transform_restored':True,
        'limitations':['If Unity absorbs the object translation into the Animator root, Animator-relative Hips height may remain unchanged. Measure the imported root/Hips/feet/humanScale before accepting.',
            'No edit-bone, vertex, UV or skin-weight data was baked or changed. No production import was performed.']})


def export_and_review(obj, rig, records, inspection, source, output, label, audit, render, world, edges):
    before=inspection['integrity'];after=integrity(obj)
    assert before==after,'Rigging changed original mesh/UV/material/rest transform buffers.'
    reset_pose(rig);bpy.context.view_layer.update()
    results={'Rest':pose_metrics(obj,world,edges)}
    assert results['Rest']['maximum_displacement_source_units']<1e-5
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.preferences.filepaths.file_preview_type='NONE'
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    blend=output/(label+'_rigged.blend')
    bpy.ops.wm.save_as_mainfile(filepath=str(blend),relative_remap=True)
    fbx=output/(label+'_rigged.fbx')
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','ARMATURE'},
                            axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
                            primary_bone_axis='Y',secondary_bone_axis='X',bake_anim=False,
                            apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
                            use_mesh_modifiers=True,mesh_smooth_type='FACE',path_mode='COPY',embed_textures=True)
    render_records=[]
    for pose in ['Rest','Relaxed','WalkContact','JointStress']:
        test_pose(rig,pose);results[pose]=pose_metrics(obj,world,edges)
        if render:
            from audit_raw_meshes import render_studio
            record={'label':label+'_'+pose}
            render_studio(output,record,1024,fixed_bounds=audit['render_bounds'])
            render_records.append(record)
    reset_pose(rig)
    return {'status':'Rig review prototype; STEP 1 mesh acceptance and Unity Humanoid/retarget not yet certified',
            'weight_profile':'See weight_audit for profile or exact inherited bind source',
            'source':str(source),'source_sha256':digest(source),'output_blend':str(blend),'output_fbx':str(fbx),
            'original_buffers_identical':before==after,'bone_count':len(records),
            'humanoid_contract_bones':52,'secondary_bones':len(records)-52,'bones':[{'name':r[0],'head_normalized':r[1],
            'tail_normalized':r[2],'parent':r[3]} for r in records],
            'pose_metrics':results,'renders':render_records,
            'foot_contact_measurements':foot_contacts(rig,(world-np.array(inspection['normalization_origin']))/inspection['height'],world,inspection),
            'limitations':['Spatial cloth segmentation remains a prototype pending posed inspection.',
                           'Test poses are skinning checks, not final locomotion animations.',
                           'Export existence does not establish Unity Avatar validity or gameplay readiness.']}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--blend', required=True)
    parser.add_argument('--source-report', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--label', choices=['Stella','Polaris'], required=True)
    parser.add_argument('--inspect-only', action='store_true')
    parser.add_argument('--render', action='store_true')
    parser.add_argument('--inherit-weights',type=Path)
    parser.add_argument('--vertex-map',type=Path)
    parser.add_argument('--weight-profile',choices=['v4','v6'],default='v4')
    parser.add_argument('--diagnose-existing',action='store_true')
    parser.add_argument('--export-grounded-existing',action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    source, output = Path(args.blend).resolve(), Path(args.output).resolve()
    if source == output or 'RigReview' not in output.parts:
        raise ValueError('Only a separate RigReview output directory is allowed.')
    output.mkdir(parents=True, exist_ok=True)
    before = digest(source)
    audit = json.loads(Path(args.source_report).read_text(encoding='utf-8'))
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    obj, normalized, world, faces, edges, report = inspect(source, output, args.label, audit)
    if args.export_grounded_existing:
        export_grounded_existing(obj,world,output,args.label,source,report)
        assert digest(source)==before
        print('EXPORT_ONLY_GROUNDED_ORIGIN_COMPLETE '+args.label,flush=True)
        return
    if args.diagnose_existing:
        diagnose_existing(obj,normalized,world,edges,output,args.label,source,args.render,audit)
        assert digest(source)==before
        print('READ_ONLY_RIG_DIAGNOSTICS_COMPLETE '+args.label,flush=True)
        return
    if args.render and args.inspect_only:
        render_inspection(output, args.label, audit, report)
    if not args.inspect_only:
        records=calibrate_bones(args.label)
        rig=build_rig(obj,records,report,args.label)
        if args.inherit_weights:
            if not args.vertex_map:raise ValueError('Exact weight inheritance requires an explicit vertex map.')
            weights=inherit_weights(obj,world,records,args.inherit_weights.resolve(),args.vertex_map.resolve(),output,args.label)
        else:
            weights=assign_weights(obj,normalized,records,edges,output,args.label,args.weight_profile)
        rig_report=export_and_review(obj,rig,records,report,source,output,args.label,audit,args.render,world,edges)
        rig_report['weight_audit']=weights
        write_json(output/(args.label+'_rig_report.json'),rig_report)
    assert digest(source) == before
    print('RIG_INSPECTION_COMPLETE '+args.label, flush=True)


if __name__ == '__main__':
    main()
