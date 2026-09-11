using System;
using System.IO;
using System.Linq;
using Orbis.Art;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    public static partial class ExplorerLookBuilder
    {
        public const string BladePrefab=Root+"/Resources/LookDev/WayfarerBlade.prefab";
        [MenuItem("Orbis/Look Development/03 Equip Wayfarer's Blade")]
        public static void Step3()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string modelPath=Root+"/Models/WayfarerBlade.fbx";
            var importer=AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if(importer==null)throw new InvalidOperationException("Build the CC0-derived WayfarerBlade FBX first.");
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.animationType=ModelImporterAnimationType.None;importer.importAnimation=false;
            importer.isReadable=true;importer.SaveAndReimport();
            var catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(Catalog);
            var material=MaterialAsset(Root+"/Resources/LookDev/WayfarerBlade.mat",catalog.ExplorerToonMaterial.shader);
            Configure(material,catalog.ExplorerToonMaterial.GetTexture("_Ramp"),"Steel");
            ConfigureHighlights(material);material.SetFloat("_VertexColorStrength",1);
            material.SetTexture("_BaseMap",null);material.SetColor("_BaseColor",Color.white);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
            var root=new GameObject("Wayfarer's Blade");
            try
            {
                var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath),root.transform);
                model.name="CC0 derived longsword";
                foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>())renderer.sharedMaterials=new[]{material};
                var filters=model.GetComponentsInChildren<MeshFilter>();
                if(filters.Length!=1||filters[0].sharedMesh.subMeshCount!=1)throw new InvalidOperationException("Blade must stay one mesh / one material.");
                if(filters[0].sharedMesh.colors.Length!=filters[0].sharedMesh.vertexCount)throw new InvalidOperationException("Sampled palette vertex colors were lost in import.");
                PrefabUtility.SaveAsPrefabAsset(root,BladePrefab);
            }
            finally{Object.DestroyImmediate(root);}
            ApplyBlade(catalog);EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();Validate();
            Debug.Log("ORBIS_LOOK_STEP3_VALIDATED shared blade; original body meshes/avatars untouched; one source material plus existing hull.");
        }
        static void ApplyBlade(ArtAssetCatalog catalog)
        {
            var blade=AssetDatabase.LoadAssetAtPath<GameObject>(BladePrefab);if(blade==null)return;
            foreach(var entry in catalog.Explorers)
            {
                var asset=entry.Character;asset.Weapon=blade;asset.WeaponLocalPosition=Vector3.zero;
                asset.WeaponLocalEuler=Vector3.zero;
                // Blade is authored in metres (+Y tip). Match 1.06m in the actual normalized avatar hierarchy.
                var temporary=new GameObject("Measure weapon socket scale");
                try
                {
                    var model=Object.Instantiate(asset.Prefab,temporary.transform);
                    var animator=model.GetComponentInChildren<Animator>();animator.runtimeAnimatorController=asset.Controller;
                    animator.avatar=asset.Avatar;animator.Rebind();animator.Play("Idle",0,0);animator.Update(0);
                    ArtCharacterRoster.NormalizeVisibleModelHeight(temporary,1.8f,Vector3.zero);
                    var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
                    asset.WeaponLocalScale=Vector3.one/Mathf.Abs(hand.lossyScale.x);
                }
                finally{Object.DestroyImmediate(temporary);}
                // Authored blade end; only the existing presentation trail anchor moves, no hitbox/range changes.
                asset.TrailTipLocalPosition=new Vector3(0,.94f,0);
            }
        }
    }
}
