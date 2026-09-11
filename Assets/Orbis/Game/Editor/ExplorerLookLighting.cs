using System.Linq;
using Orbis.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    public static partial class ExplorerLookBuilder
    {
        public const string ProfilePath=Root+"/Resources/LookDev/ExplorerGrade.asset";
        [MenuItem("Orbis/Look Development/02 Apply Lighting and Grade")]
        public static void Step2()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(Catalog);
            foreach(var material in catalog.Explorers.SelectMany(x=>x.Character.Prefab.GetComponentsInChildren<Renderer>(true))
                .SelectMany(x=>x.sharedMaterials).Concat(catalog.ExplorerToonMaterial!=null?new[]{catalog.ExplorerToonMaterial}:new Material[0]).Distinct())
                ConfigureHighlights(material);
            foreach(var material in catalog.Explorers.SelectMany(x=>x.Character.Weapon.GetComponentsInChildren<Renderer>(true)).SelectMany(x=>x.sharedMaterials).Distinct())
                ConfigureHighlights(material);
            // Persist dirty material values before prefab/scene imports can reload their asset dependencies.
            AssetDatabase.SaveAssets();
            foreach(var entry in catalog.Explorers)ApplyFaceLightingToPrefab(entry.Character.Prefab);
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);}
            var bloom=Effect<Bloom>(profile);bloom.intensity.Override(.20f);bloom.threshold.Override(1.05f);bloom.scatter.Override(.6f);
            bloom.tint.Override(ColorOf("Ivory"));
            // Deliberately modest art defaults; M3 flash/cinematic volumes retain their higher priorities.
            var grade=Effect<ColorAdjustments>(profile);grade.contrast.Override(7);grade.saturation.Override(5);grade.postExposure.Override(.08f);grade.colorFilter.Override(Color.white);
            var vignette=Effect<Vignette>(profile);vignette.intensity.Override(.10f);vignette.smoothness.Override(.65f);vignette.color.Override(ColorOf("NavyShadow"));
            Effect<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            foreach(var component in profile.components)EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
            var scene=EditorSceneManager.OpenScene("Assets/Orbis/Game/Scenes/Orbis_Island.unity",OpenSceneMode.Single);
            var sun=Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(x=>x.name=="Morning Sun");
            // Key from front-left for the +Z-facing reference pose. Existing day/night gameplay is untouched.
            sun.transform.rotation=Quaternion.LookRotation(-new Vector3(-.40f,.75f,.70f).normalized);
            sun.color=ColorOf("Ivory");sun.intensity=1.15f;
            RenderSettings.sun=sun;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=ColorOf("Ivory")*.55f;
            RenderSettings.ambientEquatorColor=ColorOf("Navy")*.6f;
            RenderSettings.ambientGroundColor=ColorOf("GoldShadow")*.35f;
            // These ambient/probe coefficients are sampled by the shader's rim lighting function.
            var existing=GameObject.Find("Explorer Look Volume");var volume=(existing!=null?existing:new GameObject("Explorer Look Volume")).GetComponent<Volume>();
            if(volume==null)volume=GameObject.Find("Explorer Look Volume").AddComponent<Volume>();
            volume.isGlobal=true;volume.priority=10;volume.weight=1;volume.sharedProfile=profile;
            foreach(var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None))camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Validate();
            Debug.Log("ORBIS_LOOK_STEP2_VALIDATED palette key/SH rim/skin and metal highlights; priority10 grade, M3 priorities preserved.");
        }
        static T Effect<T>(VolumeProfile profile) where T:VolumeComponent
        {if(profile.TryGet<T>(out var component))return component;component=profile.Add<T>(true);AssetDatabase.AddObjectToAsset(component,profile);return component;}
        static void ApplyFaceLightingToPrefab(GameObject prefab)
        {
            string path=AssetDatabase.GetAssetPath(prefab);var copy=PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed=false;
                foreach(var renderer in copy.GetComponentsInChildren<Renderer>(true))
                    if(renderer.sharedMaterials.Any(x=>x.name.EndsWith("EX_Face"))&&renderer.GetComponent<ExplorerFaceLighting>()==null)
                    {renderer.gameObject.AddComponent<ExplorerFaceLighting>();changed=true;}
                if(changed)PrefabUtility.SaveAsPrefabAsset(copy,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(copy);}
        }
        static void ConfigureHighlights(Material m)
        {
            bool skin=m.name.Contains("EX_Skin")||m.name.Contains("EX_Face");
            bool hair=m.name.Contains("EX_Hair");bool metal=m.name.Contains("EX_Gold")||m.name.Contains("Sword")||m.name.Contains("Steel")||m.name=="WayfarerBlade";
            bool gem=m.name.Contains("EX_Gem");
            m.SetFloat("_FaceLighting",m.name.EndsWith("EX_Face")?1:0);
            m.SetColor("_RimColor",ColorOf("Turquoise"));m.SetFloat("_RimStrength",hair?.18f:skin?.07f:.11f);m.SetFloat("_RimPower",3.2f);
            m.SetColor("_SpecColor",ColorOf(metal?"GoldHighlight":"Ivory"));m.SetFloat("_SpecStrength",metal?.5f:gem?.65f:hair?.12f:0);
            m.SetFloat("_SpecPower",metal?64f:40f);m.SetFloat("_SkinSpecStrength",skin?.08f:0);
            if(gem)m.SetColor("_EmissionColor",ColorOf("Turquoise")*.08f);
            EditorUtility.SetDirty(m);
        }
    }
}
