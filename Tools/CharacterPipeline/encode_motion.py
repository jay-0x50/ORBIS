"""Encode actual Unity JPEG frames using Blender's bundled FFmpeg, preserving source bytes.

blender --background --factory-startup --python Tools/CharacterPipeline/encode_motion.py -- \
    --sequence TestResults/CharacterPipeline/Motion/Before/Baseline01/Stella \
    --output TestResults/CharacterPipeline/Motion/Before/Baseline01/Stella/motion.mp4

No 3D render or synthesized frames: VSE is used only as an image-sequence encoder.
The input motion.json defines dimensions/frame count/fps. An existing output is never overwritten.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys
from datetime import datetime, timezone

import bpy


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sequence", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--boss", action="store_true", help="Encode an ActualRuntimePlayback.json Generic-boss capture.")
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(args)


def main() -> None:
    args = parse_args()
    source = args.sequence.resolve(strict=True)
    destination = args.output.resolve()
    if destination.suffix.lower() != ".mp4":
        raise ValueError("Output must use .mp4.")
    manifest_path = destination.with_suffix(".encoding.json")
    if destination.exists() or manifest_path.exists():
        raise FileExistsError("Preserve previous evidence; select a new output path.")
    report_path = source / ("ActualRuntimePlayback.json" if args.boss else "motion.json")
    report = json.loads(report_path.read_text(encoding="utf-8-sig"))
    count = report["framesExpected" if args.boss else "expectedFrameCount"]
    fps = report["frameRate"]
    if args.boss:
        first_image = bpy.data.images.load(str(source / "0000.jpg"), check_existing=False)
        width, height = first_image.size
        bpy.data.images.remove(first_image)
    else:
        width, height = report["width"], report["height"]
    if report.get("failure") or count != len(report.get("frames", [])):
        raise ValueError("Only a completed capture may be encoded as a completed motion clip.")
    frames = [source / (f"{i:04d}.jpg" if args.boss else f"frame_{i:04d}.jpg") for i in range(count)]
    pattern = "[0-9][0-9][0-9][0-9].jpg" if args.boss else "frame_*.jpg"
    if len(list(source.glob(pattern))) != count or not all(path.is_file() for path in frames):
        raise ValueError("JPEG sequence has missing, extra or non-contiguous frames.")
    hashes = [{"file": path.name, "bytes": path.stat().st_size, "sha256": sha256(path)} for path in frames]
    source_report_hash = sha256(report_path)
    destination.parent.mkdir(parents=True, exist_ok=True)

    scene = bpy.context.scene
    scene.sequence_editor_clear()
    editor = scene.sequence_editor_create()
    # Blender 5.x calls this collection strips; Blender 4.x exposed sequences.
    strips = editor.strips if hasattr(editor, "strips") else editor.sequences
    strip = strips.new_image("Actual Unity frames", filepath=str(frames[0]), channel=1, frame_start=1)
    for path in frames[1:]:
        strip.elements.append(path.name)
    strip.frame_final_duration = count
    strip.blend_type = "REPLACE"
    strip.alpha_mode = "STRAIGHT"  # JPEG is opaque; keep the compatible image-strip alpha enum.
    strip.colorspace_settings.name = "sRGB"
    scene.frame_start, scene.frame_end = 1, count
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.resolution_percentage = 100
    scene.render.fps, scene.render.fps_base = fps, 1.0
    scene.render.pixel_aspect_x = scene.render.pixel_aspect_y = 1.0
    scene.render.use_sequencer = True
    scene.render.use_compositing = False
    # Display-referred Unity JPEGs must not receive AgX or a second art-grade.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    scene.sequencer_colorspace_settings.name = "sRGB"
    if hasattr(scene.render.image_settings, "media_type"):
        scene.render.image_settings.media_type = "VIDEO"
    scene.render.image_settings.file_format = "FFMPEG"
    scene.render.ffmpeg.format = "MPEG4"
    scene.render.ffmpeg.codec = "H264"
    scene.render.ffmpeg.audio_codec = "NONE"
    scene.render.ffmpeg.constant_rate_factor = "HIGH"
    scene.render.ffmpeg.ffmpeg_preset = "GOOD"
    scene.render.ffmpeg.gopsize = fps
    scene.render.use_file_extension = True
    scene.render.filepath = str(destination)
    bpy.ops.render.render(animation=True)

    if not destination.is_file() or destination.stat().st_size < 1024:
        raise RuntimeError("Blender did not produce the requested nonempty MP4.")
    if any(sha256(path) != entry["sha256"] for path, entry in zip(frames, hashes)) or sha256(report_path) != source_report_hash:
        raise RuntimeError("Source capture changed while encoding; output is not verified.")
    manifest = {
        "schema": 1,
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "blenderVersion": bpy.app.version_string,
        "encoder": "Blender VSE / bundled FFmpeg H.264 / MPEG4 / quality HIGH",
        "source": str(source), "output": str(destination),
        "frameCount": count, "frameRate": fps, "durationSeconds": count / fps,
        "width": width, "height": height,
        "sourceMotionJsonSha256": source_report_hash,
        "outputSha256": sha256(destination), "outputBytes": destination.stat().st_size,
        "sourceFiles": hashes,
        "sourceFilesUnchanged": True,
        "scope": "Encoding only. Actual Unity frames preserved. No frame interpolation, generated imagery, 3D rerender, crop, overlay, or creative grading.",
        "decodedVideoValidation": "Not yet inspected. Open the resulting clip and/or sample decoded frames separately.",
    }
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("ORBIS_MOTION_ENCODE_COMPLETE", json.dumps({"output": str(destination), "manifest": str(manifest_path)}), flush=True)


if __name__ == "__main__":
    main()
