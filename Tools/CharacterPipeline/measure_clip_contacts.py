"""Describe actual Animator skin-sole samples. No automatic artistic pass/fail score."""
import argparse
import hashlib
import json
import math
from pathlib import Path


def vec(v):
    return [v[a] for a in ("x", "y", "z")]


def stats(values):
    return {"count": len(values), "minimum": min(values), "maximum": max(values),
            "mean": sum(values) / len(values)} if values else {"count": 0}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("review", type=Path)
    args = parser.parse_args()
    output = args.review.with_name("contact_metrics.json")
    if output.exists():
        raise FileExistsError("Preserve prior metrics: " + str(output))
    review = json.loads(args.review.read_text(encoding="utf-8-sig"))
    result = {"sourceReview": str(args.review), "sha256": hashlib.sha256(args.review.read_bytes()).hexdigest(),
              "scope": "Actual Humanoid Animator playback; skin vertices selected on imported-rest shoe sole and re-baked for each pose. Native cycle travel added analytically for in-place clips, not actual PlayerMotor/IK validation.",
              "clips": []}
    for clip in review["clips"]:
        name = clip["slot"]
        direction = [-1, 0, 0] if name.endswith("Left") else [1, 0, 0] if name.endswith("Right") else [0, 0, -1] if name.endswith("Back") else [0, 0, 1]
        travel = clip["authoredCycleDistance"] * clip["previewScaleRatio"]
        entry = {"slot": name, "nativeCycleDistance": travel, "nativeSpeed": travel / clip["duration"], "sides": {}}
        for side in ("left", "right"):
            skin = side + "SkinSole"
            contact = side + "Contact"
            heights, plant_heights, sole_heights, speeds, ground_point_speeds = [], [], [], [], []
            supported_samples = 0
            previous = None
            for sample in clip["samples"]:
                if not sample["finite"]:
                    raise ValueError("Nonfinite playback pose")
                y = sample[skin]["minimumY"]
                heights.append(y)
                sole_heights.append(sample[side + "Sole"]["y"])
                planted = sample[contact] >= .8
                if planted:
                    plant_heights.append(y)
                point = vec(sample[skin]["centroid"])
                point = [point[i] + direction[i] * travel * sample["normalized"] for i in range(3)]
                # A rolling sole centroid may move naturally. Track the SAME actual mesh
                # vertex only while it stays within6mm of the flat review ground (Y=0).
                ground_points = {pid: vec(p) for pid, p in zip(sample[skin].get("pointIds", []), sample[skin].get("pointsWorld", []))
                                 if abs(p["y"]) <= .006}
                if planted and ground_points:
                    supported_samples += 1
                if previous and planted and previous[2]:
                    dt = (sample["normalized"] - previous[0]) * clip["duration"]
                    if dt > 0:
                        speeds.append(math.hypot(point[0] - previous[1][0], point[2] - previous[1][2]) / dt)
                        for pid in ground_points.keys() & previous[3].keys():
                            v, old = ground_points[pid], previous[3][pid]
                            dn = sample["normalized"] - previous[0]
                            ground_point_speeds.append(math.hypot(v[0]-old[0]+direction[0]*travel*dn,v[2]-old[2]+direction[2]*travel*dn)/dt)
                previous = (sample["normalized"], point, planted, ground_points)
            entry["sides"][side] = {"actualSkinSoleMinimumY": stats(heights),
                                      "plantedSkinSoleMinimumY": stats(plant_heights),
                                      "footLocalProxyY": stats(sole_heights),
                                      "plantedSkinCentroidHorizontalSpeedNativeMps": stats(speeds),
                                      "groundNearVertexHorizontalSpeedNativeMps": stats(ground_point_speeds),
                                      "plantedSamplesWithActualSoleVertexWithin6mmOfGround": supported_samples}
        result["clips"].append(entry)
    output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(output)


if __name__ == "__main__":
    main()
