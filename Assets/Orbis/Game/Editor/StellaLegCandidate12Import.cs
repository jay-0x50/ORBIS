using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Explicit two-atlas candidate opt-in; existing importer and live roster are untouched.</summary>
    public static class StellaLegCandidate12Import
    {
        public const string CandidateRoot=CharacterPipelineImport.Root+"/StellaIntegrated12";
        public const string Folder=CandidateRoot+"/Stella";
        public const string Prefab=Folder+"/Stella.prefab";
        [Serializable] public sealed class MaterialSlot
        { public int slot,triangles; public string sourceMaterial,material,baseColor,normal,mask; public bool maskSupported; }
        [Serializable] public sealed class Report
        {
            public string modelSha256,prefab,avatar,unityVersion,note;
            public bool validHumanoid,rootMotion,protectedAssetsUnchanged;
            public float humanScale,weightSumMaximumError;
            public int vertices,triangles,bones,transforms; public MaterialSlot[] materials;
            public string[] protectedPaths;
        }
        public static void ImportAndCapture()
        {
            Import();
            StellaLegCandidate12Preview.CaptureRequested();
        }
        public static void Import()
        {
            string modelPath=Folder+"/Stella.fbx",reportPath="TestResults/CharacterPipeline/UnityImport/StellaIntegrated12/TwoMaterial.json";
            if(File.Exists(Prefab)||File.Exists(reportPath)) throw new IOException("Candidate import is fresh-only; preserve earlier evidence.");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            var guard=ProtectedSnapshot();
            CharacterPipelineImport.Import("Stella",CandidateRoot);
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var sourceSkins=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if(sourceSkins.Length!=1 || sourceSkins[0].sharedMesh.subMeshCount!=2 || sourceSkins[0].sharedMaterials.Length!=2)
                throw new InvalidOperationException("Reviewed FBX must have one skinned mesh and exactly two original material slots.");
            var original=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/OriginalAtlas.mat");
            var patch=new Material(original) { name="Stella Local Leg Atlas" };
            AssetDatabase.CreateAsset(patch,Folder+"/LegPatchAtlas.mat");
            Texture2D patchColor=ImportTexture("LegPatch_BaseColor.png",false,false);
            Texture2D patchNormal=ImportTexture("LegPatch_Normal.png",true,true);
            Texture2D originalMask=ImportTexture("Mask.jpg",false,true);
            Texture2D patchMask=ImportTexture("LegPatch_Mask.png",false,true);
            patch.SetTexture("_BaseMap",patchColor);patch.SetTexture("_NormalMap",patchNormal);patch.SetFloat("_NormalStrength",1);
            bool maskSupported=original.HasProperty("_MaskMap");
            if(maskSupported) { original.SetTexture("_MaskMap",originalMask);patch.SetTexture("_MaskMap",patchMask); }
            var slots=new List<MaterialSlot>();var mapped=new Material[2];
            for(int i=0;i<2;i++)
            {
                string sourceName=sourceSkins[0].sharedMaterials[i].name;
                bool isPatch=sourceName.IndexOf("Stella local leg atlas",StringComparison.OrdinalIgnoreCase)>=0;
                bool isOriginal=sourceName=="Material_0";
                if(!isPatch&&!isOriginal) throw new InvalidOperationException("Unexpected FBX material name "+sourceName);
                mapped[i]=isPatch?patch:original;
                slots.Add(new MaterialSlot { slot=i,sourceMaterial=sourceName,material=AssetDatabase.GetAssetPath(mapped[i]),
                    triangles=(int)sourceSkins[0].sharedMesh.GetIndexCount(i)/3,baseColor=AssetDatabase.GetAssetPath(mapped[i].GetTexture("_BaseMap")),
                    normal=AssetDatabase.GetAssetPath(mapped[i].GetTexture("_NormalMap")),mask=AssetDatabase.GetAssetPath(isPatch?patchMask:originalMask),maskSupported=maskSupported });
            }
            if(mapped[0]==mapped[1])throw new InvalidOperationException("Two distinct material mappings required.");
            var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            try
            {
                var skin=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();skin.sharedMaterials=mapped;
                var animator=instance.GetComponent<Animator>();
                if(!animator.avatar.isHuman||!animator.avatar.isValid)throw new InvalidOperationException("Invalid Humanoid Avatar");
                var weights=skin.sharedMesh.boneWeights;
                float error=weights.Max(w=>Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1));
                if(error>.001f)throw new InvalidOperationException("Imported weight normalization failed");
                PrefabUtility.SaveAsPrefabAsset(instance,Prefab);EditorUtility.SetDirty(patch);AssetDatabase.SaveAssets();
                AssertProtected(guard);
                File.WriteAllText(reportPath,JsonUtility.ToJson(new Report {modelSha256=Hash(modelPath),prefab=Prefab,avatar=AssetDatabase.GetAssetPath(animator.avatar),
                    unityVersion=Application.unityVersion,validHumanoid=true,rootMotion=animator.applyRootMotion,humanScale=animator.humanScale,
                    weightSumMaximumError=error,vertices=skin.sharedMesh.vertexCount,triangles=slots.Sum(s=>s.triangles),bones=skin.bones.Length,
                    transforms=instance.GetComponentsInChildren<Transform>(true).Length,materials=slots.ToArray(),protectedAssetsUnchanged=true,protectedPaths=guard.Keys.ToArray(),
                    note="Review candidate only. Existing live roster, V3 candidates, controllers and shared shaders untouched. Mask G=roughness/B=metallic is retained as linear data; the common toon currently has no Mask input, so Blender PBR roughness is not claimed reproduced. Expanded joining strips and the remaining lower source band are under actual URP review."},true));
            }
            finally {Object.DestroyImmediate(instance);AssertProtected(guard);}
            Debug.Log("ORBIS_STELLA_TWO_ATLAS_IMPORTED "+Prefab);
        }
        static Texture2D ImportTexture(string name,bool normal,bool linear)
        {
            string path=Folder+"/Textures/"+name;var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new FileNotFoundException("Missing map",path);
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=!linear;importer.alphaSource=TextureImporterAlphaSource.None;importer.mipmapEnabled=true;
            importer.npotScale=TextureImporterNPOTScale.None; // Preserve 800x784 atlas gutters and UV texel placement.
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        public static Dictionary<string,string> ProtectedSnapshot()
        {
            var paths=new List<string>{"Assets/Orbis/Art/Resources/Art/Catalog.asset"};
            foreach(string root in new[]{"Assets/Orbis/Game/Characters/Candidates/StellaIntegrated06","Assets/Orbis/Game/Characters/Candidates/OriginTrial01","Assets/Orbis/Game/Characters/Candidates/Polaris","Assets/Orbis/Game/LookDev/Shaders"})
                if(Directory.Exists(root))paths.AddRange(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".meta")&&new[]{".fbx",".prefab",".mat",".controller",".shadergraph",".shader",".hlsl"}.Contains(Path.GetExtension(p))));
            return paths.Distinct().ToDictionary(p=>p,p=>Hash(p));
        }
        public static void AssertProtected(Dictionary<string,string> values)
        {foreach(var p in values)if(!File.Exists(p.Key)||Hash(p.Key)!=p.Value)throw new InvalidOperationException("Protected asset changed: "+p.Key);}
        public static string Hash(string path)
        {using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    }
}
