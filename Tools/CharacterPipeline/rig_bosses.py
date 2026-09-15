"""CPU-only Generic boss rig review framework; never modifies source candidates.

Inspect writes measured mesh sections first. Build requires an explicit per-boss
landmark JSON, preserves geometry/UV/maps, and records weight/rest/pose diagnostics.
Outputs are review artifacts outside Assets; no automatic Unity publication.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import array,sha256,write_json


def geometry_fingerprint(mesh):
    mesh.calc_loop_triangles()
    records={
        'positions':array(mesh.vertices,'co',(len(mesh.vertices),3)),
        'loops':array(mesh.loops,'vertex_index',(len(mesh.loops),),np.int32),
        'polygon_materials':array(mesh.polygons,'material_index',(len(mesh.polygons),),np.int32),
    }
    records.update({'uv:'+uv.name:array(uv.data,'uv',(len(uv.data),2)) for uv in mesh.uv_layers})
    return {name:hashlib.sha256(data.tobytes()).hexdigest() for name,data in records.items()}


def packed_maps():
    result=[]
    for image in bpy.data.images:
        if image.source!='FILE':continue
        packed=[entry.packed_file for entry in image.packed_files]
        if not packed and image.packed_file:packed=[image.packed_file]
        result.append({'name':image.name,'colorspace':image.colorspace_settings.name,
            'sha256':[hashlib.sha256(p.data).hexdigest() for p in packed]})
    return result


def section_components(coordinates,edges,axis,center,half_width):
    """Actual mesh graph in a narrow slab, not a guessed anatomical segmentation."""
    mask=np.abs(coordinates[:,axis]-center)<=half_width
    selected=np.flatnonzero(mask)
    parent=np.arange(len(coordinates),dtype=np.int32)
    def find(i):
        while parent[i]!=i:
            parent[i]=parent[parent[i]];i=parent[i]
        return i
    for left,right in edges[mask[edges].all(axis=1)]:
        a,b=find(left),find(right)
        if a!=b:parent[a]=b
    grouped={}
    for index in selected:grouped.setdefault(find(index),[]).append(int(index))
    result=[]
    for indices in sorted(grouped.values(),key=len,reverse=True)[:16]:
        points=coordinates[indices]
        result.append({'vertices':len(indices),'centroid':points.mean(axis=0).tolist(),
            'min':points.min(axis=0).tolist(),'max':points.max(axis=0).tolist()})
    return {'axis':'XYZ'[axis],'center':float(center),'half_width':float(half_width),
        'selected_vertices':int(mask.sum()),'components':result}


def open_candidate(entry):
    path=Path(entry['candidate']).resolve()
    if sha256(path)!=entry['candidateSha256']:
        raise RuntimeError('Frozen candidate hash changed: '+entry['label'])
    bpy.ops.wm.open_mainfile(filepath=str(path),load_ui=False,use_scripts=False)
    meshes=[obj for obj in bpy.context.scene.objects if obj.type=='MESH']
    if len(meshes)!=1 or any(m.type=='ARMATURE' for m in meshes[0].modifiers):
        raise RuntimeError('Expected one unrigged frozen mesh candidate.')
    return path,meshes[0]


def inspect(entry,output):
    path,obj=open_candidate(entry)
    co=array(obj.data.vertices,'co',(len(obj.data.vertices),3))
    edges=array(obj.data.edges,'vertices',(len(obj.data.edges),2),np.int32)
    obj.data.calc_loop_triangles()
    tris=array(obj.data.loop_triangles,'vertices',(len(obj.data.loop_triangles),3),np.int32)
    lo,hi=co.min(axis=0),co.max(axis=0)
    extent=float((hi-lo).max())
    folder=output/entry['label'];folder.mkdir(parents=True,exist_ok=True)
    np.savez_compressed(folder/'measured_mesh.npz',coordinates=co,triangles=tris,edges=edges)
    report={'label':entry['label'],'candidate':str(path),'candidate_sha256':sha256(path),
        'space':'Original Blender object-local axes: +Z up, -Y forward, +X left; no import scale applied.',
        'bounds':{'min':lo.tolist(),'max':hi.tolist()},'maximum_extent':extent,
        'object_matrix':[list(row) for row in obj.matrix_world],
        'geometry_fingerprint':geometry_fingerprint(obj.data),'packed_maps':packed_maps(),
        'sections':[],'status':'Measured anatomy evidence; sections are not automatically named body parts.'}
    # Thin lower slabs distinguish actual soles/feet. Y slabs reveal body/tail
    # centerlines and wing planes without imposing humanoid proportions.
    for axis,fractions in [(2,[.015,.05,.10,.16,.24,.36,.5,.65,.8]),
                           (1,[.06,.14,.22,.30,.4,.5,.6,.7,.8,.9])]:
        for fraction in fractions:
            center=float(lo[axis]+fraction*(hi[axis]-lo[axis]))
            report['sections'].append(section_components(co,edges,axis,center,extent*.012))
    report['source_unchanged']=sha256(path)==entry['candidateSha256']
    write_json(folder/'measured_sections.json',report)
    print('BOSS_RIG_INSPECTED',entry['label'],len(co),flush=True)


def segment_distances(co,bones):
    result=np.empty((len(co),len(bones)),dtype=np.float32)
    for index,b in enumerate(bones):
        start=np.array(b['head']);delta=np.array(b['tail'])-start
        t=np.clip(((co-start)@delta)/float(delta@delta),0,1)
        result[:,index]=np.linalg.norm(co-start-t[:,None]*delta,axis=1)
        # Keep independent left/right appendages from attracting the opposite
        # limb through the abdomen. The 15 mm central overlap is provisional
        # skin blending around the attachment, not an anatomy/shape change.
        if b.get('side',0):
            cross=np.maximum(-co[:,0]*b['side']-.015,0)
            result[:,index]+=cross*8
    return result


def smoothstep(value):
    t=np.clip(value,0,1)
    return t*t*(3-2*t)


def apply_local_weight_rules(scores,co,bones,profile,edges):
    """Recorded, visually diagnosed local fixes. No rest vertex/bone edits."""
    notes=[]
    for rule in profile.get('weight_rules',[]):
        if rule['kind']=='component_branch':
            vertices=np.array(rule['vertex_indices'],dtype=np.int32)
            columns=[i for i,b in enumerate(bones) if b['branch']==rule['branch']]
            if not columns or vertices.max()>=len(co):raise RuntimeError('Local component rule does not match mesh/bones.')
            influence=smoothstep((rule['fade_top_z']-co[vertices,2])/(rule['fade_top_z']-rule['fully_isolated_below_z']))
            original=scores[vertices].copy();isolated=np.zeros_like(original)
            isolated[:,columns]=original[:,columns]
            isolated/=np.maximum(isolated.sum(axis=1,keepdims=True),1e-20)
            scores[vertices]=original*(1-influence[:,None])+isolated*influence[:,None]
            notes.append({'kind':rule['kind'],'branch':rule['branch'],'vertices':len(vertices)})
        elif rule['kind']=='rigid_foot_support':
            columns=[i for i,b in enumerate(bones) if b['branch']==rule['branch']]
            index=next(i for i,b in enumerate(bones) if b['name']==rule['bone'])
            owner=scores[:,columns].sum(axis=1)>.5
            blend=smoothstep((rule['zero_above_z']-co[:,2])/(rule['zero_above_z']-rule['full_below_z']))
            blend*=owner
            scores*=1-blend[:,None];scores[:,index]+=blend
            notes.append({'kind':rule['kind'],'bone':rule['bone'],'affected_vertices':int((blend>0).sum()),
                'fully_rigid_vertices':int((blend==1).sum()),'evidence':'Measured physical foot branch only; ankle band remains smooth, sole follows foot rather than lower-leg bend.'})
        elif rule['kind']=='local_weight_relaxation':
            distance=np.linalg.norm(co-np.array(rule['center']),axis=1)
            blend=smoothstep(1-distance/rule['radius'])*rule.get('strength',.75)
            active=np.flatnonzero(blend>0)
            degree=np.bincount(edges.ravel(),minlength=len(co))
            for _ in range(rule.get('iterations',24)):
                average=np.zeros_like(scores)
                for column in range(len(bones)):
                    total=np.bincount(edges[:,0],weights=scores[edges[:,1],column],minlength=len(co))
                    total+=np.bincount(edges[:,1],weights=scores[edges[:,0],column],minlength=len(co))
                    average[:,column]=total/np.maximum(degree,1)
                scores[active]=scores[active]*(1-blend[active,None])+average[active]*blend[active,None]
            scores/=scores.sum(axis=1,keepdims=True)
            notes.append({'kind':rule['kind'],'center':rule['center'],'radius':rule['radius'],
                'affected_vertices':len(active),'iterations':rule.get('iterations',24)})
        elif rule['kind']=='harmonic_branch_region':
            # Actual connected-component extremities act as hard anchors. A
            # graph harmonic transition avoids a planar crease and stops a
            # nearby separate limb being attracted across empty space.
            columns=[i for i,b in enumerate(bones) if b['branch']==rule['branch']]
            positive=np.array(rule['positive_vertex_indices'],dtype=np.int32)
            negative=np.array(rule['negative_vertex_indices'],dtype=np.int32)
            if not columns or len(np.intersect1d(positive,negative)):
                raise RuntimeError('Ambiguous harmonic branch anchors.')
            branch=scores[:,columns].sum(axis=1)
            mask=branch.copy();mask[positive]=1;mask[negative]=0
            degree=np.bincount(edges.ravel(),minlength=len(co))
            for iteration in range(rule.get('iterations',300)):
                previous=mask.copy()
                total=np.bincount(edges[:,0],weights=mask[edges[:,1]],minlength=len(co))
                total+=np.bincount(edges[:,1],weights=mask[edges[:,0]],minlength=len(co))
                mask=total/np.maximum(degree,1)
                mask[positive]=1;mask[negative]=0
                if float(np.abs(mask-previous).max())<1e-5:break
            inside=np.zeros_like(scores);inside[:,columns]=scores[:,columns]
            outside=scores.copy();outside[:,columns]=0
            # Re-normalize each side independently. Global Gaussian scores
            # can underflow for an excluded remote branch; restore only those
            # rows from a stable branch-local distance distribution.
            for values,selected in [(inside,columns),(outside,[i for i in range(len(bones)) if i not in columns])]:
                empty=values.sum(axis=1)<1e-20
                if empty.any():
                    distances=segment_distances(co[empty],[bones[i] for i in selected])
                    squared=distances.astype(np.float64)**2
                    fallback=np.exp(-(squared-squared.min(axis=1,keepdims=True))/(2*.05**2))
                    fallback/=fallback.sum(axis=1,keepdims=True)
                    values[np.ix_(np.flatnonzero(empty),selected)]=fallback
                values/=values.sum(axis=1,keepdims=True)
            scores[:]=inside*mask[:,None]+outside*(1-mask[:,None])
            scores/=np.maximum(scores.sum(axis=1,keepdims=True),1e-20)
            notes.append({'kind':rule['kind'],'branch':rule['branch'],
                'positive_pins':len(positive),'negative_pins':len(negative),
                'iterations':iteration+1,'maximum_mask_change':float(np.abs(mask-branch).max())})
        elif rule['kind']=='lateral_appendage':
            columns=[i for i,b in enumerate(bones) if b['branch']==rule['branch']]
            mask=smoothstep((np.abs(co[:,0])-rule['zero_below_abs_x'])/(rule['full_above_abs_x']-rule['zero_below_abs_x']))
            scores[:,columns]*=mask[:,None]
            scores/=np.maximum(scores.sum(axis=1,keepdims=True),1e-20)
            notes.append({'kind':rule['kind'],'branch':rule['branch'],'zeroed_center_vertices':int((mask==0).sum())})
        elif rule['kind']=='jaw_halfspace':
            index=next(i for i,b in enumerate(bones) if b['name']==rule['bone'])
            gap_z=rule['gap_reference_z']+rule['gap_slope']*(co[:,1]-rule['gap_reference_y'])
            mouth=smoothstep((rule['hinge_y']-co[:,1])/rule['hinge_blend'])
            lower=smoothstep((gap_z-co[:,2]+rule['gap_blend']*.25)/rule['gap_blend'])
            side=smoothstep((rule['max_abs_x']-np.abs(co[:,0]))/.025)
            mask=mouth*lower*side
            if rule.get('harmonic_transition',False):
                # Preserve observed upper/lower tooth surfaces as fixed skin
                # anchors, and solve the cheek transition over actual mesh
                # edges. A geometric plane alone caused a narrow crease in V3.
                lower_pins=(co[:,1]<-.735)&(co[:,2]<gap_z-.018)&(np.abs(co[:,0])<.09)
                upper_pins=(co[:,1]>-.64)|(co[:,2]>gap_z+.020)|(np.abs(co[:,0])>.115)
                upper_pins&=~lower_pins
                degree=np.bincount(edges.ravel(),minlength=len(co))
                for iteration in range(300):
                    previous=mask.copy()
                    total=np.bincount(edges[:,0],weights=mask[edges[:,1]],minlength=len(co))
                    total+=np.bincount(edges[:,1],weights=mask[edges[:,0]],minlength=len(co))
                    mask=total/np.maximum(degree,1)
                    mask[lower_pins]=1;mask[upper_pins]=0
                    if float(np.abs(mask-previous).max())<1e-5:break
                notes.append({'kind':'harmonic_jaw_transition','iterations':iteration+1,
                    'lower_jaw_pins':int(lower_pins.sum()),'protected_head_pins':int(upper_pins.sum())})
            # Remove all upper-jaw leakage, then rigidly support the actual
            # lower jaw away from its soft hinge band. This protects teeth.
            scores[:,index]=0
            scores/=np.maximum(scores.sum(axis=1,keepdims=True),1e-20)
            scores*=1-mask[:,None];scores[:,index]=mask
            notes.append({'kind':rule['kind'],'bone':rule['bone'],'lower_jaw_vertices_above_50_percent':int((mask>.5).sum())})
        else:raise RuntimeError('Unknown local weight rule: '+rule['kind'])
    return notes


def assign_review_weights(obj,profile):
    """Bounded deterministic seed weights, explicitly requiring deformation QA.

    Continuous segment falloff and mesh-neighbor relaxation avoid discontinuous
    parent/child support switching at a nearest-bone Voronoi border. This remains
    a diagnostic starting point, not final anatomical weight-paint approval.
    """
    bones=[b for b in profile['bones'] if b.get('deform',True)]
    co=array(obj.data.vertices,'co',(len(obj.data.vertices),3))
    distances=segment_distances(co,bones)
    # A 50 mm Gaussian is a provisional skinning support width for these
    # ~2-unit source models, not a game scale or a hidden anatomy assumption.
    squared=distances*distances
    scores=np.exp(-(squared-squared.min(axis=1,keepdims=True))/(2*.05*.05))
    scores/=scores.sum(axis=1,keepdims=True)
    edges=array(obj.data.edges,'vertices',(len(obj.data.edges),2),np.int32)
    degree=np.bincount(edges.ravel(),minlength=len(co)).astype(np.float32)
    for _ in range(6):
        average=np.zeros_like(scores)
        for bone_index in range(len(bones)):
            values=np.bincount(edges[:,0],weights=scores[edges[:,1],bone_index],minlength=len(co))
            values+=np.bincount(edges[:,1],weights=scores[edges[:,0],bone_index],minlength=len(co))
            average[:,bone_index]=values/np.maximum(degree,1)
        scores=scores*.55+average*.45
    local_notes=apply_local_weight_rules(scores,co,bones,profile,edges)
    indices=np.argsort(-scores,axis=1)[:,:4]
    weights=np.take_along_axis(scores,indices,axis=1)
    weights/=weights.sum(axis=1,keepdims=True)
    weights[weights<1e-5]=0
    weights/=weights.sum(axis=1,keepdims=True)
    for b in bones:obj.vertex_groups.new(name=b['name'])
    for vertex in range(len(co)):
        for index,weight in zip(indices[vertex],weights[vertex]):
            if weight>0:obj.vertex_groups[bones[index]['name']].add([vertex],float(weight),'REPLACE')
    return bones,indices,weights,{
        'method':'Continuous Gaussian distance to bone segments, six conservative mesh-neighbor relaxation iterations, strongest four normalized influences; provisional deterministic seed weights.',
        'vertices':len(co),'unweighted_vertices':int((weights.sum(axis=1)==0).sum()),
        'maximum_influences':int((weights>0).sum(axis=1).max()),
        'max_weight_sum_error':float(np.abs(weights.sum(axis=1)-1).max()),
        'nonfinite_weights':int((~np.isfinite(weights)).sum()),
        'local_weight_rules':local_notes,
        'influenced_vertices_per_bone':{b['name']:int(((indices==i)&(weights>0)).any(axis=1).sum()) for i,b in enumerate(bones)},
        'warning':'Requires actual posed material renders and weight-paint review. Numeric checks alone cannot approve a game rig.'}


def evaluated_positions(obj):
    depsgraph=bpy.context.evaluated_depsgraph_get();depsgraph.update()
    evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh()
    co=array(mesh.vertices,'co',(len(mesh.vertices),3))
    evaluated.to_mesh_clear()
    return co


def pose_diagnostics(obj,rig,profile,folder):
    rest=array(obj.data.vertices,'co',(len(obj.data.vertices),3))
    edges=array(obj.data.edges,'vertices',(len(obj.data.edges),2),np.int32)
    lengths=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)
    # The review also reports all edges; a separate >0.1 mm set is useful for
    # interpreting 10 micrometer local repair shells without hiding their count.
    significant=lengths>.0001
    result=[]
    frame=1
    for probe in profile['pose_probes']:
        for bone in rig.pose.bones:
            bone.rotation_mode='XYZ';bone.rotation_euler=(0,0,0)
        for rotation in probe['rotations']:
            rig.pose.bones[rotation['bone']].rotation_euler['XYZ'.index(rotation['axis_local'])]=math.radians(rotation['degrees'])
        rig.update_tag();bpy.context.view_layer.update()
        co=evaluated_positions(obj)
        ratios=np.linalg.norm(co[edges[:,0]]-co[edges[:,1]],axis=1)/np.maximum(lengths,1e-12)
        valid=ratios[significant]
        worst=np.argsort(ratios)[-20:][::-1]
        record={'name':probe['name'],'rotations':probe['rotations'],
            'nonfinite_coordinates':int((~np.isfinite(co)).sum()),
            'maximum_vertex_displacement':float(np.linalg.norm(co-rest,axis=1).max()),
            'all_edge_ratio_max':float(ratios.max()),'all_edge_ratio_min':float(ratios.min()),
            'significant_edges':int(significant.sum()),'significant_edge_ratio_p99':float(np.percentile(valid,99)),
            'significant_edge_ratio_max':float(valid.max()),'significant_edges_above_2x':int((valid>2).sum()),
            'worst_edge_samples':[{'vertices':edges[i].tolist(),'rest_length':float(lengths[i]),'ratio':float(ratios[i]),
                'rest_midpoint':rest[edges[i]].mean(axis=0).tolist()} for i in worst]}
        result.append(record)
        np.savez_compressed(folder/(probe['name']+'_positions.npz'),positions=co)
        frame+=12
    for bone in rig.pose.bones:bone.rotation_euler=(0,0,0)
    rig.update_tag();bpy.context.view_layer.update()
    return result


def build(entry,output,landmarks):
    path,obj=open_candidate(entry)
    profile=json.loads(landmarks.read_text(encoding='utf-8-sig'))
    if profile['label']!=entry['label'] or profile['candidate_sha256']!=entry['candidateSha256']:
        raise RuntimeError('Landmarks do not match this frozen candidate.')
    bones=profile['bones'];names=[b['name'] for b in bones]
    if len(set(names))!=len(names):raise RuntimeError('Duplicate bone names.')
    for bone in bones:
        if bone['parent'] and bone['parent'] not in names:raise RuntimeError('Missing bone parent.')
        if not np.isfinite(np.array([bone['head'],bone['tail']])).all():raise RuntimeError('Nonfinite bone.')
        if np.linalg.norm(np.array(bone['tail'])-bone['head'])<.001:raise RuntimeError('Degenerate bone length.')
    folder=output/entry['label'];folder.mkdir(parents=True,exist_ok=True)
    destination=folder/(entry['label']+'_rig_review.blend')
    if destination.exists():raise RuntimeError('Never overwrite a frozen rig review; use a new output directory.')
    before=geometry_fingerprint(obj.data);maps_before=packed_maps()
    rig=bpy.data.objects.new(entry['label']+'_GenericRig',bpy.data.armatures.new(entry['label']+'_Skeleton'))
    bpy.context.scene.collection.objects.link(rig);rig.matrix_world=obj.matrix_world.copy();rig.show_in_front=True
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    for b in bones:
        eb=rig.data.edit_bones.new(b['name']);eb.head=b['head'];eb.tail=b['tail'];eb.use_deform=b.get('deform',True)
    for b in bones:
        if b['parent']:rig.data.edit_bones[b['name']].parent=rig.data.edit_bones[b['parent']]
    bpy.ops.object.mode_set(mode='OBJECT')
    deform_bones,indices,weights,weight_report=assign_review_weights(obj,profile)
    modifier=obj.modifiers.new('Generic boss review deformation','ARMATURE');modifier.object=rig
    modifier.use_deform_preserve_volume=False
    world=obj.matrix_world.copy();obj.parent=rig;obj.matrix_parent_inverse=rig.matrix_world.inverted();obj.matrix_world=world
    bpy.context.view_layer.update()
    rest=array(obj.data.vertices,'co',(len(obj.data.vertices),3))
    rest_error=float(np.linalg.norm(evaluated_positions(obj)-rest,axis=1).max())
    report={'label':entry['label'],'input':str(path),'input_sha256':entry['candidateSha256'],
        'landmarks':str(landmarks),'bone_count':len(bones),'weights':weight_report,
        'maximum_rest_pose_vertex_error':rest_error,'pose_probes':pose_diagnostics(obj,rig,profile,folder)}
    report['geometry_uv_material_fingerprints_before']=before
    report['geometry_uv_material_fingerprints_after']=geometry_fingerprint(obj.data)
    report['geometry_uv_exactly_preserved']=before==report['geometry_uv_material_fingerprints_after']
    report['packed_maps_preserved']=maps_before==packed_maps()
    if (rest_error>1e-5 or not report['geometry_uv_exactly_preserved'] or not report['packed_maps_preserved']
            or weight_report['nonfinite_weights'] or weight_report['unweighted_vertices']
            or not math.isfinite(weight_report['max_weight_sum_error'])
            or weight_report['max_weight_sum_error']>1e-5):
        write_json(folder/'rig_integrity_failure.json',report)
        raise RuntimeError('Rig integrity/rest-position gate failed; no saved rig candidate.')
    report['foot_metadata']=[]
    for foot in profile['feet']:
        selected=[i for i,b in enumerate(deform_bones) if b['branch']==foot['branch']]
        membership=np.sum(np.where(np.isin(indices,selected),weights,0),axis=1)
        vertices=np.flatnonzero(membership>.5)
        note=dict(foot);note['majority_weight_vertices']=len(vertices)
        if len(vertices):
            sole=int(vertices[np.argmin(rest[vertices,2])]);note['measured_sole_vertex_index']=sole
            note['measured_sole_rest_local']=rest[sole].tolist()
            note['ankle_to_measured_sole_local']=(rest[sole]-np.array(foot['ankle_rest'])).tolist()
        report['foot_metadata'].append(note)
    report['numeric_pose_warnings']=[p['name'] for p in report['pose_probes'] if p['nonfinite_coordinates'] or p['significant_edges_above_2x']]
    report['status']='Review rig only. Pose warnings require correction; even a clean numeric pass requires actual visual deformation review.'
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(destination),compress=True)
    report['output']=str(destination);report['output_sha256']=sha256(destination)
    report['input_unchanged']=sha256(path)==entry['candidateSha256']
    np.savez_compressed(folder/'skin_weights.npz',bone_indices=indices,weights=weights,bone_names=[b['name'] for b in deform_bones])
    write_json(folder/'rig_review.json',report)
    print('BOSS_REVIEW_RIG_BUILT',entry['label'],len(bones),'bones','warnings',report['numeric_pose_warnings'],flush=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--manifest',required=True)
    parser.add_argument('--output',required=True)
    parser.add_argument('--only')
    parser.add_argument('--mode',choices=['inspect','build'],default='inspect')
    parser.add_argument('--landmarks')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    output=Path(args.output).resolve()
    if 'Assets' in output.parts:raise RuntimeError('Rig review output must remain outside Assets.')
    output.mkdir(parents=True,exist_ok=True)
    manifest=json.loads(Path(args.manifest).read_text(encoding='utf-8-sig'))
    entries=[e for e in manifest['candidates'] if not args.only or e['label']==args.only]
    if not entries:raise RuntimeError('No matching frozen candidate.')
    for entry in entries:
        if not entry['finalNumericGatesPassed']:raise RuntimeError('Input mesh numeric gates failed.')
        if args.mode=='inspect':inspect(entry,output)
        else:
            if not args.landmarks:raise RuntimeError('Build requires explicit calibrated landmark JSON or profile directory.')
            landmark=Path(args.landmarks).resolve()
            if landmark.is_dir():landmark=landmark/entry['label']/'landmarks.json'
            build(entry,output,landmark)


if __name__=='__main__':main()
