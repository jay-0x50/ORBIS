"""Measure actual live PlayerMotor capture (never add synthetic root travel).

python measure_player_contacts.py <motion.json>
Fixed actual skinned vertex IDs must be near the test floor at both consecutive
frames. Contact centroid motion alone is not called sliding; heel/toe roll can move it.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics


def stats(values):
    if not values:
        return {"count": 0}
    ordered = sorted(values)
    return {"count": len(values), "minimum": min(values), "mean": statistics.mean(values),
            "p95": ordered[min(len(ordered)-1, int(len(ordered)*.95))], "maximum": max(values)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("motion", type=Path)
    args = parser.parse_args()
    raw = args.motion.read_bytes()
    data = json.loads(raw.decode("utf-8-sig"))
    output = args.motion.parent / "player_contact_metrics.json"
    if output.exists():
        raise FileExistsError("Preserve existing evidence: " + str(output))
    floor = data["floorCenter"]["y"]
    result = {"source": str(args.motion), "sha256": hashlib.sha256(raw).hexdigest(),
              "scope": "Actual live motor+Animator+IK skin positions. No analytical/root-motion displacement is added. Near-floor threshold6mm and both contact weights>=.8; empty contact sets are unavailable, not zero slip.",
              "floorY": floor, "phaseSummaries": []}
    for phase in dict.fromkeys(f["phase"] for f in data["frames"]):
        frames = [f for f in data["frames"] if f["phase"] == phase]
        entry = {"phase": phase, "rootSpeed": stats([f["measuredSpeed"] for f in frames]),
                 "yawLagDegrees": stats([abs(f["visualYawOffset"]) for f in frames]),
                 "strideRate": stats([f["strideRate"] for f in frames]), "sides": {}}
        for side in ["Left", "Right"]:
            samples = [(f, next(s for s in f["skinFeet"] if s["side"] == side)) for f in frames]
            planted = [s for f, s in samples if s["contact"] >= .8]
            velocities, intervals = [], 0
            for (fa, a), (fb, b) in zip(samples, samples[1:]):
                if fb["index"] != fa["index"]+1 or min(a["contact"], b["contact"]) < .8:
                    continue
                pa = {i: p for i, p in zip(a["pointIds"], a["pointsWorld"]) if abs(p["y"]-floor) <= .006}
                pb = {i: p for i, p in zip(b["pointIds"], b["pointsWorld"]) if abs(p["y"]-floor) <= .006}
                common = pa.keys() & pb.keys()
                dt = fb["deltaTime"]
                if not common or dt <= 0:
                    continue
                intervals += 1
                for i in common:
                    velocities.append(math.hypot(pb[i]["x"]-pa[i]["x"], pb[i]["z"]-pa[i]["z"])/dt)
            entry["sides"][side] = {
                "skinSoleMinimumHeight": stats([s["minimumY"]-floor for _, s in samples]),
                "plantedMinimumHeight": stats([s["minimumY"]-floor for s in planted]),
                "plantSamplesWithVertexWithin6mm": sum(any(abs(p["y"]-floor)<=.006 for p in s["pointsWorld"]) for s in planted),
                "contactSurfaceVelocityMps": stats(velocities), "validConsecutiveContactIntervals": intervals}
        result["phaseSummaries"].append(entry)
    output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(output)


if __name__ == "__main__":
    main()
