using System.Collections.Generic;
using Orbis.M0;
using Orbis.M1;
using Orbis.M3;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Art
{
    /// <summary>Licensed mesh dressing around the existing M4 collision and objective layout.</summary>
    [DefaultExecutionOrder(900)]
    public sealed class ArtWorldPresentation : MonoBehaviour
    {
        private sealed class ColorLink {public Renderer Source; public Renderer[] Targets; public Color Fallback;}
        private readonly List<ColorLink> links=new List<ColorLink>();
        private readonly HashSet<Renderer> handled=new HashSet<Renderer>();
        private readonly Dictionary<string,Bounds> modelBounds=new Dictionary<string,Bounds>();
        private readonly Dictionary<Renderer,Color> baseColors=new Dictionary<Renderer,Color>();
        private MaterialPropertyBlock block;
        private ArtAssetCatalog catalog;
        private M4SceneBootstrap scene;
        private Transform world;
        public int ModelInstances {get;private set;}
        public int HiddenGrayboxRenderers {get;private set;}
        public Transform VisualRoot=>world;

        public void Configure(M4SceneBootstrap owner,ArtAssetCatalog assets)
        {
            scene=owner; catalog=assets; block=new MaterialPropertyBlock();
            if (owner.AuthoredRegion != null)
            {
                // These meshes/materials are saved in the scene; edits are authoritative during Play as well.
                // The content binder drives the existing renderer references. No runtime replacement/dressing pass.
                world = owner.AuthoredRegion.transform;
                ModelInstances = world.GetComponentsInChildren<MeshFilter>(true).Length;
                // The player remains a runtime object. Keep its existing imported glider without touching scene props.
                foreach (var renderer in owner.Traversal.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.name.Contains("Glider"))
                        Replace(renderer, renderer.name.Contains("Wing") ? "banner" : "column", false, renderer.transform.parent);
                return;
            }
            var original=owner.GetComponentsInChildren<MeshRenderer>(true);
            world=new GameObject("Imported Environment Visuals").transform; world.SetParent(transform,false);
            for(int i=0;i<scene.Content.Statues.Length;i++) ReplaceActor(scene.Content.Statues[i],"statue",1.9f,true);
            foreach(var target in scene.Content.Targets) ReplaceActor(target,"enemy",1.8f,false);
            ReplaceActor(scene.Boss.Actor,"enemy",2.6f,false);
            ReplaceNpc(); ReplaceChest();
            foreach(var renderer in original)
            {
                if(renderer==null||handled.Contains(renderer)) continue;
                string name=renderer.name;
                if(renderer.transform.IsChildOf(scene.Traversal.transform))
                {
                    if(name.Contains("Glider")) Replace(renderer,name.Contains("Wing")?"banner":"column",false,renderer.transform.parent);
                    continue;
                }
                if(renderer.GetComponentInParent<ElementalActor>()!=null) continue;
                if(name.StartsWith("M3 ")||name.StartsWith("Pooled ")||name.StartsWith("Active Member")||
                    name.StartsWith("M4 Boss Telegraph")) continue;
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy||renderer.sharedMaterial==null) continue;
                if(name.Contains("Weakness Core")) continue;
                if(name.Contains("Water Surface")||name.Contains("Sea Surface")) {ReplaceWater(renderer);continue;}
                ReplaceGeometry(renderer);
            }
            AddRegionalProps();
            ArtStyle.ApplyToHierarchy(world.gameObject,catalog,true);
            foreach(var renderer in world.GetComponentsInChildren<Renderer>(true))
                if(!renderer.name.StartsWith("Art Outline")) baseColors[renderer]=renderer.sharedMaterial!=null&&renderer.sharedMaterial.HasProperty("_BaseColor")?renderer.sharedMaterial.GetColor("_BaseColor"):Color.white;
            UpdateColors();
        }

        private void ReplaceActor(ElementalActor actor,string key,float height,bool ordered)
        {
            Renderer source=null;
            foreach(var renderer in actor.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(renderer.name=="M4 Boss Telegraph") continue;
                if(renderer.name=="Weakness Core") {Replace(renderer,"crystal",true,actor.transform);continue;}
                if(source==null||renderer.name=="Statue Body") source=renderer;
                Hide(renderer);
            }
            var visual=SpawnHeight(key,actor.transform.position,height,actor.transform.rotation,actor.transform);
            if(source!=null) Link(source,visual);
            // The same 3D statue may repeat; original order is made explicit with imported stone markers.
            if(ordered)
            {
                int ordinal=System.Array.IndexOf(scene.Content.Statues,actor)+1;
                for(int mark=0;mark<ordinal;mark++)
                    SpawnFit("crystal",actor.transform.position+new Vector3((mark-(ordinal-1)*.5f)*.22f,.18f,-.65f),
                        Quaternion.identity,new Vector3(.13f,.26f,.13f),actor.transform);
            }
        }
        private void ReplaceNpc()
        {
            foreach(var candidate in scene.GetComponentsInChildren<Transform>(true))
            {
                if(!candidate.name.StartsWith("Commission NPC /")) continue;
                foreach(var renderer in candidate.GetComponentsInChildren<MeshRenderer>(true)) Hide(renderer);
                var definition=catalog.Characters[1];
                var model=Instantiate(definition.Prefab,candidate,false); model.name="Imported Commission NPC";
                var animator=model.GetComponentInChildren<Animator>();
                if(animator!=null)
                {
                    animator.runtimeAnimatorController=definition.Controller; animator.avatar=definition.Avatar;
                    animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind(); animator.Play("Idle",0,0); animator.Update(0);
                }
                ArtCharacterRoster.NormalizeVisibleModelHeight(model,1.8f,candidate.position);
                ArtStyle.ApplyToHierarchy(model,catalog,true);
                DisableColliders(model); ModelInstances++;
                break;
            }
        }
        private void ReplaceChest()
        {
            Renderer source=null;
            foreach(var renderer in scene.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(renderer.name=="Field Reward Chest"||renderer.name=="Chest Base") source=renderer;
                if(renderer.name.StartsWith("Chest ")||renderer.name=="Field Reward Chest") Hide(renderer);
            }
            var chest=SpawnFit("chest",scene.Layout.Chest+Vector3.up*.46f,Quaternion.identity,new Vector3(1.5f,.92f,1.1f));
            if(source!=null) Link(source,chest);
        }
        private void ReplaceGeometry(Renderer source)
        {
            string name=source.name;
            Vector3 size=source.bounds.size;
            if(name=="Gentle Shore") {CoverShore(source);return;}
            if(name.Contains("Cloud")) {Replace(source,"rock_bare",false);return;}
            if(name.Contains("Beacon")||name.Contains("Element Cap")||name.Contains("Mineral Seam")||name.Contains("Survey Marker")) {Replace(source,"crystal",true);return;}
            if(name.Contains("Obelisk")) {Replace(source,"obelisk",true);return;}
            if(name=="Coral Main Branch"||name.StartsWith("Coral Fork ")) {Replace(source,"bush",true);return;}
            if(name.Contains("Lamp Post")||name.Contains("Lightning Spire")||name.Contains("Rail")||name.Contains("Trail Post")) {Replace(source,"column",false);return;}
            if(name.Contains("Pennant")||name.Contains("Windmill Sail")) {Replace(source,"banner",false);return;}
            if(name.Contains("Windmill Tower")) {Replace(source,"tower",false);return;}
            if(name.Contains("Windmill Hub")) {Replace(source,"barrel",false);return;}
            if(name.Contains("Footbridge")||name.Contains("Causeway")||name.Contains("Sleeper")) {Replace(source,"bridge",false);return;}
            if(name.Contains("Roof")||name.Contains("Cornice")) {Replace(source,"roof",false);return;}
            if(name.Contains("Pillar")) {Replace(source,"column",false);return;}
            if(name.Contains("Wall")||name.Contains("Boundary")||name.Contains("Gate ")||name.StartsWith("Entrance ")) {CoverWall(source);return;}
            if(name.Contains("Cliff")||name=="Eastern High Mine"||name.Contains("Terrace")||name.Contains("Bedrock")||name.Contains("Island")||
                name.Contains("Rest Landing")||name.Contains("Ground")||name.Contains("Seabed")||name.Contains("Bluff")||name.Contains("Mesa")||name.Contains("Tower"))
            {
                if(name.Contains("Tower")) Replace(source,"tower",false);
                else CoverGround(source,GroundKey(),size.y>.5f);
                return;
            }
            if(name.Contains("Ramp")||name.Contains("Road")||name.Contains("Switchback")||name.Contains("Cove Exit")||name.Contains("Ascent")||name.Contains("Shore")||name.Contains("Approach")||
                name.Contains("Floor")||name.Contains("Plaza")||name.Contains("Courtyard")||name.Contains("Track")||name.Contains("Street")||
                name.Contains("Avenue")||name.Contains("Arena")||size.y<.4f)
            {
                CoverGround(source,name.Contains("Floor")||name.Contains("Arena")?"floor_stone":GroundKey(),false);return;
            }
            Replace(source,"rock_bare",false);
        }
        private string GroundKey()=>scene.Region==M4RegionId.Zephyr?"ground_grass":scene.Region==M4RegionId.Teluna?"ground_sand":"floor_stone";

        private void CoverGround(Renderer source,string key,bool volume)
        {
            // Broad bedrock uses a thin skin: stretching each grass tile through several metres
            // would turn a continuous meadow into a grid of isolated dirt islands.
            if(source.name.Contains("Bedrock")||source.name.Contains("Seabed")||source.name.Contains("Ground")) volume=false;
            if(volume&&key=="floor_stone") key="ground_stone";
            var filter=source.GetComponent<MeshFilter>();
            if(filter==null) {Replace(source,key,false);return;}
            Bounds local=filter.sharedMesh.bounds;
            Vector3 scale=source.transform.lossyScale;
            Vector3 size=Vector3.Scale(local.size,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            // Natural platform silhouettes have bevelled corners. An imported flat floor beneath them
            // keeps the original continuous walkable surface visible through those corners.
            if(key!="floor_stone")
            {
                Vector3 top=local.center;top.y=local.max.y;
                var backing=SpawnFit("floor_stone",source.transform.TransformPoint(top)-source.transform.up*.06f,
                    source.transform.rotation,new Vector3(size.x,.015f,size.z));
                Color ground=key=="ground_grass"?new Color(.17f,.58f,.37f):key=="ground_sand"?new Color(.72f,.63f,.44f):new Color(.48f,.53f,.52f);
                foreach(var renderer in backing.GetComponentsInChildren<Renderer>())
                {
                    renderer.sharedMaterial=catalog.DefaultToonMaterial;
                    block.Clear();block.SetColor("_BaseColor",ground);renderer.SetPropertyBlock(block);
                }
            }
            // Unspecified dressing density: source tiles at most 8m wide; existing collider tops remain authoritative.
            int nx=Mathf.Max(1,Mathf.CeilToInt(size.x/8f)), nz=Mathf.Max(1,Mathf.CeilToInt(size.z/8f));
            float height=volume?Mathf.Max(.15f,size.y):.045f;
            for(int x=0;x<nx;x++) for(int z=0;z<nz;z++)
            {
                Vector3 localCenter=new Vector3(local.min.x+local.size.x*(x+.5f)/nx,local.max.y,
                    local.min.z+local.size.z*(z+.5f)/nz);
                Vector3 center=source.transform.TransformPoint(localCenter)-source.transform.up*(height*.5f);
                SpawnFit(key,center,source.transform.rotation,new Vector3(size.x/nx,height,size.z/nz));
            }
            Hide(source);
        }
        private void CoverShore(Renderer source)
        {
            // M2's shore is a wedge, not a rotated cube. Fit imported sand tiles to its actual sloping top.
            Vector3[] vertices=source.GetComponent<MeshFilter>().sharedMesh.vertices;
            Vector3 start=source.transform.TransformPoint(vertices[0]);
            Vector3 along=source.transform.TransformPoint(vertices[1])-start;
            Vector3 across=source.transform.TransformPoint(vertices[3])-start;
            Vector3 normal=Vector3.Cross(across,along).normalized;
            Quaternion rotation=Quaternion.LookRotation(across.normalized,normal);
            int nx=Mathf.CeilToInt(along.magnitude/8),nz=Mathf.CeilToInt(across.magnitude/8);
            for(int x=0;x<nx;x++) for(int z=0;z<nz;z++)
                SpawnFit("ground_sand",start+along*((x+.5f)/nx)+across*((z+.5f)/nz)-normal*.0225f,
                    rotation,new Vector3(along.magnitude/nx,.045f,across.magnitude/nz));
            Hide(source);
        }
        private void CoverWall(Renderer source)
        {
            Bounds local=source.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 size=Vector3.Scale(local.size,source.transform.lossyScale);
            bool alongX=size.x>size.z;
            int count=Mathf.Max(1,Mathf.CeilToInt((alongX?size.x:size.z)/4f));
            for(int i=0;i<count;i++)
            {
                Vector3 p=local.center;
                if(alongX) p.x=local.min.x+local.size.x*(i+.5f)/count; else p.z=local.min.z+local.size.z*(i+.5f)/count;
                Vector3 dimensions=alongX?new Vector3(size.x/count,size.y,size.z):new Vector3(size.z/count,size.y,size.x);
                Quaternion rotation=source.transform.rotation*(alongX?Quaternion.identity:Quaternion.Euler(0,90,0));
                SpawnFit("wall_stone",source.transform.TransformPoint(p),rotation,dimensions);
            }
            Hide(source);
        }
        private void ReplaceWater(Renderer source)
        {
            var model=SpawnFit("ground_dirt",source.bounds.center,Quaternion.identity,source.bounds.size);
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial=catalog.WaterMaterial;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            }
            Hide(source);
        }
        private void Replace(Renderer source,string key,bool dynamicColor,Transform parent=null)
        {
            var model=SpawnFit(key,source.bounds.center,source.transform.rotation,LocalWorldSize(source),parent);
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
                if(!renderer.name.StartsWith("Art Outline"))renderer.shadowCastingMode=source.shadowCastingMode;
            if(dynamicColor) Link(source,model);
            Hide(source);
        }
        private static Vector3 LocalWorldSize(Renderer renderer)
        {
            if(renderer.TryGetComponent<MeshFilter>(out var mesh)) return Vector3.Scale(mesh.sharedMesh.bounds.size,renderer.transform.lossyScale);
            return renderer.bounds.size;
        }
        private void Hide(Renderer renderer)
        {
            if(handled.Add(renderer)) HiddenGrayboxRenderers++;
            renderer.enabled=false;
        }
        private void Link(Renderer source,GameObject model)
        {
            links.Add(new ColorLink {Source=source,Targets=model.GetComponentsInChildren<Renderer>(true),
                Fallback=source.sharedMaterial!=null&&source.sharedMaterial.HasProperty("_BaseColor")?source.sharedMaterial.GetColor("_BaseColor"):Color.white});
        }
        private GameObject SpawnHeight(string key,Vector3 feet,float height,Quaternion rotation,Transform parent=null)
        {
            var bounds=BoundsFor(key);
            float factor=height/Mathf.Max(.001f,bounds.size.y);
            return SpawnFit(key,feet+rotation*(Vector3.up*height*.5f),rotation,bounds.size*factor,parent);
        }
        private GameObject SpawnFit(string key,Vector3 center,Quaternion rotation,Vector3 size,Transform parent=null)
        {
            var bounds=BoundsFor(key);
            var instance=Instantiate(catalog.Model(key),parent!=null?parent:world,false);
            instance.name="Imported "+key;
            instance.transform.rotation=rotation;
            Vector3 target=new Vector3(Mathf.Max(.015f,Mathf.Abs(size.x)),Mathf.Max(.015f,Mathf.Abs(size.y)),Mathf.Max(.015f,Mathf.Abs(size.z)));
            Vector3 scale=new Vector3(target.x/Mathf.Max(.001f,bounds.size.x),target.y/Mathf.Max(.001f,bounds.size.y),target.z/Mathf.Max(.001f,bounds.size.z));
            // Root/actor parents are unscaled in M4. Fitting the visual never changes physics geometry.
            instance.transform.localScale=scale;
            instance.transform.position=center-rotation*Vector3.Scale(bounds.center,scale);
            DisableColliders(instance); ModelInstances++;
            if(parent!=null) ArtStyle.ApplyToHierarchy(instance,catalog,true);
            return instance;
        }
        private Bounds BoundsFor(string key)
        {
            if(modelBounds.TryGetValue(key,out var bounds)) return bounds;
            var probe=Instantiate(catalog.Model(key)); probe.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); probe.transform.localScale=Vector3.one;
            bool first=true; bounds=default;
            foreach(var renderer in probe.GetComponentsInChildren<Renderer>(true))
            {
                if(first) {bounds=renderer.bounds;first=false;} else bounds.Encapsulate(renderer.bounds);
            }
            probe.SetActive(false); Destroy(probe);
            if(first) throw new System.InvalidOperationException("Environment model has no renderer: "+key);
            modelBounds.Add(key,bounds); return bounds;
        }
        private static void DisableColliders(GameObject root)
        {
            foreach(var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
        }
        private void AddRegionalProps()
        {
            // Decoration stays outside puzzle/room/boss and approach paths. No gameplay colliders are added.
            Vector3[] points={new Vector3(-26,0,-8),new Vector3(-18,0,-10),new Vector3(15,0,-9),new Vector3(25,0,-8),
                new Vector3(-29,0,22),new Vector3(29,0,37),new Vector3(26,0,16),new Vector3(-27,0,47)};
            if(scene.Region==M4RegionId.Teluna)
                points=new[]{new Vector3(6,0,-10),new Vector3(-6,0,-11),new Vector3(-31,0,13),
                    new Vector3(-13,0,5),new Vector3(29,0,37),new Vector3(28,0,16)};
            for(int i=0;i<points.Length;i++)
            {
                Vector3 p=points[i];
                if(!Physics.Raycast(p+Vector3.up*40,Vector3.down,out var hit,60,1<<8)) continue;
                p.y=hit.point.y;
                string key=scene.Region==M4RegionId.Teluna?"tree_palm":scene.Region==M4RegionId.Zephyr?"tree_oak":
                    scene.Region==M4RegionId.Granite?"rock_bare":scene.Region==M4RegionId.Voltheim?"column":"rock_bare";
                SpawnHeight(key,p,scene.Region==M4RegionId.Zephyr||scene.Region==M4RegionId.Teluna?4.5f:2.7f,Quaternion.Euler(0,i*47,0));
                SpawnHeight(scene.Region==M4RegionId.Granite?"crystal":"bush",p+new Vector3(1.5f,0,.4f),.65f,Quaternion.identity);
            }
            if(scene.Region==M4RegionId.Teluna)
                SpawnHeight("boat",new Vector3(8,-.15f,-9),.7f,Quaternion.Euler(0,30,0));
            SpawnHeight("barrel",scene.Layout.Npc+Vector3.right*1.4f,.9f,Quaternion.identity);
            SpawnHeight("crate",scene.Layout.Npc+new Vector3(1.5f,0,.9f),.75f,Quaternion.identity);
        }
        private void LateUpdate()=>UpdateColors();
        private void UpdateColors()
        {
            foreach(var link in links)
            {
                if(link.Source==null) continue;
                block.Clear(); link.Source.GetPropertyBlock(block);
                Color tint=block.HasColor("_BaseColor")?block.GetColor("_BaseColor"):link.Fallback;
                foreach(var renderer in link.Targets)
                {
                    if(renderer==null||renderer.name.StartsWith("Art Outline")) continue;
                    Color original=baseColors.TryGetValue(renderer,out var color)?color:Color.white;
                    block.Clear(); renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor",original*Color.Lerp(Color.white,tint,.55f));
                    renderer.SetPropertyBlock(block);
                }
            }
        }
    }
}
