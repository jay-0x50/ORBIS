using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Two scenery-only lake cascades fitted to the existing Terrain. Never edits heights or water gameplay.</summary>
    public static class WorldWaterfallBuilder
    {
        public const string Root = "Assets/Orbis/Game/World/Cascades";
        public const string GeneratedName = "Lake-fed Cascades";
        const int Rows = 65, Columns = 17;
        static readonly Vector2 LakeCenter = new Vector2(230, 35);
        static readonly Vector2 LakeTerrainRadius = new Vector2(125, 90);
        static float LakeLevel => IslandTerrainBuilder.LakeLevel;

        [Serializable] public sealed class CascadeRecord
        {
            public string name;
            public Vector3 source, lip, outlet;
            public float lakeLevel, drop, length;
            public int waterTriangles, rockTriangles;
            public string note = "Terrain-sampled scenery. Source reservoir, rock-supported runnel, stepped drop and lake foam share a continuous surface. Existing lake volume unchanged.";
        }
        [Serializable] sealed class CascadeAudit { public CascadeRecord[] cascades; public Vector4[] vegetationFootprints; }
        sealed class Plan
        {
            public string Name; public Vector3[] Center, Right; public float[] Width, LeftExtent, RightExtent;
            public Vector3 Flow, SourcePool, FootPool; public float SourceLevel, WidthBase;
            public int Lip;
        }
        sealed class Shape
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<Color> Color = new List<Color>();
            public readonly List<int> T = new List<int>();
            public int Add(Vector3 p, Vector2 uv, Color color) { int i = V.Count; V.Add(p); UV.Add(uv); Color.Add(color); return i; }
            public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
            public void Quad(int a, int b, int c, int d) { Tri(a,c,b); Tri(b,c,d); }
            public Mesh ToMesh(string name, Vector3 origin)
            {
                var mesh = new Mesh { name = name, indexFormat = V.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(V.Select(v => v - origin).ToList()); mesh.SetUVs(0,UV); mesh.SetColors(Color); mesh.SetTriangles(T,0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
        sealed class Ground
        {
            readonly Terrain[] tiles;
            public Ground(Terrain[] terrains) { tiles = terrains; }
            public float At(Vector3 p)
            {
                foreach (var t in tiles)
                {
                    Vector3 o = t.transform.position, s = t.terrainData.size;
                    if (p.x >= o.x && p.z >= o.z && p.x <= o.x+s.x && p.z <= o.z+s.z)
                        return t.SampleHeight(p) + o.y;
                }
                throw new InvalidOperationException("Cascade sample left the existing island: " + p);
            }
        }

        public static IReadOnlyList<Vector4> Build(Transform envParent, Transform collisionParent, Terrain[] terrains)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Author lake scenery outside Play Mode.");
            if (envParent == null || collisionParent == null || terrains == null || terrains.Length == 0)
                throw new ArgumentException("Provide environment/collision owners and the existing Terrain tiles.");
            if (terrains.Any(t => t == null || t.terrainData == null)) throw new ArgumentException("TerrainData is required.");
            var ground = new Ground(terrains);
            // Search two separate lake-shore sectors; the exact shelf comes from saved Terrain heights, not a guessed Y.
            // Art revision: the eastern fall is the broad lake landmark; the northern feeder remains subordinate.
            var plans = new[] { MakePlan(ground,"Eastern Silverfall",18,53,8.2f), MakePlan(ground,"Northern Runnel",76,116,3.5f) };
            var shader = Shader.Find("Orbis/World/Cascade");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Import the WorldCascade shader before building waterfalls.");
            var rockMaterial = WorldGroundBuilder.RockMaterial;
            if (rockMaterial == null) throw new InvalidOperationException("Complete Step 2 moss-rock material first.");
            var sourceRocks = Enumerable.Range(1,3).Select(i => AssetDatabase.LoadAssetAtPath<Mesh>(
                "Assets/Orbis/Game/World/Nature/Meshes/Rock_Medium_"+i+"_LOD0.asset")).ToArray();
            if (sourceRocks.Any(x => x == null)) throw new InvalidOperationException("The three licensed Step 2 rock meshes are required.");
            Directory.CreateDirectory(Root+"/Meshes"); Directory.CreateDirectory(Root+"/Materials"); AssetDatabase.Refresh();
            var waterMaterial = WaterMaterial(shader);
            // Idempotence is limited to this builder's named children; unrelated scenery/collision is preserved.
            RemoveChild(envParent,GeneratedName); RemoveChild(collisionParent,GeneratedName+" Collision");
            var visuals = Node(GeneratedName,envParent,Vector3.zero);
            var collisions = Node(GeneratedName+" Collision",collisionParent,Vector3.zero);
            var footprints = new List<Vector4>(); var records = new List<CascadeRecord>();
            for (int number=0; number<plans.Length; number++)
            {
                Plan plan = plans[number]; var water = new Shape(); var stone = new Shape();
                Ribbon(plan,water); Channel(plan,ground,stone);
                Pool(plan.SourcePool,plan.Flow,plan.WidthBase*.80f,5.2f,plan.SourceLevel,.16f,number+7,water,stone,ground);
                Pool(plan.FootPool,plan.Flow,plan.WidthBase*1.20f,5.8f,LakeLevel+.035f,1f,number+19,water,stone,ground);
                Banks(plan,ground,sourceRocks,stone,number);
                Vector3 origin = plan.Center[0];
                string prefix = "Cascade_"+(number+1);
                Mesh waterMesh = SaveMesh(water.ToMesh(prefix+"_Water",origin),Root+"/Meshes/"+prefix+"_Water.asset");
                Mesh rockMesh = SaveMesh(stone.ToMesh(prefix+"_RockChannel",origin),Root+"/Meshes/"+prefix+"_RockChannel.asset");
                var root = Node(plan.Name,visuals,origin);
                MeshRenderer wet = MeshNode("Continuous pool, runnel and waterfall",root,waterMesh,waterMaterial);
                wet.shadowCastingMode = ShadowCastingMode.Off; wet.receiveShadows = true;
                MeshNode("Fitted rock bed and bank boulders",root,rockMesh,rockMaterial);
                var colliderObject = Node(plan.Name,collisions,origin); colliderObject.gameObject.layer = 8;
                colliderObject.gameObject.AddComponent<MeshCollider>().sharedMesh = rockMesh;
                // Eight overlapping circles cover the connected scenery, including banks, for tree/grass removal.
                for(int i=0;i<Rows;i+=9)
                {
                    Vector3 p=plan.Center[i];footprints.Add(new Vector4(p.x,p.y,p.z,plan.Width[i]*.5f+5.5f));
                }
                footprints.Add(new Vector4(plan.SourcePool.x,plan.SourcePool.y,plan.SourcePool.z,8));
                footprints.Add(new Vector4(plan.FootPool.x,plan.FootPool.y,plan.FootPool.z,9));
                float length=0;for(int i=1;i<Rows;i++)length+=Vector3.Distance(plan.Center[i-1],plan.Center[i]);
                records.Add(new CascadeRecord{name=plan.Name,source=plan.SourcePool,lip=plan.Center[plan.Lip],outlet=plan.FootPool,
                    lakeLevel=LakeLevel,drop=plan.SourceLevel-LakeLevel,length=length,waterTriangles=waterMesh.triangles.Length/3,rockTriangles=rockMesh.triangles.Length/3});
            }
            Directory.CreateDirectory("TestResults/WorldDev");
            File.WriteAllText("TestResults/WorldDev/World03_Cascades.json",JsonUtility.ToJson(new CascadeAudit{cascades=records.ToArray(),vegetationFootprints=footprints.ToArray()},true));
            AssetDatabase.SaveAssets();
            Debug.Log("ORBIS_WORLD_CASCADES: "+string.Join("; ",records.Select(x=>x.name+" "+x.source.ToString("F2")+" -> "+x.outlet.ToString("F2")+", drop "+x.drop.ToString("F1")+"m")));
            return footprints;
        }

        static Plan MakePlan(Ground ground,string name,float angleMin,float angleMax,float width)
        {
            float best=float.NegativeInfinity,chosenAngle=0,chosenFoot=0,chosenLip=0;
            for(float angle=angleMin;angle<=angleMax;angle+=2.5f)
            {
                float shore=0;
                for(float radius=.78f;radius<=1.34f;radius+=.012f)
                    if(ground.At(Radial(angle,radius))>LakeLevel+.05f){shore=radius-.020f;break;}
                if(shore<=0)continue;
                for(float lip=shore+.22f;lip<=shore+.78f;lip+=.025f)
                {
                    Vector3 a=Radial(angle,lip),b=Radial(angle,shore);float height=ground.At(a);
                    if(height<29||height>52)continue;
                    float distance=Vector3.Distance(a,b);float grade=(height-LakeLevel)/distance;
                    float score=grade*25-Mathf.Abs(height-35)*.075f-Mathf.Abs(angle-(angleMin+angleMax)*.5f)*.009f;
                    if(score>best){best=score;chosenAngle=angle;chosenFoot=shore;chosenLip=lip;}
                }
            }
            if(float.IsNegativeInfinity(best))throw new InvalidOperationException("No suitable 29–52m natural shelf feeding the lake was found for "+name);
            float sourceRadius=chosenLip+.09f;
            Vector3 source=Radial(chosenAngle,sourceRadius),foot=Radial(chosenAngle,chosenFoot-.012f);
            Vector3 flow=(foot-source).normalized,right=Vector3.Cross(Vector3.up,flow).normalized;
            var plan=new Plan{Name=name,Center=new Vector3[Rows],Right=new Vector3[Rows],Width=new float[Rows],
                LeftExtent=new float[Rows],RightExtent=new float[Rows],Flow=flow,WidthBase=width};
            float lipT=(sourceRadius-chosenLip)/(sourceRadius-(chosenFoot-.012f));
            plan.Lip=Mathf.Clamp(Mathf.RoundToInt(lipT*(Rows-1)),1,Rows-2);
            for(int i=0;i<Rows;i++)
            {
                float t=i/(Rows-1f);
                Vector3 p=Vector3.Lerp(source,foot,t)+right*((Mathf.Sin(t*5.4f+.2f)*1.30f+Mathf.Sin(t*12.8f)*.38f)*Mathf.Sin(t*Mathf.PI));
                // Unequal margins fan below the lip and vary independently, rather than two parallel pipe edges.
                float fan=.84f+.22f*Smooth(lipT,lipT+.28f,t)+.08f*Smooth(.72f,1,t);
                float half=width*.5f*fan;
                float left=half*(1+.13f*Mathf.Sin(t*9.2f+.3f)+.055f*Mathf.Sin(t*26.3f));
                float rightExtent=half*(1+.11f*Mathf.Cos(t*7.7f+1.6f)+.055f*Mathf.Sin(t*21.1f+2));
                float h=ground.At(p);
                // Sample both water margins, so a cross-slope cannot bury half the ribbon in Terrain.
                h=Mathf.Max(h,Mathf.Max(ground.At(p+right*rightExtent),ground.At(p-right*left)));
                // A modest rock weir adds an actual lip and curved drop; the channel mesh supports it down to Terrain.
                float weir=3.2f*Smooth(lipT-.10f,lipT,t)*(1-Smooth(lipT+.005f,lipT+.085f,t));
                p.y=Mathf.Max(LakeLevel+.035f,h+.14f+weir);
                plan.Center[i]=p;plan.Right[i]=right;plan.Width[i]=left+rightExtent;
                plan.LeftExtent[i]=left;plan.RightExtent[i]=rightExtent;
            }
            // A monotone water surface may fill a shallow terrain dip; its solid rock bed is fitted at every cross-section.
            plan.Center[Rows-1].y=LakeLevel+.035f;
            for(int i=Rows-2;i>=0;i--)plan.Center[i].y=Mathf.Max(plan.Center[i].y,plan.Center[i+1].y+.015f);
            plan.SourcePool=source-flow*2.8f;
            float level=plan.Center[0].y;
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI*2/16;
                level=Mathf.Max(level,ground.At(plan.SourcePool+right*(Mathf.Cos(a)*width*.80f)+flow*(Mathf.Sin(a)*5.2f))+.20f);
            }
            plan.SourceLevel=level;plan.SourcePool.y=level;
            // Taper smoothly out of the filled head pool into the beginning of the supported runnel.
            for(int i=0;i<plan.Lip;i++)plan.Center[i].y=Mathf.Max(plan.Center[i].y,Mathf.Lerp(level,plan.Center[plan.Lip].y,i/(float)plan.Lip));
            plan.FootPool=foot+flow*2.3f;plan.FootPool.y=LakeLevel+.035f;
            if(ground.At(plan.FootPool)>LakeLevel-.03f)throw new InvalidOperationException(name+" outlet must meet actual submerged lake ground.");
            return plan;
        }

        static void Ribbon(Plan p,Shape water)
        {
            float distance=0;
            for(int row=0;row<Rows;row++)
            {
                if(row>0)distance+=Vector3.Distance(p.Center[row],p.Center[row-1]);
                int before=Mathf.Max(0,row-1),after=Mathf.Min(Rows-1,row+1);
                Vector3 tangent=p.Center[after]-p.Center[before];float slope=Mathf.Abs(tangent.y)/Mathf.Max(.1f,new Vector2(tangent.x,tangent.z).magnitude);
                float rapid=Mathf.Clamp01(.18f+slope*.7f);
                for(int column=0;column<Columns;column++)
                {
                    float u=column/(Columns-1f),edge=Mathf.Abs(u*2-1);
                    float across=u<.5f?(u*2-1)*p.LeftExtent[row]:(u*2-1)*p.RightExtent[row];
                    Vector3 pos=p.Center[row]+p.Right[row]*across;
                    // Low broad surface folds catch light differently across the sheet without extra renderers.
                    pos.y+=(.055f*Mathf.Sin(column*.76f+row*.27f)+.028f*Mathf.Sin(column*1.55f-row*.39f))*(1-edge);
                    float opacity=Smooth(0,.18f,1-edge)*Smooth(0,.06f,row/(Rows-1f))*(1-Smooth(.94f,1,row/(Rows-1f)));
                    water.Add(pos,new Vector2(u,distance),new Color(rapid,0,0,opacity));
                    if(row>0&&column>0)
                    {
                        int d=row*Columns+column,b=d-Columns,c=d-1,a=b-1;water.Quad(a,b,c,d);
                    }
                }
            }
        }
        static void Channel(Plan p,Ground ground,Shape stone)
        {
            const int columns=7;int start=stone.V.Count;
            float[] fractions={-1.60f,-1.17f,-.95f,0,.95f,1.17f,1.60f};
            for(int row=0;row<Rows;row++)for(int column=0;column<columns;column++)
            {
                float half=column<3?p.LeftExtent[row]:p.RightExtent[row];Vector3 point=p.Center[row]+p.Right[row]*(fractions[column]*half);
                if(column==0||column==6)point.y=ground.At(point)-.28f;
                // The wet rock margin sits below the water sheet. Exposed accents come from broken bank clusters,
                // avoiding a raised continuous curb that made the original narrow fall read as a stone pipe.
                else if(column==1||column==5)point.y=Mathf.Max(ground.At(point)-.14f,p.Center[row].y-.28f-.11f*Mathf.Sin(row*.43f+column));
                else point.y=p.Center[row].y-(column==3?.48f:.31f);
                stone.Add(point,new Vector2(column,row),Color.white);
                if(row>0&&column>0){int d=start+row*columns+column,b=d-columns,c=d-1,a=b-1;stone.Quad(a,b,c,d);}
            }
        }
        static void Pool(Vector3 center,Vector3 flow,float radiusX,float radiusZ,float level,float foam,int seed,Shape water,Shape rock,Ground ground)
        {
            Vector3 right=Vector3.Cross(Vector3.up,flow).normalized;
            const int segments=48,rings=6;
            int middle=water.Add(center,new Vector2(.5f,.5f),new Color(foam,0,1,.74f));
            for(int ring=1;ring<=rings;ring++)for(int segment=0;segment<segments;segment++)
            {
                float a=segment*Mathf.PI*2/segments,r=ring/(float)rings;
                float shape=1+.065f*Mathf.Sin(a*3+seed)+.035f*Mathf.Cos(a*7+seed*.37f);
                Vector3 p=center+right*(Mathf.Cos(a)*radiusX*r*shape)+flow*(Mathf.Sin(a)*radiusZ*r*shape);p.y=level;
                water.Add(p,new Vector2(Mathf.Cos(a)*r*.5f+.5f,Mathf.Sin(a)*r*.5f+.5f),new Color(foam,0,1,(1-Smooth(.64f,1,r))*.76f));
                int current=middle+1+(ring-1)*segments+segment,next=middle+1+(ring-1)*segments+(segment+1)%segments;
                if(ring==1)water.Tri(middle,next,current);
                else{int inner=current-segments,innerNext=next-segments;water.Tri(inner,innerNext,current);water.Tri(current,innerNext,next);}
            }
            int floor=rock.Add(center-Vector3.up*.52f,Vector2.zero,Color.white);
            for(int ring=0;ring<3;ring++)for(int segment=0;segment<segments;segment++)
            {
                float a=segment*Mathf.PI*2/segments,shape=1+.065f*Mathf.Sin(a*3+seed)+.035f*Mathf.Cos(a*7+seed*.37f);
                float r=ring==0?.93f:ring==1?1.12f:1.35f;
                Vector3 p=center+right*(Mathf.Cos(a)*radiusX*r*shape)+flow*(Mathf.Sin(a)*radiusZ*r*shape);
                // A downstream foot pool fades into the existing lake rather than constructing a dam across its mouth.
                bool outlet=foam>.8f;
                p.y=ring==0?level-.50f:ring==1?(outlet?Mathf.Min(ground.At(p)-.08f,level-.24f):level+.22f):ground.At(p)-.25f;
                rock.Add(p,Vector2.zero,Color.white);
                int current=floor+1+ring*segments+segment,next=floor+1+ring*segments+(segment+1)%segments;
                if(ring==0)rock.Tri(floor,next,current);
                else{int inner=current-segments,innerNext=next-segments;rock.Tri(inner,innerNext,current);rock.Tri(current,innerNext,next);}
            }
        }
        static void Banks(Plan p,Ground ground,Mesh[] meshes,Shape stone,int seed)
        {
            // Deliberately staggered clusters, with long bare stretches between them; no paired "ladder rungs".
            float[] stations={.075f,.26f,.48f,.68f,.86f};
            int[] sides={-1,1,-1,1,-1},counts={3,2,2,3,2};
            var random=new System.Random(91033+seed*37);
            for(int cluster=0;cluster<stations.Length;cluster++)for(int piece=0;piece<counts[cluster];piece++)
            {
                float station=stations[cluster]+(float)(random.NextDouble()-.5)*.065f;
                int row=Mathf.Clamp(Mathf.RoundToInt(station*(Rows-1)),1,Rows-2),side=sides[cluster];
                float extent=side<0?p.LeftExtent[row]:p.RightExtent[row];
                float size=(piece==0?1.25f:.72f)*(seed==0?1f:.78f);
                float height=(1.9f+(float)random.NextDouble()*2.4f)*size;
                Vector3 center=p.Center[row]+p.Right[row]*side*(extent*(1.08f+(float)random.NextDouble()*.40f));
                center+=p.Flow*((float)(random.NextDouble()-.5)*2.7f);
                center.y=Mathf.Max(ground.At(center)-.65f,p.Center[row].y-height*.82f);
                AppendRock(meshes[(cluster+piece+seed)%meshes.Length],stone,center,
                    Quaternion.Euler((float)(random.NextDouble()-.5)*15,(float)random.NextDouble()*360,(float)(random.NextDouble()-.5)*12),
                    new Vector3((2.8f+(float)random.NextDouble()*2.6f)*size,height,(2.1f+(float)random.NextDouble()*2.2f)*size));
            }
        }
        static void AppendRock(Mesh source,Shape target,Vector3 position,Quaternion rotation,Vector3 dimensions)
        {
            var vertices=source.vertices;var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);
            Vector3 bottom=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),size=bounds.size;
            Vector3 scale=new Vector3(dimensions.x/size.x,dimensions.y/size.y,dimensions.z/size.z);int start=target.V.Count;
            foreach(var v in vertices)target.Add(position+rotation*Vector3.Scale(v-bottom,scale),Vector2.zero,Color.white);
            foreach(int index in source.triangles)target.T.Add(start+index);
        }
        static Material WaterMaterial(Shader shader)
        {
            string path=Root+"/Materials/LakeCascade.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(shader){name="Muted freshwater and broken foam"};AssetDatabase.CreateAsset(material,path);}
            material.shader=shader;material.enableInstancing=true;
            // Art defaults only. Muted turquoise, pale green-white foam; final world grade remains a separate step.
            material.SetColor("_BaseColor",new Color(.20f,.52f,.56f,.82f));material.SetColor("_DeepColor",new Color(.10f,.30f,.36f,.78f));
            material.SetColor("_FoamColor",new Color(.78f,.90f,.86f,1));material.SetFloat("_FlowSpeed",1.7f);material.SetFloat("_FoamAmount",.62f);
            material.renderQueue=3020;EditorUtility.SetDirty(material);return material;
        }
        static Vector3 Radial(float angle,float radius)
        {float a=angle*Mathf.Deg2Rad;return new Vector3(LakeCenter.x+Mathf.Cos(a)*LakeTerrainRadius.x*radius,0,LakeCenter.y+Mathf.Sin(a)*LakeTerrainRadius.y*radius);}
        static float Smooth(float a,float b,float x)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,x));
        static Transform Node(string name,Transform parent,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.position=position;return t;}
        static MeshRenderer MeshNode(string name,Transform parent,Mesh mesh,Material material)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;return r;}
        static Mesh SaveMesh(Mesh mesh,string path)
        {var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
        static void RemoveChild(Transform parent,string name)
        {foreach(Transform child in parent.Cast<Transform>().Where(x=>x.name==name).ToArray())Object.DestroyImmediate(child.gameObject);}
    }
}
