"""Plan the Field-only delivery. Writes staging reports; never modifies the target project."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[2]
TARGET = Path("D:/Project/ORBIS")
BASELINE = "TestResults/WorldDev/Field_TargetBaseline.json"
MANIFEST = "Tools/Field/Field_DeliveryManifest.json"
REPORT = "Tools/Field/Field_DeliveryReport.json"
FILES_DOC = "Docs/Field_FILES.md"

# Existing files expected to change in this task. Additional tracked changes require an
# explicit --allow-existing argument and are displayed in the report for review.
EXISTING = {
    "Assets/Orbis/Game/Editor/IslandSceneBuilder.cs",
    "Assets/Orbis/Game/Editor/OpenWorldSceneBuilder.cs",
    "Assets/Orbis/Game/Editor/Orbis.Game.Editor.asmdef",
    "Assets/Orbis/Game/Editor/WorldArtBuilder.cs",
    "Assets/Orbis/Game/Editor/WorldLandmarkBuilder.cs",
    "Assets/Orbis/Game/Editor/WorldNatureBuilder.cs",
    "Assets/Orbis/Game/Editor/WorldPerformanceBuilder.cs",
    "Assets/Orbis/Game/Editor/WorldStreamingBuilder.cs",
    "Assets/Orbis/Game/Editor/WorldStyleBuilder.cs",
    "Assets/Orbis/Game/Editor/WorldWeatherBuilder.cs",
    "Assets/Orbis/Game/Editor/WorldWorkspaceMenu.cs",
    "Assets/Orbis/Game/Runtime/World/WorldGrassField.cs",
    "Assets/Orbis/Game/Scenes/Orbis_Island.unity",
    "Assets/Orbis/Game/Scenes/Orbis_Island/OcclusionCullingData.asset",
    "Assets/Orbis/Game/World/Resources/World/StreamCatalog.asset",
    "Assets/Orbis/M3/Editor/M3ShaderBuilder.cs",
    "Assets/Orbis/M4/Tests/EditMode/M4ContentTests.cs",
    "Assets/Orbis/M4/Tests/EditMode/Orbis.M4.EditModeTests.asmdef",
    "ProjectSettings/EditorBuildSettings.asset", "README.md",
}
for module in ("M0", "M1", "M2", "M3", "M4", "M15", "M16"):
    setup = "ExplorerProjectSetup.cs" if module == "M16" else module + "ProjectSetup.cs"
    EXISTING.update({f"Assets/Orbis/{module}/Editor/{setup}",
                     f"Assets/Orbis/{module}/Editor/Orbis.{module}.Editor.asmdef"})
for region in ("Agnia", "Teluna", "Zephyr", "Granite", "Voltheim"):
    EXISTING.add(f"Assets/Orbis/Game/World/Scenes/Environment_{region}.unity")

NEW_GLOBS = (
    "Assets/Orbis/EditorSupport.meta", "Assets/Orbis/EditorSupport/**/*",
    "Assets/Scenes.meta", "Assets/Scenes/Field.unity", "Assets/Scenes/Field.unity.meta",
    "Assets/Orbis/Game/World/Authoring.meta", "Assets/Orbis/Game/World/Authoring/**/*",
    "Assets/Orbis/Game/Editor/Field*.cs", "Assets/Orbis/Game/Editor/Field*.cs.meta",
    "Assets/Orbis/Game/Runtime/World/Field*.cs", "Assets/Orbis/Game/Runtime/World/Field*.cs.meta",
    "Assets/Orbis/Game/Tests/**/Field*.cs", "Assets/Orbis/Game/Tests/**/Field*.cs.meta",
    "Assets/Orbis/Game/Tests/**/Orbis.Game.Field*.asmdef", "Assets/Orbis/Game/Tests/**/Orbis.Game.Field*.asmdef.meta",
    "Assets/Orbis/Game/Tests/Field*.meta",
    "Assets/Orbis/M4/Tests/EditMode/Field*.cs", "Assets/Orbis/M4/Tests/EditMode/Field*.cs.meta",
)
TOOLS = ("Tools/Field/GenerateDeliveryManifest.py", "Tools/Field/ApplyDelivery.ps1", "Tools/Field/README.md")

def relative(value: str) -> str:
    path = PurePosixPath(value)
    if not value or value != path.as_posix() or path.is_absolute() or ".." in path.parts or ":" in value or "\\" in value:
        raise ValueError(f"Not a canonical relative path: {value}")
    lower = value.lower()
    if lower.startswith(("assets/img/", "assets/blend/", "assets/importedassets/")) or lower.endswith((".blend", ".blend1")):
        raise ValueError(f"Protected user/imported asset cannot be delivered: {value}")
    if lower.startswith(("library/", "temp/", "logs/", "usersettings/", ".git/")):
        raise ValueError(f"Non-deliverable workspace path: {value}")
    return value

def sha(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

def write_json(path: str, data: dict) -> None:
    destination = ROOT / path
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def entry(path: str, baseline: dict[str, str]) -> dict:
    relative(path)
    source = ROOT / path
    if not source.is_file() or source.is_symlink():
        raise ValueError(f"Missing or linked staging file: {path}")
    source.resolve().relative_to(ROOT)
    target = TARGET / path
    return {"path": path, "kind": "changed" if path in baseline else "new",
            "baselineSha256": baseline.get(path), "sha256": sha(source), "bytes": source.stat().st_size,
            "targetSha256AtPlan": sha(target) if target.is_file() else None}

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", action="append", default=[], metavar="RELATIVE_PATH",
                        help="Explicit final Field PNG/JSON/XML proof file; repeat for each reviewed result.")
    parser.add_argument("--allow-existing", action="append", default=[], metavar="RELATIVE_PATH",
                        help="Explicitly include one additional changed baseline file after inspecting it.")
    args = parser.parse_args()
    baseline = {key: value.lower() for key, value in json.loads((ROOT / BASELINE).read_text(encoding="utf-8-sig")).items()}
    additional = {relative(path) for path in args.allow_existing}
    if additional - baseline.keys():
        raise ValueError("--allow-existing paths must be recorded in the task baseline.")
    changed, missing = [], []
    for path, previous_hash in sorted(baseline.items()):
        source = ROOT / path
        if not source.is_file():
            missing.append(path)
        elif sha(source) != previous_hash:
            changed.append(path)
    blocked = sorted(set(changed) - EXISTING - additional)
    if blocked or missing:
        write_json(REPORT, {"ready": False, "blockedChangedBaselineFiles": blocked,
                            "missingBaselineFiles": missing, "changedBaselineFiles": changed})
        raise SystemExit("Delivery blocked: inspect Tools/Field/Field_DeliveryReport.json. No manifest was generated.")
    selected = set(changed)
    for pattern in NEW_GLOBS:
        for path in ROOT.glob(pattern):
            if not path.is_file():
                continue
            name = path.relative_to(ROOT).as_posix()
            if name in baseline:
                continue
            # Export/migration snapshots are never deliverable, even if an interrupted run leaves one.
            if any(part.startswith("_") for part in PurePosixPath(name).parts) or "working" in path.name.lower():
                raise ValueError(f"Resolve the leftover authoring working file before delivery: {name}")
            selected.add(relative(name))
    for name in args.evidence:
        name = relative(name)
        path = PurePosixPath(name)
        if path.parent.as_posix() != "TestResults/WorldDev" or not path.name.startswith("Field_") or \
                path.suffix.lower() not in (".png", ".json", ".xml") or "baseline" in path.name.lower():
            raise ValueError(f"Not a final Field proof file: {name}")
        selected.add(name)
    if not args.evidence:
        raise ValueError("Select final evidence explicitly with --evidence; logs and baselines are not deliverables.")
    selected.update(TOOLS)
    selected.update({"Docs/Field_Workflow.md", FILES_DOC})
    for required in ("Assets/Scenes/Field.unity", "Assets/Scenes/Field.unity.meta", "Docs/Field_Workflow.md"):
        if not (ROOT / required).is_file():
            raise ValueError(f"Required Field deliverable is missing: {required}")
    # Report files are generated before hashing; the manifest itself is separately pinned by its digest.
    listing = sorted(selected | {REPORT, MANIFEST})
    lines = ["# Field 변경·생성 파일", "", "일상 편집 씬: `Assets/Scenes/Field.unity`.", "",
             "기존 파일은 작업 시작 시점 해시와 일치할 때만 교체합니다. 원본 백업 후 전체 해시를 검증합니다.", "",
             "`Assets/Img`, `Assets/blend`, 외부 애셋, Library, 로그, 기준 해시 파일은 복사하지 않습니다.", "",
             "| 구분 | 파일 |", "|---|---|"]
    for path in listing:
        lines.append(f"| {'변경' if path in baseline else '생성'} | `{path}` |")
    (ROOT / FILES_DOC).write_text("\n".join(lines) + "\n", encoding="utf-8")
    records = [entry(path, baseline) for path in sorted(selected)]
    report = {"ready": True, "baselineFileCount": len(baseline), "changedBaselineFiles": changed,
              "additionalReviewedExistingFiles": sorted(additional), "missingBaselineFiles": [],
              "blockedChangedBaselineFiles": [], "evidence": args.evidence,
              "field_report": {"selectedFilesBeforeReport": len(records),
                               "selectedBytesBeforeReport": sum(item["bytes"] for item in records)},
              "files": records}
    write_json(REPORT, report)
    records.append(entry(REPORT, baseline))
    manifest = {"schemaVersion": 1, "task": "Field authoring workflow", "sourceRoot": str(ROOT),
                "targetRoot": str(TARGET), "manifestRelativePath": MANIFEST,
                "baselineSha256": sha(ROOT / BASELINE), "additionalReviewedExistingFiles": sorted(additional),
                "field_report": {"fileCount": len(records), "bytes": sum(item["bytes"] for item in records),
                                 "changedCount": sum(item["kind"] == "changed" for item in records),
                                 "newCount": sum(item["kind"] == "new" for item in records)},
                "files": sorted(records, key=lambda item: item["path"])}
    write_json(MANIFEST, manifest)
    print(json.dumps({"manifest": MANIFEST, "manifestSha256": sha(ROOT / MANIFEST),
                      "field_report": manifest["field_report"], "targetModified": False}, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
