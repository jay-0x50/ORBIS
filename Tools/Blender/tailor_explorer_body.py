"""Sculpted, skinned torso and garments for the supplied explorer turnarounds.

apply(name, female, rig) operates on an open face-pass source in its existing
metre scale. Bone names/rest poses and hand sockets stay intact. Authoring
dimensions below are visual defaults estimated from the small turnaround views,
not physical measurements of the drawings. Front is -Y, up is Z.
"""
import math
import bpy, bmesh
from mathutils import Vector

TAU=math.tau
IVORY='EX_TailoredIvory'; NAVY='EX_TailoredNavy'

def blend(a,b,t):
    t=max(0,min(1,t))
    return {a:1-t,b:t} if 0<t<1 else {a if t<=0 else b:1}

def torso_w(p):
    z=p[2]
    if z<1.04:return {'Hips':1}
    if z<1.17:return blend('Hips','Spine',(z-1.04)/.13)
    if z<1.32:return blend('Spine','Chest',(z-1.17)/.15)
    return {'Chest':1}

def arm_w(side,p):
    x=abs(p[0])
    if x<.245:return blend(side+'Shoulder',side+'UpperArm',(x-.15)/.095)
    if x<.40:return {side+'UpperArm':1}
    if x<.49:return blend(side+'UpperArm',side+'LowerArm',(x-.40)/.09)
    if x<.62:return {side+'LowerArm':1}
    return blend(side+'LowerArm',side+'Hand',(x-.62)/.065)

def leg_w(side,p):
    z=p[2]
    if z>.58:return {side+'UpperLeg':1}
    if z>.44:return blend(side+'UpperLeg',side+'LowerLeg',(.58-z)/.14)
    if z>.16:return {side+'LowerLeg':1}
    return blend(side+'LowerLeg',side+'Foot',(.16-z)/.07)

def skirt_w(p):
    # The waist remains attached to the hips; the hem follows each thigh enough
    # to clear the existing run/attack clips without adding cloth simulation.
    amount=max(0,min(1,(1.065-p[2])/.18))*.65
    left=max(0,min(1,(p[0]+.045)/.09))
    return {'Hips':1-amount,'LeftUpperLeg':amount*left,'RightUpperLeg':amount*(1-left)}

def spline(rows,t):
    pos=t*(len(rows)-1);i=min(int(pos),len(rows)-2);q=pos-i
    a=Vector(rows[max(0,i-1)]);b=Vector(rows[i]);c=Vector(rows[i+1]);d=Vector(rows[min(len(rows)-1,i+2)])
    return .5*((2*b)+(-a+c)*q+(2*a-5*b+4*c-d)*q*q+(-a+3*b-3*c+d)*q*q*q)

def interp(z,rows,col):
    for a,b in zip(rows,rows[1:]):
        if a[0]<=z<=b[0]:
            t=(z-a[0])/(b[0]-a[0]);t=t*t*(3-2*t)
            return a[col]+(b[col]-a[col])*t
    return rows[0 if z<rows[0][0] else -1][col]

def connected(mesh):
    links=[[] for _ in mesh.vertices]
    for e in mesh.edges:
        a,b=e.vertices;links[a].append(b);links[b].append(a)
    seen=set()
    for start in range(len(links)):
        if start in seen:continue
        stack=[start];seen.add(start);part=[]
        while stack:
            i=stack.pop();part.append(i)
            for j in links[i]:
                if j not in seen:seen.add(j);stack.append(j)
        yield part

def remove_old_body(rig):
    """Keep the new head and articulated glove palms/fingers; remove old coat cards."""
    removed=0
    for obj in list(bpy.data.objects):
        if obj.type!='MESH' or obj.parent!=rig:continue
        doomed=[]
        for part in connected(obj.data):
            weights={obj.vertex_groups[g.group].name for i in part for g in obj.data.vertices[i].groups if g.weight>.0001}
            keep=weights=={'Head'} or (weights and all(any(tag in w for tag in ('Hand','Proximal','Intermediate','Distal')) for w in weights))
            if not keep:doomed.extend(part)
        if doomed:
            removed+=len(doomed);bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
            bmesh.ops.delete(bm,geom=[bm.verts[i] for i in doomed],context='VERTS')
            bm.to_mesh(obj.data);bm.free();obj.data.update()
            if not len(obj.data.vertices):bpy.data.objects.remove(obj,do_unlink=True)
    return removed

class Tailor:
    def __init__(self,name,female,rig):
        self.name=name;self.female=female;self.rig=rig;self.parts={}
        head=rig.data.bones['Head'];self.scale=head.length/.204;self.offset=head.head_local.z-1.50*self.scale
        self.rings=[(.89,.100,.060),(.97,.128 if female else .119,.071),
                    (1.04,.103 if female else .109,.064),(1.105,.086 if female else .098,.054 if female else .063),
                    (1.18,.098 if female else .122,.067),(1.255,.119 if female else .149,.078),
                    (1.32,.134 if female else .158,.080),(1.375,.145 if female else .166,.070 if female else .077),
                    (1.405,.125 if female else .144,.057),(1.427,.052,.038)]

    def add(self,verts,faces,role,weights='Chest',uv=None):
        d=self.parts.setdefault(role,dict(v=[],f=[],w=[],uv=[]));offset=len(d['v'])
        for i,p in enumerate(verts):
            d['v'].append(tuple(p));w=weights(p) if callable(weights) else ({weights:1} if isinstance(weights,str) else weights)
            d['w'].append({k:v for k,v in w.items() if v>.000001})
            d['uv'].append(uv[i] if uv else (.025,.88))
        d['f'].extend(tuple(offset+i for i in f) for f in faces)

    def grid(self,fn,role,weights,nu=32,nv=32,uv_window=(0,0,1,1),flip=False,shell=False):
        verts=[];uv=[]
        for j in range(nv+1):
            for i in range(nu+1):
                u=i/nu;v=j/nv;verts.append(fn(u,v))
                uv.append((uv_window[0]+u*uv_window[2],uv_window[1]+(1-v)*uv_window[3]))
        faces=[(j*(nu+1)+i,j*(nu+1)+i+1,(j+1)*(nu+1)+i+1,(j+1)*(nu+1)+i) for j in range(nv) for i in range(nu)]
        if flip:faces=[tuple(reversed(f)) for f in faces]
        if shell:
            # Real thin cloth has a visible inner surface in gameplay, including the back view.
            original=len(verts);normal_sums=[Vector() for _ in verts]
            for face in faces:
                normal=(Vector(verts[face[1]])-Vector(verts[face[0]])).cross(Vector(verts[face[2]])-Vector(verts[face[0]])).normalized()
                for i in face:normal_sums[i]+=normal
            verts+= [tuple(Vector(p)-normal_sums[i].normalized()*.0014) for i,p in enumerate(list(verts))]
            uv+=list(uv);front_faces=list(faces);faces += [tuple(i+original for i in reversed(f)) for f in front_faces]
            boundary=list(range(nu+1))+[j*(nu+1)+nu for j in range(1,nv+1)]+[nv*(nu+1)+i for i in range(nu-1,-1,-1)]+[j*(nu+1) for j in range(nv-1,0,-1)]
            for a,b in zip(boundary,boundary[1:]+boundary[:1]):faces.append((a,b,b+original,a+original))
        self.add(verts,faces,role,weights,uv)
        return fn

    def tube(self,points,r=.0017,role='EX_Gold',weights='Chest',sides=8,steps=4,radii=None):
        # Parallel transported cross sections avoid twisted tubes at garment seams.
        n=max(2,(len(points)-1)*steps);verts=[];uv=[];old=None
        for j in range(n+1):
            t=j/n;p=spline(points,t)
            tangent=(spline(points,min(1,t+.001))-spline(points,max(0,t-.001))).normalized()
            if old is None:
                ref=Vector((0,0,1)) if abs(tangent.z)<.9 else Vector((0,1,0));width=tangent.cross(ref).normalized()
            else:width=(old-tangent*old.dot(tangent)).normalized()
            old=width;normal=width.cross(tangent).normalized()
            if radii:
                rr=spline([(x[0],x[1],0) for x in radii],t);rx=max(.001,rr.x);ry=max(.001,rr.y)
            else:rx=ry=r
            for k in range(sides+1):
                a=TAU*k/sides;verts.append(p+width*(rx*math.cos(a))+normal*(ry*math.sin(a)));uv.append((k/sides,1-t))
        faces=[(j*(sides+1)+k,j*(sides+1)+k+1,(j+1)*(sides+1)+k+1,(j+1)*(sides+1)+k) for j in range(n) for k in range(sides)]
        faces.extend([tuple(range(sides,-1,-1)),tuple(n*(sides+1)+k for k in range(sides+1))]);self.add(verts,faces,role,weights,uv)

    def ellipsoid(self,c,r,role,weights,rows=16,sides=32):
        self.grid(lambda u,v:(c[0]+r[0]*math.sin(math.pi*v)*math.cos(TAU*u),c[1]+r[1]*math.sin(math.pi*v)*math.sin(TAU*u),c[2]+r[2]*math.cos(math.pi*v)),role,weights,sides,rows)

    def ring(self,c,rx,ry,role='EX_Gold',weights='Chest',thickness=.0016):
        self.tube([(c[0]+rx*math.sin(TAU*i/40),c[1]-ry*math.cos(TAU*i/40),c[2]) for i in range(41)],thickness,role,weights,6,1)

    def front(self,x,z):
        rx=interp(z,self.rings,1);ry=interp(z,self.rings,2)
        front=math.sqrt(max(.02,1-(x/rx)**2))
        chest=.033*math.exp(-((abs(x)-.055)/.048)**2-((z-1.292)/.055)**2) if self.female else .012*math.exp(-((z-1.31)/.09)**2)
        return -ry*front-chest*front

    def gemstone(self,x,z,size=.008):
        y=self.front(x,z)-.005
        verts=[(x,y,z+size*1.9),(x+size,y,z),(x,y,z-size*1.9),(x-size,y,z),(x,y-size*.45,z)]
        self.add(verts,[(0,1,4),(1,2,4),(2,3,4),(3,0,4)],'EX_Gem',torso_w)
        self.tube([verts[0],verts[1],verts[2],verts[3],verts[0]],.0012,'EX_Gold',torso_w)

    def emblem(self,x,z,r=.027):
        y=self.front(x,z)-.009
        self.tube([(x+r*.67*math.sin(TAU*i/32),y,z+r*.67*math.cos(TAU*i/32)) for i in range(33)],.0012,'EX_Gold',torso_w,6,1)
        for k in range(8):
            a=TAU*k/8;dx=math.sin(a);dz=math.cos(a);length=r*(1 if k%2==0 else .68)
            pts=[(x+dx*length,y-.002,z+dz*length),(x+dz*r*.13,y-.001,z-dx*r*.13),(x,y-.005,z),(x-dz*r*.13,y-.001,z+dx*r*.13)]
            self.add(pts,[(0,1,2),(0,2,3)],'EX_Gold',torso_w)
        self.gemstone(x,z,r*.17)

    def torso(self):
        # Higher anatomical waist and curved chest replace the old long flat bib.
        def section(u,v):
            a=(u-.5)*TAU;z=1.015+.412*v;rx=interp(z,self.rings,1);ry=interp(z,self.rings,2)
            x=rx*math.sin(a);y=-ry*math.cos(a)
            if math.cos(a)>0:y=self.front(x,z)
            y+=.0014*math.sin(a*12+z*10)*math.exp(-((z-1.1)/.13)**2)
            return x,y,z
        # Ivory front sweeps around the bust; dark tailored side inserts define the waist.
        for role,a,b in [(IVORY,.36,.64),(NAVY,0,.36),(NAVY,.64,1)]:
            self.grid(lambda u,v,a=a,b=b:section(a+(b-a)*u,1-v),role,torso_w,32,60,(0,.42,1,.58) if role==NAVY else (0,0,1,1),flip=True)
        for sign in (-1,1):
            points=[]
            for i in range(32):
                z=1.045+.347*i/31;x=sign*interp(z,self.rings,1)*.77;points.append((x,self.front(x,z)-.0015,z))
            self.tube(points,.0018,'EX_Gold',torso_w,6,1)
            # Fine bodice seam/lapel follows the actual rounded front, not a flat plate.
            seam=[(sign*x,z) for x,z in [(.024,1.40),(.055,1.35),(.035,1.255),(.025,1.18),(.029,1.10)]]
            self.tube([(x,self.front(x,z)-.002,z) for x,z in seam],.0012,'EX_Gold',torso_w)
        if self.female:
            seam=[(-.080,1.29),(-.055,1.260),(-.027,1.262),(0,1.275),(.027,1.262),(.055,1.260),(.080,1.29)]
            self.tube([(x,self.front(x,z)-.0025,z) for x,z in seam],.0014,'EX_Gold',torso_w)
            for sign in (-1,1):
                # Small fabric straps connect the off-shoulder sleeves to the bodice.
                points=[(sign*.062,-.047,1.409),(sign*.106,-.029,1.426),(sign*.147,-.005,1.427),(sign*.193,.005,1.407)]
                self.tube(points,role=NAVY,weights='Chest',sides=12,steps=5,radii=[(.009,.002)]*4)
                self.tube([tuple(Vector(p)+Vector((0,-.002,.002))) for p in points],.0012,'EX_Gold','Chest')
        if not self.female:
            self.grid(lambda u,v:((u-.5)*.10*(1-v),self.front((u-.5)*.10*(1-v),1.40-.18*v)-.002,1.40-.18*v),NAVY,torso_w,18,28,(.15,.52,.7,.45),flip=True)
        # Continuous neck flares into collarbones, with very little exposed cylinder.
        # Closed end caps give reliable outward winding for Unity's back-face culling and outline hull.
        self.tube([(0,0,1.390),(0,0,1.420),(0,0,1.450),(0,.010,1.486),(0,.020,1.530)],
                  role='EX_Skin',weights=lambda p:blend('Chest','Neck',(p[2]-1.405)/.036),sides=40,steps=8,
                  radii=[(.068,.045),(.048,.036),(.030,.030),(.030,.032),(.037,.036)])
        self.grid(lambda u,v:((.049-.012*v)*math.sin(TAU*u),-(.041-.006*v)*math.cos(TAU*u),1.414+.049*v),NAVY,'Neck',40,16,(0,.62,1,.38))
        self.ring((0,0,1.463),.037,.035,'EX_Gold','Neck',.0014)
        self.emblem(-.063,1.381,.028);self.gemstone(.023,1.353,.007)
        for z in (1.272,1.205,1.142):self.gemstone(0,z,.0038)
        for side in (-1,1):
            chain=[(side*.035,1.405),(side*.068,1.378),(side*.085,1.355),(side*.048,1.336),(0,1.343)]
            self.tube([(x,self.front(x,z)-.005,z) for x,z in chain],.0010,'EX_Gold',torso_w,6)

    def waist(self):
        waist=1.095 if self.female else 1.083
        def belt(u,v):
            a=TAU*u;x=.104*math.sin(a);z=waist+(v-.5)*.033-.12*x
            return x,-.069*math.cos(a),z
        self.grid(belt,'EX_Leather',torso_w,64,5)
        for v in (0,1):self.tube([belt(i/64,v) for i in range(65)],.0012,'EX_Gold',torso_w,6,1)
        x=.016;y=-.071;z=waist-.002
        frame=[(x-.023,y,z-.019),(x+.023,y,z-.019),(x+.023,y,z+.019),(x-.023,y,z+.019),(x-.023,y,z-.019)]
        self.tube(frame,.003,'EX_Gold',torso_w,8)
        for sign in (-1,1):
            for i in range(3):
                x=sign*(.052+i*.012);z=waist-.12*x
                self.ellipsoid((x,-math.sqrt(max(.001,1-(x/.106)**2))*.071-.001,z),(.0017,.001,.0017),'EX_Gold',torso_w,6,8)
            self.ellipsoid((sign*.11,.016,waist-.045),(.025,.021,.040),'EX_Leather','Hips',12,20)
        if self.female:
            # High short skirt with soft pleats: thighs are no longer hidden behind wide knee-length cards.
            def skirt(u,v):
                a=TAU*u;pleat=.007*v*math.cos(16*a);rx=.120+.055*v+pleat;ry=.081+.035*v+pleat*.6
                return rx*math.sin(a),-ry*math.cos(a),1.07-.188*v+.010*v*math.cos(8*a)
            self.grid(skirt,NAVY,skirt_w,128,24,(0,.45,1,.55),flip=True,shell=True)
            self.tube([skirt(i/128,1) for i in range(129)],.0017,'EX_Gold',skirt_w,6,1)
        else:
            self.tube([(0,0,1.072),(0,0,1.01),(0,.007,.94),(0,.01,.89)],role='EX_Trousers',weights='Hips',sides=48,steps=7,radii=[(.108,.066),(.133,.076),(.15,.075),(.145,.068)])
        # Slim asymmetric ivory front tails, independently curved and skinned.
        for sign in (-1,1):
            bottom=(.735 if sign==1 else .80) if self.female else (.61 if sign==1 else .68)
            def panel(u,v,sign=sign,bottom=bottom):
                center=sign*(.031+.060*v);width=(.027+.019*math.sin(math.pi*v))*(1-.18*v)
                z=1.073-(1.073-bottom)*v+.08*v*(u if sign<0 else 1-u)
                x=center+(u-.5)*width*2;y=-.078-.046*v-.006*math.sin(math.pi*u)*math.sin(math.pi*v)
                return x,y,z
            panel_weights=skirt_w if self.female else 'Hips'
            self.grid(panel,IVORY,panel_weights,24,45,flip=True,shell=True)
            self.tube([panel(0,i/40) for i in range(41)]+[panel(i/24,1) for i in range(1,25)]+[panel(1,1-i/40) for i in range(1,41)],.0015,'EX_Gold',panel_weights,6,1)

    def cape(self):
        # Curved shoulder wrap and two draped tails replace the old triangle-fan cape cards.
        def mantle(u,v):
            a=-2.70+5.40*u;rx=.045+.171*math.sin(v*math.pi*.5);ry=.036+.068*v
            z=1.435-.15*v-.02*v*math.cos(a)
            return rx*math.sin(a),ry*math.cos(a)-(.012*math.sin(v*math.pi*.5) if math.cos(a)<0 else 0),z
        self.grid(mantle,NAVY,'Chest',72,30,(0,.48,1,.52),shell=True)
        self.tube([mantle(i/72,1) for i in range(73)],.0020,'EX_Gold','Chest',6,1)
        for sign in (-1,1):
            bottom=.37 if sign<0 else .47
            def cloth(u,v,sign=sign,bottom=bottom):
                x=sign*(.018+.185*u+.038*v+.016*math.sin(v*math.pi))
                y=.108+.092*v+.009*math.sin(u*math.pi*3+v*.6)*(.3+v)
                z=1.348-(1.348-bottom)*v+.10*v*(u-.7)**2
                return x,y,z
            self.grid(cloth,NAVY,lambda p:torso_w((p[0],p[1],max(1.025,p[2]))),38,64,flip=sign<0,shell=True)
            # Narrow ivory inset is visible along the open outer edge, without a solid billboard backing.
            self.grid(lambda u,v:tuple(Vector(cloth(.88+.12*u,v))+Vector((0,-.002,0))),IVORY,lambda p:torso_w((p[0],p[1],max(1.025,p[2]))),8,64)
            edge=[cloth(0,i/50) for i in range(51)]+[cloth(i/30,1) for i in range(1,31)]+[cloth(1,1-i/50) for i in range(1,51)]
            self.tube(edge,.0019,'EX_Gold',lambda p:torso_w((p[0],p[1],max(1.025,p[2]))),6,1)

    def limbs(self):
        for sign,side in ((1,'Left'),(-1,'Right')):
            aw=lambda p,side=side:arm_w(side,p);lw=lambda p,side=side:leg_w(side,p)
            def shoulder_weights(p,side=side):
                x=abs(p[0])
                if x<.145:return blend('Chest',side+'Shoulder',(x-.055)/.09)
                return arm_w(side,p)
            # Continuous collarbone-to-deltoid bridge hides the old disconnected sleeve root caps.
            shoulder_points=[(sign*.045,0,1.405),(sign*.105,0,1.398),(sign*.172,0,1.393),(sign*.230,0,1.389)]
            shoulder_radii=[(.032,.035),(.048,.049),(.052,.054),(.049,.050)]
            self.tube(shoulder_points,role='EX_Skin' if self.female else IVORY,weights=shoulder_weights,sides=40,steps=7,radii=shoulder_radii)
            # Smooth underlying upper arm, then a gathered sleeve with a slim cuff.
            xs=[.14,.20,.28,.36,.44,.49];zs=[1.392,1.392,1.389,1.385,1.382,1.381]
            self.tube([(sign*x,0,z) for x,z in zip(xs,zs)],role='EX_Skin',weights=aw,sides=32,steps=5,radii=[(.047,.049),(.047,.050),(.042,.044),(.037,.038),(.033,.035),(.034,.035)])
            if self.female:
                xs=[.235,.278,.333,.393,.440,.464];rr=[.049,.057,.063,.061,.047,.038]
            else:
                xs=[.151,.214,.286,.354,.418,.466];rr=[.046,.052,.055,.052,.046,.040]
            points=[(sign*x,0,1.397-.035*(x-.15)) for x in xs]
            self.tube(points,role=IVORY,weights=aw,sides=48,steps=8,radii=[(r,r*(1.02 if self.female else 1)) for r in rr])
            self.tube([(sign*.458,0,1.383),(sign*.481,0,1.382)],role=NAVY,weights=aw,sides=32,steps=3,radii=[(.040,.041),(.036,.037)])
            for xx,r in ((.459,.041),(.48,.037)):
                self.tube([(sign*xx,r*math.cos(TAU*k/32),1.383+r*math.sin(TAU*k/32)) for k in range(33)],.0016,'EX_Gold',aw,6,1)
            self.tube([(sign*.475,0,1.382),(sign*.525,0,1.380),(sign*.59,0,1.378),(sign*.665,0,1.375)],role='EX_Leather',weights=aw,sides=32,steps=6,radii=[(.035,.037),(.036,.037),(.030,.032),(.025,.028)])
            for xx,r in ((.50,.038),(.63,.03)):
                self.tube([(sign*xx,r*math.cos(TAU*k/32),1.38+r*math.sin(TAU*k/32)) for k in range(33)],.0018,'EX_Gold',aw,6,1)
            # Higher short skirt exposes the anatomical taper from thigh to knee.
            x=sign*.084
            skin_points=[(x,0,.925),(x*1.02,0,.86),(x*1.04,-.002,.73),(x*1.06,-.012,.515),(x*1.04,.011,.40),(x,.016,.305)]
            skin_radii=[(.062,.063),(.066,.067),(.056,.057),(.038,.041),(.043,.047),(.033,.038)]
            if self.female and sign==1:
                # Delete unseen skin below the stocking lip instead of layering nearly coplanar meshes.
                skin_points=skin_points[:3]+[(x*1.04,-.003,.686)];skin_radii=skin_radii[:3]+[(.052,.054)]
            self.tube(skin_points,role='EX_Skin' if self.female else 'EX_Trousers',weights=lw,sides=40,steps=7,radii=skin_radii)
            if self.female and sign==1:
                self.tube([(x*1.02,0,.71),(x*1.04,-.005,.62),(x*1.06,-.012,.515),(x*1.04,.011,.40),(x,.016,.30)],role=NAVY,weights=lw,sides=40,steps=6,radii=[(.057,.059),(.049,.051),(.040,.043),(.045,.049),(.035,.04)])
            if self.female and sign==-1:
                self.tube([(x*1.02,0,.785),(x*1.02,0,.768)],role='EX_Leather',weights=lw,sides=36,steps=3,radii=[(.064,.065),(.064,.065)])
            top=.33 if self.female else .385
            self.tube([(x,.015,top),(x,.014,.26),(x,-.004,.16),(x,-.007,.10),(x,-.007,.071)],role=IVORY,weights=lw,sides=36,steps=6,radii=[(.046,.050),(.037,.042),(.030,.035),(.031,.037),(.031,.034)])
            self.tube([(x,.015,.076),(x,-.030,.063),(x,-.087,.040),(x,-.137,.026)],role=IVORY,weights=side+'Foot',sides=32,steps=6,radii=[(.031,.033),(.035,.036),(.034,.025),(.011,.014)])
            self.tube([(x,.016,.037),(x,-.040,.026),(x,-.097,.014),(x,-.141,.013)],role='EX_Leather',weights=side+'Foot',sides=28,steps=6,radii=[(.031,.021),(.037,.015),(.037,.012),(.012,.012)])
            for s in (-1,1):
                self.tube([(x+s*.032,-.036,top),(x+s*.028,-.033,.22),(x+s*.023,-.039,.12),(x+s*.025,-.081,.058)],.0020,'EX_Gold',lw)
            # Fine angular boot piping instead of large round clock gears.
            self.tube([(x-.026,-.041,top-.015),(x,-.051,top-.085),(x+.026,-.041,top-.015)],.0020,'EX_Gold',lw)

    def finish(self,texture_root):
        materials={}
        for role in self.parts:
            material=bpy.data.materials.get(role) or bpy.data.materials.new(role);materials[role]=material
            if role=='EX_Trousers':
                material.diffuse_color=(.045,.052,.068,1);material.use_nodes=True
                material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=material.diffuse_color
                material.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.85
            if role in (IVORY,NAVY):
                filename='Body_Ivory_BaseColor.png' if role==IVORY else 'Body_Navy_BaseColor.png'
                material.use_nodes=True;nodes=material.node_tree.nodes;shader=nodes.get('Principled BSDF')
                image=bpy.data.images.load(str(texture_root/filename),check_existing=True)
                node=nodes.new('ShaderNodeTexImage');node.image=image;node.extension='EXTEND'
                material.node_tree.links.new(node.outputs['Color'],shader.inputs['Base Color']);shader.inputs['Roughness'].default_value=.68
                material.diffuse_color=(1,1,1,1)
        for role,d in self.parts.items():
            mesh=bpy.data.meshes.new(self.name+'_Tailored_'+role)
            mesh.from_pydata([(p[0]*self.scale,p[1]*self.scale,p[2]*self.scale+self.offset) for p in d['v']],[],d['f']);mesh.update()
            obj=bpy.data.objects.new(mesh.name,mesh);bpy.context.collection.objects.link(obj);obj.parent=self.rig;mesh.materials.append(materials[role])
            uv=mesh.uv_layers.new(name='UVMap')
            for poly in mesh.polygons:
                poly.use_smooth=True
                for li in poly.loop_indices:uv.data[li].uv=d['uv'][mesh.loops[li].vertex_index]
            groups={name:obj.vertex_groups.new(name=name) for name in self.rig.data.bones.keys()}
            for i,weights in enumerate(d['w']):
                assert abs(sum(weights.values())-1)<.0001
                for bone,weight in weights.items():groups[bone].add([i],weight,'REPLACE')
            bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
            if role=='EX_Trousers':
                # Unite pelvis and thigh surfaces to remove hard tube caps at the hip joint.
                bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
                remesh=obj.modifiers.new('Continuous trouser crotch','REMESH');remesh.mode='VOXEL';remesh.voxel_size=.005*self.scale;remesh.use_smooth_shade=True
                bpy.ops.object.modifier_apply(modifier=remesh.name)
                smooth=obj.modifiers.new('Tailored trouser smoothing','SMOOTH');smooth.factor=.65;smooth.iterations=4
                bpy.ops.object.modifier_apply(modifier=smooth.name);obj.vertex_groups.clear()
                groups={name:obj.vertex_groups.new(name=name) for name in self.rig.data.bones.keys()}
                for vertex in obj.data.vertices:
                    p=(vertex.co.x/self.scale,vertex.co.y/self.scale,(vertex.co.z-self.offset)/self.scale);side='Left' if p[0]>=0 else 'Right'
                    weights=blend(side+'UpperLeg','Hips',(p[2]-.86)/.12) if p[2]>.86 else leg_w(side,p)
                    for bone,weight in weights.items():
                        if weight>0:groups[bone].add([vertex.index],weight,'REPLACE')
                for poly in obj.data.polygons:poly.use_smooth=True
                obj.select_set(False)
            mod=obj.modifiers.new('Humanoid Skin','ARMATURE');mod.object=self.rig;mod.use_deform_preserve_volume=True
        return {'beltHeightAuthoring':1.095 if self.female else 1.083,'waistHalfWidth':.086 if self.female else .098,'chestHalfWidth':.134 if self.female else .158,'newVertices':sum(len(d['v']) for d in self.parts.values())}

def apply(name,female,rig,texture_root):
    removed=remove_old_body(rig);builder=Tailor(name,female,rig)
    builder.torso();builder.waist();builder.cape();builder.limbs();report=builder.finish(texture_root)
    report['removedBodyVertices']=removed
    return report
