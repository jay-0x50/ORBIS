using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbis.Game.World
{
    public enum WorldWeatherKind { Clear, Rain, Thunderstorm, StrongWind }
    public readonly struct WorldWeatherSample
    {
        public readonly float Rain,Storm,Wind;
        public float Cloud=>Mathf.Clamp01(Rain*.65f+Storm*.35f);
        public WorldWeatherSample(float rain,float storm,float wind){Rain=rain;Storm=storm;Wind=wind;}
    }
    /// <summary>Presentation-only weather. No damage, elemental application, lift forces, rewards or save mutation.</summary>
    [DisallowMultipleComponent,DefaultExecutionOrder(900)]
    public sealed class WorldWeather:MonoBehaviour
    {
        [SerializeField] WindZone wind;
        [SerializeField] WorldWeatherEffects effects;
        [SerializeField] bool allowPreviewShortcut=true;
        Vector3 current,source,target;
        float transitionElapsed,transitionDuration;
        float baseMain,baseTurbulence,basePulse,baseFrequency;
        bool windCaptured;
        Light renderSun;float savedSunIntensity;Color savedSunColor;bool sunCaptured;
        static readonly int WeatherId=Shader.PropertyToID("_OrbisWeather");
        static readonly int TimeId=Shader.PropertyToID("_OrbisWeatherTime");
        static readonly int FlashId=Shader.PropertyToID("_OrbisWeatherFlash");
        static readonly string[] Labels={"날씨 · 맑음  [F11]","날씨 · 비  [F11]","날씨 · 뇌우  [F11]","날씨 · 강풍  [F11]"};
        public WorldWeatherKind TargetWeather {get;private set;}
        public float CurrentRain=>current.x;
        public float CurrentStorm=>current.y;
        public float CurrentWind=>current.z;
        public float VisualTime {get;private set;}
        public WorldWeatherEffects Effects=>effects;
        public WindZone Wind=>wind;
        public bool ShowHud {get;set;}=true;

        public void Configure(WindZone zone,WorldWeatherEffects presentation)
        {
            RestoreWind();wind=zone;effects=presentation;CaptureWind();
        }
        void OnEnable()
        {
            CaptureWind();RenderPipelineManager.beginCameraRendering+=BeforeCamera;
            RenderPipelineManager.endCameraRendering+=AfterCamera;
        }
        void Update()
        {
            if(allowPreviewShortcut&&Keyboard.current!=null&&Keyboard.current.f11Key.wasPressedThisFrame)
                SetWeather((WorldWeatherKind)(((int)TargetWeather+1)%4));
            Evaluate(Time.unscaledDeltaTime);
        }
        public void SetWeather(WorldWeatherKind kind,float transitionSeconds=6)
        {
            if((int)kind<0||(int)kind>3)throw new System.ArgumentOutOfRangeException(nameof(kind));
            TargetWeather=kind;source=current;transitionElapsed=0;VisualTime=0;
            // Six-second art transition is a prototype default; the design has no timing prescription.
            transitionDuration=Mathf.Max(0,transitionSeconds);
            target=kind switch
            {
                WorldWeatherKind.Rain=>new Vector3(1,0,.35f),
                WorldWeatherKind.Thunderstorm=>new Vector3(1,1,.7f),
                WorldWeatherKind.StrongWind=>new Vector3(0,0,1),
                _=>Vector3.zero
            };
            if(transitionDuration==0)current=target;
            if(isActiveAndEnabled)ApplyWind();
        }
        public void Evaluate(float deltaSeconds)
        {
            float dt=Mathf.Max(0,deltaSeconds);VisualTime+=dt;transitionElapsed+=dt;
            float t=transitionDuration<=0?1:Mathf.Clamp01(transitionElapsed/transitionDuration);
            current=Vector3.Lerp(source,target,t*t*(3-2*t));
            if(isActiveAndEnabled)ApplyWind();
        }
        public WorldWeatherSample Sample(Vector3 position)
        {
            var w=WorldBiome.Sample(position.x,position.z);
            // Region identity changes intensity gently; no spatial trigger creates a wall of weather.
            return new WorldWeatherSample(current.x*(.78f+.22f*w.Teluna),
                current.y*(.72f+.28f*w.Voltheim),current.z*(.72f+.28f*w.Zephyr));
        }
        public static float FlashAt(float seconds)
        {
            // A small distant double flash, 16s apart. Purely visual: no lightning hazard or damage.
            float phase=Mathf.Repeat(seconds-4,16);
            float first=Mathf.Clamp01(1-Mathf.Abs(phase-.08f)/.08f);
            float second=Mathf.Clamp01(1-Mathf.Abs(phase-.30f)/.06f)*.4f;
            return Mathf.Max(first,second);
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera==null||(camera.cameraType!=CameraType.Game&&camera.cameraType!=CameraType.SceneView))return;
            if(camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional)&&additional.renderType==CameraRenderType.Overlay)return;
            var sample=Sample(camera.transform.position);float flash=FlashAt(VisualTime)*sample.Storm;
            Shader.SetGlobalVector(WeatherId,new Vector4(sample.Rain,sample.Storm,sample.Wind,sample.Cloud));
            Shader.SetGlobalFloat(TimeId,VisualTime);Shader.SetGlobalFloat(FlashId,flash);
            renderSun=RenderSettings.sun;
            if(renderSun!=null)
            {
                savedSunIntensity=renderSun.intensity;savedSunColor=renderSun.color;sunCaptured=true;
                renderSun.intensity=savedSunIntensity*(1-.42f*sample.Cloud)+flash*.35f;
                renderSun.color=Color.Lerp(savedSunColor,new Color(.76f,.83f,.94f),sample.Cloud*.35f);
            }
            if(effects!=null)effects.RenderWeather(camera,sample.Rain,sample.Storm,sample.Wind,
                wind!=null?wind.transform.forward:Vector3.right,VisualTime);
        }
        void AfterCamera(ScriptableRenderContext context,Camera camera)=>RestoreCamera();
        void RestoreCamera()
        {
            if(sunCaptured&&renderSun!=null){renderSun.intensity=savedSunIntensity;renderSun.color=savedSunColor;}
            sunCaptured=false;Shader.SetGlobalVector(WeatherId,Vector4.zero);Shader.SetGlobalFloat(FlashId,0);
        }
        void CaptureWind()
        {
            if(windCaptured||wind==null)return;
            baseMain=wind.windMain;baseTurbulence=wind.windTurbulence;basePulse=wind.windPulseMagnitude;baseFrequency=wind.windPulseFrequency;
            windCaptured=true;
        }
        void ApplyWind()
        {
            CaptureWind();if(wind==null)return;
            wind.windMain=baseMain+current.z*1.55f;wind.windTurbulence=baseTurbulence+current.z*.65f;
            wind.windPulseMagnitude=basePulse+current.z*.3f;wind.windPulseFrequency=baseFrequency+current.z*.18f;
            GetComponent<WorldWind>()?.RefreshNow();
        }
        void RestoreWind()
        {
            if(windCaptured&&wind!=null)
            {wind.windMain=baseMain;wind.windTurbulence=baseTurbulence;wind.windPulseMagnitude=basePulse;wind.windPulseFrequency=baseFrequency;}
            windCaptured=false;GetComponent<WorldWind>()?.RefreshNow();
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;RenderPipelineManager.endCameraRendering-=AfterCamera;
            RestoreCamera();RestoreWind();
        }
        void OnGUI()
        {
            if(!allowPreviewShortcut||!ShowHud)return;
            GUI.Label(new Rect(Screen.width*.5f-90,12,240,24),Labels[(int)TargetWeather]);
        }
    }
}
