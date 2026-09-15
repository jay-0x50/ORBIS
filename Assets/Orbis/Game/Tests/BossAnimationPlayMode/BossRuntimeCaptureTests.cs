#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Art;
using Orbis.Game.Animation;
using Orbis.M0;
using Orbis.M1;
using Orbis.M4;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class BossRuntimeCaptureTests
    {
        GameObject host; float timeScale,captureDelta; bool background,initialized,asyncCompile;
        [UnityTest]
        public IEnumerator OriginalGenericRigTracksExistingM4Encounter()
        {
            string folder=Arg("-bossRuntimeFolder"),run=Arg("-bossRuntimeCaptureRun");
            if(folder==null)Assert.Ignore("Opt-in original Generic M4 playback. Supply -bossRuntimeFolder and -bossRuntimeCaptureRun.");
            Assert.That(folder.StartsWith("Assets/Orbis/Game/Characters/Bosses/") && !folder.Split('/').Contains(".."),Is.True);
            Assert.That(!string.IsNullOrEmpty(run)&&run.All(c=>char.IsLetterOrDigit(c)||c=='_'),Is.True);
            string name=Path.GetFileName(folder);
            string output="TestResults/CharacterPipeline/BossRuntimePlayback/"+run+"/"+name;
            Assert.That(Directory.Exists(output),Is.False);
            var profile=AssetDatabase.LoadAssetAtPath<BossMotionProfile>(folder+"/MotionProfile.asset");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/"+name+".prefab");
            Assert.That(profile,Is.Not.Null); Assert.That(prefab,Is.Not.Null);profile.Validate();
            ElementType element=name=="FireBoss"?ElementType.Fire:name=="WaterBoss"?ElementType.Water:
                name=="WindBoss"?ElementType.Wind:name=="RockBoss"?ElementType.Rock:ElementType.Lightning;
            timeScale=Time.timeScale;captureDelta=Time.captureDeltaTime;background=Application.runInBackground;
            asyncCompile=ShaderUtil.allowAsyncCompilation;initialized=true;
            Time.timeScale=1;Time.captureDeltaTime=1f/30;Application.runInBackground=true;ShaderUtil.allowAsyncCompilation=false;
            host=new GameObject("Isolated M4 visual playback "+name);host.transform.position=new Vector3(5000,100,5000);
            var reactions=Child("Reactions").AddComponent<ElementalReactionManager>();reactions.AutoTick=false;
            var player=Child("Player");player.transform.localPosition=Vector3.back*12;
            var input=player.AddComponent<M0Input>();var motor=player.AddComponent<PlayerMotor>();
            var combo=player.AddComponent<BasicAttackCombo>();combo.Configure(null);motor.Configure(input,player.transform,null,combo);
            input.enabled=false;motor.enabled=false;combo.enabled=false;
            var members=new PartyMember[4];
            for(int i=0;i<4;i++)
            {
                var obj=new GameObject("Review member "+i);obj.transform.SetParent(player.transform,false);
                var actor=obj.AddComponent<ElementalActor>();actor.Configure("BossReview."+i,ElementType.Fire,ActorTeam.Player);
                members[i]=new PartyMember("Review member "+i,actor);
            }
            var party=player.AddComponent<PartyManager>();party.Configure(input,motor,combo,reactions,members);
            var bossObject=Child(name+" unchanged gameplay anchor");bossObject.layer=9;
            var collider=bossObject.AddComponent<CapsuleCollider>();collider.center=Vector3.up;collider.height=2;collider.radius=.7f;
            var boss=bossObject.AddComponent<M4FieldBoss>();boss.AutoTick=false;
            var region=element==ElementType.Fire?M4RegionId.Agnia:element==ElementType.Water?M4RegionId.Teluna:
                element==ElementType.Wind?M4RegionId.Zephyr:element==ElementType.Rock?M4RegionId.Granite:M4RegionId.Voltheim;
            boss.Configure(region,element,M4BossProfile.DefaultWeakness(element),reactions,party,player.transform,true);
            var view=Object.Instantiate(prefab,bossObject.transform);view.name=name+" original Generic visual";
            var animator=view.GetComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var skin=view.GetComponentsInChildren<SkinnedMeshRenderer>().Single();skin.updateWhenOffscreen=true;
            var mesh=new Mesh();skin.BakeMesh(mesh,true);
            var restPoints=mesh.vertices.Select(v=>skin.transform.TransformPoint(v)).ToArray();Object.Destroy(mesh);
            var bounds=new Bounds(restPoints[0],Vector3.zero);foreach(var p in restPoints)bounds.Encapsulate(p);
            view.transform.localScale*=1.8f/bounds.size.y;
            ArtStyle.ApplyToHierarchy(view,Resources.Load<ArtAssetCatalog>("Art/Catalog"),true);
            foreach(var renderer in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))renderer.forceMatrixRecalculationPerRender=true;
            var presenter=view.AddComponent<BossAnimationPresenter>();presenter.Configure(boss,animator,profile);
            var key=Child("Capture key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.1f;
            key.color=Color.white;key.shadows=LightShadows.Soft;key.transform.rotation=Quaternion.Euler(42,-35,0);
            var camera=Child("Capture camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.14f,.16f,.20f);camera.nearClipPlane=.03f;camera.farClipPlane=60;
            camera.fieldOfView=30;camera.allowHDR=true;camera.allowMSAA=true;camera.useOcclusionCulling=false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            // Deliberately isolated studio capture. Camera remains fixed and does not hide root drift.
            Vector3 focus=bossObject.transform.position+Vector3.up*.35f;
            Vector3 direction=new Vector3(.65f,.27f,1).normalized;
            camera.transform.SetPositionAndRotation(focus+direction*10,Quaternion.LookRotation(-direction));
            var probe=host.AddComponent<BossRuntimeProbe>();
            probe.Configure(output,boss,animator,profile,presenter,party,reactions,player.transform,camera);
            var stimulus=host.AddComponent<BossRuntimeStimulus>();stimulus.Probe=probe;
            boss.AutoTick=true;Physics.SyncTransforms();
            float deadline=Time.realtimeSinceStartup+180;
            while(!probe.Done && probe.Failure==null && Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(probe.Failure,Is.Null);Assert.That(probe.Done,Is.True,"Continuous playback capture timed out.");
            Assert.That(probe.Attacks,Is.EqualTo(1),"Visual animation must not generate extra damage events.");
            Assert.That(probe.Defeats,Is.EqualTo(1),"Saved defeat restoration must not emit a new death.");
            Assert.That(probe.MaximumRootDrift,Is.LessThan(.0001f));
            Assert.That(probe.MaximumHurtWeight,Is.GreaterThan(.1f));
            Assert.That(probe.ExposedSeen && probe.RecoveryAfterExposureSeen && probe.LeashResetSeen && probe.SavedDeathHeld,Is.True);
            Assert.That(probe.HitstopParameterDrift,Is.LessThan(.0001f));
            Assert.That(probe.MaximumBoneMotion,Is.GreaterThan(.01f),"Actual Generic skin bones must move.");
        }
        GameObject Child(string name){var o=new GameObject(name);o.transform.SetParent(host.transform,false);return o;}
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(host!=null)Object.Destroy(host);yield return null;
            if(initialized){Time.timeScale=timeScale;Time.captureDeltaTime=captureDelta;Application.runInBackground=background;ShaderUtil.allowAsyncCompilation=asyncCompile;}
        }
        static string Arg(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
    }

    [DefaultExecutionOrder(-60)]
    public sealed class BossRuntimeStimulus:MonoBehaviour
    {
        public BossRuntimeProbe Probe;
        void Update(){if(Probe!=null && !Probe.Done && Probe.Failure==null)Probe.Stimulate();}
    }

    [DefaultExecutionOrder(32000)]
    public sealed class BossRuntimeProbe:MonoBehaviour
    {
        int totalFrames,firstContact,hurtFrame,freezeFrame,exposeFrame,leashFrame,approachFrame,deathFrame,savedFrame,respawnFrame;
        [Serializable] public sealed class Sample
        {
            public int index,unityFrame,attacks,defeats;public string state;
            public float timeScale,health,remaining,telegraph,attackTime,deadTime,hurtWeight,rootDrift;
            public int animatorState;public Vector3[] bones;
        }
        [Serializable] public sealed class Report
        {
            public string source="Actual Unity PlayMode M4FieldBoss.AutoTick + BossAnimationPresenter + Animator evaluation, fixed-camera URP JPEG frames. No Animator.Update/normalized-time sampling in the recorder.";
            public string graphics,profile;public int frameRate=30,framesExpected;
            public string phases;
            public List<Sample> frames=new List<Sample>();
        }
        public int Frame {get;private set;} public bool Done {get;private set;} public string Failure {get;private set;}
        public int Attacks {get;private set;} public int Defeats {get;private set;}
        public float MaximumRootDrift {get;private set;} public float MaximumHurtWeight {get;private set;}
        public float HitstopParameterDrift {get;private set;} public float MaximumBoneMotion {get;private set;}
        public bool ExposedSeen,RecoveryAfterExposureSeen,LeashResetSeen,SavedDeathHeld;
        M4FieldBoss boss;Animator animator;BossMotionProfile profile;BossAnimationPresenter presenter;
        PartyManager party;ElementalReactionManager reactions;Transform player;Camera camera;string output;
        Vector3 anchor;Transform[] bones;Vector3[] restBonePositions;float frozenAttack;
        RenderTexture target,resolved;Texture2D pixels;Report report;
        public void Configure(string path,M4FieldBoss owner,Animator rig,BossMotionProfile motions,BossAnimationPresenter visual,
            PartyManager members,ElementalReactionManager manager,Transform pawn,Camera view)
        {
            output=path;Directory.CreateDirectory(output);boss=owner;animator=rig;profile=motions;presenter=visual;
            party=members;reactions=manager;player=pawn;camera=view;anchor=boss.transform.position;
            bones=rig.GetComponentsInChildren<SkinnedMeshRenderer>().First().bones;restBonePositions=bones.Select(b=>b.position).ToArray();
            // Schedule from each existing element's encounter timings; never tune gameplay durations to fit a test.
            firstContact=30+Mathf.CeilToInt(boss.Profile.TelegraphDuration*30)+1;
            hurtFrame=firstContact+5;freezeFrame=firstContact+10;
            exposeFrame=firstContact+Mathf.CeilToInt(boss.Profile.RecoveryDuration*30)+12+Mathf.CeilToInt(boss.Profile.TelegraphDuration*15);
            leashFrame=exposeFrame+Mathf.CeilToInt(boss.Profile.ExposureDuration*30)+Mathf.CeilToInt(boss.Profile.RecoveryDuration*15);
            approachFrame=leashFrame+5;deathFrame=approachFrame+Mathf.CeilToInt(boss.Profile.TelegraphDuration*15);
            savedFrame=deathFrame+Mathf.CeilToInt(profile.Dead.length*30)+10;respawnFrame=savedFrame+10;totalFrames=respawnFrame+30;
            report=new Report {graphics=SystemInfo.graphicsDeviceName,profile=AssetDatabase.GetAssetPath(profile),framesExpected=totalFrames,
                phases="0..29 idle;30 approach;"+hurtFrame+" environmental hurt;"+freezeFrame+".."+(freezeFrame+9)+" hitstop;"+
                    exposeFrame+" three weakness hits;"+leashFrame+" leash reset;"+approachFrame+" approach;"+deathFrame+
                    " lethal environmental damage;"+savedFrame+" saved defeat restore;"+respawnFrame+" respawn outside leash."};
            target=new RenderTexture(768,576,24,RenderTextureFormat.ARGB32){antiAliasing=2};target.Create();
            resolved=new RenderTexture(768,576,0,RenderTextureFormat.ARGB32);resolved.Create();pixels=new Texture2D(768,576,TextureFormat.RGB24,false);
            boss.AttackExecuted+=OnAttack;boss.Defeated+=OnDefeat;
        }
        void OnAttack()=>Attacks++;void OnDefeat(M4RegionId region)=>Defeats++;
        public void Stimulate()
        {
            try
            {
                if(Frame==30 || Frame==approachFrame)player.position=anchor+Vector3.back*2;
                if(Frame==hurtFrame)reactions.ApplyEnvironmentalDamage(boss.Actor,1f);
                if(Frame==freezeFrame)Time.timeScale=0;
                if(Frame==freezeFrame+10)Time.timeScale=1;
                if(Frame==exposeFrame)for(int i=0;i<3;i++)reactions.Apply(boss.Actor,boss.Weakness,party.ActiveMember.Actor,.1f);
                if(Frame==leashFrame)player.position=anchor+Vector3.back*12;
                if(Frame==deathFrame)reactions.ApplyEnvironmentalDamage(boss.Actor,10000f);
                if(Frame==savedFrame){boss.ResetEncounter(true);presenter.ResetPresentation();}
                if(Frame==respawnFrame){player.position=anchor+Vector3.back*12;boss.ResetEncounter(false);presenter.ResetPresentation();}
            }
            catch(Exception ex){Failure=ex.ToString();}
        }
        void LateUpdate()
        {
            if(boss==null||Done||Failure!=null)return;
            try
            {
                float drift=Vector3.Distance(anchor,boss.transform.position);MaximumRootDrift=Mathf.Max(MaximumRootDrift,drift);
                float hurt=animator.GetLayerWeight(1);MaximumHurtWeight=Mathf.Max(MaximumHurtWeight,hurt);
                float attackTime=animator.GetFloat("AttackTime");
                if(Frame==freezeFrame)frozenAttack=attackTime;
                if(Frame>freezeFrame&&Frame<freezeFrame+10)HitstopParameterDrift=Mathf.Max(HitstopParameterDrift,Mathf.Abs(attackTime-frozenAttack));
                if(Frame>=exposeFrame&&Frame<exposeFrame+100)ExposedSeen|=boss.State==M4BossState.Exposed;
                if(Frame>exposeFrame+130&&Frame<leashFrame)RecoveryAfterExposureSeen|=boss.State==M4BossState.Recover && animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle");
                if(Frame==leashFrame+1)LeashResetSeen=boss.State==M4BossState.Dormant&&Mathf.Approximately(boss.HitPoints,boss.MaximumHitPoints);
                if(Frame==savedFrame+1)SavedDeathHeld=boss.State==M4BossState.Defeated&&animator.GetFloat("DeadTime")>=.999f;
                var positions=bones.Select(b=>b.position-anchor).ToArray();
                for(int i=0;i<bones.Length;i++)MaximumBoneMotion=Mathf.Max(MaximumBoneMotion,Vector3.Distance(bones[i].position,restBonePositions[i]));
                report.frames.Add(new Sample {index=Frame,unityFrame=Time.frameCount,attacks=Attacks,defeats=Defeats,state=boss.State.ToString(),
                    timeScale=Time.timeScale,health=boss.HitPoints,remaining=boss.StateRemaining,telegraph=boss.TelegraphProgress,
                    attackTime=attackTime,deadTime=animator.GetFloat("DeadTime"),hurtWeight=hurt,rootDrift=drift,bones=positions,
                    animatorState=animator.GetCurrentAnimatorStateInfo(0).fullPathHash});
                Capture();
                Frame++;
                if(Frame>=totalFrames){Done=true;File.WriteAllText(output+"/ActualRuntimePlayback.json",JsonUtility.ToJson(report,true));}
            }
            catch(Exception ex){Failure=ex.ToString();File.WriteAllText(output+"/Failure.txt",Failure);}
        }
        void Capture()
        {
            var previous=RenderTexture.active;VolumeStack stack=null;
            try
            {
                if(!VolumeManager.instance.isInitialized)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);camera.UpdateVolumeStack();
                stack=VolumeManager.instance.stack;VolumeManager.instance.stack=camera.GetUniversalAdditionalCameraData().volumeStack;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                target.ResolveAntiAliasedSurface(resolved);RenderTexture.active=resolved;pixels.ReadPixels(new Rect(0,0,768,576),0,0);pixels.Apply();
                File.WriteAllBytes(output+"/"+Frame.ToString("D4")+".jpg",pixels.EncodeToJPG(90));
            }
            finally{RenderTexture.active=previous;if(stack!=null)VolumeManager.instance.stack=stack;}
        }
        void OnDestroy()
        {
            if(boss!=null){boss.AttackExecuted-=OnAttack;boss.Defeated-=OnDefeat;}
            if(target!=null){target.Release();Object.Destroy(target);}if(resolved!=null){resolved.Release();Object.Destroy(resolved);}if(pixels!=null)Object.Destroy(pixels);
        }
    }
}
#endif
