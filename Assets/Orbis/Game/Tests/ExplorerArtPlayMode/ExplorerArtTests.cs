using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Art;
using Orbis.M0;
using Orbis.M1;
using Orbis.M4;
using Orbis.M16;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class ExplorerArtTests
    {
        string directory;
        M4ProgressService progress;
        M4SceneBootstrap world;
        Keyboard keyboard;
        bool background,cursorVisible;
        CursorLockMode cursor;
        float scale,fixedDelta,capture;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
        InputSettings.BackgroundBehavior backgroundInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scale=Time.timeScale;fixedDelta=Time.fixedDeltaTime;capture=Time.captureDeltaTime;
            background=Application.runInBackground;cursor=Cursor.lockState;cursorVisible=Cursor.visible;
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;backgroundInput=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground=true;Time.timeScale=1;Time.captureDeltaTime=1f/60f;
            keyboard=InputSystem.AddDevice<Keyboard>();Keys();
            directory=Path.Combine(Path.GetTempPath(),"Orbis-Explorer-Art-"+Guid.NewGuid().ToString("N"));
            progress=new M4ProgressService(Path.Combine(directory,"profile.json"));
            ExplorerJourney.Stop();M4Session.UseProgressForTests(progress);
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Keys();if(world!=null&&world.Presentation!=null)world.Presentation.Ultimate.Cancel();
            ExplorerJourney.Stop();
            var previous=SceneManager.GetActiveScene();var empty=SceneManager.CreateScene("Explorer art cleanup");SceneManager.SetActiveScene(empty);
            if(previous.IsValid()&&previous.isLoaded)yield return SceneManager.UnloadSceneAsync(previous);
            var router=Object.FindAnyObjectByType<M4RegionRouter>();if(router!=null)Object.Destroy(router.gameObject);
            yield return null;M4Session.UseProgressForTests(null);
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;InputSystem.settings.backgroundBehavior=backgroundInput;
            Time.timeScale=scale;Time.fixedDeltaTime=fixedDelta;Time.captureDeltaTime=capture;Application.runInBackground=background;
            Cursor.lockState=cursor;Cursor.visible=cursorVisible;
            Assert.That(Path.GetFileName(directory),Does.StartWith("Orbis-Explorer-Art-"));
            Assert.That(Path.GetDirectoryName(directory),Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
        [UnityTest] public IEnumerator StellaUsesHerOwnHumanoidAndKeepsItThroughWalkingAndElementChanges()
        {yield return Check(ExplorerChoice.Stella,"stella","polaris");}
        [UnityTest] public IEnumerator PolarisUsesHisOwnHumanoidAndKeepsItThroughWalkingAndElementChanges()
        {yield return Check(ExplorerChoice.Polaris,"polaris","stella");}

        IEnumerator Check(ExplorerChoice choice,string sourceId,string otherId)
        {
            Assert.That(progress.TrySelectExplorer(choice),Is.True);
            yield return SceneManager.LoadSceneAsync(ExplorerJourney.IslandScene,LoadSceneMode.Single);
            // Appearance and pooled particles use unscaled time. Batch capture frames can run faster than real time.
            yield return new WaitForSecondsRealtime(1.25f);
            yield return Frames(100); // Settle the live Idle pose independently of real-time presentation.
            world=Object.FindAnyObjectByType<M4SceneBootstrap>();Assert.That(world,Is.Not.Null);
            var catalog=Resources.Load<ArtAssetCatalog>("Art/Catalog");
            var definition=catalog.Explorer(sourceId);var other=catalog.Explorer(otherId);
            Assert.That(definition,Is.Not.Null);Assert.That(other,Is.Not.Null);
            Assert.That(definition.Prefab,Is.Not.SameAs(other.Prefab));Assert.That(definition.Avatar,Is.Not.SameAs(other.Avatar));
            var art=world.GetComponent<ArtScenePresentation>().Characters;
            Assert.That(art.UsesExplorerModel,Is.True);Assert.That(art.PermanentSourceId,Is.EqualTo(sourceId));
            Assert.That(art.PrewarmedCount,Is.EqualTo(5));Assert.That(art.ActiveVisual,Is.SameAs(art.PermanentVisual));
            Assert.That(art.ActiveVisual.name,Is.EqualTo("Art "+definition.DisplayName));
            Assert.That(world.Party.PermanentMember.Actor.SourceId,Is.EqualTo(sourceId));
            Assert.That(world.Traversal.GetComponentsInChildren<Transform>(true).Any(x=>x.name=="Art "+other.DisplayName),Is.False,
                "The unselected protagonist must not be instantiated on the shared pawn.");
            Assert.That(world.Traversal.transform.Find("Visual").gameObject.activeSelf,Is.False);
            var visual=art.ActiveVisual;var animator=art.ActiveAnimator;
            Assert.That(animator.avatar,Is.SameAs(definition.Avatar));Assert.That(animator.isHuman&&animator.avatar.isValid,Is.True);
            Assert.That(animator.applyRootMotion,Is.False);
            Assert.That(animator.runtimeAnimatorController,Is.SameAs(catalog.Characters[0].Controller));
            Assert.That(animator.runtimeAnimatorController.animationClips.All(x=>x.isHumanMotion),Is.True);
            var skins=animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(x=>!x.name.StartsWith("Art Outline")&&!x.name.StartsWith("M3 Silhouette")).ToArray();
            Assert.That(skins,Is.Not.Empty);
            Assert.That(skins.Sum(x=>x.sharedMesh.vertexCount),Is.GreaterThan(100));
            Assert.That(skins.Max(x=>x.sharedMesh.bindposes.Length),Is.GreaterThan(10));
            var explorerToon=catalog.ExplorerToonMaterial!=null?catalog.ExplorerToonMaterial:catalog.DefaultToonMaterial;
            foreach(var material in skins.SelectMany(x=>x.sharedMaterials))
                Assert.That(material.shader,Is.SameAs(explorerToon.shader));
            var rightHand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            Assert.That(rightHand.GetComponentsInChildren<Transform>(true).Any(x=>x.name==definition.DisplayName+" Weapon"),Is.True);
            if(definition.Weapon.name=="Wayfarer's Blade")
            {
                var weapon=rightHand.GetComponentsInChildren<Transform>(true).Single(x=>x.name==definition.DisplayName+" Weapon");
                var source=weapon.GetComponentsInChildren<MeshRenderer>().Single(x=>!x.name.StartsWith("Art Outline"));
                Assert.That(source.sharedMaterials.Length,Is.EqualTo(1),"Decorative blade must not multiply character material slots.");
                Assert.That(source.sharedMaterial.shader,Is.SameAs(explorerToon.shader));
                Assert.That(source.sharedMaterial.GetFloat("_VertexColorStrength"),Is.EqualTo(1));
                var tip=weapon.GetComponentsInChildren<Transform>().Single(x=>x.name=="Weapon Trail Tip");
                Assert.That(Vector3.Distance(weapon.position,tip.position),Is.InRange(.85f,1.1f),"The existing M3 trail must follow the new metre-scale blade.");
            }
            foreach(var renderer in skins)
            {
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_Threshold"),Is.Zero,"The live appearance must finish before the portrait is captured.");
            }
            Texture faceTexture=CheckTexturedFace(animator,other.Prefab,null,true);
            CheckHairUvs(animator);
            Texture[] wardrobeTextures=CheckTailoredTextures(animator,null,true);
            CaptureExplorer(definition.DisplayName);
            CaptureFaceViews(definition.DisplayName,animator);
            CaptureAnatomyViews(definition.DisplayName,animator);
            animator.Play("Idle",0,0f);animator.Update(0f);
            LookDevSnapshots.CaptureIfRequested(definition.DisplayName,animator,world.Traversal.transform);
            Bounds body=MeasureBody(animator);
            Assert.That(body.size.y,Is.EqualTo(1.8f).Within(.02f));
            Assert.That(body.min.y-world.Traversal.transform.position.y,Is.EqualTo(0f).Within(.02f));
            var bones=new[]{HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg}
                .Select(animator.GetBoneTransform).ToArray();
            var rotations=bones.Select(x=>x.localRotation).ToArray();
            Vector3 position=world.Traversal.transform.position;
            Keys(Key.W);yield return Frames(15);
            Assert.That(Vector3.Distance(position,world.Traversal.transform.position),Is.GreaterThan(.35f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash,Is.EqualTo(Animator.StringToHash("Walk")));
            Assert.That(bones.Select((bone,index)=>Quaternion.Angle(rotations[index],bone.localRotation)).Max(),Is.GreaterThan(2f),
                "The shared Humanoid walking clip must actually move the newly mapped leg bones.");
            CaptureAnatomyPose(definition.DisplayName,"Walk",animator,35f);
            Keys();yield return Frames(3);
            CheckTexturedFace(animator,null,faceTexture,false);
            // Exercise the same ground-attack request and live motor clock used by gameplay; do not pose the rig manually.
            var motor=world.Traversal.GetComponent<PlayerMotor>();
            var combat=world.Traversal.GetComponent<BasicAttackCombo>();
            Assert.That(motor,Is.Not.Null);Assert.That(combat,Is.Not.Null);
            Assert.That(motor.IsGrounded,Is.True);
            combat.RequestAttack(motor.IsGrounded);
            Assert.That(combat.IsAttacking,Is.True);
            yield return Frames(12);
            Assert.That(combat.CurrentStep,Is.EqualTo(1));
            Assert.That(motor.State,Is.EqualTo(PlayerActionState.Attack));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash,Is.EqualTo(Animator.StringToHash("Attack1")));
            CaptureAnatomyPose(definition.DisplayName,"Attack",animator,35f);
            yield return Frames(30);
            Assert.That(combat.IsAttacking,Is.False);
            CheckTexturedFace(animator,null,faceTexture,false);
            CheckTailoredTextures(animator,wardrobeTextures,false);
            foreach(var element in new[]{ElementType.Fire,ElementType.Water,ElementType.Wind,ElementType.Rock,ElementType.Lightning})
            {
                if(ExplorerJourney.Current.SelectedElement!=element)Assert.That(ExplorerJourney.Current.TryChangeElement(element),Is.True);
                Assert.That(art.ActiveVisual,Is.SameAs(visual));Assert.That(art.ActiveAnimator,Is.SameAs(animator));
                Assert.That(world.Party.PermanentMember.Actor.SourceId,Is.EqualTo(sourceId));
                CheckTexturedFace(animator,null,faceTexture,false);
            }
            Assert.That(world.Party.TrySwitch(1),Is.True);Assert.That(world.Party.TrySwitch(0),Is.True);
            Assert.That(art.ActiveVisual,Is.SameAs(visual));Assert.That(art.ActiveAnimator,Is.SameAs(animator));
            yield return new WaitForSecondsRealtime(1.25f);
            yield return Frames(3); // ArtOutlineSync and the M3 appearance both get a frame after reactivation.
            CheckTexturedFace(animator,null,faceTexture,false);
            CheckTailoredTextures(animator,wardrobeTextures,false);
        }

        static SkinnedMeshRenderer[] SourceSkins(GameObject root)=>root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(renderer=>!renderer.name.StartsWith("Art Outline")&&!renderer.name.StartsWith("M3 Silhouette")).ToArray();

        static Texture CheckTexturedFace(Animator animator,GameObject otherPrefab,Texture expected,bool validateUvs)
        {
            var faces=SourceSkins(animator.gameObject).SelectMany(renderer=>renderer.sharedMaterials
                .Select((material,index)=>(Renderer:renderer,Material:material,Index:index)))
                .Where(slot=>slot.Material.name.EndsWith("EX_Face",StringComparison.Ordinal)).ToArray();
            Assert.That(faces.Length,Is.EqualTo(1),"The live face must use its dedicated mapped surface, not separate floating eye and lip pieces.");
            var face=faces[0];
            if(Resources.Load<VolumeProfile>("LookDev/ExplorerGrade")!=null)
            {
                Assert.That(face.Renderer.GetComponent<ExplorerFaceLighting>(),Is.Not.Null,"Reimport must preserve the animated facial lighting basis.");
                Assert.That(face.Material.GetFloat("_FaceLighting"),Is.EqualTo(1),"Persist facial lighting before prefab/scene imports reload material dependencies.");
            }
            Texture texture=face.Material.GetTexture("_BaseMap");
            Assert.That(texture,Is.Not.Null,"Face artwork must survive conversion to the gameplay toon material.");
            Assert.That(texture.width,Is.GreaterThanOrEqualTo(1024));Assert.That(texture.height,Is.GreaterThanOrEqualTo(1024));
            Assert.That(texture.wrapMode,Is.EqualTo(TextureWrapMode.Clamp),"The hidden scalp UV padding must not wrap facial features onto the forehead.");
            if(expected!=null)Assert.That(texture,Is.SameAs(expected),"Walking, changing elements and switching party members must retain the chosen face map.");
            if(otherPrefab!=null)
            {
                var otherFace=SourceSkins(otherPrefab).SelectMany(renderer=>renderer.sharedMaterials)
                    .Single(material=>material.name.EndsWith("EX_Face",StringComparison.Ordinal));
                Assert.That(otherFace.GetTexture("_BaseMap"),Is.Not.Null);
                Assert.That(texture,Is.Not.SameAs(otherFace.GetTexture("_BaseMap")),"Stella and Polaris have distinct facial artwork.");
            }
            if(validateUvs)
            {
                Mesh mesh=face.Renderer.sharedMesh;var uv=mesh.uv;
                Assert.That(uv.Length,Is.EqualTo(mesh.vertexCount),"The imported skinned head must retain a UV for every vertex.");
                var faceUvs=mesh.GetIndices(face.Index).Select(index=>uv[index]).ToArray();
                Assert.That(faceUvs,Is.Not.Empty);
                Assert.That(faceUvs.All(Finite),Is.True);
                // The upper two scalp rings are under the hair and deliberately use clamped v up to 1.14.
                Assert.That(faceUvs.All(point=>point.x>=-.05f&&point.x<=1.05f&&point.y>=-.05f&&point.y<=1.20f),Is.True);
                Assert.That(faceUvs.Max(point=>point.x)-faceUvs.Min(point=>point.x),Is.GreaterThan(.5f));
                Assert.That(faceUvs.Max(point=>point.y)-faceUvs.Min(point=>point.y),Is.GreaterThan(.5f));
            }
            // Outline GameObject names contain "/", which Transform.Find interprets as a hierarchy path.
            // Match the actual direct child component instead, including inactive copies during a switch.
            var outline=face.Renderer.GetComponentsInChildren<ArtOutlineSync>(true)
                .SingleOrDefault(sync=>sync.transform.parent==face.Renderer.transform);
            Assert.That(outline,Is.Not.Null,"The new head must still participate in the existing skinned outline system.");
            var renderer=outline.GetComponent<Renderer>();var block=new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block,face.Index);
            float width=block.GetFloat("_OutlinePixels");
            Assert.That(width,Is.GreaterThan(0f).And.LessThan(1f),"The face must retain a fine outline after M3 updates the material property block.");
            Assert.That(block.GetFloat("_Threshold"),Is.Zero,"Reactivated facial outlines must finish their appearance transition.");
            return texture;
        }

        static void CheckHairUvs(Animator animator)
        {
            var hair=SourceSkins(animator.gameObject).Where(renderer=>renderer.sharedMaterials.Any(material=>
                material.name.EndsWith("EX_Hair",StringComparison.Ordinal)||material.name.EndsWith("EX_HairLight",StringComparison.Ordinal))).ToArray();
            Assert.That(hair,Is.Not.Empty);
            foreach(var renderer in hair)
            {
                var uv=renderer.sharedMesh.uv;
                Assert.That(uv.Length,Is.EqualTo(renderer.sharedMesh.vertexCount),"Hair strand shading requires imported UVs on every skinned vertex.");
                Assert.That(uv.All(Finite),Is.True);
                Assert.That(uv.Max(point=>point.x)-uv.Min(point=>point.x),Is.GreaterThan(.1f));
                foreach(var material in renderer.sharedMaterials.Where(material=>material.name.EndsWith("EX_Hair",StringComparison.Ordinal)||material.name.EndsWith("EX_HairLight",StringComparison.Ordinal)))
                    Assert.That(material.GetTexture("_BaseMap"),Is.Not.Null);
            }
        }
        static Texture[] CheckTailoredTextures(Animator animator,Texture[] expected,bool validateUvs)
        {
            var roles=new[]{"EX_TailoredIvory","EX_TailoredNavy"};
            var result=new Texture[roles.Length];
            var slots=SourceSkins(animator.gameObject).SelectMany(renderer=>renderer.sharedMaterials
                .Select((material,index)=>(Renderer:renderer,Material:material,Index:index))).ToArray();
            for(int role=0;role<roles.Length;role++)
            {
                var surfaces=slots.Where(slot=>slot.Material.name.EndsWith(roles[role],StringComparison.Ordinal)).ToArray();
                Assert.That(surfaces,Is.Not.Empty,"The live wardrobe is missing its mapped "+roles[role]+" surface.");
                foreach(var surface in surfaces)
                {
                    Texture texture=surface.Material.GetTexture("_BaseMap");
                    Assert.That(texture,Is.Not.Null,"Clothing texture must survive the shared toon material conversion.");
                    Assert.That(texture.width,Is.GreaterThanOrEqualTo(1024));Assert.That(texture.height,Is.GreaterThanOrEqualTo(1024));
                    if(result[role]==null)result[role]=texture;
                    else Assert.That(texture,Is.SameAs(result[role]),"Pieces with the same cloth role must use the same authored fabric map.");
                    if(expected!=null)Assert.That(texture,Is.SameAs(expected[role]),"Attacking and party reactivation must preserve the wardrobe texture binding.");
                    if(!validateUvs)continue;
                    Mesh mesh=surface.Renderer.sharedMesh;var uv=mesh.uv;
                    Assert.That(uv.Length,Is.EqualTo(mesh.vertexCount),"The skinned wardrobe must retain its imported UV coordinates.");
                    var mapped=mesh.GetIndices(surface.Index).Select(index=>uv[index]).ToArray();
                    Assert.That(mapped,Is.Not.Empty);Assert.That(mapped.All(Finite),Is.True);
                    // Tiled fabric UVs may exceed 0..1, but may not collapse the painted cloth into one texel or a line.
                    Assert.That(mapped.Max(point=>point.x)-mapped.Min(point=>point.x),Is.GreaterThan(.0001f));
                    Assert.That(mapped.Max(point=>point.y)-mapped.Min(point=>point.y),Is.GreaterThan(.0001f));
                }
            }
            return result;
        }
        static bool Finite(Vector2 value)=>!float.IsNaN(value.x)&&!float.IsNaN(value.y)&&!float.IsInfinity(value.x)&&!float.IsInfinity(value.y);

        void CaptureExplorer(string name)
        {
            Vector3 feet=world.Traversal.transform.position;
            CaptureView("Island_"+name+"_Unity",feet+new Vector3(2f,1.4f,3.5f),feet+Vector3.up,38f,1200,1400);
        }

        void CaptureFaceViews(string name,Animator animator)
        {
            var head=animator.GetBoneTransform(HumanBodyBones.Head);
            var left=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var right=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Assert.That(head,Is.Not.Null);
            Assert.That(left,Is.Not.Null);Assert.That(right,Is.Not.Null);
            // Derive the live facing from Humanoid bones rather than FBX local axes or the pawn's initial yaw.
            Vector3 horizontalRight=Vector3.ProjectOnPlane(right.position-left.position,Vector3.up).normalized;
            Assert.That(horizontalRight.sqrMagnitude,Is.GreaterThan(.99f));
            Vector3 forward=Vector3.Cross(horizontalRight,Vector3.up).normalized;
            // Default portrait framing: 0.50 m around the face includes the crown and collar on both 1.8 m avatars.
            Vector3 center=head.position+Vector3.up*.095f+forward*.02f;
            const float fieldOfView=28f;
            float distance=.50f/(2f*Mathf.Tan(fieldOfView*.5f*Mathf.Deg2Rad));
            foreach(var view in new[]{("Front",0f),("ThreeQuarter",35f),("Profile",90f)})
            {
                Vector3 direction=Quaternion.AngleAxis(view.Item2,Vector3.up)*forward;
                CaptureView("Face_"+name+"_"+view.Item1,center+direction*distance,center,fieldOfView,1024,1024);
            }
        }

        void CaptureAnatomyViews(string name,Animator animator)
        {
            foreach(var view in new[]{("Front",0f),("ThreeQuarter",35f),("Profile",90f),("Back",180f)})
                CaptureAnatomyPose(name,view.Item1,animator,view.Item2);
        }

        void CaptureAnatomyPose(string name,string pose,Animator animator,float yaw)
        {
            var left=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var right=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Assert.That(left,Is.Not.Null);Assert.That(right,Is.Not.Null);
            Vector3 horizontalRight=Vector3.ProjectOnPlane(right.position-left.position,Vector3.up).normalized;
            Assert.That(horizontalRight.sqrMagnitude,Is.GreaterThan(.99f));
            Vector3 forward=Vector3.Cross(horizontalRight,Vector3.up).normalized;
            // Full-body defaults: 2.30 m vertical view at 24 degrees reduces perspective distortion without an artificial studio camera.
            Vector3 center=world.Traversal.transform.position+Vector3.up*.92f;
            const float fieldOfView=24f;
            float distance=2.30f/(2f*Mathf.Tan(fieldOfView*.5f*Mathf.Deg2Rad));
            Vector3 direction=Quaternion.AngleAxis(yaw,Vector3.up)*forward;
            CaptureView("Anatomy_"+name+"_"+pose,center+direction*distance,center,fieldOfView,1200,1600);
        }
        internal static void CaptureView(string filename,Vector3 position,Vector3 lookAt,float fieldOfView,int width,int height,
            Action<Camera,int> beforeCapture=null,Action afterCapture=null)
        {
            if (SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) return;
            Camera source=Camera.main;
            Assert.That(source,Is.Not.Null);
            GameObject captureObject=null;
            RenderTexture target=null,resolved=null;
            Texture2D pixels=null;
            RenderTexture previous=RenderTexture.active;
#if UNITY_EDITOR
            bool asynchronous=UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation=false;
#endif
            try
            {
                // Copy only Camera settings: no Cinemachine Brain, AudioListener, MainCamera tag or gameplay mutation.
                captureObject=new GameObject("Explorer portrait capture");
                var camera=captureObject.AddComponent<Camera>();
                camera.CopyFrom(source);camera.enabled=false;camera.targetTexture=null;
                var originalData=source.GetUniversalAdditionalCameraData();
                var data=camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing=originalData.renderPostProcessing;
                data.antialiasing=originalData.antialiasing;data.antialiasingQuality=originalData.antialiasingQuality;
                data.volumeLayerMask=originalData.volumeLayerMask;
                data.volumeTrigger=originalData.volumeTrigger;
                camera.transform.position=position;
                camera.transform.LookAt(lookAt);
                camera.fieldOfView=fieldOfView;camera.nearClipPlane=.02f;
                // Match the game camera's active URP MSAA instead of silently forcing the capture target to 1x.
                var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var descriptor=new RenderTextureDescriptor(width,height,RenderTextureFormat.ARGB32,24)
                    {
                        msaaSamples=source.allowMSAA&&pipeline!=null?pipeline.msaaSampleCount:1,
                        // Preserve RenderTextureReadWrite.Default's display conversion in a Linear project.
                        // The same descriptor is used by the explicit resolve target, avoiding dark linear PNG bytes.
                        sRGB=QualitySettings.activeColorSpace==ColorSpace.Linear
                    };
                descriptor.msaaSamples=Mathf.Max(1,SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
                target=new RenderTexture(descriptor);
                target.Create();camera.aspect=(float)width/height;
                // Let the request own its destination binding. Prebinding it would make URP restore the
                // same MSAA surface as the active render target immediately before our explicit resolve.
                beforeCapture?.Invoke(camera,target.antiAliasing);
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
                RenderTexture.active=previous;
                if(target.antiAliasing>1)
                {
                    descriptor.msaaSamples=1;descriptor.depthBufferBits=0;
                    resolved=new RenderTexture(descriptor);resolved.Create();
                    target.ResolveAntiAliasedSurface(resolved);
                }
                RenderTexture.active=resolved!=null?resolved:target;
                Debug.Log("ORBIS_FACE_CAPTURE "+filename+" MSAA="+target.antiAliasing+" CameraPostAA="+data.antialiasing+" sRGB="+target.sRGB+" Format="+target.graphicsFormat);
                pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                afterCapture?.Invoke();
                Directory.CreateDirectory("TestResults");
                File.WriteAllBytes("TestResults/"+filename+".png",pixels.EncodeToPNG());
                camera.targetTexture=null;
            }
            finally
            {
                RenderTexture.active=previous;
                if(pixels!=null)Object.Destroy(pixels);
                if(captureObject!=null){captureObject.GetComponent<Camera>().targetTexture=null;Object.Destroy(captureObject);}
                if(target!=null){target.Release();Object.Destroy(target);}
                if(resolved!=null){resolved.Release();Object.Destroy(resolved);}
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation=asynchronous;
#endif
            }
        }
        static Bounds MeasureBody(Animator animator)
        {
            bool found=false;Bounds result=default;var baked=new Mesh();
            Transform tip=animator.GetComponentsInChildren<Transform>(true).FirstOrDefault(x=>x.name=="Weapon Trail Tip");
            Transform weapon=tip!=null?tip.parent:null;
            try
            {
                foreach(var renderer in animator.GetComponentsInChildren<Renderer>())
                {
                    if(!renderer.enabled||renderer.name.StartsWith("Art Outline")||renderer.name.StartsWith("M3 Silhouette")||
                        weapon!=null&&renderer.transform.IsChildOf(weapon))continue;
                    Mesh mesh;
                    if(renderer is SkinnedMeshRenderer skin){baked.Clear();skin.BakeMesh(baked,true);mesh=baked;}
                    else if(renderer.TryGetComponent<MeshFilter>(out var filter))mesh=filter.sharedMesh;
                    else continue;
                    if(mesh==null)continue;
                    foreach(var vertex in mesh.vertices)
                    {Vector3 point=renderer.transform.TransformPoint(vertex);if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);}
                }
            }
            finally{Object.Destroy(baked);}
            Assert.That(found,Is.True);return result;
        }
        void Keys(params Key[] keys){if(keyboard!=null&&keyboard.added)InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
        static IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
    }
}
