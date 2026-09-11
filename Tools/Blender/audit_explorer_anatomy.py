"""Read-only audit of the rebuilt explorer sources against the immutable rigs.

Blender --background --python-exit-code 1 --python this_file.py
Unlike the old face audit, body geometry is expected to have changed. This checks
the rest skeleton, usable skinning/UVs and portable, current embedded textures.
No blend, FBX, PNG, material, mesh, or rig is saved or edited by this script.
"""
import bpy, hashlib, json, math
from pathlib import Path
from mathutils import Vector
from datetime import datetime, timezone

ROOT=Path(__file__).resolve().parents[2]
SOURCES=ROOT/'Tools/Blender/Sources'
BASE=SOURCES/'AnatomyBaseSources'
TEXTURES=ROOT/'Assets/Orbis/Game/Island/Textures/Explorers'
OUT=ROOT/'TestResults/Anatomy-Source-Audit.json'
ROLES=('EX_Face','EX_Hair','EX_TailoredIvory','EX_TailoredNavy')

def sha(path):
    value=hashlib.sha256()
    with path.open('rb') as stream:
        for data in iter(lambda:stream.read(1024*1024),b''):value.update(data)
    return value.hexdigest()

def relative(path):return path.relative_to(ROOT).as_posix()

def skeleton(rig):
    return {b.name:{'parent':b.parent.name if b.parent else None,
                    'deform':bool(b.use_deform),
                    'matrix':[list(row) for row in b.matrix_local],
                    'head':list(b.head_local),'tail':list(b.tail_local)}
            for b in rig.data.bones}

def below_rig(obj,rig):
    parent=obj.parent
    while parent:
        if parent==rig:return True
        parent=parent.parent
    return False

def audit_neck_outward_normals(name,rig,meshes):
    """Audit actual polygon winding on the exposed side of the closed neck tube.

    End caps are outside this Z interval. The gently curved tube centre moves
    from Y=0 at authoring Z=1.450 to Y=.010 at Z=1.486. This independent radial
    test catches an inside-out neck even when a double-sided outline hides it.
    """
    head=rig.data.bones['Head'];scale=head.length/.204;offset=head.head_local.z-1.50*scale
    samples=[];front=back=0
    for obj in meshes:
        if not obj.name.endswith('Tailored_EX_Skin'):continue
        to_rig=rig.matrix_world.inverted()@obj.matrix_world
        normal_matrix=to_rig.to_3x3().inverted().transposed()
        for polygon in obj.data.polygons:
            point=to_rig@polygon.center
            x=point.x/scale;y=point.y/scale;z=(point.z-offset)/scale
            if not (1.454<=z<=1.480 and abs(x)<.055 and abs(y)<.065):continue
            centre_y=.010*max(0,min(1,(z-1.450)/.036))
            radial=Vector((x,y-centre_y,0))
            assert radial.length>1e-5,'Degenerate neck radial face: '+obj.name
            normal=(normal_matrix@polygon.normal).normalized()
            assert all(math.isfinite(value) for value in normal),'Non-finite neck normal: '+obj.name
            dot=normal.dot(radial.normalized())
            samples.append(dot)
            if y<centre_y:front+=1
            else:back+=1
            assert dot>0,'Inward neck winding/normal: '+obj.name+' polygon '+str(polygon.index)+' dot '+str(dot)
    assert len(samples)>=16 and front>0 and back>0,'Missing neck side-face coverage: '+name
    return {'passed':True,'authoringZRange':[1.454,1.480],'checkedSideFaces':len(samples),
            'frontFaces':front,'backFaces':back,'minimumNormalDotOutwardRadial':min(samples),
            'meanNormalDotOutwardRadial':sum(samples)/len(samples),'allNormalsPointOutward':True}
def audit_model(name):
    bpy.ops.wm.open_mainfile(filepath=str(BASE/(name+'.blend')))
    baseline_rig=bpy.data.objects.get(name+'_Rig')
    assert baseline_rig and baseline_rig.type=='ARMATURE','Missing baseline rig: '+name
    original=skeleton(baseline_rig)
    bpy.ops.wm.open_mainfile(filepath=str(SOURCES/(name+'.blend')))
    rig=bpy.data.objects.get(name+'_Rig')
    assert rig and rig.type=='ARMATURE','Missing rebuilt rig: '+name
    current=skeleton(rig)
    assert len(original)==52 and len(current)==52,'Expected 52 bones: '+name
    assert original==current,'Bone rest matrix/hierarchy/deform flags changed: '+name
    meshes=[o for o in bpy.data.objects if o.type=='MESH' and below_rig(o,rig)]
    assert meshes,'No character meshes: '+name
    neck_report=audit_neck_outward_normals(name,rig,meshes)
    vertex_count=triangles=weights_checked=0;max_weight_error=0
    low=[math.inf]*3;high=[-math.inf]*3
    uv_by_role={role:[] for role in ROLES};role_meshes={role:set() for role in ROLES}
    for obj in meshes:
        armatures=[m for m in obj.modifiers if m.type=='ARMATURE']
        assert armatures and all(m.object==rig for m in armatures),'Invalid skin modifier: '+obj.name
        vertex_count+=len(obj.data.vertices)
        triangles+=sum(len(p.vertices)-2 for p in obj.data.polygons)
        for v in obj.data.vertices:
            assert all(math.isfinite(x) for x in v.co),'Non-finite vertex: '+obj.name
            assert v.groups,'Unweighted vertex: '+obj.name
            total=0
            for group in v.groups:
                assert 0<=group.group<len(obj.vertex_groups),'Invalid group index: '+obj.name
                bone_name=obj.vertex_groups[group.group].name
                assert bone_name in current,'Group refers to missing bone: '+bone_name
                assert math.isfinite(group.weight) and 0<=group.weight<=1.00001,'Invalid weight: '+obj.name
                total+=group.weight;weights_checked+=1
            error=abs(total-1);max_weight_error=max(max_weight_error,error)
            assert error<.0001,'Weights do not sum to one: '+obj.name
            p=obj.matrix_world@v.co
            assert all(math.isfinite(x) for x in p),'Non-finite world vertex: '+obj.name
            for i in range(3):low[i]=min(low[i],p[i]);high[i]=max(high[i],p[i])
        for polygon in obj.data.polygons:
            assert 0<=polygon.material_index<len(obj.data.materials),'Missing material slot: '+obj.name
            mat=obj.data.materials[polygon.material_index]
            if mat and mat.name in ROLES:
                role=mat.name;uv=obj.data.uv_layers.active
                assert uv and len(uv.data)==len(obj.data.loops),'Missing/incomplete UV0: '+obj.name
                role_meshes[role].add(obj.name)
                for li in polygon.loop_indices:
                    value=tuple(uv.data[li].uv)
                    assert all(math.isfinite(x) for x in value),'Non-finite UV0: '+obj.name
                    uv_by_role[role].append(value)
    uv_report=[]
    for role,values in uv_by_role.items():
        assert values,'No assigned polygons using role '+role+' in '+name
        minimum=[min(v[i] for v in values) for i in (0,1)]
        maximum=[max(v[i] for v in values) for i in (0,1)]
        assert all(maximum[i]-minimum[i]>.0001 for i in (0,1)),'Degenerate role UV0: '+role
        uv_report.append({'role':role,'meshes':sorted(role_meshes[role]),'loopCount':len(values),
                          'finite':True,'min':minimum,'max':maximum})
    expected={name+'_Face_BaseColor.png','AshBlond_Hair_BaseColor.png',
              'Body_Ivory_BaseColor.png','Body_Navy_BaseColor.png'}
    images={Path(i.filepath.replace('\\','/')).name:i for i in bpy.data.images
            if i.source=='FILE' and Path(i.filepath.replace('\\','/')).name in expected}
    assert set(images)==expected,'Missing source texture: '+name
    image_report=[]
    for filename in sorted(expected):
        image=images[filename];path=Path(bpy.path.abspath(image.filepath)).resolve()
        expected_path=(TEXTURES/filename).resolve()
        assert image.packed_file,'Unpacked texture: '+filename
        assert image.filepath.startswith('//'),'Absolute texture path: '+filename
        assert path==expected_path and path.is_file(),'Broken/incorrect relative texture path: '+filename
        assert all(s>0 for s in image.size),'Empty texture dimensions: '+filename
        packed_sha=hashlib.sha256(image.packed_file.data).hexdigest();external_sha=sha(expected_path)
        assert packed_sha==external_sha,'Embedded PNG differs from delivered PNG: '+filename
        image_report.append({'name':filename,'relativePath':image.filepath,'packed':True,
                             'externalPathExists':True,'width':image.size[0],'height':image.size[1],
                             'packedMatchesExternalPng':True,'sha256':external_sha})
    height=high[2]-low[2]
    assert 1<height<2.5 and triangles>0,'Implausible source dimensions: '+name
    return {'name':name,'baseline':relative(BASE/(name+'.blend')),'source':relative(SOURCES/(name+'.blend')),
            'boneCount':52,'boneNamesRestMatricesHierarchyPreserved':True,
            'allWeightsFiniteNormalizedAndReferenceValidBones':True,'weightEntriesChecked':weights_checked,
            'maxWeightSumError':max_weight_error,'meshCount':len(meshes),'vertices':vertex_count,
            'triangles':triangles,'sourceHeightMetres':height,'boundsMin':low,'boundsMax':high,
            'texturedRoleUvs':uv_report,'embeddedPngs':image_report,'neckOutwardNormals':neck_report}

def main():
    watched=set()
    for name in ('Stella','Polaris'):
        watched.update([BASE/(name+'.blend'),SOURCES/(name+'.blend'),ROOT/'Tools/Blender/AnatomyExports'/(name+'.fbx'),
                        TEXTURES/(name+'_Face_BaseColor.png')])
    watched.update(TEXTURES/name for name in ('AshBlond_Hair_BaseColor.png','Body_Ivory_BaseColor.png','Body_Navy_BaseColor.png'))
    watched=sorted(watched,key=str)
    before={path:sha(path) for path in watched}
    reports=[audit_model(name) for name in ('Stella','Polaris')]
    after={path:sha(path) for path in watched}
    assert before==after,'A source/model/texture file changed during the read-only audit.'
    result={'passed':True,'completedUtc':datetime.now(timezone.utc).isoformat(),'readOnly':True,
            'sourceModelAndTextureFilesUnchanged':True,'models':reports,
            'unchangedFiles':[{'path':relative(path),'sha256Before':before[path],'sha256After':after[path]} for path in watched]}
    OUT.write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('ORBIS_ANATOMY_AUDIT='+json.dumps({'passed':True,'models':[{k:r[k] for k in ('name','boneCount','vertices','triangles','sourceHeightMetres')} for r in reports],
                                         'unchangedFiles':len(watched)}),flush=True)

try:main()
except Exception as error:
    OUT.write_text(json.dumps({'passed':False,'error':str(error)},indent=2),encoding='utf-8')
    raise
