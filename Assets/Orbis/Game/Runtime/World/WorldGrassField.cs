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
        // Old generated scenes retain world-space placement. The Field authoring scene opts in once,
        // recording its original matrix so later hierarchy translation/rotation/scale moves the same grass.
        [SerializeField] bool transformEditing;
        [SerializeField,HideInInspector] Matrix4x4 placementWorldToReference=Matrix4x4.identity;
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
        public bool TransformEditingEnabled=>transformEditing;
        public Matrix4x4 PlacementToWorldMatrix=>PlacementTransform();
        /// <summary>Conservative bound shared by scene export, editor culling and runtime rendering.</summary>
        public Bounds GetWorldCellBounds(WorldGrassCell cell)
        {
            if(cell==null)throw new ArgumentNullException(nameof(cell));
            if(mesh==null)throw new InvalidOperationException("Grass mesh is missing.");
            var extent=Vector3.Max(Abs(mesh.bounds.min),Abs(mesh.bounds.max));
            var maximum=Vector3.Scale(extent,cell.MaximumScale);
            float horizontal=Mathf.Sqrt(maximum.x*maximum.x+maximum.z*maximum.z);
            var bounds=cell.PositionBounds;
            bounds.Expand(new Vector3(horizontal,maximum.y,horizontal)*2f);
            bounds=TransformBounds(bounds,PlacementTransform());
            bounds.Expand(Vector3.one*(DeformationPadding*2f));
            return bounds;
        }
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
            public Matrix4x4[] Matrices;
        }
        PreparedCell[] prepared;
        Matrix4x4[] batch;
        Plane[] planes;
        MaterialPropertyBlock properties;
        Matrix4x4 preparedTransform;
        bool needsPrepare;

        /// <summary>Opt in without moving current placements. Repeated calls never reset the authored offset.</summary>
        public void EnableTransformEditing()
        {
            if(transformEditing)return;
            if(Mathf.Abs(transform.localToWorldMatrix.determinant)<.000001f)
                throw new InvalidOperationException("Grass needs a non-zero hierarchy scale before enabling transform editing.");
            placementWorldToReference=transform.worldToLocalMatrix;
            transformEditing=true;needsPrepare=true;
        }
        void OnValidate()
        {
            ViewDistance=Mathf.Max(1,ViewDistance);FadeStart=Mathf.Clamp(FadeStart,0,ViewDistance-.01f);
            DeformationPadding=Mathf.Max(0,DeformationPadding);
            // OnValidate can run during import. Rebuild render buffers on the next render callback on the main thread.
            needsPrepare=true;
        }

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
            prepared=null;ApproximateBufferBytes=0;needsPrepare=false;
            preparedTransform=PlacementTransform();
            if(mesh==null||material==null||data==null)return;
            batch=new Matrix4x4[InstancesPerBatch];planes=new Plane[6];properties=new MaterialPropertyBlock();
            prepared=new PreparedCell[data.Cells.Length];
            for(int c=0;c<prepared.Length;c++)
            {
                var source=data.Cells[c];var matrices=new Matrix4x4[source.Instances.Length];
                for(int i=0;i<matrices.Length;i++)
                {
                    var p=source.Instances[i];
                    matrices[i]=preparedTransform*Matrix4x4.TRS(p.Position,Quaternion.Euler(0,p.Yaw,0),p.Scale);
                }
                // Preserve source data/assets; transform the bounds and instances together for opt-in authoring.
                var bounds=GetWorldCellBounds(source);
                prepared[c]=new PreparedCell{Bounds=bounds,Matrices=matrices};
                ApproximateBufferBytes+=(long)matrices.Length*64L+24L;
            }
            ApproximateBufferBytes+=InstancesPerBatch*64L+6L*16L;
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(!isActiveAndEnabled||camera==null||!Eligible(camera))return;
            if(needsPrepare||preparedTransform!=PlacementTransform())Prepare();
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
                    Vector3 instancePosition=cell.Matrices[i].GetColumn(3);
                    if((instancePosition-position).sqrMagnitude>distanceSquared)continue;
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
        Matrix4x4 PlacementTransform()=>transformEditing?transform.localToWorldMatrix*placementWorldToReference:Matrix4x4.identity;
        static Bounds TransformBounds(Bounds bounds,Matrix4x4 matrix)
        {
            var e=bounds.extents;
            var extent=Abs(matrix.MultiplyVector(new Vector3(e.x,0,0)))+
                Abs(matrix.MultiplyVector(new Vector3(0,e.y,0)))+Abs(matrix.MultiplyVector(new Vector3(0,0,e.z)));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center),extent*2f);
        }
        static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
    }
}
