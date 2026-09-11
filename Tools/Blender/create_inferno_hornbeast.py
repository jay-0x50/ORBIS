"""Original procedural sculpt study based on Assets/Img/불보스.png.

Run with Blender 5.2 --background --factory-startup --python this_file.py.
No reference pixels are uploaded or copied into textures. Mesh geometry is original.
The sample is a first silhouette/armor study, not a production-ready exact replica.
Forward in Blender is -Y; FBX axis conversion uses Unity's -Z/Y configuration.
"""
from pathlib import Path
import bpy
import math
import json
import random
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Orbis/Game/Island/Models/InfernoHornbeast.fbx"
BLEND = ROOT / "Tools/Blender/Sources/InfernoHornbeast.blend"
PNG = ROOT / "TestResults/Island_InfernoHornbeast.png"
STATS = ROOT / "TestResults/Island_InfernoHornbeast.json"
for p in (OUT, BLEND, PNG, STATS):
    p.parent.mkdir(parents=True, exist_ok=True)

random.seed(714)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
for datablocks in (bpy.data.meshes, bpy.data.materials):
    for data in list(datablocks):
        if data.users == 0:
            datablocks.remove(data)

# Six draw groups retain their albedo/emission when converted to the project toon shader.
MATERIALS = [
    ("IB_Basalt",      (0.031, 0.043, 0.058, 1), 0.24, 0.76, 0.0),
    ("IB_SlateEdge",   (0.092, 0.116, 0.145, 1), 0.32, 0.60, 0.0),
    ("IB_Magma",       (1.000, 0.070, 0.006, 1), 0.05, 0.50, 3.2),
    ("IB_HeatCore",    (1.000, 0.450, 0.035, 1), 0.08, 0.34, 5.5),
    ("IB_CompassGold", (0.680, 0.360, 0.070, 1), 0.75, 0.34, 0.0),
    ("IB_BurntIron",   (0.185, 0.212, 0.241, 1), 0.48, 0.47, 0.0),
]
mats = []
for name, color, metal, rough, emission in MATERIALS:
    m = bpy.data.materials.new(name)
    m.diffuse_color = color
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Emission Color"].default_value = color
    bsdf.inputs["Emission Strength"].default_value = emission
    m["OrbisEmissionStrength"] = emission
    m["OrbisEmissionColor"] = list(color)
    mats.append(m)

verts_by_mat = [[] for _ in mats]
faces_by_mat = [[] for _ in mats]

def geom(mi, vs, fs):
    first = len(verts_by_mat[mi])
    verts_by_mat[mi].extend([tuple(v) for v in vs])
    faces_by_mat[mi].extend([tuple(first + i for i in f) for f in fs])

def normal_basis(direction):
    n = Vector(direction).normalized()
    reference = Vector((0, 0, 1)) if abs(n.z) < .92 else Vector((0, 1, 0))
    u = n.cross(reference).normalized()
    v = n.cross(u).normalized()
    return n, u, v

def loft(points, radii, sides=8, mi=0, alt=None, elliptic=1, rotation=0):
    """Custom tapered polyline tube; rings use a stable transported frame."""
    pts = [Vector(p) for p in points]
    vs = []
    previous_u = None
    for j, (p, r) in enumerate(zip(pts, radii)):
        tangent = (pts[min(j+1,len(pts)-1)] - pts[max(0,j-1)]).normalized()
        if previous_u is None:
            _, u, v = normal_basis(tangent)
        else:
            u = (previous_u - tangent * previous_u.dot(tangent)).normalized()
            v = tangent.cross(u).normalized()
        previous_u = u
        rx, ry = (r, r * elliptic) if isinstance(r, (int, float)) else r
        for k in range(sides):
            a = rotation + k * math.tau / sides
            # Slight alternating facets keep long shapes rocky, not smooth capsules.
            f = 1.0 if (k+j)%3 else .94
            vs.append(p + f * (math.cos(a)*rx*u + math.sin(a)*ry*v))
    geom(mi, vs, [tuple(range(sides-1,-1,-1))])
    for j in range(len(pts)-1):
        for k in range(sides):
            face = (j*sides+k, j*sides+(k+1)%sides,
                    (j+1)*sides+(k+1)%sides, (j+1)*sides+k)
            geom(alt if alt is not None and k%4 == 1 else mi, vs, [face])
    geom(mi, vs, [tuple((len(pts)-1)*sides+k for k in range(sides))])

def line(points, thickness=.022, mi=2):
    loft(points, [thickness]*len(points), sides=4, mi=mi)

def plate(center, direction, length=.7, width=.6, height=.12, angle=0, mi=0, glow=True):
    """Irregular six-sided basalt armor slab, raised ridge and beveled skirt."""
    c = Vector(center)
    n = Vector(direction).normalized()
    # Main axis follows the spine where possible, for layered rather than random scales.
    u = Vector((0, 1, 0))
    u = (u - n*u.dot(n)).normalized() if abs(n.y) < .95 else Vector((1, 0, 0))
    v = n.cross(u).normalized()
    ca, sa = math.cos(angle), math.sin(angle)
    u, v = ca*u + sa*v, -sa*u + ca*v
    outline = [(-.54,-.34),(-.62,.10),(-.20,.53),(.40,.40),(.56,-.07),(.13,-.46)]
    top = [c+u*(x*length)+v*(y*width) for x,y in outline]
    base = [p-n*.055 for p in top]
    peak = c+n*height+u*(length*.04)+v*(width*.04)
    vs = top+base+[peak]
    for j in range(6):
        geom(mi if j%3 else 1, vs, [(j,(j+1)%6,12)])
        geom(1 if j%2 else mi, vs, [(j,j+6,(j+1)%6+6,(j+1)%6)])
    geom(mi, vs, [tuple(range(11,5,-1))])
    if glow:
        # Only two edges glow; selective cracks avoid the uniformly wireframe look.
        a = [top[j]-n*.023 for j in (3,4,5)]
        line(a, .018, 2)
        if random.random() < .24:
            line([a[1], a[1]*.66+peak*.34], .012, 3)

def spike(points, base, mi=0):
    radii = [base*(1-i/(len(points)-1))**.75+.009 for i in range(len(points))]
    loft(points,radii,6,mi,alt=1,rotation=.2)

def ellipsoid(center, radii, mi=0, rows=6, sides=10):
    c = Vector(center)
    rx,ry,rz = radii
    vs = []
    for j in range(rows+1):
        a = math.pi*j/rows
        for k in range(sides):
            b = math.tau*k/sides + (j%2)*.13
            vs.append(c+Vector((rx*math.sin(a)*math.cos(b),ry*math.sin(a)*math.sin(b),rz*math.cos(a))))
    fs=[]
    for j in range(rows):
        for k in range(sides):
            fs.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
    geom(mi,vs,fs)

# Bulky rhino-like shoulder and narrow waist are a custom loft, not a primitive body.
body_path=[(0,-1.05,1.51),(0,-.68,1.65),(0,-.15,1.63),(0,.45,1.53),(0,.98,1.46),(0,1.35,1.45)]
body_r=[(.48,.59),(.94,.84),(1.00,.79),(.88,.68),(.78,.70),(.47,.47)]
loft(body_path,body_r,12,0,alt=1)
# Armor follows the shoulder-to-haunch silhouette, leaving hot joints below.
for row,y in enumerate((-.83,-.39,.09,.55,.97)):
    rx=(.90,.99,.96,.87,.76)[row]
    rz=(.80,.78,.72,.68,.62)[row]
    z=(1.65,1.65,1.60,1.53,1.48)[row]
    for j in range(9):
        theta=-.25 + j*(math.pi+.5)/8
        x=rx*math.cos(theta)
        zz=z+rz*math.sin(theta)
        direction=Vector((math.cos(theta),-.04,math.sin(theta))).normalized()
        plate((x,y+(.10 if j%2 else 0),zz),direction,
              length=.76,width=.70,height=.12+random.random()*.07,
              angle=random.uniform(-.12,.12),glow=(j+row)%3 != 0)
# Underbelly fissures and overlapping ventral guards.
for y in (-.65,-.2,.25,.7):
    plate((0,y,.96),(0,0,-1),.65,.75,.08,glow=False)
    line([(-.38,y,.96),(0,y-.04,.91),(.38,y,.96)],.019)

# Four weight-bearing, bent armored limbs. Feet have individual custom talons.
for side in (-1,1):
    x=.78*side
    front=[(x,-.79,1.68),(x*1.16,-1.02,1.05),(x*1.18,-.99,.48),(x*1.15,-1.20,.21)]
    hind=[(x*.89,.97,1.47),(x*1.15,1.31,.90),(x*1.06,.88,.48),(x*1.12,.75,.21)]
    for leg_index,path in enumerate((front,hind)):
        loft(path,[.42,.34,.25,.27],8,0,alt=1,elliptic=1.1)
        knee=Vector(path[1])
        plate(knee+Vector((side*.23,-.08,.10)),(side,-.55,.25),.65,.66,.18,angle=.2)
        plate(Vector(path[2])+Vector((side*.16,-.05,0)),(side,-.8,.1),.50,.44,.12,angle=-.4)
        # Hot flexure joints are partially enclosed by basalt armor.
        line([knee+Vector((side*.14,-.27,.07)),knee+Vector((0,-.33,0)),knee+Vector((-side*.15,-.25,-.03))],.033)
        foot=Vector(path[-1])
        ellipsoid(foot+Vector((0,-.10,-.035)),(.34,.43,.18),0,4,8)
        for toe in (-1,0,1):
            start=foot+Vector((toe*.19,-.30,-.03))
            end=start+Vector((toe*.028,-.36,-.115))
            loft([start,start+Vector((0,-.17,-.04)),end],[.12,.075,.007],5,5)
            line([start+Vector((-.065,.05,.085)),start+Vector((.065,.04,.085))],.014)
        for k in range(2):
            off=Vector((side*.30,.10*k,.13))
            spike([Vector(path[0])+off,Vector(path[0])+off+Vector((side*.29,.1,.20)),
                   Vector(path[0])+off+Vector((side*.49,.23,.27))],.16)

# Neck/head: sharp cheek wedges, brow, low broad muzzle, pronounced teeth.
loft([(0,-1.04,1.57),(0,-1.43,1.52),(0,-1.82,1.32),(0,-2.16,1.19)],
     [(.54,.55),(.62,.53),(.54,.39),(.41,.25)],10,0,alt=1)
plate((0,-1.62,1.94),(0,-.35,1),.75,.92,.17,angle=.03)
plate((0,-1.98,1.51),(0,-.2,1),.56,.64,.11,glow=False)
for side in (-1,1):
    plate((side*.50,-1.73,1.42),(side,-.35,.20),.73,.64,.14,angle=side*.2)
    plate((side*.35,-2.08,1.10),(side,-.55,-.2),.62,.48,.10,glow=False)
    # Narrow inset luminous eyes, overhung by dark brows.
    line([(side*.465,-1.75,1.64),(side*.41,-1.95,1.55),(side*.31,-2.02,1.53)],.040,3)
    spike([(side*.53,-1.75,1.74),(side*.45,-1.94,1.70),(side*.32,-2.10,1.60)],.13)
    line([(side*.39,-2.19,1.13),(side*.43,-2.04,1.04),(side*.48,-1.82,1.02)],.020,2)
    for i in range(3):
        y=-2.15+i*.12
        spike([(side*.37,y,1.09),(side*.39,y-.04,.97)],.054,5)
# Nose armor and two ember nostrils.
plate((0,-2.27,1.28),(0,-1,.20),.56,.33,.07,glow=False)
for side in(-1,1):
    line([(side*.22,-2.292,1.26),(side*.12,-2.306,1.26)],.031,2)

# Large recurved paired horns are the primary visual read from the reference.
for side in(-1,1):
    h=[(side*.48,-1.54,1.77),(side*.80,-1.73,1.72),(side*1.08,-2.02,1.62),
       (side*1.22,-2.34,1.67),(side*1.23,-2.59,1.88),
       (side*1.16,-2.75,2.16),(side*1.04,-2.81,2.43),(side*.91,-2.76,2.65)]
    hr=[.29,.30,.265,.21,.16,.12,.071,.008]
    loft(h,hr,9,0,alt=5,rotation=.12)
    # Sparse molten crossbands accent each horn's angular sweep.
    for j in (1,3,5):
        p=Vector(h[j]); tangent=(Vector(h[j+1])-Vector(h[j-1])).normalized()
        _,u,v=normal_basis(tangent)
        line([p+(u*math.cos(t)+v*math.sin(t))*hr[j]*1.015
              for t in (0,.6,1.2,1.8,2.4,3.0)],.015,2)
    loft(h[-3:],hr[-3:],9,2,alt=3)
    spike([(side*.44,-1.38,1.95),(side*.57,-1.14,2.24),(side*.56,-.95,2.31)],.16)

# Broken dorsal ridge, alternating large slabs and smaller offset spines.
for i,y in enumerate((-.76,-.29,.20,.68,1.08)):
    z=(2.38,2.45,2.30,2.17,2.07)[i]
    height=(.48,.63,.60,.51,.42)[i]
    spike([(0,y,z-.12),(.03,y+.15,z+height*.56),(0,y+.43,z+height)],.25 if i<3 else .19)
    for side in(-1,1):
        spike([(side*.46,y,z-.20),(side*.63,y+.17,z+.08),
               (side*.75,y+.31,z+.20)],.14)
    line([(-.13,y-.08,z-.055),(0,y-.12,z+.025),(.13,y-.06,z-.03)],.025)

# Segmented raised tail: no single smooth cylinder; plated sides and asymmetric barb.
tail=[(0,1.19,1.47),(0,1.63,1.40),(.04,2.08,1.53),(.13,2.49,1.84),
      (.23,2.70,2.25),(.27,2.69,2.67),(.23,2.45,3.02),(.15,2.12,3.27)]
radii=[.40,.37,.32,.27,.235,.20,.145,.035]
loft(tail,radii,9,0,alt=1)
for i in range(1,len(tail)-1):
    p=Vector(tail[i]); tangent=(Vector(tail[i+1])-Vector(tail[i-1])).normalized()
    _,u,v=normal_basis(tangent)
    for k in range(5):
        a=k*math.tau/5
        n=(u*math.cos(a)+v*math.sin(a)).normalized()
        plate(p+n*radii[i]*.93,n,.51,.48 if i<4 else .36,.09,angle=.05*k,glow=k%2==0)
    if i<6:
        spike([p+Vector((0,.06,radii[i]*.8)),p+Vector((0,.31,radii[i]+.13)),
               p+Vector((0,.52,radii[i]+.20))],.13)
spike([tail[-2],tail[-1],(.05,1.78,3.42)],.15)
loft([(.18,2.26,3.12),(.15,2.12,3.27),(.05,1.78,3.42)],[.084,.064,.003],6,2,alt=3)

def ring(center, normal, outer, inner, depth, mi, segments=32):
    c=Vector(center); n,u,v=normal_basis(normal)
    vs=[]
    for d,r in ((-depth/2,outer),(depth/2,outer),(-depth/2,inner),(depth/2,inner)):
        for j in range(segments):
            a=math.tau*j/segments
            vs.append(c+n*d+r*(math.cos(a)*u+math.sin(a)*v))
    fs=[]
    for j in range(segments):
        k=(j+1)%segments
        fs.extend([(j,k,segments+k,segments+j),
                   (2*segments+j,3*segments+j,3*segments+k,2*segments+k),
                   (segments+j,segments+k,3*segments+k,3*segments+j),
                   (j,2*segments+j,2*segments+k,k)])
    geom(mi,vs,fs)

# Both shoulder rings ensure the authored weakpoint reads from either approach.
for side in (-1,1):
    c=Vector((side*1.005,-.57,1.79))
    n=Vector((side,-.08,.05)).normalized()
    _,u,v=normal_basis(n)
    ring(c,n,.415,.31,.12,5)
    ring(c+n*.075,n,.375,.31,.08,4)
    ring(c+n*.035,n,.278,.247,.035,2)
    # Eight engraved compass spokes, longer cardinal points and ember hub.
    for j in range(8):
        a=j*math.tau/8
        radial=u*math.cos(a)+v*math.sin(a)
        tangent=-u*math.sin(a)+v*math.cos(a)
        r0=.12 if j%2==0 else .21
        r1=.345 if j%2==0 else .315
        p0=c+n*.095+radial*r0
        p1=c+n*.10+radial*r1
        width=.033 if j%2==0 else .020
        geom(4,[p0-tangent*width,p0+tangent*width,p1+radial*.048],[(0,1,2)])
        line([c+n*.075+radial*.36,c+n*.075+radial*.41],.020,4)
    ellipsoid(c+n*.105,(.070,.10,.10),3,5,10)
    ring(c+n*.11,n,.12,.095,.04,4,20)

# Normalize only generated vertices: feet at origin, overall height 3 metres.
all_vertices=[Vector(v) for vs in verts_by_mat for v in vs]
lowest=min(v.z for v in all_vertices)
highest=max(v.z for v in all_vertices)
factor=3.0/(highest-lowest)
root=bpy.data.objects.new("InfernoHornbeast",None)
bpy.context.collection.objects.link(root)
root["Reference"]="Assets/Img/불보스.png; main quadruped study"
root["Authoring"]="Original procedural custom meshes; static first sculpt"
root["NominalHeightMetres"]=3.0
meshes=[]
for mi,raw in enumerate(verts_by_mat):
    if not raw:
        continue
    mesh=bpy.data.meshes.new(MATERIALS[mi][0]+"_Geometry")
    coordinates=[(v[0]*factor,v[1]*factor,(v[2]-lowest)*factor) for v in raw]
    mesh.from_pydata(coordinates,[],faces_by_mat[mi])
    mesh.update()
    obj=bpy.data.objects.new(MATERIALS[mi][0],mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent=root
    obj.data.materials.append(mats[mi])
    # Recalculate surface normals while preserving deliberate sharp facets.
    bpy.context.view_layer.objects.active=obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=.00001)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)
    meshes.append(obj)

triangles=0
for obj in meshes:
    obj.data.calc_loop_triangles()
    triangles+=len(obj.data.loop_triangles)
assert len(meshes)<=6,(len(meshes),"material draw group budget")
assert triangles<30000,(triangles,"triangle budget")
bounds=[obj.matrix_world@v.co for obj in meshes for v in obj.data.vertices]
dimensions=[max(v[i] for v in bounds)-min(v[i] for v in bounds) for i in range(3)]

bpy.ops.object.select_all(action="DESELECT")
root.select_set(True)
for obj in meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT),use_selection=True,
    object_types={"EMPTY","MESH"},axis_forward="-Z",axis_up="Y",
    apply_unit_scale=True,apply_scale_options="FBX_SCALE_UNITS",
    use_mesh_modifiers=True,mesh_smooth_type="FACE",
    add_leaf_bones=False,bake_anim=False,path_mode="AUTO")
# Studio presentation objects are intentionally excluded from FBX.
studio=bpy.data.collections.new("Studio (not exported)")
bpy.context.scene.collection.children.link(studio)
def studio_object(name,data):
    obj=bpy.data.objects.new(name,data);studio.objects.link(obj);return obj
floor_mat=bpy.data.materials.new("Studio Warm Slate")
floor_mat.diffuse_color=(.13,.16,.185,1)
floor_mat.use_nodes=True
floor_bsdf=floor_mat.node_tree.nodes.get("Principled BSDF")
floor_bsdf.inputs["Base Color"].default_value=(.13,.16,.185,1)
floor_bsdf.inputs["Roughness"].default_value=.84
floor_mesh=bpy.data.meshes.new("Studio Floor")
floor_mesh.from_pydata([(-200,-200,-.025),(200,-200,-.025),(200,200,-.025),(-200,200,-.025)],[],[(0,1,2,3)])
floor=studio_object("Studio Floor (not exported)",floor_mesh)
floor.data.materials.append(floor_mat)
def aim(obj,point):
    obj.rotation_euler=(Vector(point)-obj.location).to_track_quat("-Z","Y").to_euler()
def area(name,loc,power,color,size):
    d=bpy.data.lights.new(name,"AREA");d.energy=power;d.color=color;d.shape="DISK";d.size=size
    obj=studio_object(name,d);obj.location=loc;aim(obj,(0,0,1.3))
area("Warm key",(4,-5,7),1500,(1,.79,.59),5)
area("Cool fill",(-4,-1,4),1050,(.40,.62,1),4)
area("Blue rim",(1,5,6),1900,(.42,.67,1),3.5)
camera=studio_object("Hero Camera",bpy.data.cameras.new("Hero Camera"))
camera.location=(6.2,-7.6,4.4)
aim(camera,(0,-.05,1.45))
camera.data.type="ORTHO";camera.data.ortho_scale=6.15
scene=bpy.context.scene;scene.camera=camera
scene.world.color=(.15,.15,.15)
scene.world.use_nodes=True
scene.world.node_tree.nodes["Background"].inputs[0].default_value=(.085,.11,.15,1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value=.36
scene.render.engine="CYCLES"
scene.cycles.samples=48
scene.cycles.use_denoising=True
scene.render.threads_mode="FIXED";scene.render.threads=12
scene.render.resolution_x=1400;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.render.image_settings.file_format="PNG"
scene.render.filepath=str(PNG)
scene.view_settings.view_transform="AgX"
scene.render.film_transparent=False
# Blender 5 uses node-group compositor; skip glow postprocessing for portable raw evidence.
scene["PresentationNote"]="Raw lit Blender render; no image-to-mesh or painted image compositing"
bpy.ops.object.select_all(action="DESELECT")
root.select_set(True);bpy.context.view_layer.objects.active=root
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
statistics={
    "name":"InfernoHornbeast","meshGroups":len(meshes),"triangles":triangles,
    "dimensionsBlenderMetres":dimensions,"source":str(BLEND),"fbx":str(OUT),"render":str(PNG),
    "materials":[{"name":n,"baseColor":list(c),"metallic":m,"roughness":r,"emissionStrength":e}
                 for n,c,m,r,e in MATERIALS],
    "limitations":["Static unrigged first sculpt; no attack animation.",
                   "Reference-inspired custom geometry, not exact high-detail reconstruction.",
                   "Toon look requires the project's runtime material conversion."],
}
STATS.write_text(json.dumps(statistics,indent=2),encoding="utf-8")
print("ORBIS_MODEL_STATS="+json.dumps(statistics))
bpy.ops.render.render(write_still=True)
print("ORBIS_MODEL_COMPLETE")
