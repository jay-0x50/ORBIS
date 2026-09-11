"""Checks the delivered Blender source against the immutable pre-face source."""
import bpy, json
from collections import Counter
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
SOURCES=ROOT/'Tools/Blender/Sources'

def snapshot(path,name):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    rig=bpy.data.objects[name+'_Rig']
    bones={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
    body=Counter()
    for obj in bpy.data.objects:
        if obj.type!='MESH' or obj.parent!=rig:continue
        for vertex in obj.data.vertices:
            weights=tuple(sorted((obj.vertex_groups[g.group].name,round(g.weight,7)) for g in vertex.groups))
            if weights==(('Head',1.0),):continue
            body[(obj.name,tuple(vertex.co),weights)]+=1
    images=[{'name':i.name,'path':i.filepath,'packed':bool(i.packed_file),'externalPathExists':Path(bpy.path.abspath(i.filepath)).exists()}
            for i in bpy.data.images if i.source=='FILE' and 'BaseColor' in i.name]
    return bones,body,images

reports=[]
for name in ('Stella','Polaris'):
    old_bones,old_body,_=snapshot(SOURCES/'FaceV1Sources'/(name+'.blend'),name)
    bones,body,images=snapshot(SOURCES/(name+'.blend'),name)
    assert bones==old_bones,'Bone rest transforms changed: '+name
    assert body==old_body,'Body vertices/skin weights changed: '+name
    assert len(bones)==52
    assert len(images)==2 and all(i['packed'] and i['path'].startswith('//') and i['externalPathExists'] for i in images),images
    reports.append({'name':name,'boneRestTransformsUnchanged':True,'boneCount':52,
                    'nonHeadVerticesAndWeightsUnchanged':True,'nonHeadVertexCount':sum(body.values()),
                    'embeddedTextures':images})
out=ROOT/'TestResults/FacePolish-Source-Audit.json'
out.write_text(json.dumps(reports,indent=2),encoding='utf-8')
print('ORBIS_FACE_SOURCE_AUDIT='+json.dumps(reports),flush=True)
