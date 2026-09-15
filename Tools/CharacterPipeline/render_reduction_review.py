"""Render a derivative using precisely the source audit's framing and lighting."""
import argparse
import json
import sys
from pathlib import Path
import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from audit_raw_meshes import render_studio, sha256, write_json


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--blend", required=True)
    parser.add_argument("--source-report", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--closeups", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    source = json.loads(Path(args.source_report).read_text(encoding="utf-8"))
    path, output = Path(args.blend).resolve(), Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    record = {"label": args.label, "rendered_blend": str(path), "blend_sha256_before": sha256(path),
              "source_audit": str(Path(args.source_report).resolve()),
              "camera_policy": "Identical source bounds, key/fill/rim positions and intensities, AgX transform and orthographic framing."}
    bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False)
    render_studio(output, record, 1024, fixed_bounds=source["render_bounds"])
    for expected, actual in zip(source["renders"], record["renders"]):
        assert max(abs(a-b) for a,b in zip(expected["camera_position"], actual["camera_position"])) < 1e-5
        assert abs(expected["orthographic_scale"]-actual["orthographic_scale"]) < 1e-5
    if args.closeups:
        scene, camera = bpy.context.scene, bpy.context.scene.camera
        lo, hi = Vector(source["render_bounds"]["min"]), Vector(source["render_bounds"]["max"])
        height, center = hi.z-lo.z, (hi+lo)*.5
        # Review cameras only: normalized windows encompass head and torso on the
        # supplied near-T-pose heroes. They do not move or reshape any geometry.
        for name, altitude, size in (("head_close", .87, .33), ("torso_close", .62, .48)):
            target = Vector((center.x, center.y, lo.z+height*altitude))
            camera.location = target+Vector((0,-1,.025)).normalized()*height*3
            camera.rotation_euler = (target-camera.location).to_track_quat("-Z", "Y").to_euler()
            camera.data.ortho_scale = height*size
            scene.render.filepath = str(output/(args.label+"_"+name+".png"))
            bpy.ops.render.render(write_still=True)
            record["renders"].append({"view": name, "path": scene.render.filepath,
                                      "camera_position": list(camera.location), "camera_target": list(target),
                                      "orthographic_scale": camera.data.ortho_scale})
    record["blend_sha256_after"] = sha256(path)
    assert record["blend_sha256_before"] == record["blend_sha256_after"]
    record["blend_unchanged"] = True
    write_json(output/(args.label+".json"), record)


if __name__ == "__main__":
    main()
