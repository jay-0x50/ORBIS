"""CPU-only, read-only localization of source topology defects.

Never repairs a file automatically. The reports distinguish geometric evidence
from anatomical interpretation; a centerline crossing may be cape, not skin.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy
import numpy as np


def digest(path):
    result = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(8*1024*1024), b""):
            result.update(chunk)
    return result.hexdigest()


def read_array(collection, name, shape, dtype=np.float32):
    result = np.empty(shape, dtype=dtype)
    collection.foreach_get(name, result.ravel())
    return result


def describe_points(points):
    return {"min": points.min(axis=0).tolist(), "max": points.max(axis=0).tolist(),
            "centroid": points.mean(axis=0).tolist()}


def edge_components(indices, edges, coordinates):
    adjacency = {}
    for edge_index in indices:
        a, b = (int(v) for v in edges[edge_index])
        adjacency.setdefault(a, []).append((b, int(edge_index)))
        adjacency.setdefault(b, []).append((a, int(edge_index)))
    remaining, result = set(adjacency), []
    while remaining:
        stack, vertices, component_edges = [remaining.pop()], set(), set()
        while stack:
            vertex = stack.pop()
            if vertex in vertices:
                continue
            vertices.add(vertex)
            remaining.discard(vertex)
            for neighbor, edge_index in adjacency[vertex]:
                component_edges.add(edge_index)
                if neighbor not in vertices:
                    stack.append(neighbor)
        vertex_list = sorted(vertices)
        edge_list = sorted(component_edges)
        lengths = np.linalg.norm(coordinates[edges[edge_list,0]]-coordinates[edges[edge_list,1]], axis=1)
        result.append({
            "vertex_count": len(vertex_list), "edge_count": len(edge_list),
            "closed_unbranched_loop": all(len(adjacency[v]) == 2 for v in vertex_list),
            "degree_one_vertices": sum(len(adjacency[v]) == 1 for v in vertex_list),
            "branch_vertices": sum(len(adjacency[v]) > 2 for v in vertex_list),
            "length_sum": float(lengths.sum()), "maximum_edge_length": float(lengths.max()),
            "local_bounds": describe_points(coordinates[vertex_list]),
            "vertex_indices": vertex_list, "edge_indices": edge_list,
        })
    return sorted(result, key=lambda c: c["edge_count"], reverse=True)


def lower_leg_components(edges, coordinates, cutoff):
    included = coordinates[:,2] < cutoff
    selected = edges[included[edges[:,0]] & included[edges[:,1]]]
    parents = np.arange(len(coordinates),dtype=np.int32)
    sizes = np.ones(len(coordinates),dtype=np.int32)
    for va,vb in selected:
        a,b=int(va),int(vb)
        while parents[a] != a:
            parents[a]=parents[parents[a]]
            a=int(parents[a])
        while parents[b] != b:
            parents[b]=parents[parents[b]]
            b=int(parents[b])
        if a != b:
            if sizes[a] < sizes[b]: a,b=b,a
            parents[b]=a
            sizes[a]+=sizes[b]
    verts=np.flatnonzero(included)
    roots=parents[verts]
    while True:
        next_roots=parents[roots]
        if np.array_equal(next_roots,roots): break
        roots=next_roots
    unique,counts=np.unique(roots,return_counts=True)
    order=np.argsort(-counts)
    components=[]
    for index in order[:12]:
        points=coordinates[verts[roots==unique[index]]]
        components.append({"vertex_count":int(counts[index]),"bounds":describe_points(points),
            "spans_left_and_right_foot_regions":bool(points[:,0].min() < -0.05 and points[:,0].max() > 0.05)})
    return {"local_z_cutoff":cutoff,"component_count":len(unique),"largest_components":components,
        "interpretation":"Induced graph restricted below cutoff. A component reaching both X<-0.05 and X>0.05 is a measured join spanning both foot regions; it may still include cloth/trim, so inspect before cutting."}


def audit(source, output, label):
    before = digest(source)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    record = {"source": str(source), "source_sha256_before": before, "label": label,
              "coordinate_space": "Raw mesh LOCAL coordinates; matrix_world is recorded per object.",
              "objects": [], "policy": "Read-only inspection. No welding, normals change, face deletion or saving original."}
    output.mkdir(parents=True, exist_ok=True)
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        coordinates = read_array(mesh.vertices, "co", (len(mesh.vertices),3))
        edges = read_array(mesh.edges, "vertices", (len(mesh.edges),2), np.int32)
        loop_edges = read_array(mesh.loops, "edge_index", len(mesh.loops), np.int32)
        uses = np.bincount(loop_edges, minlength=len(mesh.edges))
        boundary = np.flatnonzero(uses == 1)
        overshared = np.flatnonzero(uses > 2)
        mesh.calc_loop_triangles()
        triangles = read_array(mesh.loop_triangles, "vertices", (len(mesh.loop_triangles),3), np.int32)
        sorted_faces = np.sort(triangles, axis=1)
        _, first, counts = np.unique(sorted_faces, axis=0, return_index=True, return_counts=True)
        duplicated = first[counts > 1]
        item = {
            "name": obj.name, "matrix_world": [list(row) for row in obj.matrix_world],
            "bounds": describe_points(coordinates),
            "boundary_components": edge_components(boundary, edges, coordinates),
            "overshared_components": edge_components(overshared, edges, coordinates),
            "duplicate_triangle_groups": len(duplicated),
            "duplicate_triangle_excess_count": int((counts[counts > 1]-1).sum()),
            "duplicate_triangle_locations": [dict(triangle_index=int(i), vertices=triangles[i].tolist(),
                local_centroid=coordinates[triangles[i]].mean(axis=0).tolist()) for i in duplicated[:500]],
        }
        # For every defective edge include neighboring triangles and UV values.
        # Matching a small defect-index set against all loops remains O(loop count).
        selected = np.concatenate((boundary,overshared))
        loop_vertices = read_array(mesh.loops, "vertex_index", len(mesh.loops), np.int32)
        uv = read_array(mesh.uv_layers.active.data, "uv", (len(mesh.loops),2)) if mesh.uv_layers.active else None
        polygon_starts = read_array(mesh.polygons, "loop_start", len(mesh.polygons), np.int32)
        poly_for_loop = np.searchsorted(polygon_starts, np.flatnonzero(np.isin(loop_edges,selected)), side="right")-1
        defect_loops = np.flatnonzero(np.isin(loop_edges,selected))
        edge_details = {int(index): {"edge_index": int(index), "face_users": int(uses[index]),
            "vertices": edges[index].tolist(), "points": coordinates[edges[index]].tolist(), "loops": []}
            for index in selected}
        for loop, polygon in zip(defect_loops, poly_for_loop):
            index = int(loop_edges[loop])
            edge_details[index]["loops"].append({"polygon": int(polygon), "loop": int(loop),
                "vertex": int(loop_vertices[loop]), "uv": uv[loop].tolist() if uv is not None else None})
        item["defective_edges"] = list(edge_details.values())
        # Report centerline crossings by height/depth rather than claiming skin
        # bridges from a fully dressed single mesh. Zero X crossing is a precise
        # spatial diagnostic, not an anatomical classification.
        cross = ((coordinates[edges[:,0],0] < 0) & (coordinates[edges[:,1],0] > 0)) | ((coordinates[edges[:,0],0] > 0) & (coordinates[edges[:,1],0] < 0))
        crossing_edges = edges[cross]
        crossing_indices = np.flatnonzero(cross)
        mids = (coordinates[crossing_edges[:,0]]+coordinates[crossing_edges[:,1]])*0.5
        lengths = np.linalg.norm(coordinates[crossing_edges[:,0]]-coordinates[crossing_edges[:,1]],axis=1)
        bins = []
        zmin,zmax = float(coordinates[:,2].min()),float(coordinates[:,2].max())
        for low,high in zip(np.linspace(zmin,zmax,33)[:-1],np.linspace(zmin,zmax,33)[1:]):
            mask = (mids[:,2] >= low) & (mids[:,2] < high)
            bins.append({"z_min": float(low), "z_max": float(high), "crossing_edge_count": int(mask.sum()),
                "depth_y_min": float(mids[mask,1].min()) if mask.any() else None,
                "depth_y_max": float(mids[mask,1].max()) if mask.any() else None,
                "max_edge_length": float(lengths[mask].max()) if mask.any() else None})
        item["centerline_crossing_height_bins"] = bins
        # Retain all crossings in lower 45% of height; cape can cross these bins.
        lower = mids[:,2] < zmin+(zmax-zmin)*0.45
        item["lower_centerline_crossings"] = [{"edge": int(index), "midpoint": point.tolist(), "length": float(length)}
            for index,point,length in zip(crossing_indices[lower],mids[lower],lengths[lower])]
        item["lower_centerline_interpretation"] = "Potential cloth, hanging ornaments or between-leg joins. Inspect Y depth and source visuals; do not cut automatically."
        item["lower_leg_connectivity"] = lower_leg_components(edges,coordinates,-0.60)
        record["objects"].append(item)
        print("DEFECTS "+label+" "+json.dumps({"boundary_components":len(item["boundary_components"]),
            "overshared_components":len(item["overshared_components"]), "duplicate_triangle_excess":item["duplicate_triangle_excess_count"],
            "lower_centerline_edges":len(item["lower_centerline_crossings"])}),flush=True)
        del coordinates,edges,loop_edges,uses,triangles,sorted_faces,loop_vertices,uv
    record["source_sha256_after"] = digest(source)
    record["source_unchanged"] = before == record["source_sha256_after"]
    (output / (label+"_topology_defects.json")).write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding="utf-8")


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--source-dir",required=True)
    parser.add_argument("--output-dir",required=True)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    for name,label in (("여주인공","Stella"),("남주인공","Polaris")):
        audit(Path(args.source_dir)/(name+".blend"),Path(args.output_dir),label)


if __name__=="__main__":
    main()
