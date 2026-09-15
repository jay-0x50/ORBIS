"""Conservative, CPU-only repairs on an explicitly selected DERIVED candidate.

Never overwrite the input. Stella boot separation is limited to measured lower
centerline contacts. Micro patches require a closed rim, a single material, UV
continuity and a <=6 mm patch at the audited ~1.9-unit source height. Anything
outside these gates is reported, not automatically filled or redesigned.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.geometry import closest_point_on_tri

sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import topology, array, sha256, write_json
from inspect_topology_defects import lower_leg_components


def ordered_ring(edges):
    adjacency={}
    for edge in edges:
        for vertex in edge.verts:
            adjacency.setdefault(vertex,[]).append(edge)
    if not edges or any(len(incident)!=2 for incident in adjacency.values()):
        return None
    first=next(iter(adjacency))
    current=first
    previous=None
    ordered=[]
    while True:
        ordered.append(current)
        candidates=[e for e in adjacency[current] if e is not previous]
        edge=candidates[0]
        current=edge.other_vert(current)
        previous=edge
        if current is first:
            return ordered if len(ordered)==len(adjacency) else None
        if current in ordered:
            return None


def groups_of_edges(edges):
    remaining=set(edges)
    groups=[]
    while remaining:
        stack=[remaining.pop()]
        group=set()
        while stack:
            edge=stack.pop()
            if edge in group: continue
            group.add(edge)
            remaining.discard(edge)
            for vertex in edge.verts:
                stack.extend(other for other in vertex.link_edges if other in remaining)
        groups.append(group)
    return groups


def uv_for_vertices(faces,vertices,uv_layers,tolerance=1e-5):
    """Reject ambiguous seams; do not silently choose a different chart."""
    result={}
    for vertex in vertices:
        result[vertex]={}
        for uv_layer in uv_layers:
            values=[loop[uv_layer].uv.copy() for face in faces for loop in face.loops if loop.vert is vertex]
            if not values:
                return None
            if any((value-values[0]).length > tolerance for value in values[1:]):
                return None
            result[vertex][uv_layer]=values[0]
    return result


def face_signature(face,uv_layers):
    # Preserve loop order as well as positions and UVs. BMesh insertion does not
    # reorder untouched face loops during these operations.
    return (face.material_index,tuple((tuple(loop.vert.co),tuple(tuple(loop[layer].uv) for layer in uv_layers)) for loop in face.loops))


def manifold_face_subset(faces,edges,max_nodes=20000):
    """Retain a maximal-area subset with exact 0/2 users at every local edge.

    Exterior face users are fixed. Boundary constraints propagate most variables;
    bounded search resolves a tiny folded sheet without changing surviving UVs.
    """
    face_list=list(faces)
    indices={face:index for index,face in enumerate(face_list)}
    areas=np.array([face.calc_area() for face in face_list])
    constraints=[]
    for edge in edges:
        variables=[indices[face] for face in edge.link_faces if face in indices]
        outside=sum(face not in indices for face in edge.link_faces)
        targets=[total-outside for total in (0,2) if 0<=total-outside<=len(variables)]
        if not targets:
            return None,{"reason":"No manifold subset compatible with fixed exterior faces."}
        constraints.append((variables,targets))
    best=None
    best_cost=float("inf")
    nodes=0
    def search(values):
        nonlocal best,best_cost,nodes
        nodes+=1
        if nodes>max_nodes:
            return
        while True:
            changed=False
            for variables,targets in constraints:
                ones=sum(values[index]==1 for index in variables)
                unknown=[index for index in variables if values[index]<0]
                options=[target for target in targets if ones<=target<=ones+len(unknown)]
                if not options: return
                if len(options)==1 and unknown:
                    target=options[0]-ones
                    if target in (0,len(unknown)):
                        for index in unknown: values[index]=int(target!=0)
                        changed=True
            if not changed: break
        removed_cost=float(areas[values==0].sum())
        if removed_cost>=best_cost: return
        undecided=np.flatnonzero(values<0)
        if not len(undecided):
            if np.any(values==1):
                best=values.copy()
                best_cost=removed_cost
            return
        # Constrained variables first, retain higher-area original faces first.
        variable=max(undecided,key=lambda index:sum(index in c[0] for c in constraints))
        for value in (1,0):
            trial=values.copy()
            trial[variable]=value
            search(trial)
    search(np.full(len(face_list),-1,dtype=np.int8))
    if best is None:
        return None,{"reason":"No bounded-search manifold subset found.","search_nodes":nodes}
    removed=[face_list[index] for index in np.flatnonzero(best==0)]
    kept=[face_list[index] for index in np.flatnonzero(best==1)]
    if not removed:
        return None,{"reason":"Subset does not remove the reported defect.","search_nodes":nodes}
    triangles=[tuple(vertex.co.copy() for vertex in face.verts) for face in kept if len(face.verts)==3]
    if not triangles:
        return None,{"reason":"No retained triangular reference surface."}
    samples=[vertex.co for face in removed for vertex in face.verts]
    samples.extend(sum((v.co for v in face.verts),Vector())/len(face.verts) for face in removed)
    max_distance=max(min((point-closest_point_on_tri(point,*tri)).length for tri in triangles) for point in samples)
    return removed,{"search_nodes":nodes,"removed_faces":len(removed),"retained_faces":len(kept),
                    "removed_area":best_cost,"maximum_removed_surface_distance":max_distance,
                    "preserve_as_thin_shell":max_distance>.00075}


def split_four_face_contact(bm,edge,id_layer):
    """Separate two coherently oriented surface fans at an isolated 4-user edge."""
    faces=list(edge.link_faces)
    if len(faces)!=4: return None
    start,end=(vertex.co.copy() for vertex in edge.verts)
    directions=[]
    for face in faces:
        loop=next(loop for loop in face.loops if loop.edge is edge)
        directions.append(1 if (loop.vert.co-start).length<1e-8 else -1)
    candidates=[]
    for pairs in (((0,1),(2,3)),((0,2),(1,3)),((0,3),(1,2))):
        if all(directions[a]!=directions[b] for a,b in pairs):
            score=sum(faces[a].normal.dot(faces[b].normal) for a,b in pairs)
            candidates.append((score,pairs))
    score,pairs=max(candidates,key=lambda item:item[0]) if candidates else (0,((0,1),(2,3)))
    bmesh.ops.split_edges(bm,edges=[edge])
    contacts=[]
    for face in faces:
        match=[e for e in face.edges if
               all(min((v.co-start).length,(v.co-end).length)<1e-8 for v in e.verts)]
        if len(match)!=1:
            raise RuntimeError("Four-face fan split did not expose expected independent contact edges.")
        contacts.append(match[0])
    if len(set(contacts))==2 and all(len(edge.link_faces)==2 for edge in set(contacts)):
        return {"pairs":[[face[id_layer] for face in edge.link_faces] for edge in set(contacts)],
                "normal_agreement_sum":score,"position_policy":"Blender separated two existing local surface fans; all positions/UVs retained."}
    if len(set(contacts))!=4 or any(len(edge.link_faces)!=1 for edge in contacts):
        raise RuntimeError("Unexpected radial users after four-face contact split: "+str([len(edge.link_faces) for edge in contacts]))
    # Existing exterior faces can connect some split endpoints. Build canonical
    # equivalence classes and test the resulting edge users instead of emitting
    # an order-dependent weld chain across two surface fans.
    target_map=None
    ranked=sorted(candidates,key=lambda item:item[0],reverse=True)
    if not ranked: ranked=[(0,((0,1),(2,3))),(0,((0,2),(1,3))),(0,((0,3),(1,2)))]
    for trial_score,trial_pairs in ranked:
        vertices=set(v for contact in contacts for v in contact.verts)
        parents={vertex:vertex for vertex in vertices}
        def find(vertex):
            while parents[vertex] is not vertex:
                vertex=parents[vertex]
            return vertex
        for a,b in trial_pairs:
            for vertex in contacts[b].verts:
                target=min(contacts[a].verts,key=lambda other:(other.co-vertex.co).length)
                left,right=find(vertex),find(target)
                if left is not right: parents[left]=right
        keys={}
        for contact in contacts:
            key=frozenset(find(v) for v in contact.verts)
            keys[key]=keys.get(key,0)+1
        if len(keys)==2 and all(count==2 for count in keys.values()):
            target_map={vertex:find(vertex) for vertex in vertices if find(vertex) is not vertex}
            pairs,score=trial_pairs,trial_score
            break
    if target_map is None:
        raise RuntimeError("No two-fan weld partition preserves manifold edge users.")
    bmesh.ops.weld_verts(bm,targetmap=target_map)
    return {"pairs":[[faces[a][id_layer],faces[b][id_layer]] for a,b in pairs],
            "normal_agreement_sum":score,"position_policy":"Coincident vertex duplication only; all face positions and UVs retained."}


def preserve_sheet_as_shells(bm,faces,uv_layers,id_layer,thickness=.00001,opposite_uvs=None):
    """Keep a protruding microscopic fin's front surface; close behind it by 10 um."""
    records=[]
    touched_edges=set(edge for face in faces for edge in face.edges)
    touched_verts=set(vertex for face in faces for vertex in face.verts)
    for face in faces:
        records.append({"points":[loop.vert.co.copy() for loop in face.loops],
            "uvs":[{layer:loop[layer].uv.copy() for layer in uv_layers} for loop in face.loops],
            "normal":face.normal.copy(),"material":face.material_index,"id":face[id_layer],
            "opposite_uvs":opposite_uvs.get(face) if opposite_uvs else None})
    bmesh.ops.delete(bm,geom=list(faces),context="FACES_ONLY")
    wire=[edge for edge in touched_edges if edge.is_valid and not edge.link_faces]
    if wire: bmesh.ops.delete(bm,geom=wire,context="EDGES")
    loose=[v for v in touched_verts if v.is_valid and not v.link_edges]
    if loose: bmesh.ops.delete(bm,geom=loose,context="VERTS")
    for data in records:
        front=[bm.verts.new(point) for point in data["points"]]
        back=[bm.verts.new(point-data["normal"]*thickness) for point in data["points"]]
        uv_lookup={vertex:data["uvs"][index] for index,vertex in enumerate(front)}
        uv_lookup.update({vertex:(data["opposite_uvs"][tuple(data["points"][index])] if data["opposite_uvs"] else data["uvs"][index]) for index,vertex in enumerate(back)})
        original=bm.faces.new(front)
        original[id_layer]=data["id"]
        new_faces=[original,bm.faces.new(list(reversed(back)))]
        for index in range(len(front)):
            other=(index+1)%len(front)
            new_faces.append(bm.faces.new((front[other],front[index],back[index],back[other])))
        for added in new_faces:
            added.material_index=data["material"]
            if added is not original: added[id_layer]=0
            for loop in added.loops:
                for layer in uv_layers:
                    loop[layer].uv=uv_lookup[loop.vert][layer]
        bmesh.ops.triangulate(bm,faces=new_faces[1:],quad_method="BEAUTY",ngon_method="BEAUTY")
    return {"preserved_original_faces":len(records),"back_surface_thickness":thickness,
            "front_surface_policy":"Original front positions, material and UVs are exactly retained."}


def boot_split(bm,uv_layers,id_layer):
    before_faces=set(bm.faces)
    crossing=[edge for edge in bm.edges if edge.verts[0].co.x*edge.verts[1].co.x < 0
              and all(v.co.z < -.60 and v.co.y < -.05 for v in edge.verts)]
    if not crossing:
        return {"status":"No measured lower-foot centerline contacts remain; no split applied.","crossing_edges":0},set()
    mids=[(edge.verts[0].co+edge.verts[1].co)*.5 for edge in crossing]
    # Audited contact lies near Z=-.75,Y=-.13. A generous few-centimeter window
    # allows reduction movement but rejects unrelated cloth/body centerlines.
    if any(not (-.80 < point.z < -.68 and -.17 < point.y < -.09) for point in mids):
        raise RuntimeError("Lower-foot crossing moved outside audited contact window; inspect candidate rather than guessing a cut.")
    faces=set(face for edge in crossing for face in edge.link_faces)
    changed_ids={face[id_layer] for face in faces}
    geom=set(faces)
    geom.update(edge for face in faces for edge in face.edges)
    geom.update(vertex for face in faces for vertex in face.verts)
    result=bmesh.ops.bisect_plane(bm,geom=list(geom),dist=1e-7,plane_co=(0,0,0),plane_no=(1,0,0),
                                use_snap_center=True,clear_inner=False,clear_outer=False)
    cut_edges=[item for item in result["geom_cut"] if isinstance(item,bmesh.types.BMEdge)]
    if not cut_edges:
        raise RuntimeError("Bisect produced no cut edges.")
    cut_vertices=set(v for edge in cut_edges for v in edge.verts)
    # Collect UV values from each future side before topology splitting, then
    # use the UV of that side's own boundary loop when closing its cap.
    bmesh.ops.split_edges(bm,edges=cut_edges)
    created_boundary=[edge for edge in bm.edges if len(edge.link_faces)==1 and all(abs(v.co.x) < 1e-6
                      and -.81 < v.co.z < -.67 and -.18 < v.co.y < -.08 for v in edge.verts)]
    rings=groups_of_edges(created_boundary)
    if len(rings)!=2:
        raise RuntimeError("Expected two local boot cap rings, found "+str(len(rings)))
    caps=[]
    for ring_edges in rings:
        vertices=ordered_ring(ring_edges)
        if vertices is None:
            raise RuntimeError("Boot contact boundary is not a closed unbranched ring.")
        neighbors=set(face for edge in ring_edges for face in edge.link_faces)
        # Caps close only the tiny newly cut contact. A vertex can lie on a UV
        # seam; use its side-local neighboring face, retaining all old UV loops.
        cap_uv={vertex:{layer:next(loop[layer].uv.copy() for face in neighbors for loop in face.loops if loop.vert is vertex)
                        for layer in uv_layers} for vertex in vertices}
        material=max((face.material_index for face in neighbors),key=lambda n:sum(face.material_index==n for face in neighbors))
        cap=bm.faces.new(vertices)
        cap.material_index=material
        cap[id_layer]=0
        cap.normal_update()
        side=np.mean([float(v.co.x) for face in neighbors for v in face.verts if abs(v.co.x)>1e-6])
        # Right surface cap faces left; left surface cap faces right.
        if cap.normal.x*side > 0:
            cap.normal_flip()
        for loop in cap.loops:
            for layer in uv_layers:
                loop[layer].uv=cap_uv[loop.vert][layer]
        caps.append(cap)
    bmesh.ops.triangulate(bm,faces=caps,quad_method="BEAUTY",ngon_method="BEAUTY")
    # Bisect may copy an original face ID onto more than one child; mark all
    # descendants of the intentionally edited local faces as changed.
    for face in bm.faces:
        if face[id_layer] in changed_ids:
            face[id_layer]=0
    return {"status":"Split measured fused boot contact into independent capped surfaces.",
            "crossing_edges":len(crossing),"bisect_edges":len(cut_edges),"cap_rings":len(rings),
            "uv_policy":"Original visible UVs preserved; each new cap vertex samples its own side boundary UV.",
            "faces_before":len(before_faces),"faces_after":len(bm.faces)},changed_ids


def repair_micro_patches(bm,uv_layers,id_layer,max_diameter=.006):
    bad=[edge for edge in bm.edges if len(edge.link_faces)!=2]
    groups=groups_of_edges(bad)
    report=[]
    changed=set()
    for group in groups:
        if any(not edge.is_valid for edge in group):
            continue
        faces=set(face for edge in group for face in edge.link_faces)
        note={"defect_edges":len(group),"original_patch_faces":len(faces)}
        def skip(reason):
            note["status"]="held"
            note["reason"]=reason
            report.append(note)
        if not faces or len(faces)>96:
            skip("Not a small face patch.")
            continue
        vertices=set(v for face in faces for v in face.verts)
        points=np.array([v.co[:] for v in vertices])
        diagonal=float(np.linalg.norm(points.max(axis=0)-points.min(axis=0)))
        note["bounds_min"]=points.min(axis=0).tolist()
        note["bounds_max"]=points.max(axis=0).tolist()
        note["diameter_bound"]=diagonal
        four_face_contacts=[edge for edge in group if len(edge.link_faces)==4]
        # Pure fan separation changes no positions/UVs and does not flatten any
        # patch, so the 6 mm reconstruction gate does not apply to this case.
        all_edges=set(edge for face in faces for edge in face.edges)
        if four_face_contacts:
            splits=[]
            for contact in four_face_contacts:
                if not contact.is_valid or len(contact.link_faces)!=4: continue
                split=split_four_face_contact(bm,contact,id_layer)
                if split is not None: splits.append(split)
            if splits:
                note["status"]="repaired"
                note["method"]="Separated two surface fans joined at a four-face edge; no visible geometry or UV changes."
                note["fan_splits"]=splits
                report.append(note)
                continue
        remove,subset_report=manifold_face_subset(faces,all_edges)
        note["subset_solver"]=subset_report
        if remove is not None:
            removed_points=np.array([v.co[:] for face in remove for v in face.verts])
            removed_diagonal=float(np.linalg.norm(removed_points.max(axis=0)-removed_points.min(axis=0)))
            note["removed_sheet_diameter_bound"]=removed_diagonal
            # Reduction can make adjacent retained triangles larger than 6 mm.
            # Only the actual sheet being removed is subject to that gate; its
            # unaffected neighbors retain their complete geometry and UVs.
            if removed_diagonal>max_diameter:
                skip("Actual redundant/protruding sheet exceeds micro-detail gate; preserve for review.")
                continue
            if subset_report["preserve_as_thin_shell"]:
                note["shell_preservation"]=preserve_sheet_as_shells(bm,remove,uv_layers,id_layer)
                note["status"]="repaired"
                note["method"]="Preserved a protruding microscopic fin as an independent closed thin shell; retained its visible front UV and geometry."
                report.append(note)
                continue
            changed.update(face[id_layer] for face in remove if face[id_layer]>0)
            touched_vertices=set(v for face in remove for v in face.verts)
            touched_edges=set(e for face in remove for e in face.edges)
            bmesh.ops.delete(bm,geom=remove,context="FACES_ONLY")
            wire=[edge for edge in touched_edges if edge.is_valid and not edge.link_faces]
            if wire: bmesh.ops.delete(bm,geom=wire,context="EDGES")
            loose=[v for v in touched_vertices if v.is_valid and not v.link_edges]
            if loose: bmesh.ops.delete(bm,geom=loose,context="VERTS")
            note["status"]="repaired"
            note["method"]="Removed only a microscopic redundant sheet using exact manifold edge constraints; every retained face and UV is unchanged."
            report.append(note)
            continue
        if diagonal>max_diameter:
            skip("Reconstruction patch exceeds measured micro-defect size gate.")
            continue
        # A raw defective open edge with no face outside this patch is internal
        # repair geometry, not the preserved outer rim. The rim is precisely the
        # interface between one removed face and one untouched exterior face.
        boundary=[edge for edge in all_edges if sum(face in faces for face in edge.link_faces)==1
                  and sum(face not in faces for face in edge.link_faces)==1]
        rim=ordered_ring(boundary)
        if rim is None or any(len(edge.link_faces)!=2 for edge in boundary):
            skip("Patch has no single closed manifold rim; broader topology review needed.")
            continue
        materials={face.material_index for face in faces}
        if len(materials)!=1:
            skip("Patch crosses material boundary.")
            continue
        rim_faces=set(face for edge in boundary for face in edge.link_faces if face in faces)
        uv=uv_for_vertices(rim_faces,rim,uv_layers)
        if uv is None:
            skip("Patch crosses an ambiguous UV seam; preserve chart boundaries.")
            continue
        normals=[face.normal.copy() for edge in boundary for face in edge.link_faces if face not in faces]
        mean=sum(normals,Vector()).normalized()
        if not normals or mean.length < .9 or any(normal.dot(mean)<.75 for normal in normals):
            skip("Patch rim surrounds a sharp form; avoid flattening silhouette/detail.")
            continue
        origin=sum((v.co for v in rim),Vector())/len(rim)
        max_plane=max(abs((v.co-origin).dot(mean)) for v in vertices)
        note["maximum_plane_offset"]=max_plane
        if max_plane>.0015:
            skip("Patch cannot be reconstructed within the 1.5 mm local deviation gate.")
            continue
        changed.update(face[id_layer] for face in faces if face[id_layer]>0)
        # Store all data before invalidating old face handles. Only face-local
        # interior wire geometry can be removed; rim positions remain identical.
        material=next(iter(materials))
        interior_edges=[edge for edge in all_edges if edge not in boundary]
        interior_vertices=[v for v in vertices if v not in rim]
        bmesh.ops.delete(bm,geom=list(faces),context="FACES_ONLY")
        loose_edges=[edge for edge in interior_edges if edge.is_valid and not edge.link_faces]
        if loose_edges:
            bmesh.ops.delete(bm,geom=loose_edges,context="EDGES")
        loose_vertices=[v for v in interior_vertices if v.is_valid and not v.link_edges]
        if loose_vertices:
            bmesh.ops.delete(bm,geom=loose_vertices,context="VERTS")
        patch=bm.faces.new(rim)
        patch.material_index=material
        patch[id_layer]=0
        patch.normal_update()
        if patch.normal.dot(mean)<0:
            patch.normal_flip()
        for loop in patch.loops:
            for layer in uv_layers:
                loop[layer].uv=uv[loop.vert][layer]
        bmesh.ops.triangulate(bm,faces=[patch],quad_method="BEAUTY",ngon_method="BEAUTY")
        note["status"]="repaired"
        note["method"]="Reconstructed only a tiny nearly planar patch from its unchanged single UV-continuous rim."
        report.append(note)
    return report,changed


def remove_subvoxel_noise(bm,id_layer,voxel):
    """Remove only tiny isolated collapsed remesh residues, not hair/ornaments."""
    if not 0 < voxel <= .002:
        raise RuntimeError("Unexpected remesh voxel size; audit the candidate's scale.")
    remaining=set(bm.verts)
    components=[]
    while remaining:
        stack=[remaining.pop()]
        vertices=set()
        while stack:
            vertex=stack.pop()
            if vertex in vertices: continue
            vertices.add(vertex)
            remaining.discard(vertex)
            stack.extend(edge.other_vert(vertex) for edge in vertex.link_edges if edge.other_vert(vertex) in remaining)
        components.append(vertices)
    changed=set()
    notes=[]
    for vertices in components:
        if len(vertices)>4: continue
        points=np.array([v.co[:] for v in vertices])
        diagonal=float(np.linalg.norm(points.max(axis=0)-points.min(axis=0)))
        faces=set(face for vertex in vertices for face in vertex.link_faces)
        area=sum(face.calc_area() for face in faces)
        note={"vertices":len(vertices),"faces":len(faces),"diameter_bound":diagonal,"surface_area":area,
              "bounds_min":points.min(axis=0).tolist(),"bounds_max":points.max(axis=0).tolist()}
        # A disconnected residue smaller than one voxel and under 5% of a
        # voxel-face area cannot represent a resolved feature of this remesh.
        if diagonal<=voxel and area<=voxel*voxel*.05:
            changed.update(face[id_layer] for face in faces if face[id_layer]>0)
            bmesh.ops.delete(bm,geom=list(vertices),context="VERTS")
            note["status"]="removed_subvoxel_collapsed_residue"
        else:
            note["status"]="held_as_potential_resolved_detail"
        notes.append(note)
    return {"voxel_source_units":voxel,"measured_small_components":notes,
            "removed_components":sum(n["status"]=="removed_subvoxel_collapsed_residue" for n in notes)},changed


def split_pinched_vertex_fans(bm,uv_layers,id_layer):
    """Detach disconnected face fans at a point without changing any face/UV."""
    bm.verts.index_update()
    source_layer=bm.verts.layers.int.new("_RepairVertexOrigin")
    for vertex in bm.verts: vertex[source_layer]=vertex.index+1
    records=[]
    for vertex in list(bm.verts):
        if vertex.is_manifold or any(len(edge.link_faces)!=2 for edge in vertex.link_edges):
            continue
        remaining=set(vertex.link_faces)
        fans=[]
        while remaining:
            stack=[remaining.pop()]
            fan=set()
            while stack:
                face=stack.pop()
                if face in fan: continue
                fan.add(face)
                remaining.discard(face)
                for edge in face.edges:
                    if vertex in edge.verts:
                        stack.extend(other for other in edge.link_faces if other in remaining)
            fans.append(fan)
        if len(fans)<=1: continue
        fans.sort(key=len,reverse=True)
        for fan in fans[1:]:
            clone=bm.verts.new(vertex.co)
            deform=bm.verts.layers.deform.active
            if deform is not None:
                for group,weight in vertex[deform].items(): clone[deform][group]=weight
            clone[source_layer]=vertex[source_layer]
            old_edges=set(edge for face in fan for edge in face.edges if vertex in edge.verts)
            face_data=[{"vertices":[clone if loop.vert is vertex else loop.vert for loop in face.loops],
                "uv":[{layer:loop[layer].uv.copy() for layer in uv_layers} for loop in face.loops],
                "material":face.material_index,"smooth":face.smooth,"id":face[id_layer]} for face in fan]
            bmesh.ops.delete(bm,geom=list(fan),context="FACES_ONLY")
            wire=[edge for edge in old_edges if edge.is_valid and not edge.link_faces]
            if wire: bmesh.ops.delete(bm,geom=wire,context="EDGES")
            for data in face_data:
                added=bm.faces.new(data["vertices"])
                added.material_index=data["material"]
                added.smooth=data["smooth"]
                added[id_layer]=data["id"]
                for loop,uv in zip(added.loops,data["uv"]):
                    for layer in uv_layers: loop[layer].uv=uv[layer]
            records.append({"source_vertex":vertex[source_layer]-1,"new_vertex_object":clone,
                            "copied_face_count":len(face_data),"position":list(vertex.co)})
    bm.verts.index_update()
    for record in records:
        record["new_vertex"]=record.pop("new_vertex_object").index
    # Full mapping also proves that preexisting indices stay stable when this
    # operation is applied to an already rigged review mesh.
    mapping=[vertex[source_layer]-1 for vertex in bm.verts]
    bm.verts.layers.int.remove(source_layer)
    return {"duplicated_vertices":records,"new_to_source_vertex_indices":mapping,
            "remaining_nonmanifold_vertices":sum(not v.is_manifold for v in bm.verts),
            "weight_transfer":"Copy each new vertex's weights from new_to_source_vertex_indices[new_index]."}


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--input",required=True)
    parser.add_argument("--output",required=True)
    parser.add_argument("--label",required=True)
    parser.add_argument("--split-stella-boots",action="store_true")
    parser.add_argument("--micro-patches",action="store_true")
    parser.add_argument("--remove-subvoxel-noise",action="store_true")
    parser.add_argument("--voxel-size",type=float,default=.0015)
    parser.add_argument("--split-pinched-vertices",action="store_true")
    parser.add_argument("--maximum-review-faces",type=int,default=300000,
        help="Explicit provisional LOD0 review cap; default 300k. Never permits million-face failed reductions.")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    if not 1 <= args.maximum_review_faces <= 600000:
        raise RuntimeError("Review face cap must remain within 1..600000.")
    source=Path(args.input).resolve()
    output=Path(args.output).resolve()
    if source==output or output.suffix!=".blend" or "Assets" in output.parts:
        raise RuntimeError("Output must be a separate .blend review candidate outside Assets.")
    output.parent.mkdir(parents=True,exist_ok=True)
    report_path=output.with_suffix(".repair.json")
    initial_hash=sha256(source)
    report={"input":str(source),"input_sha256_before":initial_hash,"output":str(output),
        "label":args.label,"status":"Inspecting candidate; final rig/import approval remains separate."}
    try:
        bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
        meshes=[obj for obj in bpy.context.scene.objects if obj.type=="MESH"]
        if len(meshes)!=1 or any(m.type=="ARMATURE" for m in meshes[0].modifiers):
            raise RuntimeError("Expected one unrigged candidate mesh.")
        obj=meshes[0]
        report["maximum_review_faces"]=args.maximum_review_faces
        if len(obj.data.polygons)>args.maximum_review_faces:
            raise RuntimeError("Candidate exceeds the explicit review face cap; resolve the reduction before local game-mesh repair.")
        report["before"]=topology(obj.data)
        coordinates=array(obj.data.vertices,"co",(len(obj.data.vertices),3))
        edges=array(obj.data.edges,"vertices",(len(obj.data.edges),2),np.int32)
        report["feet_before"]=lower_leg_components(edges,coordinates,-.60)
        del coordinates,edges
        write_json(report_path,report)
        print("CANDIDATE_REPAIR_BEGIN",args.label,json.dumps(report["before"]),flush=True)
        bm=bmesh.new()
        bm.from_mesh(obj.data)
        uv_layers=list(bm.loops.layers.uv.values())
        id_layer=bm.faces.layers.int.new("_RepairSourceFace")
        for index,face in enumerate(bm.faces):
            face[id_layer]=index+1
        # The full loop signatures are practical for the requested game-budget
        # candidate. Refuse million-face derivatives to avoid concealing a failed
        # reduction and allocating a second massive Python face representation.
        signatures={face[id_layer]:face_signature(face,uv_layers) for face in bm.faces}
        changed=set()
        if args.remove_subvoxel_noise:
            report["subvoxel_noise"],modified=remove_subvoxel_noise(bm,id_layer,args.voxel_size)
            changed.update(modified)
        if args.split_stella_boots:
            report["boot_split"],modified=boot_split(bm,uv_layers,id_layer)
            changed.update(modified)
        if args.micro_patches:
            report["micro_patches"]=[]
            for repair_round in range(3):
                bad_before=sum(len(edge.link_faces)!=2 for edge in bm.edges)
                bm.normal_update()
                patches,modified=repair_micro_patches(bm,uv_layers,id_layer)
                for patch in patches: patch["round"]=repair_round+1
                report["micro_patches"].extend(patches)
                changed.update(modified)
                bad_after=sum(len(edge.link_faces)!=2 for edge in bm.edges)
                if bad_after==0 or bad_after>=bad_before: break
            report["edges_after_micro_passes"]={"boundary":sum(len(edge.link_faces)==1 for edge in bm.edges),
                "overshared":sum(len(edge.link_faces)>2 for edge in bm.edges)}
            write_json(report_path,report)
        # Separating an intersecting surface fan can expose an original zero-
        # thickness two-sided triangular detail. Preserve both UV sides while
        # giving only its rear surface a 10 um separation, rather than retaining
        # coincident duplicate faces or deleting a visible ornament.
        bm.verts.index_update()
        face_groups={}
        for face in bm.faces:
            if len(face.verts)==3:
                face_groups.setdefault(tuple(sorted(v.index for v in face.verts)),[]).append(face)
        report["double_face_shells"]=[]
        for group in face_groups.values():
            if len(group)!=2: continue
            first,opposite=group
            # A zero-thickness detail may touch another surface only at a
            # vertex. Detaching that pinched contact preserves all exterior
            # faces; an edge shared with exterior faces still requires review.
            if any(set(edge.link_faces)!=set(group) for edge in first.edges):
                report["held_duplicate_triangle"]={"faces":[face[id_layer] for face in group],
                    "points":[list(v.co) for v in first.verts],
                    "edge_face_users":[len(edge.link_faces) for edge in first.edges]}
                raise RuntimeError("A duplicate triangle belongs to a larger surface; inspect before automatic repair.")
            back_uv={tuple(loop.vert.co):{layer:loop[layer].uv.copy() for layer in uv_layers} for loop in opposite.loops}
            if opposite[id_layer]>0: changed.add(opposite[id_layer])
            bmesh.ops.delete(bm,geom=[opposite],context="FACES_ONLY")
            shell=preserve_sheet_as_shells(bm,[first],uv_layers,id_layer,opposite_uvs={first:back_uv})
            report["double_face_shells"].append(shell)
        if args.split_pinched_vertices:
            report["vertex_fan_split"]=split_pinched_vertex_fans(bm,uv_layers,id_layer)
        report["nonmanifold_vertices_after"]=sum(not vertex.is_manifold for vertex in bm.verts)
        preserved=0
        for face in bm.faces:
            index=face[id_layer]
            if index>0 and index not in changed:
                if face_signature(face,uv_layers)!=signatures[index]:
                    raise RuntimeError("Untouched source face position/UV/material changed: "+str(index))
                preserved+=1
        if preserved!=len(signatures)-len(changed):
            raise RuntimeError("An unselected original face disappeared or duplicated during repair.")
        report["untouched_face_signatures_verified"]=preserved
        report["intentionally_changed_original_faces"]=len(changed)
        bm.faces.layers.int.remove(id_layer)
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()
        report["after"]=topology(obj.data)
        obj.data.calc_loop_triangles()
        triangles=array(obj.data.loop_triangles,"vertices",(len(obj.data.loop_triangles),3),np.int32)
        report["duplicate_triangle_vertex_groups_after"]=int(len(triangles)-len(np.unique(np.sort(triangles,axis=1),axis=0)))
        coordinates=array(obj.data.vertices,"co",(len(obj.data.vertices),3))
        edges=array(obj.data.edges,"vertices",(len(obj.data.edges),2),np.int32)
        report["feet_after"]=lower_leg_components(edges,coordinates,-.60)
        if args.split_stella_boots and any(c["spans_left_and_right_foot_regions"] for c in report["feet_after"]["largest_components"]):
            raise RuntimeError("Stella foot-region connectivity remains joined after repair.")
        report["uv_layers"]=[]
        for layer in obj.data.uv_layers:
            uv=array(layer.data,"uv",(len(layer.data),2))
            nonfinite=int(np.count_nonzero(~np.isfinite(uv)))
            if nonfinite:
                raise RuntimeError("Nonfinite UV after repair.")
            report["uv_layers"].append({"name":layer.name,"nonfinite_values":nonfinite,
                "outside_unit_square_loops":int(np.count_nonzero(np.any((uv<0)|(uv>1),axis=1)))})
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(output),compress=True)
        report["output_sha256"]=sha256(output)
        report["status"]="Saved derived repair candidate; inspect deformation and Unity import before acceptance."
    except Exception as error:
        import traceback
        report["status"]="Stopped without saving an accepted repair."
        report["error"]=str(error)
        report["traceback"]=traceback.format_exc()
    report["input_sha256_after"]=sha256(source)
    report["input_unchanged"]=initial_hash==report["input_sha256_after"]
    write_json(report_path,report)
    print("CANDIDATE_REPAIR_COMPLETE",args.label,report["status"],report.get("error",""),flush=True)


if __name__=="__main__":
    main()
