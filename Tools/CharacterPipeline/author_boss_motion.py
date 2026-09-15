"""Original six-slot Generic boss motion candidates, outside Assets only.

Authored joint curves plus measured two-link foot placement. Raw/rest FBX and
rig files remain immutable. These are candidates until material-motion QA.
"""
import argparse,json,math,sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Matrix,Quaternion,Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import sha256,write_json,array
from rig_bosses import geometry_fingerprint,evaluated_positions

FPS=30
# Provisional authoring durations, not gameplay cooldown or damage timing.
CLIPS=[('Idle',96,True),('Move',48,True),('Attack',90,False),
       ('Hurt',12,False),('Exposed',72,True),('Dead',60,False)]

def smooth(t):
    t=max(0,min(1,t));return t*t*(3-2*t)

def keyed(t,points):
    for (a,x),(b,y) in zip(points,points[1:]):
        if t<=b:return x+(y-x)*smooth((t-a)/(b-a))
    return points[-1][1]

def reset(rig):
    for p in rig.pose.bones:
        p.rotation_mode='QUATERNION';p.location=(0,0,0)
        p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)

def local(rig,name,axis,degrees):
    if name in rig.pose.bones:
        p=rig.pose.bones[name]
        p.rotation_quaternion=p.rotation_quaternion@Quaternion(Vector(axis),math.radians(degrees))

def world_shift(rig,name,delta):
    bpy.context.view_layer.update()
    p=rig.pose.bones[name];m=p.matrix.copy();m.translation+=Vector(delta);p.matrix=m
    bpy.context.view_layer.update()

def world_rotate(rig,name,axis,degrees):
    bpy.context.view_layer.update()
    p=rig.pose.bones[name];m=p.matrix.copy()
    rotation=Quaternion(Vector(axis),math.radians(degrees))@m.to_quaternion()
    p.matrix=Matrix.LocRotScale(m.translation,rotation,Vector((1,1,1)))
    bpy.context.view_layer.update()

def set_segment(p,head,tail,reference):
    direction=reference.to_3x3()@Vector((0,1,0))
    q=direction.rotation_difference(tail-head)@reference.to_quaternion()
    p.matrix=Matrix.LocRotScale(head,q,Vector((1,1,1)))
    bpy.context.view_layer.update()

def measured_leg_ik(rig,foot,goal,rest_matrices):
    """Two exact measured bone lengths; pole comes from original joint bend."""
    names=[foot['branch']+str(i) for i in [1,2,3]]
    upper,lower,toe=[rig.pose.bones[n] for n in names]
    parent=upper.parent
    delta=parent.matrix@rest_matrices[parent.name].inverted()
    hip=delta@rig.data.bones[names[0]].head_local
    old_knee=delta@rig.data.bones[names[1]].head_local
    old_ankle=delta@rig.data.bones[names[2]].head_local
    first=rig.data.bones[names[0]].length;second=rig.data.bones[names[1]].length
    axis=goal-hip;requested=axis.length
    axis.normalize()
    distance=min(max(requested,abs(first-second)+.00001),first+second-.00001)
    reached=hip+axis*distance
    original_axis=(old_ankle-hip).normalized()
    pole=(old_knee-hip)-original_axis*(old_knee-hip).dot(original_axis)
    pole-=axis*pole.dot(axis)
    if pole.length<1e-6:
        pole=delta.to_3x3()@Vector((0,-1,0));pole-=axis*pole.dot(axis)
    pole.normalize()
    projection=(first*first-second*second+distance*distance)/(2*distance)
    knee=hip+axis*projection+pole*math.sqrt(max(first*first-projection*projection,0))
    set_segment(upper,hip,knee,delta@rest_matrices[upper.name])
    set_segment(lower,knee,reached,delta@rest_matrices[lower.name])
    # Original world foot orientation keeps the measured sole plane parallel
    # to its source. No arbitrary ankle-height/roll guess is introduced.
    toe.matrix=Matrix.LocRotScale(reached,rest_matrices[toe.name].to_quaternion(),Vector((1,1,1)))
    bpy.context.view_layer.update()
    return {'branch':foot['branch'],'reach_error':(goal-reached).length}

def foot_goal(foot,plane,clip,t,label):
    point=Vector(foot['ankle_rest'])
    point.z+=plane-foot['measured_sole_rest_local'][2]
    if clip!='Move':return point,True
    # In-place authored gait: root travel is omitted; stance Y velocity is
    # reported as nominal travel speed, never silently applied to field AI.
    branch=foot['branch'];side=branch.endswith('.L')
    if label=='FireBoss':
        group=(branch.startswith('Front') and side) or (branch.startswith('Middle') and not side) or (branch.startswith('Rear') and side)
        phase=0 if group else .5;stride=.13;height=.028;duty=.65
    elif label=='WindBoss':
        phase=0 if (branch.startswith('Front')==side) else .5
        stride=.14;height=.034;duty=.62
    elif label=='LightningBoss':
        phase={'RearLeg.L':0,'FrontLeg.L':.25,'RearLeg.R':.5,'FrontLeg.R':.75}[branch]
        stride=.13;height=.030;duty=.68
    else:
        phase=0 if side else .5;stride=.16;height=.033;duty=.62
    phase=(t+phase)%1
    stance=phase<duty
    if stance:point.y+=stride*(phase/duty-.5)
    else:
        swing=(phase-duty)/(1-duty)
        point.y+=stride*(.5-smooth(swing));point.z+=height*math.sin(math.pi*swing)
    return point,stance

def fit_measured_reach(rig,feet,goals):
    """Derive a pelvis crouch from actual lengths and support plane, not an offset guess."""
    required=0
    for foot,goal in zip(feet,goals):
        upper=rig.pose.bones[foot['branch']+'1']
        first=rig.data.bones[foot['branch']+'1'].length
        second=rig.data.bones[foot['branch']+'2'].length
        distance=goal-upper.head
        horizontal=distance.x**2+distance.y**2
        radius=first+second-.001
        if horizontal>=radius**2:raise RuntimeError('Authored stride exceeds measured limb reach; reduce stride.')
        allowed_z=math.sqrt(radius*radius-horizontal)
        required=max(required,upper.head.z-goal.z-allowed_z)
    if required>0:world_shift(rig,'Pelvis',(0,0,-required))
    return required

def pose_curves(rig,label,clip,t,tail_clearance):
    """Species-specific original curves; no animation/shape borrowed elsewhere."""
    wave=math.sin(math.tau*t)
    attack=keyed(t,[(0,0),(.22,-.65),(1/3,1),(.43,.72),(.75,0),(1,0)]) if clip=='Attack' else 0
    hurt=math.sin(math.pi*min(t/.32,1))*math.exp(-3*t) if clip=='Hurt' else 0
    dead=smooth(t/.78) if clip=='Dead' else 0
    exposed=1 if clip=='Exposed' else 0
    move=clip=='Move';idle=clip=='Idle'
    if label=='WaterBoss':
        rate=1 if move else .5
        for i in range(1,7):
            amplitude=(2.8 if move else .85)*(1+i*.12)
            angle=amplitude*math.sin(math.tau*t-(i-1)*.55)
            if clip in ['Attack','Hurt']:angle*=.35
            if dead:angle=angle*(1-dead)+3*dead
            local(rig,'Tail'+str(i),(0,0,1),angle)
        for side,sign in [('L',1),('R',-1)]:
            local(rig,'Pectoral.'+side+'1',(0,1,0),sign*(wave*(5 if move else 2)+exposed*4+dead*10))
            local(rig,'Pelvic.'+side,(1,0,0),wave*(3 if move else 1.4))
        local(rig,'Jaw',(1,0,0),keyed(t,[(0,0),(.24,10),(1/3,0),(.45,2),(1,0)]) if clip=='Attack' else exposed*4+dead*7)
        local(rig,'Neck',(1,0,0),-5*attack+3*hurt+10*dead)
        local(rig,'Head',(1,0,0),3*attack+4*hurt)
        world_shift(rig,'Body',(0,-.026*attack,.003*wave*(1-dead)))
        world_rotate(rig,'Body',(0,1,0),18*dead)
        return
    root_body='Pelvis'
    # Subtle presentation offsets are absorbed by measured limb IK. Root's
    # local transform remains identity in every authored frame.
    drop=-.0015*wave if idle else -.006*math.cos(math.tau*t*2) if move else -.015*max(-attack,0)
    drop-=exposed*.015+dead*({'FireBoss':.040,'RockBoss':.060,'WindBoss':.040,'LightningBoss':.035}[label])
    world_shift(rig,root_body,(0,0,drop))
    if label=='RockBoss':
        local(rig,'Chest',(1,0,0),.35*wave+5*attack+6*hurt+16*dead+exposed*4)
        for side,sign in [('L',1),('R',-1)]:
            local(rig,'Arm.'+side+'1',(1,0,0),sign*5*wave if move else -10*max(-attack,0)+6*max(attack,0)+6*dead)
            local(rig,'Arm.'+side+'2',(1,0,0),20*max(-attack,0)+9*max(attack,0)+12*dead+exposed*7)
        return
    local(rig,'Spine',(1,0,0),.35*wave+3*attack+4*hurt+8*dead)
    if label=='FireBoss':
        local(rig,'Chest',(1,0,0),5*attack+5*hurt+8*dead+exposed*3)
        local(rig,'Neck',(1,0,0),-7*attack+8*dead)
        for i in range(2,7):local(rig,'Tail'+str(i),(0,0,1),(.8 if move else .4)*math.sin(math.tau*t-i*.6)*(1-dead))
    else:
        local(rig,'Neck',(1,0,0),-6*attack+7*dead+exposed*3)
        local(rig,'Head',(1,0,0),5*attack+5*hurt+9*dead)
        for side,sign in [('L',1),('R',-1)]:
            local(rig,'Wing.'+side+'1',(0,1,0),sign*((2 if move else .7)*wave+6*attack+8*dead+exposed*3))
        for i in range(2,6):local(rig,'Tail'+str(i),(0,0,1),(.9 if move else .35)*math.sin(math.tau*t-i*.5)*(1-dead))
        if tail_clearance:world_rotate(rig,'Tail1',(1,0,0),tail_clearance)

def skin_vertex(rig,obj,index,rest_matrices):
    vertex=obj.data.vertices[index];result=Vector((0,0,0))
    for weight in vertex.groups:
        name=obj.vertex_groups[weight.group].name
        result+=(rig.pose.bones[name].matrix@rest_matrices[name].inverted()@vertex.co)*weight.weight
    return result

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--manifest',required=True)
    parser.add_argument('--only',required=True);parser.add_argument('--output',required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    source_manifest=json.loads(Path(args.manifest).read_text(encoding='utf-8-sig'))
    entry=next(e for e in source_manifest['entries'] if e['label']==args.only)
    source=Path(entry['rig']);label=args.only
    if sha256(source)!=entry['rigSha256']:raise RuntimeError('Frozen rig changed.')
    folder=Path(args.output).resolve()/label
    if 'Assets' in folder.parts or (folder/(label+'_motion_review.blend')).exists():raise RuntimeError('Separate new candidate output required.')
    folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    before=geometry_fingerprint(obj.data)
    rig_report=json.loads(source.with_name('rig_review.json').read_text(encoding='utf-8'))
    feet=rig_report['foot_metadata'];plane=entry['origin']['source_reference_z']
    matrices={b.name:b.matrix_local.copy() for b in rig.data.bones}
    rest=array(obj.data.vertices,'co',(len(obj.data.vertices),3))
    edges=array(obj.data.edges,'vertices',(len(obj.data.edges),2),np.int32)
    rest_lengths=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)
    scene=bpy.context.scene;scene.render.fps=FPS
    rig.animation_data_create();clearance=0;tail_floor_samples=[]
    if label=='WindBoss':
        # Solve the existing low tail clearance through a pose, never rest
        # edits. Only measured tail-majority vertices are used for this gate.
        names={g.index:g.name for g in obj.vertex_groups}
        tail_indices=[v.index for v in obj.data.vertices if sum(g.weight for g in v.groups if names[g.group].startswith('Tail'))>.5]
        tail_floor_samples=sorted(tail_indices,key=lambda i:rest[i,2])[:64]
        for angle in range(0,21):
            reset(rig);world_rotate(rig,'Tail1',(1,0,0),angle)
            co=evaluated_positions(obj)
            if float(co[tail_indices,2].min())>=plane+.010:
                clearance=angle;break
        if not clearance:raise RuntimeError('Tail clearance needs manual review above 20 degrees.')
    report={'label':label,'status':'Authored motion candidate; actual material-motion and Unity binding QA pending.',
        'restRig':str(source),'restRigSha256':entry['rigSha256'],'restFbx':entry['fbx'],'restFbxSha256':entry['sha256'],
        'rigObjectName':rig.name,'rootBone':'Root','fps':FPS,'originContract':entry['origin'],
        'tailClearanceBaseDegreesWorldX':clearance,'clips':[],'gameReady':False}
    upper=[b.name for b in rig.data.bones if b.name not in ['Root','Pelvis'] and not any(k in b.name for k in ['Leg','Tail'])]
    report['hurtBoneNames']=upper
    for clip,end,loop in CLIPS:
        rig.animation_data.action=None;reset(rig)
        action=bpy.data.actions.new(label+'_'+clip);action.use_fake_user=True
        rig.animation_data.action=action
        diagnostics=[];sole_records=[];matrix_endpoints=[];reach_max=0;fit_drop_max=0;sole_error_max=0;extra_tail_max=0
        for frame in range(end+1):
            scene.frame_set(frame);reset(rig)
            t=frame/end;pose_curves(rig,label,clip,t,clearance)
            bpy.context.view_layer.update()
            targets=[foot_goal(f,plane,clip,t,label) for f in feet]
            goals=[g.copy() for g,stance in targets]
            desired_soles=[Vector(f['measured_sole_rest_local'])+g-Vector(f['ankle_rest']) for f,g in zip(feet,goals)]
            if feet:fit_drop_max=max(fit_drop_max,fit_measured_reach(rig,feet,goals))
            # Actual skinned sole can include some lower-leg weight. Correct
            # the measured contact point through small IK goal feedback; the
            # rest mesh, actual bone lengths and weights stay unchanged.
            for iteration in range(3):
                for foot,goal in zip(feet,goals):
                    ik=measured_leg_ik(rig,foot,goal,matrices)
                    if iteration==2:reach_max=max(reach_max,ik['reach_error'])
                if iteration<2:
                    for i,foot in enumerate(feet):
                        actual=skin_vertex(rig,obj,foot['measured_sole_vertex_index'],matrices)
                        goals[i]-=actual-desired_soles[i]
                    if feet:fit_drop_max=max(fit_drop_max,fit_measured_reach(rig,feet,goals))
            for i,foot in enumerate(feet):
                sole=skin_vertex(rig,obj,foot['measured_sole_vertex_index'],matrices)
                sole_error_max=max(sole_error_max,(sole-desired_soles[i]).length)
                if targets[i][1]:sole_records.append({'frame':frame,'foot':foot['branch'],'zFromSupport':sole.z-plane})
            if tail_floor_samples:
                extra=0
                for _ in range(12):
                    tips=[skin_vertex(rig,obj,i,matrices) for i in tail_floor_samples]
                    tip=min(tips,key=lambda p:p.z)
                    if tip.z>=plane+.008:break
                    if extra>=20:raise RuntimeError('Dynamic tail clearance exceeds bounded review angle.')
                    lever=max(abs(tip.y-rig.pose.bones['Tail1'].head.y),.1)
                    increment=math.degrees(math.atan2(plane+.0082-tip.z,lever))
                    increment=min(3,max(.005,increment))
                    world_rotate(rig,'Tail1',(1,0,0),increment);extra+=increment
                extra_tail_max=max(extra_tail_max,extra)
            for p in rig.pose.bones:
                p.keyframe_insert('location',frame=frame,group=p.name)
                p.keyframe_insert('rotation_quaternion',frame=frame,group=p.name)
                p.keyframe_insert('scale',frame=frame,group=p.name)
            if frame in [0,end]:matrix_endpoints.append(np.array([list(p.matrix_basis) for p in rig.pose.bones]))
            if frame in sorted(set([0,end//4,end//3,end//2,end*3//4,end])):
                co=evaluated_positions(obj)
                delta=np.linalg.norm(co[edges[:,0]]-co[edges[:,1]],axis=1)-rest_lengths
                significant=rest_lengths>.0001
                ratios=(delta+rest_lengths)/np.maximum(rest_lengths,1e-12)
                diagnostics.append({'frame':frame,'nonfinite':int((~np.isfinite(co)).sum()),
                    'maximumEdgeElongation':float(delta.max()),'edgeRatioP99':float(np.percentile(ratios[significant],99)),
                    'edgesOver2x':int((ratios[significant]>2).sum()),'minimumZ':float(co[:,2].min())})
        # Set an explicit complete frame range for FBX multi-action baking.
        action.use_frame_range=True;action.frame_start=0;action.frame_end=end
        clip_record={'slot':clip,'action':action.name,'startFrame':0,'endFrame':end,'fps':FPS,
            'duration':end/FPS,'loop':loop,'contactFrame':30 if clip=='Attack' else None,
            'contactNormalized':1/3 if clip=='Attack' else None,'rootLocalMotion':False,
            'loopEndpointMatrixError':float(np.abs(matrix_endpoints[0]-matrix_endpoints[1]).max()) if loop else None,
            'maximumIkReachError':reach_max,'maximumMeasuredReachCrouch':fit_drop_max,
            'maximumAdditionalTailClearanceDegrees':extra_tail_max,
            'maximumSkinnedSoleTargetError':sole_error_max,'stanceSoleMinimum':min((r['zFromSupport'] for r in sole_records),default=None),
            'stanceSoleMaximum':max((r['zFromSupport'] for r in sole_records),default=None),
            'deformationSamples':diagnostics,'hurtBoneNames':upper if clip=='Hurt' else [],
            'moveNote':'In-place preview cycle. No field AI/root translation. Stance horizontal movement corresponds to omitted nominal forward travel.' if clip=='Move' else None}
        report['clips'].append(clip_record)
        write_json(folder/(clip+'_diagnostics.json'),{'clip':clip_record,'stanceSoles':sole_records})
        write_json(folder/'motion_manifest.json',report)
        print('BOSS_MOTION_AUTHORED',label,clip,'reach',round(reach_max,6),'edgeMax',round(max(d['maximumEdgeElongation'] for d in diagnostics),6),flush=True)
    if geometry_fingerprint(obj.data)!=before:raise RuntimeError('Original rest surface/UV changed.')
    report['geometryUvMaterialFingerprint']=before;report['originalRigUnchanged']=sha256(source)==entry['rigSha256']
    scene.frame_set(0);rig.animation_data.action=bpy.data.actions[label+'_Idle'];reset(rig)
    scene.frame_set(1);scene.frame_set(0)
    bpy.context.preferences.filepaths.save_version=0
    blend=folder/(label+'_motion_review.blend');bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
    report['reviewBlend']=str(blend);report['reviewBlendSha256']=sha256(blend)
    write_json(folder/'motion_manifest.json',report)

if __name__=='__main__':main()
