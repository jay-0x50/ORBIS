using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M1;
using Orbis.M3;
using Orbis.M4;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Orbis.Art.Tests
{
    public sealed class ArtIntegrationTests
    {
        private M4SceneBootstrap scene;
        private ArtScenePresentation art;
        private M4RegionRouter router;
        private Keyboard keyboard;
        private Gamepad gamepad;
        private string saveDirectory;
        private float scale,fixedDelta,capture;
        private bool background,cursorVisible;
        private CursorLockMode cursor;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private InputSettings.BackgroundBehavior inputBackground;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scale=Time.timeScale;fixedDelta=Time.fixedDeltaTime;capture=Time.captureDeltaTime;
            background=Application.runInBackground;cursor=Cursor.lockState;cursorVisible=Cursor.visible;
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;inputBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            Time.timeScale=1;Time.captureDeltaTime=0;Application.runInBackground=true;
            keyboard=InputSystem.AddDevice<Keyboard>();gamepad=InputSystem.AddDevice<Gamepad>();Keys();Pad(false);
            saveDirectory=Path.Combine(Path.GetTempPath(),"OrbisArt-"+Guid.NewGuid().ToString("N"));
            M4Session.UseProgressForTests(new M4ProgressService(Path.Combine(saveDirectory,"progress.json"),()=>new DateTime(2026,9,10,3,0,0,DateTimeKind.Utc)));
            yield return SceneManager.LoadSceneAsync("Assets/Orbis/M4/Scenes/M4_Launcher.unity",LoadSceneMode.Single);
            router=M4RegionRouter.Instance;yield return AwaitRegion(M4RegionId.Agnia);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if(scene!=null)scene.Presentation.Ultimate.Cancel();
            var current=SceneManager.GetActiveScene();var empty=SceneManager.CreateScene("Art Cleanup");SceneManager.SetActiveScene(empty);
            if(current.IsValid()&&current.isLoaded)yield return SceneManager.UnloadSceneAsync(current);
            if(router!=null)Object.Destroy(router.gameObject);
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            if(gamepad!=null&&gamepad.added)InputSystem.RemoveDevice(gamepad);
            M4Session.UseProgressForTests(null);
            Time.timeScale=scale;Time.fixedDeltaTime=fixedDelta;Time.captureDeltaTime=capture;Application.runInBackground=background;
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;InputSystem.settings.backgroundBehavior=inputBackground;
            Cursor.lockState=cursor;Cursor.visible=cursorVisible;
            // Isolated test saves only. Never read or delete a user's normal game save.
            if(saveDirectory!=null&&Directory.Exists(saveDirectory))Directory.Delete(saveDirectory,true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FiveCharactersRetargetRealMotionAndSwitchWithoutGrowingEffectPools()
        {
            Assert.That(art.Characters.PrewarmedCount,Is.EqualTo(5));
            int trails=scene.GetComponentsInChildren<TrailRenderer>(true).Length;
            int meshes=scene.GetComponentsInChildren<MeshFilter>(true).Count(x=>x.name.StartsWith("Snapshot "));
            var seen=new System.Collections.Generic.HashSet<ElementType>();
            for(int slot=0;slot<4;slot++)
            {
                scene.Party.TrySwitch(slot);yield return new WaitForSecondsRealtime(.4f);
                yield return CheckCharacter();seen.Add(art.Characters.ActiveElement);
                Assert.That(scene.GetComponentsInChildren<TrailRenderer>(true).Length,Is.EqualTo(trails));
                Assert.That(scene.GetComponentsInChildren<MeshFilter>(true).Count(x=>x.name.StartsWith("Snapshot ")),Is.EqualTo(meshes));
            }
            scene.Vitals.Restore(63);scene.Traversal.Stamina.SetCurrent(52);
            Assert.That(router.Travel(M4RegionId.Granite),Is.True);yield return AwaitRegion(M4RegionId.Granite);
            Assert.That(scene.Vitals.Health,Is.EqualTo(63));
            Assert.That(scene.Party.TrySwitch(3),Is.True);yield return new WaitForSecondsRealtime(.4f);
            yield return CheckCharacter();seen.Add(art.Characters.ActiveElement);
            Assert.That(seen.Count,Is.EqualTo(5));
        }

        private IEnumerator CheckCharacter()
        {
            var animator=art.Characters.ActiveAnimator;
            Assert.That(animator.isHuman&&animator.avatar.isValid,Is.True);
            Assert.That(animator.applyRootMotion,Is.False);
            Assert.That(animator.runtimeAnimatorController.animationClips.All(x=>x.isHumanMotion),Is.True);
            Assert.That(scene.Traversal.transform.Find("Visual").gameObject.activeSelf,Is.False);
            var skin=animator.GetComponentsInChildren<SkinnedMeshRenderer>().First(x=>!x.name.StartsWith("Art Outline")&&!x.name.StartsWith("M3 Silhouette"));
            Assert.That(skin.sharedMesh.vertexCount,Is.GreaterThan(100));
            // NormalizeVisibleModelHeight measures the authored Idle start pose before weapons are attached.
            // Sample that same pose and include rigid armor/helmet parts, then restore the live animation clock.
            AnimatorStateInfo previousState=animator.GetCurrentAnimatorStateInfo(0);
            float previousSpeed=animator.speed;
            try
            {
                animator.speed=0f;animator.Play("Idle",0,0f);animator.Update(0f);
                CaptureCharacter();
                Bounds bodyBounds=MeasureBody(animator);
                Assert.That(bodyBounds.size.y,Is.EqualTo(1.8f).Within(.015f),"The complete body in its reference pose must be normalized to 1.8 m.");
                Assert.That(bodyBounds.min.y-scene.Traversal.transform.position.y,Is.EqualTo(0f).Within(.015f),"The reference-pose feet must meet the pawn origin.");
            }
            finally
            {
                animator.Play(previousState.fullPathHash,0,previousState.normalizedTime);
                animator.speed=previousSpeed;animator.Update(0f);
            }
            Material material=skin.sharedMaterial;Texture atlas=material.GetTexture("_BaseMap");
            Assert.That(atlas,Is.Not.Null);
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            Vector3 idleHandPosition=animator.transform.InverseTransformPoint(hand.position);
            Quaternion idleHandRotation=Quaternion.Inverse(animator.transform.rotation)*hand.rotation;
            Pad(true);yield return null;Pad(false);yield return new WaitForSecondsRealtime(.18f);
            float handDistance=Vector3.Distance(idleHandPosition,animator.transform.InverseTransformPoint(hand.position));
            float handAngle=Quaternion.Angle(idleHandRotation,Quaternion.Inverse(animator.transform.rotation)*hand.rotation);
            Assert.That(handDistance>.025f||handAngle>2f,Is.True,"The imported attack must move the hand through its animated arm chain; wrist-local rotation may stay fixed.");
            Assert.That(scene.Presentation.MeshEffects.TrailEmitting,Is.True);
            yield return new WaitForSecondsRealtime(.6f);
            scene.Presentation.MeshEffects.BeginAppearance();
            Assert.That(skin.sharedMaterial,Is.SameAs(material));Assert.That(skin.sharedMaterial.GetTexture("_BaseMap"),Is.SameAs(atlas));
            var block=new MaterialPropertyBlock();skin.GetPropertyBlock(block);Assert.That(block.GetFloat("_Threshold"),Is.GreaterThan(.9f));
            yield return new WaitForSecondsRealtime(.35f);
            CaptureCharacter();
        }

        private static Bounds MeasureBody(Animator animator)
        {
            bool found=false;Bounds result=default;var baked=new Mesh();
            Transform tip=animator.GetComponentsInChildren<Transform>(true).FirstOrDefault(x=>x.name=="Weapon Trail Tip");
            Transform weapon=tip!=null?tip.parent:null;
            try
            {
                foreach(var renderer in animator.GetComponentsInChildren<Renderer>())
                {
                    if(renderer.name.StartsWith("Art Outline")||renderer.name.StartsWith("M3 Silhouette")||!renderer.enabled||
                        (weapon!=null&&renderer.transform.IsChildOf(weapon)))continue;
                    Mesh mesh;
                    if(renderer is SkinnedMeshRenderer skin){baked.Clear();skin.BakeMesh(baked,true);mesh=baked;}
                    else if(renderer.TryGetComponent<MeshFilter>(out var filter))mesh=filter.sharedMesh;
                    else continue;
                    if(mesh==null)continue;
                    foreach(var vertex in mesh.vertices)
                    {
                        Vector3 world=renderer.transform.TransformPoint(vertex);
                        if(!found){result=new Bounds(world,Vector3.zero);found=true;}else result.Encapsulate(world);
                    }
                }
            }
            finally{Object.Destroy(baked);}
            Assert.That(found,Is.True);return result;
        }
        private void CaptureCharacter()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            var camera=Camera.main;camera.GetComponent<CinemachineBrain>().enabled=false;
            Vector3 feet=scene.Traversal.transform.position;
            camera.transform.position=feet+new Vector3(2.35f,1.65f,3.7f);camera.transform.LookAt(feet+Vector3.up*.95f);camera.fieldOfView=35;
            Capture(camera,"Art_Character_"+art.Characters.ActiveElement+".png");
        }

        [UnityTest]
        public IEnumerator ReenabledPresentationCapturesTheOutgoingAvatarBeforeSwapping()
        {
            var presentation=scene.Presentation;
            presentation.enabled=false;yield return null;
            presentation.enabled=true;yield return null;
            presentation.MeshEffects.Clear();
            var outgoing=art.Characters.ActiveAnimator;
            var skin=outgoing.GetComponentsInChildren<SkinnedMeshRenderer>().First(x=>!x.name.StartsWith("Art Outline")&&!x.name.StartsWith("M3 Silhouette"));
            var expected=new Mesh();skin.BakeMesh(expected,true);Vector3[] pose=expected.vertices;Object.Destroy(expected);
            Assert.That(scene.Party.TrySwitch(1),Is.True);
            Assert.That(art.Characters.ActiveAnimator,Is.Not.SameAs(outgoing));
            var snapshots=scene.GetComponentsInChildren<MeshFilter>().Where(x=>x.name.StartsWith("Snapshot ")&&x.sharedMesh!=null&&x.GetComponent<Renderer>().enabled).ToArray();
            Assert.That(snapshots.Any(x=>x.sharedMesh.vertices.SequenceEqual(pose)),Is.True,"The outgoing mesh must be baked before the incoming avatar is activated, including after M3 re-subscription.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledPresentationAndReenabledRosterFollowTheCurrentPartyMember()
        {
            scene.Presentation.enabled=false;
            Assert.That(scene.Party.TrySwitch(1),Is.True);
            Assert.That(art.Characters.ActiveElement,Is.EqualTo(scene.Party.ActiveMember.Actor.Element));
            var block=new MaterialPropertyBlock();
            foreach(var renderer in art.Characters.ActiveAnimator.GetComponentsInChildren<Renderer>())
            {
                if(renderer.name.StartsWith("Art Outline")||renderer.name.StartsWith("M3 Silhouette"))continue;
                renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_Threshold"),Is.Zero,"Disabled effects cannot finish a hidden incoming avatar.");
            }
            scene.Presentation.enabled=true;
            art.Characters.enabled=false;
            Assert.That(scene.Party.TrySwitch(2),Is.True);
            art.Characters.enabled=true;
            Assert.That(art.Characters.ActiveElement,Is.EqualTo(scene.Party.ActiveMember.Actor.Element));
            Assert.That(art.Characters.ActiveVisual.activeInHierarchy,Is.True);
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(art.Characters.ActiveAnimator.speed,Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator ImportedGliderFollowsItsVisibilityParentAndPlayerTeleport()
        {
            Transform pawn=scene.Traversal.transform;
            Transform glider=pawn.Find("Placeholder Glider");
            Assert.That(glider,Is.Not.Null);
            var source=glider.Find("Glider Wing").GetComponent<Renderer>();
            Transform banner=glider.Find("Imported banner");
            Assert.That(source.enabled,Is.False);Assert.That(banner,Is.Not.Null);
            bool wasActive=glider.gameObject.activeSelf;Vector3 original=pawn.position;
            try
            {
                glider.gameObject.SetActive(false);Assert.That(banner.gameObject.activeInHierarchy,Is.False);
                glider.gameObject.SetActive(true);Assert.That(banner.gameObject.activeInHierarchy,Is.True);
                Assert.That(banner.GetComponentsInChildren<Renderer>().Any(x=>x.enabled),Is.True);
                Vector3 local=banner.localPosition,world=banner.position,delta=new Vector3(2f,1f,3f);
                scene.Traversal.Teleport(original+delta);
                Assert.That(Vector3.Distance(banner.localPosition,local),Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(banner.position,world+delta),Is.LessThan(.0001f));
                Assert.That(source.enabled,Is.False);
            }
            finally
            {
                scene.Traversal.Teleport(original);glider.gameObject.SetActive(wasActive);
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator SkinnedAfterimageKeepsPoseAndUltimateOutlineUsesTheRealMesh()
        {
            var animator=art.Characters.ActiveAnimator;
            var skin=animator.GetComponentsInChildren<SkinnedMeshRenderer>().First(x=>!x.name.StartsWith("Art Outline")&&!x.name.StartsWith("M3 Silhouette"));
            var effects=scene.Presentation.MeshEffects;
            effects.EmitAfterimage(ElementType.Fire);
            var snapshot=scene.GetComponentsInChildren<MeshFilter>().First(x=>x.name.StartsWith("Snapshot ")&&x.sharedMesh!=null&&x.sharedMesh.name.StartsWith("Pooled Skinned"));
            Assert.That(snapshot.sharedMesh.vertexCount,Is.EqualTo(skin.sharedMesh.vertexCount));
            Vector3[] vertices=snapshot.sharedMesh.vertices;
            var posed=new Mesh();skin.BakeMesh(posed,true);
            Vector3[] reference=posed.vertices;
            Assert.That(vertices.SequenceEqual(reference),Is.True,"A compensated local-space snapshot must match its scaled source instead of applying scale twice.");
            Bounds sourceWorld=new Bounds(skin.transform.TransformPoint(reference[0]),Vector3.zero);
            Bounds ghostWorld=new Bounds(snapshot.transform.TransformPoint(vertices[0]),Vector3.zero);
            for(int i=1;i<vertices.Length;i++)
            {
                sourceWorld.Encapsulate(skin.transform.TransformPoint(reference[i]));
                ghostWorld.Encapsulate(snapshot.transform.TransformPoint(vertices[i]));
            }
            Assert.That(Vector3.Distance(ghostWorld.size,sourceWorld.size),Is.LessThan(.001f),"The ghost must preserve the posed source's world dimensions.");
            Assert.That(Vector3.Distance(ghostWorld.center,sourceWorld.center),Is.LessThan(.001f),"The ghost must preserve the posed source's world position.");
            Object.Destroy(posed);
            Pad(true);yield return null;Pad(false);yield return new WaitForSecondsRealtime(.14f);
            CollectionAssert.AreEqual(vertices,snapshot.sharedMesh.vertices,"Baked ghost should not animate with its source.");
            effects.SetOutline(true,ElementType.Fire);
            var outline=skin.GetComponentsInChildren<SkinnedMeshRenderer>().First(x=>x.name.StartsWith("M3 Silhouette"));
            Assert.That(outline.enabled,Is.True);Assert.That(outline.sharedMesh,Is.SameAs(skin.sharedMesh));
            effects.SetOutline(false,ElementType.Fire);Assert.That(outline.enabled,Is.False);
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(scene.Presentation.PlayUltimatePresentation(),Is.True);
            yield return new WaitForSecondsRealtime(1.4f);
            Assert.That(scene.Presentation.Ultimate.IsPlaying,Is.False);
            Assert.That(Time.timeScale,Is.EqualTo(1f));
            Assert.That(scene.Traversal.GetComponent<M0Input>().GameplayEnabled,Is.True);
        }

        [UnityTest]
        public IEnumerator AllWorldsUseImportedMeshesAndIconsAndRetainObjectiveColliders()
        {
            foreach(var region in M4RegionCatalog.All)
            {
                if(scene.Region!=region.Id){Assert.That(router.Travel(region.Id),Is.True);yield return AwaitRegion(region.Id);}
                Assert.That(art.World.ModelInstances,Is.GreaterThan(50));
                Assert.That(art.World.HiddenGrayboxRenderers,Is.GreaterThan(30));
                Assert.That(scene.ShowHud,Is.False);Assert.That(art.Hud.isActiveAndEnabled,Is.True);
                Assert.That(art.Catalog.Icons.All(x=>x.Texture!=null&&x.Texture.width>4),Is.True);
                Assert.That(art.World.VisualRoot.GetComponentsInChildren<Collider>().Any(x=>x.enabled),Is.False);
                foreach(var mesh in art.World.VisualRoot.GetComponentsInChildren<MeshFilter>())
                {
                    Assert.That(mesh.sharedMesh,Is.Not.Null);
#if UNITY_EDITOR
                    Assert.That(UnityEditor.AssetDatabase.GetAssetPath(mesh.sharedMesh),Does.StartWith("Assets/ImportedAssets/"));
#endif
                }
                Assert.That(scene.Content.Challenge.TryStart(),Is.True);
                Assert.That(scene.Content.Targets.All(x=>x.GetComponent<Collider>().enabled),Is.True);
                scene.Content.Challenge.ResetChallenge();
                Assert.That(scene.Content.Statues.All(x=>x.GetComponent<Collider>().enabled),Is.True);
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null)
                {
                    var camera=Camera.main;camera.GetComponent<CinemachineBrain>().enabled=false;
                    camera.transform.position=new Vector3(53,58,-49);camera.transform.LookAt(new Vector3(0,region.Id==M4RegionId.Granite?5:0,20));camera.fieldOfView=55;camera.farClipPlane=220;
                    Capture(camera,"Art_Region_"+region.Id+".png");
                }
            }
        }
        private IEnumerator AwaitRegion(M4RegionId region)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+40;
            do {yield return null;scene=Object.FindAnyObjectByType<M4SceneBootstrap>();}
            while((router.IsLoading||scene==null||scene.Region!=region)&&Time.realtimeSinceStartupAsDouble<deadline);
            Assert.That(router.LastError,Is.Null);Assert.That(scene,Is.Not.Null);Assert.That(scene.Region,Is.EqualTo(region));
            art=scene.GetComponent<ArtScenePresentation>();Assert.That(art,Is.Not.Null);
            yield return new WaitForSecondsRealtime(.45f);
        }
        private void Keys(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        private void Pad(bool attack)=>InputSystem.QueueStateEvent(gamepad,attack?new GamepadState().WithButton(GamepadButton.West):new GamepadState());
        private static void Capture(Camera camera,string name)
        {
            RenderTexture previous=RenderTexture.active,previousTarget=camera.targetTexture;float aspect=camera.aspect;
#if UNITY_EDITOR
            bool async=UnityEditor.ShaderUtil.allowAsyncCompilation;UnityEditor.ShaderUtil.allowAsyncCompilation=false;
#endif
            var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);Texture2D pixels=null;
            try
            {
                target.Create();camera.aspect=1280f/720f;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
                var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","TestResults"));Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory,name),pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=previousTarget;camera.aspect=aspect;
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation=async;
#endif
                if(pixels!=null)Object.Destroy(pixels);target.Release();Object.Destroy(target);
            }
        }
    }
}






