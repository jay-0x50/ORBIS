using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbis.Game.World
{
    /// <summary>Environment-only instancing; every render camera uses its own culling and distance-fade origin.</summary>
    [ExecuteAlways,DisallowMultipleComponent]
    public sealed class WorldGrassField : MonoBehaviour
    {
        const int InstancesPerBatch=256;
        static readonly int CameraPositionId=Shader.PropertyToID("_OrbisGrassCameraPosition");
        static readonly int FadeStartId=Shader.PropertyToID("_OrbisGrassFadeStart");
        static readonly int FadeEndId=Shader.PropertyToID("_OrbisGrassFadeEnd");
        [SerializeField] Mesh mesh;
        [SerializeField] Material material;
        [SerializeField] WorldGrassData data;
        // Defaults absent from the plan: full near density, dither fade from 75m, stop submitting beyond 100m.
        [Min(1)] public float ViewDistance=100f;
        [Min(0)] public float FadeStart=75f;
        // The world wind/player bend shader clamps total horizontal displacement to 1.2m.
        [Min(0)] public float DeformationPadding=2f;
        public bool ShowInSceneView=true;
        public bool ReceiveShadows=true;
        public ShadowCastingMode CastShadows=ShadowCastingMode.Off;
        public WorldGrassData Data=>data;
        public Mesh InstanceMesh=>mesh;
        public Material InstanceMaterial=>material;
        /// <summary>Submitted instances/batches for the most recent eligible camera, not an estimate of visible pixels.</summary>
        public int VisibleInstanceCount {get;private set;}
        public int DrawBatchCount {get;private set;}
        public int VisibleCellCount {get;private set;}
        public EntityId LastCameraEntityId {get;private set;}
        public long ApproximateBufferBytes {get;private set;}
        public long ApproximatePlacementBytes=>data!=null?data.ApproximatePlacementBytes:0;

        sealed class PreparedCell
        {
            public Bounds Bounds;
            public WorldGrassPlacement[] Placements;
            public Matrix4x4[] Matrices;
        }
        PreparedCell[] prepared;
        Matrix4x4[] batch;
        Plane[] planes;
        MaterialPropertyBlock properties;

        public void Configure(Mesh instanceMesh,Material instanceMaterial,WorldGrassData placements)
        {
            if(instanceMesh==null)throw new ArgumentNullException(nameof(instanceMesh));
            if(instanceMesh.subMeshCount!=1)throw new ArgumentException("Grass uses one mesh/material pass.",nameof(instanceMesh));
            if(instanceMaterial==null)throw new ArgumentNullException(nameof(instanceMaterial));
            if(!instanceMaterial.enableInstancing)throw new ArgumentException("Enable GPU instancing on the grass material.",nameof(instanceMaterial));
            if(placements==null)throw new ArgumentNullException(nameof(placements));
            mesh=instanceMesh;material=instanceMaterial;data=placements;
            if(isActiveAndEnabled)Prepare();
        }
        void OnEnable()
        {
            Prepare();
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;
            prepared=null;batch=null;planes=null;properties=null;ApproximateBufferBytes=0;
            ClearCounters();
        }
        void Prepare()
        {
            prepared=null;ApproximateBufferBytes=0;
            if(mesh==null||material==null||data==null)return;
            batch=new Matrix4x4[InstancesPerBatch];planes=new Plane[6];properties=new MaterialPropertyBlock();
            prepared=new PreparedCell[data.Cells.Length];
            var extent=Vector3.Max(Abs(mesh.bounds.min),Abs(mesh.bounds.max));
            for(int c=0;c<prepared.Length;c++)
            {
                var source=data.Cells[c];var matrices=new Matrix4x4[source.Instances.Length];
                for(int i=0;i<matrices.Length;i++)
                {
                    var p=source.Instances[i];
                    matrices[i]=Matrix4x4.TRS(p.Position,Quaternion.Euler(0,p.Yaw,0),p.Scale);
                }
                // Positions/scales are authored in world coordinates, independent of the environment root transform.
                var maximum=Vector3.Scale(extent,source.MaximumScale);
                float horizontal=Mathf.Sqrt(maximum.x*maximum.x+maximum.z*maximum.z);
                var bounds=source.PositionBounds;
                bounds.Expand(new Vector3(horizontal,maximum.y,horizontal)*2f+Vector3.one*(DeformationPadding*2f));
                prepared[c]=new PreparedCell{Bounds=bounds,Placements=source.Instances,Matrices=matrices};
                ApproximateBufferBytes+=(long)matrices.Length*64L+24L;
            }
            ApproximateBufferBytes+=InstancesPerBatch*64L+6L*16L;
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(!isActiveAndEnabled||camera==null||!Eligible(camera))return;
            ClearCounters();LastCameraEntityId=camera.GetEntityId();
            if(prepared==null||mesh==null||material==null||!material.enableInstancing||!SystemInfo.supportsInstancing)return;
            if((camera.cullingMask&(1<<gameObject.layer))==0)return;
            Vector3 position=camera.transform.position;
            float distance=Mathf.Max(1,ViewDistance),distanceSquared=distance*distance;
            properties.SetVector(CameraPositionId,new Vector4(position.x,position.y,position.z,1));
            properties.SetFloat(FadeStartId,Mathf.Clamp(FadeStart,0,distance-.01f));
            properties.SetFloat(FadeEndId,distance);
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            var render=new RenderParams(material)
            {
                camera=camera,layer=gameObject.layer,matProps=properties,shadowCastingMode=CastShadows,
                receiveShadows=ReceiveShadows,lightProbeUsage=LightProbeUsage.Off,reflectionProbeUsage=ReflectionProbeUsage.Off,
                motionVectorMode=MotionVectorGenerationMode.ForceNoMotion
            };
            int count=0,lastCell=-1;Bounds batchBounds=default;
            for(int c=0;c<prepared.Length;c++)
            {
                var cell=prepared[c];
                if(cell.Bounds.SqrDistance(position)>distanceSquared||!GeometryUtility.TestPlanesAABB(planes,cell.Bounds))continue;
                bool submitted=false;
                for(int i=0;i<cell.Matrices.Length;i++)
                {
                    if((cell.Placements[i].Position-position).sqrMagnitude>distanceSquared)continue;
                    if(count==0){batchBounds=cell.Bounds;lastCell=c;}
                    else if(lastCell!=c){batchBounds.Encapsulate(cell.Bounds);lastCell=c;}
                    batch[count++]=cell.Matrices[i];VisibleInstanceCount++;submitted=true;
                    if(count==InstancesPerBatch){Submit(ref render,batchBounds,count);count=0;}
                }
                if(submitted)VisibleCellCount++;
            }
            if(count>0)Submit(ref render,batchBounds,count);
        }
        void Submit(ref RenderParams render,Bounds bounds,int count)
        {
            render.worldBounds=bounds;
            Graphics.RenderMeshInstanced(render,mesh,0,batch,count);
            DrawBatchCount++;
        }
        bool Eligible(Camera camera)
        {
            if(camera.cameraType==CameraType.SceneView)return ShowInSceneView;
            if(camera.cameraType!=CameraType.Game)return false;
            // URP UI/overlay cameras must not duplicate the base camera's grass submissions.
            return !camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional)||additional.renderType!=CameraRenderType.Overlay;
        }
        void ClearCounters(){VisibleInstanceCount=0;DrawBatchCount=0;VisibleCellCount=0;}
        static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
    }
}
