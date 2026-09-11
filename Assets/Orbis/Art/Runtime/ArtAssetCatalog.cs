using System;
using Orbis.M1;
using UnityEngine;

namespace Orbis.Art
{
    [Serializable]
    public sealed class ArtCharacterAsset
    {
        public string DisplayName;
        public ElementType Element;
        public GameObject Prefab;
        public RuntimeAnimatorController Controller;
        public Avatar Avatar;
        public GameObject Weapon;
        public string WeaponBoneName;
        public Vector3 WeaponLocalPosition;
        public Vector3 WeaponLocalEuler;
        public Vector3 WeaponLocalScale=Vector3.one;
        public Vector3 TrailTipLocalPosition;
    }

    [Serializable]
    public sealed class ArtExplorerAsset
    {
        // Stable gameplay identity; Art does not depend on the later protagonist/save assembly.
        public string SourceId;
        public ArtCharacterAsset Character;
    }
    [Serializable]
    public sealed class ArtEnvironmentAsset
    {
        public string Key;
        public GameObject Prefab;
    }

    [Serializable]
    public sealed class ArtIconAsset
    {
        public string Key;
        public Texture2D Texture;
    }

    /// <summary>References licensed source assets and generated material/animation adapters.</summary>
    [CreateAssetMenu(menuName="Orbis/Art/Asset Catalog")]
    public sealed class ArtAssetCatalog : ScriptableObject
    {
        public ArtCharacterAsset[] Characters=Array.Empty<ArtCharacterAsset>();
        public ArtExplorerAsset[] Explorers=Array.Empty<ArtExplorerAsset>();
        public ArtEnvironmentAsset[] Environment=Array.Empty<ArtEnvironmentAsset>();
        public ArtIconAsset[] Icons=Array.Empty<ArtIconAsset>();
        public Material DefaultToonMaterial;
        public Material OutlineMaterial;
        // Optional explorer look. Companions/world keep the original style until explicitly authored.
        public Material ExplorerToonMaterial;
        public Material ExplorerOutlineMaterial;
        public Material WaterMaterial;
        public string AnimationSource="KayKit Character Animations 1.1 / CC0";
        public bool UsesMixamo;

        public ArtCharacterAsset Explorer(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId) || Explorers == null) return null;
            foreach (var entry in Explorers)
                if (entry != null && string.Equals(entry.SourceId,sourceId,StringComparison.Ordinal)) return entry.Character;
            return null;
        }        public GameObject Model(string key)
        {
            foreach(var entry in Environment) if(entry.Key==key) return entry.Prefab;
            throw new InvalidOperationException("Art model is not mapped: "+key);
        }
        public Texture2D Icon(string key)
        {
            foreach(var entry in Icons) if(entry.Key==key) return entry.Texture;
            return null;
        }
    }
}
