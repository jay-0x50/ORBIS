using NUnit.Framework;
using Orbis.Game.Editor;
using Orbis.Game.World;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Game.Tests
{
    public sealed class WorldBiomeTests
    {
        [Test]
        public void FiveRegionsRemainDistinctAndEverySampleIsANormalizedMixture()
        {
            for(int i=0;i<5;i++)
            {
                var id=(M4RegionId)i;Vector3 site=WorldBiome.SiteCenter(id);
                Assert.That(site,Is.EqualTo(IslandTerrainBuilder.SiteCenter(id)),"Existing content coordinate contract");
                Assert.That(WorldBiome.Sample(site.x,site.z)[id],Is.GreaterThan(.97f),id+" lost its own region core");
            }
            for(int z=-1100;z<=1100;z+=55)for(int x=-1100;x<=1100;x+=55)
            {
                WorldBiomeWeights weights=WorldBiome.Sample(x,z);
                Assert.That(weights.Sum,Is.EqualTo(1).Within(.00001f));
                for(int i=0;i<5;i++)Assert.That(weights[(M4RegionId)i],Is.InRange(0f,1f));
            }
        }

        [Test]
        public void RegionBordersBlendContinuouslyAcrossEveryPairOfSites()
        {
            // Walk across the former nearest-region boundaries in one-metre increments.
            // Any enum-style switch would produce a 1.0 jump rather than this small gradient.
            for(int a=0;a<5;a++)for(int b=a+1;b<5;b++)
            {
                Vector3 first=WorldBiome.SiteCenter((M4RegionId)a),last=WorldBiome.SiteCenter((M4RegionId)b);
                Vector3 middle=(first+last)*.5f;Vector3 direction=new Vector3(last.x-first.x,0,last.z-first.z).normalized;
                WorldBiomeWeights previous=WorldBiome.Sample(middle.x-direction.x*170,middle.z-direction.z*170);
                for(int step=-169;step<=170;step++)
                {
                    Vector3 p=middle+direction*step;WorldBiomeWeights next=WorldBiome.Sample(p.x,p.z);
                    for(int i=0;i<5;i++)Assert.That(Mathf.Abs(next[(M4RegionId)i]-previous[(M4RegionId)i]),Is.LessThan(.025f));
                    previous=next;
                }
            }
        }

        [Test]
        public void ExistingPadsFreshwaterAndDryConnectingRoadRemainTraversable()
        {
            for(int i=0;i<5;i++)
            {
                Vector3 site=WorldBiome.SiteCenter((M4RegionId)i);
                Assert.That(WorldTerrainUpgrade.HeightAt(site.x,site.z),Is.EqualTo(site.y).Within(.0001f));
                for(int k=0;k<24;k++)
                {
                    float angle=k*Mathf.PI/12;
                    Assert.That(WorldTerrainUpgrade.HeightAt(site.x+Mathf.Cos(angle)*64.9f,site.z+Mathf.Sin(angle)*64.9f),
                        Is.EqualTo(site.y).Within(.0001f),"Protected content pad changed");
                }
            }
            Assert.That(WorldTerrainUpgrade.HeightAt(230,35),Is.LessThan(IslandTerrainBuilder.LakeLevel-3));
            foreach(float x in new[]{-1000f,1000f})foreach(float z in new[]{-1000f,1000f})
                Assert.That(WorldTerrainUpgrade.HeightAt(x,z),Is.LessThan(IslandTerrainBuilder.SeaLevel));
            Vector3[] route=IslandTerrainBuilder.RoutePoints();Vector3 previous=default;bool hasPrevious=false;float maximumGrade=0;
            for(int segment=0;segment<route.Length-1;segment++)
            {
                // One-metre samples catch short steep approaches outside a protected content pad.
                int steps=Mathf.CeilToInt(Vector2.Distance(new Vector2(route[segment].x,route[segment].z),
                    new Vector2(route[segment+1].x,route[segment+1].z)));
                for(int step=0;step<=steps;step++)
                {
                    Vector3 p=Vector3.Lerp(route[segment],route[segment+1],step/(float)steps);
                    p.y=WorldTerrainUpgrade.HeightAt(p.x,p.z);
                    Assert.That(p.y,Is.GreaterThan(5),"The five-site land route became flooded");
                    if(hasPrevious)
                    {
                        float distance=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(previous.x,previous.z));
                        if(distance>.01f)
                        {
                            float grade=Mathf.Abs(p.y-previous.y)/distance;maximumGrade=Mathf.Max(maximumGrade,grade);
                            Assert.That(grade,Is.LessThan(.45f),$"Road grade requires a cliff climb: edge {segment}, {previous:F3} -> {p:F3}");
                        }
                    }
                    previous=p;hasPrevious=true;
                }
            }
            TestContext.WriteLine($"Maximum one-metre road grade: {maximumGrade:F6}");
        }
    }
}
