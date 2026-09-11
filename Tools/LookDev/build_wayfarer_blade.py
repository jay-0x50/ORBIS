"""Derive the Wayfarer blade from the licensed Kenney/KayKit sword inputs.

Run with Blender --background --python this_file.py -- [--inspect-only].
The imported source assets are read-only. Final output is one mesh/material.
"""
import bpy, bmesh, json, math, sys, hashlib
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Orbis/Game/LookDev/Models'
SOURCES = ROOT / 'Tools/LookDev/Sources'
EVIDENCE = ROOT / 'TestResults/LookDev'
INPUTS = {
    'Kenney': ROOT / 'Assets/ImportedAssets/Kenney/MiniDungeon/Models/weapon-sword.fbx',
    'KayKit': ROOT / 'Assets/ImportedAssets/KayKit/Adventurers/Weapons/sword_1handed.fbx',
}
PALETTE = ROOT / 'Assets/Orbis/Game/LookDev/Resources/LookDev/ReferencePalette.json'
VERTICES, FACES, FACE_COLORS = [], [], []
PARTS = []
COLORS = {}

def linear(value):
    return value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4

def load_colors():
    for entry in json.loads(PALETTE.read_text(encoding='utf-8'))['swatches']:
        COLORS[entry['name']] = tuple(linear(entry[c] / 255) for c in ('r','g','b')) + (1.,)

def piece(name, vertices, faces, colors):
    offset = len(VERTICES)
    start = len(FACES)
    VERTICES.extend(vertices)
    FACES.extend([tuple(offset+i for i in f) for f in faces])
    FACE_COLORS.extend([COLORS[c] for c in ([colors]*len(faces) if isinstance(colors,str) else colors)])
    PARTS.append({'name': name, 'vertices': len(vertices), 'faceStart': start,
                  'faces': len(faces), 'triangles': sum(len(f)-2 for f in faces)})

def loft(name, rings, sides, colors):
    """Closed Z-axis faceted lathe; radii can differ in X/Y for a slim grip."""
    vs = [(math.cos(i*math.tau/sides)*rx, math.sin(i*math.tau/sides)*ry, z)
          for z,rx,ry in rings for i in range(sides)]
    fs = [tuple(reversed(range(sides))), tuple((len(rings)-1)*sides+i for i in range(sides))]
    cs = [colors[0],colors[0]]
    for j in range(len(rings)-1):
        for i in range(sides):
            fs.append((j*sides+i,j*sides+(i+1)%sides,(j+1)*sides+(i+1)%sides,(j+1)*sides+i))
            cs.append(colors[i % len(colors)])
    piece(name,vs,fs,cs)

def tube(name, points, widths, depth, palette=('Gold','GoldHighlight','Gold','GoldShadow','GoldShadow','Gold')):
    """Six-sided swept solid in XZ, tangent-aligned without twisting frames."""
    points = [Vector(p) for p in points]
    vs,fs,cs = [],[],[]
    sides=6
    for j,p in enumerate(points):
        t=(points[min(j+1,len(points)-1)]-points[max(0,j-1)]).normalized()
        side=Vector((t.z,0,-t.x)).normalized()
        for i in range(sides):
            a=i*math.tau/sides
            vs.append(tuple(p+side*(math.cos(a)*widths[j])+Vector((0,math.sin(a)*depth,0))))
    fs.extend([tuple(reversed(range(sides))),tuple((len(points)-1)*sides+i for i in range(sides))])
    cs.extend([palette[0],palette[1]])
    for j in range(len(points)-1):
        for i in range(sides):
            fs.append((j*sides+i,j*sides+(i+1)%sides,(j+1)*sides+(i+1)%sides,(j+1)*sides+i))
            cs.append(palette[i])
    piece(name,vs,fs,cs)

def bezier(a,b,c,d,count=13):
    a,b,c,d=map(Vector,(a,b,c,d))
    return [tuple((1-t)**3*a+3*(1-t)**2*t*b+3*(1-t)*t*t*c+t**3*d) for t in [i/(count-1) for i in range(count)]]

def diamond(name, x,z,rx,rz,y,depth,colors):
    """Faceted gem/crest with an embedded back and raised front centre."""
    perimeter=[(x-rx,y,z),(x,y,z-rz),(x+rx,y,z),(x,y,z+rz)]
    vs=perimeter+[(x,y-depth,z),(x,y+depth*.4,z)]
    fs=[(i,(i+1)%4,4) for i in range(4)]+[((i+1)%4,i,5) for i in range(4)]
    piece(name,vs,fs,[colors[i%len(colors)] for i in range(8)])

def diamond_frame(name,x,z,rx,rz,y,width,depth):
    points=[(x,y,z+rz),(x+rx,y,z),(x,y,z-rz),(x-rx,y,z),(x,y,z+rz)]
    tube(name,points,[width]*len(points),depth)

def source_blade(report):
    src=report['Kenney']['meshes'][0]
    # Preserve the source blade's 15 vertices and 22 triangle connections. The source
    # grip/guard are excluded because their broad toy proportions do not match the reference.
    selected=[f['v'] for f in src['faces'] if all(src['positions'][i][2] > .07 for i in f['v'])
              and all(i < 15 for i in f['v'])]
    assert len(selected)==22
    verts=[]
    for i,(x,y,z) in enumerate(src['positions'][:15]):
        if z > .34: v=(0,0,.94)
        elif abs(x)<.001: v=(0,math.copysign(.0018,y),.909)
        elif z > .29: v=(math.copysign(.018,x),0,.824)
        elif z > .28: v=(math.copysign(.015,x),math.copysign(.0038,y),.814)
        else:
            v=(math.copysign(.034 if abs(x)>.06 else .030,x),0 if abs(y)<.001 else math.copysign(.005,y),.100)
        verts.append(v)
    colors=[]
    for face in selected:
        # The source's double bevel is gold; the broad recessed blade faces remain steel.
        edge=any(abs(verts[i][1]) < .0001 for i in face)
        colors.append('Gold' if edge else 'Steel')
    # Close the imported blade's open root beneath the new guard.
    root=[i for i,v in enumerate(verts) if abs(v[2]-.1)<.0001]
    root.sort(key=lambda i:math.atan2(verts[i][1],verts[i][0]))
    selected.append(tuple(reversed(root)));colors.append('Steel')
    piece('Kenney source blade: original 22 triangles refined; root capped',verts,selected,colors)

def build(report):
    clear();load_colors()
    source_blade(report)
    # Dimensions are prototype art defaults: 1.06m total, 0.228m swept guard,
    # 0.024m grip diameter. Origin is the hand's grip centre, not the blade base.
    loft('Navy leather grip',[(-.079,.014,.011),(-.068,.013,.010),(-.024,.011,.009),
                              (.026,.012,.0095),(.063,.014,.011)],8,
         ('NavyShadow','Leather','Leather','NavyShadow','NavyShadow','Leather','Leather','NavyShadow'))
    for label,z,rad in [('Pommel ferrule',-.076,.0145),('Guard ferrule',.058,.015)]:
        loft(label,[(z-.004,rad,.0115),(z-.002,rad+.001,.0125),(z+.003,rad+.001,.0125),(z+.005,rad,.0115)],8,
             ('GoldShadow','Gold','GoldHighlight','Gold','GoldShadow','Gold','GoldHighlight','Gold'))
    loft('Guard throat connecting grip and crest',[(.060,.010,.008),(.076,.011,.008),(.088,.014,.008)],8,
         ('GoldShadow','Gold','GoldHighlight','Gold','GoldShadow','Gold','GoldHighlight','Gold'))
    # Fine diagonal leather binding. Its narrow relief reads as grip detail, not extra materials.
    for band in range(3):
        z=-.052+band*.034
        path=[(.0127*math.cos(t),.0104*math.sin(t),z+.010*t/math.tau) for t in [i*math.tau/16 for i in range(17)]]
        tube('Leather grip binding '+str(band),path,[.0007]*len(path),.00065,('Leather',)*6)
    diamond('Pommel gold setting',0,-.098,.019,.022,-.002,.007,('Gold','GoldHighlight','GoldShadow','Gold'))
    diamond('Pommel navy inset',0,-.098,.010,.014,-.0095,.002,('NavyShadow','Navy','NavyShadow','Navy'))
    diamond('Pommel tiny teal inset',0,-.098,.0045,.007,-.012,.001,('TurquoiseDeep','Turquoise','TurquoiseDeep','Turquoise'))

    for sign in (-1,1):
        # Swept, tapered quillons have one continuous S-like arc; no giant flat rectangle.
        pts=bezier((sign*.013,0,.083),(sign*.046,0,.068),(sign*.084,0,.049),(sign*.112,0,.091),17)
        widths=[.009*(1-t)+.0017*t for t in [i/16 for i in range(17)]]
        tube('Swept gold guard '+str(sign),pts,widths,.0055)
        pts=bezier((sign*.013,-.006,.110),(sign*.049,-.006,.103),(sign*.083,-.003,.059),(sign*.109,0,.091),13)
        tube('Guard inner gold arch '+str(sign),pts,[.0034*(1-i/12)+.0007*(i/12) for i in range(13)],.002)
        # Short upwards spear on each guard shoulder, matching the reference's thin gold fins.
        pts=[(sign*.019,-.003,.080),(sign*.044,-.003,.077),(sign*.055,-.002,.069)]
        tube('Guard shoulder fin '+str(sign),pts,[.007,.004,.0005],.003)

    diamond('Central navy crest',0,.112,.025,.038,-.007,.0035,('NavyShadow','Navy','NavyShadow','Navy'))
    diamond_frame('Central gold crest frame',0,.112,.027,.041,-.010,.003,.0023)
    diamond('Guard turquoise diamond',0,.112,.013,.024,-.014,.004,
            ('Turquoise','TurquoiseDeep','TurquoiseDeep','Turquoise'))
    # Back gets a shallow crest too, so the single-sided showcase does not hide an unfinished sword.
    diamond('Rear central gold crest',0,.112,.023,.034,.007,-.0035,('Gold','GoldShadow','Gold','GoldHighlight'))
    diamond('Rear turquoise inset',0,.112,.010,.019,.011,-.003,('TurquoiseDeep','Turquoise','TurquoiseDeep','Turquoise'))
    for sign in (-1,1):
        for front in (-1,1):
            y=front*.0062
            tube('Blade root gold scroll '+str((sign,front)),
                 [(sign*.022,y,.145),(sign*.024,y,.178),(sign*.014,y,.205),(sign*.008,y,.227)],
                 [.0025,.002,.0018,.0005],.001)
            tube('Long blade gold inlay '+str((sign,front)),
                 [(sign*.008,y,.238),(sign*.010,y,.270),(sign*.004,front*.0048,.405),
                  (sign*.002,front*.0042,.670),(0,front*.0035,.814)],
                 [.0015,.0010,.0007,.0005,.00015],.0005)
    for front in (-1,1):
        diamond_frame('Blade upper star inlay '+str(front),0,.212,.008,.019,front*.0070,.0012,.00065)
        diamond_frame('Blade middle star inlay '+str(front),0,.444,.007,.029,front*.0058,.001,.00055)
    # One short, rigid navy tassel reproduces the pommel silhouette. Animation is deliberately absent.
    tube('Pommel tassel gold loop',[(.014,-.001,-.103),(.026,-.001,-.092),(.030,-.001,-.066)],
         [.0016,.0015,.0012],.001)
    vs=[(.027,-.003,-.070),(.033,-.003,-.070),(.040,-.004,.039),(.032,-.007,.046),(.025,-.004,.042),
        (.026,.003,-.069),(.033,.003,-.069),(.040,.003,.039),(.032,.005,.046),(.025,.003,.042)]
    fs=[(0,1,3),(1,2,3),(0,3,4),(5,8,6),(6,8,7),(5,9,8),
        (0,5,6,1),(1,6,7,2),(2,7,8,3),(3,8,9,4),(4,9,5,0)]
    piece('Navy pommel tassel',vs,fs,['NavyShadow','Navy','NavyShadow','Navy','NavyShadow','Navy']+['NavyShadow']*5)

    mesh=bpy.data.meshes.new('WayfarerBlade_Mesh')
    mesh.from_pydata(VERTICES,[],FACES);mesh.update()
    # Recalculate component winding before adding per-corner colors; components remain a single draw.
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    colors=mesh.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
    for polygon,color in zip(mesh.polygons,FACE_COLORS):
        for index in polygon.loop_indices: colors.data[index].color=color
    mesh.color_attributes.active_color=colors
    uv=mesh.uv_layers.new(name='UVMap')
    for polygon in mesh.polygons:
        for li in polygon.loop_indices:
            p=mesh.vertices[mesh.loops[li].vertex_index].co
            uv.data[li].uv=((p.x+.12)/.24,(p.z+.12)/1.06)
    mat=bpy.data.materials.new('WayfarerBlade_VertexColor');mat.use_nodes=True
    bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Metallic'].default_value=.45;bsdf.inputs['Roughness'].default_value=.43
    vcol=mat.node_tree.nodes.new('ShaderNodeVertexColor');vcol.layer_name='Color'
    mat.node_tree.links.new(vcol.outputs['Color'],bsdf.inputs['Base Color'])
    mesh.materials.append(mat)
    obj=bpy.data.objects.new('WayfarerBlade',mesh);bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    # Real triangles make the <3000 budget unambiguous in Blender and Unity.
    triangulate=obj.modifiers.new('Deterministic triangles','TRIANGULATE')
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    return obj

def render(obj):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
    scene.render.threads_mode='FIXED';scene.render.threads=10
    scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0
    scene.world=bpy.data.worlds.new('Wayfarer neutral studio');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.72,.72,.72,1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.65
    for name,loc,power,size in [('Key',(-1,-2,2),80,2),('Fill',(2,-1,.4),35,2),('Edge',(-1,1,.8),50,1)]:
        data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
        light=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(light);light.location=loc
        light.rotation_euler=(Vector((0,0,.4))-light.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Wayfarer inspection');camera=bpy.data.objects.new('Wayfarer inspection',data)
    bpy.context.collection.objects.link(camera);scene.camera=camera;data.type='ORTHO'
    for suffix,position,look,span,w,h in [('Front',(0,-3,.41),(0,0,.41),1.19,720,1600),
        ('ThreeQuarter',(.8,-3,.43),(0,0,.41),1.19,720,1600),
        ('Guard',(.18,-3,.10),(0,0,.10),.31,1100,1100)]:
        camera.location=position
        camera.rotation_euler=(Vector(look)-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.rotation_euler.rotate_axis('Z',math.pi) # Tip down, matching the reference's weapon detail.
        data.ortho_scale=span;scene.render.resolution_x=w;scene.render.resolution_y=h
        scene.render.filepath=str(EVIDENCE/('Step03_WayfarerBlade_'+suffix+'.png'))
        bpy.ops.render.render(write_still=True)

def export(obj,report):
    OUT.mkdir(parents=True,exist_ok=True)
    bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.preferences.filepaths.file_preview_type='NONE'
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(OUT/'WayfarerBlade.fbx'),use_selection=True,object_types={'MESH'},
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,mesh_smooth_type='FACE',
        colors_type='LINEAR',bake_anim=False,path_mode='AUTO')
    mesh=obj.data;points=[v.co for v in mesh.vertices]
    count=sum(len(p.vertices)-2 for p in mesh.polygons)
    assert count<3000, count
    assert len(mesh.materials)==1 and len(mesh.color_attributes)==1
    assert all(math.isfinite(x) for p in points for x in p)
    unchanged={label:digest(path)==report[label]['sha256'] for label,path in INPUTS.items()}
    assert all(unchanged.values())
    evidence={
        'route':'b: refine existing CC0 similar sword; preserve imported source assets',
        'selectedSource':report['Kenney']['path'],'selectedSourceSHA256':report['Kenney']['sha256'],
        'sourceUrl':'https://kenney.nl/assets/mini-dungeon','creator':'Kenney','license':'CC0 1.0',
        'licenseEvidence':'Docs/AssetLicenses/Kenney_MiniDungeon/License.txt',
        'alternativeInspected':report['KayKit']['path'],
        'sourceMeshReuse':'Original Kenney blade 15 vertices / 22 triangle connectivity retained, reshaped and root capped. Original guard/grip replaced with reference-derived integrated geometry.',
        'reference':json.loads(PALETTE.read_text(encoding='utf-8'))['source'],
        'palette':str(PALETTE.relative_to(ROOT)).replace('\\','/'),'paletteSHA256':digest(PALETTE),
        'vertexColorContract':'Exact palette samples converted sRGB to linear, FLOAT_COLOR/CORNER Color, FBX colors_type=LINEAR. Unity should pass vertex COLOR unchanged to the toon shader.',
        'meshCount':1,'materialCount':1,'vertices':len(points),'triangles':count,
        'blenderBounds':[[min(p[i] for p in points) for i in range(3)],[max(p[i] for p in points) for i in range(3)]],
        'blenderAxes':'+Z tip, -Y decorated front, +X guard span',
        'fbxUnityAxes':'+Y tip, +Z decorated front, +X guard span; baked axis conversion; grip midpoint origin',
        'unityTipLocal':[0,.94,0],'unityPommelLocal':[0,-.12,0],'lengthMetres':1.06,
        'prototypeDefaults':'0.228m swept guard, 0.024m grip diameter; fixed navy tassel; no gameplay/rig changes.',
        'sourceHashesUnchanged':unchanged,'fbxSHA256':digest(OUT/'WayfarerBlade.fbx'),
        'parts':PARTS}
    (EVIDENCE/'Step03_WayfarerBlade_SourceEvidence.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    print('ORBIS_WAYFARER_BLADE='+json.dumps(evidence),flush=True)
    render(obj)
    SOURCES.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCES/'WayfarerBlade.blend'))

def audit_export():
    """Read-only FBX roundtrip: geometry, exact linear palette, no hidden texture/material dependency."""
    watched=list(INPUTS.values())+[PALETTE,OUT/'WayfarerBlade.fbx',SOURCES/'WayfarerBlade.blend']
    before={str(p.relative_to(ROOT)).replace('\\','/'):digest(p) for p in watched}
    clear();load_colors()
    bpy.ops.import_scene.fbx(filepath=str(OUT/'WayfarerBlade.fbx'),colors_type='LINEAR')
    objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
    assert len(objects)==1
    obj=objects[0];mesh=obj.data;points=[obj.matrix_world@v.co for v in mesh.vertices]
    colors=mesh.color_attributes.active_color
    palette=list(COLORS.values())
    errors=[min(max(abs(c.color[i]-p[i]) for i in range(4)) for p in palette) for c in colors.data]
    assert max(errors)<.00001,max(errors)
    bounds=lambda ps:[[min(p[i] for p in ps) for i in range(3)],[max(p[i] for p in ps) for i in range(3)]]
    triangles=sum(len(p.vertices)-2 for p in mesh.polygons)
    assert triangles<3000 and len(mesh.materials)==1
    assert all(math.isfinite(x) for p in points for x in p)
    zeroarea=sum(p.area<1e-12 for p in mesh.polygons)
    assert zeroarea==0,zeroarea
    after={str(p.relative_to(ROOT)).replace('\\','/'):digest(p) for p in watched}
    assert before==after
    data={'passed':True,'readOnlyHashesUnchanged':before==after,'sha256':after,
          'importColorsType':'LINEAR','maxErrorFromExactLinearPalette':max(errors),
          'meshCount':len(objects),'materialCount':len(mesh.materials),'vertices':len(mesh.vertices),
          'triangles':triangles,'zeroAreaTriangles':zeroarea,'uvLayerCount':len(mesh.uv_layers),
          'colorsName':colors.name,'colorCount':len(colors.data),
          'localBoundsAfterBlenderFBXImport':bounds([v.co for v in mesh.vertices]),
          'worldBoundsAfterBlenderFBXImport':bounds(points),
          'matrixAfterBlenderFBXImport':[list(row) for row in obj.matrix_world],
          'note':'Blender reintroduces its Z-up world convention when reading a Y-up FBX; local mesh +Y is the authored Unity blade length.'}
    (EVIDENCE/'Step03_WayfarerBlade_RoundtripAudit.json').write_text(json.dumps(data,indent=2),encoding='utf-8')
    print('ORBIS_WAYFARER_AUDIT='+json.dumps(data),flush=True)

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

def inspect_sources():
    report = {}
    for label, path in INPUTS.items():
        clear()
        bpy.ops.import_scene.fbx(filepath=str(path))
        meshes = []
        for obj in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
            points = [obj.matrix_world @ v.co for v in obj.data.vertices]
            lo = [min(p[i] for p in points) for i in range(3)]
            hi = [max(p[i] for p in points) for i in range(3)]
            mesh = {'name': obj.name, 'vertices': len(points), 'triangles': sum(len(p.vertices)-2 for p in obj.data.polygons),
                    'bounds': [lo, hi], 'materials': [m.name for m in obj.data.materials],
                    'matrix': [list(row) for row in obj.matrix_world],
                    'positions': [list(p) for p in points],
                    'faces': [{'v': list(p.vertices), 'material': p.material_index} for p in obj.data.polygons]}
            meshes.append(mesh)
        report[label] = {'path': str(path.relative_to(ROOT)).replace('\\','/'), 'sha256': digest(path), 'meshes': meshes}
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    (EVIDENCE/'Step03_SwordSources.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    return report

if __name__ == '__main__':
    if '--audit-only' in sys.argv:
        audit_export()
    else:
        report = inspect_sources()
        print('ORBIS_SWORD_SOURCES=' + json.dumps({k: {'meshes': [{f: v for f, v in m.items() if f not in ('positions','faces')} for m in r['meshes']]} for k,r in report.items()}), flush=True)
        if '--inspect-only' not in sys.argv:
            export(build(report),report)
            audit_export()
