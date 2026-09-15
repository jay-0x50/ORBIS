using System;
using System.IO;
using System.Linq;
using Orbis.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    public static class BossPipelinePreview
    {
        [Serializable] sealed class Evidence
        {
            public string model, avatar, shader, graphics;
            public Vector3 camera, target, normalizedGeometryMin, normalizedGeometryMax;
            public float fieldOfView;
            public bool outline;
            public string note="Actual Unity URP still of the imported Generic rest rig. Fixed camera/lighting/atlas; outline alone toggled. No gameplay, final motion or performance acceptance.";
        }
        public static void CaptureReadyPair()
        {
            foreach(string name in new[]{"FireBoss","WaterBoss"}) Capture(name);
        }
        public static void ImportRemainingAndCaptureAll()
        {
            foreach(string name in new[]{"RockBoss","WindBoss","LightningBoss"})
                BossPipelineImport.Import(name,"Import01");
            foreach(string name in new[]{"FireBoss","WaterBoss","RockBoss","WindBoss","LightningBoss"}) Capture(name);
        }
        static void Capture(string name)
        {
            string folder="Assets/Orbis/Game/Characters/BossCandidates/Import01/"+name;
            string output="TestResults/CharacterPipeline/UnityBossPreview/Rest02/"+name;
            if(Directory.Exists(output)) throw new IOException("Preserve previous rest evidence.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var host=new GameObject("Transient Generic rest");
            var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/"+name+".prefab"),host.transform);
            var animator=model.GetComponent<Animator>();
            if(animator==null || animator.avatar==null || !animator.avatar.isValid || animator.avatar.isHuman)
                throw new InvalidOperationException("Reviewed Generic Avatar required.");
            animator.enabled=false; // Preserve the imported rest hierarchy; no nonexistent Idle state is sampled.
            ArtCharacterRoster.NormalizeVisibleModelHeight(host,1.8f,Vector3.zero);
            Bounds geometry=GeometryBounds(host);
            if(Mathf.Abs(geometry.size.y-1.8f)>.02f)
                throw new InvalidOperationException("Rest capture scale disagrees with actual normalized geometry: "+geometry.size);
            var catalog=Resources.Load<ArtAssetCatalog>("Art/Catalog");
            ArtStyle.ApplyToHierarchy(host,catalog,true);
            foreach(var skin in host.GetComponentsInChildren<SkinnedMeshRenderer>())
                skin.forceMatrixRecalculationPerRender=true;
            var outlines=host.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("Art Outline",StringComparison.Ordinal)).ToArray();
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.28f,.30f,.34f);
            RenderSettings.fog=false;
            var key=new GameObject("Rest key").AddComponent<Light>();
            key.type=LightType.Directional; key.intensity=1.1f; key.color=Color.white;
            key.shadows=LightShadows.Soft; key.transform.rotation=Quaternion.Euler(42,-35,0); RenderSettings.sun=key;
            var camera=new GameObject("Rest camera").AddComponent<Camera>();
            camera.enabled=false; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.14f,.16f,.20f); camera.nearClipPlane=.03f; camera.farClipPlane=40f;
            camera.allowHDR=true; camera.allowMSAA=true; camera.useOcclusionCulling=false;
            camera.fieldOfView=30f; camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            // A fitted bounding sphere avoids clipping the water boss's much longer tail.
            float distance=geometry.extents.magnitude/Mathf.Sin(camera.fieldOfView*Mathf.Deg2Rad*.5f)*1.08f;
            var angles=new[]{("Front",new Vector3(0,.08f,1).normalized),("ThreeQuarter",new Vector3(.65f,.27f,1).normalized)};
            Directory.CreateDirectory(output);
            try
            {
                foreach(var angle in angles) foreach(bool outline in new[]{false,true})
                {
                    foreach(var renderer in outlines) renderer.enabled=outline;
                    Vector3 target=geometry.center;
                    camera.transform.SetPositionAndRotation(target+angle.Item2*distance,Quaternion.LookRotation(-angle.Item2));
                    string path=output+"/"+angle.Item1+(outline?"_OutlineOn":"_OutlineOff");
                    CharacterPipelinePreview.Render(camera,path+".png");
                    File.WriteAllText(path+".json",JsonUtility.ToJson(new Evidence
                    {
                        model=folder+"/"+name+".fbx",avatar=AssetDatabase.GetAssetPath(animator.avatar),
                        shader=AssetDatabase.GetAssetPath(catalog.ExplorerToonMaterial.shader),graphics=SystemInfo.graphicsDeviceName,
                        camera=camera.transform.position,target=target,fieldOfView=camera.fieldOfView,
                        normalizedGeometryMin=geometry.min,normalizedGeometryMax=geometry.max,outline=outline
                    },true));
                }
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(key.gameObject); Object.DestroyImmediate(camera.gameObject); }
            Debug.Log("ORBIS_GENERIC_REST_CAPTURE "+output);
        }
        static Bounds GeometryBounds(GameObject root)
        {
            var bounds=new Bounds(); bool first=true; var baked=new Mesh();
            try
            {
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // Match the normalization helper: imported FBX renderers have a non-unit scale.
                    // Mixing the default BakeMesh(false) with TransformPoint put the former camera
                    // hundreds of metres away. Rest01 blank images are invalid, retained evidence.
                    baked.Clear(); skin.BakeMesh(baked,true);
                    foreach(var point in baked.vertices)
                    {
                        Vector3 world=skin.transform.TransformPoint(point);
                        if(first) { bounds=new Bounds(world,Vector3.zero); first=false; } else bounds.Encapsulate(world);
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            if(first) throw new InvalidOperationException("No skinned rest geometry.");
            return bounds;
        }
    }
}
