using System;
using System.IO;
using System.Linq;
using Orbis.Art;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.Game.Editor
{
    public static class IslandPresentationPolish
    {
        [MenuItem("Orbis/Game/Apply Island Presentation")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(IslandSceneBuilder.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene=EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath);
            }
            var root=scene.GetRootGameObjects().Single(x=>x.GetComponent<M4SceneBootstrap>()!=null);
            var world=root.GetComponent<M4SceneBootstrap>();
            var terrains=root.GetComponentsInChildren<Terrain>();
            // Clear-day defaults. The overview sits 2 km from the center; short-arena fog hid the entire island.
            RenderSettings.fogStartDistance=1800;RenderSettings.fogEndDistance=4800;
            var entry=root.GetComponent<GameSceneEntry>();
            entry.OverviewCamera.transform.position=new Vector3(-1150,1250,-1360);
            entry.OverviewCamera.transform.LookAt(entry.OverviewFocus);
            entry.OverviewCamera.fieldOfView=59;
            var shader=Shader.Find("Orbis/Island Water");
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Island water shader did not compile.");
            string directory=IslandSceneBuilder.Generated+"/Textures";
            Directory.CreateDirectory(directory);AssetDatabase.Refresh();
            string path=directory+"/WaterElevation.asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            const int size=512;
            if(texture==null){texture=new Texture2D(size,size,TextureFormat.RFloat,false,true){name="Saved island elevations for shoreline water",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};AssetDatabase.CreateAsset(texture,path);}
            var pixels=new Color[size*size];
            for(int z=0;z<size;z++)for(int x=0;x<size;x++)
            {
                Vector3 point=new Vector3(-1000+2000f*x/(size-1),0,-1000+2000f*z/(size-1));
                var tile=terrains.FirstOrDefault(t=>point.x>=t.transform.position.x&&point.x<=t.transform.position.x+1000&&point.z>=t.transform.position.z&&point.z<=t.transform.position.z+1000);
                float height=tile!=null?tile.SampleHeight(point)+tile.transform.position.y:-60;
                pixels[z*size+x]=new Color((height+60)/320,0,0,1);
            }
            texture.SetPixels(pixels);texture.Apply();EditorUtility.SetDirty(texture);
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bool ocean=renderer.name=="Ocean Surface",lake=renderer.name=="Highland Lake Surface";
                if(!ocean&&!lake)continue;
                string materialPath=IslandSceneBuilder.Generated+"/Materials/"+(ocean?"SeaWithShoreline":"LakeWithShoreline")+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}
                material.SetTexture("_HeightMap",texture);material.SetFloat("_WaterLevel",ocean?0:17);
                renderer.sharedMaterial=material;EditorUtility.SetDirty(material);
                if(ocean)renderer.transform.localScale=new Vector3(2000,1,2000);
            }
            var catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>("Assets/Orbis/Art/Resources/Art/Catalog.asset");
            var ruin=AssetDatabase.LoadAssetAtPath<Material>(IslandSceneBuilder.Generated+"/Materials/IslandRuinStone.mat");
            foreach(var region in world.IslandRegions)
                foreach(var node in region.Authored.GetComponentsInChildren<Transform>(true))
                    if(node.name=="Trial arch"||node.name.StartsWith("Ancient shrine pillar")||node.name.StartsWith("Broken colonnade"))
                        foreach(var renderer in node.GetComponentsInChildren<Renderer>())
                        {renderer.sharedMaterials=Enumerable.Repeat(ruin,renderer.sharedMaterials.Length).ToArray();PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);}
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
            IslandSceneBuilder.Validate();
            Debug.Log("Island presentation: long clear-day visibility, continuous shoreline water shading and neutral ancient stone.");
        }
    }
}
