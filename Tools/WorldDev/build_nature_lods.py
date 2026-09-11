"""Derive low-cost CC0 vegetation without changing the downloaded source files.

Blender CLI: blender -b --python Tools/WorldDev/build_nature_lods.py
UV0 preserves repeating source UVs; UV1=(array slice, wind weight),
UV2=(leaf flutter mask, normalized height). FBX has one mesh/material per LOD.
Billboards are eight actual orthographic, unlit base-color renders, not artwork.
"""
from pathlib import Path
import hashlib, json, math, re, sys, zipfile
import bpy, bmesh
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
DOWNLOADS=ROOT/'Tools/WorldDev/SourceDownloads'
MEGA=DOWNLOADS/'Quaternius_MegaKit_Standard'
ULTIMATE=DOWNLOADS/'Quaternius_UltimateStylizedNature_Selection'
ARCHIVE=MEGA/'Stylized Nature MegaKit[Standard].zip'
CACHE=ROOT/'Tools/WorldDev/BuildCache/Nature'
DEST=ROOT/'Assets/Orbis/Game/World/Nature'
MODELS=DEST/'Models';TEXTURES=DEST/'Textures'
EVIDENCE=ROOT/'TestResults/WorldDev'
CELL=256
# Six bark images, six tree leaf images, and three small ground-cover images.
TEXTURE_SPECS=[
 ('Bark_NormalTree','mega','Bark_NormalTree.png',False),
 ('Bark_DeadTree','mega','Bark_DeadTree.png',False),
 ('Bark_TwistedTree','mega','Bark_TwistedTree.png',False),
 ('BirchTree_Bark','ultimate','BirchTree_Bark.png',False),
 ('MapleTree_Bark','ultimate','MapleTree_Bark.png',False),
 ('PalmTree_Trunk','ultimate','PalmTree_Trunk.png',False),
 ('Leaves_NormalTree','mega','Leaves_NormalTree_C.png',True),
 ('Leaves_Pine','mega','Leaf_Pine_C.png',True),
 ('Leaves_TwistedTree','mega','Leaves_TwistedTree_C.png',True),
 ('BirchTree_Leaves','ultimate','BirchTree_Leaves.png',True),
 ('MapleTree_Leaves','ultimate','MapleTree_Leaves.png',True),
 ('PalmTree_Leaves','ultimate','PalmTree_Leaves.png',True),
 ('Grass','mega','Grass.png',True),
 ('Leaves','mega','Leaves.png',True),
 ('Rocks','mega','Rocks_Diffuse.png',False),
]
TREES=[('CommonTree_3','mega',1100),('Pine_5','mega',360),('DeadTree_5','mega',1800),
       ('TwistedTree_2','mega',2200),('BirchTree_1','ultimate',750),
       ('MapleTree_1','ultimate',850),('PalmTree_1','ultimate',300)]
EXTRAS=[('Grass_Common_Short','mega',99999),('Fern_1','mega',99999),
        ('Rock_Medium_1','mega',99999),('Rock_Medium_2','mega',99999),('Rock_Medium_3','mega',99999)]
MATERIAL_SLICES={x[0]:i for i,x in enumerate(TEXTURE_SPECS)}
SOURCE_HASHES={};TEXTURE_REPORT=[];BAKE_MATERIALS={}

def relative(path):return Path(path).relative_to(ROOT).as_posix()
def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def remember(path):SOURCE_HASHES[relative(path)]=digest(path)
def active(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
def clear_scene():
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
def canonical_material(name):
    return re.sub(r'\.\d+$','',name)
def triangle_count(mesh):return sum(len(p.vertices)-2 for p in mesh.polygons)

def extract_sources():
    CACHE.mkdir(parents=True,exist_ok=True);remember(ARCHIVE)
    with zipfile.ZipFile(ARCHIVE) as archive:
        names=set(archive.namelist())
        def extract(member):
            target=(CACHE/member).resolve()
            if not target.is_relative_to(CACHE.resolve()):raise RuntimeError('Unsafe archive path')
            target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(archive.read(member))
        for name,pack,_ in TREES+EXTRAS:
            if pack!='mega':continue
            member='glTF/'+name+'.gltf';data=json.loads(archive.read(member));extract(member)
            for entry in data.get('buffers',[])+data.get('images',[]):
                uri=entry['uri'];source='glTF/'+uri
                if source not in names:source='Textures/'+uri
                (CACHE/'glTF'/uri).write_bytes(archive.read(source))
        for _,pack,filename,_ in TEXTURE_SPECS:
            if pack=='mega':
                source='Textures/'+filename
                if source not in names:source='glTF/'+filename
                extract(source)
    for path in [MEGA/'License_Standard.txt',MEGA/'SourceManifest.json',ULTIMATE/'License.txt',ULTIMATE/'SourceManifest.json']:
        remember(path)

def textures():
    for slice_index,(name,pack,filename,leaf) in enumerate(TEXTURE_SPECS):
        path=(CACHE/'Textures'/filename) if pack=='mega' else ULTIMATE/filename
        if not path.exists():path=CACHE/'glTF'/filename
        if pack=='ultimate':remember(path)
        image=bpy.data.images.load(str(path),check_existing=False);image.colorspace_settings.name='sRGB'
        original=list(image.size);image.scale(512,512)
        target=TEXTURES/f'Slice_{slice_index:02}_{name}.png'
        image.filepath_raw=str(target);image.file_format='PNG';image.save()
        TEXTURE_REPORT.append({'slice':slice_index,'name':name,'path':relative(target),
            'source':relative(ARCHIVE)+'!Textures/'+filename if pack=='mega' else relative(path),
            'sourceSize':original,'size':[512,512],'alphaCutout':leaf,'sha256':digest(target)})
        material=bpy.data.materials.new('Bake_'+name);material.use_nodes=True
        nodes=material.node_tree.nodes;nodes.clear();links=material.node_tree.links
        output=nodes.new('ShaderNodeOutputMaterial');tex=nodes.new('ShaderNodeTexImage');tex.image=image
        tex.extension='REPEAT';uv=nodes.new('ShaderNodeUVMap');uv.uv_map='UV0';links.new(uv.outputs['UV'],tex.inputs['Vector'])
        emission=nodes.new('ShaderNodeEmission');links.new(tex.outputs['Color'],emission.inputs['Color'])
        if leaf:
            transparent=nodes.new('ShaderNodeBsdfTransparent');threshold=nodes.new('ShaderNodeMath');threshold.operation='GREATER_THAN';threshold.inputs[1].default_value=.2
            links.new(tex.outputs['Alpha'],threshold.inputs[0]);mix=nodes.new('ShaderNodeMixShader')
            links.new(threshold.outputs[0],mix.inputs[0]);links.new(transparent.outputs[0],mix.inputs[1]);links.new(emission.outputs[0],mix.inputs[2]);links.new(mix.outputs[0],output.inputs['Surface'])
        else:links.new(emission.outputs[0],output.inputs['Surface'])
        BAKE_MATERIALS[slice_index]=material

def load_model(name,pack):
    clear_scene()
    if pack=='mega':
        source=CACHE/'glTF'/(name+'.gltf');bpy.ops.import_scene.gltf(filepath=str(source));source_name=relative(ARCHIVE)+'!glTF/'+name+'.gltf'
    else:
        source=ULTIMATE/(name+'.fbx');remember(source);bpy.ops.import_scene.fbx(filepath=str(source));source_name=relative(source)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:obj.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();obj=bpy.context.object
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    bottom=min(v.co.z for v in obj.data.vertices)
    for v in obj.data.vertices:v.co.z-=bottom
    obj.name=name
    while len(obj.data.uv_layers)>1:obj.data.uv_layers.remove(obj.data.uv_layers[-1])
    obj.data.uv_layers[0].name='UV0'
    mapping=[]
    for material in obj.data.materials:
        key=canonical_material(material.name)
        if key not in MATERIAL_SLICES:raise RuntimeError((name,key))
        mapping.append(MATERIAL_SLICES[key])
    original_triangles=triangle_count(obj.data)
    return obj,mapping,source_name,original_triangles

def copy_object(obj,name):
    result=obj.copy();result.data=obj.data.copy();bpy.context.collection.objects.link(result);result.name=name;return result

def leaf_components(mesh):
    parent=list(range(len(mesh.vertices)))
    def find(x):
        while parent[x]!=x:parent[x]=parent[parent[x]];x=parent[x]
        return x
    for edge in mesh.edges:
        a,b=map(find,edge.vertices);parent[b]=a
    groups={}
    for p in mesh.polygons:groups.setdefault(find(p.vertices[0]),[]).append(p.index)
    return list(groups.values())

def reduce_leaf_cards(obj):
    groups=leaf_components(obj.data)
    if len(groups)<20:
        decimate(obj,.53);return
    # Keep whole connected leaf cards, including silhouette extrema. Collapsing individual
    # leaf triangles instead would break alpha-card UVs and leave visibly torn branches.
    centers=[]
    for indices in groups:
        vertices=set(v for i in indices for v in obj.data.polygons[i].vertices)
        centers.append(sum((obj.data.vertices[v].co for v in vertices),Vector())/len(vertices))
    keep=set()
    for axis in range(3):
        keep.add(min(range(len(groups)),key=lambda i:centers[i][axis]));keep.add(max(range(len(groups)),key=lambda i:centers[i][axis]))
    ranked=sorted(range(len(groups)),key=lambda i:hashlib.sha256(('%s:%d'%(obj.name,i)).encode()).digest())
    keep.update(ranked[:max(1,round(len(groups)*.48))])
    drop={p for i,g in enumerate(groups) if i not in keep for p in g}
    bm=bmesh.new();bm.from_mesh(obj.data);bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.index in drop],context='FACES')
    bm.to_mesh(obj.data);bm.free();obj.data.update()

def decimate(obj,ratio):
    if ratio>=.995:return
    active(obj)
    # Source exports split normals/UV vertices; weld coincident geometry while UVs stay per-loop.
    bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001);bm.to_mesh(obj.data);bm.free()
    modifier=obj.modifiers.new('Preserve silhouette, reduce internal edges','DECIMATE');modifier.ratio=max(.02,ratio)
    modifier.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=modifier.name)

def make_lod(source,mapping,lod,wood_budget,grass=False,rock=False):
    pieces=[]
    for mat_index,slice_index in enumerate(mapping):
        piece=copy_object(source,source.name+f'_part{mat_index}_LOD{lod}')
        bm=bmesh.new();bm.from_mesh(piece.data)
        bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.material_index!=mat_index],context='FACES')
        loose=[v for v in bm.verts if not v.link_faces]
        if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
        bm.to_mesh(piece.data);bm.free();piece.data.update()
        is_leaf=TEXTURE_SPECS[slice_index][3]
        count=triangle_count(piece.data)
        if lod==0 and not is_leaf:decimate(piece,min(1,wood_budget/max(1,count)))
        if lod==1:
            if is_leaf:reduce_leaf_cards(piece)
            else:decimate(piece,min(.42,wood_budget*.40/max(1,count)))
        for polygon in piece.data.polygons:polygon.material_index=0
        piece.data.materials.clear();piece.data.materials.append(BAKE_MATERIALS[slice_index])
        uv_slice=piece.data.uv_layers.new(name='UV1_ArrayWind');uv_flutter=piece.data.uv_layers.new(name='UV2_FlutterHeight')
        height=max(v.co.z for v in source.data.vertices)
        for loop in piece.data.loops:
            h=max(0,min(1,piece.data.vertices[loop.vertex_index].co.z/max(.01,height)))
            weight=0 if rock else (h*h if grass else h**1.65*(1 if is_leaf else .22))
            uv_slice.data[loop.index].uv=(slice_index,weight);uv_flutter.data[loop.index].uv=(1 if is_leaf else 0,h)
        pieces.append(piece)
    bpy.ops.object.select_all(action='DESELECT')
    for piece in pieces:piece.select_set(True)
    bpy.context.view_layer.objects.active=pieces[0];bpy.ops.object.join();obj=bpy.context.object;obj.name=source.name+f'_LOD{lod}'
    modifier=obj.modifiers.new('Export real triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj

def export_model(obj):
    exported=copy_object(obj,obj.name+'_export');exported.data.materials.clear()
    material=bpy.data.materials.get('NatureArray') or bpy.data.materials.new('NatureArray');exported.data.materials.append(material)
    for p in exported.data.polygons:p.material_index=0
    for attr in list(exported.data.color_attributes):exported.data.color_attributes.remove(attr)
    colors=exported.data.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
    for c in colors.data:c.color=(1,1,1,1)
    active(exported);path=MODELS/(obj.name+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
        bake_space_transform=True,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,
        mesh_smooth_type='FACE',colors_type='LINEAR',bake_anim=False,path_mode='AUTO')
    points=[v.co for v in exported.data.vertices]
    lo=[min(v[i] for v in points) for i in range(3)];hi=[max(v[i] for v in points) for i in range(3)]
    uvstats=[]
    for uv in exported.data.uv_layers:
        values=[x.uv for x in uv.data]
        assert len(values)==len(exported.data.loops) and all(math.isfinite(c) for value in values for c in value)
        uvstats.append({'name':uv.name,'min':[min(v[i] for v in values) for i in range(2)],'max':[max(v[i] for v in values) for i in range(2)]})
    result={'path':relative(path),'triangles':triangle_count(exported.data),'vertices':len(points),'meshCount':1,'materialCount':1,
        'boundsUnityMin':[lo[0],lo[2],-hi[1]],'boundsUnityMax':[hi[0],hi[2],-lo[1]],'uv':uvstats,'sha256':digest(path)}
    bpy.data.objects.remove(exported,do_unlink=True);return result

def render_billboard(obj):
    for item in bpy.context.scene.objects:item.hide_render=item!=obj
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=False
    scene.cycles.max_bounces=2;scene.cycles.transparent_max_bounces=64
    scene.render.threads_mode='FIXED';scene.render.threads=12;scene.render.film_transparent=True
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth='8'
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
    scene.world=bpy.data.worlds.new('No baked directional light');scene.world.color=(0,0,0)
    points=[v.co for v in obj.data.vertices];height=max(v.z for v in points)*1.035
    width=2*max(math.hypot(v.x,v.y) for v in points)*1.035
    span=max(width,height);scene.render.resolution_x=max(64,round(384*width/span));scene.render.resolution_y=max(64,round(384*height/span));scene.render.resolution_percentage=100
    data=bpy.data.cameras.new('Eight yaw unlit bake');camera=bpy.data.objects.new('Eight yaw unlit bake',data);bpy.context.collection.objects.link(camera)
    scene.camera=camera;data.type='ORTHO';data.ortho_scale=1
    frame=data.view_frame(scene=scene);span_x=max(v.x for v in frame)-min(v.x for v in frame);data.ortho_scale=width/span_x
    atlas=np.zeros((CELL*2,CELL*4,4),dtype=np.float32);frames=[]
    for index in range(8):
        angle=index*math.tau/8;camera.location=(math.sin(angle)*span*3,-math.cos(angle)*span*3,height/2)
        camera.rotation_euler=(Vector((0,0,height/2))-camera.location).to_track_quat('-Z','Y').to_euler()
        path=CACHE/(obj.name+f'_View{index}.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
        image=bpy.data.images.load(str(path),check_existing=False);image.scale(CELL,CELL)
        pixels=np.array(image.pixels[:],dtype=np.float32).reshape(CELL,CELL,4)
        assert np.count_nonzero(pixels[:,:,3]>.3)>CELL*CELL*.002, (obj.name,index,'Empty alpha bake')
        atlas[(index//4)*CELL:(index//4+1)*CELL,(index%4)*CELL:(index%4+1)*CELL,:]=pixels
        frames.append({'view':index,'cameraUnityDirection':[round(math.sin(angle),6),0,round(math.cos(angle),6)],'alphaCoverage':float(np.mean(pixels[:,:,3]>.3))})
        bpy.data.images.remove(image)
    out=TEXTURES/(obj.name.replace('_LOD0','')+'_Billboard.png');image=bpy.data.images.new(out.stem,width=CELL*4,height=CELL*2,alpha=True)
    image.pixels.foreach_set(atlas.ravel());image.filepath_raw=str(out);image.file_format='PNG';image.save();bpy.data.images.remove(image)
    bpy.data.objects.remove(camera,do_unlink=True)
    return {'path':relative(out),'width':width,'height':height,'viewCount':8,'columns':4,'rows':2,'cellPixels':CELL,
        'lighting':'unlit source baseColor; alpha threshold 0.2; actual LOD0 geometry',
        'pivot':'ground center; quad x=-width/2..+width/2, y=0..height, z=0',
        'viewOrder':'round(atan2(cameraObject.x,cameraObject.z)/TAU*8) mod8; view0 at UV lower-left',
        'frames':frames,'sha256':digest(out)}

def audit_exports():
    """Read exported FBX back from disk; do not save or alter any production asset."""
    manifest=json.loads((DEST/'NatureModelManifest.json').read_text(encoding='utf-8'))
    results=[]
    for entry in manifest['species']+manifest['groundCover']:
        for level in ('lod0','lod1'):
            expected=entry[level];path=ROOT/expected['path'];before=digest(path);clear_scene()
            bpy.ops.import_scene.fbx(filepath=str(path),colors_type='LINEAR')
            meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert len(meshes)==1,(path,len(meshes))
            obj=meshes[0];mesh=obj.data
            assert len(mesh.materials)==1 and len(mesh.uv_layers)==3,(path,len(mesh.materials),len(mesh.uv_layers))
            assert triangle_count(mesh)==expected['triangles'],(path,triangle_count(mesh),expected['triangles'])
            for layer in mesh.uv_layers:
                assert len(layer.data)==len(mesh.loops)
                assert all(math.isfinite(c) for item in layer.data for c in item.uv)
            slices=sorted({round(item.uv.x) for item in mesh.uv_layers[1].data})
            assert slices==sorted(entry['slices']),(path,slices,entry['slices'])
            assert all(abs(item.uv.x-round(item.uv.x))<.00001 and 0<=item.uv.y<=1 for item in mesh.uv_layers[1].data)
            assert all(0<=c<=1 for item in mesh.uv_layers[2].data for c in item.uv)
            points=[obj.matrix_world@v.co for v in mesh.vertices]
            height=max(v.z for v in points)-min(v.z for v in points)
            expected_height=expected['boundsUnityMax'][1]-expected['boundsUnityMin'][1]
            assert abs(height-expected_height)<.0001,(path,height,expected_height)
            assert before==digest(path)
            results.append({'path':relative(path),'triangles':triangle_count(mesh),'meshCount':1,'materialCount':1,
                'uvChannels':len(mesh.uv_layers),'slices':slices,'heightMetres':height,'finiteUV':True,'sha256Unchanged':True})
    sources={path:digest(ROOT/path)==sha for path,sha in manifest['sourceHashes'].items()};assert all(sources.values())
    report={'passed':True,'fbxCount':len(results),'models':results,'sourceHashesUnchanged':sources}
    (EVIDENCE/'NatureExportAudit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('NATURE_EXPORT_AUDIT_PASSED',len(results),flush=True)

def main():
    if '--audit' in sys.argv:
        audit_exports();return
    for directory in (MODELS,TEXTURES,EVIDENCE):directory.mkdir(parents=True,exist_ok=True)
    extract_sources();textures();species=[];ground=[]
    requested=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    only=requested[requested.index('--only')+1] if '--only' in requested else None
    for name,pack,budget in TREES+EXTRAS:
        if only and name!=only:continue
        obj,mapping,source,original_tris=load_model(name,pack);obj.hide_render=True
        rock=name.startswith('Rock_');grass=name in ('Grass_Common_Short','Fern_1')
        lod0=make_lod(obj,mapping,0,budget,grass,rock);lod1=make_lod(obj,mapping,1,budget,grass,rock)
        first=export_model(lod0);second=export_model(lod1)
        assert second['triangles']<first['triangles'],(name,first['triangles'],second['triangles'])
        entry={'id':name,'source':source,'sourceTriangles':original_tris,'lod0Path':first['path'],'lod1Path':second['path'],
            'lod0Triangles':first['triangles'],'lod1Triangles':second['triangles'],'lod0':first,'lod1':second,
            'boundsUnityMin':first['boundsUnityMin'],'boundsUnityMax':first['boundsUnityMax'],'slices':mapping}
        if not grass and not rock:
            baked=render_billboard(lod0);entry['billboard']=baked;entry['billboardPath']=baked['path'];entry['billboardTexturePath']=baked['path']
            entry['billboardWidth']=baked['width'];entry['billboardHeight']=baked['height'];species.append(entry)
        else:ground.append(entry)
        (EVIDENCE/(name+'_NatureBuild.json')).write_text(json.dumps(entry,indent=2),encoding='utf-8')
        print('NATURE_COMPLETE',name,first['triangles'],second['triangles'],flush=True)
    unchanged={p:digest(ROOT/p)==sha for p,sha in SOURCE_HASHES.items()};assert all(unchanged.values())
    report={'version':1,'license':'CC0 1.0 Universal; Quaternius. Original license text and manifests preserved under Tools/WorldDev/SourceDownloads.',
        'coordinateSystem':'Unity metres: +Y up, +Z front, +X right; pivot ground center; original texture UV0 repeats',
        'vertexContract':{'UV0':'source Repeat UV','UV1':'x=array slice integer,y=windWeight 0..1','UV2':'x=leafFlutter 0/1,y=heightNormalized 0..1','Color':'linear white'},
        'textures':TEXTURE_REPORT,'species':species,'groundCover':ground,'grass':next((x for x in ground if x['id']=='Grass_Common_Short'),None),
        'sourceHashes':SOURCE_HASHES,'sourceHashesUnchanged':unchanged,
        'biomeSpecies':{'Zephyr':['CommonTree_3','BirchTree_1','MapleTree_1'],'Teluna':['PalmTree_1','CommonTree_3','TwistedTree_2'],
            'Granite':['Pine_5','BirchTree_1','DeadTree_5'],'Agnia':['DeadTree_5','Pine_5','MapleTree_1'],'Voltheim':['TwistedTree_2','Pine_5','BirchTree_1']}}
    filename='NatureModelManifest.json' if not only else 'NatureModelManifest_'+only+'.json'
    (DEST/filename).write_text(json.dumps(report,indent=2),encoding='utf-8')
    (EVIDENCE/filename).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('NATURE_BUILD_FINISHED',relative(DEST/filename),flush=True)

if __name__=='__main__':main()
