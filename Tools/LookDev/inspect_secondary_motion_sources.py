"""Read-only connected-component inventory; does not build motion assets or save models.

Blender --background --python this_file.py
Reports source .blend and canonical Unity FBX topology/UV/weight signatures.
"""
import bpy, json, hashlib, math
from pathlib import Path
from collections import defaultdict, Counter
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
RESULT=ROOT/'TestResults/LookDev/Step04_MotionMaskInspection.json'

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def components(mesh):
    links=[[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a,b=edge.vertices;links[a].append(b);links[b].append(a)
    seen=set()
    for start in range(len(links)):
        if start in seen:continue
        stack=[start];seen.add(start);part=[]
        while stack:
            i=stack.pop();part.append(i)
            for k in links[i]:
                if k not in seen:seen.add(k);stack.append(k)
        yield sorted(part)

def inventory(name,path):
    if path.suffix=='.blend':bpy.ops.wm.open_mainfile(filepath=str(path))
    else:
        bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
        bpy.ops.import_scene.fbx(filepath=str(path))
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE' and 'Head' in o.data.bones)
    head=rig.data.bones['Head'];hp=rig.matrix_world@head.head_local
    hips=rig.matrix_world@rig.data.bones['Hips'].head_local
    # FBX does not preserve leaf-bone tail length. Use two authored rest joint
    # positions (.935 Hips / 1.50 Head), whose distance is preserved by import.
    scale=(hp-hips).length/(1.50-.935);offset=hp.z-1.50*scale
    meshes=[]
    roles=('_EX_Hair','_Tailored_EX_TailoredNavy','_Tailored_EX_TailoredIvory','_Tailored_EX_Gold')
    for obj in bpy.context.scene.objects:
        if obj.type!='MESH' or not obj.name.endswith(roles):continue
        mesh=obj.data
        uv=[set() for _ in mesh.vertices]
        if mesh.uv_layers:
            for loop,value in zip(mesh.loops,mesh.uv_layers.active.data):uv[loop.vertex_index].add(tuple(value.uv))
        poses=[];weights=[]
        for v in mesh.vertices:
            p=obj.matrix_world@v.co;poses.append((p.x/scale,p.y/scale,(p.z-offset)/scale))
            weights.append({obj.vertex_groups[g.group].name:float(g.weight) for g in v.groups if g.weight>.00001})
        vertex_faces=[[] for _ in mesh.vertices]
        for polygon in mesh.polygons:
            for i in polygon.vertices:vertex_faces[i].append(polygon.index)
        parts=[]
        for index,indices in enumerate(components(mesh)):
            points=[poses[i] for i in indices];faces={f for i in indices for f in vertex_faces[i]}
            bound=[[min(p[k] for p in points) for k in range(3)],[max(p[k] for p in points) for k in range(3)]]
            uvvalues=[p for i in indices for p in uv[i]]
            uvbound=[[min(p[k] for p in uvvalues) for k in range(2)],[max(p[k] for p in uvvalues) for k in range(2)]] if uvvalues else None
            bones=sorted({bone for i in indices for bone in weights[i]})
            fingerprint=[]
            for i in indices:
                fingerprint.append((tuple(round(v,5) for v in poses[i]),tuple(sorted(tuple(round(x,5) for x in u) for u in uv[i])),
                                    tuple(sorted((b,round(w,5)) for b,w in weights[i].items()))))
            fingerprint.sort()
            parts.append({'component':index,'firstVertex':indices[0],'lastVertex':indices[-1],
                'vertices':len(indices),'triangles':sum(len(mesh.polygons[f].vertices)-2 for f in faces),
                'boundsAuthor':bound,'uvBounds':uvbound,'bones':bones,
                'maxUVsPerVertex':max(len(uv[i]) for i in indices),
                'componentPoseUVWeightSHA5dp':hashlib.sha256(json.dumps(fingerprint,separators=(',',':')).encode()).hexdigest()})
        meshes.append({'name':obj.name,'vertices':len(mesh.vertices),'components':len(parts),
                       'materials':[m.name for m in mesh.materials],'parts':parts})
    return {'path':str(path.relative_to(ROOT)).replace('\\','/'),'sha256':sha(path),'boneCount':len(rig.data.bones),
            'authorScale':scale,'authorZOffset':offset,'meshes':meshes}

paths={name:{'blend':ROOT/f'Tools/Blender/Sources/{name}.blend',
             'fbx':ROOT/f'Assets/Orbis/Game/Island/Models/{name}.fbx'} for name in ('Stella','Polaris')}
before={str(p.relative_to(ROOT)):sha(p) for ps in paths.values() for p in ps.values()}
data={name:{kind:inventory(name,path) for kind,path in kinds.items()} for name,kinds in paths.items()}
after={str(p.relative_to(ROOT)):sha(p) for ps in paths.values() for p in ps.values()}
assert before==after
summary=[]
for name,inputs in data.items():
    for kind,item in inputs.items():
        for mesh in item['meshes']:
            hair=mesh['name'].endswith('_EX_Hair')
            picks=[p for p in mesh['parts'] if (hair and 14<=p['component']<=22) or
                   (not hair and p['boundsAuthor'][0][1]>.09 and p['boundsAuthor'][0][2]<.55 and p['boundsAuthor'][1][2]>1.34)]
            for p in picks:summary.append({'character':name,'input':kind,'mesh':mesh['name'],**p})
output={'readOnlyHashesUnchanged':True,'inputs':data,'candidates':summary,
 'provenance':'Hair components 14..22 are 6 main bangs plus 3 fine fringe locks generated after 14 crown/back locks. Long cape candidates must be verified against cape() component topology counts, not selected by bounds alone.',
 'notes':['Unity may reorder or split imported vertices. Do not copy source vertex indices into a Unity mask.',
          'Canonical FBX is inspected via Blender import. Real Unity Mesh vertex ordering must be bound in the Unity editor against position/UV0/bone weight signatures.']}
RESULT.parent.mkdir(parents=True,exist_ok=True);RESULT.write_text(json.dumps(output,indent=2),encoding='utf-8')
print('ORBIS_MOTION_MASK_CANDIDATES='+json.dumps(summary),flush=True)
