using Orbis.M4;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Orbis.Art
{
    /// <summary>Installs asset presentation after the completed M4 scene has built its gameplay objects.</summary>
    public sealed class ArtScenePresentation : MonoBehaviour
    {
        private VolumeProfile profile;
        public ArtCharacterRoster Characters {get;private set;}
        public ArtWorldPresentation World {get;private set;}
        public ArtHud Hud {get;private set;}
        public ArtAssetCatalog Catalog {get;private set;}

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded-=OnSceneLoaded;
            SceneManager.sceneLoaded+=OnSceneLoaded;
        }
        private static void OnSceneLoaded(Scene loaded,LoadSceneMode mode)
        {
            foreach(var root in loaded.GetRootGameObjects())
            {
                var scene=root.GetComponentInChildren<M4SceneBootstrap>();
                if(scene==null||!scene.IsInitialized||scene.GetComponent<ArtScenePresentation>()!=null) continue;
                var catalog=Resources.Load<ArtAssetCatalog>("Art/Catalog");
                if(catalog==null) return; // A project without imported assets retains its original prototype presentation.
                scene.gameObject.AddComponent<ArtScenePresentation>().Configure(scene,catalog);
            }
        }
        public static ArtScenePresentation EnsureInstalled(M4SceneBootstrap scene)
        {
            if (scene == null || !scene.IsInitialized) return null;
            var existing = scene.GetComponent<ArtScenePresentation>();
            if (existing != null) return existing;
            var catalog = Resources.Load<ArtAssetCatalog>("Art/Catalog");
            if (catalog == null) return null;
            var result = scene.gameObject.AddComponent<ArtScenePresentation>();
            result.Configure(scene, catalog);
            return result;
        }
        public void Configure(M4SceneBootstrap scene,ArtAssetCatalog catalog)
        {
            Catalog=catalog;
            Characters=gameObject.AddComponent<ArtCharacterRoster>(); Characters.Configure(scene,catalog);
            World=gameObject.AddComponent<ArtWorldPresentation>(); World.Configure(scene,catalog);
            Hud=gameObject.AddComponent<ArtHud>(); Hud.Configure(scene,catalog);
            var volume=gameObject.AddComponent<Volume>(); volume.isGlobal=true; volume.priority=5; volume.weight=1;
            profile=ScriptableObject.CreateInstance<VolumeProfile>();
            var color=profile.Add<ColorAdjustments>(true);
            // 04 style unification: a mild common grade keeps both source atlases readable.
            color.saturation.value=-8; color.contrast.value=5; color.colorFilter.value=new Color(1f,.98f,.95f);
            volume.sharedProfile=profile;
        }
        private void OnDestroy()
        {
            if(profile==null) return;
            foreach(var component in profile.components) Destroy(component);
            Destroy(profile);
        }
    }
}
