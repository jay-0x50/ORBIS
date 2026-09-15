using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Art;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Reviewed source rigs only. Creates separate Generic candidates without touching Field actors.</summary>
    public static class BossPipelineImport
    {
        const string Root="Assets/Orbis/Game/Characters/BossCandidates";
        [Serializable] sealed class Report
        {
            public string name, model, modelSha256, prefab, avatar, motionRoot, unityVersion;
            public bool validGeneric;
            public int rendererCount, importedVertexCount, triangles;
            public string[] boneNames;
            public Vector3 boundsMin, boundsMax;
            public string note="Generic rest import only. Large-pose deformation, original motion playback, Field mapping and performance require separate acceptance. Packed mask G=roughness/B=metallic is retained; the shared diffuse toon material does not invent an AO channel.";
        }
        public static void ImportRequested()
        {
            Import(Argument("-bossName"),Argument("-bossImportRun"));
        }
        public static void ImportReadyPair()
        {
            string run=Argument("-bossImportRun");
            foreach(string name in new[]{"FireBoss","WaterBoss"}) Import(name,run);
        }
        public static void Import(string name,string run)
        {
            if(!new[]{"FireBoss","WaterBoss","WindBoss","RockBoss","LightningBoss"}.Contains(name) ||
                string.IsNullOrEmpty(run) || !run.All(c=>char.IsLetterOrDigit(c)||c=='_'))
                throw new ArgumentException("Supply reviewed -bossName and fresh -bossImportRun label.");
            string folder=Root+"/"+run+"/"+name, path=folder+"/"+name+".fbx";
            if(!File.Exists(path)) throw new FileNotFoundException("Reviewed FBX required",path);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            var raw=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(importer==null || raw==null) throw new InvalidOperationException("FBX import failed.");
            var rootBones=raw.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Root").ToArray();
            if(rootBones.Length!=1) throw new InvalidOperationException("One measured Root bone required.");
            string motionRoot=AnimationUtility.CalculateTransformPath(rootBones[0],raw.transform);
            importer.animationType=ModelImporterAnimationType.Generic;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar=null;
            // Unity's documented motionNodeName is a transform path, not merely the leaf bone name.
            // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-motionNodeName.html
            importer.motionNodeName=motionRoot;
            importer.importAnimation=false; importer.optimizeGameObjects=false; importer.optimizeBones=false;
            importer.importNormals=ModelImporterNormals.Import;
            importer.importTangents=ModelImporterTangents.CalculateMikk;
            importer.isReadable=true; importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.importCameras=false; importer.importLights=false; importer.addCollider=false;
            importer.SaveAndReimport();
            var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().SingleOrDefault(a=>a.isValid && !a.isHuman);
            if(avatar==null) throw new InvalidOperationException("Reviewed source failed to create a valid Generic Avatar.");
            var catalog=Resources.Load<ArtAssetCatalog>("Art/Catalog");
            if(catalog==null || catalog.ExplorerToonMaterial==null) throw new InvalidOperationException("Shared toon material required.");
            string materialPath=folder+"/OriginalAtlas.mat", prefabPath=folder+"/"+name+".prefab";
            if(File.Exists(materialPath) || File.Exists(prefabPath)) throw new IOException("Use a fresh candidate run to preserve evidence.");
            var material=new Material(catalog.ExplorerToonMaterial) { name=name+" Source Atlas" };
            material.SetColor("_BaseColor",Color.white); material.SetFloat("_PaletteStrength",0f);
            material.SetFloat("_VertexColorStrength",0f); material.SetFloat("_FaceLighting",0f);
            material.SetTexture("_BaseMap",Map(folder,"BaseColor",false,false));
            material.SetTexture("_NormalMap",Map(folder,"Normal",true,true)); material.SetFloat("_NormalStrength",1f);
            Map(folder,"Mask",false,true);
            AssetDatabase.CreateAsset(material,materialPath);
            var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                instance.name=name+" Reviewed Generic Candidate";
                var animator=instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar=avatar; animator.runtimeAnimatorController=null; animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var skins=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if(skins.Length!=1) throw new InvalidOperationException("Expected the reviewed single skinned source mesh.");
                var skin=skins[0]; var mesh=skin.sharedMesh;
                if(mesh==null || skin.bones.Any(b=>b==null) || mesh.boneWeights.Length!=mesh.vertexCount ||
                    mesh.boneWeights.Any(w=>float.IsNaN(w.weight0)||Math.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1f)>.001f))
                    throw new InvalidOperationException("Invalid imported Generic skin binding.");
                skin.sharedMaterials=Enumerable.Repeat(material,mesh.subMeshCount).ToArray();
                skin.quality=SkinQuality.Bone4; skin.updateWhenOffscreen=true;
                if(instance.GetComponentsInChildren<Collider>(true).Length!=0)
                    throw new InvalidOperationException("Visual candidate must not add gameplay colliders.");
                PrefabUtility.SaveAsPrefabAsset(instance,prefabPath);
                var bounds=skin.bounds;
                var report=new Report { name=name,model=path,modelSha256=Hash(path),prefab=prefabPath,
                    avatar=avatar.name,motionRoot=motionRoot,unityVersion=Application.unityVersion,
                    validGeneric=avatar.isValid&&!avatar.isHuman,rendererCount=skins.Length,
                    importedVertexCount=mesh.vertexCount,triangles=mesh.triangles.Length/3,
                    boneNames=skin.bones.Select(b=>b.name).ToArray(),boundsMin=bounds.min,boundsMax=bounds.max };
                string output="TestResults/CharacterPipeline/UnityBossImport/"+run;
                Directory.CreateDirectory(output);
                File.WriteAllText(output+"/"+name+".json",JsonUtility.ToJson(report,true));
            }
            finally { Object.DestroyImmediate(instance); }
            AssetDatabase.SaveAssets();
            Debug.Log("ORBIS_GENERIC_CANDIDATE_IMPORTED "+name);
        }
        static Texture2D Map(string folder,string role,bool normal,bool linear)
        {
            string path=folder+"/Textures/"+role+".jpg";
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null) throw new FileNotFoundException("Verified original texture required",path);
            importer.textureType=normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture=!linear; importer.maxTextureSize=4096;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=true; importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Trilinear; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static string Argument(string key)
        { var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key); return i>=0&&i+1<args.Length ? args[i+1] : null; }
        static string Hash(string path)
        { using(var input=File.OpenRead(path)) using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant(); }
    }
}
