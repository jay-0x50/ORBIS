using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Art
{
    public static class ArtStyle
    {
        public static void ApplyToHierarchy(GameObject root,ArtAssetCatalog catalog,bool outlines=true)
        {
            var scope=root.GetComponent<ArtStyleScope>();
            if(scope==null) scope=root.AddComponent<ArtStyleScope>();
            scope.Apply(catalog,outlines);
        }
    }

    /// <summary>Owns converted materials and outline copies; source asset materials remain untouched.</summary>
    public sealed class ArtStyleScope : MonoBehaviour
    {
        private readonly Dictionary<Material,Material> converted=new Dictionary<Material,Material>();
        private readonly HashSet<Renderer> styled=new HashSet<Renderer>();
        public int StyledRendererCount=>styled.Count;

        public void Apply(ArtAssetCatalog catalog,bool outlines)
        {
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if(renderer==null||styled.Contains(renderer)||renderer.name.StartsWith("Art Outline")||
                    renderer is ParticleSystemRenderer||renderer is TrailRenderer||renderer is LineRenderer) continue;
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++) materials[i]=Convert(materials[i],catalog);
                renderer.sharedMaterials=materials; styled.Add(renderer);
                var outline=catalog.ExplorerToonMaterial!=null&&renderer.sharedMaterial!=null&&
                    renderer.sharedMaterial.shader==catalog.ExplorerToonMaterial.shader?
                    catalog.ExplorerOutlineMaterial:catalog.OutlineMaterial;
                if(outlines&&outline!=null&&renderer.sharedMaterial!=null&&renderer.sharedMaterial.renderQueue<2500) CreateOutline(renderer,outline);
            }
        }
        private Material Convert(Material original,ArtAssetCatalog catalog)
        {
            if(original==null) return catalog.DefaultToonMaterial;
            if(original.shader==catalog.DefaultToonMaterial.shader) return original;
            if(catalog.ExplorerToonMaterial!=null&&original.shader==catalog.ExplorerToonMaterial.shader) return original;
            if(converted.TryGetValue(original,out var result)) return result;
            result=new Material(catalog.DefaultToonMaterial){name="Toon / "+original.name};
            Color color=original.HasProperty("_BaseColor")?original.GetColor("_BaseColor"):
                original.HasProperty("_Color")?original.GetColor("_Color"):Color.white;
            Texture texture=original.HasProperty("_BaseMap")?original.GetTexture("_BaseMap"):
                original.HasProperty("_MainTex")?original.GetTexture("_MainTex"):null;
            result.SetColor("_BaseColor",color);
            if(texture!=null)
            {
                result.SetTexture("_BaseMap",texture);
                string property=original.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
                result.SetTextureScale("_BaseMap",original.GetTextureScale(property));
                result.SetTextureOffset("_BaseMap",original.GetTextureOffset(property));
            }
            converted.Add(original,result); return result;
        }
        private static void CreateOutline(Renderer source,Material outline)
        {
            Mesh mesh=null;
            if(source is SkinnedMeshRenderer skin) mesh=skin.sharedMesh;
            else if(source.TryGetComponent<MeshFilter>(out var filter)) mesh=filter.sharedMesh;
            if(mesh==null) return;
            // Some Kenney floors are double-sided zero-thickness planes. Extruding their reverse face
            // would cover the entire floor with black; only closed/volumetric meshes receive a hull.
            Vector3 dimensions=mesh.bounds.size;
            if(Mathf.Min(dimensions.x,Mathf.Min(dimensions.y,dimensions.z))<.0001f) return;
            var go=new GameObject("Art Outline / "+source.name); go.layer=source.gameObject.layer;
            go.transform.SetParent(source.transform,false);
            Renderer renderer;
            if(source is SkinnedMeshRenderer sourceSkin)
            {
                var copy=go.AddComponent<SkinnedMeshRenderer>(); copy.sharedMesh=mesh;
                copy.bones=sourceSkin.bones; copy.rootBone=sourceSkin.rootBone;
                copy.localBounds=sourceSkin.localBounds; copy.updateWhenOffscreen=sourceSkin.updateWhenOffscreen;
                renderer=copy;
            }
            else {go.AddComponent<MeshFilter>().sharedMesh=mesh; renderer=go.AddComponent<MeshRenderer>();}
            var materials=new Material[mesh.subMeshCount];
            for(int i=0;i<materials.Length;i++) materials[i]=outline;
            renderer.sharedMaterials=materials; renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false; renderer.enabled=source.enabled;
            go.AddComponent<ArtOutlineSync>().Configure(source,renderer);
        }
        private void OnDestroy()
        {
            foreach(var material in converted.Values)
                if(material!=null) {if(Application.isPlaying) Destroy(material);else DestroyImmediate(material);}
            converted.Clear();
        }
    }
}
