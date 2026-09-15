using System;
using System.Collections.Generic;
using NUnit.Framework;
using Orbis.Game.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class FieldGrassAuthoringTests
    {
        [Test] public void OptInKeepsInitialPlacementThenFollowsParentAndPreservesSharedSourceData()
        {
            WithGrass((root, field, data) =>
            {
                Matrix4x4 originalRoot = field.transform.localToWorldMatrix;
                Vector3 originalPosition = data.Cells[0].Instances[0].Position;
                field.EnableTransformEditing();
                Assert.That(field.TransformEditingEnabled, Is.True);
                AssertVector(field.PlacementToWorldMatrix.MultiplyPoint3x4(originalPosition), originalPosition);
                root.transform.position += new Vector3(70, -2, 11);
                root.transform.rotation = Quaternion.Euler(9, 42, 8);
                root.transform.localScale = new Vector3(1.8f, .7f, 1.2f);
                field.transform.localRotation = Quaternion.Euler(15, 25, 11);
                Vector3 expected = (field.transform.localToWorldMatrix * originalRoot.inverse).MultiplyPoint3x4(originalPosition);
                AssertVector(field.PlacementToWorldMatrix.MultiplyPoint3x4(originalPosition), expected);
                field.EnableTransformEditing();
                AssertVector(field.PlacementToWorldMatrix.MultiplyPoint3x4(originalPosition), expected,
                    "A repeated conversion must not rebase already moved grass.");
                AssertVector(data.Cells[0].Instances[0].Position, originalPosition, "Shared binary placement data must stay unchanged.");
                var bound = field.GetWorldCellBounds(data.Cells[0]);
                Assert.That(bound.SqrDistance(expected), Is.LessThan(.00001f));
                // Bounds must cover transformed mesh corners under the same nonuniform/sheared parent chain.
                var placement = data.Cells[0].Instances[0];
                Matrix4x4 instance = field.PlacementToWorldMatrix * Matrix4x4.TRS(placement.Position, Quaternion.Euler(0, placement.Yaw, 0), placement.Scale);
                foreach (var vertex in field.InstanceMesh.vertices)
                    Assert.That(bound.SqrDistance(instance.MultiplyPoint3x4(vertex)), Is.LessThan(.00001f));
            });
        }

        [Test] public void ExistingWorldSpaceGrassIgnoresHierarchyTransformsUntilExplicitlyEnabled()
        {
            WithGrass((root, field, data) =>
            {
                var original = data.Cells[0].Instances[0].Position;
                Bounds before = field.GetWorldCellBounds(data.Cells[0]);
                root.transform.position += new Vector3(200, 8, -30);
                root.transform.localScale *= 2;
                Assert.That(field.TransformEditingEnabled, Is.False);
                AssertVector(field.PlacementToWorldMatrix.MultiplyPoint3x4(original), original);
                AssertVector(field.GetWorldCellBounds(data.Cells[0]).center, before.center);
                AssertVector(field.GetWorldCellBounds(data.Cells[0]).size, before.size);
                root.transform.localScale = new Vector3(0, 1, 1);
                Assert.Throws<InvalidOperationException>(() => field.EnableTransformEditing());
                Assert.That(field.TransformEditingEnabled, Is.False);
            });
        }

        static void WithGrass(Action<GameObject, WorldGrassField, WorldGrassData> inspect)
        {
            var root = new GameObject("Grass authoring fixture"); root.SetActive(false);
            var child = new GameObject("Instanced meadow"); child.transform.SetParent(root.transform, false);
            root.transform.position = new Vector3(12, 3, -6);
            root.transform.rotation = Quaternion.Euler(0, 13, 0);
            child.transform.localPosition = new Vector3(2, 0, 4);
            Mesh mesh = null; Material material = null; WorldGrassData data = null;
            try
            {
                mesh = new Mesh { name = "Synthetic grass" };
                mesh.vertices = new[] { new Vector3(-.3f, 0, 0), new Vector3(.3f, 0, 0), new Vector3(0, 1, 0) };
                mesh.triangles = new[] { 0, 1, 2 }; mesh.RecalculateBounds();
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit"); Assert.That(shader, Is.Not.Null);
                material = new Material(shader) { enableInstancing = true };
                data = ScriptableObject.CreateInstance<WorldGrassData>();
                data.SetInstances(new List<WorldGrassPlacement> { new WorldGrassPlacement
                    { Position = new Vector3(6, 2, 10), Yaw = 39, Scale = new Vector3(1.4f, .8f, .9f) } });
                var field = child.AddComponent<WorldGrassField>(); field.Configure(mesh, material, data);
                inspect(root, field, data);
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (material != null) Object.DestroyImmediate(material);
                if (data != null) Object.DestroyImmediate(data);
            }
        }
        static void AssertVector(Vector3 actual, Vector3 expected, string message = null) =>
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.002f), message);
    }
}
