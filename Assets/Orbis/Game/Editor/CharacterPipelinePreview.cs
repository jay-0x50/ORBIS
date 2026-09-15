using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Actual URP stills of a reviewed candidate. All posing/material overrides are transient.</summary>
    public static class CharacterPipelinePreview
    {
        [Serializable] sealed class Evidence
        {
            public string model, modelSha256, graphics, shader, note;
            public Vector3 camera, target;
            public float fieldOfView, normalStrength;
            public bool outlineEnabled, keyShadows;
        }
        public static void ImportCaptureAndAudit()
        {
            ImportAndCapture();
            AvatarScaleAudit.RunRequested();
        }
        public static void ImportAndCapture()
        {
            CharacterPipelineImport.ImportRequested();
            string[] args=Environment.GetCommandLineArgs();
            string name=args[Array.IndexOf(args,"-characterPipelineName")+1];
            string run="AtlasNormal01";
            int index=Array.IndexOf(args,"-characterPreviewRun");
            if(index>=0) run=args[index+1];
            if(!run.All(c=>char.IsLetterOrDigit(c)||c=='_')) throw new ArgumentException("Invalid preview run label");
            Capture(name,run,CharacterPipelineImport.RequestedRoot());
        }
        public static void CaptureRequested()
        {
            string[] args=Environment.GetCommandLineArgs();
            int character=Array.IndexOf(args,"-characterPipelineName"), run=Array.IndexOf(args,"-characterPreviewRun");
            if(character<0 || character+1>=args.Length || run<0 || run+1>=args.Length)
                throw new ArgumentException("Supply character name and fresh preview run label.");
            if(!args[run+1].All(c=>char.IsLetterOrDigit(c)||c=='_')) throw new ArgumentException("Invalid preview run label");
            Capture(args[character+1],args[run+1],CharacterPipelineImport.RequestedRoot());
        }
        public static void Capture(string name,string run,string candidateRoot=CharacterPipelineImport.Root)
        {
            bool diagnostics=Environment.GetCommandLineArgs().Contains("-characterPreviewDiagnostic");
            string folder=candidateRoot+"/"+name;
            string output="TestResults/CharacterPipeline/UnityPreview/"+name+"/"+run;
            if(Directory.Exists(output)) throw new InvalidOperationException("Preserve earlier image evidence; use a fresh run label.");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Transient reviewed model");
            var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/"+name+".prefab"),root.transform);
            Animator animator=model.GetComponent<Animator>();
            animator.Rebind(); animator.Play("Idle",0,0f); animator.Update(0f);
            ArtCharacterRoster.NormalizeVisibleModelHeight(root,1.8f,Vector3.zero);
            var materials=new List<Material>();
            foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.sharedMaterials=skin.sharedMaterials.Select(m=>
                {
                    var copy=new Material(m) { name="Preview "+m.name };
                    materials.Add(copy); return copy;
                }).ToArray();
            }
            var catalog=Resources.Load<ArtAssetCatalog>("Art/Catalog");
            ArtStyle.ApplyToHierarchy(root,catalog,true);
            var outlines=root.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("Art Outline",StringComparison.Ordinal)).ToArray();
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.28f,.30f,.34f);
            RenderSettings.fog=false;
            var light=new GameObject("Preview key").AddComponent<Light>();
            light.type=LightType.Directional; light.intensity=1.1f; light.color=Color.white;
            light.shadows=LightShadows.Soft; light.transform.rotation=Quaternion.Euler(42,-35,0);
            RenderSettings.sun=light;
            var camera=new GameObject("Preview camera").AddComponent<Camera>();
            camera.enabled=false; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.14f,.16f,.20f); camera.nearClipPlane=.03f; camera.farClipPlane=30;
            camera.allowHDR=true; camera.allowMSAA=true; camera.useOcclusionCulling=false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            string modelHash=Hash(folder+"/"+name+".fbx");
            var views=new List<(string,Vector3,Vector3,float)>
            {
                ("Body",new Vector3(3.2f,2.1f,4.5f),new Vector3(0,.9f,0),28f),
                ("Face",new Vector3(0,1.64f,2.1f),new Vector3(0,1.61f,0),16f),
                ("Torso",new Vector3(1.2f,1.3f,2.8f),new Vector3(0,1.15f,0),23f)
            };
            if(diagnostics) views.Add(("Legs",new Vector3(0,.55f,2.1f),new Vector3(0,.55f,0),18f));
            var settings=diagnostics ? new[]{(1f,true,true),(1f,false,true),(1f,true,false),(1f,false,false)} :
                new[]{(0f,true,true),(1f,true,true)};
            try
            {
                foreach(var view in views)
                foreach(var setting in settings)
                {
                    float strength=setting.Item1;
                    foreach(var material in materials) material.SetFloat("_NormalStrength",strength);
                    foreach(var outline in outlines) outline.enabled=setting.Item2;
                    light.shadows=setting.Item3 ? LightShadows.Soft : LightShadows.None;
                    camera.transform.SetPositionAndRotation(view.Item2,Quaternion.LookRotation(view.Item3-view.Item2));
                    camera.fieldOfView=view.Item4;
                    string path=output+"/"+view.Item1+(strength>0?"_NormalOn":"_NormalOff");
                    if(diagnostics) path+=(setting.Item2 ? "_OutlineOn" : "_OutlineOff")+(setting.Item3 ? "_ShadowOn" : "_ShadowOff");
                    Render(camera,path+".png");
                    File.WriteAllText(path+".json",JsonUtility.ToJson(new Evidence
                    {
                        model=folder+"/"+name+".fbx", modelSha256=modelHash, graphics=SystemInfo.graphicsDeviceName,
                        shader=AssetDatabase.GetAssetPath(materials[0].shader), camera=view.Item2, target=view.Item3,
                        fieldOfView=view.Item4, normalStrength=strength,
                        outlineEnabled=setting.Item2, keyShadows=setting.Item3,
                        note=diagnostics ? "Actual Unity URP still. Fixed pose/atlas/camera/key; only outline renderer enabled state and key shadow setting vary. Surface diagnostic, not quality approval." :
                            "Actual Unity URP Edit Mode still, identical Idle pose/camera/key/atlas with only normal strength toggled. No performance or animation-quality acceptance. No asset material or catalog mutation."
                    },true));
                }
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(light.gameObject); Object.DestroyImmediate(camera.gameObject);
                foreach(var material in materials) Object.DestroyImmediate(material);
            }
            if(Hash(folder+"/"+name+".fbx")!=modelHash) throw new InvalidOperationException("Preview changed its source FBX");
            Debug.Log("ORBIS_CANDIDATE_PREVIEW "+name+" / "+output);
        }
        public static void Render(Camera camera,string path)
        {
            var target=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32) { antiAliasing=4 };
            var resolved=new RenderTexture(1024,1024,0,RenderTextureFormat.ARGB32);
            var pixels=new Texture2D(1024,1024,TextureFormat.RGB24,false);
            var active=RenderTexture.active;
            VolumeStack previous=null;
            bool asynchronous=ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation=false;
            try
            {
                target.Create(); resolved.Create();
                if(!VolumeManager.instance.isInitialized)
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting); camera.UpdateVolumeStack();
                previous=VolumeManager.instance.stack;
                VolumeManager.instance.stack=camera.GetUniversalAdditionalCameraData().volumeStack;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                target.ResolveAntiAliasedSurface(resolved); RenderTexture.active=resolved;
                pixels.ReadPixels(new Rect(0,0,1024,1024),0,0); pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=active;
                if(previous!=null) VolumeManager.instance.stack=previous;
                ShaderUtil.allowAsyncCompilation=asynchronous;
                Object.DestroyImmediate(pixels); target.Release(); resolved.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(resolved);
            }
        }
        static string Hash(string path)
        {
            using(var stream=File.OpenRead(path))
            using(var algorithm=SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
