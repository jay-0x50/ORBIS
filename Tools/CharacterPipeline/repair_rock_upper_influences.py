"""Remove measured leg-weight leakage above the RockBoss hip, candidate only.

The source hip starts at Z=-0.14; all-leg upper-thigh armor reaches Z=-0.055.
The confirmed warning vertices are around Z=0.14 and Z=0.38. An 0.08-unit
smooth transition above measured Spine start Z=0.02 limits the change to
the torso/shoulder decoration; hips and upper thighs remain untouched.
No foot, rest position, bone, UV or map
is changed. This is a repair candidate until repeat motion and visual QA.
"""
import argparse,copy,json,sys
from pathlib import Path
import bpy,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import sha256,array,write_json
from rig_bosses import geometry_fingerprint,packed_maps

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--source',required=True)
    parser.add_argument('--expected-sha256',required=True);parser.add_argument('--output',required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    source=Path(args.source);folder=Path(args.output).resolve()
    output=folder/'RockBoss_rig_review.blend'
    assert sha256(source)==args.expected_sha256 and not output.exists() and 'Assets' not in folder.parts
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    assert rig.name=='RockBoss_GenericRig'
    geometry=geometry_fingerprint(obj.data);maps=packed_maps()
    bones={b.name:{'matrix':[list(row) for row in b.matrix_local],
           'parent':b.parent.name if b.parent else None,'connect':b.use_connect,
           'head':list(b.head_local),'tail':list(b.tail_local)} for b in rig.data.bones}
    positions=array(obj.data.vertices,'co',(len(obj.data.vertices),3))
    names=[g.name for g in obj.vertex_groups]
    original=np.zeros((len(positions),len(names)),np.float64)
    for v in obj.data.vertices:
        for g in v.groups:original[v.index,g.group]=g.weight
    columns=np.array([i for i,n in enumerate(names) if n.startswith('Leg.')])
    remaining=np.array([i for i,n in enumerate(names) if not n.startswith('Leg.')])
    hip=max(rig.data.bones[n].head_local.z for n in ['Leg.L1','Leg.R1'])
    spine=rig.data.bones['Spine'].head_local.z
    transition=.08 # Technical default: lower-spine band above intact upper-thigh armor.
    factor=np.clip((positions[:,2]-spine)/transition,0,1);factor=factor*factor*(3-2*factor)
    leg_mass=original[:,columns].sum(axis=1)
    removed=leg_mass*factor
    selected=np.flatnonzero(removed>1e-8)
    corrected=original.copy()
    corrected[:,columns]*=(1-factor[:,None])
    other_mass=original[:,remaining].sum(axis=1)
    fallback=selected[other_mass[selected]<1e-12]
    # Unexpected all-leg upper vertices are not assigned a guessed replacement.
    if len(fallback):
        print('ALL_LEG_REGION',len(fallback),'min',positions[fallback].min(axis=0).tolist(),
              'max',positions[fallback].max(axis=0).tolist(),'samples',positions[fallback[:12]].tolist(),flush=True)
        raise RuntimeError('Upper-body all-leg vertices need anatomical inspection: '+str(fallback[:30]))
    # A leaking Leg influence belongs to the torso parent at these heights,
    # not proportionally to whatever nearby Arm already won a Gaussian score.
    # Proportional redistribution amplified an Arm/Spine seam in rejected V7.
    chest_start=rig.data.bones['Chest'].head_local.z
    chest_mix=np.clip((positions[:,2]-(chest_start-.05))/.10,0,1)
    chest_mix=chest_mix*chest_mix*(3-2*chest_mix)
    corrected[:,names.index('Spine')]+=removed*(1-chest_mix)
    corrected[:,names.index('Chest')]+=removed*chest_mix
    corrected[selected]/=corrected[selected].sum(axis=1,keepdims=True)
    # Removing a remote limb exposes two existing abrupt Arm/Spine seams.
    # Smooth only measured warning neighborhoods through mesh adjacency; the
    # kernel vanishes at each radius, and no below-spine weights are touched.
    # The centers are exact rest midpoints from LocalizedEdgeWarnings.json.
    centers=[] # Parent-core redistribution is tested before optional neighbor smoothing.
    blend=np.zeros(len(positions))
    for center in centers:
        u=np.clip(1-np.linalg.norm(positions-np.array(center),axis=1)/.12,0,1)
        blend=np.maximum(blend,u*u*(3-2*u))
    blend*=np.clip((positions[:,2]-spine)/.04,0,1)
    active=np.flatnonzero(blend>0)
    fixed_leg=corrected[:,columns].copy()
    edges=array(obj.data.edges,'vertices',(len(obj.data.edges),2),np.int32)
    degree=np.bincount(edges.ravel(),minlength=len(positions))
    for _ in range(32 if len(active) else 0):
        average=np.zeros_like(corrected)
        for column in range(len(names)):
            total=np.bincount(edges[:,0],weights=corrected[edges[:,1],column],minlength=len(positions))
            total+=np.bincount(edges[:,1],weights=corrected[edges[:,0],column],minlength=len(positions))
            average[:,column]=total/np.maximum(degree,1)
        corrected[active]=corrected[active]*(1-blend[active,None]) + average[active]*blend[active,None]
        corrected[np.ix_(active,columns)]=fixed_leg[active]
        corrected[np.ix_(active,remaining)]*=((1-fixed_leg[active].sum(axis=1)) /
            np.maximum(corrected[np.ix_(active,remaining)].sum(axis=1),1e-12))[:,None]
    selected=np.union1d(selected,active)
    # Runtime contract is four bone influences. Preserve unchanged rows exactly.
    for index in selected:
        keep=np.argsort(corrected[index])[-4:]
        mask=np.ones(len(names),dtype=bool);mask[keep]=False;corrected[index,mask]=0
    corrected[selected]/=corrected[selected].sum(axis=1,keepdims=True)
    for group in obj.vertex_groups:group.remove(selected.tolist())
    for index in selected:
        for column in np.flatnonzero(corrected[index]>1e-8):
            obj.vertex_groups[int(column)].add([int(index)],float(corrected[index,column]),'REPLACE')
    bpy.context.view_layer.update()
    actual=np.zeros_like(original)
    for v in obj.data.vertices:
        for g in v.groups:actual[v.index,g.group]=g.weight
    changed=np.flatnonzero(np.abs(actual-original).max(axis=1)>1e-8)
    check={'geometryUvMaterials':geometry_fingerprint(obj.data)==geometry,
           'packedMaps':packed_maps()==maps,
           'bones':bones=={b.name:{'matrix':[list(row) for row in b.matrix_local],
               'parent':b.parent.name if b.parent else None,'connect':b.use_connect,
               'head':list(b.head_local),'tail':list(b.tail_local)} for b in rig.data.bones},
           'belowSpineWeightsExact':bool(np.array_equal(actual[positions[:,2]<=spine],original[positions[:,2]<=spine])),
           'finite':bool(np.isfinite(actual).all()),'fourInfluences':bool(((actual>0).sum(axis=1)<=4).all()),
           'normalized':bool(np.abs(actual.sum(axis=1)-1).max()<1e-5)}
    assert all(check.values()),check
    folder.mkdir(parents=True,exist_ok=True)
    np.savez_compressed(folder/'upper_influence_comparison.npz',coordinates=positions,original=original,
                        corrected=actual,bone_names=np.array(names),changed_indices=changed)
    report={'source':str(source),'sourceSha256':args.expected_sha256,'candidate':str(output),
        'measuredHipStartZ':hip,'measuredSpineStartZ':spine,'fullLegExclusionAboveZ':spine+transition,
        'transitionNote':'Smoothstep over 0.08 source units above measured Spine start; removed Leg influence transfers to measured Spine/Chest parent, not neighboring Arm. Hips/thighs/legs unchanged.',
        'changedVertices':len(changed),'removedLegMassMaximum':float(removed.max()),
        'allLegFallbackVertices':len(fallback),'checks':check,'gameReady':False,
        'localAdjacencyRelaxation':{'centers':centers,'radius':.12,'iterations':32 if len(active) else 0,'vertices':len(active)},
        'scope':'Skin weights only. No geometry, UV, maps, bones, root transforms or authored Actions modified.'}
    review=json.loads(source.with_name('rig_review.json').read_text(encoding='utf-8'))
    review=copy.deepcopy(review);review['local_revision']=report
    review['weights']['max_weight_sum_error']=float(np.abs(actual.sum(axis=1)-1).max())
    review['weights']['local_weight_rules'].append({'kind':'upper_body_leg_exclusion',
        'spine_start_z':spine,'full_above_z':spine+transition,'changed_vertices':len(changed)})
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(output),compress=True)
    report['candidateSha256']=sha256(output);report['sourceUnchanged']=sha256(source)==args.expected_sha256
    write_json(folder/'UpperInfluenceRepair.json',report);write_json(folder/'rig_review.json',review)
    print('ROCK_UPPER_REPAIR',len(changed),'checks',check,'sha',report['candidateSha256'],flush=True)

if __name__=='__main__':main()
