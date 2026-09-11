using Orbis.M1;
using Orbis.M3;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Game.World
{
    /// <summary>One continuous, camera-sampled art atmosphere. Independent of streamed scene ownership.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WorldAtmosphere : MonoBehaviour
    {
        [SerializeField, Range(0, .15f)] float elementalTint = .065f;
        [SerializeField] Color clearFog = new Color32(163, 187, 199, 255);
        [SerializeField, Min(0)] float fogStart = 400;
        [SerializeField, Min(1)] float fogEnd = 2600;
        static readonly int AmbientId = Shader.PropertyToID("_OrbisBiomeAmbient");
        Color originalFog; float originalStart, originalEnd; bool originalEnabled; FogMode originalMode;
        bool captured;
        WorldWeather weather;

        public static Color ElementalColor(Vector3 position)
        {
            var w=WorldBiome.Sample(position.x,position.z);
            return M3Palette.Primary(ElementType.Fire)*w.Agnia + M3Palette.Primary(ElementType.Water)*w.Teluna
                + M3Palette.Primary(ElementType.Wind)*w.Zephyr + M3Palette.Primary(ElementType.Rock)*w.Granite
                + M3Palette.Primary(ElementType.Lightning)*w.Voltheim;
        }
        public static Color AmbientAt(Vector3 position,float strength=.065f)
            =>Color.Lerp(Color.white,ElementalColor(position),Mathf.Clamp(strength,0,.15f));
        public Color FogAt(Vector3 position)=>Color.Lerp(clearFog,ElementalColor(position),.035f);
        public void ConfigureClearRange(float start,float end){fogStart=Mathf.Max(0,start);fogEnd=Mathf.Max(fogStart+1,end);}

        void OnEnable()
        {
            originalFog=RenderSettings.fogColor;originalStart=RenderSettings.fogStartDistance;
            originalEnd=RenderSettings.fogEndDistance;originalEnabled=RenderSettings.fog;originalMode=RenderSettings.fogMode;captured=true;
            weather=GetComponent<WorldWeather>();
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;
            RenderPipelineManager.endCameraRendering+=AfterCamera;
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera==null||camera.cameraType==CameraType.Preview)return;
            ApplyForCamera(camera);
        }
        public void ApplyForCamera(Camera camera)
        {
            if(camera==null)return;
            // Art defaults: only 6.5% of the exact design-03 palette enters environment ambient light.
            // Broad biome weights produce a continuous tint, never a region-ID or scene-load switch.
            if(weather==null)weather=GetComponent<WorldWeather>();
            float cloud=weather!=null&&weather.isActiveAndEnabled?weather.Sample(camera.transform.position).Cloud:0;
            Shader.SetGlobalColor(AmbientId,AmbientAt(camera.transform.position,elementalTint).linear*(1-cloud*.12f));
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=Color.Lerp(FogAt(camera.transform.position),new Color32(103,124,145,255),cloud*.8f);
            // Above the world's 211m peaks the low-altitude haze thins. This also keeps the actual
            // selection/overview camera readable, without any special case for test camera names.
            float altitude=Mathf.Lerp(1,2.2f,Mathf.InverseLerp(350,1800,camera.transform.position.y));
            RenderSettings.fogStartDistance=Mathf.Lerp(fogStart,60,cloud)*altitude;
            RenderSettings.fogEndDistance=Mathf.Lerp(fogEnd,850,cloud)*altitude;
        }
        void AfterCamera(ScriptableRenderContext context,Camera camera)=>Restore();
        void Restore()
        {
            Shader.SetGlobalColor(AmbientId,Color.white);
            if(!captured)return;
            RenderSettings.fogColor=originalFog;RenderSettings.fogStartDistance=originalStart;
            RenderSettings.fogEndDistance=originalEnd;RenderSettings.fog=originalEnabled;RenderSettings.fogMode=originalMode;
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;
            RenderPipelineManager.endCameraRendering-=AfterCamera;Restore();captured=false;
        }
    }
}
