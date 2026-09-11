"""Regenerate committed, editable Unity Shader Graph 17 graphs (Python stdlib only).
Numeric defaults below are M3 prototype art choices, not combat/gameplay tuning.
Run from any directory; generated graph object IDs are stable across regeneration.
"""
import json
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Resources/M3/Shaders'
NS = uuid.UUID('f0590a04-b029-453e-970e-cbd12e93089c')

def uid(key): return uuid.uuid5(NS, key).hex
def ref(key): return {'m_Id': key}
def vec(n, value=0): return dict(zip('xyzw'[:n], [value] * n))
def rgb(value): return dict(zip('rgba', [int(value[i:i+2], 16) / 255 for i in (0,2,4)] + [1]))

class Graph:
    def __init__(self, name, face=0):
        self.name, self.objects, self.nodes, self.properties, self.edges = name, [], [], [], []
        self.vertices, self.fragments = [], []
        self.face = face
    def obj(self, key, kind, **kw):
        o = dict(m_SGVersion=0, m_Type='UnityEditor.ShaderGraph.' + kind, m_ObjectId=uid(self.name + key))
        o.update(kw); self.objects.append(o); return o
    def slot(self, key, index, name, n, output=False, stage=3, kind=None, value=None, **kw):
        value = value if value is not None else (0.0 if n == 1 else vec(n))
        return self.obj(key, kind or f'Vector{n}MaterialSlot', m_Id=index, m_DisplayName=name,
            m_SlotType=int(output), m_Hidden=False, m_ShaderOutputName=name.replace(' ', ''),
            m_StageCapability=stage, m_Value=value, m_DefaultValue=value, m_Labels=[], **kw)
    def node(self, key, kind, name, slots, x=0, y=0, **kw):
        o = self.obj(key, kind, m_Group=ref(''), m_Name=name,
            m_DrawState={'m_Expanded':True, 'm_Position':{'serializedVersion':'2','x':x,'y':y,'width':230,'height':150}},
            m_Slots=[ref(s['m_ObjectId']) for s in slots], synonyms=[], m_Precision=0,
            m_PreviewExpanded=True, m_DismissedVersion=0, m_PreviewMode=0,
            m_CustomColors={'m_SerializableColors':[]}, **kw)
        self.nodes.append(ref(o['m_ObjectId'])); return o
    def edge(self, source, source_slot, dest, dest_slot):
        self.edges.append({'m_OutputSlot':{'m_Node':ref(source['m_ObjectId']),'m_SlotId':source_slot},
                           'm_InputSlot':{'m_Node':ref(dest['m_ObjectId']),'m_SlotId':dest_slot}})
    def property(self, name, value, color=False, y=0):
        key = name.replace(' ', '')
        prop = self.obj('prop'+key, 'Internal.ColorShaderProperty' if color else 'Internal.Vector1ShaderProperty',
            m_Guid={'m_GuidSerialized':str(uuid.UUID(uid(self.name+'guid'+key)))},m_Name=name,
            m_DefaultRefNameVersion=1, m_RefNameGeneratedByDisplayName=name,
            m_DefaultReferenceName='_'+key, m_OverrideReferenceName='_'+key,
            m_GeneratePropertyBlock=True, m_UseCustomSlotLabel=False,m_CustomSlotLabel='',
            m_DismissedVersion=0,m_Precision=0,overrideHLSLDeclaration=False,hlslDeclarationOverride=0,
            m_Hidden=False,m_PerRendererData=False,m_customAttributes=[],m_Value=value)
        prop['m_SGVersion'] = 3 if color else 1
        if color: prop.update(isMainColor=False, m_ColorMode=0)
        else: prop.update(m_FloatType=1,m_RangeValues={'x':0.0,'y':0.15 if name=='Thickness' else 1.0},m_LiteralFloatMode=False)
        self.properties.append(ref(prop['m_ObjectId']))
        slot = self.slot('propslot'+key,0,'Out',4 if color else 1,True)
        return self.node('propnode'+key,'PropertyNode',name,[slot],-700,y,m_Property=ref(prop['m_ObjectId']))
    def geometry(self, kind, n=3, space=None, y=0):
        key = kind + str(space)
        slot = self.slot('geomslot'+key,0,'Out',n,True)
        kw = {}
        if space is not None: kw['m_Space'] = space
        if kind == 'UVNode': kw['m_OutputChannel'] = 0
        if kind == 'PositionNode': kw['m_PositionSource'] = 0
        o = self.node('geom'+key,kind,kind.replace('Node',''),[slot],-420,y,**kw)
        if kind in ('PositionNode','ViewDirectionNode'): o['m_SGVersion']=1
        return o
    def block(self, name, n, vertex=False, kind=None, **kw):
        descriptor=('VertexDescription.' if vertex else 'SurfaceDescription.')+name
        value=1.0 if name=='Alpha' else (0.0 if n==1 else vec(n))
        slot=self.slot('blockslot'+name,0,name,n,stage=1 if vertex else 2,kind=kind,value=value,**kw)
        node=self.node('block'+name,'BlockNode',descriptor,[slot],1100,0,m_SerializedDescriptor=descriptor)
        (self.vertices if vertex else self.fragments).append(ref(node['m_ObjectId'])); return node
    def function(self, name, args, outputs, body, y=0):
        slots=[]
        for i,(arg,n,source) in enumerate(args): slots.append(self.slot(name+'input'+arg,i,arg,n))
        for i,(arg,n) in enumerate(outputs,len(args)):slots.append(self.slot(name+'output'+arg,i,arg,n,True))
        f=self.node('function'+name,'CustomFunctionNode',name+' (Custom Function)',slots,280,y,
            m_SourceType=1,m_FunctionName=name,m_FunctionSource='',m_FunctionSourceUsePragmas=True,m_FunctionBody=body)
        f['m_SGVersion']=1
        for i,(_,_,source) in enumerate(args):self.edge(source,0,f,i)
        return f
    def save(self):
        cat=self.obj('category','CategoryData',m_Name='M3 Effect',m_ChildObjectList=self.properties)
        target=self.obj('target','Unused')
        target.update(m_SGVersion=1,m_Type='UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget',m_Datas=[],
            m_ActiveSubTarget=ref(uid(self.name+'unlit')),m_AllowMaterialOverride=False,m_SurfaceType=1,
            m_ZTestMode=4,m_ZWriteControl=0,m_AlphaMode=0,m_RenderFace=self.face,m_AlphaClip=False,
            m_CastShadows=False,m_ReceiveShadows=False,m_DisableTint=False,m_AdditionalMotionVectorMode=0,
            m_AlembicMotionVectors=False,m_SupportsLODCrossFade=False,m_CustomEditorGUI='',m_SupportVFX=False)
        unlit=self.obj('unlit','Unused')
        unlit.update(m_SGVersion=2,m_Type='UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget')
        data=dict(m_SGVersion=3,m_Type='UnityEditor.ShaderGraph.GraphData',m_ObjectId=uid(self.name+'graph'),
            m_Properties=self.properties,m_Keywords=[],m_Dropdowns=[],m_CategoryData=[ref(cat['m_ObjectId'])],
            m_Nodes=self.nodes,m_GroupDatas=[],m_StickyNoteDatas=[],m_Edges=self.edges,
            m_VertexContext={'m_Position':{'x':1100,'y':-100},'m_Blocks':self.vertices},
            m_FragmentContext={'m_Position':{'x':1100,'y':300},'m_Blocks':self.fragments},
            m_PreviewData={'serializedMesh':{'m_SerializedMesh':'{"mesh":{"instanceID":0}}','m_Guid':''},'preventRotation':False},
            m_Path='Orbis/M3',m_GraphPrecision=1,m_PreviewMode=2,m_OutputNode=ref(''),m_SubDatas=[],m_ActiveTargets=[ref(target['m_ObjectId'])])
        OUT.mkdir(parents=True, exist_ok=True)
        (OUT/(self.name+'.shadergraph')).write_text('\n\n'.join(json.dumps(o,indent=4) for o in [data]+self.objects)+'\n',encoding='utf-8')

def build(name):
    g=Graph(name,1 if name=='Outline' else 0)
    primary=g.property('Primary Color',rgb('FF5A1F'),True,0)
    secondary=g.property('Secondary Color',rgb('FFD166'),True,210)
    progress=g.property('Progress',0.0,y=420)
    args=[('Primary',4,primary),('Secondary',4,secondary),('Progress',1,progress)]
    posblock=g.block('Position',3,True,'PositionMaterialSlot',m_Space=0)
    g.block('Normal',3,True,'NormalMaterialSlot',m_Space=0)
    g.block('Tangent',3,True,'TangentMaterialSlot',m_Space=0)
    colorblock=g.block('BaseColor',3,kind='ColorRGBMaterialSlot',m_ColorMode=0,m_DefaultColor={'r':0.5,'g':0.5,'b':0.5,'a':1})
    alphablock=g.block('Alpha',1)
    if name=='Dissolve':
        args.append(('Position',3,g.geometry('PositionNode',space=0,y=500)))
        body='''// Default 12/19-unit object-space noise frequencies and 0.065 burn edge are visual tuning.
float noise = 0.5 + 0.24 * sin(dot(Position, float3(12.7, 8.3, 17.1))) + 0.24 * sin(dot(Position, float3(-19.1, 23.7, 9.2)));
float p = saturate(Progress);
float coverage = smoothstep(p - 0.025, p + 0.025, noise);
float edge = 1.0 - smoothstep(0.0, 0.065, noise - p);
Color = lerp(Primary.rgb, Secondary.rgb * 1.5, edge * step(0.001, p));
Alpha = Primary.a * coverage * (1.0 - step(0.9999, p));'''
    elif name=='Outline':
        thickness=g.property('Thickness',0.03,y=640)
        p=g.geometry('PositionNode',space=0,y=680); n=g.geometry('NormalVectorNode',space=0,y=880)
        vf=g.function('M3OutlineShell',[('Position',3,p),('Normal',3,n),('Thickness',1,thickness)],[('Out',3)],
            '// Default shell width is 0.03 object-space units. Back-face rendering leaves an outer silhouette.\nOut = Position + normalize(Normal) * max(0.0, Thickness);',-300)
        g.edge(vf,3,posblock,0)
        body='''float p = saturate(Progress);
Color = lerp(Secondary.rgb, Primary.rgb, p);
Alpha = Primary.a * (1.0 - p);'''
    elif name=='WeaponTrail':
        args.extend([('UV',4,g.geometry('UVNode',4,y=650)),('VertexColor',4,g.geometry('VertexColorNode',4,y=850))])
        body='''// TrailRenderer UV0.y spans its width; native vertex alpha carries the lifetime gradient.
float across = saturate(UV.y);
float edge = smoothstep(0.0, 0.16, across) * smoothstep(0.0, 0.16, 1.0 - across);
Color = lerp(Primary.rgb, Secondary.rgb, 1.0 - abs(across * 2.0 - 1.0)) * VertexColor.rgb;
Alpha = Primary.a * VertexColor.a * edge * (1.0 - saturate(Progress));'''
    elif name=='SwirlRing':
        args.append(('UV',4,g.geometry('UVNode',4,y=650)))
        body='''// Ring radius/feather are visual defaults; the runtime expands the mesh independently.
float2 xy = UV.xy * 2.0 - 1.0;
float radius = length(xy);
float ring = smoothstep(0.57, 0.67, radius) * (1.0 - smoothstep(0.85, 0.98, radius));
float swirl = 0.5 + 0.5 * sin(atan2(xy.y, xy.x) * 5.0 - Progress * 9.0 + radius * 10.0);
Color = lerp(Primary.rgb, Secondary.rgb, swirl);
Alpha = Primary.a * ring * (0.45 + 0.55 * swirl) * (1.0 - saturate(Progress));'''
    else:
        args.extend([('Position',3,g.geometry('PositionNode',space=2,y=650)),('Normal',3,g.geometry('NormalVectorNode',space=2,y=850)),('View',3,g.geometry('ViewDirectionNode',space=2,y=1050))])
        body='''// Smooth normals define the Fresnel rim; derivatives reveal the mesh triangle facets.
float3 n = normalize(Normal);
float3 view = normalize(View);
float fresnel = pow(1.0 - saturate(abs(dot(n, view))), 2.2);
float3 facetCross = cross(ddx(Position), ddy(Position));
float3 facetNormal = facetCross * rsqrt(max(dot(facetCross, facetCross), 0.000001));
float facet = saturate(abs(dot(facetNormal, normalize(float3(0.4, 0.8, 0.3)))));
Color = lerp(Secondary.rgb, Primary.rgb, 0.25 + 0.65 * facet) + Secondary.rgb * fresnel * 0.5;
Alpha = Primary.a * (0.12 + 0.2 * facet + 0.55 * fresnel) * (1.0 - saturate(Progress));'''
    fn=g.function('M3'+name,args,[('Color',3),('Alpha',1)],body)
    g.edge(fn,len(args),colorblock,0);g.edge(fn,len(args)+1,alphablock,0);g.save()

if __name__=='__main__':
    for effect in ['Dissolve','Outline','WeaponTrail','SwirlRing','CrystalShield']: build(effect)
    print('Generated five editable URP Shader Graph assets in',OUT)
