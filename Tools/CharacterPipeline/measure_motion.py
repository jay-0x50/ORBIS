"""Summarize measured Unity motion; bone-height proxies are not sole-contact/sliding claims."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics


def vec(sample: dict) -> tuple[float, float, float]:
    return sample["x"], sample["y"], sample["z"]


def span(values: list[float]) -> dict:
    return {"min": min(values), "max": max(values), "range": max(values) - min(values), "mean": statistics.mean(values)}


def qangle(first: dict, last: dict) -> float:
    a, b = [first[key] for key in "xyzw"], [last[key] for key in "xyzw"]
    norm = math.sqrt(sum(v*v for v in a) * sum(v*v for v in b))
    if norm < 1e-12:
        raise ValueError("Degenerate quaternion in motion sample")
    return math.degrees(2 * math.acos(min(1, abs(sum(x*y for x, y in zip(a, b)) / norm))))


def metrics(report: dict) -> dict:
    if report.get("failure") or len(report["frames"]) != report["expectedFrameCount"]:
        raise ValueError("The capture must be complete before summarizing it as a baseline.")
    frames = report["frames"]
    if not all(frame.get("finite") for frame in frames):
        raise ValueError("Non-finite capture frame")
    result = {
        "schema": 1, "stage": report["stage"], "character": report["character"],
        "recipeVersion": report["recipeVersion"], "frameCount": len(frames),
        "frameRate": report["frameRate"], "durationSeconds": len(frames) / report["frameRate"],
        "cautions": [
            "These are measured motion descriptors, not an automatic quality score.",
            "Foot distances refer to ankle/toe bones, not the skinned sole. Low-ankle planar velocity is a descriptive proxy, not verified foot sliding.",
            "Phase rows exclude their first five frames to separate steady motion from transitions; transition root angular deltas are also reported globally.",
            "The camera follows root translation. Camera motion must not be confused with foot/root motion, which is recorded in world coordinates.",
        ],
        "controller": report["animatorController"], "avatar": report["avatar"],
        "sourceMeshes": report["sourceMeshes"], "missingOptionalBones": report["missingOptionalBones"],
        "phases": [],
    }
    phases = list(dict.fromkeys(frame["phase"] for frame in frames))
    for phase in phases:
        all_phase = [frame for frame in frames if frame["phase"] == phase]
        steady = all_phase[5:] if len(all_phase) > 10 else all_phase
        row = {"phase": phase, "frameCount": len(all_phase), "steadyFrameCount": len(steady),
               "actualPlanarSpeedMps": span([frame["measuredSpeed"] for frame in steady]),
               "requestedSpeedMps": span([frame["requestedSpeed"] for frame in steady]),
               "states": sorted(set(frame["gameplayState"] for frame in all_phase)), "bones": {}}
        for slot in ("Hips", "Spine", "Chest", "Head", "LeftHand", "RightHand", "LeftFoot", "RightFoot"):
            samples = [(frame, next((bone for bone in frame["bones"] if bone["slot"] == slot), None)) for frame in steady]
            samples = [(frame, bone) for frame, bone in samples if bone is not None]
            if not samples:
                continue
            first = samples[0][1]
            observation = {"rootLocalXMetres": span([bone["rootLocalPosition"]["x"] for _, bone in samples]),
                           "rootLocalYMetres": span([bone["rootLocalPosition"]["y"] for _, bone in samples]),
                           "rootLocalZMetres": span([bone["rootLocalPosition"]["z"] for _, bone in samples]),
                           "localAngularDisplacementFromFirstDeg": span([qangle(first["localRotation"], bone["localRotation"]) for _, bone in samples])}
            if slot.endswith("Foot"):
                heights = [bone["boneHeightAboveFloor"] for _, bone in samples if bone["floorHit"]]
                if heights:
                    observation["ankleBoneHeightAboveFloorMetres"] = span(heights)
                    limit = sorted(heights)[int((len(heights) - 1) * .3)]
                    low_speeds = []
                    for (previous_frame, previous_bone), (frame, bone) in zip(samples, samples[1:]):
                        if not bone["floorHit"] or bone["boneHeightAboveFloor"] > limit:
                            continue
                        dt = frame["sampleTime"] - previous_frame["sampleTime"]
                        x, _, z = vec(bone["worldPosition"])
                        px, _, pz = vec(previous_bone["worldPosition"])
                        low_speeds.append(math.hypot(x - px, z - pz) / dt)
                    observation["lowest30PercentAnkleHeightThresholdMetres"] = limit
                    observation["lowAnklePlanarSpeedProxyMps"] = span(low_speeds) if low_speeds else None
            row["bones"][slot] = observation
        clips = {}
        for frame in steady:
            for layer in frame["layers"]:
                for clip in layer["currentClips"]:
                    key = layer["layer"] + " / " + clip["name"]
                    clips.setdefault(key, []).append(clip["weight"] * layer["weight"])
        row["observedClips"] = {name: {"observedFrames": len(weights), "meanLayerTimesClipWeight": statistics.mean(weights)} for name, weights in clips.items()}
        result["phases"].append(row)
    deltas = [{"index": frame["index"], "phase": frame["phase"], "degrees": qangle(previous["rootRotation"], frame["rootRotation"])}
              for previous, frame in zip(frames, frames[1:])]
    result["largestRootRotationSteps"] = sorted(deltas, key=lambda row: row["degrees"], reverse=True)[:10]
    result["imageLumaSampleRange"] = span([frame["imageSampleMax"] - frame["imageSampleMin"] for frame in frames])
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("motion_json", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    source = args.motion_json.resolve(strict=True)
    destination = args.output.resolve() if args.output else source.with_name("metrics.json")
    if destination.exists():
        raise FileExistsError("Preserve previous measurements; select a new --output path.")
    raw = source.read_bytes()
    report = metrics(json.loads(raw.decode("utf-8-sig")))
    report["motionJsonSha256"] = hashlib.sha256(raw).hexdigest()
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("ORBIS_MOTION_METRICS", destination)


if __name__ == "__main__":
    main()
