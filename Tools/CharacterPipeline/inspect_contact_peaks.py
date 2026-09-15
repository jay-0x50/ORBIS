"""Separate sustained, interior skin contacts from contact-enter/exit samples."""
import argparse
import json
import math
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("review", type=Path)
    args = parser.parse_args()
    output = args.review.with_name("contact_peak_analysis.json")
    if output.exists():
        raise FileExistsError(output)
    review = json.loads(args.review.read_text(encoding="utf-8-sig"))
    report = {"source": str(args.review), "scope": "Same shoe mesh vertex near ground in both consecutive actual Animator samples; only contact>=.8 on both samples. Not gameplay-foot-IK validation.", "clips": []}
    for clip in review["clips"]:
        if clip["slot"] not in ("Walk", "Run", "WalkLeft", "WalkBack", "RunBack"):
            continue
        travel = clip["authoredCycleDistance"] * clip["previewScaleRatio"]
        direction = (-1, 0) if clip["slot"].endswith("Left") else (0, -1) if clip["slot"].endswith("Back") else (0, 1)
        sides = {}
        for side in ("left", "right"):
            pairs = []
            for a, b in zip(clip["samples"], clip["samples"][1:]):
                contact = side + "Contact"
                if min(a[contact], b[contact]) < .8:
                    continue
                sa, sb = a[side + "SkinSole"], b[side + "SkinSole"]
                points_a = {i: v for i, v in zip(sa["pointIds"], sa["pointsWorld"]) if abs(v["y"]) <= .006}
                points_b = {i: v for i, v in zip(sb["pointIds"], sb["pointsWorld"]) if abs(v["y"]) <= .006}
                dn = b["normalized"] - a["normalized"]
                if dn <= 0:
                    continue
                dt = dn * clip["duration"]
                values = [math.hypot(points_b[i]["x"] - points_a[i]["x"] + direction[0]*travel*dn,
                                    points_b[i]["z"] - points_a[i]["z"] + direction[1]*travel*dn) / dt
                          for i in points_a.keys() & points_b.keys()]
                if values:
                    pairs.append({"normalizedFrom": a["normalized"], "normalizedTo": b["normalized"],
                                  "contactFrom": a[contact], "contactTo": b[contact], "sharedGroundNearVertices": len(values),
                                  "groundNearVertexSpeedMean": sum(values)/len(values), "groundNearVertexSpeedMax": max(values),
                                  "interiorContactBothAbove99Percent": min(a[contact], b[contact]) >= .99})
            sides[side] = {"eligiblePairs": len(pairs), "highestFive": sorted(pairs, key=lambda p: p["groundNearVertexSpeedMax"], reverse=True)[:5]}
        report["clips"].append({"slot": clip["slot"], "sides": sides})
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(output)


if __name__ == "__main__":
    main()
