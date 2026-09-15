"""Archive the exact code/shader files referenced by one completed motion capture.
Large versioned FBXs/textures/clips remain at their recorded immutable asset paths.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("capture",type=Path)
args=parser.parse_args()
evidence=json.loads((args.capture/"source_provenance.json").read_text(encoding="utf-8-sig"))
selected=[f for f in evidence["files"] if Path(f["path"]).suffix in {".cs",".asmdef",".hlsl",".shader"}]
output=args.capture/"source_snapshot"
if output.exists(): raise FileExistsError("Preserve previous snapshot: "+str(output))
resolved={}
for f in selected:
    source=Path(f["path"])
    if source.is_absolute() or ".." in source.parts: raise ValueError("Project-relative path required")
    if not source.exists() and source.parts[0]=="Packages":
        # AssetDatabase paths for registry packages are virtual. Resolve the actual
        # installed cache folder, then still require the exact captured hash.
        candidates=[p.joinpath(*source.parts[2:]) for p in Path("Library/PackageCache").glob(source.parts[1]+"@*")]
        candidates=[p for p in candidates if p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest()==f["sha256"]]
        if len(candidates)!=1: raise ValueError("Missing/ambiguous exact package source: "+str(source))
        source=candidates[0]
    if hashlib.sha256(source.read_bytes()).hexdigest()!=f["sha256"]:
        raise ValueError("Source changed since capture; do not mislabel it: "+str(source))
    resolved[f["path"]]=source
output.mkdir()
for f in selected:
    target=output/f["path"]
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(resolved[f["path"]],target)
(output/"snapshot.json").write_text(json.dumps({"source":str(args.capture/"source_provenance.json"),"files":selected},indent=2),encoding="utf-8")
print(output,len(selected))
