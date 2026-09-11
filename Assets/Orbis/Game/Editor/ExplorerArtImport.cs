using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Art;
using Orbis.M1;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Derive two protagonist render prefabs from original rigged FBXs; the shared gameplay FSM is unchanged.</summary>
    public static class ExplorerArtImport
    {
        const string Models = "Assets/Orbis/Game/Island/Models";
        const string Output = "Assets/Orbis/Game/Island/Prefabs";
        const string Materials = "Assets/Orbis/Game/Island/Materials/Explorers";
        const string Textures = "Assets/Orbis/Game/Island/Textures/Explorers";
        const string CatalogPath = "Assets/Orbis/Art/Resources/Art/Catalog.asset";
        static readonly string[] Names = { "Stella", "Polaris" };

        [MenuItem("Orbis/Game/Import Explorer Characters")]
        public static void Build()
        {
            Build(AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(CatalogPath));
        }

        /// <summary>Allows island setup to run while the separately-authored character source files are still being supplied.</summary>
        public static bool TryBuild()
        {
            if (Names.Any(name => !File.Exists(Models + "/" + name + ".fbx"))) return false;
            Build();
            return true;
        }

        public static void Build(ArtAssetCatalog catalog)
        {
            if (catalog == null || catalog.DefaultToonMaterial == null || catalog.Characters == null || catalog.Characters.Length != 5)
                throw new InvalidOperationException("Import the existing five-character Art catalog before protagonist art.");
            var shared = catalog.Characters[0];
            if (shared.Controller == null || shared.Weapon == null || shared.Controller.animationClips.Length == 0 ||
                shared.Controller.animationClips.Any(clip => !clip.isHumanMotion))
                throw new InvalidOperationException("The existing seven-state Humanoid controller and one-handed sword are required.");
            foreach (string name in Names)
                if (!File.Exists(Models + "/" + name + ".fbx"))
                    throw new FileNotFoundException("The original rigged protagonist FBX is not present yet.", Models + "/" + name + ".fbx");
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Materials); AssetDatabase.Refresh();
            ImportPortraitTextures();
            var appearances = new List<ArtExplorerAsset>();
            foreach (string name in Names)
            {
                string modelPath=Models+"/"+name+".fbx";
                Avatar avatar=ImportHumanoid(modelPath);
                GameObject prefab=BuildPrefab(name,modelPath,avatar,shared.Controller,catalog);
                appearances.Add(new ArtExplorerAsset
                {
                    SourceId=name.ToLowerInvariant(),
                    Character=new ArtCharacterAsset
                    {
                        DisplayName=name, Element=ElementType.None, Prefab=prefab, Avatar=avatar,
                        // The same seven Humanoid clips retarget to this avatar; no new action states or timings.
                        Controller=shared.Controller, Weapon=shared.Weapon, WeaponBoneName="RightHand",
                        WeaponLocalPosition=Vector3.zero, WeaponLocalEuler=shared.WeaponLocalEuler,
                        // Visual default: the slender explorer rig uses 55% of the borrowed chunky prototype sword size.
                        WeaponLocalScale=shared.WeaponLocalScale*.55f, TrailTipLocalPosition=shared.TrailTipLocalPosition
                    }
                });
            }
            catalog.Explorers=appearances.ToArray();
            if(ExplorerLookBuilder.IsReady) ExplorerLookBuilder.Apply(catalog);
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Validate(catalog);
            Debug.Log("ORBIS: Stella and Polaris have textured faces, ash-blond hair, tailored ivory/navy cloth, valid Humanoid Avatars and the existing seven-state animation controller.");
        }

        static void ImportPortraitTextures()
        {
            foreach(string name in Names) ImportPortraitTexture(Textures+"/"+name+"_Face_BaseColor.png",TextureWrapMode.Clamp);
            ImportPortraitTexture(Textures+"/AshBlond_Hair_BaseColor.png",TextureWrapMode.Repeat);
            // A complete 0..1 UV panel per cloth surface; clamp avoids wrapping opposite hem embroidery at edges.
            ImportPortraitTexture(Textures+"/Body_Ivory_BaseColor.png",TextureWrapMode.Clamp);
            ImportPortraitTexture(Textures+"/Body_Navy_BaseColor.png",TextureWrapMode.Clamp);
        }

        static void ImportPortraitTexture(string path,TextureWrapMode wrap)
        {
            if(!File.Exists(path)) throw new FileNotFoundException("The authored explorer texture is required before rebuilding its appearance.",path);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null) throw new InvalidOperationException("No explorer texture importer: "+path);
            importer.textureType=TextureImporterType.Default;
            importer.textureShape=TextureImporterShape.Texture2D;
            importer.sRGBTexture=true; importer.alphaSource=TextureImporterAlphaSource.None;
            importer.isReadable=false; importer.mipmapEnabled=true;
            importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=4;
            importer.wrapMode=wrap; importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=2048;
            // Portrait prototype default: retain fine iris/eyelash strokes without block-compression artifacts.
            // Mipmaps and trilinear filtering still prevent shimmer at the normal third-person camera distance.
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.crunchedCompression=false;
            importer.SaveAndReimport();
        }

        static Avatar ImportHumanoid(string path)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer==null) throw new InvalidOperationException("No FBX importer: "+path);
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model==null) throw new InvalidOperationException("Cannot load protagonist FBX: "+path);
            var transforms=model.GetComponentsInChildren<Transform>(true);
            var human=new List<HumanBone>();
            for (int i=0;i<HumanTrait.BoneName.Length;i++)
            {
                string humanName=HumanTrait.BoneName[i];
                // Unity finger slots contain spaces, while the Blender contract uses LeftIndexProximal, etc.
                string sourceName=humanName.Replace(" ","");
                var joint=transforms.FirstOrDefault(item=>item.name==sourceName);
                if (joint==null)
                {
                    if (HumanTrait.RequiredBone(i)) throw new InvalidOperationException(path+" lacks required bone "+sourceName);
                    continue;
                }
                human.Add(new HumanBone {boneName=joint.name,humanName=humanName,limit=new HumanLimit {useDefaultValues=true}});
            }
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar=null;
            importer.importAnimation=false;
            importer.optimizeGameObjects=false; // Preserve hand sockets and bones for M3 outlines/snapshots.
            importer.isReadable=true; importer.addCollider=false;
            // Keep the authored smooth face normals and UV precision instead of rebuilding faceted features.
            importer.importNormals=ModelImporterNormals.Import;
            importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.importCameras=false; importer.importLights=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation=ModelImporterMaterialLocation.InPrefab;
            importer.humanDescription=new HumanDescription
            {
                human=human.ToArray(),
                skeleton=transforms.Select(item=>new SkeletonBone
                    {name=item.name,position=item.localPosition,rotation=item.localRotation,scale=item.localScale}).ToArray(),
                upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,
                armStretch=.05f,legStretch=.05f,feetSpacing=0f,hasTranslationDoF=false
            };
            importer.SaveAndReimport();
            var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(item=>item.isValid&&item.isHuman);
            if (avatar==null) throw new InvalidOperationException("The generated protagonist rig did not produce a valid Humanoid Avatar: "+path);
            return avatar;
        }

        static GameObject BuildPrefab(string name,string path,Avatar avatar,RuntimeAnimatorController controller,ArtAssetCatalog catalog)
        {
            var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                instance.name=name;
                var animator=instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar=avatar; animator.runtimeAnimatorController=controller;
                animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var cache=new Dictionary<Material,Material>();
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>ConvertMaterial(name,source,catalog,cache)).ToArray();
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen=true;
                }
                if (instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==0)
                    throw new InvalidOperationException("The protagonist FBX must contain a skinned body: "+path);
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                // Keep authored proportions and native transforms. Runtime fits the actual Idle geometry to 1.8m before weapon attachment.
                return PrefabUtility.SaveAsPrefabAsset(instance,Output+"/"+name+".prefab");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        static Material ConvertMaterial(string character,Material source,ArtAssetCatalog catalog,Dictionary<Material,Material> cache)
        {
            if (source==null) return catalog.DefaultToonMaterial;
            if (cache.TryGetValue(source,out var result)) return result;
            string name=source.name;
            foreach(char invalid in Path.GetInvalidFileNameChars()) name=name.Replace(invalid,'_');
            string path=Materials+"/"+character+"_"+name+".mat";
            result=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result==null)
            {
                result=new Material(catalog.DefaultToonMaterial) {name=character+" / "+source.name};
                AssetDatabase.CreateAsset(result,path);
            }
            result.shader=catalog.DefaultToonMaterial.shader;
            Color color=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):
                source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;
            result.SetColor("_BaseColor",color);
            string map=source.HasProperty("_BaseMap")?"_BaseMap":source.HasProperty("_MainTex")?"_MainTex":null;
            result.SetTexture("_BaseMap",map!=null?source.GetTexture(map):null);
            if (map!=null) {result.SetTextureScale("_BaseMap",source.GetTextureScale(map));result.SetTextureOffset("_BaseMap",source.GetTextureOffset(map));}
            if (result.HasProperty("_EmissionColor"))
                result.SetColor("_EmissionColor",source.HasProperty("_EmissionColor")?source.GetColor("_EmissionColor"):Color.black);
            ApplyPortraitMaterial(character,source.name,result);
            EditorUtility.SetDirty(result); cache.Add(source,result);
            return result;
        }

        static void ApplyPortraitMaterial(string character,string sourceName,Material material)
        {
            // Explorer-only presentation defaults. The shader defaults and shared companion/environment materials remain unchanged.
            material.SetFloat("_BandSoftness",.22f);
            material.SetFloat("_ShadowStrength",.8f);
            material.SetFloat("_AmbientFill",.10f);
            material.SetFloat("_OutlineScale",.55f); // 0.825px from the common 1.5px outline pass.
            material.SetColor("_OutlineColor",new Color(.09f,.08f,.10f,1));
            material.SetColor("_ShadowColor",new Color(.48f,.52f,.63f,1));
            bool face=IsRole(sourceName,"EX_Face");
            bool skin=IsRole(sourceName,"EX_Skin");
            bool hair=IsRole(sourceName,"EX_Hair")||IsRole(sourceName,"EX_HairLight");
            if(face||skin)
            {
                material.SetFloat("_BandSoftness",1f);
                material.SetFloat("_ShadowStrength",.18f);
                material.SetFloat("_AmbientFill",.35f);
                material.SetFloat("_OutlineScale",.20f); // Fine 0.3px contour; the painted eyes need no separate hulls.
                material.SetColor("_OutlineColor",new Color(.28f,.17f,.18f,1));
                material.SetColor("_ShadowColor",new Color(.77f,.67f,.67f,1));
                // Neck, ears and hands sample the same sRGB albedo as the face; no separate tint conversion.
                if(skin) BindSkinTexture(material,character);
            }
            if(face) BindTexture(material,Textures+"/"+character+"_Face_BaseColor.png");
            if(hair)
            {
                BindTexture(material,Textures+"/AshBlond_Hair_BaseColor.png");
                material.SetFloat("_BandSoftness",.85f);
                material.SetFloat("_ShadowStrength",.35f);
                material.SetFloat("_AmbientFill",.20f);
                material.SetFloat("_OutlineScale",.28f); // 0.42px avoids thick black borders on each thin lock.
                // Pale blond locks need a soft brown contour; dark ink overwhelms the thin textured strands.
                material.SetColor("_OutlineColor",new Color(.50f,.40f,.28f,1));
                material.SetColor("_ShadowColor",new Color(.70f,.63f,.54f,1));
            }
            if(IsRole(sourceName,"EX_TailoredIvory")||IsRole(sourceName,"EX_TailoredNavy"))
            {
                bool ivory=IsRole(sourceName,"EX_TailoredIvory");
                BindTexture(material,Textures+(ivory?"/Body_Ivory_BaseColor.png":"/Body_Navy_BaseColor.png"));
                // Visual defaults only for the rebuilt cloth: soft broad folds, preserved painted albedo, a fine 0.35px contour.
                material.SetFloat("_BandSoftness",.80f);
                material.SetFloat("_ShadowStrength",.55f);
                material.SetFloat("_AmbientFill",.18f);
                material.SetFloat("_OutlineScale",.35f/1.5f);
                material.SetColor("_OutlineColor",ivory?new Color(.38f,.33f,.29f,1):new Color(.12f,.15f,.21f,1));
                material.SetColor("_ShadowColor",ivory?new Color(.78f,.77f,.79f,1):new Color(.58f,.65f,.75f,1));
            }
            if(IsRole(sourceName,"EX_EyeWhite")||IsRole(sourceName,"EX_Ink"))
            {
                material.SetFloat("_OutlineScale",0f);
                material.SetFloat("_ShadowStrength",0f);
                material.SetFloat("_AmbientFill",.65f);
            }
        }

        static bool IsRole(string sourceName,string role)=>sourceName==role||sourceName.StartsWith(role+".",StringComparison.Ordinal);

        static void BindTexture(Material material,string path)
        {
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture==null) throw new InvalidOperationException("Missing imported explorer base color: "+path);
            material.SetTexture("_BaseMap",texture);
            material.SetTextureScale("_BaseMap",Vector2.one); material.SetTextureOffset("_BaseMap",Vector2.zero);
            // FBX diffuse tint was authored for the old untextured pass. Do not multiply it into painted albedo.
            material.SetColor("_BaseColor",Color.white);
        }

        static void BindSkinTexture(Material material,string character)
        {
            BindTexture(material,Textures+"/"+character+"_Face_BaseColor.png");
            // Visually checked clear skin pixel at UV (.025,.88), away from the painted facial features.
            // Source 1254px PNGs: Stella sRGB (251,229,216), Polaris (249,224,209).
            // Sampling that texel uses the exact same sRGB-to-linear path as the face in either project color space.
            material.SetTextureScale("_BaseMap",Vector2.zero);
            material.SetTextureOffset("_BaseMap",new Vector2(.025f,.88f));
        }
        public static void Validate(ArtAssetCatalog catalog)
        {
            foreach (string name in Names)
            {
                var entry=catalog.Explorer(name.ToLowerInvariant());
                if (entry==null||entry.Prefab==null||entry.Controller==null||entry.Avatar==null||!entry.Avatar.isHuman||!entry.Avatar.isValid)
                    throw new InvalidOperationException("Missing or invalid protagonist appearance: "+name);
                var animator=entry.Prefab.GetComponent<Animator>();
                if (animator==null||animator.avatar!=entry.Avatar||animator.runtimeAnimatorController!=entry.Controller||animator.applyRootMotion)
                    throw new InvalidOperationException("Protagonist prefab animator differs from its visual catalog: "+name);
                if (entry.Controller.animationClips.Any(clip=>!clip.isHumanMotion))
                    throw new InvalidOperationException("Protagonist animations must use the existing Humanoid clips: "+name);
                bool hasFace=false,hasIvoryCloth=false,hasNavyCloth=false;
                foreach(var renderer in entry.Prefab.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials)
                    {
                        var expected=catalog.ExplorerToonMaterial!=null?catalog.ExplorerToonMaterial.shader:catalog.DefaultToonMaterial.shader;
                        if(material==null||!EditorUtility.IsPersistent(material)||material.shader!=expected)
                            throw new InvalidOperationException("Protagonist material must be a saved shared-toon asset: "+name);
                        if(material.name.Contains("EX_Face"))
                        {
                            hasFace=true;
                            ValidateTextureBinding(material,Textures+"/"+name+"_Face_BaseColor.png");
                        }
                        if(material.name.Contains("EX_Skin"))
                        {
                            ValidateTextureBinding(material,Textures+"/"+name+"_Face_BaseColor.png");
                            if(material.GetTextureScale("_BaseMap")!=Vector2.zero||
                                material.GetTextureOffset("_BaseMap")!=new Vector2(.025f,.88f))
                                throw new InvalidOperationException("Explorer skin must sample the checked face-background texel: "+name);
                        }
                        if(material.name.Contains("EX_TailoredIvory"))
                        {
                            hasIvoryCloth=true;
                            ValidateClothMaterial(material,Textures+"/Body_Ivory_BaseColor.png");
                        }
                        if(material.name.Contains("EX_TailoredNavy"))
                        {
                            hasNavyCloth=true;
                            ValidateClothMaterial(material,Textures+"/Body_Navy_BaseColor.png");
                        }
                        if(material.name.Contains("EX_Hair"))
                            ValidateTextureBinding(material,Textures+"/AshBlond_Hair_BaseColor.png");
                    }
                if(!hasFace) throw new InvalidOperationException("The updated explorer must contain its UV-mapped EX_Face surface: "+name);
                if(!hasIvoryCloth||!hasNavyCloth)
                    throw new InvalidOperationException("The rebuilt explorer requires both UV-mapped EX_TailoredIvory and EX_TailoredNavy cloth surfaces: "+name);
            }
        }

        static void ValidateClothMaterial(Material material,string path)
        {
            ValidateTextureBinding(material,path);
            if(material.GetTextureScale("_BaseMap")!=Vector2.one||material.GetTextureOffset("_BaseMap")!=Vector2.zero||
                material.GetFloat("_OutlineScale")<=0||material.GetFloat("_OutlineScale")>1)
                throw new InvalidOperationException("Tailored cloth must preserve its full panel UVs and fine contour: "+material.name);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null||!importer.sRGBTexture||!importer.mipmapEnabled||importer.filterMode!=FilterMode.Trilinear||
                importer.wrapMode!=TextureWrapMode.Clamp||importer.maxTextureSize!=2048||
                importer.npotScale!=TextureImporterNPOTScale.None||importer.textureCompression!=TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException("Tailored cloth albedo must retain its original pixels and high-quality mipmapped import: "+path);
        }

        static void ValidateTextureBinding(Material material,string path)
        {
            if(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"))!=path||material.GetColor("_BaseColor")!=Color.white)
                throw new InvalidOperationException("Explorer albedo must use its explicit texture with white tint: "+material.name);
        }
    }
}

