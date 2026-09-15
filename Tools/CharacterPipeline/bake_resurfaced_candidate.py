"""Rebake supplied maps onto a reviewed derivative; source is never saved.

Cycles selected-to-active projection, not generated artwork. The result remains
a candidate until identical-angle texture/face/trim review passes.
"""
import argparse
import sys
from pathlib import Path
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
from audit_raw_meshes import sha256, write_json, topology


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--surface", required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--size", type=int, default=4096)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    source_path, surface_path, output = Path(args.source).resolve(), Path(args.surface).resolve(), Path(args.output).resolve()
    if source_path.is_relative_to(output) or surface_path.is_relative_to(output):
        raise RuntimeError("Bake output must be separate from both source inputs.")
    output.mkdir(parents=True, exist_ok=True)
    record = {"label": args.label, "source": str(source_path), "surface": str(surface_path),
              "source_sha256_before": sha256(source_path), "surface_sha256_before": sha256(surface_path),
              "status": "Texture bake candidate, visual inspection and rig deformation pending",
              "method": "Cycles selected-to-active EMIT albedo/packed roughness-metallic plus tangent NORMAL projection from the user's original packed maps.",
              "texture_size": args.size, "padding_pixels": 12,
              "cage_extrusion_source_units": .004, "ray_distance_source_units": .012}
    bpy.ops.wm.open_mainfile(filepath=str(source_path), load_ui=False, use_scripts=False)
    bpy.context.preferences.filepaths.save_version = 0
    sources = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if len(sources) != 1:
        raise RuntimeError("Expected audited single source mesh.")
    source = sources[0]
    source.name = args.label+"_HighSource"
    with bpy.data.libraries.load(str(surface_path), link=False) as (available, loaded):
        loaded.objects = available.objects
    targets = []
    for obj in loaded.objects:
        if obj and obj.type == "MESH":
            bpy.context.scene.collection.objects.link(obj)
            targets.append(obj)
    if len(targets) != 1:
        raise RuntimeError("Expected reviewed single target surface.")
    target = targets[0]
    target.name = args.label+"_BakedCandidate"
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    # Prototype atlas default: reserve a 12px bake dilation; 4096 maintains fine
    # trim coverage after introducing new charts, not new source image detail.
    bpy.ops.uv.smart_project(angle_limit=.75, island_margin=.003, area_weight=0., correct_aspect=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    mat = bpy.data.materials.new(args.label+"_OriginalAtlasRebake")
    mat.use_nodes = True
    target.data.materials.clear()
    target.data.materials.append(mat)
    principled = mat.node_tree.nodes.get("Principled BSDF")
    bake_node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    mat.node_tree.nodes.active = bake_node
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    scene.render.bake.use_selected_to_active = True
    scene.render.bake.use_clear = True
    scene.render.bake.cage_extrusion = .004
    scene.render.bake.max_ray_distance = .012
    scene.render.bake.margin = 12
    source.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    source_mat = source.data.materials[0]
    tree = source_mat.node_tree
    src_principled = next(n for n in tree.nodes if n.type == "BSDF_PRINCIPLED")
    src_output = next(n for n in tree.nodes if n.type == "OUTPUT_MATERIAL")
    old_surface = src_output.inputs["Surface"].links[0].from_socket
    emission = tree.nodes.new("ShaderNodeEmission")
    emission.inputs["Strength"].default_value = 1.
    maps = {}
    for role, bake_type, colorspace in (("BaseColor", "EMIT", "sRGB"), ("Normal", "NORMAL", "Non-Color"), ("Mask", "EMIT", "Non-Color")):
        image = bpy.data.images.new(args.label+"_"+role, args.size, args.size, alpha=False)
        image.colorspace_settings.name = colorspace
        image.generated_color = (.5, .5, 1., 1.) if role == "Normal" else (0., 0., 0., 1.)
        bake_node.image = image
        mat.node_tree.nodes.active = bake_node
        if role == "Normal":
            tree.links.new(old_surface, src_output.inputs["Surface"])
        else:
            if role == "BaseColor":
                socket = src_principled.inputs["Base Color"]
                if socket.is_linked:
                    tree.links.new(socket.links[0].from_socket, emission.inputs["Color"])
                else:
                    emission.inputs["Color"].default_value = socket.default_value
            else:
                combine = tree.nodes.new("ShaderNodeCombineColor")
                combine.mode = "RGB"
                combine.inputs[0].default_value = 1. # Unspecified AO is neutral.
                for index, name in ((1,"Roughness"), (2,"Metallic")):
                    socket = src_principled.inputs[name]
                    if socket.is_linked:
                        tree.links.new(socket.links[0].from_socket, combine.inputs[index])
                    else:
                        combine.inputs[index].default_value = socket.default_value
                tree.links.new(combine.outputs[0], emission.inputs["Color"])
            tree.links.new(emission.outputs[0], src_output.inputs["Surface"])
        print("BAKE_BEGIN", role, args.size, flush=True)
        bpy.ops.object.bake(type=bake_type)
        path = output/(args.label+"_"+role+".png")
        image.filepath_raw = str(path)
        image.file_format = "PNG"
        image.save()
        maps[role] = image
        record.setdefault("maps", []).append({"role": role, "path": str(path), "sha256": sha256(path), "colorspace": colorspace})
        print("BAKE_SAVED", role, flush=True)
    # Build the review material only from these correctly identified channels.
    base = mat.node_tree.nodes.new("ShaderNodeTexImage"); base.image = maps["BaseColor"]
    mat.node_tree.links.new(base.outputs["Color"], principled.inputs["Base Color"])
    normal = mat.node_tree.nodes.new("ShaderNodeTexImage"); normal.image = maps["Normal"]
    unpack = mat.node_tree.nodes.new("ShaderNodeNormalMap")
    mat.node_tree.links.new(normal.outputs["Color"], unpack.inputs["Color"])
    mat.node_tree.links.new(unpack.outputs["Normal"], principled.inputs["Normal"])
    packed = mat.node_tree.nodes.new("ShaderNodeTexImage"); packed.image = maps["Mask"]
    separate = mat.node_tree.nodes.new("ShaderNodeSeparateColor"); separate.mode = "RGB"
    mat.node_tree.links.new(packed.outputs["Color"], separate.inputs[0])
    mat.node_tree.links.new(separate.outputs[1], principled.inputs["Roughness"])
    mat.node_tree.links.new(separate.outputs[2], principled.inputs["Metallic"])
    mat.node_tree.nodes.remove(bake_node)
    for obj in list(bpy.context.scene.objects):
        if obj != target:
            bpy.data.objects.remove(obj, do_unlink=True)
    for image in maps.values():
        image.pack()
    target.data.calc_loop_triangles()
    record["triangles"] = len(target.data.loop_triangles)
    record["uv_layers"] = [u.name for u in target.data.uv_layers]
    path = output/(args.label+"_baked_candidate.blend")
    bpy.ops.wm.save_as_mainfile(filepath=str(path), compress=True)
    record["candidate_blend"] = str(path)
    record["candidate_sha256"] = sha256(path)
    record["source_sha256_after"], record["surface_sha256_after"] = sha256(source_path), sha256(surface_path)
    assert record["source_sha256_before"] == record["source_sha256_after"]
    assert record["surface_sha256_before"] == record["surface_sha256_after"]
    record["inputs_unchanged"] = True
    write_json(output/(args.label+".json"), record)
    print("BAKE_COMPLETE", args.label, flush=True)


if __name__ == "__main__":
    main()
