// Activate under Assets/Orbis/Game/Editor only after the rig's Blender pose review.
// This imports separate candidate assets; it does not mutate the live art catalog.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Art;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    public static class CharacterPipelineImport
    {
        public const string Root = "Assets/Orbis/Game/Characters/Candidates";
        [Serializable] public sealed class ImportReport
        {
            public string character, model, prefab, avatar, unityVersion;
            public bool validHumanoid, appliedRootMotion, normalMapSupported;
            public int humanBoneCount, transformCount, rendererCount, vertexCount;
            public string[] warnings;
        }

        public static void ImportRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-characterPipelineName");
            if (index < 0 || index+1 >= args.Length)
                throw new ArgumentException("Supply -characterPipelineName Stella or Polaris.");
            Import(args[index+1], RequestedRoot());
        }

        public static string RequestedRoot()
        {
            string[] args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"-characterPipelineRoot");
            string path=index<0 ? Root : index+1<args.Length ? args[index+1].Replace('\\','/').TrimEnd('/') : "";
            if(path!=Root && (!path.StartsWith(Root+"/",StringComparison.Ordinal) ||
                path.Split('/').Any(part=>part==".." || part=="." || part.Length==0)))
                throw new ArgumentException("Import root must be a candidate asset subfolder.");
            return path;
        }

        public static void Import(string name, string candidateRoot=Root)
        {
            if (name != "Stella" && name != "Polaris") throw new ArgumentException("Unknown hero candidate.");
            string folder = candidateRoot+"/"+name;
            string modelPath = folder+"/"+name+".fbx";
            if (!File.Exists(modelPath)) throw new FileNotFoundException("Reviewed rig FBX required", modelPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing model importer");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var transforms = model.GetComponentsInChildren<Transform>(true);
            var human = new List<HumanBone>();
            for (int i=0; i<HumanTrait.BoneName.Length; ++i)
            {
                string humanName=HumanTrait.BoneName[i], boneName=humanName.Replace(" ", "");
                var matches=transforms.Where(t=>t.name == boneName).ToArray();
                if (matches.Length > 1) throw new InvalidOperationException("Ambiguous bone "+boneName);
                if (matches.Length == 0)
                {
                    if (HumanTrait.RequiredBone(i)) throw new InvalidOperationException("Missing required bone "+boneName);
                    continue;
                }
                human.Add(new HumanBone { humanName=humanName, boneName=boneName,
                    limit=new HumanLimit { useDefaultValues=true } });
            }
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar=null;
            importer.importAnimation=false;
            importer.optimizeGameObjects=false; // Secondary bones, hand sockets and M3 remain accessible.
            importer.isReadable=true; // Needed by existing geometry normalization and pose acceptance.
            importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.importNormals=ModelImporterNormals.Import;
            importer.importTangents=ModelImporterTangents.CalculateMikk;
            importer.importCameras=false; importer.importLights=false; importer.addCollider=false;
            importer.humanDescription=new HumanDescription
            {
                human=human.ToArray(), skeleton=transforms.Select(t=>new SkeletonBone
                    { name=t.name, position=t.localPosition, rotation=t.localRotation, scale=t.localScale }).ToArray(),
                upperArmTwist=.5f, lowerArmTwist=.5f, upperLegTwist=.5f, lowerLegTwist=.5f,
                armStretch=.03f, legStretch=.03f, feetSpacing=0f, hasTranslationDoF=false
            };
            importer.SaveAndReimport();
            var avatar=AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().SingleOrDefault(a=>a.isHuman && a.isValid);
            if (avatar==null) throw new InvalidOperationException("Candidate did not produce a valid Humanoid Avatar");
            var catalog=Resources.Load<ArtAssetCatalog>("Art/Catalog");
            if (catalog==null || catalog.ExplorerToonMaterial==null) throw new InvalidOperationException("Existing toon catalog required");
            Material material=AssetDatabase.LoadAssetAtPath<Material>(folder+"/OriginalAtlas.mat");
            if (material==null)
            {
                material=new Material(catalog.ExplorerToonMaterial) { name=name+" Original Atlas" };
                AssetDatabase.CreateAsset(material, folder+"/OriginalAtlas.mat");
            }
            // Do not pass through EX_Face/EX_Hair material remapping: these maps are
            // the supplied character's complete atlas, not the old generated UV layout.
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_PaletteStrength", 0f);
            material.SetFloat("_VertexColorStrength", 0f);
            material.SetFloat("_FaceLighting", 0f);
            material.SetTexture("_BaseMap", ImportMap(folder, "BaseColor", false));
            Texture2D normal=ImportMap(folder, "Normal", true);
            bool normalSupported=material.HasProperty("_NormalMap");
            if (normalSupported)
            {
                material.SetTexture("_NormalMap", normal);
                material.SetFloat("_NormalStrength", 1f);
            }
            var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            string prefabPath=folder+"/"+name+".prefab";
            try
            {
                instance.name=name+" Candidate";
                var animator=instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar=avatar;
                animator.runtimeAnimatorController=catalog.Characters[0].Controller;
                animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var skins=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skins.Length==0) throw new InvalidOperationException("Rig contains no skinned renderer");
                foreach (var skin in skins)
                {
                    if (skin.sharedMesh==null || skin.bones.Any(b=>b==null)) throw new InvalidOperationException("Missing skin mesh/bone binding");
                    skin.sharedMaterials=Enumerable.Repeat(material,skin.sharedMesh.subMeshCount).ToArray();
                    skin.quality=SkinQuality.Bone4;
                    skin.updateWhenOffscreen=true;
                    if (skin.sharedMesh.boneWeights.Length != skin.sharedMesh.vertexCount)
                        throw new InvalidOperationException("FBX did not retain per-vertex skin weights");
                    if (skin.sharedMesh.boneWeights.Any(w=>float.IsNaN(w.weight0) || Math.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1f)>.001f))
                        throw new InvalidOperationException("Invalid normalized skin weights after FBX import");
                }
                PrefabUtility.SaveAsPrefabAsset(instance,prefabPath);
                var report=new ImportReport { character=name, model=modelPath, prefab=prefabPath, avatar=avatar.name,
                    unityVersion=Application.unityVersion, validHumanoid=avatar.isHuman && avatar.isValid,
                    appliedRootMotion=animator.applyRootMotion, normalMapSupported=normalSupported,
                    humanBoneCount=human.Count, transformCount=transforms.Length, rendererCount=skins.Length,
                    vertexCount=skins.Sum(s=>s.sharedMesh.vertexCount),
                    warnings=normalSupported ? Array.Empty<string>() : new[]{"Atlas normal map is stored but the current toon graph lacks its input; appearance integration pending."} };
                string reportDir="TestResults/CharacterPipeline/UnityImport"+candidateRoot.Substring(Root.Length);
                Directory.CreateDirectory(reportDir);
                File.WriteAllText(reportDir+"/"+name+".json",JsonUtility.ToJson(report,true));
            }
            finally { Object.DestroyImmediate(instance); }
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            // Compile both ordinary and outline passes with the candidate atlas assigned.
            // A serialized texture connection alone is not proof of a working URP normal input.
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                for (int pass=0; pass<material.passCount; ++pass) ShaderUtil.CompilePass(material,pass);
                var errors=ShaderUtil.GetShaderMessages(material.shader).Where(m=>m.severity.ToString()=="Error").ToArray();
                if (errors.Length>0) throw new InvalidOperationException(string.Join("\n",errors.Select(m=>m.message)));
            }
            finally { ShaderUtil.allowAsyncCompilation=oldAsync; }
            Debug.Log("ORBIS_CANDIDATE_IMPORTED "+name+" / valid Humanoid; live catalog unchanged");
        }

        static Texture2D ImportMap(string folder,string role,bool normal)
        {
            string path=new[]{".png",".jpg"}.Select(extension=>folder+"/Textures/"+role+extension).Single(File.Exists);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null) throw new InvalidOperationException("Missing map importer "+path);
            importer.textureType=normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture=!normal;
            importer.alphaSource=TextureImporterAlphaSource.None;
            importer.mipmapEnabled=true;
            importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=4;
            importer.maxTextureSize=4096;
            importer.textureCompression=TextureImporterCompression.Uncompressed; // Acceptance baseline; GPU compression is reviewed afterwards.
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
