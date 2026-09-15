"""Compare sparse MP4-decoded PNGs against the original Unity JPEGs (Pillow + NumPy)."""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--boss", action="store_true")
    args = parser.parse_args()
    capture = args.capture.resolve(strict=True)
    decoded = capture / ("runtime_decoded" if args.boss else "motion_decoded")
    source = json.loads((capture / ("ActualRuntimePlayback.json" if args.boss else "motion.json")).read_text(encoding="utf-8-sig"))
    decode = json.loads((decoded / "decode.json").read_text(encoding="utf-8-sig"))
    output = capture / "video_qa.json"
    if output.exists():
        raise FileExistsError("Previous video QA must be preserved.")
    rows = []
    for item in decode["frames"]:
        index = item["zeroBasedFrame"]
        original_path = capture / (f"{index:04d}.jpg" if args.boss else f"frame_{index:04d}.jpg")
        decoded_path = decoded / item["file"]
        original = np.asarray(Image.open(original_path).convert("RGB"), dtype=np.float32)
        roundtrip = np.asarray(Image.open(decoded_path).convert("RGB"), dtype=np.float32)
        if original.shape != roundtrip.shape:
            raise ValueError("Decoded dimensions differ from capture dimensions.")
        error = np.abs(original - roundtrip)
        rows.append({"zeroBasedFrame": index, "phase": source["frames"][index]["state" if args.boss else "phase"],
                     "sourceSha256": digest(original_path), "decodedSha256": digest(decoded_path),
                     "meanAbsoluteRgbError8Bit": float(error.mean()), "p99AbsoluteRgbError8Bit": float(np.percentile(error, 99))})
    passed = all(item["meanAbsoluteRgbError8Bit"] < 5 and item["p99AbsoluteRgbError8Bit"] < 35 for item in rows)
    result = {"schema": 1, "character": capture.name if args.boss else source["character"], "stage": "Boss runtime" if args.boss else source["stage"],
              "videoSha256": digest(capture / ("runtime.mp4" if args.boss else "motion.mp4")), "captureFrames": len(source["frames"]),
              "decodedFrameCount": decode["decodedFrameCount"], "decodedWidth": decode["decodedWidth"], "decodedHeight": decode["decodedHeight"],
              "samples": rows, "roundTripWithinTolerance": passed,
              "tolerance": "Per sample: mean absolute RGB error <5/255 and p99 <35/255. Detects unintended grading, flipped order or framing; allows lossy H.264 differences.",
              "scope": "Six sparse decoded frames checked numerically. This does not prove natural motion or skinned sole contact."}
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if not passed:
        raise ValueError("Decoded pixels differ too far from the recorded frames; inspect video_qa.json.")
    print("ORBIS_MOTION_VIDEO_QA", result["character"], "PASSED", str(output))


if __name__ == "__main__":
    main()
