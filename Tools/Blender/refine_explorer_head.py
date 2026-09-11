"""Geometry-only second sculpt pass for the original Orbis explorer heads.

Integration: open an unmodified delivered source, then apply(name, female, rig).
The function does not move bones, alter body vertices, save, export, or edit PNGs.
All Head-only geometry receives the agreed (.95,.95,.93) transform about authoring
(0,0,1.50), followed by -0.015 authoring metres in Z. This is coordinated with the
new neck/collar and must not be applied twice to the same source.

Standalone execution writes only Tools/Blender/HeadRefine-preview sources/renders.
"""
import bpy, bmesh, math, json, sys
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT=Path(__file__).resolve().parents[2]
HEAD_SCALE=(.95,.95,.93)
HEAD_Z_OFFSET=-.015

def smoothstep(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)))
    return t*t*(3-2*t)

def profile(z,table,column):
    for i in range(len(table)-1):
        if table[i][0]<=z<=table[i+1][0]:
            t=(z-table[i][0])/(table[i+1][0]-table[i][0])
            a=table[max(0,i-1)][column];b=table[i][column]
            c=table[i+1][column];d=table[min(len(table)-1,i+2)][column]
            return .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)
    return table[0 if z<table[0][0] else -1][column]

def face_v(v,female):
    pairs=([(0,0),(.25,.262),(.42,.405),(.60,.575),(.72,.715),(1,1)] if female
           else [(0,0),(.25,.300),(.42,.420),(.60,.648),(.72,.725),(1,1)])
    for (a,b),(c,d) in zip(pairs,pairs[1:]):
        if a<=v<=c:return b+(v-a)/(c-a)*(d-b)
    return v

def is_head(vertex,obj):
    return len(vertex.groups)==1 and obj.vertex_groups[vertex.groups[0].group].name=='Head' and vertex.groups[0].weight>.9999

def mesh_object(name,vertices,faces,uvs,materials,rig,scale,offset,indices=None):
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata([(x*scale,y*scale,z*scale+offset) for x,y,z in vertices],[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.parent=rig
    for material in materials:mesh.materials.append(material)
    layer=mesh.uv_layers.new(name='UVMap')
    for polygon in mesh.polygons:
        polygon.use_smooth=True
        if indices:polygon.material_index=indices[polygon.index]
        for li in polygon.loop_indices:layer.data[li].uv=uvs[mesh.loops[li].vertex_index]
    head=obj.vertex_groups.new(name='Head');head.add(list(range(len(vertices))),1,'REPLACE')
    arm=obj.modifiers.new('Humanoid Skin','ARMATURE');arm.object=rig;arm.use_deform_preserve_volume=True
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    return obj

def face_mesh(name,female,rig,scale,offset):
    # Radius, front depth and occipital depth are independent. The old circular
    # lower rings withdrew the chin into the neck and produced an egg-shaped mask.
    table=[(1.497,.003,.049,.005),(1.505,.023 if female else .028,.065,.025),
           (1.519,.043 if female else .048,.068,.044),(1.535,.059 if female else .064,.073,.060),
           (1.551,.067 if female else .071,.077,.072),(1.568,.076 if female else .080,.080,.081),
           (1.577,.079 if female else .083,.078,.084),
           (1.610,.086 if female else .090,.079,.094),(1.646,.089 if female else .092,.082,.101),
           (1.682,.085,.083,.099),(1.713,.063,.072,.082),(1.735,.027,.039,.044),(1.743,.002,.005,.008)]
    rows=98;sides=120;vs=[];uv=[]
    def gaussian(x,z,cx,cz,rx,rz):return math.exp(-((x-cx)/rx)**2-((z-cz)/rz)**2)
    for j in range(rows+1):
        z=table[0][0]+(table[-1][0]-table[0][0])*j/rows
        rx=profile(z,table,1);front_depth=profile(z,table,2);back_depth=profile(z,table,3)
        for k in range(sides+1):
            angle=-math.pi+math.tau*k/sides;c=math.cos(angle);x=rx*math.sin(angle)
            y=-(front_depth*c**.82) if c>=0 else back_depth*(-c)
            if c>0:
                # Low anime nose (female 14 mm, male 16 mm), not a detached cone.
                tip=(.014 if female else .016)*gaussian(x,z,0,1.586,.011,.017)
                bridge=(.005 if female else .0065)*gaussian(x,z,0,1.605,.008,.020)
                wings=.0026*(gaussian(x,z,-.009,1.577,.006,.005)+gaussian(x,z,.009,1.577,.006,.005))
                y-=(tip+bridge+wings)*c
                # Continuous cupid bow, philtrum and rounded lower lip. The mouth
                # remains closed; expression continues to come from the same PNG.
                y-=.0026*gaussian(x,z,0,1.551,.021,.0040)
                y-=.0022*gaussian(x,z,0,1.544,.018,.0042)
                y+=.0004*gaussian(x,z,0,1.548,.023,.0018)
                y+=.0006*gaussian(x,z,0,1.562,.006,.006)
                # Raised cheekbones taper into a smaller lower cheek and temple.
                y-=.003*(gaussian(x,z,-.049,1.590,.024,.019)+gaussian(x,z,.049,1.590,.024,.019))
                y+=.0018*(gaussian(x,z,-.073,1.639,.016,.022)+gaussian(x,z,.073,1.639,.016,.022))
                y+=.0015*(gaussian(x,z,-.0408,1.624,.025,.014)+gaussian(x,z,.0408,1.624,.025,.014))
            vs.append((x,y,z))
            u=.5+x/.204;v=(z-1.495)/.215
            # Six percent smaller eye impression without changing eye spacing,
            # iris centres, nose position, mouth position, or source texture pixels.
            eye_weight=math.exp(-((v-.60)/.085)**4)*smoothstep(.014,.023,abs(x))*(1-smoothstep(.069,.083,abs(x)))
            centre=.30 if x<0 else .70
            u+=(centre+(u-centre)/.94-u)*eye_weight
            if female:v+=(.60+(v-.60)/.96-v)*eye_weight
            uv.append((u,face_v(v,female)))
    faces=[];indices=[]
    for j in range(rows):
        for k in range(sides):
            a=j*(sides+1)+k;faces.append((a,a+1,a+sides+2,a+sides+1))
            indices.append(0 if math.cos(-math.pi+math.tau*(k+.5)/sides)>.04 else 1)
    faces.extend([tuple(range(sides,-1,-1)),tuple(rows*(sides+1)+k for k in range(sides+1))]);indices.extend([1,1])
    return mesh_object(name+'_EX_Face',vs,faces,uv,[bpy.data.materials['EX_Face'],bpy.data.materials['EX_Skin']],rig,scale,offset,indices)

class Ribbons:
    def __init__(self):self.v=[];self.f=[];self.uv=[];self.count=0
    def lock(self,points,widths,depth=.20):
        seed=self.count;self.count+=1;controls=[Vector(p) for p in points];path=[];sizes=[]
        for j in range(len(controls)-1):
            a=controls[max(0,j-1)];b=controls[j];c=controls[j+1];d=controls[min(len(controls)-1,j+2)]
            for step in range(7):
                t=step/7;path.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
                sizes.append(widths[j]*(1-t)+widths[j+1]*t)
        path.append(controls[-1]);sizes.append(.00015);start=len(self.v);sides=10;old=None
        for j,(p,w) in enumerate(zip(path,sizes)):
            n=(path[min(j+1,len(path)-1)]-path[max(0,j-1)]).normalized()
            if old is None:
                radial=Vector((p.x,p.y-.010,max(.01,(p.z-1.63)*.6))).normalized()
                across=n.cross(radial).normalized()
                if across.length<.01:across=n.cross(Vector((0,1,0))).normalized()
            else:across=(old-n*old.dot(n)).normalized()
            old=across;normal=across.cross(n).normalized()
            for k in range(sides+1):
                theta=math.tau*k/sides
                self.v.append(tuple(p+across*w*math.cos(theta)+normal*w*depth*math.sin(theta)))
                # Subsections of the existing flow texture avoid putting its full
                # strand pattern and highlight band on every small lock.
                self.uv.append(((seed*.381966)%1+k/sides*.16,
                                .10+(seed*.047)% .16+(1-j/(len(path)-1))*(.66+(seed%4)*.055)))
        for j in range(len(path)-1):
            for k in range(sides):
                a=start+j*(sides+1)+k;self.f.append((a,a+1,a+sides+2,a+sides+1))
        self.f.append(tuple(start+k for k in range(sides,-1,-1)))
        self.f.append(tuple(start+(len(path)-1)*(sides+1)+k for k in range(sides+1)))

def hair_mesh(name,female,rig,scale,offset):
    h=Ribbons()
    # Crown/back layers are irregular in their finish height and thickness.
    for sign in (-1,1):
        for i in range(7):
            t=i/6
            h.lock([(sign*(.006+.009*t),.002+.060*t,1.746-.012*t),
                    (sign*(.053+.022*t),.020+.078*t,1.725-.010*t),
                    (sign*(.094+.012*t),.034+.081*t,1.666-.025*t),
                    (sign*(.112+.008*math.sin(i*1.71)),.038+.070*t,1.581-.011*(i%3))],
                   [.010,.025,.025,.00015],.30)
    # Main fringe follows the illustration's side part and thin, uneven tips.
    bangs=[
      ([(.021,-.014,1.745),(.005,-.061,1.721),(-.038,-.083,1.686),(-.072,-.075,1.655),(-.092,-.049,1.633)],[.009,.018,.021,.010,.00015]),
      ([(-.001,-.010,1.745),(-.026,-.055,1.722),(-.063,-.078,1.685),(-.099,-.045,1.638)],[.009,.019,.018,.00015]),
      ([(.030,-.010,1.747),(.025,-.071,1.710),(.008,-.090,1.670),(-.007,-.091,1.639)],[.009,.020,.013,.00015]),
      ([(.040,-.005,1.745),(.055,-.065,1.716),(.065,-.077,1.678),(.071,-.060,1.635)],[.009,.020,.015,.00015]),
      ([(.052,.007,1.739),(.079,-.035,1.713),(.095,-.052,1.672),(.100,-.025,1.624)],[.008,.020,.014,.00015]),
      ([(-.025,.008,1.738),(-.065,-.031,1.711),(-.094,-.039,1.669),(-.110,-.012,1.603)],[.010,.021,.017,.00015]),
    ]
    for points,widths in bangs:h.lock(points,widths,.16)
    for points in [
        [(.019,-.027,1.742),(-.004,-.077,1.709),(-.032,-.092,1.680),(-.051,-.086,1.650)],
        [(.034,-.029,1.740),(.041,-.083,1.704),(.030,-.090,1.675),(.018,-.091,1.645)],
        [(-.018,-.022,1.739),(-.051,-.073,1.702),(-.078,-.084,1.674),(-.086,-.064,1.647)],
    ]:h.lock(points,[.002,.004,.003,.00015],.13)
    # Soft side locks, not a row of equal straight comb teeth at the ears.
    for sign in (-1,1):
        if female:
            h.lock([(sign*.080,.008,1.686),(sign*.104,-.012,1.635),(sign*.101,-.014,1.588),
                    (sign*.115,-.018,1.551),(sign*.109,-.035,1.516)],
                   [.010,.018,.014,.008,.00015],.20)
        else:
            h.lock([(sign*.080,.010,1.686),(sign*.101,-.010,1.632),
                    (sign*.103,-.012,1.589),(sign*.104,-.014,1.555)],
                   [.009,.016,.011,.00015],.20)
        h.lock([(sign*.091,.031,1.668),(sign*.114,.024,1.625),(sign*.119,.020,1.581),
                (sign*.128,.031,1.558)], [.011,.020,.015,.00015],.23)
    if female:
        for i in range(17):
            t=i/16;x=-.101+.202*t;y=.076+.035*math.sin(t*math.pi)
            s=.014*math.sin(i*1.72);end=1.105+.16*(.5+.5*math.sin(i*1.31))
            h.lock([(x*.53,y*.73,1.723),(x,y,1.637),(x*1.08+s,y+.027,1.516),
                    (x*1.25-s,y+.039,1.390),(x*1.39+s,y+.019,1.265),(x*1.48+s*1.5,y+.018,end)],
                   [.009,.020,.023,.021,.012,.00015],.25)
        for sign in (-1,1):
            for i in range(2):
                h.lock([(sign*(.089+i*.010),.024,1.633),(sign*(.111+i*.010),-.012+i*.020,1.556),
                        (sign*(.130+i*.011),-.002+i*.022,1.478),(sign*(.144+i*.012),.018+i*.023,1.397),
                        (sign*(.127+i*.020),-.007+i*.021,1.329),(sign*(.158+i*.018),.004+i*.024,1.265+i*.048)],
                       [.008,.014,.015,.013,.008,.00015],.18)
        h.lock([(-.012,.018,1.744),(-.002,.004,1.758),(.022,-.012,1.762),(.049,-.018,1.750)],
               [.001,.0017,.0011,.00015],.15)
    else:
        for i in range(11):
            a=math.pi*i/10;x=.092*math.cos(a);y=.090*math.sin(a)+.024
            h.lock([(x*.4,y*.52,1.732),(x,y,1.680),(x*1.13,y+.012,1.624),
                    (x*1.25,y+.007,1.562+.022*math.sin(i*1.49))],
                   [.009,.024,.022,.00015],.28)
        for p in [
            [(-.032,.015,1.735),(-.045,.003,1.752),(-.075,-.010,1.752),(-.094,-.021,1.737)],
            [(.021,.013,1.741),(.030,.009,1.755),(.056,-.005,1.754),(.074,-.013,1.741)],
        ]:h.lock(p,[.002,.0034,.002,.00015],.16)
    # Compact scalp volume behind the layered strands.
    cap=[(1.655,.097,.105),(1.687,.096,.109),(1.719,.074,.087),(1.743,.037,.047),(1.752,.002,.010)]
    start=len(h.v);rows=28;sides=72
    for j in range(rows+1):
        z=cap[0][0]+(cap[-1][0]-cap[0][0])*j/rows
        rx=profile(z,cap,1);ry=profile(z,cap,2)
        for k in range(sides+1):
            a=math.tau*k/sides
            # Back depth is larger than front to fit the fuller occipital shape.
            yy=ry*math.sin(a);yy*=.82 if yy<0 else 1
            h.v.append((rx*math.cos(a),.007+yy,z));h.uv.append((k/sides,j/rows*.8+.1))
    for j in range(rows):
        for k in range(sides):
            a=start+j*(sides+1)+k;h.f.append((a,a+1,a+sides+2,a+sides+1))
    obj=mesh_object(name+'_EX_Hair',h.v,h.f,h.uv,[bpy.data.materials['EX_Hair']],rig,scale,offset)
    return obj,h.count

def apply(name,female,rig):
    """Replace face/hair and return measurements; caller owns save/export."""
    if rig.get('OrbisHeadRefineApplied'):raise RuntimeError('Head refine must start from an unmodified source.')
    head=rig.data.bones['Head'];scale=head.length/.204;offset=head.head_local.z-1.50*scale
    bone_matrices={b.name:tuple(tuple(row) for row in b.matrix_local) for b in rig.data.bones}
    def body_coordinates():
        values={}
        for obj in bpy.data.objects:
            if obj.type!='MESH' or obj.parent!=rig:continue
            points=tuple((v.index,tuple(v.co)) for v in obj.data.vertices if not is_head(v,obj))
            if points:values[obj.name]=points
        return values
    original_body=body_coordinates()
    old_head=[o for o in bpy.data.objects if o.type=='MESH' and o.parent==rig and o.name in (name+'_EX_Face',name+'_EX_Hair',name+'_EX_HairLight')]
    for obj in old_head:bpy.data.objects.remove(obj,do_unlink=True)
    face=face_mesh(name,female,rig,scale,offset);hair,lock_count=hair_mesh(name,female,rig,scale,offset)
    changed=0
    for obj in bpy.data.objects:
        if obj.type!='MESH' or obj.parent!=rig:continue
        mat=obj.data.materials[0].name if obj.data.materials else ''
        for vertex in obj.data.vertices:
            if not is_head(vertex,obj):continue
            x=vertex.co.x/scale;y=vertex.co.y/scale;z=(vertex.co.z-offset)/scale
            # Keep the original ears but reduce their oversized, oval primitive profile.
            if mat=='EX_Skin' and abs(x)>.085 and 1.545<z<1.626:
                sign=1 if x>0 else -1
                x=sign*(.098+(abs(x)-.104)*.83);y=.010+(y-.002)*.78;z=1.586+(z-1.586)*.82
            if mat in ('EX_Gold','EX_Gem') and abs(x)>.096 and y<.001 and 1.510<z<1.573:
                x-=math.copysign(.006,x);z+=.003
            x*=HEAD_SCALE[0];y*=HEAD_SCALE[1];z=1.50+(z-1.50)*HEAD_SCALE[2]+HEAD_Z_OFFSET
            vertex.co=(x*scale,y*scale,z*scale+offset);changed+=1
        obj.data.update()
    assert bone_matrices=={b.name:tuple(tuple(row) for row in b.matrix_local) for b in rig.data.bones}
    assert len(bone_matrices)==52
    assert original_body==body_coordinates(),'Head module changed non-Head geometry'
    rig['OrbisHeadRefineApplied']=True
    return {'name':name,'headScale':HEAD_SCALE,'authoringHeadZOffset':HEAD_Z_OFFSET,'boneCount':52,
            'boneRestTransformsUnchanged':True,'nonHeadGeometryUnchanged':True,'headVerticesChanged':changed,'hairLocks':lock_count,
            'faceTriangles':sum(len(p.vertices)-2 for p in face.data.polygons),
            'hairTriangles':sum(len(p.vertices)-2 for p in hair.data.polygons),
            'texturePixelsChanged':False,'noseProjectionAuthoringMm':14 if female else 16}

def preview():
    out=ROOT/'Tools/Blender/HeadRefine-preview';out.mkdir(parents=True,exist_ok=True)
    for name,female in [('Stella',True),('Polaris',False)]:
        if '--only-stella' in sys.argv and not female:continue
        if '--only-polaris' in sys.argv and female:continue
        bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Tools/Blender/Sources'/(name+'.blend')))
        rig=bpy.data.objects[name+'_Rig'];report=apply(name,female,rig)
        bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
        scale=rig.data.bones['Head'].length/.204;offset=rig.data.bones['Head'].head_local.z-1.50*scale
        for side,sign in [('Left',1),('Right',-1)]:
            bone=rig.pose.bones[side+'UpperArm'];bone.rotation_mode='QUATERNION';rest=bone.bone.matrix_local.to_quaternion()
            bone.rotation_quaternion=rest.inverted()@Quaternion(Vector((0,1,0)),sign*math.radians(65))@rest
        bpy.context.view_layer.update()
        scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=True
        scene.render.threads_mode='FIXED';scene.render.threads=12
        scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
        camera=scene.camera;target=Vector((0,-.012,1.50+(1.612-1.50)*.93-.015))*scale+Vector((0,0,offset))
        for label,vector in [('Front',(0,-3,.045)),('ThreeQuarter',(1.8,-3,.050)),('Profile',(3,-.001,.025))]:
            camera.location=target+Vector(vector);camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=.365
            scene.render.filepath=str(out/(name+'_'+label+'.png'));bpy.ops.render.render(write_still=True)
        (out/(name+'_report.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
        print('HEAD_REFINE='+json.dumps(report),flush=True)

if __name__=='__main__':preview()
