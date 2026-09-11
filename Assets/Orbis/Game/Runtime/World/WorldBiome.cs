using System;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Game.World
{
    /// <summary>Continuous physical-region weights; does not change encounter/HUD region routing.</summary>
    public readonly struct WorldBiomeWeights
    {
        public readonly float Agnia, Teluna, Zephyr, Granite, Voltheim;
        public WorldBiomeWeights(float agnia,float teluna,float zephyr,float granite,float voltheim)
        {Agnia=agnia;Teluna=teluna;Zephyr=zephyr;Granite=granite;Voltheim=voltheim;}
        public float this[M4RegionId region] => region switch
        {
            M4RegionId.Agnia=>Agnia,M4RegionId.Teluna=>Teluna,M4RegionId.Zephyr=>Zephyr,
            M4RegionId.Granite=>Granite,M4RegionId.Voltheim=>Voltheim,
            _=>throw new ArgumentOutOfRangeException(nameof(region))
        };
        public float Sum=>Agnia+Teluna+Zephyr+Granite+Voltheim;
    }

    public static class WorldBiome
    {
        // Art defaults, absent from the planning documents. For two equally spaced sites,
        // temperature 80m produces an approximately 176m-wide 10%-90% transition.
        // A 28m low-frequency domain warp follows irregular foothills rather than straight bisectors.
        public const float TransitionTemperature=80f, BoundaryWarp=28f;
        private static readonly Vector3[] sites={new Vector3(-520,90,340),new Vector3(520,12,-380),
            new Vector3(0,25,-180),new Vector3(-450,115,-450),new Vector3(450,90,430)};

        public static Vector3 SiteCenter(M4RegionId region)
        {
            int index=(int)region;
            if(index<0||index>=sites.Length)throw new ArgumentOutOfRangeException(nameof(region));
            return sites[index];
        }

        public static WorldBiomeWeights Sample(float x,float z)
        {
            // Fixed offsets are the world seed. No per-tile randomness or allocation occurs here.
            float wx=x+(Mathf.PerlinNoise(x*.0023f+73.1f,z*.0023f+31.7f)-.5f)*BoundaryWarp*2;
            float wz=z+(Mathf.PerlinNoise(x*.0023f+19.4f,z*.0023f+89.2f)-.5f)*BoundaryWarp*2;
            float a=Distance(wx,wz,sites[0]),t=Distance(wx,wz,sites[1]),w=Distance(wx,wz,sites[2]);
            float g=Distance(wx,wz,sites[3]),v=Distance(wx,wz,sites[4]);
            float nearest=Mathf.Min(Mathf.Min(a,t),Mathf.Min(w,Mathf.Min(g,v)));
            // Subtracting the minimum prevents numerical underflow even outside the island.
            a=Mathf.Exp((nearest-a)/TransitionTemperature);t=Mathf.Exp((nearest-t)/TransitionTemperature);
            w=Mathf.Exp((nearest-w)/TransitionTemperature);g=Mathf.Exp((nearest-g)/TransitionTemperature);
            v=Mathf.Exp((nearest-v)/TransitionTemperature);
            float sum=a+t+w+g+v;
            return new WorldBiomeWeights(a/sum,t/sum,w/sum,g/sum,v/sum);
        }
        private static float Distance(float x,float z,Vector3 site)
            =>Mathf.Sqrt((x-site.x)*(x-site.x)+(z-site.z)*(z-site.z));
    }
}
