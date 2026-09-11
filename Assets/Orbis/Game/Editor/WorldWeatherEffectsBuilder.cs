using System;
using System.IO;
using Orbis.Game.World;
using UnityEditor;
using UnityEngine;

namespace Orbis.Game.Editor
{
    public static class WorldWeatherEffectsBuilder
    {
        public const string MaterialFolder="Assets/Orbis/Game/World/Weather/Materials";
        public const string RainPath=MaterialFolder+"/WorldRain.mat";
        public const string StreakPath=MaterialFolder+"/WorldWindStreak.mat";
        public static void Configure(WorldWeatherEffects effects)
        {
            if(effects==null)throw new ArgumentNullException(nameof(effects));
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Configure persistent world weather materials outside Play Mode.");
            Directory.CreateDirectory(MaterialFolder);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader=Shader.Find("Orbis/World/WeatherParticles");
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Import the world weather shader without errors first.");
            // Art defaults: restrained cool rain and pale neutral wind. No external/generated bitmap, VFX asset or gameplay dependency.
            var rain=MaterialAt(RainPath,shader);rain.SetFloat("_Mode",0);rain.SetFloat("_Opacity",.24f);
            rain.SetColor("_BaseColor",new Color32(171,195,205,255));rain.renderQueue=3100;
            var streak=MaterialAt(StreakPath,shader);streak.SetFloat("_Mode",1);streak.SetFloat("_Opacity",.095f);
            streak.SetColor("_BaseColor",new Color32(195,209,198,255));streak.renderQueue=3101;
            EditorUtility.SetDirty(rain);EditorUtility.SetDirty(streak);effects.Configure(rain,streak);EditorUtility.SetDirty(effects);
            AssetDatabase.SaveAssets();
        }
        static Material MaterialAt(string path,Shader shader)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
            else material.shader=shader;
            return material;
        }
    }
}
