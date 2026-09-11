"""Original Blender mesh studies of the supplied Stella/Polaris character sheets.

Background invocation: blender --background --factory-startup --python this_file.py
Output FBXs are T-pose, metre-scale, skinned humanoids. No image uploads, remote
model generation, traced textures or third-party character geometry are used.
The first models preserve the reference's silhouette/palette/motifs, not every
illustrated detail. Cape/hair have authored static weights, not cloth simulation.
"""
import bpy, math, random, json, sys
from pathlib import Path
from mathutils import Vector, Quaternion

bpy.context.preferences.filepaths.save_version=0
bpy.context.preferences.filepaths.file_preview_type="NONE"
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/("Tools/Blender/FinalExports" if "--staged-export" in sys.argv else "Assets/Orbis/Game/Island/Models")
SOURCES=ROOT/"Tools/Blender/Sources"
RESULTS=ROOT/"TestResults"
for folder in (OUT,SOURCES,RESULTS): folder.mkdir(parents=True,exist_ok=True)
random.seed(1616)

# Twelve shared colour families. All details below are geometry, not image decals.
DEFS=[
 ("EX_Skin",(.91,.66,.52,1),.0,.72,0),
 ("EX_Ivory",(.91,.89,.81,1),.10,.55,0),
 ("EX_Navy",(.035,.062,.12,1),.15,.56,0),
 ("EX_Gold",(.66,.43,.17,1),.67,.36,0),
 ("EX_Leather",(.085,.064,.058,1),.08,.65,0),
 ("EX_Hair",(.43,.31,.18,1),.05,.50,0),
 ("EX_HairLight",(.68,.52,.32,1),.08,.44,0),
 ("EX_EyeWhite",(.96,.97,.96,1),.0,.33,0),
 ("EX_Teal",(.075,.40,.44,1),.18,.32,.10),
 ("EX_Ink",(.018,.025,.034,1),.0,.56,0),
 ("EX_Gem",(.12,.72,.68,1),.42,.23,.28),
 ("EX_CapeLining",(.22,.34,.48,1),.12,.59,0),
]
def clamp(x): return max(0,min(1,x))
def blend(a,b,t):
 t=clamp(t)
 return {a:1-t,b:t} if 0<t<1 else {a if t<=0 else b:1}
def torso_weights(p):
 z=p[2]
 if z<=1.04:return {"Hips":1}
 if z<1.17:return blend("Hips","Spine",(z-1.04)/.13)
 if z<1.32:return blend("Spine","Chest",(z-1.17)/.15)
 return {"Chest":1}
def cloth_weights(p):
 return torso_weights((p[0],p[1],max(1.04,p[2])))
def arm_weights(side,p):
 x=abs(p[0])
 if x<.24:return blend(side+"Shoulder",side+"UpperArm",(x-.15)/.09)
 if x<.40:return {side+"UpperArm":1}
 if x<.49:return blend(side+"UpperArm",side+"LowerArm",(x-.40)/.09)
 if x<.62:return {side+"LowerArm":1}
 return blend(side+"LowerArm",side+"Hand",(x-.62)/.065)
def leg_weights(side,p):
 z=p[2]
 if z>.58:return {side+"UpperLeg":1}
 if z>.44:return blend(side+"UpperLeg",side+"LowerLeg",(.58-z)/.14)
 if z>.16:return {side+"LowerLeg":1}
 return blend(side+"LowerLeg",side+"Foot",(.16-z)/.07)
def basis(n):
 n=Vector(n).normalized()
 ref=Vector((0,0,1)) if abs(n.z)<.95 else Vector((0,1,0))
 u=n.cross(ref).normalized()
 return n,u,n.cross(u).normalized()

class Builder:
 def __init__(self,name,female):
  self.name,self.female=name,female
  self.data=[dict(v=[],f=[],w=[],smooth=[]) for _ in DEFS]
  self.finger_bones=[]
 def add(self,verts,faces,mat=0,weights="Head",smooth=True):
  d=self.data[mat];offset=len(d["v"])
  d["v"].extend([Vector(p) for p in verts])
  d["f"].extend([tuple(offset+i for i in f) for f in faces])
  for p in verts:
   w=weights(p) if callable(weights) else ({weights:1} if isinstance(weights,str) else weights)
   d["w"].append({k:v for k,v in w.items() if v>.00001})
  d["smooth"].extend([smooth]*len(faces))
 def loft(self,points,radii,mat=0,weights="Head",sides=12,elliptic=1,smooth=True):
  points=[Vector(p) for p in points];verts=[];old_u=None
  for j,(p,r) in enumerate(zip(points,radii)):
   n=(points[min(j+1,len(points)-1)]-points[max(0,j-1)]).normalized()
   if old_u is None: _,u,v=basis(n)
   else:
    u=(old_u-n*old_u.dot(n)).normalized();v=n.cross(u).normalized()
   old_u=u
   rx,ry=(r,r*elliptic) if isinstance(r,(float,int)) else r
   for k in range(sides):
    a=math.tau*k/sides
    verts.append(p+rx*math.cos(a)*u+ry*math.sin(a)*v)
  faces=[tuple(range(sides-1,-1,-1))]
  for j in range(len(points)-1):
   for k in range(sides):
    faces.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
  faces.append(tuple((len(points)-1)*sides+k for k in range(sides)))
  self.add(verts,faces,mat,weights,smooth)
 def tube(self,points,r=.0025,mat=3,weights="Chest",sides=6):
  self.loft(points,[r]*len(points),mat,weights,sides)
 def ellipsoid(self,c,r,mat=0,weights="Head",rows=12,sides=16,smooth=True):
  c=Vector(c);verts=[]
  for j in range(rows+1):
   a=math.pi*j/rows
   for k in range(sides):
    b=math.tau*k/sides
    verts.append(c+Vector((r[0]*math.sin(a)*math.cos(b),r[1]*math.sin(a)*math.sin(b),r[2]*math.cos(a))))
  faces=[]
  for j in range(rows):
   for k in range(sides):
    faces.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
  self.add(verts,faces,mat,weights,smooth)
 def zloft(self,rings,mat=0,weights=torso_weights,sides=24):
  # Explicit xy rings give torso, jaw and pelvis a continuous tailored silhouette.
  verts=[]
  for z,rx,ry,cy in rings:
   for j in range(sides):
    t=math.tau*j/sides
    verts.append((rx*math.cos(t),cy+ry*math.sin(t),z))
  faces=[]
  for k in range(len(rings)-1):
   for j in range(sides):
    faces.append((k*sides+j,k*sides+(j+1)%sides,(k+1)*sides+(j+1)%sides,(k+1)*sides+j))
  faces.extend([tuple(range(sides-1,-1,-1)),tuple((len(rings)-1)*sides+j for j in range(sides))])
  self.add(verts,faces,mat,weights)
 def panel(self,points,mat=1,weights=cloth_weights,trim=True,thickness=.002):
  vs=[Vector(p) for p in points];normal=(vs[1]-vs[0]).cross(vs[2]-vs[0]).normalized()
  center=sum(vs,Vector())/len(vs)
  # Slight central fold makes a draped panel rather than a flat triangular card.
  center+=normal*.006
  n=len(vs)
  verts=vs+[v-normal*thickness for v in vs]+[center,center-normal*thickness]
  faces=[]
  for j in range(n):
   k=(j+1)%n
   faces.extend([(j,k,n*2),(n+k,n+j,n*2+1),(j,n+j,n+k,k)])
  self.add(verts,faces,mat,weights)
  if trim:self.tube(vs+[vs[0]],.0022,3,weights,5)
 def ring(self,c,n,r,thickness=.003,mat=3,weights="Chest",segments=24):
  c=Vector(c);n,u,v=basis(n)
  self.tube([c+r*(math.cos(j*math.tau/segments)*u+math.sin(j*math.tau/segments)*v)
            for j in range(segments+1)],thickness,mat,weights,6)
 def compass(self,c,scale=.04,weights="Chest",normal=(0,-1,0)):
  c=Vector(c);n,u,v=basis(normal)
  self.ring(c,n,scale*.72,.0025,3,weights)
  for i in range(8):
   a=i*math.tau/8
   radial=math.cos(a)*u+math.sin(a)*v
   tangent=-math.sin(a)*u+math.cos(a)*v
   length=scale*(1.2 if i%2==0 else .86)
   vs=[c+n*.004,c+radial*length,c+radial*scale*.38+tangent*scale*.15,
       c+radial*scale*.38-tangent*scale*.15]
   self.add(vs,[(0,2,1),(0,1,3)],3,weights,False)
  self.gem(c+n*.006,scale*.18,scale*.31,weights,normal)
 def gem(self,c,width=.01,height=.025,weights="Chest",normal=(0,-1,0)):
  c=Vector(c);n,u,v=basis(normal)
  vs=[c+v*height,c+u*width,c-v*height,c-u*width,c+n*width*.65,c-n*.001]
  self.add(vs,[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(1,0,5),(2,1,5),(3,2,5),(0,3,5)],10,weights,False)
  self.tube([vs[0],vs[1],vs[2],vs[3],vs[0]],.0015,3,weights,5)

 def body(self):
  f=self.female
  # Continuous underlying torso; the collar/shoulder/sleeve overlaps hide limb joins.
  rings=[(.89,.102,.063,0),(.96,.120 if f else .123,.071,0),
         (1.04,.092 if f else .108,.058,0),(1.11,.076 if f else .104,.050 if f else .059,0),
         (1.20,.101 if f else .132,.071 if f else .078,-.002),(1.26,.126 if f else .151,.089 if f else .083,-.003),(1.32,.139 if f else .168,.076 if f else .081,-.005),
         (1.37,.147 if f else .181,.070 if f else .079,0),(1.405,.135 if f else .155,.060,0),
         (1.43,.065,.045,0)]
  self.zloft(rings,2)
  # Tailored grid follows bust/chest, waist and hip; it cannot bridge the waist as a flat plate.
  front_rows=[(1.382,.051 if f else .063,-.073 if f else -.082),
              (1.321,.065 if f else .075,-.084 if f else -.090),
              (1.264,.067 if f else .073,-.098 if f else -.090),
              (1.203,.054 if f else .069,-.083 if f else -.085),
              (1.112,.046 if f else .062,-.058 if f else -.068),
              (1.043,.056 if f else .063,-.066 if f else -.068),
              (.998,.077 if f else .073,-.077)]
  front=[];faces=[]
  for z,w,y in front_rows:
   for i in range(7):
    t=(i-3)/3
    front.append((t*w,y+.008*t*t,z))
  for j in range(len(front_rows)-1):
   for i in range(6):
    faces.append((j*7+i,j*7+i+1,(j+1)*7+i+1,(j+1)*7+i))
  self.add(front,faces,1,torso_weights)
  for s in(-1,1):
   self.tube([(s*w,y+.008-.001,z) for z,w,y in front_rows],.0028,3,torso_weights)
   self.panel([(s*.09,-.071,1.34),(s*.13,-.069,1.38),(s*.07,-.069,1.42),(s*.042,-.083,1.35)],1)
  # Articulated waist belt and side buckle/pouches.
  self.zloft([(.982,.115,.071,0),(1.012,.115,.071,0)],4)
  for s in(-1,1):
   for z in(.985,1.01):
    self.tube([(s*.015,-.075,z),(s*.065,-.070,z),(s*.107,-.044,z)],.002,3,"Hips")
  self.panel([(-.021,-.078,.985),(.021,-.078,.985),(.021,-.078,1.015),(-.021,-.078,1.015)],3,"Hips",False)
  self.panel([(-.013,-.081,.991),(.013,-.081,.991),(.013,-.081,1.009),(-.013,-.081,1.009)],4,"Hips",False)
  # Neck, raised dark collar, collarbone transitions.
  self.zloft([(1.405,.049,.039,0),(1.465,.040,.035,0),(1.505,.042,.039,0)],0,"Neck",16)
  self.zloft([(1.42,.052,.044,0),(1.472,.046,.041,0)],2,"Neck",20)
  self.tube([(-.045,-.022,1.473),(-.03,-.040,1.473),(0,-.043,1.473),(.03,-.040,1.473),(.045,-.022,1.473)],.0025,3,"Neck")
  self.compass((-.092,-.086,1.374),.048)
  self.gem((.029,-.09,1.38),.012,.028,"Chest")
  for z,y in((1.27,-.101),(1.20,-.091),(1.12,-.074)):
   self.compass((0,y,z),.013,torso_weights)
  # Draped shoulder mantle, lighter inner facing, split hanging cloak.
  for s in(-1,1):
   self.panel([(s*.038,.03,1.432),(s*.17,-.002,1.401),(s*.224,.025,1.325),
               (s*.18,.105,1.29),(s*.035,.113,1.365)],2)
   self.tube([(s*.048,-.003,1.415),(s*.15,-.032,1.404),(s*.215,.008,1.34)],.004,3,"Chest")
   self.panel([(s*.035,.108,1.366),(s*.18,.115,1.30),(s*.22,.16,1.03),
               (s*.25,.215,.69),(s*.19,.25,.40),(s*.045,.21,.57)],2)
   self.panel([(s*.045,.093,1.31),(s*.13,.102,1.26),(s*.185,.150,1.01),
               (s*.215,.204,.69),(s*.17,.237,.47),(s*.065,.199,.59)],11,trim=False)
   self.compass((s*.145,.264,.60),.043,"Hips",(0,1,0))
   self.gem((s*.19,.249,.405),.006,.017,"Hips",(0,1,0))
   # Front ivory/navy tails, offset from legs to leave a readable layered waist.
   self.panel([(s*.033,-.074,.992),(s*.111,-.059,.972),(s*.15,-.076,.79),
               (s*.13,-.118,.62),(s*.039,-.107,.725)],1)
   self.panel([(s*.105,-.046,.97),(s*.153,-.005,.95),(s*.20,-.017,.77),
               (s*.18,-.052,.67),(s*.12,-.079,.79)],2)
   self.compass((s*.105,-.115,.77),.019,"Hips")
   # Turquoise pendants and leather belt straps.
   self.tube([(s*.11,-.061,1.00),(s*.145,-.071,.943),(s*.142,-.076,.90)],.0025,3,"Hips")
   self.gem((s*.142,-.078,.881),.009,.025,"Hips")
  # Rear-facing emblems and hems are deliberately readable from the gameplay camera.
  self.panel([(-.073,.112,1.382),(.073,.112,1.382),(.148,.148,1.235),
              (.079,.189,1.055),(0,.202,1.014),(-.079,.189,1.055),(-.148,.148,1.235)],2)
  self.compass((0,.162,1.278),.063,"Chest",(0,1,0))
  for s in(-1,1):
   self.compass((s*.145,.245,.715),.052,"Hips",(0,1,0))
   self.tube([(s*.18,.123,1.30),(s*.224,.166,1.01),(s*.254,.221,.69),
              (s*.190,.257,.40),(s*.045,.217,.57)],.0037,3,cloth_weights)
   self.tube([(s*.205,.188,.93),(s*.216,.221,.75),(s*.184,.248,.54)],.0022,3,"Hips")
  if not f:
   # Polaris's longer split coat and trouser straps distinguish his tailored silhouette.
   for sign,side in ((1,"Left"),(-1,"Right")):
    self.panel([(sign*.038,-.080,.983),(sign*.092,-.066,.966),
                (sign*.159,-.109,.64),(sign*.144,-.116,.49),(sign*.068,-.104,.67)],1)
    self.tube([(sign*.07,-.078,.86),(sign*.112,-.075,.815),(sign*.137,-.033,.78)],.004,4,side+"UpperLeg")
    self.gem((sign*.13,-.116,.585),.006,.017,"Hips")

  if f:
   # Pleated short skirt is intentionally separate from the long split tails.
   for i in range(12):
    a0=math.tau*i/12;a1=math.tau*(i+1)/12
    self.panel([(.12*math.cos(a0),.073*math.sin(a0),.964),
                (.12*math.cos(a1),.073*math.sin(a1),.964),
                (.162*math.cos(a1),.096*math.sin(a1),.793+(.02 if i%2 else 0)),
                (.162*math.cos(a0),.096*math.sin(a0),.793+(.02 if i%2 else 0))],2,"Hips",False)
  # Skin or trousers use leg joints; visible thighs have a continuous tapered surface.
  for sign,side in((1,"Left"),(-1,"Right")):
   x=sign*.084
   points=[(x,0,.938),(x*1.02,0,.82),(x*1.04,-.005,.67),(x*1.06,-.012,.515),
           (x*1.04,.006,.43),(x,.016,.29),(x,-.004,.13),(x,-.004,.08)]
   radii=[(.065,.064),(.068,.067),(.054,.054),(.041,.045),(.045,.047),(.036,.040),(.025,.028),(.025,.027)]
   # Remove unseen body/trouser surfaces below the boot lip: skin must not poke through
   # the ankle when retargeting bends the foot. A short overlap stays inside the boot.
   boot_top=.36 if f else .40
   points=points[:5]+[(x,.014,boot_top-.026)]
   radii=radii[:5]+[(.040,.044)]
   self.loft(points,radii,0 if f else 4,lambda p,sd=side:leg_weights(sd,p),16)
   if f and sign==1:
    self.loft([(x*1.04,-.004,.65),(x*1.06,-.01,.515),(x*1.04,.006,.43),(x,.014,boot_top-.026)],
              [(.056,.056),(.043,.047),(.047,.049),(.042,.046)],2,lambda p,sd=side:leg_weights(sd,p),16)
    self.ring((x,-.004,.65),(0,0,1),.055,.003,3,side+"UpperLeg")
   elif f:
    self.loft([(x,0,.738),(x,0,.72)],[(.065,.067),(.065,.067)],4,side+"UpperLeg",16)
    self.compass((x,-.068,.73),.018,side+"UpperLeg")
   # Boots have a calf shaft, shaped instep and pointed sole; no primitive capsule feet.
   boot_top=.36 if f else .40
   self.loft([(x,.014,boot_top),(x,.014,.29),(x,-.004,.16),(x,-.008,.10),(x,-.006,.071)],
             [(.052,.056),(.044,.048),(.032,.037),(.034,.040),(.035,.039)],1,lambda p,sd=side:leg_weights(sd,p),12)
   self.loft([(x,.016,.077),(x,-.036,.065),(x,-.097,.041),(x,-.139,.032)],
             [(.033,.040),(.038,.040),(.038,.027),(.018,.017)],1,side+"Foot",12)
   self.loft([(x,.018,.041),(x,-.04,.030),(x,-.104,.017),(x,-.147,.016)],
             [(.035,.023),(.041,.017),(.041,.014),(.02,.013)],4,side+"Foot",10)
   for s in(-1,1):
    self.tube([(x+s*.037,-.041,boot_top),(x+s*.031,-.035,.24),(x+s*.025,-.044,.12),(x+s*.026,-.087,.064)],.0032,3,lambda p,sd=side:leg_weights(sd,p))
   self.gem((x,-.045,.19),.01,.029,side+"LowerLeg")
   self.compass((x,-.058,boot_top),.025,side+"LowerLeg")
   if not f:
    self.panel([(x,-.053,.58),(x+.040,-.051,.53),(x+.033,-.054,.44),(x,-.064,.405),
                (x-.033,-.054,.44),(x-.040,-.051,.53)],2,lambda p,sd=side:leg_weights(sd,p))
    self.compass((x,-.068,.515),.032,side+"LowerLeg")
  # Connected T-pose arms, loose ivory sleeves, dark gloves and gold wrist work.
  for sign,side in((1,"Left"),(-1,"Right")):
   aw=lambda p,sd=side:arm_weights(sd,p)
   points=[(sign*.13,0,1.392),(sign*.21,0,1.391),(sign*.32,0,1.387),
           (sign*.44,0,1.382),(sign*.54,0,1.38),(sign*.645,0,1.377),(sign*.68,0,1.375)]
   radii=[.061,.057,.047,.038,.036,.027,.028]
   # The elbow-to-gauntlet skin remains visible. Hidden skin stops just inside
   # the gauntlet so different radial tessellation/retargeting cannot expose
   # flesh through the wrist. Hands are already fully authored as glove meshes.
   points=points[:4]+[(sign*.493,0,1.38094)]
   radii=radii[:4]+[.03694]
   self.loft(points,radii,0,aw,16)
   self.loft([(sign*.145,0,1.393),(sign*.205,0,1.391),(sign*.275,0,1.389),(sign*.335,0,1.386),(sign*.40,0,1.383)],
             [.061,.064,.064,.068,.064],1,aw,20)
   self.loft([(sign*.393,0,1.383),(sign*.412,0,1.383)],[.065,.065],2,aw,16)
   for j in range(4):
    a=j*math.tau/4
    self.tube([(sign*.21,math.cos(a)*.064,1.393+math.sin(a)*.064),
               (sign*.30,math.cos(a+.12)*.066,1.387+math.sin(a+.12)*.066),
               (sign*.392,math.cos(a)*.064,1.383+math.sin(a)*.064)],.002,11,aw)
   self.loft([(sign*.475,0,1.381),(sign*.55,0,1.38),(sign*.645,0,1.377),(sign*.69,0,1.375)],
             [.039,.039,.0315,.0305],4,aw,12)
   for xx,rr in((.48,.040),(.623,.032)):
    self.ring((sign*xx,0,1.38),(1,0,0),rr,.0034,3,aw)
   self.panel([(sign*.50,-.039,1.402),(sign*.615,-.032,1.394),(sign*.63,-.032,1.37),
               (sign*.51,-.040,1.356)],2,aw)
   self.compass((sign*.555,-.044,1.38),.018,aw)
   self.hand(sign,side)

 def hand(self,sign,side):
  # Flattened palm plus five separately skinned tapered fingers.
  self.loft([(sign*.663,0,1.375),(sign*.71,0,1.375),(sign*.743,0,1.375)],
            [(.024,.029),(.023,.034),(.019,.029)],4,side+"Hand",12)
  fingers=[("Index",-.021,0,.074),("Middle",-.006,.002,.082),("Ring",.010,0,.074),("Little",.025,-.003,.060)]
  for name,y,zoff,length in fingers:
   start=Vector((sign*.737,y,1.375+zoff))
   middle=start+Vector((sign*length*.5,.001,-.005))
   tip=start+Vector((sign*length,0,-.009))
   p2=start+Vector((sign*length*.76,.0005,-.007))
   bone1=side+name+"Proximal";bone2=side+name+"Intermediate";bone3=side+name+"Distal"
   self.finger_bones.extend([(bone1,tuple(start),tuple(middle),side+"Hand"),
                            (bone2,tuple(middle),tuple(p2),bone1),(bone3,tuple(p2),tuple(tip),bone2)])
   def fw(p,a=bone1,b=bone2,c=bone3,st=start,L=length):
    t=abs(p[0]-st.x)/L
    return blend(a,b,(t-.32)/.32) if t<.68 else blend(b,c,(t-.68)/.20)
   self.loft([start,middle,p2,tip],[.009,.008,.0068,.004],4,fw,7)
  thumb=[(sign*.694,-.025,1.375),(sign*.711,-.046,1.366),(sign*.738,-.06,1.36),(sign*.750,-.065,1.356)]
  names=[side+"ThumbProximal",side+"ThumbIntermediate",side+"ThumbDistal"]
  for j,name in enumerate(names):
   self.finger_bones.append((name,thumb[j],thumb[j+1],side+"Hand" if j==0 else names[j-1]))
  self.loft(thumb,[.011,.010,.008,.004],4,names[0],8)

 def face(self):
  f=self.female
  self.zloft([(1.495,.015,.025,-.002),(1.514,.041 if f else .052,.044,-.005),
              (1.536,.061 if f else .069,.058,-.003),(1.553,.074 if f else .080,.065,-.002),(1.572,.082,.071,0),
              (1.61,.096,.079,0),(1.648,.099,.083,.001),(1.682,.085,.078,.004),
              (1.708,.057,.057,.005),(1.723,.016,.024,.005)],0,"Head",32)
  # Small tapered nose merged visually into the face; distinct bridge and tip.
  self.loft([(0,-.077,1.615),(0,-.080,1.600),(0,-.087,1.586),(0,-.079,1.581)],
            [(.002,.002),(.004,.003),(.007,.004),(.005,.003)],0,"Head",10)
  for sign in(-1,1):
   x=sign*.043
   # Almond eyeball patch; shallow depth and non-spherical silhouette avoid doll eyes.
   outline=[(-.030,0),(-.020,.012),(-.006,.016),(.012,.014),(.029,.004),(.022,-.007),(.006,-.010),(-.011,-.008)]
   vs=[(x+sign*dx*(1 if f else .95),-.083+abs(dx)*.12,1.612+dz*(1 if f else .79)) for dx,dz in outline]
   center=(x,-.087,1.614)
   self.add(vs+[center],[(i,(i+1)%8,8) for i in range(8)],7,"Head")
   self.ellipsoid((x,-.088,1.614),(.012,.0025,.013 if f else .0105),8,"Head",10,16)
   self.ellipsoid((x,-.0905,1.615),(.005,.001,.009 if f else .0078),9,"Head",8,12)
   self.ellipsoid((x-sign*.0035,-.092,1.621),(.0035,.001,.0042),7,"Head",6,10)
   self.ellipsoid((x+sign*.003,-.091,1.609),(.0015,.001,.0018),7,"Head",4,8)
   # Tapered upper lashes and slim outer flicks; gentle lower lash.
   upper=[vs[i] for i in(0,1,2,3,4)]
   self.loft([(p[0],p[1]-.0018,p[2]+.0007) for p in upper],[.0008,.0018,.0023,.0021,.001],9,"Head",5)
   self.tube([(vs[i][0],vs[i][1]-.001,vs[i][2]) for i in(4,5,6,7)],.0007,5,"Head",4)
   self.loft([(x+sign*.025,-.081,1.617),(x+sign*.034,-.081,1.622)],[.0022,.0002],9,"Head",5)
   self.loft([(x-sign*.026,-.079,1.642 if f else 1.640),(x-sign*.008,-.087,1.648 if f else 1.645),(x+sign*.018,-.079,1.646)],
             [.0015,.0025 if f else .0035,.0008],5,"Head",5)
   # Ears and suspended turquoise/navy ornaments.
   self.ellipsoid((sign*.097,.002,1.586),(.015,.018,.030),0,"Head",8,12)
   self.ellipsoid((sign*.104,-.010,1.588),(.006,.008,.017),0,"Head",6,8)
   if f:
    self.tube([(sign*.108,-.005,1.565),(sign*.109,-.005,1.543)],.0017,3,"Head")
    self.gem((sign*.109,-.009,1.53),.0055,.013,"Head")
  # Closed, subtle smile with a lower lip highlight. No painted flat facial texture.
  self.loft([(-.019,-.075,1.551),(-.008,-.080,1.548),(0,-.081,1.547),(.01,-.079,1.55),(.018,-.075,1.553)],
            [.0004,.0009,.001,.0009,.0002],4,"Head",5)
  self.loft([(-.009,-.079,1.543),(0,-.081,1.542),(.009,-.078,1.544)],[.0003,.0013,.0003],0,"Head",5)

 def hair_strand(self,points,widths,mat=6):
  # Catmull-Rom interpolation gives each authored lock a smooth, tapered sweep.
  controls=[Vector(p) for p in points]
  smooth=[];sizes=[]
  for j in range(len(controls)-1):
   a=controls[max(0,j-1)];b=controls[j];c=controls[j+1];d=controls[min(len(controls)-1,j+2)]
   for k in range(3):
    t=k/3
    smooth.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
    sizes.append(widths[j]*(1-t)+widths[j+1]*t)
  smooth.append(controls[-1]);sizes.append(widths[-1])
  self.loft(smooth,[(w,w*.30) for w in sizes],mat,"Head",10)
  if len(points)>3 and max(widths)>.012:
   ps=[p+Vector((0,-.0018,.0012)) for p in smooth[2:]]
   self.loft(ps,[max(.0005,w*.035) for w in sizes[2:]],6 if mat==5 else 5,"Head",5)

 def hair(self):
  f=self.female
  # Scalp is behind brows; bangs carry the front outline without obscuring both eyes.
  self.zloft([(1.652,.099,.081,.007),(1.685,.096,.083,.008),
              (1.715,.073,.065,.012),(1.738,.038,.039,.014),(1.742,.007,.014,.015)],5,"Head",28)
  # Swept fringe from an off-centre part, leaving eye apertures clear.
  bangs=[
   ([(-.018,-.018,1.742),(-.026,-.069,1.710),(-.043,-.084,1.668),(-.070,-.080,1.639)],[.016,.025,.018,.001]),
   ([(.007,-.018,1.744),(.010,-.073,1.711),(-.012,-.088,1.674),(-.030,-.085,1.643)],[.018,.026,.016,.001]),
   ([(.025,-.01,1.743),(.040,-.064,1.708),(.064,-.080,1.674),(.075,-.073,1.635)],[.019,.026,.018,.001]),
   ([(.027,-.008,1.744),(.040,-.052,1.718),(.043,-.082,1.683),(.028,-.088,1.655)],[.012,.019,.012,.001]),
   ([(-.035,-.005,1.737),(-.060,-.052,1.706),(-.083,-.061,1.667),(-.096,-.045,1.627)],[.019,.024,.017,.001]),
  ]
  for i,(p,w) in enumerate(bangs):self.hair_strand(p,w,6 if i%2 else 5)
  # Crown and side locks, overlapping rather than a helmet shell.
  for sign in(-1,1):
   for i in range(7):
    t=i/6
    start=(sign*(.006+.015*t),.01+.035*t,1.741-.005*t)
    p1=(sign*(.064+.014*t),.028+.055*t,1.712-.02*t)
    p2=(sign*(.096+.01*t),.015+.072*t,1.652-.04*t)
    tip=(sign*(.103+.018*math.sin(i)),.024+.058*t,1.586-.028*t)
    self.hair_strand([start,p1,p2,tip],[.011,.024,.022,.001],5 if i%3 else 6)
   self.hair_strand([(sign*.08,-.017,1.69),(sign*.103,-.019,1.644),
                     (sign*.10,-.022,1.59),(sign*.118,-.012,1.553)],[.020,.021,.016,.001])
  if f:
   # Long asymmetrical, lightly waving locks behind the shoulders and down the back.
   for i in range(16):
    t=i/15
    x=-.101+.202*t
    sway=.018*math.sin(i*1.3)
    y=.063+.040*math.sin(t*math.pi)
    endz=1.09+.12*(.5+.5*math.sin(i*1.47))
    p=[(x*.55,y*.7,1.705),(x,y,1.61),(x*1.12+sway,y+.025,1.49),
       (x*1.14-sway,y+.041,1.36),(x*1.22+sway,y+.031,1.24),
       (x*1.3+sway*2,y+.020,endz)]
    self.hair_strand(p,[.015,.023,.026,.024,.018,.001],5 if i%3 else 6)
   for sign in(-1,1):
    self.hair_strand([(sign*.085,-.005,1.63),(sign*.118,-.033,1.55),
      (sign*.116,-.02,1.45),(sign*.151,-.008,1.38),(sign*.139,-.047,1.30),
      (sign*.161,-.03,1.245)],[.019,.022,.021,.019,.014,.001])
  else:
   for i in range(11):
    a=math.pi*i/10
    x=.096*math.cos(a);y=.087*math.sin(a)+.019
    self.hair_strand([(x*.4,y*.5,1.72),(x,y,1.665),(x*1.1,y+.008,1.597),
                      (x*1.23,y+.01,1.548+.02*math.sin(i))],[.014,.027,.021,.001],5 if i%2 else 6)
   for i in range(4):
    self.hair_strand([(-.016+i*.016,.023,1.729),(-.026+i*.019,.015,1.752),
                      (-.060+i*.020,-.012,1.758),(-.082+i*.027,-.018,1.741)],[.014,.018,.012,.001])
  # Navy bow and gold compass are a compact recognisable reference accent.
  x=-.106;y=.040;z=1.635
  self.panel([(x,y,z),(x-.035,y+.005,z+.047),(x-.051,y+.007,z+.006),(x-.008,y-.009,z-.01)],2,"Head")
  self.panel([(x,y,z),(x-.023,y+.007,z-.047),(x+.007,y+.008,z-.06),(x+.014,y-.005,z-.01)],2,"Head")
  self.compass((x-.012,y-.010,z),.027,"Head")
  self.panel([(x-.016,y+.013,z-.025),(x-.045,y+.018,z-.08),
              (x-.018,y+.022,z-.144),(x+.005,y+.008,z-.06)],2,"Head")

 def skeleton(self):
  name=self.name
  arm=bpy.data.armatures.new(name+"_Humanoid")
  obj=bpy.data.objects.new(name+"_Rig",arm);bpy.context.collection.objects.link(obj)
  bpy.context.view_layer.objects.active=obj;obj.select_set(True)
  bpy.ops.object.mode_set(mode="EDIT")
  records=[
   ("Root",(0,0,0),(0,0,.12),None),
   ("Hips",(0,0,.935),(0,0,1.04),"Root"),
   ("Spine",(0,0,1.04),(0,0,1.21),"Hips"),
   ("Chest",(0,0,1.21),(0,0,1.414),"Spine"),
   ("Neck",(0,0,1.414),(0,0,1.50),"Chest"),
   ("Head",(0,0,1.50),(0,0,1.704),"Neck"),
  ]
  for sign,side in((1,"Left"),(-1,"Right")):
   records.extend([
    (side+"Shoulder",(0,0,1.392),(sign*.175,0,1.392),"Chest"),
    (side+"UpperArm",(sign*.175,0,1.392),(sign*.44,0,1.382),side+"Shoulder"),
    (side+"LowerArm",(sign*.44,0,1.382),(sign*.665,0,1.375),side+"UpperArm"),
    (side+"Hand",(sign*.665,0,1.375),(sign*.746,0,1.375),side+"LowerArm"),
    (side+"UpperLeg",(sign*.084,0,.935),(sign*.089,-.012,.515),"Hips"),
    (side+"LowerLeg",(sign*.089,-.012,.515),(sign*.084,-.004,.095),side+"UpperLeg"),
    (side+"Foot",(sign*.084,-.004,.095),(sign*.084,-.103,.041),side+"LowerLeg"),
    (side+"Toes",(sign*.084,-.103,.041),(sign*.084,-.151,.037),side+"Foot"),
   ])
  records+=self.finger_bones
  for name,head,tail,parent in records:
   b=arm.edit_bones.new(name);b.head=head;b.tail=tail
   if parent:b.parent=arm.edit_bones[parent]
   # Local bone Y follows each limb. Blender export emits bone axes explicitly.
   b.use_deform=name!="Root"
  bpy.ops.object.mode_set(mode="OBJECT")
  obj.show_in_front=True;obj.select_set(False)
  return obj,records

 def finish(self):
  rig,records=self.skeleton()
  materials=[]
  for name,color,metal,rough,emit in DEFS:
   mat=bpy.data.materials.get(name) or bpy.data.materials.new(name)
   mat.diffuse_color=color;mat.use_nodes=True
   shader=mat.node_tree.nodes.get("Principled BSDF")
   shader.inputs["Base Color"].default_value=color
   shader.inputs["Roughness"].default_value=rough
   shader.inputs["Metallic"].default_value=metal
   shader.inputs["Emission Color"].default_value=color
   shader.inputs["Emission Strength"].default_value=emit
   mat["OrbisEmissionStrength"]=emit
   materials.append(mat)
  meshes=[];missing=set()
  # Hair curl is part of the intended height; use one shared metre-scale transform.
  allpoints=[p for d in self.data for p in d["v"]]
  lo=min(p.z for p in allpoints);hi=max(p.z for p in allpoints)
  target=1.70 if self.female else 1.74
  scale=target/(hi-lo)
  for bone in rig.data.bones:
   pass
  bpy.context.view_layer.objects.active=rig;rig.select_set(True)
  bpy.ops.object.mode_set(mode="EDIT")
  for bone in rig.data.edit_bones:
   bone.head=Vector((bone.head.x*scale,bone.head.y*scale,(bone.head.z-lo)*scale))
   bone.tail=Vector((bone.tail.x*scale,bone.tail.y*scale,(bone.tail.z-lo)*scale))
  bpy.ops.object.mode_set(mode="OBJECT");rig.select_set(False)
  for mi,d in enumerate(self.data):
   if not d["v"]:continue
   mesh=bpy.data.meshes.new(self.name+"_"+DEFS[mi][0])
   verts=[(v.x*scale,v.y*scale,(v.z-lo)*scale) for v in d["v"]]
   mesh.from_pydata(verts,[],d["f"]);mesh.update()
   obj=bpy.data.objects.new(self.name+"_"+DEFS[mi][0],mesh)
   bpy.context.collection.objects.link(obj);obj.parent=rig
   mesh.materials.append(materials[mi])
   for poly,smooth in zip(mesh.polygons,d["smooth"]):poly.use_smooth=smooth
   groups={name:obj.vertex_groups.new(name=name) for name in rig.data.bones.keys()}
   for i,w in enumerate(d["w"]):
    for name,value in w.items():
     if name not in groups:missing.add(name)
     else:groups[name].add([i],value,"REPLACE")
   mod=obj.modifiers.new("Humanoid Skin","ARMATURE");mod.object=rig;mod.use_deform_preserve_volume=True
   # Normals are recalculated after joining, without merging weights or applying the rig.
   bpy.context.view_layer.objects.active=obj;obj.select_set(True)
   bpy.ops.object.mode_set(mode="EDIT");bpy.ops.mesh.select_all(action="SELECT")
   bpy.ops.mesh.normals_make_consistent(inside=False)
   bpy.ops.object.mode_set(mode="OBJECT");obj.select_set(False)
   meshes.append(obj)
  assert not missing,missing
  triangles=sum(len(poly.vertices)-2 for obj in meshes for poly in obj.data.polygons)
  assert len(meshes)<=12,len(meshes)
  assert triangles<50000,triangles
  for obj in meshes:
   for vertex in obj.data.vertices:
    assert vertex.groups and abs(sum(g.weight for g in vertex.groups)-1)<.0001,"unweighted vertex"
  bpy.ops.object.select_all(action="DESELECT");rig.select_set(True)
  for obj in meshes:obj.select_set(True)
  bpy.context.view_layer.objects.active=rig
  path=OUT/(self.name+".fbx")
  bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,
    object_types={"MESH","ARMATURE"},axis_forward="-Z",axis_up="Y",
    apply_unit_scale=True,apply_scale_options="FBX_SCALE_UNITS",
    use_mesh_modifiers=True,mesh_smooth_type="FACE",add_leaf_bones=False,
    primary_bone_axis="Y",secondary_bone_axis="X",bake_anim=False,path_mode="AUTO")
  report={"name":self.name,"heightMetres":target,"triangles":triangles,"skinnedMeshes":len(meshes),
          "vertices":sum(len(o.data.vertices) for o in meshes),"armature":rig.name,
          "bones":[r[0] for r in records],"materials":[d[0] for d in DEFS],
          "fbx":str(path),"tPose":True,"forwardBlender":"-Y","upBlender":"Z",
          "limitations":["Original first character study; not an exact reconstruction of the illustration.",
                         "Static cape and hair skinning; no cloth or secondary-motion simulation.",
                         "No facial expression blendshapes; existing game Humanoid animation is retargeted in Unity."]}
  (RESULTS/(self.name+"_Model.json")).write_text(json.dumps(report,indent=2),encoding="utf-8")
  return rig,meshes,report

def aim(obj,point):
 obj.rotation_euler=(Vector(point)-obj.location).to_track_quat("-Z","Y").to_euler()
def studio(name):
 scene=bpy.context.scene
 world=bpy.data.worlds.new(name+"_StudioWorld");scene.world=world;world.use_nodes=True
 world.node_tree.nodes["Background"].inputs[0].default_value=(.24,.28,.34,1)
 world.node_tree.nodes["Background"].inputs[1].default_value=.55
 mat=bpy.data.materials.new("Studio Floor");mat.diffuse_color=(.20,.23,.27,1);mat.use_nodes=True
 mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(.20,.23,.27,1)
 mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value=.82
 mesh=bpy.data.meshes.new("Studio Floor")
 mesh.from_pydata([(-200,-200,-.004),(200,-200,-.004),(200,200,-.004),(-200,200,-.004)],[],[(0,1,2,3)])
 ob=bpy.data.objects.new("Studio Floor (not exported)",mesh);bpy.context.collection.objects.link(ob);mesh.materials.append(mat)
 for title,loc,power,color,size in [
  ("Soft key",(2.5,-4,4),450,(1,.89,.75),4),
  ("Blue fill",(-3,-2,2.3),260,(.68,.82,1),3),
  ("Rim",(0,2.5,3),550,(1,.86,.65),3)]:
  data=bpy.data.lights.new(title,"AREA");data.energy=power;data.color=color;data.size=size
  light=bpy.data.objects.new(title,data);bpy.context.collection.objects.link(light);light.location=loc;aim(light,(0,0,1))
 camera=bpy.data.objects.new("Portrait Camera",bpy.data.cameras.new("Portrait Camera"))
 bpy.context.collection.objects.link(camera);camera.location=(2.1,-5.5,2.55);aim(camera,(0,0,.91))
 camera.data.type="ORTHO";camera.data.ortho_scale=2.16;scene.camera=camera
 scene.render.engine="CYCLES";scene.cycles.samples=40;scene.cycles.use_denoising=True
 scene.render.threads_mode="FIXED";scene.render.threads=12
 scene.render.resolution_x=1250;scene.render.resolution_y=1450;scene.render.resolution_percentage=100
 scene.view_settings.view_transform="AgX"
 scene.render.image_settings.file_format="PNG";scene.render.film_transparent=False
 return camera

reports=[]
for name,female in (("Stella",True),("Polaris",False)):
 bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
 builder=Builder(name,female);builder.body();builder.face();builder.hair()
 rig,meshes,report=builder.finish()
 camera=studio(name)
 # Source is saved with the true rest pose. Posed studio renders do not alter exported FBX.
 bpy.context.view_layer.objects.active=rig
 bpy.ops.object.select_all(action="DESELECT");rig.select_set(True)
 bpy.ops.wm.save_as_mainfile(filepath=str(SOURCES/(name+".blend")))
 print("ORBIS_CHARACTER_STATS="+json.dumps(report),flush=True)
 # Gentle relaxed pose for the preview only; retains fingers and shoulders attached.
 for side,sign in (("Left",1),("Right",-1)):
  bone=rig.pose.bones[side+"UpperArm"];bone.rotation_mode="QUATERNION"
  rest=bone.bone.matrix_local.to_quaternion()
  # Convert a global downward arm swing to the authored bone's local coordinates.
  world_drop=Quaternion(Vector((0,1,0)),sign*math.radians(65))
  bone.rotation_quaternion=rest.inverted() @ world_drop @ rest
 bpy.context.view_layer.update()
 bpy.context.scene.render.filepath=str(RESULTS/("Island_"+name+".png"))
 bpy.ops.render.render(write_still=True)
 # Dedicated face preview makes eye/hairstyle review possible before Unity retargeting.
 if "--skip-face" not in sys.argv:
  camera.location=(.24,-2.8,1.70);aim(camera,(0,-.005,1.59));camera.data.ortho_scale=.48
  bpy.context.scene.render.resolution_x=1000;bpy.context.scene.render.resolution_y=1000
  bpy.context.scene.render.filepath=str(RESULTS/("Island_"+name+"_Face.png"))
  bpy.ops.render.render(write_still=True)
 camera.location=(1.6,5.5,2.30);aim(camera,(0,.03,.92));camera.data.ortho_scale=2.02
 bpy.context.scene.render.resolution_x=1150;bpy.context.scene.render.resolution_y=1400
 bpy.context.scene.render.filepath=str(RESULTS/("Island_"+name+"_Rear.png"))
 bpy.ops.render.render(write_still=True)
 reports.append(report)
print("ORBIS_EXPLORERS_COMPLETE="+json.dumps(reports),flush=True)
