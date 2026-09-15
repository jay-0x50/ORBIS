"""Read-only actual weapon-triangle contact diagnostics for Stella Grip01/02."""
from pathlib import Path
import argparse,hashlib,json,sys
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from review_hero_grip import posed_world,hand_mask
ROOT=Path(__file__).resolve().parents[2]/'TestResults/CharacterPipeline/RigReview'
parser=argparse.ArgumentParser();parser.add_argument('--candidate',default='StellaGrip02');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
assert args.candidate.isalnum() and args.candidate.startswith('StellaGrip')
reports=[]
for run,file in [('StellaGrip01','Stella_grip_review.blend'),(args.candidate,'Stella_grip_refined.blend')]:
    source=ROOT/run/file;before=hashlib.sha256(source.read_bytes()).hexdigest();bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    weapon=bpy.data.objects['Wayfarer_EXISTING_MESH_grip_review'];body=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o!=weapon)
    mask=hand_mask(body);body.data.calc_loop_triangles();bt=np.asarray([t.vertices[:] for t in body.data.loop_triangles]);bt=bt[mask[bt].all(1)]
    world=posed_world(body);inverse=np.asarray(weapon.matrix_world.inverted());local=world@inverse[:3,:3].T+inverse[:3,3];v=local[mask];t=local[bt]
    samples=np.concatenate([v,t.mean(1),(t[:,0]+t[:,1])*.5,(t[:,1]+t[:,2])*.5,(t[:,2]+t[:,0])*.5])
    m=weapon.data;m.calc_loop_triangles();co=np.asarray([v.co[:] for v in m.vertices]);tri=np.asarray([t.vertices[:] for t in m.loop_triangles]);tree=BVHTree.FromPolygons(co.tolist(),tri.tolist(),all_triangles=True)
    # Measure actual nearest triangles only in the handle's axial interval.
    # A local signed-nearest value is an orientation diagnostic; overlapping
    # ornamental sheets mean it is not a closed-volume collision certificate.
    axial=(samples[:,2]>-.068)&(samples[:,2]<.063);selected=samples[axial]
    signed=[];faces=[]
    for p in selected:
        q,n,fi,d=tree.find_nearest(Vector(p));signed.append(float((Vector(p)-q).dot(n)));faces.append(int(fi))
    signed=np.asarray(signed);worst=np.argsort(signed)[:12]
    hand_edges=np.unique(np.sort(np.vstack([bt[:,[0,1]],bt[:,[1,2]],bt[:,[2,0]]]),axis=1),axis=0)
    crossings=[]
    for a,b in hand_edges:
        start,end=local[a],local[b];delta=end-start;length=float(np.linalg.norm(delta))
        if length<.00001:continue
        direction=delta/length;hit=tree.ray_cast(Vector(start+direction*.000005),Vector(direction),length-.00001)
        if hit[0] is not None and -.079<float(hit[0][2])<.079:
            crossings.append({'bodyEdge':[int(a),int(b)],'weaponTriangle':int(hit[2]),'hitWeaponLocal':list(hit[0])})
    reports.append({'source':str(source),'sourceSha256Unchanged':before,'actualWeaponTriangles':len(tri),'actualHandTriangles':len(bt),'handleRegionSamples':len(selected),
        'signedNearestSamplesBelowMinus2mm':int((signed<-.002).sum()),'signedNearestMinimum':float(signed.min()),
        'actualHandEdgesCrossingWeaponTrianglesInHandleInterval':len(crossings),'edgeCrossingDetails':crossings,
        'worst':[{'pointWeaponLocal':selected[i].tolist(),'signedNearest':float(signed[i]),'weaponTriangle':faces[i],'weaponTrianglePoints':co[tri[faces[i]]].tolist()} for i in worst],
        'method':'Actual posed hand vertices plus actual hand-triangle centroid and three edge-midpoint samples to actual complete weapon triangle BVH. Signed local normal distance is diagnostic, not watertight collision proof.'})
    assert before==hashlib.sha256(source.read_bytes()).hexdigest()
(ROOT/args.candidate/'ActualSurfaceDiagnostic.json').write_text(json.dumps(reports,indent=2),encoding='utf-8')
print('STELLA_ACTUAL_GRIP_SURFACE '+json.dumps([{k:r[k] for k in ['actualHandTriangles','handleRegionSamples','signedNearestSamplesBelowMinus2mm','signedNearestMinimum']} for r in reports]),flush=True)
