using System;
using System.Linq;
using Orbis.M1;
using Orbis.M3;
using UnityEditor;
using UnityEngine;

namespace Orbis.Game.Editor
{
    /// <summary>Step 4 material-only authoring. Leaves, six shared architecture materials and their existing users.</summary>
    public static class WorldMaterialStyling
    {
        const string NatureMaterials="Assets/Orbis/Game/World/Nature/Materials";
        const string ArchitectureMaterials="Assets/Orbis/Game/World/Architecture/Materials";
        const string RampPath="Assets/Orbis/Game/LookDev/Resources/LookDev/CelRamp.asset";
        static readonly string[] ArchitectureRoles={"Ivory","Timber","Slate","Stone","Brass","Glass"};

        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Style world material assets outside Play Mode.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader architecture=Shader.Find("Orbis/World/Architecture"),foliage=Shader.Find("Orbis/World/FoliageToon");
            if(architecture==null||foliage==null)throw new InvalidOperationException("Import the Step 4 world shaders first.");
            if(ShaderUtil.ShaderHasError(architecture)||ShaderUtil.ShaderHasError(foliage))throw new InvalidOperationException("World shaders have compilation errors.");
            var ramp=AssetDatabase.LoadAssetAtPath<Texture2D>(RampPath);
            if(ramp==null||ramp.width!=3||ramp.filterMode!=FilterMode.Point||ramp.wrapMode!=TextureWrapMode.Clamp)
                throw new InvalidOperationException("The existing Point/Clamp three-band CelRamp is required.");
            var nature=AssetDatabase.FindAssets("t:Material",new[]{NatureMaterials}).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m=>m!=null).ToArray();
            var buildings=ArchitectureRoles.Select(role=>AssetDatabase.LoadAssetAtPath<Material>(ArchitectureMaterials+"/Architecture_"+role+".mat")).ToArray();
            if(nature.Length<9||buildings.Any(x=>x==null))throw new InvalidOperationException("Build the existing vegetation and six shared architecture materials first.");
            // All input contracts are checked before modifying a material. Source textures and references stay intact.
            foreach(var material in nature)
            {
                if(material.shader!=foliage)throw new InvalidOperationException("Unexpected nature shader: "+material.name);
                foreach(string property in new[]{"_LeafSaturation","_LeafShadowLift","_LeafTint","_LeafTintStrength","_AmbientStrength","_BaseArray","_BillboardMode"})
                    if(!material.HasProperty(property))throw new InvalidOperationException("Missing editable foliage property "+property);
            }
            // Art default, absent from the game design: a light sage reference used only for green leaf pixels.
            // Luminance matching in the graph retains the original source's painted leaf detail.
            ColorUtility.TryParseHtmlString("#ADC1A0",out Color sage);
            foreach(var material in nature)
            {
                bool grass=material.name=="MeadowGrass"||material.GetFloat("_GrassBend")>.5f;
                material.SetFloat("_LeafSaturation",grass?.84f:.64f);
                material.SetFloat("_LeafShadowLift",grass?0f:.035f);
                material.SetColor("_LeafTint",sage);material.SetFloat("_LeafTintStrength",grass?.16f:.27f);
                material.SetFloat("_AmbientStrength",grass?.045f:.18f);
                // Capture-calibrated art defaults: ground-cover grass needs less desaturation/sky fill
                // than tree crowns. This mild olive multiplier preserves the source's four color
                // gradients and roots/tips, while avoiding pale sage cards floating above the soil.
                // Assign an absolute value so repeated Step 4 builds do not compound the tint.
                if(grass)material.SetColor("_BaseColor",new Color(.79f,.84f,.72f,1f));
                // Keep source textures, tree albedo, shadow palette, cutoff, wind and billboard atlas unchanged.
                material.enableInstancing=true;EditorUtility.SetDirty(material);
            }
            for(int index=0;index<ArchitectureRoles.Length;index++)
            {
                string role=ArchitectureRoles[index];var material=buildings[index];
                Color source=material.GetColor("_BaseColor");Texture texture=material.GetTexture("_BaseMap");
                Vector2 scale=material.GetTextureScale("_BaseMap"),offset=material.GetTextureOffset("_BaseMap");
                material.shader=architecture;
                material.SetColor("_BaseColor",source);material.SetTexture("_BaseMap",texture);
                material.SetTextureScale("_BaseMap",scale);material.SetTextureOffset("_BaseMap",offset);material.SetTexture("_Ramp",ramp);
                material.SetColor("_ShadowColor",new Color(.68f,.73f,.78f));material.SetFloat("_AmbientFill",.24f);
                bool brass=role=="Brass",glass=role=="Glass",slate=role=="Slate";
                // Existing exact M3 secondary colors at restrained material strengths, without editing M3/character data.
                material.SetColor("_SpecColor",brass?M3Palette.Secondary(ElementType.Fire):M3Palette.Secondary(ElementType.Water));
                material.SetFloat("_SpecStrength",brass?.24f:glass?.18f:slate?.055f:.018f);
                material.SetFloat("_SpecPower",brass?64:glass?72:40);
                material.SetColor("_RimColor",brass?M3Palette.Secondary(ElementType.Fire):M3Palette.Secondary(ElementType.Water));
                material.SetFloat("_RimStrength",glass?.10f:slate?.035f:brass?.025f:.012f);material.SetFloat("_RimPower",3.2f);
                material.SetColor("_EmissionColor",glass?M3Palette.Secondary(ElementType.Water)*.025f:Color.black);
                material.SetFloat("_Cull",2);material.renderQueue=2000;material.enableInstancing=true;
                EditorUtility.SetDirty(material);
            }
            // Structural timber/brass already share these six assets. Stone foundations, arches and cascade banks
            // share WorldGroundBuilder.RockMaterial and receive the separate ground pass's shader treatment.
            AssetDatabase.SaveAssets();
            Debug.Log("ORBIS_WORLD_MATERIAL_STYLE: "+nature.Length+" nature materials receive leaf-mask-only sage/luminance controls; six architecture materials preserve source palette/maps with three-band light, LOD-safe shadows/depth and quiet M3 highlights.");
        }
    }
}
