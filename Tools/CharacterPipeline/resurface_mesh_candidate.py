"""Measured micro-surface repair trial for a scan whose topology blocks reduction.

This is a separate candidate, never a replacement for the supplied source. Mesh
silhouette distances and visual comparison must pass; new UV/baked maps are a
separate mandatory gate. No unverified result is copied into Unity Assets.
"""
import argparse
import sys
import time
from pathlib import Path
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
from audit_raw_meshes import array, bounds, sha256, topology, write_json
from reduce_mesh_candidate import sampled_error


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--voxel", type=float, default=.0015)
    parser.add_argument("--triangles", type=int, default=100000)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    source, output = Path(args.source).resolve(), Path(args.output).resolve()
    if source.is_relative_to(output) or output == source.parent:
        raise RuntimeError("Output cannot contain source input.")
    output.mkdir(parents=True, exist_ok=True)
    record = {"label": args.label, "source": str(source), "source_sha256_before": sha256(source),
              "status": "Geometry trial only: UV/texture bake and deformation acceptance still required",
              "voxel_source_units": args.voxel, "target_triangles": args.triangles,
              "reason": "Original micro-handles prevented quadric reduction from reaching the game budget without large surface loss."}
    started = time.perf_counter()
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    bpy.context.preferences.filepaths.save_version = 0
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if len(meshes) != 1 or meshes[0].vertex_groups or meshes[0].modifiers:
        raise RuntimeError("Expected audited unrigged single source mesh.")
    obj = meshes[0]
    source_coordinates = array(obj.data.vertices, "co", (len(obj.data.vertices), 3))
    record["source_bounds"] = bounds(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    mod = obj.modifiers.new("Micro surface cleanup candidate", "REMESH")
    mod.mode = "VOXEL"
    mod.voxel_size = args.voxel
    mod.adaptivity = 0
    mod.use_smooth_shade = True
    mod.use_remove_disconnected = False
    print("RESURFACE_BEGIN", args.label, args.voxel, flush=True)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.calc_loop_triangles()
    record["resurfaced_triangles"] = len(obj.data.loop_triangles)
    print("RESURFACE_SURFACE", record["resurfaced_triangles"], flush=True)
    mod = obj.modifiers.new("Game surface budget candidate", "DECIMATE")
    mod.ratio = min(1., args.triangles/len(obj.data.loop_triangles))
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    record["topology"] = topology(obj.data)
    record["budget_passed"] = record["topology"]["triangles"] <= args.triangles*1.05
    record["sampled_surface_error"] = sampled_error(obj.data, source_coordinates)
    record["elapsed_seconds"] = time.perf_counter()-started
    record["source_sha256_after"] = sha256(source)
    assert record["source_sha256_before"] == record["source_sha256_after"]
    record["source_unchanged"] = True
    # The remesher drops UVs. Do not pretend original atlas lookup is valid.
    obj.data.materials.clear()
    mat = bpy.data.materials.new("Untextured geometry review only")
    mat.diffuse_color = (.45, .45, .45, 1)
    obj.data.materials.append(mat)
    path = output/(args.label+"_surface_candidate.blend")
    bpy.ops.wm.save_as_mainfile(filepath=str(path), compress=True)
    record["candidate_blend"] = str(path)
    record["candidate_sha256"] = sha256(path)
    write_json(output/(args.label+".json"), record)
    print("RESURFACE_COMPLETE", record["topology"], record["sampled_surface_error"], flush=True)


if __name__ == "__main__":
    main()
