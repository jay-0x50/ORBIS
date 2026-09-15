using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Art;
using Orbis.EditorSupport;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M4;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Creates an editable world once. Runtime binds these objects without rebuilding them.</summary>
    public static class OpenWorldSceneBuilder
    {
        public const string ScenePath = "Assets/Orbis/Game/Scenes/Orbis_OpenWorld.unity";
        private const string Generated = "Assets/Orbis/Game/Generated";
        private const string CatalogPath = "Assets/Orbis/Art/Resources/Art/Catalog.asset";
        [MenuItem("Orbis/Development/Legacy/Game/Create Open World")]
        public static void CreateAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (FieldSceneBuildPolicy.IsProductMode)
                throw new BuildFailedException("The canonical field already exists. Use Orbis > Field > Open Field to edit it; legacy generators cannot replace its exported scenes.");
            if (File.Exists(ScenePath)) { EnsureBuildEntry(); Validate(); Debug.Log("Existing authored world preserved: " + ScenePath); return; }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var catalog = AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(CatalogPath);
            if (catalog == null || catalog.DefaultToonMaterial == null || catalog.OutlineMaterial == null)
                throw new BuildFailedException("The existing Orbis Art catalog is required before creating the open world.");
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Directory.CreateDirectory(Generated + "/Materials");
            Directory.CreateDirectory(Generated + "/Meshes");
            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new Builder(catalog).Build();
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new BuildFailedException("Could not save " + ScenePath);
            EnsureBuildEntry(); Validate();
            Debug.Log("Authored Agnia open world created: " + ScenePath);
        }
        [MenuItem("Orbis/Development/Legacy/Game/Open Open World")]
        public static void OpenOpenWorld()
        {
            if (FieldSceneBuildPolicy.IsProductMode) { FieldSceneAuthoring.OpenField(); return; }
            if (File.Exists(IslandSceneBuilder.ScenePath)) { IslandSceneBuilder.OpenIsland(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) CreateAndValidate();
            if (!File.Exists(ScenePath) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var entry = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<GameSceneEntry>(true)).Single();
            Selection.activeGameObject = entry.World.gameObject;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0,1,12), Quaternion.Euler(43,-25,0), 56);
        }
        [MenuItem("Orbis/Development/Legacy/Game/Validate Open World")]
        public static void Validate()
        {
            if (!File.Exists(ScenePath)) throw new BuildFailedException("Create the authored open world first.");
            Scene preview = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var all = preview.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
                var worlds = all.Select(x => x.GetComponent<M4SceneBootstrap>()).Where(x => x != null).ToArray();
                if (worlds.Length != 1 || worlds[0].AuthoredRegion == null || worlds[0].InitializeOnAwake)
                    throw new BuildFailedException("The authored scene needs one manually initialized world with serialized bindings.");
                worlds[0].AuthoredRegion.Validate();
                if (all.Count(x => x.GetComponent<WaterVolume>() != null) != 1 || !all.Any(x => x.GetComponent<ClimbableSurface>() != null))
                    throw new BuildFailedException("The saved world needs its water volume and climbable cliff.");
                if (all.Any(x => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(x.gameObject) > 0))
                    throw new BuildFailedException("The authored scene contains a missing script.");
                var renderers = all.Select(x => x.GetComponent<Renderer>()).Where(x => x != null).ToArray();
                if (renderers.Length < 100) throw new BuildFailedException("The open world needs actual saved environment models.");
                foreach (Renderer renderer in renderers)
                    foreach (Material material in renderer.sharedMaterials)
                        if (material == null || !EditorUtility.IsPersistent(material)) throw new BuildFailedException("Unsaved material on " + renderer.name);
                foreach (var filter in all.Select(x => x.GetComponent<MeshFilter>()).Where(x => x != null))
                    if (filter.sharedMesh == null || !EditorUtility.IsPersistent(filter.sharedMesh)) throw new BuildFailedException("Unsaved mesh on " + filter.name);
                var entry = all.Select(x => x.GetComponent<GameSceneEntry>()).SingleOrDefault(x => x != null);
                if (entry == null || entry.World != worlds[0] || entry.OverviewCamera == null)
                    throw new BuildFailedException("The selection/start entry and overview camera are missing.");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        [MenuItem("Orbis/Development/Legacy/Game/Apply Ground and Cliff Materials")]
        public static void ApplyPresentationFixes() => OpenWorldPresentationFixes.ApplySavedScene();
        private static void EnsureBuildEntry()
        {
            if (FieldSceneBuildPolicy.IsProductMode) { FieldSceneBuildPolicy.ApplyProductLayout(); return; }
            var entries = EditorBuildSettings.scenes.ToList();
            int index = entries.FindIndex(x => x.path == ScenePath);
            if (index < 0) entries.Add(new EditorBuildSettingsScene(ScenePath, true));
            else entries[index] = new EditorBuildSettingsScene(ScenePath, true);
            // M1.6 character selection stays first; existing ordering is preserved.
            EditorBuildSettings.scenes = entries.ToArray();
        }
        private sealed class Builder
        {
            private readonly ArtAssetCatalog catalog;
            private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
            private readonly Dictionary<string, Bounds> bounds = new Dictionary<string, Bounds>();
            private Transform root;
            private M4AuthoredRegion binding;
            private Material stone, sand, path, accent;
            public Builder(ArtAssetCatalog assets) { catalog = assets; }
            public void Build()
            {
                root = Node("ORBIS / Agnia Open World", null, Vector3.zero);
                var world = root.gameObject.AddComponent<M4SceneBootstrap>();
                world.InitializeOnAwake = false;
                binding = root.gameObject.AddComponent<M4AuthoredRegion>(); world.AuthoredRegion = binding;
                var serialized = new SerializedObject(world);
                serialized.FindProperty("region").enumValueIndex = (int)M4RegionId.Agnia;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                stone = Material("Agnia Stone", new Color(.44f,.46f,.41f));
                sand = Material("Lake Sand", new Color(.63f,.58f,.40f));
                path = Material("Warm Trail", new Color(.54f,.46f,.34f));
                accent = Material("Fire Gold", new Color(1f,.60f,.18f));
                BuildTerrain(Node("01 Terrain and Traversal", root, Vector3.zero));
                BuildPuzzle(Node("02 Field Puzzle", root, Vector3.zero));
                BuildChallenge(Node("03 Challenge Courtyard", root, Vector3.zero));
                BuildBoss(Node("04 Guardian Arena", root, Vector3.zero));
                BuildSettlement(Node("05 Settlement and Transit", root, Vector3.zero));
                BuildLightingAndEntry(world, Node("06 Lighting and View", root, Vector3.zero));
                OpenWorldPresentationFixes.ApplyTo(root.gameObject,catalog);
                RecordPrefabOverrides();
                Physics.SyncTransforms();
            }
            private void BuildTerrain(Transform terrain)
            {
                // Preserve M2 Agnia's 60 x 55 m collision footprint. Models follow their editable collider parents.
                Ground("South Ground", terrain, new Vector3(0,-2,-3), new Vector3(60,4,24), "ground_grass");
                Ground("North Ground", terrain, new Vector3(0,-2,32), new Vector3(60,4,16), "ground_grass");
                Ground("East Ground", terrain, new Vector3(11.5f,-2,16.5f), new Vector3(37,4,15), "ground_grass");
                Ground("West Ground", terrain, new Vector3(-25.5f,-2,16.5f), new Vector3(9,4,15), "ground_grass");
                Ground("Lake Floor", terrain, new Vector3(-14,-3.2f,16.5f), new Vector3(14,.4f,15), "ground_sand");
                BuildShore(terrain);
                var cliff = Solid("Climbable Eight Meter Cliff", terrain, new Vector3(0,4,16), new Vector3(8,8,8));
                cliff.gameObject.AddComponent<ClimbableSurface>();
                Surface("Summit", cliff, new Vector3(0,8.006f,16), new Vector2(8,8), stone);
                Model("cliff", "South Cliff Face", cliff, new Vector3(0,0,12.2f), new Vector3(8,8,.4f));
                Model("cliff", "North Cliff Face", cliff, new Vector3(0,0,19.8f), new Vector3(8,8,.4f), 180);
                Model("cliff", "West Cliff Face", cliff, new Vector3(-3.8f,0,16), new Vector3(8,8,.4f), -90);
                Model("cliff", "East Cliff Face", cliff, new Vector3(3.8f,0,16), new Vector3(8,8,.4f), 90);
                Model("rock_bare", "Summit Rock", cliff, new Vector3(-2.5f,8,18), new Vector3(2,1.25f,1.4f));
                Model("board", "Climbing Trail Sign", terrain, new Vector3(-2,0,10), new Vector3(.9f,1.2f,.35f), 180);
                Surface("Cliff Approach", terrain, new Vector3(0,.02f,6), new Vector2(2,12), path);
                Surface("Puzzle Trail", terrain, new Vector3(7,.024f,3), new Vector2(14,2), path);
                Surface("Lakeside Trail", terrain, new Vector3(-9,.026f,5), new Vector2(18,2), path);
                Surface("Challenge Trail", terrain, new Vector3(6.5f,.022f,16), new Vector2(2.5f,26), path);
                var water = Node("Water Volume", terrain, new Vector3(-14,-1.5f,16.5f)); water.gameObject.layer = 11;
                water.gameObject.AddComponent<WaterVolume>().Configure(Vector3.zero, new Vector3(14,3,15), 0);
                var surface = Surface("Water Surface", water, new Vector3(-14,-.035f,16.5f), new Vector2(14,15), catalog.WaterMaterial);
                surface.gameObject.layer = 11; surface.shadowCastingMode = ShadowCastingMode.Off; surface.receiveShadows = false;
                Model("boat", "Moored Lake Canoe", water, new Vector3(-19,-.17f,17.5f), new Vector3(1.15f,.45f,3), -15);
                Model("bridge", "Short Lakeside Jetty", terrain, new Vector3(-23,0,14), new Vector3(2.4f,.22f,4.5f), 90);
                Boundary("North Ridge", terrain, new Vector3(0,1,40), new Vector3(60,2,.4f), 0);
                Boundary("South Ridge", terrain, new Vector3(0,1,-15), new Vector3(60,2,.4f), 180);
                Boundary("West Ridge", terrain, new Vector3(-30,1,12.5f), new Vector3(.4f,2,55), -90);
                Boundary("East Ridge", terrain, new Vector3(30,1,12.5f), new Vector3(.4f,2,55), 90);
                var trees = Node("Woodland and Rock Outcrops", terrain, Vector3.zero);
                Vector3[] grove = {
                    new Vector3(-25,0,-8),new Vector3(-19,0,-6),new Vector3(-13,0,-10),new Vector3(-7,0,-8),
                    new Vector3(5,0,-10),new Vector3(10,0,-10),new Vector3(-26,0,3),new Vector3(-25,0,22),
                    new Vector3(-24,0,29),new Vector3(-17,0,31),new Vector3(-10,0,29),new Vector3(-4,0,33),
                    new Vector3(3,0,36),new Vector3(25,0,35),new Vector3(27,0,15),new Vector3(26,0,8)};
                for (int i=0;i<grove.Length;i++)
                {
                    // Unspecified art defaults: varied silhouettes while keeping routes and the arena clear.
                    Model(i%3==0?"tree_pine":"tree_oak", "Grove Tree "+(i+1), trees, grove[i], new Vector3(2.8f,3.7f+i%4*.5f,2.8f), i*41);
                    Model("bush", "Grove Bush "+(i+1), trees, grove[i]+new Vector3(1.6f,0,-.8f), new Vector3(1.3f,.7f,1.1f), i*19);
                    if(i%2==0) Model("rock_bare", "Grove Boulder "+i, trees, grove[i]+new Vector3(-1.6f,0,1.2f), new Vector3(1.7f,1.1f,1.4f), i*17);
                }
                Model("cave", "Western Ridge Cave Facade", trees, new Vector3(-27.9f,0,32), new Vector3(5,4,3), 90);
            }
            private void BuildShore(Transform terrain)
            {
                // Preserve the M2 20.6-degree walkable shore as a persistent mesh asset.
                string assetPath = Generated + "/Meshes/AgniaShore.asset";
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                if (mesh == null)
                {
                    mesh = new Mesh { name = "Agnia Walkable Shore" };
                    mesh.vertices = new[] {
                        new Vector3(-15,-3,9),new Vector3(-7,0,9),new Vector3(-7,0,24),new Vector3(-15,-3,24),
                        new Vector3(-15,-3.3f,9),new Vector3(-7,-3.3f,9),new Vector3(-7,-3.3f,24),new Vector3(-15,-3.3f,24)};
                    mesh.triangles = new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
                    mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, assetPath);
                }
                var shore = Node("Gentle Shore", terrain, Vector3.zero); shore.gameObject.layer = 8;
                shore.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                shore.gameObject.AddComponent<MeshRenderer>().sharedMaterial = sand;
                shore.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            private void BuildPuzzle(Transform puzzle)
            {
                Surface("Fire Statue Courtyard", puzzle, new Vector3(14,.035f,8.5f), new Vector2(10,7), stone);
                binding.Statues = new ElementalActor[3]; binding.StatueRenderers = new Renderer[3];
                for (int i=0;i<3;i++)
                {
                    binding.Statues[i] = Actor("Fire Statue "+(i+1), puzzle, new Vector3(11+i*3,0,8), "statue", 1.9f, false, out binding.StatueRenderers[i]);
                    for (int mark=0;mark<=i;mark++)
                        Model("crystal", "Order Mark "+(mark+1), binding.Statues[i].transform,
                            binding.Statues[i].transform.position+new Vector3((mark-i*.5f)*.28f,.04f,-.85f), new Vector3(.14f,.30f,.14f));
                }
                binding.Puzzle = puzzle.gameObject.AddComponent<FieldElementPuzzle>();
                binding.Chest = Node("Field Reward Chest", puzzle, new Vector3(14,0,10.5f));
                var chest = Model("chest", "Imported Reward Chest", binding.Chest, binding.Chest.position, new Vector3(1.5f,.92f,1.1f));
                binding.ChestRenderer = MainRenderer(chest);
                Model("column", "Puzzle Left Pillar", puzzle, new Vector3(9.5f,0,11), new Vector3(.75f,2.8f,.75f));
                Model("column", "Puzzle Right Pillar", puzzle, new Vector3(18.5f,0,11), new Vector3(.75f,2.8f,.75f));
                Model("banner", "Fire Court Banner", puzzle, new Vector3(9.5f,1.1f,10.55f), new Vector3(.5f,1.2f,.12f));
            }
            private void BuildChallenge(Transform room)
            {
                Surface("Challenge Courtyard Floor", room, new Vector3(14,.035f,27), new Vector2(12,14), stone);
                Wall("Room West Wall", room, new Vector3(8,1.5f,27), new Vector3(.4f,3,14));
                Wall("Room East Wall", room, new Vector3(20,1.5f,27), new Vector3(.4f,3,14));
                Wall("Room North Wall", room, new Vector3(14,1.5f,34), new Vector3(12,3,.4f));
                Wall("Entrance Left", room, new Vector3(10.5f,1.5f,20), new Vector3(5,3,.4f));
                Wall("Entrance Right", room, new Vector3(17.5f,1.5f,20), new Vector3(5,3,.4f));
                binding.ChallengeEntry = Node("Challenge Start Marker", room, new Vector3(14,0,21.5f));
                Surface("Challenge Interaction Tile", binding.ChallengeEntry, binding.ChallengeEntry.position+Vector3.up*.055f, new Vector2(1.8f,1.8f), accent);
                binding.Targets = new ElementalActor[3]; binding.ChallengeRenderers = new Renderer[3];
                Vector3[] positions = {new Vector3(11,0,26),new Vector3(14,0,28),new Vector3(17,0,26)};
                for(int i=0;i<3;i++) binding.Targets[i] = Actor("Challenge Target "+(i+1), room, positions[i], "enemy", 1.8f, false, out binding.ChallengeRenderers[i]);
                binding.Challenge = room.gameObject.AddComponent<ChallengeRoom>();
                foreach(float x in new[]{8f,20f}) foreach(float z in new[]{20f,34f})
                    Model("column", "Courtyard Corner", room, new Vector3(x,0,z), new Vector3(.8f,3.4f,.8f));
            }
            private void BuildBoss(Transform arena)
            {
                Surface("Guardian Arena Floor", arena, new Vector3(20,.045f,-3), new Vector2(16,16), stone);
                binding.BossObject = Actor("Agnia Elemental Field Guardian", arena, new Vector3(20,0,-3), "enemy", 2.6f, true, out _).gameObject;
                var core = Primitive("Weakness Core", binding.BossObject.transform, new Vector3(20,1.3f,-3.92f), Vector3.one*.45f,
                    PrimitiveType.Sphere, Material("Guardian Water Weakness", new Color(.12f,.64f,1f)));
                Outline(core.GetComponent<Renderer>());
                // Eight metre encounter radius is kept clear; columns are outside the fighting footprint.
                for(int i=0;i<8;i++)
                {
                    float angle=i*Mathf.PI*.25f;
                    Model("column", "Guardian Court Pillar "+(i+1), arena,
                        new Vector3(20+Mathf.Sin(angle)*8.5f,0,-3+Mathf.Cos(angle)*8.5f), new Vector3(.7f,2.2f,.7f));
                }
            }
            private void BuildSettlement(Transform settlement)
            {
                binding.Spawn = Node("Player Spawn", settlement, new Vector3(0,.1f,0));
                Surface("Spawn Trailhead", settlement, new Vector3(0,.031f,0), new Vector2(3,3), path);
                binding.Npc = Node("Commission NPC / 아그니아", settlement, new Vector3(-3,0,0));
                var definition = catalog.Characters[1];
                var npc = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab, binding.Npc);
                npc.name = "Imported Commission NPC";
                Fit(npc, BoundsOf(npc), binding.Npc.position, new Vector3(0,1.8f,0), 0, true);
                foreach(var collider in npc.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach(var animator in npc.GetComponentsInChildren<Animator>(true))
                {
                    animator.avatar=definition.Avatar; animator.runtimeAnimatorController=definition.Controller; animator.applyRootMotion=false;
                }
                foreach(var renderer in npc.GetComponentsInChildren<Renderer>(true)) Outline(renderer);
                Model("board", "Commission Notice Board", settlement, new Vector3(-4.3f,0,.3f), new Vector3(.85f,1.3f,.3f));
                binding.Portal = Node("Regional Transit", settlement, new Vector3(-5,0,0));
                Model("obelisk", "Regional Transit Obelisk", binding.Portal, binding.Portal.position, new Vector3(.9f,2.8f,.9f));
                Model("crystal", "Survey Marker", binding.Portal, binding.Portal.position+Vector3.forward*3, new Vector3(.65f,.75f,.65f));
                var camp=Node("Trailhead Camp",settlement,new Vector3(-10,0,-2));
                Model("lamp", "Campfire", camp, camp.position, new Vector3(1.1f,.35f,1.1f));
                Model("crate", "Supply Chest", camp, camp.position+new Vector3(-2,0,0), new Vector3(1.1f,.7f,.8f), 15);
                Model("barrel", "Supply Barrel A", camp, camp.position+new Vector3(-2.2f,0,1.1f), new Vector3(.7f,1,.7f));
                Model("barrel", "Supply Barrel B", camp, camp.position+new Vector3(-3,0,.5f), new Vector3(.7f,.9f,.7f));
                Model("fence", "Camp Fence", camp, camp.position+new Vector3(0,0,-2.5f), new Vector3(5,1,.2f));
                // An existing modular tower forms a visible settlement silhouette, outside traversal routes.
                Model("tower", "Trailhead Watchtower", camp, new Vector3(-16,0,-3), new Vector3(3.2f,5.5f,3.2f));
            }
            private void BuildLightingAndEntry(M4SceneBootstrap world,Transform view)
            {
                RenderSettings.skybox=null; RenderSettings.ambientMode=AmbientMode.Trilight;
                RenderSettings.ambientSkyColor=new Color(.69f,.77f,.85f);
                RenderSettings.ambientEquatorColor=new Color(.43f,.48f,.43f);
                RenderSettings.ambientGroundColor=new Color(.22f,.24f,.25f);
                RenderSettings.fog=true; RenderSettings.fogMode=FogMode.Linear;
                RenderSettings.fogColor=new Color(.47f,.62f,.68f); RenderSettings.fogStartDistance=65; RenderSettings.fogEndDistance=130;
                var sun=Node("Agnia Sun",view,Vector3.zero).gameObject.AddComponent<Light>();
                sun.type=LightType.Directional; sun.intensity=1.4f; sun.color=new Color(1,.95f,.86f);
                sun.transform.rotation=Quaternion.Euler(48,-30,0); sun.shadows=LightShadows.Soft; RenderSettings.sun=sun;
                var focus=Node("Overview Focus",view,new Vector3(0,1,12));
                var camera=Node("Overview Camera",view,new Vector3(-39,43,-47)).gameObject.AddComponent<Camera>();
                camera.transform.LookAt(focus); camera.fieldOfView=52; camera.farClipPlane=220;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=RenderSettings.fogColor;
                camera.gameObject.AddComponent<AudioListener>();
                var entry=root.gameObject.AddComponent<GameSceneEntry>();
                entry.World=world; entry.OverviewCamera=camera; entry.OverviewFocus=focus;
            }
            private ElementalActor Actor(string name,Transform parent,Vector3 feet,string model,float height,bool boss,out Renderer body)
            {
                var node=Node(name,parent,feet); node.gameObject.layer=9;
                var collider=node.gameObject.AddComponent<CapsuleCollider>();
                collider.center=Vector3.up*(boss?1.25f:1); collider.height=boss?2.5f:2; collider.radius=boss?.65f:.42f;
                node.gameObject.AddComponent<TrainingDummy>();
                var actor=node.gameObject.AddComponent<ElementalActor>();
                actor.Configure(boss?"boss.Agnia":name,boss?ElementType.Fire:ElementType.None,ActorTeam.Enemy);
                var visual=Model(model,"Imported "+name,node,feet,new Vector3(0,height,0),0,true);
                body=MainRenderer(visual); return actor;
            }
            private Transform Ground(string name,Transform parent,Vector3 center,Vector3 size,string model)
            {
                var ground=Solid(name,parent,center,size); Vector3 top=center+Vector3.up*(size.y*.5f);
                Surface("Continuous Surface",ground,top,new Vector2(size.x,size.z),model=="ground_sand"?sand:stone);
                int columns=Mathf.CeilToInt(size.x/6),rows=Mathf.CeilToInt(size.z/6);
                float dx=size.x/columns,dz=size.z/rows;
                for(int x=0;x<columns;x++) for(int z=0;z<rows;z++)
                    Model(model,"Ground Tile "+x+" "+z,ground,
                        top+new Vector3(-size.x*.5f+dx*(x+.5f),-.045f,-size.z*.5f+dz*(z+.5f)),new Vector3(dx,.06f,dz),0,false,false);
                return ground;
            }
            private void Boundary(string name,Transform parent,Vector3 center,Vector3 size,float yaw)
            {
                var ridge=Solid(name,parent,center,size); bool alongX=size.x>size.z;
                int count=Mathf.CeilToInt((alongX?size.x:size.z)/6);
                float span=(alongX?size.x:size.z)/count;
                for(int i=0;i<count;i++)
                {
                    float offset=-(alongX?size.x:size.z)*.5f+span*(i+.5f);
                    Vector3 feet=center+(alongX?Vector3.right:Vector3.forward)*offset-Vector3.up;
                    Model("cliff","Ridge Face "+(i+1),ridge,feet,new Vector3(span,2.5f+i%3*.4f,1.2f),yaw);
                }
            }
            private void Wall(string name,Transform parent,Vector3 center,Vector3 size)
            {
                var wall=Solid(name,parent,center,size); bool alongX=size.x>size.z;
                float length=alongX?size.x:size.z; int count=Mathf.CeilToInt(length/2.5f);
                for(int i=0;i<count;i++)
                {
                    Vector3 feet=center-Vector3.up*size.y*.5f+(alongX?Vector3.right:Vector3.forward)*(-length*.5f+length/count*(i+.5f));
                    Model("wall_stone","Wall Segment "+(i+1),wall,feet,new Vector3(length/count,size.y,alongX?size.z:size.x),alongX?0:90);
                }
            }
            private Transform Solid(string name,Transform parent,Vector3 center,Vector3 size)
            {
                var node=Node(name,parent,center); node.gameObject.layer=8;
                node.gameObject.AddComponent<BoxCollider>().size=size; return node;
            }
            private Renderer Surface(string name,Transform parent,Vector3 position,Vector2 size,Material material)
            {
                // A true imported floor plane receives no inverted-hull outline.
                var surface=Model("floor_stone",name,parent,position,new Vector3(size.x,.015f,size.y),0,false,false);
                foreach(var renderer in surface.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial=material;
                return MainRenderer(surface);
            }
            private GameObject Model(string key,string name,Transform parent,Vector3 feet,Vector3 dimensions,float yaw=0,bool uniform=false,bool outline=true)
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(catalog.Model(key),parent); instance.name=name;
                if(!bounds.TryGetValue(key,out Bounds sourceBounds)) { sourceBounds=BoundsOf(instance); bounds.Add(key,sourceBounds); }
                Fit(instance,sourceBounds,feet,dimensions,yaw,uniform);
                foreach(var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true)) if(outline) Outline(renderer);
                return instance;
            }
            private static void Fit(GameObject item,Bounds local,Vector3 feet,Vector3 size,float yaw,bool uniform)
            {
                item.transform.localScale=Vector3.one;
                Vector3 scale=uniform?Vector3.one*(size.y/Mathf.Max(.0001f,local.size.y)):
                    new Vector3(size.x/Mathf.Max(.0001f,local.size.x),local.size.y<.0001f?1:size.y/local.size.y,size.z/Mathf.Max(.0001f,local.size.z));
                item.transform.localScale=scale; item.transform.rotation=Quaternion.Euler(0,yaw,0);
                var localFeet=new Vector3(local.center.x,local.min.y,local.center.z);
                item.transform.position=feet-item.transform.rotation*Vector3.Scale(localFeet,scale);
            }
            private static Bounds BoundsOf(GameObject item)
            {
                Bounds result=default; bool first=true;
                foreach(Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
                {
                    Bounds box=renderer.bounds;
                    for(int i=0;i<8;i++)
                    {
                        Vector3 corner=box.center+Vector3.Scale(box.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        Vector3 point=item.transform.InverseTransformPoint(corner);
                        if(first) {result=new Bounds(point,Vector3.zero);first=false;} else result.Encapsulate(point);
                    }
                }
                if(first) throw new BuildFailedException("Imported model has no visible bounds: "+item.name);
                return result;
            }
            private void Outline(Renderer source)
            {
                if(source==null||source.name.StartsWith("Art Outline")||source.sharedMaterial==null||source.sharedMaterial.renderQueue>=2500) return;
                Mesh mesh=source is SkinnedMeshRenderer skin?skin.sharedMesh:source.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh==null||Mathf.Min(mesh.bounds.size.x,Mathf.Min(mesh.bounds.size.y,mesh.bounds.size.z))<.0001f) return;
                var node=Node("Art Outline / "+source.name,source.transform,source.transform.position);
                node.localPosition=Vector3.zero; node.localRotation=Quaternion.identity; node.localScale=Vector3.one;
                Renderer outline;
                if(source is SkinnedMeshRenderer original)
                {
                    var copy=node.gameObject.AddComponent<SkinnedMeshRenderer>(); copy.sharedMesh=mesh;
                    copy.bones=original.bones; copy.rootBone=original.rootBone; copy.localBounds=original.localBounds; outline=copy;
                }
                else {node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;outline=node.gameObject.AddComponent<MeshRenderer>();}
                outline.sharedMaterials=Enumerable.Repeat(catalog.OutlineMaterial,mesh.subMeshCount).ToArray();
                outline.shadowCastingMode=ShadowCastingMode.Off; outline.receiveShadows=false;
                node.gameObject.AddComponent<ArtOutlineSync>().Configure(source,outline);
                // Real serialized references, including source/outline sync. No transient materials are saved.
            }
            private Material Material(string name,Color color)
            {
                if(materials.TryGetValue(name,out Material existing)) return existing;
                string assetPath=Generated+"/Materials/"+name.Replace(" ","")+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if(material==null)
                {
                    material=new Material(catalog.DefaultToonMaterial){name=name}; material.SetColor("_BaseColor",color);
                    AssetDatabase.CreateAsset(material,assetPath);
                }
                materials.Add(name,material); return material;
            }
            private void RecordPrefabOverrides()
            {
                // Programmatically fitted instances retain their position, scale and renderer changes after reload.
                foreach(var node in root.GetComponentsInChildren<Transform>(true))
                {
                    if(!PrefabUtility.IsPartOfPrefabInstance(node)) continue;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(node.gameObject);
                    foreach(var component in node.GetComponents<Component>())
                        if(component!=null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }
            private static Renderer MainRenderer(GameObject model)=>model.GetComponentsInChildren<Renderer>(true).First(x=>!x.name.StartsWith("Art Outline"));
            private static Transform Node(string name,Transform parent,Vector3 worldPosition)
            {
                var node=new GameObject(name).transform; node.SetParent(parent,false); node.position=worldPosition; return node;
            }
            private static GameObject Primitive(string name,Transform parent,Vector3 position,Vector3 size,PrimitiveType type,Material material)
            {
                var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false);
                go.transform.position=position; go.transform.localScale=size;
                Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial=material; return go;
            }
        }
    }
}
