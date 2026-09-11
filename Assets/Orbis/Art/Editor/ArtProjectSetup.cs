using System;
using System.IO;
using System.Linq;
using Orbis.M4.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Art.Editor
{
    public static class ArtProjectSetup
    {
        public const string CatalogPath="Assets/Orbis/Art/Resources/Art/Catalog.asset";
        private const string Materials="Assets/Orbis/Art/Resources/Art/Materials";

        [MenuItem("Orbis/Art/Setup and Validate")]
        public static void SetupAndValidate()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            M4ProjectSetup.SetupAndValidate();
            Directory.CreateDirectory(Materials); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader shader=Shader.Find("Orbis/Art/UnifiedToon");
            if(shader==null||ShaderUtil.ShaderHasError(shader)) throw new BuildFailedException("Unified toon shader is missing or has compiler errors.");
            var catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(CatalogPath);
            if(catalog==null) {catalog=ScriptableObject.CreateInstance<ArtAssetCatalog>();AssetDatabase.CreateAsset(catalog,CatalogPath);}
            catalog.DefaultToonMaterial=Material("UnifiedToon",shader);
            catalog.DefaultToonMaterial.SetColor("_BaseColor",Color.white);
            catalog.DefaultToonMaterial.SetFloat("_OutlinePixels",1.5f);
            catalog.OutlineMaterial=Material("UnifiedOutline",shader);
            catalog.OutlineMaterial.SetFloat("_OutlineOnly",1);
            catalog.OutlineMaterial.SetFloat("_OutlinePixels",1.5f);
            catalog.OutlineMaterial.SetFloat("_Cull",(float)CullMode.Front);
            catalog.OutlineMaterial.SetColor("_OutlineColor",new Color(.055f,.065f,.085f));
            catalog.WaterMaterial=Material("UnifiedWater",shader);
            catalog.WaterMaterial.SetColor("_BaseColor",new Color(.18f,.55f,.69f,.48f));
            catalog.WaterMaterial.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            catalog.WaterMaterial.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            catalog.WaterMaterial.SetFloat("_ZWrite",0);
            catalog.WaterMaterial.SetOverrideTag("RenderType","Transparent");
            catalog.WaterMaterial.renderQueue=(int)RenderQueue.Transparent;
            foreach(var material in new[]{catalog.DefaultToonMaterial,catalog.OutlineMaterial,catalog.WaterMaterial}) EditorUtility.SetDirty(material);
            ArtCharacterImport.Build(catalog);
            ArtEnvironmentImport.Build(catalog);
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("ORBIS ART setup passed: five humanoid characters, licensed environment/UI, unified toon shader. "+catalog.AnimationSource);
        }
        private static Material Material(string name,Shader shader)
        {
            string path=Materials+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(shader){name=name};AssetDatabase.CreateAsset(material,path);}
            material.shader=shader; return material;
        }
        [MenuItem("Orbis/Art/Validate Imported Assets")]
        public static void Validate()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(CatalogPath);
            if(catalog==null||catalog.Characters.Length!=5) throw new BuildFailedException("Exactly five existing characters must be mapped.");
            foreach(var character in catalog.Characters)
            {
                if(character.Prefab==null||character.Avatar==null||!character.Avatar.isValid||!character.Avatar.isHuman)
                    throw new BuildFailedException("Invalid character Avatar: "+character.DisplayName);
                if(!(character.Controller is AnimatorOverrideController controller)||controller.overridesCount!=7)
                    throw new BuildFailedException("The original seven-state Animator must be overridden: "+character.DisplayName);
                if(controller.animationClips.Any(x=>!x.isHumanMotion)) throw new BuildFailedException("A replacement clip is not humanoid: "+character.DisplayName);
                foreach(var renderer in character.Prefab.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials) CheckMaterial(material);
            }
            foreach(var entry in catalog.Environment)
            {
                if(entry.Prefab==null||entry.Prefab.GetComponentsInChildren<Renderer>(true).Length==0) throw new BuildFailedException("Invalid model: "+entry.Key);
                if(entry.Prefab.GetComponentsInChildren<Collider>(true).Length!=0) throw new BuildFailedException("Art prefab must not add gameplay colliders: "+entry.Key);
                foreach(var renderer in entry.Prefab.GetComponentsInChildren<Renderer>(true)) foreach(var material in renderer.sharedMaterials) CheckMaterial(material);
            }
            foreach(var entry in catalog.Icons) if(entry.Texture==null) throw new BuildFailedException("Missing Kenney UI icon: "+entry.Key);
            if(catalog.DefaultToonMaterial==null||catalog.OutlineMaterial==null||ShaderUtil.ShaderHasError(catalog.DefaultToonMaterial.shader))
                throw new BuildFailedException("Common material is invalid.");
            M4ProjectSetup.Validate();
        }
        private static void CheckMaterial(Material material)
        {
            if(material==null||material.shader.name!="Orbis/Art/UnifiedToon") throw new BuildFailedException("Imported material did not receive the common toon shader.");
        }
        [MenuItem("Orbis/Art/Open Asset World")]
        public static void OpenWorld()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(CatalogPath)==null) SetupAndValidate();
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(M4ProjectSetup.LauncherScene);
        }
        [MenuItem("Orbis/Art/Build Local Content")]
        public static void BuildContent() {SetupAndValidate();M4ProjectSetup.BuildContent();}
    }
}
