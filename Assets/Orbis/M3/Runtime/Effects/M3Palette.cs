using Orbis.M1;
using UnityEngine;

namespace Orbis.M3
{
    /// <summary>Design 03's exact sRGB colors. Convert to linear only at the shader/VFX boundary.</summary>
    public static class M3Palette
    {
        public static Color Primary(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return new Color32(0xFF, 0x5A, 0x1F, 0xFF);
                case ElementType.Water: return new Color32(0x1F, 0xA2, 0xFF, 0xFF);
                case ElementType.Wind: return new Color32(0x6C, 0xFF, 0xB8, 0xFF);
                case ElementType.Rock: return new Color32(0xD4, 0xA9, 0x3B, 0xFF);
                case ElementType.Lightning: return new Color32(0xB2, 0x6C, 0xFF, 0xFF);
                default: return Color.white;
            }
        }

        public static Color Secondary(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return new Color32(0xFF, 0xD1, 0x66, 0xFF);
                case ElementType.Water: return new Color32(0xA7, 0xE8, 0xFF, 0xFF);
                case ElementType.Wind: return new Color32(0xE8, 0xFF, 0xF3, 0xFF);
                case ElementType.Rock: return new Color32(0x7A, 0x5C, 0x2E, 0xFF);
                case ElementType.Lightning: return new Color32(0xF0, 0xD9, 0xFF, 0xFF);
                default: return Color.white;
            }
        }

        public static void Set(MaterialPropertyBlock block, ElementType element, float progress = 0f)
        {
            block.SetColor("_PrimaryColor", Primary(element));
            block.SetColor("_SecondaryColor", Secondary(element));
            block.SetFloat("_Progress", Mathf.Clamp01(progress));
        }
    }
}
