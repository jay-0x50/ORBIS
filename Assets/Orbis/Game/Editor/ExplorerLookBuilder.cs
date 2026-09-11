using System;
using System.IO;
using System.Linq;
using Orbis.Art;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Sequential art changes only; source FBXs, avatars and combat slots are preserved.</summary>
    public static partial class ExplorerLookBuilder
    {
        public const string Root="Assets/Orbis/Game/LookDev";
        const string Catalog="Assets/Orbis/Art/Resources/Art/Catalog.asset";
        [Serializable] public class Palette {public Swatch[] swatches;}
        [Serializable] public class Swatch {public string name;public int r,g,b;}
        static Palette palette;
        public static Color ColorOf(string name)
        {
            if(palette==null)palette=JsonUtility.FromJson<Palette>(File.ReadAllText(Root+"/Resources/LookDev/ReferencePalette.json"));
            var s=palette.swatches.Single(x=>x.name==name);
            return new Color32((byte)s.r,(byte)s.g,(byte)s.b,255);
        }
        public static bool IsReady=>File.Exists(Root+"/Resources/LookDev/ExplorerToon.mat");
        [MenuItem("Orbis/Look Development/01 Apply Ramp Toon")]
        public static void Step1()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/ExplorerToon.shadergraph");
            if(shader==null)throw new InvalidOperationException("Editable ExplorerToon Shader Graph must import first.");
            string dir=Root+"/Resources/LookDev";
            // A data LUT, not painted artwork: exact point-filtered three-band luminance values.
            var ramp=AssetDatabase.LoadAssetAtPath<Texture2D>(dir+"/CelRamp.asset");
            if(ramp==null){ramp=new Texture2D(3,1,TextureFormat.RGBA32,false,true){name="CelRamp"};AssetDatabase.CreateAsset(ramp,dir+"/CelRamp.asset");}
            ramp.filterMode=FilterMode.Point;ramp.wrapMode=TextureWrapMode.Clamp;
            ramp.SetPixels(new[]{new Color(.18f,.18f,.18f,1),new Color(.62f,.62f,.62f,1),Color.white});ramp.Apply();EditorUtility.SetDirty(ramp);
            var main=MaterialAsset(dir+"/ExplorerToon.mat",shader);
            Configure(main,ramp,"Default");
            var outline=MaterialAsset(dir+"/ExplorerOutline.mat",shader);
            Configure(outline,ramp,"Default");outline.SetFloat("_OutlineOnly",1);outline.SetFloat("_Cull",1);
            outline.SetFloat("_CastShadows",0);outline.SetFloat("_ZWrite",0);outline.SetFloat("_ZWriteControl",2);outline.renderQueue=2001;
            foreach(string pass in new[]{"ShadowCaster","DepthOnly","DepthNormals","DepthNormalsOnly","MotionVectors"})outline.SetShaderPassEnabled(pass,false);
            var catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(Catalog);
            catalog.ExplorerToonMaterial=main;catalog.ExplorerOutlineMaterial=outline;
            Apply(catalog);AssetDatabase.SaveAssets();Validate();
        }
        public static void Apply(ArtAssetCatalog catalog)
        {
            if(catalog.ExplorerToonMaterial==null)return;
            var ramp=catalog.ExplorerToonMaterial.GetTexture("_Ramp");
            foreach(var explorer in catalog.Explorers)
            {
                foreach(var renderer in explorer.Character.Prefab.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials.Distinct())
                    {
                        material.shader=catalog.ExplorerToonMaterial.shader;
                        Configure(material,ramp,material.name);
                    }
            }
            // STEP 1 changes the existing sword's shading only. STEP 3 supplies the replacement silhouette.
            string path=Root+"/Resources/LookDev/PrototypeSword.prefab";
            GameObject prototype=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prototype==null)
            {
                var source=catalog.Characters[0].Weapon;
                var copy=Object.Instantiate(source);
                try
                {
                    foreach(var renderer in copy.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats=renderer.sharedMaterials;
                        for(int i=0;i<mats.Length;i++)
                        {
                            string mp=Root+"/Resources/LookDev/PrototypeSword_"+i+".mat";
                            var material=MaterialAsset(mp,catalog.ExplorerToonMaterial.shader);
                            Configure(material,ramp,"Steel");material.SetColor("_BaseColor",ColorOf("Steel"));
                            if(mats[i]!=null&&mats[i].HasProperty("_BaseMap"))material.SetTexture("_BaseMap",mats[i].GetTexture("_BaseMap"));
                            mats[i]=material;
                        }
                        renderer.sharedMaterials=mats;
                    }
                    prototype=PrefabUtility.SaveAsPrefabAsset(copy,path);
                }
                finally{Object.DestroyImmediate(copy);}
            }
            foreach(var explorer in catalog.Explorers)explorer.Character.Weapon=prototype;
            foreach(var material in prototype.GetComponentsInChildren<Renderer>(true).SelectMany(x=>x.sharedMaterials).Distinct())
            {Configure(material,ramp,"Steel");material.SetColor("_BaseColor",ColorOf("Steel"));EditorUtility.SetDirty(material);}
            if(File.Exists(ProfilePath))
            {
                foreach(var material in catalog.Explorers.SelectMany(x=>x.Character.Prefab.GetComponentsInChildren<Renderer>(true)).SelectMany(x=>x.sharedMaterials).Distinct())ConfigureHighlights(material);
                foreach(var material in prototype.GetComponentsInChildren<Renderer>(true).SelectMany(x=>x.sharedMaterials).Distinct())ConfigureHighlights(material);
                AssetDatabase.SaveAssets();
                foreach(var entry in catalog.Explorers)ApplyFaceLightingToPrefab(entry.Character.Prefab);
            }
            if(File.Exists(BladePrefab))ApplyBlade(catalog);
            EditorUtility.SetDirty(catalog);
        }
        static void Configure(Material m,Texture ramp,string role)
        {
            m.SetTexture("_Ramp",ramp);m.SetFloat("_OutlineOnly",0);m.SetFloat("_Cull",2);m.SetFloat("_ZWrite",1);
            m.SetFloat("_SrcBlend",1);m.SetFloat("_DstBlend",0);m.SetFloat("_Threshold",0);m.renderQueue=2000;
            m.SetFloat("_AlphaClip",1);m.EnableKeyword("_ALPHATEST_ON");m.SetFloat("_CastShadows",1);
            m.SetFloat("_ZWriteControl",1);m.SetFloat("_QueueControl",1);
            m.SetFloat("_OutlinePixels",1.5f);m.SetFloat("_OutlineScale",.8f);
            m.SetColor("_OutlineColor",ColorOf("Leather"));m.SetColor("_ShadowColor",ColorOf("NavyShadow"));
            m.SetFloat("_PaletteStrength",0);m.SetColor("_GoldColor",ColorOf("Gold"));
            m.SetColor("_EmissionColor",Color.black);m.SetColor("_BaseColor",Color.white);
            if(role.EndsWith("EX_Face")||role.EndsWith("EX_Skin")){m.SetFloat("_OutlineScale",.5f);m.SetColor("_OutlineColor",ColorOf("GoldShadow"));m.SetColor("_ShadowColor",ColorOf("GoldShadow"));}
            if(role.EndsWith("EX_Hair")||role.EndsWith("EX_HairLight")){m.SetFloat("_OutlineScale",.4f);m.SetColor("_OutlineColor",ColorOf("GoldShadow"));m.SetColor("_ShadowColor",ColorOf("GoldShadow"));}
            if(role.EndsWith("EX_TailoredNavy")||role.EndsWith("EX_TailoredIvory"))
            {
                bool navy=role.EndsWith("EX_TailoredNavy");m.SetColor("_PaletteColor",ColorOf(navy?"Navy":"Ivory"));
                m.SetFloat("_PaletteStrength",1);m.SetFloat("_TextureReference",TextureMidtone(m.GetTexture("_BaseMap")));
            }
            else if(role.Contains("EX_Gold"))m.SetColor("_BaseColor",ColorOf("Gold"));
            else if(role.Contains("EX_Gem")){m.SetColor("_BaseColor",ColorOf("Turquoise"));}
            else if(role.Contains("EX_Leather")||role.Contains("EX_Trousers"))m.SetColor("_BaseColor",ColorOf("Leather"));
            else if(role.Contains("EX_Navy"))m.SetColor("_BaseColor",ColorOf("Navy"));
            else if(role.Contains("EX_Ivory")||role.Contains("EX_CapeLining"))m.SetColor("_BaseColor",ColorOf("Ivory"));
            else if(role.Contains("EX_Teal"))m.SetColor("_BaseColor",ColorOf("Turquoise"));
            else if(role.Contains("EX_Ink"))m.SetColor("_BaseColor",ColorOf("Leather"));
            EditorUtility.SetDirty(m);
        }
        static float TextureMidtone(Texture texture)
        {
            string path=AssetDatabase.GetAssetPath(texture);
            if(string.IsNullOrEmpty(path))return .5f;
            var readable=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try{readable.LoadImage(File.ReadAllBytes(path));var values=readable.GetPixels().Where((_,i)=>i%97==0).Select(x=>x.linear.grayscale).OrderBy(x=>x).ToArray();return Mathf.Max(.001f,values[values.Length/2]);}
            finally{Object.DestroyImmediate(readable);}
        }
        static Material MaterialAsset(string path,Shader shader)
        {var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}else m.shader=shader;return m;}
        public static void Validate()
        {
            var c=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(Catalog);bool old=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                foreach(var m in new[]{c.ExplorerToonMaterial,c.ExplorerOutlineMaterial})
                {
                    foreach(string p in new[]{"_Ramp","_BaseMap","_Threshold","_PrimaryColor","_SecondaryColor","_OutlineOnly","_Cull"})
                        if(!m.HasProperty(p))throw new InvalidOperationException("Explorer shader missing "+p);
                    for(int i=0;i<m.passCount;i++)ShaderUtil.CompilePass(m,i);
                    var errors=ShaderUtil.GetShaderMessages(m.shader).Where(x=>x.severity.ToString()=="Error").ToArray();
                    if(errors.Length>0)throw new InvalidOperationException(string.Join("\n",errors.Select(x=>x.message)));
                }
            }
            finally{ShaderUtil.allowAsyncCompilation=old;}
            Debug.Log("ORBIS_LOOK_STEP1_VALIDATED editable ramp graph; existing avatar, texture and M3 property contracts retained.");
        }
    }
}
