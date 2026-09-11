using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbis.Game.World
{
    /// <summary>Camera-local visual rain/wind. WorldWeather owns state and calls RenderWeather before each camera.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WorldWeatherEffects : MonoBehaviour
    {
        const int RainCount=1024, StreakCount=96, StreakSegments=8;
        static readonly int OriginId=Shader.PropertyToID("_WeatherOrigin");
        static readonly int CameraId=Shader.PropertyToID("_WeatherCameraPosition");
        static readonly int WindId=Shader.PropertyToID("_WeatherWindDirection");
        static readonly int StateId=Shader.PropertyToID("_WeatherState");
        static readonly int TimeId=Shader.PropertyToID("_WeatherTime");
        static readonly int ShelterId=Shader.PropertyToID("_WeatherShelter");
        [SerializeField] Material rainMaterial,streakMaterial;
        // Visual defaults absent from the plan. Read existing solid colliders only; no new collision/gameplay.
        [SerializeField] LayerMask shelterLayers=1<<8;
        public bool ShowInSceneView=true;
        public Material RainMaterial=>rainMaterial;
        public Material StreakMaterial=>streakMaterial;
        public int DrawCount {get;private set;}
        public float LastRain {get;private set;}
        public float LastStorm {get;private set;}
        public float LastStrongWind {get;private set;}
        public EntityId LastCameraEntityId {get;private set;}
        public int MeshTriangleCount=>(rainMesh!=null?RainCount*2:0)+(streakMesh!=null?StreakCount*StreakSegments*2:0);
        public bool CameraSheltered {get;private set;}
        Mesh rainMesh,streakMesh;
        MaterialPropertyBlock properties;
        Camera shelterCamera;
        Vector3 shelterPosition;
        float shelterTime=-1000;

        public void Configure(Material rain,Material streak)
        {
            if(rain==null)throw new ArgumentNullException(nameof(rain));
            if(streak==null)throw new ArgumentNullException(nameof(streak));
            if(!rain.HasProperty("_OrbisWeatherEffects")||!streak.HasProperty("_OrbisWeatherEffects"))
                throw new ArgumentException("Use the world weather visual shader for both materials.");
            rainMaterial=rain;streakMaterial=streak;
        }

        public void RenderWeather(Camera camera,float rain,float storm,float strongWind,Vector3 windDirection,float timeSeconds)
        {
            DrawCount=0;LastRain=Mathf.Clamp01(rain);LastStorm=Mathf.Clamp01(storm);LastStrongWind=Mathf.Clamp01(strongWind);
            if(!isActiveAndEnabled||camera==null||rainMaterial==null||streakMaterial==null)return;
            if(camera.cameraType==CameraType.SceneView&&!ShowInSceneView)return;
            if(camera.cameraType!=CameraType.Game&&camera.cameraType!=CameraType.SceneView)return;
            if(camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional)&&additional.renderType==CameraRenderType.Overlay)return;
            if((camera.cullingMask&(1<<gameObject.layer))==0)return;
            LastCameraEntityId=camera.GetEntityId();
            if(LastRain<=.001f&&LastStrongWind<=.001f)return;
            if(properties==null)properties=new MaterialPropertyBlock();
            if(rainMesh==null)rainMesh=BuildMesh("World weather / 1024 rain ribbons",RainCount,1,27183);
            if(streakMesh==null)streakMesh=BuildMesh("World weather / 96 curved wind ribbons",StreakCount,StreakSegments,73147);
            Vector3 position=camera.transform.position;
            // Continuous camera volume; the shader subtracts origin/volumeSize before wrapping its world phase.
            // Existing streaks remain world-anchored as the camera moves; only faded volume edges recycle.
            Vector3 origin=position;
            windDirection.y=0;windDirection=windDirection.sqrMagnitude>.0001f?windDirection.normalized:Vector3.right;
            UpdateShelter(camera,position,timeSeconds);
            properties.SetVector(OriginId,new Vector4(origin.x,origin.y,origin.z,1));
            properties.SetVector(CameraId,new Vector4(position.x,position.y,position.z,1));
            properties.SetVector(WindId,new Vector4(windDirection.x,0,windDirection.z,0));
            properties.SetVector(StateId,new Vector4(LastRain,LastStorm,LastStrongWind,0));
            properties.SetFloat(TimeId,timeSeconds);properties.SetFloat(ShelterId,CameraSheltered?1:0);
            // Depth is used only for transparent soft intersections. Root also enables URP depth on the world camera.
            camera.depthTextureMode|=DepthTextureMode.Depth;
            if(additional!=null)additional.requiresDepthTexture=true;
            var render=new RenderParams(rainMaterial)
            {
                camera=camera,layer=gameObject.layer,matProps=properties,worldBounds=new Bounds(origin,Vector3.one*64),
                shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,lightProbeUsage=LightProbeUsage.Off,
                reflectionProbeUsage=ReflectionProbeUsage.Off,motionVectorMode=MotionVectorGenerationMode.ForceNoMotion
            };
            if(LastRain>.001f){Graphics.RenderMesh(render,rainMesh,0,Matrix4x4.identity);DrawCount++;}
            if(LastStrongWind>.001f){render.material=streakMaterial;Graphics.RenderMesh(render,streakMesh,0,Matrix4x4.identity);DrawCount++;}
        }

        void UpdateShelter(Camera camera,Vector3 position,float time)
        {
            if(shelterCamera==camera&&(position-shelterPosition).sqrMagnitude<1&&Mathf.Abs(time-shelterTime)<.2f)return;
            shelterCamera=camera;shelterPosition=position;shelterTime=time;
            // Cast down so one-sided roof meshes are hit from their front face. A camera under a roof suppresses
            // nearby streaks but retains distant rain outside; this is visual shelter, not an indoor gameplay state.
            CameraSheltered=Physics.Raycast(position+Vector3.up*32,Vector3.down,out var hit,31.65f,shelterLayers,QueryTriggerInteraction.Ignore)
                &&hit.point.y>position.y+.35f;
        }

        static Mesh BuildMesh(string name,int count,int segments,int randomSeed)
        {
            var random=new System.Random(randomSeed);int perRibbon=(segments+1)*2;
            var vertices=new Vector3[count*perRibbon];var uv=new Vector2[vertices.Length];var seeds=new Vector4[vertices.Length];
            var indices=new int[count*segments*6];int triangle=0;
            for(int ribbon=0;ribbon<count;ribbon++)
            {
                var seed=new Vector4((float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble());
                int first=ribbon*perRibbon;
                for(int segment=0;segment<=segments;segment++)for(int side=0;side<2;side++)
                {
                    int vertex=first+segment*2+side;float along=segment/(float)segments;
                    vertices[vertex]=new Vector3(along,side-.5f,0);uv[vertex]=new Vector2(along,side);seeds[vertex]=seed;
                }
                for(int segment=0;segment<segments;segment++)
                {
                    int a=first+segment*2;indices[triangle++]=a;indices[triangle++]=a+2;indices[triangle++]=a+1;
                    indices[triangle++]=a+1;indices[triangle++]=a+2;indices[triangle++]=a+3;
                }
            }
            var mesh=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};
            mesh.vertices=vertices;mesh.uv=uv;mesh.SetUVs(1,seeds);mesh.triangles=indices;
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one*64);mesh.UploadMeshData(true);return mesh;
        }
        void OnDisable()=>DisposeMeshes();
        void OnDestroy()=>DisposeMeshes();
        void DisposeMeshes()
        {
            Dispose(rainMesh);Dispose(streakMesh);rainMesh=null;streakMesh=null;properties=null;
            shelterCamera=null;shelterTime=-1000;DrawCount=0;
        }
        static void Dispose(Mesh mesh){if(mesh==null)return;if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
    }
}
