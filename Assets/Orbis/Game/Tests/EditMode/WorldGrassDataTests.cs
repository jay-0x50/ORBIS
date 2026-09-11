using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Game.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class WorldGrassDataTests
    {
        [Test] public void SampledPlacementsSurviveBinaryAssetRoundTripAcrossNegativeCells()
        {
            string path="Assets/Orbis/Game/Tests/GrassRoundTrip_"+Guid.NewGuid().ToString("N")+".asset";
            WorldGrassData data=ScriptableObject.CreateInstance<WorldGrassData>();
            try
            {
                var placements=new List<WorldGrassPlacement>();
                for(int i=0;i<640;i++)placements.Add(new WorldGrassPlacement
                {
                    Position=new Vector3(-95+(i%32)*6.05f,7+Mathf.Sin(i*.1f)*3,-62+(i/32)*7.1f),
                    Yaw=(i*37)%360,Scale=new Vector3(.5f+(i%3)*.1f,.7f+(i%7)*.05f,.6f)
                });
                data.SetInstances(placements,32);AssetDatabase.CreateAsset(data,path);AssetDatabase.SaveAssets();
                // Large authored populations must stay compact even in this project's Force Text serialization mode.
                byte[] bytes=File.ReadAllBytes(path);
                Assert.That(System.Text.Encoding.ASCII.GetString(bytes,0,Math.Min(bytes.Length,5)),Is.Not.EqualTo("%YAML"));
                Resources.UnloadAsset(data);data=AssetDatabase.LoadAssetAtPath<WorldGrassData>(path);
                Assert.That(data.InstanceCount,Is.EqualTo(placements.Count));
                Assert.That(data.Cells.Any(c=>c.Coordinate.x<0&&c.Coordinate.y<0),Is.True);
                var restored=data.Cells.SelectMany(c=>c.Instances).OrderBy(p=>p.Position.z).ThenBy(p=>p.Position.x).ToArray();
                var expected=placements.OrderBy(p=>p.Position.z).ThenBy(p=>p.Position.x).ToArray();
                for(int i=0;i<expected.Length;i++)
                {
                    Assert.That(restored[i].Position,Is.EqualTo(expected[i].Position));
                    Assert.That(restored[i].Scale,Is.EqualTo(expected[i].Scale));
                    Assert.That(restored[i].Yaw,Is.EqualTo(expected[i].Yaw));
                }
                foreach(var cell in data.Cells)
                    foreach(var placement in cell.Instances)
                        Assert.That(cell.PositionBounds.SqrDistance(placement.Position),Is.LessThan(.000001f));
            }
            finally
            {
                // Only this test's exact GUID-named asset is removed; no content folders or authored assets are touched.
                if(AssetDatabase.LoadMainAssetAtPath(path)!=null)AssetDatabase.DeleteAsset(path);
                else if(data!=null)Object.DestroyImmediate(data);
            }
        }
    }
}
