"""Locate source-to-derivative geometric loss, including face/limb review windows."""
import argparse
import sys
from pathlib import Path
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree
sys.path.insert(0, str(Path(__file__).resolve().parent))
from audit_raw_meshes import array, sha256, write_json


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--candidate", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    source, candidate = Path(args.source).resolve(), Path(args.candidate).resolve()
    hashes = [sha256(source), sha256(candidate)]
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    coordinates = array(obj.data.vertices, "co", (len(obj.data.vertices), 3))
    lo, hi = coordinates.min(axis=0), coordinates.max(axis=0)
    # 100k uniform-in-index source vertices: enough to localize loss, not an area-
    # weighted Hausdorff bound. Window labels are audit regions, not semantic masks.
    indices = np.linspace(0, len(coordinates)-1, min(100000, len(coordinates))).astype(np.int32)
    samples = coordinates[indices].copy()
    bpy.ops.wm.open_mainfile(filepath=str(candidate), load_ui=False, use_scripts=False)
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    mesh = obj.data
    mesh.calc_loop_triangles()
    vertices = array(mesh.vertices, "co", (len(mesh.vertices), 3))
    triangles = array(mesh.loop_triangles, "vertices", (len(mesh.loop_triangles), 3), np.int32)
    bvh = BVHTree.FromPolygons(vertices.tolist(), triangles.tolist(), all_triangles=True)
    nearest = [bvh.find_nearest(tuple(p)) for p in samples]
    distances = np.array([n[3] for n in nearest])
    height = hi[2]-lo[2]
    normalized = (samples-lo)/height
    x = np.abs(samples[:,0])/height
    z = normalized[:,2]
    y = (samples[:,1]-(lo[1]+hi[1])*.5)/height
    windows = {"all": np.ones(len(samples), dtype=bool),
               "head_including_hair": z > .79,
               "central_front_face_window": (z > .825)&(z < .915)&(x < .048)&(y < -.045),
               "hands_and_wrist_window": (x > .30)&(z > .64)&(z < .82),
               "lower_leg_and_cape_window": z < .45,
               "torso_and_upper_cape_window": (z >= .45)&(z <= .79)&(x < .17)}
    result = {"source": str(source), "candidate": str(candidate), "source_sha256": hashes[0],
              "candidate_sha256": hashes[1], "method": "100k uniform-index source vertex samples to nearest candidate triangle; local source units. Windows are approximate anatomical audit boxes, not skin regions.",
              "windows": {}, "worst_samples": []}
    for name, mask in windows.items():
        values = distances[mask]
        if len(values):
            result["windows"][name] = {"samples": len(values), "mean": float(values.mean()),
                                        "p95": float(np.quantile(values,.95)), "p99": float(np.quantile(values,.99)),
                                        "max": float(values.max())}
    for i in np.argsort(distances)[-30:][::-1]:
        result["worst_samples"].append({"source_vertex": int(indices[i]), "source_position": samples[i].tolist(),
                                        "target_position": list(nearest[i][0]), "distance": float(distances[i])})
    assert hashes == [sha256(source), sha256(candidate)]
    result["inputs_unchanged"] = True
    write_json(Path(args.output), result)
    print("SURFACE_MEASURED", result["windows"], flush=True)


if __name__ == "__main__":
    main()
