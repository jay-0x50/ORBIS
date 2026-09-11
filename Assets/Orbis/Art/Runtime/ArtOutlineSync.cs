using UnityEngine;

namespace Orbis.Art
{
    [DefaultExecutionOrder(950)]
    public sealed class ArtOutlineSync : MonoBehaviour
    {
        [SerializeField] private Renderer source,target;
        // Optional per-submesh values are serialized so authored outline copies survive scene reloads.
        // Legacy materials use scale 1, leave these arrays empty, and keep the shared 1.5px outline.
        [SerializeField] private float[] outlineWidths;
        [SerializeField] private Color[] outlineColors;
        private MaterialPropertyBlock block;

        public void Configure(Renderer original,Renderer copy)
        {
            source=original; target=copy; block=new MaterialPropertyBlock();
            var originals=source.sharedMaterials;
            var outlines=target.sharedMaterials;
            bool hasOverride=false;
            foreach(var material in originals)
                if(material!=null&&material.HasProperty("_OutlineScale")&&
                    !Mathf.Approximately(material.GetFloat("_OutlineScale"),1f)) hasOverride=true;
            outlineWidths=null; outlineColors=null;
            if(hasOverride)
            {
                outlineWidths=new float[outlines.Length]; outlineColors=new Color[outlines.Length];
                for(int i=0;i<outlines.Length;i++)
                {
                    var originalMaterial=i<originals.Length?originals[i]:null;
                    float scale=originalMaterial!=null&&originalMaterial.HasProperty("_OutlineScale")?
                        originalMaterial.GetFloat("_OutlineScale"):1f;
                    outlineWidths[i]=outlines[i].GetFloat("_OutlinePixels")*Mathf.Max(0f,scale);
                    outlineColors[i]=originalMaterial!=null&&originalMaterial.HasProperty("_OutlineColor")?
                        originalMaterial.GetColor("_OutlineColor"):outlines[i].GetColor("_OutlineColor");
                }
            }
            Synchronize();
        }

        private void LateUpdate()=>Synchronize();

        private void Synchronize()
        {
            if(source==null||target==null)return;
            if(block==null)block=new MaterialPropertyBlock(); // Scene reload restores references, not runtime property blocks.
            target.enabled=source.enabled;
            source.GetPropertyBlock(block);
            float threshold=block.GetFloat("_Threshold");
            block.Clear(); block.SetFloat("_Threshold",threshold); target.SetPropertyBlock(block);
            if(outlineWidths==null||outlineColors==null)return;
            for(int i=0;i<outlineWidths.Length;i++)
            {
                block.Clear(); block.SetFloat("_Threshold",threshold);
                block.SetFloat("_OutlinePixels",outlineWidths[i]);
                block.SetColor("_OutlineColor",outlineColors[i]);
                target.SetPropertyBlock(block,i);
            }
        }
    }
}