using System;
using System.IO;
using System.Linq;
using Orbis.Art;
using Orbis.M4;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.Game.Editor
{
    /// <summary>A material-only migration for the authored world; transforms, colliders and scene IDs stay intact.</summary>
    public static class OpenWorldPresentationFixes
    {
        private const string MaterialFolder = "Assets/Orbis/Game/Generated/Materials";
        public static void ApplySavedScene()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            if(!File.Exists(OpenWorldSceneBuilder.ScenePath)) throw new BuildFailedException("Create the authored world first.");
            Scene scene=SceneManager.GetSceneByPath(OpenWorldSceneBuilder.ScenePath);
            if(!scene.IsValid()||!scene.isLoaded)
            {
                if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene=EditorSceneManager.OpenScene(OpenWorldSceneBuilder.ScenePath,OpenSceneMode.Single);
            }
            var world=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<M4SceneBootstrap>(true)).Single();
            var catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>("Assets/Orbis/Art/Resources/Art/Catalog.asset");
            ApplyTo(world.gameObject,catalog);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene,OpenWorldSceneBuilder.ScenePath)) throw new BuildFailedException("Could not save presentation changes.");
            OpenWorldSceneBuilder.Validate();
            Transform[] saved=world.GetComponentsInChildren<Transform>(true);
            string groups=string.Join(" | ",world.transform.Cast<Transform>().Select(x=>x.name));
            Debug.Log("Authored world inventory: "+saved.Length+" GameObjects, "+
                world.GetComponentsInChildren<Renderer>(true).Length+" Renderers, "+
                world.GetComponentsInChildren<Collider>(true).Length+" Colliders. Groups: "+groups);
            Debug.Log("Authored world presentation updated: coherent grass/sand surfaces and stone cliffs; saved object transforms and colliders preserved.");
        }

        public static void ApplyTo(GameObject world,ArtAssetCatalog catalog)
        {
            if(world==null||catalog==null||catalog.DefaultToonMaterial==null) throw new ArgumentException("An authored world and Art catalog are required.");
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
            // Unspecified presentation defaults: muted grass and warm rock, with very small tile variation.
            // Color atlases are deliberately removed here: platform_grass's orange rim must not create islands.
            Material grass=Plain(catalog,"MeadowGrass",new Color(.28f,.40f,.23f));
            Material grassVariation=Plain(catalog,"MeadowGrassSoftVariation",new Color(.284f,.405f,.235f));
            Material sand=Plain(catalog,"MeadowLakeSand",new Color(.58f,.51f,.35f));
            Material rock=Plain(catalog,"MeadowCliffStone",new Color(.37f,.36f,.32f));
            Material ridge=Plain(catalog,"MeadowRidgeStone",new Color(.355f,.34f,.30f));
            var transforms=world.GetComponentsInChildren<Transform>(true);
            foreach(Transform node in transforms)
            {
                if(node.name=="South Ground"||node.name=="North Ground"||node.name=="East Ground"||node.name=="West Ground")
                {
                    foreach(Transform part in node)
                    {
                        Material color=grass;
                        if(part.name.StartsWith("Ground Tile ",StringComparison.Ordinal))
                        {
                            string[] numbers=part.name.Split(' ');
                            if(numbers.Length==4&&int.TryParse(numbers[2],out int x)&&int.TryParse(numbers[3],out int z)&&(x*7+z*3)%5==0)
                                color=grassVariation;
                        }
                        Assign(part,color);
                    }
                }
                else if(node.name=="Lake Floor") Assign(node,sand);
                else if(node.name.EndsWith("Cliff Face",StringComparison.Ordinal)||node.name=="Western Ridge Cave Facade") Assign(node,rock);
                else if(node.name.StartsWith("Ridge Face ",StringComparison.Ordinal)) Assign(node,ridge);
            }
        }
        private static Material Plain(ArtAssetCatalog catalog,string name,Color color)
        {
            string path=MaterialFolder+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null)
            {
                material=new Material(catalog.DefaultToonMaterial){name=name};
                material.SetTexture("_BaseMap",null);
                material.SetColor("_BaseColor",color);
                AssetDatabase.CreateAsset(material,path);
            }
            return material;
        }
        private static void Assign(Transform root,Material material)
        {
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer.name.StartsWith("Art Outline",StringComparison.Ordinal)) continue;
                renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
                EditorUtility.SetDirty(renderer);
                if(PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }
    }
}
