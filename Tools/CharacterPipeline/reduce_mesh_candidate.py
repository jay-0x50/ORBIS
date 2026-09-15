"""UV-preserving *candidate* reduction, never a declaration of finished retopology.

Original input is read-only. Outputs are reviewable derivatives outside Assets.
Blender --background --factory-startup --disable-autoexec --python this.py --
  --source ... --label Stella --triangles 100000 --output ... [--render]
Joint deformation, cloth separation and Unity import must pass later gates.
"""
import argparse
import json
import sys
import time
from pathlib import Path

import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from audit_raw_meshes import array, bounds, sha256, topology, write_json, render_studio


def detail_weights(obj):
    """Keep extra samples on head/hands; measured coordinates, not a design edit.

    Defaults describe the two supplied near-T-pose hero meshes only. They do not
    infer monster anatomy. Full head weighting protects face AND hair contours.
    """
    coordinates = array(obj.data.vertices, "co", (len(obj.data.vertices), 3))
    lo, hi = coordinates.min(axis=0), coordinates.max(axis=0)
    height = hi[2]-lo[2]
    z = (coordinates[:, 2]-lo[2])/height
    x = np.abs(coordinates[:, 0]-(hi[0]+lo[0])*.5)/height
    weight = np.maximum(np.clip((z-.79)/.045, 0, 1), np.clip((x-.29)/.055, 0, 1)*.6)
    # Quantized weights avoid millions of one-vertex API operations. Blender's
    # collapse cost retains LOW-weight regions; this is not a deformation group.
    # Every vertex must belong to the group: zero/unassigned weights LOCK the
    # collapse in Blender, rather than simply giving the region a low priority.
    quantized = 32-np.round(weight*31).astype(np.int32)
    group = obj.vertex_groups.new(name="Candidate_DetailPriority")
    for value in range(1, 33):
        indices = np.flatnonzero(quantized == value).tolist()
        if indices:
            group.add(indices, value/32, "REPLACE")
    return group, coordinates


def extract_maps(folder):
    """Copy packed bytes exactly; no guessed recoloring or gamma conversion."""
    folder.mkdir(parents=True, exist_ok=True)
    result = []
    for material in bpy.data.materials:
        if not material.use_nodes:
            continue
        for node in material.node_tree.nodes:
            if node.type != "TEX_IMAGE" or not node.image:
                continue
            image = node.image
            if not image.packed_file:
                raise RuntimeError("Expected packed source map: "+image.name)
            payload = bytes(image.packed_file.data)
            extension = ".png" if payload[:8] == b"\x89PNG\r\n\x1a\n" else ".jpg" if payload[:2] == b"\xff\xd8" else None
            if extension is None:
                raise RuntimeError("Unrecognized packed map format: "+image.name)
            path = folder/(image.name+extension)
            path.write_bytes(payload)
            result.append({"image": image.name, "node": node.name, "path": str(path),
                           "colorspace": image.colorspace_settings.name,
                           "sha256": sha256(path), "bytes": len(payload),
                           "outputs": [{"socket": link.to_socket.name, "node": link.to_node.name}
                                       for output in node.outputs for link in output.links]})
    return result


def sampled_error(mesh, source_coordinates, count=20000):
    """Deterministic source-vertex -> reduced surface distance, not Hausdorff proof."""
    vertices = array(mesh.vertices, "co", (len(mesh.vertices), 3))
    mesh.calc_loop_triangles()
    triangles = array(mesh.loop_triangles, "vertices", (len(mesh.loop_triangles), 3), np.int32)
    bvh = BVHTree.FromPolygons(vertices.tolist(), triangles.tolist(), all_triangles=True)
    samples = source_coordinates[np.linspace(0, len(source_coordinates)-1, min(count, len(source_coordinates))).astype(np.int32)]
    distances = np.array([bvh.find_nearest(tuple(p))[3] for p in samples])
    return {"method": "One-way deterministic source vertex samples to candidate triangles; not a two-way surface bound.",
            "samples": len(samples), "mean_source_units": float(distances.mean()),
            "p95_source_units": float(np.quantile(distances, .95)),
            "p99_source_units": float(np.quantile(distances, .99)),
            "max_source_units": float(distances.max())}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--triangles", type=int, default=100000)
    parser.add_argument("--output", required=True)
    parser.add_argument("--render", action="store_true")
    parser.add_argument("--weighted", action="store_true", help="Experimental detail weighting; verify geometry before acceptance")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    source, output = Path(args.source).resolve(), Path(args.output).resolve()
    if output == source.parent or source.is_relative_to(output):
        raise RuntimeError("Derivatives must be separate from source directory.")
    output.mkdir(parents=True, exist_ok=True)
    started = time.perf_counter()
    digest = sha256(source)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    bpy.context.preferences.filepaths.save_version = 0
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 1 or meshes[0].vertex_groups or meshes[0].modifiers:
        raise RuntimeError("Candidate expects audited single unrigged mesh; do not silently replace a rig.")
    obj = meshes[0]
    obj.name = args.label+"_ReducedCandidate"
    obj.data.calc_loop_triangles()
    source_triangles = len(obj.data.loop_triangles)
    if args.triangles < 10000 or args.triangles >= source_triangles:
        raise RuntimeError("Invalid candidate budget.")
    record = {"label": args.label, "source": str(source), "source_sha256_before": digest,
              "status": "Review candidate only; not skinning/import approved", "budget_triangles": args.triangles,
              "source_triangles": source_triangles, "source_bounds": bounds(obj),
              "method": "Quadric collapse preserving original UV/material layers; optional low-weight detail protection, no voxel remesh, redesign or automatic boundary filling.",
              "maps": extract_maps(output/"Textures")}
    write_json(output/(args.label+".json"), record)
    if args.weighted:
        group, source_coordinates = detail_weights(obj)
    else:
        group = None
        source_coordinates = array(obj.data.vertices, "co", (len(obj.data.vertices), 3))
    modifier = obj.modifiers.new("UV preserving game budget candidate", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = args.triangles/source_triangles
    modifier.use_collapse_triangulate = True
    if group is not None:
        modifier.vertex_group = group.name
        modifier.vertex_group_factor = .001
    record["weighted"] = args.weighted
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    print("REDUCTION_BEGIN", args.label, source_triangles, "->", args.triangles, flush=True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    # Applying a modifier invalidates some RNA handles in Blender 5.2.
    group = obj.vertex_groups.get("Candidate_DetailPriority")
    if group is not None:
        obj.vertex_groups.remove(group)
    record["topology"] = topology(obj.data)
    record["budget_passed"] = record["topology"]["triangles"] <= args.triangles*1.05
    if not record["budget_passed"]:
        record["status"] = "Rejected: actual triangle count exceeds candidate budget"
    record["sampled_surface_error"] = sampled_error(obj.data, source_coordinates)
    record["geometry_error_gate_passed"] = record["sampled_surface_error"]["p99_source_units"] < .002 and record["sampled_surface_error"]["max_source_units"] < .005
    if not record["geometry_error_gate_passed"]:
        record["status"] = "Rejected: sampled surface loss exceeds 2mm p99 or 5mm maximum; do not rig or import"
    del source_coordinates
    for uv in obj.data.uv_layers:
        coordinates = array(uv.data, "uv", (len(uv.data), 2))
        if not np.isfinite(coordinates).all():
            raise RuntimeError("Reduction generated nonfinite UVs.")
    record["source_sha256_after"] = sha256(source)
    assert record["source_sha256_after"] == digest, "Source must remain byte-identical"
    record["source_unchanged"] = True
    record["elapsed_reduction_seconds"] = round(time.perf_counter()-started, 2)
    path = output/(args.label+"_candidate.blend")
    bpy.ops.wm.save_as_mainfile(filepath=str(path), compress=True)
    record["candidate_blend"] = str(path)
    record["candidate_sha256"] = sha256(path)
    write_json(output/(args.label+".json"), record)
    print("REDUCTION_SAVED", args.label, json.dumps(record["topology"]), flush=True)
    if args.render:
        render_studio(output, record, 1024)
        # render_studio uses the same source-bounds convention; geometric bounds
        # changes are recorded so angle/scale equivalence can be checked explicitly.
        write_json(output/(args.label+".json"), record)
    print("REDUCTION_COMPLETE", args.label, flush=True)


if __name__ == "__main__":
    main()
