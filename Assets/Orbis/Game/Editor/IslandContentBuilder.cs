using System;
using System.Linq;
using Orbis.Art;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M4;
using Orbis.M3;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Place M2/M4 content at terrain sites without scaling encounter distances or gameplay.</summary>
    internal sealed class IslandContentBuilder
    {
        private readonly ArtAssetCatalog catalog;
        private readonly M4AuthoredRegion binding;
        private readonly M4RegionProfile profile;
        private readonly Vector3 center;
        private readonly Transform root;
        private readonly Material stone, accent;
        public IslandContentBuilder(ArtAssetCatalog assets, M4AuthoredRegion authored, M4RegionProfile data, Vector3 site)
        {
            catalog = assets; binding = authored; profile = data; center = site; root = authored.transform;
            stone = Material("IslandRuinStone", new Color(.58f, .59f, .51f));
            // Region palette is the M3 specification; no change to combat statistics.
            Color color = M3Palette.Primary(data.Element);
            accent = Material(data.Id + "IslandAccent", color);
        }
        private Vector3 At(float x, float z, float y = 0) => center + new Vector3(x, y, z);
        private Transform Node(string name, Transform parent, Vector3 point) => IslandSceneBuilder.Node(name, parent, point);
        public void Build()
        {
            BuildPuzzle(); BuildChallenge(); BuildBoss(); BuildTrailhead();
        }

        private void BuildPuzzle()
        {
            var puzzle = Node("01 Ordered Element Shrine", root, At(22,6));
            Pad("Shrine stone mosaic", puzzle, At(22,7,.025f), 6.5f, stone);
            binding.Statues = new ElementalActor[3]; binding.StatueRenderers = new Renderer[3];
            for (int i=0;i<3;i++)
            {
                binding.Statues[i] = Actor(profile.Id+" Statue "+(i+1), puzzle, At(19+i*3,6), false);
                var visual = Model("statue", "Elemental statue "+(i+1), binding.Statues[i].transform, At(19+i*3,6), 1.9f);
                binding.StatueRenderers[i] = Main(visual);
                for(int mark=0;mark<=i;mark++)
                {
                    var gem=Model("crystal","Order gem "+(mark+1),binding.Statues[i].transform,At(19+i*3+(mark-i*.5f)*.3f,5),.28f);
                    Paint(gem,accent);
                }
                Outline(visual);
            }
            binding.Puzzle = puzzle.gameObject.AddComponent<FieldElementPuzzle>();
            binding.Chest = Node("Reward chest / F", puzzle, At(22,10));
            var chest = Model("chest", "Shrine reward chest", binding.Chest, binding.Chest.position, .95f);
            binding.ChestRenderer = Main(chest);
            for(int i=-1;i<=1;i+=2)
            {
                Model("column", "Ancient shrine pillar", puzzle, At(22+i*5.5f,10), 3.3f);
                var banner=Model("banner", "Element banner", puzzle, At(22+i*5.5f,9.7f,1.25f),1.2f);
                Paint(banner,accent);
            }
        }
        private void BuildChallenge()
        {
            var room=Node("02 Reaction Trial Ruins",root,At(-23,22));
            Pad("Ancient trial floor",room,At(-23,25,.04f),8,stone);
            binding.ChallengeEntry=Node("Trial entrance / F",room,At(-23,17));
            Pad("Trial entry seal",room,At(-23,17,.065f),1,accent);
            binding.Targets=new ElementalActor[3]; binding.ChallengeRenderers=new Renderer[3];
            for(int i=0;i<3;i++)
            {
                Vector3 point=At(-26+i*3,i==1?27:24);
                binding.Targets[i]=Actor(profile.Id+" Challenge "+(i+1),room,point,false);
                var visual=Model("enemy","Trial guardian "+(i+1),binding.Targets[i].transform,point,1.8f);
                binding.ChallengeRenderers[i]=Main(visual); Outline(visual);
            }
            binding.Challenge=room.gameObject.AddComponent<ChallengeRoom>();
            // Open ruin walls retain access from the trail; walls are not invisible arena boundaries.
            for(int i=0;i<5;i++)
            {
                float angle=(i*40+10)*Mathf.Deg2Rad;
                Vector3 point=At(-23+Mathf.Cos(angle)*8,25+Mathf.Sin(angle)*8);
                var pillar=Model("column","Broken colonnade "+i,room,point,3.8f+i%2*1.2f);
                Solid(pillar,Vector3.up*1.8f,new Vector3(.7f,3.6f,.7f));
            }
            Model("arch","Trial arch",room,At(-23,16),4.5f);
        }
        private void BuildBoss()
        {
            var arena=Node("03 Field Guardian",root,At(25,38));
            Pad("Weathered guardian circle",arena,At(25,38,.035f),9,stone);
            binding.BossObject=Actor(profile.Id+" Field Guardian",arena,At(25,38),true).gameObject;
            string path="Assets/Orbis/Game/Island/Models/InfernoHornbeast.fbx";
            var referenceBoss=profile.Id==M4RegionId.Agnia?AssetDatabase.LoadAssetAtPath<GameObject>(path):null;
            GameObject visual;
            if(referenceBoss!=null)
            {
                visual=Place(referenceBoss,"홍염각수 / Blender reference study",binding.BossObject.transform,At(25,38),3);
                ApplyReferenceMaterials(visual);
                // Visual study fits the existing prototype encounter. 09's production-scale boss gameplay is separate work.
            }
            else
            {
                visual=Model("enemy",profile.DisplayName+" guardian",binding.BossObject.transform,At(25,38),2.6f);
                Paint(visual,accent);
                for(int i=0;i<3;i++)
                {
                    var crest=Model("crystal","Element crest "+i,binding.BossObject.transform,At(25+(i-1)*.6f,38,2),.95f);
                    Paint(crest,accent);
                }
            }
            Outline(visual);
            var core=Model("crystal","Weakness Core",binding.BossObject.transform,At(25,37.05f,1.1f),.5f);
            Main(core).name="Weakness Core";
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI*.25f;
                Model("column","Guardian perimeter relic "+i,arena,At(25+Mathf.Sin(angle)*10,38+Mathf.Cos(angle)*10),1.4f+i%3*.3f);
            }
        }
        private void BuildTrailhead()
        {
            var camp=Node("04 Trailhead and Commissions",root,At(0,-24));
            binding.Spawn=Node("Player spawn",camp,At(0,-30,.12f));
            binding.Npc=Node("Commission NPC / F",camp,At(-5,-20));
            var character=catalog.Characters[((int)profile.Id+1)%catalog.Characters.Length];
            var npc=Place(character.Prefab,"Commission keeper",binding.Npc,binding.Npc.position,1.8f);
            foreach(var animator in npc.GetComponentsInChildren<Animator>(true))
            { animator.avatar=character.Avatar; animator.runtimeAnimatorController=character.Controller; animator.applyRootMotion=false; }
            Outline(npc);
            Model("board","Commission board",camp,At(-6.5f,-20),1.35f);
            binding.Portal=Node("Island waypoint / F",camp,At(-10,-25));
            Model("obelisk","Waypoint monolith",binding.Portal,binding.Portal.position,2.8f);
            var survey=Model("crystal","Survey landmark",binding.Portal,At(-10,-22),.7f); Paint(survey,accent); binding.Survey=survey.transform;
            Model("lamp","Trail fire",camp,At(-15,-19),.5f);
            Model("crate","Provision chest",camp,At(-17,-21),.85f);
            Model("barrel","Water barrel",camp,At(-17,-19),1);
            Model("fence","Camp fence",camp,At(-19,-20),1.05f,90);
            Model("board","Island trail sign",camp,At(3,-31),1.4f,25);
        }
        private ElementalActor Actor(string name,Transform parent,Vector3 point,bool boss)
        {
            var actorRoot=Node(name,parent,point); actorRoot.gameObject.layer=9;
            var collider=actorRoot.gameObject.AddComponent<CapsuleCollider>();
            collider.center=Vector3.up*(boss?1.25f:1); collider.height=boss?2.5f:2; collider.radius=boss?.65f:.42f;
            actorRoot.gameObject.AddComponent<TrainingDummy>();
            var actor=actorRoot.gameObject.AddComponent<ElementalActor>();
            actor.Configure(boss?"boss."+profile.Id:name,boss?profile.Element:ElementType.None,ActorTeam.Enemy);
            return actor;
        }
        private GameObject Model(string key,string name,Transform parent,Vector3 point,float height,float yaw=0)
            =>Place(catalog.Model(key),name,parent,point,height,yaw);
        private static GameObject Place(GameObject prefab,string name,Transform parent,Vector3 point,float height,float yaw=0)
        {
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
            instance.name=name; instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); instance.transform.localScale=Vector3.one;
            var renderers=instance.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0) throw new InvalidOperationException("Missing mesh: "+name);
            Bounds bounds=renderers[0].bounds;
            foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
            float scale=height/Mathf.Max(.001f,bounds.size.y);
            instance.transform.localScale=Vector3.one*scale;
            instance.transform.rotation=Quaternion.Euler(0,yaw,0);
            instance.transform.position=point-instance.transform.rotation*(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale);
            foreach(var collider in instance.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
            return instance;
        }
        private void ApplyReferenceMaterials(GameObject visual)
        {
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>
                {
                    Color color=source!=null&&source.HasProperty("_Color")?source.color:Color.gray;
                    string sourceName=source!=null?source.name:"Basalt";
                    var material=Material("Hornbeast_"+sourceName.Replace(" ","_"),color);
                    if(sourceName.IndexOf("emiss",StringComparison.OrdinalIgnoreCase)>=0||sourceName.IndexOf("magma",StringComparison.OrdinalIgnoreCase)>=0)
                        if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",color*2);
                    EditorUtility.SetDirty(material);return material;
                }).ToArray();
            }
        }
        private void Pad(string name,Transform parent,Vector3 point,float radius,Material material)
        {
            string path=IslandSceneBuilder.Generated+"/Meshes/RoundRuinFloor.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null)
            {
                const int n=48; var vertices=new Vector3[n+1];var uv=new Vector2[n+1];var triangles=new int[n*3];
                uv[0]=Vector2.one*.5f;
                for(int i=0;i<n;i++)
                {float a=i*Mathf.PI*2/n;vertices[i+1]=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));uv[i+1]=new Vector2(vertices[i+1].x,vertices[i+1].z)*.5f+Vector2.one*.5f;
                 triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=(i+1)%n+1;}
                mesh=new Mesh{name="Circular ancient stone floor",vertices=vertices,uv=uv,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
            }
            var floor=Node(name,parent,point);floor.localScale=new Vector3(radius,1,radius);
            floor.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            floor.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        private Material Material(string name,Color color)
        {
            string path=IslandSceneBuilder.Generated+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null)
            {material=new Material(catalog.DefaultToonMaterial){name=name};if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",null);
             material.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(material,path);}
            return material;
        }
        private static void Paint(GameObject item,Material material)
        {foreach(var renderer in item.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();}
        private static Renderer Main(GameObject item)=>item.GetComponentsInChildren<Renderer>(true).First(x=>!x.name.StartsWith("Art Outline"));
        private static void Solid(GameObject visual,Vector3 center,Vector3 size)
        {
            // Collision dimensions are world-space and independent from fitted prefab scale.
            var solid=IslandSceneBuilder.Node("Pillar collision",visual.transform.parent,visual.transform.position);
            solid.gameObject.layer=8;var collider=solid.gameObject.AddComponent<BoxCollider>();collider.center=center;collider.size=size;
        }
        private void Outline(GameObject visual)
        {
            foreach(var source in visual.GetComponentsInChildren<Renderer>(true))
            {
                if(source.name.StartsWith("Art Outline"))continue;
                Mesh mesh=source is SkinnedMeshRenderer skin?skin.sharedMesh:source.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh==null)continue;
                var node=new GameObject("Art Outline / "+source.name);node.transform.SetParent(source.transform,false);
                Renderer output;
                if(source is SkinnedMeshRenderer original)
                {var copy=node.AddComponent<SkinnedMeshRenderer>();copy.sharedMesh=mesh;copy.bones=original.bones;copy.rootBone=original.rootBone;copy.localBounds=original.localBounds;output=copy;}
                else{node.AddComponent<MeshFilter>().sharedMesh=mesh;output=node.AddComponent<MeshRenderer>();}
                output.sharedMaterials=Enumerable.Repeat(catalog.OutlineMaterial,mesh.subMeshCount).ToArray();
                output.shadowCastingMode=ShadowCastingMode.Off;output.receiveShadows=false;
                node.AddComponent<ArtOutlineSync>().Configure(source,output);
            }
        }
    }
}
