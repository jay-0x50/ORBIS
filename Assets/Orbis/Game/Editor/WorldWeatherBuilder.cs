using System;
using System.IO;
using Orbis.Game.World;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    public static class WorldWeatherBuilder
    {
        [MenuItem("Orbis/Development/Legacy/World Art/05 Connect Continuous Visual Weather")]
        public static void Step5()
        {
            FieldSceneAuthoring.RequireGeneratedEditingAllowed();
            var scene=EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath,OpenSceneMode.Single);
            var world=Object.FindAnyObjectByType<M4SceneBootstrap>();
            if(world==null||world.GetComponent<WorldAtmosphere>()==null)throw new InvalidOperationException("Finish world styling first.");
            var effects=world.GetComponent<WorldWeatherEffects>()??world.gameObject.AddComponent<WorldWeatherEffects>();
            WorldWeatherEffectsBuilder.Configure(effects);
            var weather=world.GetComponent<WorldWeather>()??world.gameObject.AddComponent<WorldWeather>();
            weather.Configure(world.GetComponent<WorldWind>().Zone,effects);
            weather.SetWeather(WorldWeatherKind.Clear,0);
            // Weather streaks fade against the actual scene depth rather than drawing through solid surfaces.
            var pipeline=UniversalRenderPipeline.asset;pipeline.supportsCameraDepthTexture=true;EditorUtility.SetDirty(pipeline);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("TestResults/WorldDev");
            File.WriteAllText("TestResults/WorldDev/World05_Weather.txt","One resident visual weather controller. F11 Clear / Rain / Thunderstorm / StrongWind, six-second transitions. No damage, lift, aura or save changes.\n");
            Debug.Log("ORBIS_WORLD_STEP5: resident continuous visual weather, WindZone, scene-depth precipitation and common sky/fog.");
        }
    }
}
