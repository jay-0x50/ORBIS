"""Reference face/hair pass for the original Orbis humanoids.

Run Blender --background --python Tools/Blender/polish_explorers.py.
The immutable FaceV1Sources backup makes repeated runs non-accumulating.
The original body, costume, skin weights, bone names and proportions are retained.
Textures must exist before an FBX can be exported; --shape-preview allows only
temporary, unexported clay inspection. Facial details use a dedicated planar UV.
"""
import bpy, bmesh, math, json, shutil, sys
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT=Path(__file__).resolve().parents[2]
SOURCES=ROOT/'Tools/Blender/Sources'
BASE=SOURCES/'FaceV1Sources'
OUT=ROOT/'Tools/Blender/FaceExports'
TEXTURES=ROOT/'Assets/Orbis/Game/Island/Textures/Explorers'
RESULTS=ROOT/'TestResults'
PREVIEW='--shape-preview' in sys.argv
for folder in (BASE,OUT,RESULTS):folder.mkdir(parents=True,exist_ok=True)
bpy.context.preferences.filepaths.save_version=0
bpy.context.preferences.filepaths.file_preview_type='NONE'

def aim(obj,point):
    obj.rotation_euler=(Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()

def material(name,color,texture=None):
    mat=bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color=color;mat.use_nodes=True
    shader=mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value=color
    shader.inputs['Metallic'].default_value=0
    shader.inputs['Roughness'].default_value=.78
    if texture:
        if not texture.exists():
            if not PREVIEW:raise FileNotFoundError(texture)
        else:
            image=bpy.data.images.load(str(texture),check_existing=True)
            node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image
            node.extension='EXTEND' if name=='EX_Face' else 'REPEAT'
            mat.node_tree.links.new(node.outputs['Color'],shader.inputs['Base Color'])
    return mat

def components(mesh):
    adjacency=[[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a,b=edge.vertices;adjacency[a].append(b);adjacency[b].append(a)
    seen=set()
    for index in range(len(mesh.vertices)):
        if index in seen:continue
        stack=[index];seen.add(index);group=[]
        while stack:
            current=stack.pop();group.append(current)
            for neighbor in adjacency[current]:
                if neighbor not in seen:seen.add(neighbor);stack.append(neighbor)
        yield group

def remove_old_face(name,scale,offset):
    """Remove connected face details, preserving ears, neck and costume ornaments."""
    removed={}
    for obj in list(bpy.data.objects):
        if obj.type!='MESH' or not obj.name.startswith(name+'_'):continue
        mat=obj.data.materials[0].name if obj.data.materials else ''
        if mat in ('EX_Hair','EX_HairLight'):
            removed[obj.name]=len(obj.data.vertices);bpy.data.objects.remove(obj,do_unlink=True);continue
        doomed=[]
        head=obj.vertex_groups.get('Head')
        if not head:continue
        for group in components(obj.data):
            source=[Vector((obj.data.vertices[i].co.x/scale,obj.data.vertices[i].co.y/scale,
                           (obj.data.vertices[i].co.z-offset)/scale)) for i in group]
            center=sum(source,Vector())/len(source)
            has_head=all(any(g.group==head.index and g.weight>.99 for g in obj.data.vertices[i].groups) for i in group)
            if not has_head:continue
            if mat=='EX_Skin':
                is_face=abs(center.x)<.07 and center.z>1.495
            else:
                is_face=(abs(center.x)<.085 and center.y<-.045 and 1.50<center.z<1.66
                         and mat in ('EX_EyeWhite','EX_Teal','EX_Ink','EX_Leather'))
            if is_face:doomed.extend(group)
        if doomed:
            removed[obj.name]=len(doomed)
            bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
            bmesh.ops.delete(bm,geom=[bm.verts[i] for i in doomed],context='VERTS')
            bm.to_mesh(obj.data);bm.free();obj.data.update()
            if not len(obj.data.vertices):bpy.data.objects.remove(obj,do_unlink=True)
    return removed

def create_mesh(name,vertices,faces,uvs,mat,rig,scale,offset,materials=None,indices=None):
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata([(p[0]*scale,p[1]*scale,p[2]*scale+offset) for p in vertices],[],faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.parent=rig
    for entry in (materials or [mat]):mesh.materials.append(entry)
    layer=mesh.uv_layers.new(name='UVMap')
    for polygon in mesh.polygons:
        polygon.use_smooth=True
        if indices:polygon.material_index=indices[polygon.index]
        for li in polygon.loop_indices:layer.data[li].uv=uvs[mesh.loops[li].vertex_index]
    group=obj.vertex_groups.new(name='Head');group.add(list(range(len(vertices))),1,'REPLACE')
    mod=obj.modifiers.new('Humanoid Skin','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=True
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT');obj.select_set(False)
    return obj

def interpolated_profile(z,table,column):
    for i in range(len(table)-1):
        if table[i][0]<=z<=table[i+1][0]:
            t=(z-table[i][0])/(table[i+1][0]-table[i][0])
            p0=table[max(0,i-1)][column];p1=table[i][column]
            p2=table[i+1][column];p3=table[min(len(table)-1,i+2)][column]
            return .5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t)
    return table[0 if z<table[0][0] else -1][column]

def face_v(v,female):
    # Generated texture anchors are measured against the delivered image, not assumed.
    # Preserve the intended physical eye/nose/mouth locations by mapping the mesh UV.
    pairs=([(0,0),(.25,.262),(.42,.405),(.60,.575),(.72,.715),(1,1)] if female
           else [(0,0),(.25,.300),(.42,.420),(.60,.648),(.72,.725),(1,1)])
    for (a,b),(c,d) in zip(pairs,pairs[1:]):
        if a<=v<=c:return b+(v-a)/(c-a)*(d-b)
    return v
def make_face(name,female,rig,scale,offset):
    # Coordinates intentionally remain in the original generator's authoring space.
    # Texture anchors: eyes (+/-.0408, 1.624), nose (0,1.585), mouth (0,1.549).
    table=[(1.495,.004,.027),(1.506,.027,.039),(1.526,.051,.055),
           (1.549,.071 if female else .074,.068),(1.577,.083 if female else .086,.075),
           (1.612,.090 if female else .093,.079),(1.650,.093 if female else .096,.081),
           (1.685,.087,.079),(1.712,.066,.067),(1.735,.029,.034),(1.741,.003,.005)]
    rows=72;sides=112;vs=[];uv=[]
    for j in range(rows+1):
        z=table[0][0]+(table[-1][0]-table[0][0])*j/rows
        rx=interpolated_profile(z,table,1);ry=interpolated_profile(z,table,2)
        for k in range(sides+1):
            angle=-math.pi+math.tau*k/sides
            x=rx*math.sin(angle);front=math.cos(angle)
            # Broad, gently rounded anime facial plane: painted eyes stay frontal.
            y=-ry*math.copysign(abs(front)**(.43 if front>0 else 1),front)
            if front>0:
                nose=.0048*math.exp(-((x/.0078)**2+((z-1.586)/.010)**2))
                bridge=.0018*math.exp(-((x/.0065)**2+((z-1.603)/.018)**2))
                y-=(nose+bridge)*front
                y-=.0015*math.exp(-((abs(x)-.051)/.025)**2-((z-1.585)/.023)**2)
            vs.append((x,y,z))
            # Keep UVs continuous at the face/back material seam. EX_Skin samples its own constant UV.
            uv.append((.5+x/.204,face_v((z-1.495)/.215,female)))
    faces=[];indices=[]
    for j in range(rows):
        for k in range(sides):
            faces.append((j*(sides+1)+k,j*(sides+1)+k+1,(j+1)*(sides+1)+k+1,(j+1)*(sides+1)+k))
            angle=-math.pi+math.tau*(k+.5)/sides
            indices.append(0 if math.cos(angle)>.06 else 1)
    faces.extend([tuple(range(sides,-1,-1)),tuple(rows*(sides+1)+k for k in range(sides+1))]);indices.extend([1,1])
    skin=bpy.data.materials['EX_Skin']
    face=material('EX_Face',(1,1,1,1),TEXTURES/(name+'_Face_BaseColor.png'))
    return create_mesh(name+'_EX_Face',vs,faces,uv,face,rig,scale,offset,[face,skin],indices)

class Hair:
    def __init__(self):self.v=[];self.f=[];self.uv=[]
    def lock(self,points,widths,seed=0,depth=.16):
        controls=[Vector(p) for p in points];smooth=[];sizes=[]
        for j in range(len(controls)-1):
            a=controls[max(0,j-1)];b=controls[j];c=controls[j+1];d=controls[min(len(controls)-1,j+2)]
            for step in range(8):
                t=step/8
                smooth.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
                sizes.append(max(.00018,widths[j]*(1-t)+widths[j+1]*t))
        smooth.append(controls[-1]);sizes.append(.00018)
        start=len(self.v);sides=12;previous_width=None
        for j,(p,w) in enumerate(zip(smooth,sizes)):
            tangent=(smooth[min(j+1,len(smooth)-1)]-smooth[max(0,j-1)]).normalized()
            # Continuous radial frame avoids an abrupt UV/normal reversal at the temples.
            normal=Vector((p.x*.85,p.y-.005,max(.005,(p.z-1.63)*.5))).normalized()
            if previous_width is None:
                width_axis=tangent.cross(normal).normalized()
                if width_axis.length<.1:width_axis=tangent.cross(Vector((0,1,0))).normalized()
            else:
                # Parallel transport preserves orientation through a curved lock.
                # Rebuilding from the radial normal at every ring can flip at a bend.
                width_axis=(previous_width-tangent*previous_width.dot(tangent)).normalized()
            previous_width=width_axis
            normal=width_axis.cross(tangent).normalized()
            for k in range(sides+1):
                theta=math.tau*k/sides
                vertex=p+width_axis*w*math.cos(theta)+normal*w*depth*math.sin(theta)
                self.v.append(tuple(vertex));self.uv.append((k/sides+seed*.131,1-j/(len(smooth)-1)))
        for j in range(len(smooth)-1):
            for k in range(sides):
                a=start+j*(sides+1)+k
                self.f.append((a,a+1,a+sides+2,a+sides+1))
        self.f.append(tuple(start+k for k in range(sides,-1,-1)))
        self.f.append(tuple(start+(len(smooth)-1)*(sides+1)+k for k in range(sides+1)))

def make_hair(name,female,rig,scale,offset):
    hair=Hair()
    # Dense overlapping back/crown ribbons support separated fine outer strands.
    for sign in (-1,1):
        for i in range(13):
            t=i/12
            hair.lock([(sign*(.004+.009*t),-.008+.075*t,1.744-.016*t),
                       (sign*(.054+.025*t),.006+.071*t,1.725-.017*t),
                       (sign*(.102+.008*t),.008+.078*t,1.654-.026*t),
                       (sign*(.105+.013*math.sin(i*1.7)),.015+.084*t,1.576-.025*t)],
                       [.009,.021,.018,.0002],i+sign,depth=.22)
    # An off-centre part and diagonal, thin tapered fringe keep both eye apertures open.
    bangs=[
      ([(.021,-.024,1.747),(.001,-.075,1.712),(-.033,-.088,1.671),(-.069,-.081,1.646)],[.010,.023,.016,.0002]),
      ([(.027,-.020,1.748),(.034,-.073,1.718),(.052,-.090,1.686),(.079,-.071,1.641)],[.011,.023,.018,.0002]),
      ([(.016,-.027,1.745),(.007,-.084,1.700),(-.005,-.091,1.669),(-.016,-.092,1.642)],[.008,.016,.010,.0002]),
      ([(-.007,-.022,1.744),(-.033,-.079,1.704),(-.065,-.086,1.668),(-.092,-.061,1.628)],[.010,.020,.014,.0002]),
      ([(.041,-.014,1.742),(.070,-.058,1.703),(.090,-.065,1.662),(.097,-.044,1.616)],[.011,.021,.015,.0002]),
      ([(-.027,-.004,1.738),(-.064,-.055,1.706),(-.091,-.060,1.665),(-.106,-.032,1.608)],[.011,.021,.016,.0002]),
      ([(.019,-.045,1.742),(-.004,-.086,1.715),(-.041,-.096,1.680),(-.061,-.090,1.660)],[.006,.010,.009,.0002]),
      ([(.038,-.040,1.744),(.052,-.083,1.716),(.064,-.094,1.685),(.048,-.094,1.660)],[.006,.012,.011,.0002]),
      ([(-.035,-.028,1.733),(-.068,-.075,1.700),(-.089,-.079,1.664),(-.099,-.056,1.647)],[.007,.014,.012,.0002]),
      ([(.021,-.028,1.750),(.005,-.077,1.720),(-.017,-.096,1.685),(-.027,-.093,1.657)],[.005,.010,.008,.0002]),
    ]
    for i,(p,w) in enumerate(bangs):
        if not female:
            p=[(x*1.04,y,z+(.005 if j==len(p)-1 and i%2 else 0)) for j,(x,y,z) in enumerate(p)]
        hair.lock(p,w,i,depth=.14)
    # Fine, unequal fringe flyaways cross the brow boundary without hiding the irises.
    for i,(points,widths) in enumerate([
        ([(.023,-.053,1.740),(.008,-.095,1.706),(-.019,-.101,1.672),(-.031,-.099,1.648)], [.002,.0035,.0024,.0002]),
        ([(.033,-.040,1.742),(.040,-.093,1.706),(.033,-.099,1.674),(.019,-.097,1.650)], [.002,.003,.0021,.0002]),
        ([(-.009,-.049,1.737),(-.045,-.093,1.697),(-.068,-.094,1.670),(-.078,-.083,1.648)], [.002,.0032,.0024,.0002]),
        ([(.061,-.032,1.735),(.074,-.087,1.694),(.086,-.077,1.665),(.082,-.077,1.635)], [.0018,.0031,.0022,.0002]),
    ]):hair.lock(points,widths,i+41,depth=.15)
    for sign in (-1,1):
        for j in range(4):
            hair.lock([(sign*(.080+j*.004),-.009+j*.006,1.685-j*.009),
                       (sign*(.103+j*.003),-.029+j*.008,1.626-j*.005),
                       (sign*(.105+j*.004),-.019+j*.01,1.573-j*.007),
                       (sign*(.118+j*.004),-.016+j*.012,1.535-j*.010)],
                       [.010,.016,.011,.0002],j+8,depth=.18)
    if female:
        for i in range(27):
            t=i/26;x=-.109+.218*t;y=.059+.037*math.sin(t*math.pi)
            sway=.010*math.sin(i*1.91);end=1.11+.12*(.5+.5*math.sin(i*1.27))
            hair.lock([(x*.65,y*.70,1.714),(x,y,1.626),(x*1.15+sway,y+.034,1.491),
                       (x*1.25-sway,y+.042,1.347),(x*1.17+sway,y+.029,1.223),
                       (x*1.44+sway,y+.018,end)],
                       [.009,.017,.018,.016,.011,.0002],i,depth=.22)
        for sign in (-1,1):
            for i in range(4):
                hair.lock([(sign*(.087+i*.007),-.004+i*.005,1.631-i*.009),
                           (sign*(.118+i*.006),-.030+i*.005,1.539),
                           (sign*(.12+i*.008),-.010+i*.008,1.441),
                           (sign*(.151+i*.006),-.006+i*.008,1.371),
                           (sign*(.137+i*.01),-.035+i*.007,1.303),
                           (sign*(.164+i*.010),-.021+i*.007,1.235+i*.021)],
                           [.009,.014,.013,.012,.008,.0002],i,depth=.15)
        # One fine, sideways flyaway rises less than 1 cm above the main crown.
        hair.lock([(-.017,.020,1.739),(-.005,.005,1.755),(.025,-.012,1.759),(.051,-.020,1.748)],
                   [.0012,.0017,.0010,.0002],0,depth=.18)
    else:
        for i in range(21):
            angle=math.pi*i/20;x=.098*math.cos(angle);y=.083*math.sin(angle)+.021
            hair.lock([(x*.4,y*.55,1.728),(x,y,1.661),(x*1.13,y+.006,1.592),
                       (x*1.27,y+.016,1.540+.028*math.sin(i*1.72))],
                       [.009,.019,.014,.0002],i,depth=.21)
        # Unequal low swept crown tips, avoiding a repeated antenna silhouette.
        for i,(points,widths) in enumerate([
            ([(-.046,.014,1.729),(-.055,.006,1.744),(-.078,-.009,1.750),(-.099,-.021,1.738)],[.003,.005,.003,.0002]),
            ([(-.011,.008,1.739),(-.010,-.002,1.752),(-.033,-.017,1.758),(-.058,-.026,1.746)],[.002,.003,.002,.0002]),
            ([(.024,.015,1.740),(.040,.014,1.752),(.063,-.002,1.751),(.085,-.014,1.735)],[.002,.003,.002,.0002]),
        ]):hair.lock(points,widths,i,depth=.14)
    # A smooth underlying cap closes crown gaps beneath the individual locks.
    cap=[(1.658,.098,.084),(1.687,.096,.085),(1.718,.075,.069),(1.742,.041,.044),(1.751,.002,.009)]
    start=len(hair.v);rows=28;sides=72
    for j in range(rows+1):
        z=cap[0][0]+(cap[-1][0]-cap[0][0])*j/rows
        rx=interpolated_profile(z,cap,1);ry=interpolated_profile(z,cap,2)
        for k in range(sides+1):
            angle=math.tau*k/sides
            hair.v.append((rx*math.cos(angle),.007+ry*math.sin(angle),z))
            hair.uv.append((k/sides*3,j/rows))
    for j in range(rows):
        for k in range(sides):
            a=start+j*(sides+1)+k
            hair.f.append((a,a+1,a+sides+2,a+sides+1))
    mat=material('EX_Hair',(1,1,1,1),TEXTURES/'AshBlond_Hair_BaseColor.png')
    return create_mesh(name+'_EX_Hair',hair.v,hair.f,hair.uv,mat,rig,scale,offset)

def export_and_render(name,female):
    source=BASE/(name+'.blend')
    if not source.exists():shutil.copy2(SOURCES/(name+'.blend'),source)
    bpy.ops.wm.open_mainfile(filepath=str(source))
    rig=bpy.data.objects[name+'_Rig']
    head=rig.data.bones['Head']
    scale=head.length/.204;offset=head.head_local.z-1.50*scale
    removed=remove_old_face(name,scale,offset)
    # Source concept uses a pale neutral skin tone; no fake metal skin/strong specular.
    skin=material('EX_Skin',(1,1,1,1),TEXTURES/(name+'_Face_BaseColor.png'))
    sample=skin.node_tree.nodes.new('ShaderNodeCombineXYZ')
    sample.inputs['X'].default_value=.025;sample.inputs['Y'].default_value=.88
    for node in skin.node_tree.nodes:
        if node.type=='TEX_IMAGE':skin.node_tree.links.new(sample.outputs['Vector'],node.inputs['Vector'])
    make_face(name,female,rig,scale,offset);make_hair(name,female,rig,scale,offset)
    meshes=[o for o in bpy.data.objects if o.type=='MESH' and o.parent==rig]
    assert len(rig.data.bones)==52
    for obj in meshes:
        for vertex in obj.data.vertices:
            assert vertex.groups and abs(sum(g.weight for g in vertex.groups)-1)<.0001
    rig.data.pose_position='POSE'
    for bone in rig.pose.bones:bone.rotation_quaternion=Quaternion()
    bpy.context.view_layer.update()
    if not PREVIEW:
        # Portable source files reference the delivered project texture directory.
        for image in bpy.data.images:
            if image.source=='FILE' and Path(bpy.path.abspath(image.filepath)).parent==TEXTURES:
                image.pack()
                image.filepath='//../../../Assets/Orbis/Game/Island/Textures/Explorers/'+Path(image.filepath).name
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCES/(name+'.blend')),relative_remap=False)
        bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
        for obj in meshes:obj.select_set(True)
        bpy.context.view_layer.objects.active=rig
        bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,
            object_types={'MESH','ARMATURE'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,mesh_smooth_type='FACE',
            add_leaf_bones=False,primary_bone_axis='Y',secondary_bone_axis='X',bake_anim=False,path_mode='AUTO')
    for side,sign in (('Left',1),('Right',-1)):
        bone=rig.pose.bones[side+'UpperArm'];bone.rotation_mode='QUATERNION'
        rest=bone.bone.matrix_local.to_quaternion()
        bone.rotation_quaternion=rest.inverted() @ Quaternion(Vector((0,1,0)),sign*math.radians(65)) @ rest
    bpy.context.view_layer.update()
    scene=bpy.context.scene;camera=scene.camera
    scene.render.engine='CYCLES';scene.cycles.samples=8 if '--fast-render' in sys.argv else 32;scene.cycles.use_denoising=True
    scene.render.threads_mode='FIXED';scene.render.threads=12
    scene.view_settings.view_transform='AgX'
    scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
    for suffix,location,look,ortho,size in [
        ('Face',(0,-2.8,1.61*scale+offset),(0,-.009,1.604*scale+offset),.34,(1000,1000)),
        ('Face_ThreeQuarter',(.85,-2.8,1.66*scale+offset),(0,-.002,1.603*scale+offset),.38,(1000,1000)),
        ('Full',(1.3,-5.5,2.2),(0,0,.93),2.08,(1100,1400)),
    ]:
        camera.location=location;aim(camera,look);camera.data.ortho_scale=ortho
        scene.render.resolution_x,scene.render.resolution_y=size
        if '--fast-render' in sys.argv:
            scene.render.resolution_x=600;scene.render.resolution_y=round(600*size[1]/size[0])
        scene.render.filepath=str(RESULTS/('FacePolish_'+name+'_'+suffix+'.png'))
        bpy.ops.render.render(write_still=True)
    report={'name':name,'boneCount':len(rig.data.bones),'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),
            'originalSource':str(source),'faceUv':'u=.5+x/.204; v is piecewise landmark-remapped from (z-1.495)/.215; skin sample (.025,.88)',
            'hairUv':'U across lock; V=1 root and V=0 tip','removedVertices':removed,
            'textures':[str(TEXTURES/(name+'_Face_BaseColor.png')),str(TEXTURES/'AshBlond_Hair_BaseColor.png')],
            'fbx':str(OUT/(name+'.fbx')) if not PREVIEW else None,'textureRequiredForExport':True}
    (RESULTS/('FacePolish_'+name+'_Model.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('ORBIS_FACE_POLISH='+json.dumps(report),flush=True)

for character,is_female in (('Stella',True),('Polaris',False)):
    if '--only-stella' in sys.argv and character!='Stella':continue
    if '--only-polaris' in sys.argv and character!='Polaris':continue
    export_and_render(character,is_female)
