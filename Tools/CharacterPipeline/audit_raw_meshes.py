"""Read-only Blender source audit and neutral studio render.

Run with Blender --background --factory-startup --disable-autoexec --python ... --
  --source-dir <original Assets/blend> --output-dir <staging audit directory>
No source mesh, UV, rig, material, or file is altered on disk. Studio objects and
visibility exclusions exist only in the loaded in-memory render scene.
"""
import argparse
import hashlib
import json
import math
import sys
import time
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector


LABELS = {
    "여주인공": "Stella", "남주인공": "Polaris", "조력자": "Cosmo",
    "물보스몬스터": "WaterBoss", "바람보스몬스터": "WindBoss",
    "바위보스몬스터": "RockBoss", "번개보스몬스터": "LightningBoss",
    "불보스몬스터": "FireBoss",
}
ORDER = {name: index for index, name in enumerate(LABELS)}


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".partial.json")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")
    temporary.replace(path)


def array(collection, property_name, shape, dtype=np.float32):
    result = np.empty(shape, dtype=dtype)
    collection.foreach_get(property_name, result.ravel())
    return result


def bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return {
        "min": [min(p[axis] for p in points) for axis in range(3)],
        "max": [max(p[axis] for p in points) for axis in range(3)],
    }


def topology(mesh):
    """Use mesh edge/loop arrays, avoiding a second full BMesh allocation."""
    vertex_count, edge_count = len(mesh.vertices), len(mesh.edges)
    coordinates = array(mesh.vertices, "co", (vertex_count, 3))
    edge_vertices = array(mesh.edges, "vertices", (edge_count, 2), np.int32)
    loop_edges = array(mesh.loops, "edge_index", len(mesh.loops), np.int32)
    uses = np.bincount(loop_edges, minlength=edge_count)
    loose_vertices = vertex_count - np.unique(edge_vertices).size if edge_count else vertex_count
    # Coordinate equality is exact in source precision; welded vs deliberately split
    # vertices cannot be distinguished by this measurement alone.
    duplicate_count = vertex_count - np.unique(coordinates, axis=0).shape[0]
    mesh.calc_loop_triangles()
    triangles = array(mesh.loop_triangles, "vertices", (len(mesh.loop_triangles), 3), np.int32)
    degenerate = 0
    for start in range(0, len(triangles), 200_000):
        tri = triangles[start:start + 200_000]
        a, b, c = (coordinates[tri[:, column]] for column in range(3))
        area_squared = np.einsum("ij,ij->i", np.cross(b-a, c-a), np.cross(b-a, c-a))
        degenerate += int(np.count_nonzero(area_squared <= 1e-20))
    # Exact connected components via union-find. No adjacency matrix proportional
    # to vertices squared; topology arrays have linear storage.
    parents = np.arange(vertex_count, dtype=np.int32)
    sizes = np.ones(vertex_count, dtype=np.int32)
    for left, right in edge_vertices:
        a, b = int(left), int(right)
        while parents[a] != a:
            parents[a] = parents[parents[a]]
            a = int(parents[a])
        while parents[b] != b:
            parents[b] = parents[parents[b]]
            b = int(parents[b])
        if a != b:
            if sizes[a] < sizes[b]:
                a, b = b, a
            parents[b] = a
            sizes[a] += sizes[b]
    roots = np.flatnonzero(parents == np.arange(vertex_count))
    result = {
        "vertices": vertex_count, "edges": edge_count, "polygons": len(mesh.polygons),
        "triangles": len(triangles), "boundary_edges": int(np.count_nonzero(uses == 1)),
        "nonmanifold_edges": int(np.count_nonzero(uses != 2)),
        "over_shared_edges": int(np.count_nonzero(uses > 2)),
        "loose_edges": int(np.count_nonzero(uses == 0)), "loose_vertices": int(loose_vertices),
        "duplicate_exact_coordinate_vertices": int(duplicate_count),
        "degenerate_triangles_area_squared_threshold_1e_20": degenerate,
        "connected_islands": int(len(roots)),
        "largest_island_vertex_counts": sorted((int(sizes[r]) for r in roots), reverse=True)[:20],
        "nonfinite_coordinate_values": int(np.count_nonzero(~np.isfinite(coordinates))),
    }
    del coordinates, edge_vertices, loop_edges, triangles, parents, sizes
    return result


def mesh_record(obj):
    mesh = obj.data
    record = {
        "name": obj.name, "type": obj.type, "data_name": mesh.name,
        "location": list(obj.location), "rotation_euler": list(obj.rotation_euler),
        "scale": list(obj.scale), "world_matrix": [list(row) for row in obj.matrix_world],
        "world_bounds": bounds(obj), "hide_render": obj.hide_render,
        "hide_viewport": obj.hide_viewport, "parent": obj.parent.name if obj.parent else None,
        "collections": [c.name for c in obj.users_collection],
        "material_slots": [m.name if m else None for m in mesh.materials],
        "vertex_groups": [g.name for g in obj.vertex_groups],
        "shape_keys": [k.name for k in mesh.shape_keys.key_blocks] if mesh.shape_keys else [],
        "modifiers": [{"name": m.name, "type": m.type,
                       "show_render": m.show_render, "show_viewport": m.show_viewport,
                       "armature": m.object.name if m.type == "ARMATURE" and m.object else None}
                      for m in obj.modifiers],
        "topology": topology(mesh), "uv_layers": [],
    }
    for layer in mesh.uv_layers:
        uv = array(layer.data, "uv", (len(layer.data), 2))
        finite = uv[np.isfinite(uv).all(axis=1)]
        record["uv_layers"].append({
            "name": layer.name, "loops": len(uv), "active_render": layer.active_render,
            "nonfinite_values": int(np.count_nonzero(~np.isfinite(uv))),
            "min": finite.min(axis=0).tolist() if len(finite) else None,
            "max": finite.max(axis=0).tolist() if len(finite) else None,
            "outside_unit_square_loop_count": int(np.count_nonzero(np.any((uv < 0) | (uv > 1), axis=1))),
            "unique_uv_coordinate_count": int(np.unique(finite, axis=0).shape[0]),
        })
    return record


def reference_reason(obj):
    if obj.type == "EMPTY" and obj.empty_display_type == "IMAGE":
        return "Image reference empty (non-rendered reference by Blender definition)."
    if obj.type == "MESH" and len(obj.data.vertices) <= 4 and len(obj.data.polygons) <= 2:
        ref_name = any(word in obj.name.lower() for word in ("reference", "ref_", "image", "参考", "참고"))
        if ref_name:
            return "Named image/reference mesh with at most four vertices; excluded only in memory."
    return None


def add_area(scene, name, position, target, power, size):
    data = bpy.data.lights.new(name, "AREA")
    data.energy, data.shape, data.size = power, "DISK", size
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = position
    obj.rotation_euler = (target-obj.location).to_track_quat("-Z", "Y").to_euler()


def render_studio(output_dir, record, resolution, fixed_bounds=None):
    scene = bpy.context.scene
    excluded = []
    visible = []
    for obj in list(scene.objects):
        reason = reference_reason(obj)
        if obj.type in {"CAMERA", "LIGHT"}:
            reason = "Original camera/light excluded for documented neutral studio replacement."
        if reason:
            excluded.append({"name": obj.name, "type": obj.type, "reason": reason})
            obj.hide_render = True
        elif obj.type == "MESH" and not obj.hide_render and obj.visible_get():
            visible.append(obj)
    record["render_excluded_objects"] = excluded
    if not visible:
        record["render_error"] = "No visible renderable source meshes; source collection visibility was preserved."
        return
    lows = Vector((min(bounds(o)["min"][i] for o in visible) for i in range(3)))
    highs = Vector((max(bounds(o)["max"][i] for o in visible) for i in range(3)))
    if fixed_bounds is not None:
        record["measured_candidate_bounds"] = {"min": list(lows), "max": list(highs)}
        lows, highs = Vector(fixed_bounds["min"]), Vector(fixed_bounds["max"])
    center = (lows+highs)*0.5
    extent = highs-lows
    size = max(extent)
    record["render_bounds"] = {"min": list(lows), "max": list(highs)}
    record["render_visible_meshes"] = [o.name for o in visible]
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.use_file_extension = True
    scene.world = bpy.data.worlds.new("Audit neutral studio")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.12, 0.12, 0.12, 1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.25
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1
    # Light power scales with the source size to keep exposure consistent even
    # when raw generation files use centimeters or unnormalized object units.
    add_area(scene, "Audit key", center+Vector((-1.4,-1.8,1.6))*size, center, 80*size*size, size*1.3)
    add_area(scene, "Audit fill", center+Vector((1.7,-0.5,0.8))*size, center, 40*size*size, size*1.5)
    add_area(scene, "Audit rim", center+Vector((0.5,1.5,1.8))*size, center, 80*size*size, size)
    data = bpy.data.cameras.new("Audit camera")
    camera = bpy.data.objects.new("Audit camera", data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    data.type = "ORTHO"
    data.clip_start = max(size*0.001, 0.0001)
    data.clip_end = size*30
    record["renders"] = []
    for name, direction in (("front", (0,-1,0.04)), ("side", (1,0,0.04)), ("three_quarter", (0.65,-1,0.15))):
        camera.location = center+Vector(direction).normalized()*size*3
        camera.rotation_euler = (center-camera.location).to_track_quat("-Z", "Y").to_euler()
        data.ortho_scale = size*1.25
        path = output_dir / (record["label"]+"_"+name+".png")
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        record["renders"].append({"view": name, "path": str(path), "camera_position": list(camera.location),
                                  "camera_target": list(center), "orthographic_scale": data.ortho_scale})
        write_json(output_dir / (record["label"]+".json"), record)
        print("AUDIT_RENDER "+record["label"]+" "+name+" "+str(path), flush=True)


def audit_file(source, output, resolution, no_render):
    started = time.perf_counter()
    initial_hash = sha256(source)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    label = LABELS.get(source.stem, source.stem)
    result = {
        "source": str(source), "label": label, "source_bytes": source.stat().st_size,
        "source_sha256_before": initial_hash, "blender_version": bpy.app.version_string,
        "audit_policy": "Read-only original source; no save_mainfile, remesh, decimation, welding or UV edits.",
        "topology_interpretation": "Nonmanifold includes boundaries/loose edges. Exact coordinate duplicates may be intentional seams or separate parts. These counts alone do not authorize destructive cleanup.",
        "scene": bpy.context.scene.name,
        "scene_frame": bpy.context.scene.frame_current,
        "scene_unit_system": bpy.context.scene.unit_settings.system,
        "scene_unit_scale": bpy.context.scene.unit_settings.scale_length,
        "collections": [{"name": c.name, "hide_render": c.hide_render, "hide_viewport": c.hide_viewport,
                         "objects": [o.name for o in c.objects]} for c in bpy.data.collections],
        "objects": [], "armatures": [], "actions": [], "images": [], "materials": [],
    }
    output.mkdir(parents=True, exist_ok=True)
    report_path = output / (label+".json")
    result["quick_mesh_inventory"] = [{"name": o.name, "vertices": len(o.data.vertices),
        "polygons": len(o.data.polygons), "world_bounds": bounds(o), "vertex_groups": len(o.vertex_groups),
        "modifiers": [m.type for m in o.modifiers]} for o in bpy.data.objects if o.type == "MESH"]
    write_json(report_path, result)
    print("AUDIT_INVENTORY "+label+" "+json.dumps(result["quick_mesh_inventory"]), flush=True)
    for obj in bpy.data.objects:
        if obj.type == "MESH":
            item = mesh_record(obj)
            result["objects"].append(item)
            print("AUDIT_MESH "+label+" "+obj.name+" "+json.dumps(item["topology"]), flush=True)
            write_json(report_path, result)
        else:
            result["objects"].append({"name": obj.name, "type": obj.type,
                                     "location": list(obj.location), "scale": list(obj.scale),
                                     "hide_render": obj.hide_render, "hide_viewport": obj.hide_viewport})
        if obj.type == "ARMATURE":
            result["armatures"].append({"name": obj.name, "bones": [{"name": b.name,
                "parent": b.parent.name if b.parent else None, "head": list(b.head_local),
                "tail": list(b.tail_local), "deform": b.use_deform} for b in obj.data.bones]})
    for action in bpy.data.actions:
        result["actions"].append({"name": action.name, "frame_range": list(action.frame_range),
            "slots": [slot.identifier for slot in action.slots] if hasattr(action, "slots") else []})
    for image in bpy.data.images:
        absolute = bpy.path.abspath(image.filepath) if image.filepath else ""
        result["images"].append({"name": image.name, "source": image.source, "size": list(image.size),
            "channels": image.channels, "filepath": image.filepath, "absolute_filepath": absolute,
            "packed": bool(image.packed_file or image.packed_files),
            "packed_bytes": sum(p.packed_file.size for p in image.packed_files),
            "external_exists": Path(absolute).is_file() if absolute else False,
            "colorspace": image.colorspace_settings.name})
    for material in bpy.data.materials:
        result["materials"].append({"name": material.name, "use_nodes": material.use_nodes,
            "diffuse_color": list(material.diffuse_color),
            "image_nodes": [{"node": n.name, "image": n.image.name if n.image else None}
                for n in material.node_tree.nodes if n.type == "TEX_IMAGE"] if material.use_nodes else []})
    result["totals"] = {key: sum(o["topology"][key] for o in result["objects"] if "topology" in o)
                         for key in ("vertices", "triangles", "polygons", "nonmanifold_edges", "boundary_edges",
                                     "degenerate_triangles_area_squared_threshold_1e_20", "duplicate_exact_coordinate_vertices")}
    write_json(report_path, result)
    if not no_render:
        try:
            render_studio(output, result, resolution)
        except Exception as error:
            import traceback
            result["render_error"] = str(error)
            result["render_traceback"] = traceback.format_exc()
    result["source_sha256_after"] = sha256(source)
    result["source_unchanged"] = initial_hash == result["source_sha256_after"]
    result["elapsed_seconds"] = round(time.perf_counter()-started, 3)
    write_json(report_path, result)
    print("AUDIT_COMPLETE "+label+" "+json.dumps(result["totals"])+" unchanged="+str(result["source_unchanged"]), flush=True)
    return {"label": label, "source": str(source), "report": str(report_path),
            "totals": result["totals"], "source_unchanged": result["source_unchanged"],
            "render_error": result.get("render_error"), "renders": result.get("renders", [])}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--only", default="")
    parser.add_argument("--skip", default="")
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--resolution", type=int, default=1024)
    parser.add_argument("--no-render", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    source_dir, output_dir = Path(args.source_dir), Path(args.output_dir)
    files = sorted(source_dir.rglob("*.blend"), key=lambda p: ORDER.get(p.stem, 999))
    if args.only:
        files = [p for p in files if p.stem == args.only or LABELS.get(p.stem) == args.only]
    if args.skip:
        files = [p for p in files if p.stem not in args.skip.split(",") and LABELS.get(p.stem) not in args.skip.split(",")]
    manifest = {"policy": "Original files are opened read-only in memory and never saved.", "sources": []}
    for source in files:
        try:
            if args.render_only:
                label = LABELS.get(source.stem, source.stem)
                report_path = output_dir / (label+".json")
                existing = json.loads(report_path.read_text(encoding="utf-8"))
                if sha256(source) != existing["source_sha256_before"]:
                    raise RuntimeError("Source changed since geometry audit; rerun full audit.")
                bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
                render_studio(output_dir, existing, args.resolution)
                existing["source_sha256_after"] = sha256(source)
                existing["source_unchanged"] = existing["source_sha256_before"] == existing["source_sha256_after"]
                write_json(report_path, existing)
                print("AUDIT_RENDER_ONLY_COMPLETE "+label, flush=True)
                continue
            manifest["sources"].append(audit_file(source, output_dir, args.resolution, args.no_render))
        except Exception as error:
            import traceback
            manifest["sources"].append({"source": str(source), "error": str(error), "traceback": traceback.format_exc()})
            print("AUDIT_ERROR "+str(source)+" "+str(error), flush=True)
        write_json(output_dir / ("manifest"+("_"+args.only if args.only else "")+".json"), manifest)


if __name__ == "__main__":
    main()
