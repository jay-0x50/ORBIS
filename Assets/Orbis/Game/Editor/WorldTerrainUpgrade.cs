using System;
using System.Collections.Generic;
using System.Linq;
using Orbis.Game.World;
using Orbis.M4;
using UnityEditor;
using UnityEngine;

namespace Orbis.Game.Editor
{
    /// <summary>Step 1: reshape/repaint the four existing TerrainData assets without rebuilding scene content.</summary>
    public static class WorldTerrainUpgrade
    {
        public const float BaseY=-60f, HeightRange=320f, TileSize=1000f;
        public const float ProtectedPadRadius=65f;
        private const float RoadFlatEntrance=70f;
        private static float[] roadEdgeLengths,roadSectionOffsets,roadSectionLengths;

        public static void Apply(GameObject root)
        {
            if(root==null)throw new ArgumentNullException(nameof(root));
            Apply(new[]{root});
        }

        public static void Apply(IEnumerable<GameObject> roots)
        {
            if(roots==null)throw new ArgumentNullException(nameof(roots));
            Terrain[] tiles=roots.Where(x=>x!=null).SelectMany(x=>x.GetComponentsInChildren<Terrain>(true)).Distinct().ToArray();
            if(tiles.Length!=4)throw new InvalidOperationException("World Step 1 requires the four existing island Terrain tiles.");
            // Validate all inputs before writing any asset. Content objects, water volumes and transforms remain untouched.
            foreach(Terrain tile in tiles)
            {
                TerrainData data=tile.terrainData;
                if(data==null||!EditorUtility.IsPersistent(data)||data.heightmapResolution<513||data.terrainLayers.Length!=6||
                   (data.size-new Vector3(TileSize,HeightRange,TileSize)).sqrMagnitude>.001f||
                   Mathf.Abs(tile.transform.position.y-BaseY)>.001f)
                    throw new InvalidOperationException("Unexpected island Terrain contract: "+tile.name);
            }
            foreach(Terrain tile in tiles)
            {
                TerrainData data=tile.terrainData;int n=data.heightmapResolution;
                Vector3 origin=tile.transform.position;var heights=new float[n,n];
                for(int z=0;z<n;z++)for(int x=0;x<n;x++)
                    heights[z,x]=(HeightAt(origin.x+x*TileSize/(n-1),origin.z+z*TileSize/(n-1))-BaseY)/HeightRange;
                data.SetHeights(0,0,heights);Paint(data,origin.x,origin.z);
                // Re-snap existing instances to their new ground only; no species/count/XZ placement changes in Step 1.
                if(data.treeInstanceCount>0)data.SetTreeInstances(data.treeInstances,true);
                if(tile.TryGetComponent<TerrainCollider>(out var collider))collider.terrainData=data;
                tile.Flush();EditorUtility.SetDirty(data);EditorUtility.SetDirty(tile);
            }
            foreach(Terrain tile in tiles)
            {
                Vector3 p=tile.transform.position;
                Terrain At(float x,float z)=>tiles.FirstOrDefault(t=>Mathf.Abs(t.transform.position.x-x)<.01f&&Mathf.Abs(t.transform.position.z-z)<.01f);
                tile.SetNeighbors(At(p.x-TileSize,p.z),At(p.x,p.z+TileSize),At(p.x+TileSize,p.z),At(p.x,p.z-TileSize));
            }
            AssetDatabase.SaveAssets();
            Debug.Log("World Step 1: existing 2km terrain reshaped with connected highlands, gentle road shoulders and continuous biome paint; five 65m content pads preserved.");
        }

        public static float HeightAt(float x,float z)
        {
            // All numbers below are terrain art defaults; the five site elevations and water levels
            // are existing gameplay contracts. Broad connected shoulders support the road naturally.
            float warp=(Mathf.PerlinNoise(x*.0018f+10,z*.0018f+20)-.5f)*.105f;
            float radial=Mathf.Sqrt(x*x/(970f*970f)+z*z/(945f*945f))+warp;
            float coast=1-Smooth(.79f,1.055f,radial);
            float broad=(Mathf.PerlinNoise(x*.0021f+3,z*.0021f+7)-.45f)*19;
            float small=(Mathf.PerlinNoise(x*.008f+23,z*.008f+13)-.5f)*3.5f;
            float northShelf=58*Ellipse(x,z,0,470,730,325);
            float westSpine=40*Ellipse(x,z,-530,-50,260,570);
            float meadowBasin=-10*Ellipse(x,z,70,-170,360,270);
            float foothills=30*Ellipse(x,z,-280,-400,330,200)+14*Ellipse(x,z,460,70,260,320);
            float d=Vector2.Distance(new Vector2(x,z),new Vector2(-610,535));
            float volcano=140*Mathf.Exp(-d*d/36000)+28*Mathf.Exp(-Mathf.Pow((d-95)/25,2))-75*Mathf.Exp(-d*d/1750);
            float granite=145*Ellipse(x,z,-575,-585,205,225)+35*Ellipse(x,z,-330,-665,140,135);
            // Low-amplitude continuous ridge detail is subordinate to the main traversable mountain shoulders.
            float ridge=10*Ellipse(x,z,-490,-520,300,300)*Mathf.Pow(Mathf.Sin(x*.021f+Mathf.Sin(z*.016f)),2);
            float storm=65*Ellipse(x,z,490,540,260,290);
            float h=Mathf.Lerp(-30,24+broad+small+northShelf+westSpine+meadowBasin+foothills+volcano+granite+ridge+storm,coast);
            // Retain Teluna's cove and two satellite islands: the main continent is connected,
            // while the water region still reads as a navigable archipelago along its east coast.
            h=Mathf.Lerp(h,-13,Ellipse(x,z,760,-420,125,145)*.94f);
            h=Mathf.Max(h,-24+44*Ellipse(x,z,885,-285,78,50));
            h=Mathf.Max(h,-25+40*Ellipse(x,z,795,-620,72,58));
            float lake=Mathf.Sqrt(Mathf.Pow((x-230)/125,2)+Mathf.Pow((z-35)/90,2));
            if(lake<1.3f)h=Mathf.Lerp(11+6*Smooth(.68f,1.15f,lake),Mathf.Max(h,20),Smooth(.95f,1.3f,lake));
            float road=GradedRoadDistance(x,z,out float roadHeight);
            // 10m path, 105m feathered shoulders replace a narrow road cut. The northern and
            // western highlands above reduce the required cut/fill rather than merely hiding it.
            if(road<105)h=Mathf.Lerp(roadHeight,h,Smooth(5,105,road));
            float feather=142+(Mathf.PerlinNoise(x*.006f+41,z*.006f+67)-.5f)*24;
            for(int i=0;i<5;i++)
            {
                Vector3 site=WorldBiome.SiteCenter((M4RegionId)i);
                float distance=Vector2.Distance(new Vector2(site.x,site.z),new Vector2(x,z));
                if(distance<feather)h=Mathf.Lerp(site.y,h,Smooth(ProtectedPadRadius,feather,distance));
            }
            return Mathf.Clamp(h,-52,240);
        }

        private static float GradedRoadDistance(float x,float z,out float elevation)
        {
            Vector3[] route=IslandTerrainBuilder.RoutePoints();
            // The existing closed loop has five equally sampled Catmull-Rom sections. Keep its
            // XZ coordinates, but grade each section by metres travelled, not its curve parameter.
            // A 70m level entrance clears each protected 65m pad before ascent/descent begins.
            // This removes compressed climbs where the old parameter-based slope met a flat pad.
            int edgesPerSection=(route.Length-1)/5;
            if(roadEdgeLengths==null)
            {
                roadEdgeLengths=new float[route.Length-1];roadSectionOffsets=new float[route.Length-1];roadSectionLengths=new float[5];
                for(int i=0;i<route.Length-1;i++)
                {
                    int section=i/edgesPerSection;
                    roadSectionOffsets[i]=roadSectionLengths[section];
                    roadEdgeLengths[i]=Vector2.Distance(new Vector2(route[i].x,route[i].z),new Vector2(route[i+1].x,route[i+1].z));
                    roadSectionLengths[section]+=roadEdgeLengths[i];
                }
            }
            Vector2 p=new Vector2(x,z);float best=float.MaxValue,bestT=0;int bestEdge=0;
            for(int i=0;i<route.Length-1;i++)
            {
                Vector2 a=new Vector2(route[i].x,route[i].z),b=new Vector2(route[i+1].x,route[i+1].z),edge=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,edge)/edge.sqrMagnitude);float sq=(p-(a+edge*t)).sqrMagnitude;
                if(sq<best){best=sq;bestT=t;bestEdge=i;}
            }
            int nearestSection=bestEdge/edgesPerSection,first=nearestSection*edgesPerSection;
            float length=roadSectionLengths[nearestSection],distance=roadSectionOffsets[bestEdge]+roadEdgeLengths[bestEdge]*bestT;
            float progress=Smooth(RoadFlatEntrance,length-RoadFlatEntrance,distance);
            elevation=Mathf.Lerp(route[first].y,route[first+edgesPerSection].y,progress);
            return Mathf.Sqrt(best);
        }

        public static void Paint(TerrainData data,float originX,float originZ)
        {
            if(data==null||data.terrainLayers.Length!=6)throw new ArgumentException("Expected the six existing island terrain layers.",nameof(data));
            int n=data.alphamapResolution;var result=new float[n,n,6];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                float wx=originX+x*TileSize/(n-1),wz=originZ+z*TileSize/(n-1),height=HeightAt(wx,wz);
                float dx=(HeightAt(wx+3,wz)-HeightAt(wx-3,wz))/6,dz=(HeightAt(wx,wz+3)-HeightAt(wx,wz-3))/6;
                float slope=Mathf.Sqrt(dx*dx+dz*dz);WorldBiomeWeights b=WorldBiome.Sample(wx,wz);
                float beach=(1-Smooth(1,14,height))*(.82f+.18f*b.Teluna);
                float rock=Mathf.Clamp01(Smooth(.34f,.9f,slope)+b.Granite*Smooth(55,155,height)*.40f+b.Voltheim*.07f);
                // Geography and exposure matter more than a region's label: green lower volcanic
                // foothills fade gradually into high ash, while low water-region ground stays sandy.
                float volcanicCore=Ellipse(wx,wz,-610,510,265,290);
                float ash=b.Agnia*volcanicCore*(.20f+.58f*Smooth(65,150,height));
                float earth=.11f+b.Agnia*.11f+b.Granite*.06f+b.Teluna*.04f;
                float grass=Mathf.Max(.025f,1-rock-ash-beach*.92f);
                float road=IslandTerrainBuilder.RoadDistance(wx,wz,out _);
                float trail=(1-Smooth(3.5f,8,road))*(height>1?1:0);
                float total=grass+earth+rock+beach+ash;
                result[z,x,0]=grass/total*(1-trail);result[z,x,1]=earth/total*(1-trail);
                result[z,x,2]=rock/total*(1-trail);result[z,x,3]=beach/total*(1-trail);
                result[z,x,4]=ash/total*(1-trail);result[z,x,5]=trail;
            }
            data.SetAlphamaps(0,0,result);
        }
        private static float Ellipse(float x,float z,float cx,float cz,float rx,float rz)
            =>Mathf.Exp(-((x-cx)*(x-cx)/(rx*rx)+(z-cz)*(z-cz)/(rz*rz)));
        private static float Smooth(float a,float b,float value)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));
    }
}
