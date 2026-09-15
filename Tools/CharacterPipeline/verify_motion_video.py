"""Reopen the encoded MP4 in Blender/FFmpeg and extract sparse actual decoded frames.

blender --background --factory-startup --python-exit-code 1 --python this.py -- --video <motion.mp4>
The originals and video are read-only. Decoded PNGs are evidence, not a replacement for the capture.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import sys
import bpy


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--video", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    video = args.video.resolve(strict=True)
    encoding_path = video.with_suffix(".encoding.json")
    encoding = json.loads(encoding_path.read_text(encoding="utf-8-sig"))
    if digest(video) != encoding["outputSha256"]:
        raise ValueError("Video changed since it was encoded.")
    output = video.parent / (video.stem + "_decoded")
    if output.exists():
        raise FileExistsError("Preserve previous decode evidence; the decode directory already exists.")
    output.mkdir()
    scene = bpy.context.scene
    scene.sequence_editor_clear()
    editor = scene.sequence_editor_create()
    strips = editor.strips if hasattr(editor, "strips") else editor.sequences
    movie = strips.new_movie("Encoded Unity capture", str(video), channel=1, frame_start=1)
    count = int(movie.frame_duration)
    width = movie.elements[0].orig_width
    height = movie.elements[0].orig_height
    if count != encoding["frameCount"] or [width, height] != [encoding["width"], encoding["height"]]:
        raise ValueError(f"Decoded metadata mismatch: frames {count}, dimensions {width}x{height}.")
    movie.blend_type = "REPLACE"
    scene.frame_start, scene.frame_end = 1, count
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.resolution_percentage = 100
    scene.render.pixel_aspect_x = scene.render.pixel_aspect_y = 1
    scene.render.fps, scene.render.fps_base = encoding["frameRate"], 1.0
    scene.render.use_sequencer, scene.render.use_compositing = True, False
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure, scene.view_settings.gamma = 0, 1
    scene.sequencer_colorspace_settings.name = "sRGB"
    if hasattr(scene.render.image_settings, "media_type"):
        scene.render.image_settings.media_type = "IMAGE"
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "8"
    observations = []
    for index in sorted(set([0, 45, 120, 195, 240, 299, count-1])):
        if index >= count:
            continue
        path = output / f"frame_{index:04d}.png"
        scene.frame_set(index + 1)
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        if not path.is_file():
            raise RuntimeError("Missing decoded frame " + str(path))
        observations.append({"zeroBasedFrame": index, "file": path.name, "sha256": digest(path)})
    report = {"schema": 1, "video": str(video), "videoSha256": digest(video),
              "decodedFrameCount": count, "decodedWidth": width, "decodedHeight": height,
              "sourceEncodingManifestSha256": digest(encoding_path), "frames": observations,
              "scope": "Actual MP4 reopened through Blender/FFmpeg. Sparse decoded frames; compare with original JPEGs and visually inspect before claiming video QA complete."}
    (output / "decode.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("ORBIS_MOTION_DECODE_COMPLETE", str(output), flush=True)


if __name__ == "__main__":
    main()
