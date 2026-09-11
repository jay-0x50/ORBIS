"""Original Orbis village architecture. No commercial-game or external model input.

Blender -b --python Tools/WorldDev/build_architecture.py
Five architectural designs, two LODs, shared six-material palette, metre UVs.
Blender authoring front is -Y. The native FBX imports in Unity with front -Z;
WorldLandmarkBuilder rotates every LOD child 180 degrees around Y so the final
usable prefab has front +Z. Do not apply that prefab correction in this exporter.
Windmill Rotor is a separate child and rotates around its own local Z axis.
"""
from pathlib import Path
import json, math, hashlib, sys
import bpy, bmesh
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Orbis/Game/World/Architecture'
MODELS=OUT/'Models';EVIDENCE=ROOT/'TestResults/WorldDev'
PALETTE={
 'Architecture_Ivory':(.79,.755,.65,1),
 'Architecture_Timber':(.245,.175,.115,1),
 'Architecture_Slate':(.17,.255,.315,1),
 'Architecture_Stone':(.43,.445,.40,1),
 'Architecture_Brass':(.60,.465,.245,1),
 'Architecture_Glass':(.095,.205,.245,1),
}
IVORY,TIMBER,SLATE,STONE,BRASS,GLASS=range(6)
MATERIALS=[]

def srgb_linear(x):return x/12.92 if x<=.04045 else ((x+.055)/1.055)**2.4
def material_setup():
 for name,color in PALETTE.items():
  mat=bpy.data.materials.new(name);mat.diffuse_color=tuple(srgb_linear(v) for v in color[:3])+(1,)
  mat.use_nodes=True;shader=mat.node_tree.nodes.get('Principled BSDF');shader.inputs['Base Color'].default_value=mat.diffuse_color
  shader.inputs['Roughness'].default_value=.78 if name!='Architecture_Glass' else .35
  MATERIALS.append(mat)

class Shape:
 def __init__(self):self.vertices=[];self.faces=[];self.materials=[]
 def solid(self,vertices,faces,material):
  start=len(self.vertices);self.vertices.extend(tuple(v) for v in vertices)
  self.faces.extend(tuple(start+i for i in f) for f in faces);self.materials.extend([material]*len(faces))
 def box(self,center,size,material,rotation=None):
  center=Vector(center);sx,sy,sz=[v*.5 for v in size]
  points=[Vector((x*sx,y*sy,z*sz)) for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
  if rotation is not None:points=[rotation@p for p in points]
  self.solid([center+p for p in points],[(3,2,1,0),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],material)
 def beam(self,a,b,width,depth,material):
  a,b=Vector(a),Vector(b);direction=b-a;rotation=direction.to_track_quat('Z','Y').to_matrix()
  self.box((a+b)*.5,(width,depth,direction.length),material,rotation)
 def rings(self,center,profile,material,sides=12,phase=0):
  # A closed loft supports beveled plinths, tapered towers and sculpted roof eaves.
  cx,cy,cz=center;vertices=[]
  for radius,z in profile:
   for i in range(sides):
    a=phase+i*math.tau/sides;vertices.append((cx+radius*math.cos(a),cy+radius*math.sin(a),cz+z))
  faces=[tuple(reversed(range(sides))),tuple((len(profile)-1)*sides+i for i in range(sides))]
  for k in range(len(profile)-1):
   for i in range(sides):faces.append((k*sides+i,k*sides+(i+1)%sides,(k+1)*sides+(i+1)%sides,(k+1)*sides+i))
  self.solid(vertices,faces,material)
 def plate(self,corners,thickness,material):
  normal=(Vector(corners[1])-Vector(corners[0])).cross(Vector(corners[2])-Vector(corners[0])).normalized()
  if normal.z<0:normal=-normal
  vertices=[Vector(p) for p in corners]+[Vector(p)-normal*thickness for p in corners]
  self.solid(vertices,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],material)
 def torus(self,center,radius,tube,material,normal='Z',segments=24):
  cx,cy,cz=center;vertices=[]
  for i in range(segments):
   a=i*math.tau/segments
   for j in range(4):
    b=j*math.tau/4;x=(radius+math.cos(b)*tube)*math.cos(a);y=(radius+math.cos(b)*tube)*math.sin(a);z=math.sin(b)*tube
    if normal=='Y':y,z=z,y
    vertices.append((cx+x,cy+y,cz+z))
  faces=[]
  for i in range(segments):
   for j in range(4):faces.append((i*4+j,((i+1)%segments)*4+j,((i+1)%segments)*4+(j+1)%4,i*4+(j+1)%4))
  self.solid(vertices,faces,material)
 def mesh(self,name,location=(0,0,0),bevel=0):
  mesh=bpy.data.meshes.new(name);mesh.from_pydata(self.vertices,[],self.faces);mesh.update()
  for material in MATERIALS:mesh.materials.append(material)
  for polygon,slot in zip(mesh.polygons,self.materials):polygon.material_index=slot
  bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
  obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.location=location
  bpy.context.view_layer.objects.active=obj;obj.select_set(True)
  if bevel:
   mod=obj.modifiers.new('Small architectural edge chamfers','BEVEL');mod.width=bevel;mod.segments=1;mod.affect='EDGES';mod.angle_limit=.55
   bpy.ops.object.modifier_apply(modifier=mod.name)
  modifier=obj.modifiers.new('Explicit export triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=modifier.name)
  uv=obj.data.uv_layers.new(name='UV0_Metres')
  # Local metres projected on each face's dominant plane: repeating textures retain their scale.
  for polygon in obj.data.polygons:
   dominant=max(range(3),key=lambda i:abs(polygon.normal[i]));axes=[i for i in range(3) if i!=dominant]
   for loop_index in polygon.loop_indices:
    p=obj.data.vertices[obj.data.loops[loop_index].vertex_index].co;uv.data[loop_index].uv=(p[axes[0]]*.5,p[axes[1]]*.5)
  obj.select_set(False);return obj

def triangle_count(obj):return sum(len(p.vertices)-2 for p in obj.data.polygons)
def transform_point(point,origin,yaw):return Vector(origin)+Matrix.Rotation(yaw,3,'Z')@Vector(point)

def front_window(shape,x,y,z,width=1.05,height=1.25,lod=0,yaw=0,shutters=True):
 origin=(x,y,z);rotation=Matrix.Rotation(yaw,3,'Z')
 def box(c,size,mat):shape.box(transform_point(c,origin,yaw),size,mat,rotation)
 box((0,.015,height*.5),(width,.10,height),GLASS)
 for px in (-width*.5,width*.5):box((px,-.075,height*.5),(.115,.19,height+.18),TIMBER)
 for pz in (0,height):box((0,-.075,pz),(width+.2,.19,.115),TIMBER)
 box((0,-.12,height*.5),(.065,.08,height),IVORY);box((0,-.12,height*.49),(width,.08,.065),IVORY)
 box((0,-.14,-.075),(width+.36,.38,.14),STONE)
 if shutters:
  for side in (-1,1):
   sx=side*(width*.5+.23);box((sx,.01,height*.5),(.32,.12,height*.96),TIMBER)
   if not lod:
    for j in range(4):box((sx,-.06,height*(j+1)/5),(.28,.035,.025),BRASS)

def front_door(shape,x,y,base,width=1.12,height=2.12,lod=0):
 shape.box((x,y+.015,base+height*.5),(width,.13,height),TIMBER)
 for side in (-1,1):shape.box((x+side*(width*.5+.09),y-.075,base+height*.5),(.18,.3,height+.28),STONE)
 shape.box((x,y-.09,base+height+.08),(width+.42,.35,.2),STONE)
 if not lod:
  for i in range(1,6):shape.box((x-width*.5+width*i/6,y-.064,base+height*.5),(.012,.014,height-.12),BRASS)
 shape.box((x+width*.28,y-.1,base+1.03),(.055,.1,.19),BRASS)
 shape.box((x,y-.16,base+.02),(width+.38,.44,.1),STONE)

def foundation(shape,cx,cy,width,depth,height,lod):
 shape.box((cx,cy,height*.5),(width+.12,depth+.12,height),STONE)
 shape.box((cx,cy,height-.04),(width+.33,depth+.33,.18),STONE)
 if lod:return
 rows=2
 for row in range(rows):
  for side in (-1,1):
   count=math.ceil(width/1.05)
   for i in range(count):
    x=cx-width/2+(i+.5)*width/count;shape.box((x,cy+side*(depth/2+.085),(row+.5)*height/rows),(width/count-.045,.12,height/rows-.025),STONE)
   count=math.ceil(depth/1.05)
   for i in range(count):
    y=cy-depth/2+(i+.5)*depth/count;shape.box((cx+side*(width/2+.085),y,(row+.5)*height/rows),(.12,depth/count-.045,height/rows-.025),STONE)

def gabled_roof(shape,cx,cy,width,depth,eave,ridge,lod):
 # True pitched roof on a triangular gable volume, with staggered thick slate shingles.
 hw=width*.5;hd=depth*.5
 shape.solid([(cx-hw,cy-hd,eave),(cx+hw,cy-hd,eave),(cx,cy-hd,ridge),(cx-hw,cy+hd,eave),(cx+hw,cy+hd,eave),(cx,cy+hd,ridge)],
  [(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],IVORY)
 roof_hw=hw+.43;roof_hd=hd+.44;low=eave-.18;high=ridge+.13
 for side in (-1,1):
  def point(s,t,offset=0):return (cx+side*roof_hw*s,cy-roof_hd+2*roof_hd*t,high+(low-high)*s+offset)
  shape.plate([point(0,0),point(1,0),point(1,1),point(0,1)],.17,SLATE)
  shape.beam(point(1,0,-.13),point(1,1,-.13),.15,.22,TIMBER)
  for end in (0,1):shape.beam(point(0,end,.035),point(1,end,.035),.13,.18,TIMBER)
  if not lod:
   rows=max(5,math.ceil(math.hypot(roof_hw,high-low)/.69));columns=math.ceil(2*roof_hd/.69)
   for row in range(rows):
    for col in range(columns+(row%2)):
     # Separate small joints instead of overlapping coplanar faces (which flicker in Unity).
     s0=max(0,row/rows+.002);s1=min(1,(row+1)/rows-.002)
     t0=max(0,(col-.5*(row%2))/columns+.003);t1=min(1,(col+1-.5*(row%2))/columns-.003)
     if t1<=t0:continue
     shape.plate([point(s0,t0,.09),point(s1,t0,.09),point(s1,t1,.09),point(s0,t1,.09)],.055,SLATE)
 shape.beam((cx,cy-roof_hd-.04,high+.14),(cx,cy+roof_hd+.04,high+.14),.20,.25,BRASS)
 for end in (-1,1):
  y=cy+end*(hd+.05)
  shape.beam((cx-hw,y,eave),(cx,y,ridge-.15),.16,.14,TIMBER);shape.beam((cx+hw,y,eave),(cx,y,ridge-.15),.16,.14,TIMBER)
  shape.beam((cx,y,eave),(cx,y,ridge-.13),.17,.15,TIMBER)

def chimney(shape,x,y,z,lod):
 shape.box((x,y,z+.63),(.70,.79,1.38),STONE);shape.box((x,y,z+1.33),(.94,1.0,.16),SLATE)
 shape.box((x,y,z+1.45),(.68,.72,.14),TIMBER)
 if not lod:
  for h in (.2,.6,1.0):shape.box((x,y-.407,z+h),(.72,.035,.025),TIMBER)

def porch(shape,x,front_y,base,lod,width=2.8,awning=True):
 shape.box((x,front_y-.62,base*.5),(width,1.32,base),STONE)
 for step in range(3):
  height=base*(step+1)/3;shape.box((x,front_y-1.48-(2-step)*.36,height*.5),(1.55,.35,height),STONE)
 if awning:
  for side in (-1,1):
   px=x+side*(width*.5-.12);shape.box((px,front_y-1.2,base+1.26),(.16,.16,2.52),TIMBER)
   shape.beam((px,front_y-1.2,base+1.85),(px,front_y-.75,base+2.4),.11,.12,TIMBER)
  shape.plate([(x-width/2-.12,front_y+.13,base+2.85),(x+width/2+.12,front_y+.13,base+2.85),
   (x+width/2+.12,front_y-1.5,base+2.43),(x-width/2-.12,front_y-1.5,base+2.43)],.12,SLATE)

def compass(shape,center,radius,material=BRASS,normal='Y',lod=0):
 x,y,z=center;shape.torus(center,radius*.58,radius*.055,material,normal,16 if lod else 24)
 for i in range(8):
  a=i*math.pi/4;r=radius*(1 if i%2==0 else .73);w=radius*.10
  p0=Vector((math.sin(a)*r,0,math.cos(a)*r));p1=Vector((math.sin(a+math.pi/2)*w,0,math.cos(a+math.pi/2)*w));p2=-p1
  if normal=='Z':p0.y,p0.z=p0.z,p0.y;p1.y,p1.z=p1.z,p1.y;p2.y,p2.z=p2.z,p2.y
  o=Vector(center);d=Vector((0,.055,0)) if normal=='Y' else Vector((0,0,.055))
  shape.solid([o+p0,o+p1,o+p2,o+p0+d,o+p1+d,o+p2+d],[(0,1,2),(5,4,3),(0,3,4,1),(1,4,5,2),(2,5,3,0)],material)

def house(name,lod):
 shape=Shape();base=.54
 width,depth,eave,ridge={'House_Cottage':(6.8,5.4,3.65,6.25),'House_Merchant':(8.8,6.2,5.85,8.55),'House_Workshop':(8.2,6.8,4.05,6.95)}[name]
 foundation(shape,0,0,width,depth,base,lod)
 shape.box((0,0,(base+eave)*.5),(width,depth,eave-base),IVORY)
 for x in (-width/2,width/2):
  for y in (-depth/2,depth/2):shape.box((x,y,(base+eave)*.5),(.23,.23,eave-base+.1),TIMBER)
 for z in (base+.08,eave-.06):
  for side in (-1,1):
   shape.box((0,side*(depth/2+.025),z),(width+.17,.19,.20),TIMBER)
   shape.box((side*(width/2+.025),0,z),(.19,depth+.17,.20),TIMBER)
 # Separate front bays establish human-sized window/door openings; side timber braces carry eaves.
 for side in (-1,1):
  for y in (-depth/2+1,depth/2-1):
   shape.beam((side*(width/2+.06),y-.7,base+.25),(side*(width/2+.06),y+.7,eave-.3),.12,.12,TIMBER)
 gabled_roof(shape,0,0,width,depth,eave,ridge,lod)
 if name=='House_Cottage':
  front_door(shape,0,-depth/2-.07,base,lod=lod)
  for x in (-2.12,2.12):front_window(shape,x,-depth/2-.10,1.42,lod=lod)
  porch(shape,0,-depth/2,base,lod,2.45)
  front_window(shape,0,depth/2+.1,1.55,width=1.3,height=1.25,lod=lod,yaw=math.pi)
  chimney(shape,1.9,1.2,ridge-(ridge-eave)*1.9/(width/2)+.22,lod)
 elif name=='House_Merchant':
  front_door(shape,2.2,-depth/2-.07,base,width=1.2,lod=lod)
  front_window(shape,-1.85,-depth/2-.1,1.32,width=2.45,height=1.4,lod=lod,shutters=False)
  for x in (-2.65,0,2.65):front_window(shape,x,-depth/2-.1,3.85,width=1.18,height=1.24,lod=lod)
  for side in (-1,1):shape.box((0,side*(depth/2+.04),3.28),(width+.2,.22,.22),TIMBER)
  shape.box((0,-depth/2-.10,4.4),(.2,.22,2.9),TIMBER)
  porch(shape,2.2,-depth/2,base,lod,2.35,False)
  awning_w=4.2
  for i in range(6):
   x=-2.0-awning_w/2+(i+.5)*awning_w/6
   shape.plate([(x-awning_w/12,-depth/2-.14,3.08),(x+awning_w/12,-depth/2-.14,3.08),
    (x+awning_w/12,-depth/2-1.65,2.66),(x-awning_w/12,-depth/2-1.65,2.66)],.045,IVORY if i%2 else SLATE)
  for x in (-4.0,.0):shape.box((x,-depth/2-1.58,1.34),(.12,.12,2.68),TIMBER)
  shape.box((-2.0,-depth/2-.66,1.02),(3.65,.72,.16),TIMBER)
  chimney(shape,-2.75,1.3,ridge-(ridge-eave)*2.75/(width/2)+.22,lod)
 else:
  front_door(shape,.75,-depth/2-.07,base,width=1.75,height=2.35,lod=lod)
  front_window(shape,-2.36,-depth/2-.10,1.55,width=1.17,height=1.04,lod=lod)
  porch(shape,.75,-depth/2,base,lod,3.0,False)
  # Small attached workshop bay breaks the main roof silhouette and makes an L-shaped plan.
  ax=width/2+1.16;ay=.7;aw=2.35;ad=3.7
  foundation(shape,ax,ay,aw,ad,base,lod);shape.box((ax,ay,1.67),(aw,ad,2.28),IVORY)
  gabled_roof(shape,ax,ay,aw,ad,2.8,4.17,lod)
  front_window(shape,ax,ay-ad/2-.1,1.17,width=.96,height=1.05,lod=lod,shutters=False)
  chimney(shape,-2.55,1.4,ridge-(ridge-eave)*2.55/(width/2)+.2,lod)
  if not lod:
   for i in range(4):shape.rings((-3.1+i*.24,-depth/2-.26,.70),[(.105,0),(.105,.66)],TIMBER,8)
 compass(shape,(0,-depth/2-.13,eave+.76),.38,lod=lod)
 return [shape.mesh(name+'_Body',bevel=.016 if not lod else 0)]

def windmill(lod):
 body=Shape();sides=12 if not lod else 8
 body.rings((0,0,0),[(4.40,0),(4.45,.18),(4.15,1.05),(4.02,1.16)],STONE,sides,math.pi/12)
 body.rings((0,0,0),[(4.02,1.05),(3.63,6),(3.13,12),(2.80,15.65)],IVORY,sides,math.pi/12)
 for z,r in ((1.2,4.05),(6.2,3.64),(11.1,3.23),(15.4,2.97)):
  body.rings((0,0,z),[(r,.0),(r,.21)],TIMBER,sides,math.pi/12)
 for i in range(8):
  a=i*math.tau/8;body.beam((math.sin(a)*4.03,-math.cos(a)*4.03,1.17),(math.sin(a)*2.88,-math.cos(a)*2.88,15.5),.18,.18,TIMBER)
 body.rings((0,0,0),[(2.9,15.55),(4.0,15.8),(3.75,16.08),(2.83,16.9),(.18,20.28)],SLATE,sides,math.pi/12)
 body.rings((0,0,20.28),[(.18,0),(.06,.52)],BRASS,8)
 for i in range(8):
  a=i*math.tau/8;body.beam((math.sin(a)*3.88,math.cos(a)*3.88,15.94),(math.sin(a)*.14,math.cos(a)*.14,20.32),.07,.10,BRASS)
 front_door(body,0,-4.08,.22,width=1.38,height=2.35,lod=lod);porch(body,0,-4.0,.22,lod,2.3,False)
 for z,r in ((4.50,3.90),(9.20,3.45),(13.25,3.05)):
  for angle in (0,math.pi/2,math.pi,math.pi*1.5):
   front_window(body,math.sin(angle)*r,-math.cos(angle)*r,z,width=.95,height=1.22,lod=lod,yaw=angle,shutters=False)
 rotor=Shape()
 for i in range(4):
  angle=i*math.pi/2+.20;rotation=Matrix.Rotation(angle,3,'Y')
  def p(x,y,z):return rotation@Vector((x,y,z))
  rotor.beam(p(0,0,.38),p(0,0,7.9),.19,.21,TIMBER)
  for j in range(3):
   lower=2.0+j*1.82;upper=lower+1.9;w0=1.68-j*.17;w1=w0-.12
   rotor.plate([p(.12,-.13,lower),p(w0,-.13+math.sin(j)*.1,lower),p(w1,-.08,upper),p(.12,-.10,upper)],.035,IVORY)
  rotor.beam(p(1.65,-.08,2.0),p(1.26,-.08,7.57),.095,.11,TIMBER)
  if not lod:
   for j in range(7):
    z=2.0+j*.9;w=1.66-(z-2)*.073;rotor.beam(p(0,-.17,z),p(w,-.17,z),.065,.055,TIMBER)
 rotor.torus((0,-.12,0),.71,.10,BRASS,'Y',16 if lod else 24)
 compass(rotor,(0,-.26,0),1.0,lod=lod)
 rotor.box((0,.10,0),(.55,.68,.55),SLATE)
 body_obj=body.mesh('Windmill_Body',bevel=.016 if not lod else 0)
 rotor_obj=rotor.mesh('Rotor',location=(0,-4.76,13.70),bevel=.008 if not lod else 0)
 return [body_obj,rotor_obj]

def spire(lod):
 shape=Shape();sides=12 if not lod else 8
 shape.rings((0,0,0),[(4.1,0),(4.1,.25),(3.72,.48),(3.72,1.15),(3.4,1.36)],STONE,sides,math.pi/8)
 shape.rings((0,0,0),[(3.35,1.2),(3.12,8.0),(2.72,17.4)],IVORY,sides,math.pi/8)
 for z,r in ((1.4,3.42),(5.6,3.32),(10.6,3.08),(16.8,2.88)):
  shape.rings((0,0,z),[(r,0),(r,.24)],SLATE,sides,math.pi/8)
 for i in range(8):
  a=i*math.tau/8;shape.beam((math.sin(a)*3.30,-math.cos(a)*3.30,1.35),(math.sin(a)*2.81,-math.cos(a)*2.81,17.5),.15,.19,STONE)
  if i%2==0:
   for z,r in ((3.0,3.35),(8.2,3.2),(13.0,3.0)):
    front_window(shape,math.sin(a)*r,-math.cos(a)*r,z,width=.65,height=1.65,lod=lod,yaw=a,shutters=False)
 shape.rings((0,0,0),[(2.9,17.35),(4.4,17.75),(4.4,18.15),(3.1,18.3)],SLATE,sides,math.pi/8)
 # The open lantern and conductor crown distinguish this from an elongated village tower.
 shape.rings((0,0,18.24),[(2.64,0),(2.64,4.55)],GLASS,8,math.pi/8)
 for i in range(8):
  a=math.pi/8+i*math.tau/8;x=2.76*math.cos(a);y=2.76*math.sin(a)
  shape.box((x,y,20.56),(.22,.22,4.75),BRASS)
  x=3.93*math.cos(a);y=3.93*math.sin(a);shape.box((x,y,18.79),(.12,.12,1.28),BRASS)
 shape.torus((0,0,19.44),3.94,.065,BRASS,segments=16 if lod else 32)
 shape.rings((0,0,0),[(2.96,22.84),(3.65,23.12),(3.47,23.40),(1.23,26.1),(.30,27.0)],SLATE,sides,math.pi/8)
 for z,r in ((24.30,2.0),(27.45,1.47)):
  shape.torus((0,0,z),r,.12,BRASS,segments=16 if lod else 32)
 for i in range(4):
  a=i*math.tau/4+math.pi/4;shape.beam((math.cos(a)*2.8,math.sin(a)*2.8,23.35),(math.cos(a)*1.46,math.sin(a)*1.46,27.5),.12,.15,BRASS)
 shape.rings((0,0,26.0),[(.40,0),(.40,2.50),(.68,2.8),(.15,4.85),(.035,5.4)],BRASS,8)
 front_door(shape,0,-3.42,.47,width=1.45,height=2.55,lod=lod);porch(shape,0,-3.38,.47,lod,2.4,False)
 compass(shape,(0,-3.45,4.33),.55,lod=lod)
 return [shape.mesh('StormSpire_Body',bevel=.016 if not lod else 0)]

def export(name,lod,objects):
 bpy.ops.object.select_all(action='DESELECT')
 for obj in objects:obj.select_set(True)
 path=MODELS/f'{name}_LOD{lod}.fbx'
 bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
  bake_space_transform=True,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,
  mesh_smooth_type='FACE',colors_type='LINEAR',bake_anim=False,path_mode='AUTO')
 points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
 lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
 result={'path':path.relative_to(ROOT).as_posix(),'triangles':sum(triangle_count(o) for o in objects),'meshCount':len(objects),
  'boundsCoordinateSpace':'nativeUnityFbx',
  'boundsUnityMin':[lo[0],lo[2],lo[1]],'boundsUnityMax':[hi[0],hi[2],hi[1]],
  'normalizedPrefabBoundsUnityMin':[-hi[0],lo[2],-hi[1]],'normalizedPrefabBoundsUnityMax':[-lo[0],hi[2],-lo[1]],
  'materialNames':list(PALETTE),'meshes':[{'name':o.name,'triangles':triangle_count(o),'usedMaterialSlots':sorted(set(p.material_index for p in o.data.polygons)),
   'localPivotUnity':[o.location.x,o.location.z,o.location.y]} for o in objects], 'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
 if name=='Windmill':result['rotor']={'meshName':'Rotor','pivotCoordinateSpace':'nativeUnityFbx',
  'pivotUnity':[0,13.7,-4.76],'normalizedPrefabPivotUnity':[0,13.7,4.76],'rotationAxisUnity':[0,0,1]}
 assert all(math.isfinite(c) for o in objects for uv in o.data.uv_layers[0].data for c in uv.uv)
 return result

def render(name,objects):
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
 scene.render.threads_mode='FIXED';scene.render.threads=12
 scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
 scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
 scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0
 scene.world=bpy.data.worlds.new('Architecture soft studio');scene.world.use_nodes=True
 scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.57,.64,.69,1);scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.65
 points=[o.matrix_world@v.co for o in objects for v in o.data.vertices];height=max(v.z for v in points)
 width=max(v.x for v in points)-min(v.x for v in points);depth=max(v.y for v in points)-min(v.y for v in points)
 span=max(width,depth,height);target=Vector((0,0,height*.44))
 plane=Shape();plane.box((0,0,-.14),(span*200,span*200,.22),STONE);floor=plane.mesh('Studio ground')
 for light_name,location,power,size in [('Key',(-span*.8,-span,span*1.8),span*span*42,span*.7),('Fill',(span,-span*.4,span),span*span*15,span)]:
  data=bpy.data.lights.new(light_name,'AREA');obj=bpy.data.objects.new(light_name,data);bpy.context.collection.objects.link(obj);obj.location=location;data.energy=power;data.size=size
  obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
 camera_data=bpy.data.cameras.new('Actual architecture review');camera=bpy.data.objects.new('Actual architecture review',camera_data);bpy.context.collection.objects.link(camera);scene.camera=camera
 camera_data.type='ORTHO';camera_data.ortho_scale=span*1.42
 camera.location=(span*1.25,-span*1.75,span*1.00+height*.22);camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
 scene.render.filepath=str(EVIDENCE/f'Architecture_{name}_ThreeQuarter.png');bpy.ops.render.render(write_still=True)
 if name.startswith('House_'):
  camera.location=(0,-span*2,height*.47);camera.rotation_euler=(Vector((0,0,height*.47))-camera.location).to_track_quat('-Z','Y').to_euler()
  scene.render.filepath=str(EVIDENCE/f'Architecture_{name}_Front.png');bpy.ops.render.render(write_still=True)

def clear():
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)

def audit():
 # This is a Blender FBX round-trip check, not a Unity importer/runtime test. The x,z,y
 # coordinate mapping was established separately from actual Unity prefab bounds and front views.
 manifest=OUT/'ArchitectureManifest.json'
 if '--manifest' in sys.argv:manifest=ROOT/sys.argv[sys.argv.index('--manifest')+1]
 report=json.loads(manifest.read_text(encoding='utf-8'));results=[]
 if report.get('version',1)<2:raise RuntimeError('Use the corrected version 2 manifest; version 1 assumed the wrong Unity front sign.')
 for design in report['models']:
  for level in design['lods']:
   clear();path=ROOT/level['path'];before=hashlib.sha256(path.read_bytes()).hexdigest()
   bpy.ops.import_scene.fbx(filepath=str(path),colors_type='LINEAR')
   objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
   assert len(objects)==level['meshCount']
   assert sum(triangle_count(o) for o in objects)==level['triangles']
   for obj in objects:
    assert len(obj.data.uv_layers)>=1
    assert all(math.isfinite(c) for v in obj.data.uv_layers[0].data for c in v.uv)
    assert all(m.name.split('.')[0] in PALETTE for m in obj.data.materials)
    assert all(math.isfinite(c) for v in obj.data.vertices for c in v.co)
   points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
   bounds_min=[min(v.x for v in points),min(v.z for v in points),min(v.y for v in points)]
   bounds_max=[max(v.x for v in points),max(v.z for v in points),max(v.y for v in points)]
   for a,b in zip(bounds_min+bounds_max,level['boundsUnityMin']+level['boundsUnityMax']):assert abs(a-b)<.0002,(path,a,b)
   rotor=None
   if design['id']=='Windmill':
    obj=next(o for o in objects if o.name=='Rotor');p=obj.matrix_world.translation
    rotor=[p.x,p.z,p.y]
    assert (Vector(rotor)-Vector((0,13.7,-4.76))).length<.0001
   assert hashlib.sha256(path.read_bytes()).hexdigest()==before
   results.append({'path':level['path'],'meshCount':len(objects),'triangles':level['triangles'],
    'boundsMatch':True,'finiteUV':True,'sharedPalettePreserved':True,'sourceShaUnchanged':True,'rotorPivotUnity':rotor})
 audit_path=EVIDENCE/'ArchitectureExportAudit.json'
 if '--audit-output' in sys.argv:audit_path=ROOT/sys.argv[sys.argv.index('--audit-output')+1]
 audit_path.write_text(json.dumps({'passed':True,'scope':'Blender FBX reimport only; does not execute Unity or independently verify Unity front direction',
  'coordinateMappingSource':'Actual Unity native prefab bounds/front inspection; documented in Docs/WorldArt/Architecture_Coordinates.md',
  'manifest':manifest.relative_to(ROOT).as_posix(),'fbxCount':len(results),'models':results},indent=2),encoding='utf-8')
 print('ARCHITECTURE_EXPORT_AUDIT_PASSED',len(results),flush=True)

def main():
 if '--audit' in sys.argv:audit();return
 MODELS.mkdir(parents=True,exist_ok=True);EVIDENCE.mkdir(parents=True,exist_ok=True);material_setup()
 bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1
 names=['House_Cottage','House_Merchant','House_Workshop','Windmill','StormSpire']
 args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
 if '--only' in args:names=[args[args.index('--only')+1]]
 models=[]
 for name in names:
  entry={'id':name,'author':'Original Orbis geometry authored in local Blender; no external model input','lods':[]}
  for lod in range(2):
   clear();objects=house(name,lod) if name.startswith('House_') else windmill(lod) if name=='Windmill' else spire(lod)
   entry['lods'].append(export(name,lod,objects))
   if lod==0 and '--no-render' not in args:render(name,objects)
  assert entry['lods'][1]['triangles']<entry['lods'][0]['triangles']
  models.append(entry);print('ARCHITECTURE_COMPLETE',name,[x['triangles'] for x in entry['lods']],flush=True)
 report={'version':2,'authoring':'Original Orbis procedural architectural geometry; no commercial game or third-party mesh input.',
  'palette':[{ 'name':n,'sRGB':list(c),'linear':[srgb_linear(v) for v in c[:3]]+[1]} for n,c in PALETTE.items()],
  'coordinateSystem':'Native FBX: Unity +Y up, -Z front, metres, ground-centre origin. Final prefab: each LOD child rotates Y=180 degrees in WorldLandmarkBuilder, giving +Z front. Rotor rotates around its own local Z axis.',
  'coordinateContract':{'blenderAuthoringFront':[0,-1,0],'nativeUnityFbxFront':[0,0,-1],
   'blenderPointToNativeUnity':'(x,z,y)','prefabLodRotationEuler':[0,180,0],'normalizedPrefabFront':[0,0,1],
   'verification':'Blender round-trip validates mesh/UV/budgets. Unity front sign is separately confirmed from actual prefab bounds and gameplay captures.'},
  'uv':'UV0 repeating planar mapping: 1 UV unit = 2 metres; no vertex tint multiplication required',
  'doorHeightsMetres':{'houses':2.12,'workshop':2.35,'windmill':2.35,'spire':2.55},'models':models}
 filename='ArchitectureManifest.json' if len(names)==5 else f'ArchitectureManifest_{names[0]}.json'
 (OUT/filename).write_text(json.dumps(report,indent=2),encoding='utf-8');(EVIDENCE/filename).write_text(json.dumps(report,indent=2),encoding='utf-8')
 print('ARCHITECTURE_BUILD_FINISHED',filename,flush=True)

if __name__=='__main__':main()
