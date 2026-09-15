using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Orbis.Game.Editor;
using Orbis.Game.World;
using Orbis.M4;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class FieldGeometryBindingTests
    {
        Scene previous, fixture;
        readonly List<Object> transient = new List<Object>();
        FieldAuthoring field;
        Transform core, region;

        [SetUp] public void CreateFixture()
        {
            previous = SceneManager.GetActiveScene();
            // The Unity test runner may own an untitled scene. A preview scene isolates this fixture
            // without saving, replacing or attempting to add another normal scene beside that scene.
            fixture = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Synthetic field source");
            SceneManager.MoveGameObjectToScene(root, fixture);
            root.SetActive(false);
            field = root.AddComponent<FieldAuthoring>();
            core = Node("Core", root.transform);
            field.Entry = core.gameObject.AddComponent<GameSceneEntry>();
            field.Entry.World = core.gameObject.AddComponent<M4SceneBootstrap>();
            region = Node("Editable region", root.transform);
            field.Regions = new[] { new FieldAuthoringRegion { Id = M4RegionId.Zephyr, Root = region.gameObject } };
        }

        [TearDown] public void Cleanup()
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (fixture.IsValid()) EditorSceneManager.ClosePreviewScene(fixture);
            foreach (var item in transient) if (item != null) Object.DestroyImmediate(item);
            transient.Clear();
        }

        [Test] public void TrunkBindsWithoutMovingThenFollowsTreeAndUnmatchedCollisionStaysResident()
        {
            var tree = Node("CommonTree_3 / 0", region);
            tree.position = new Vector3(8, 1, 4);
            tree.rotation = Quaternion.Euler(0, 53, 0);
            tree.localScale = Vector3.one * 1.7f;
            tree.gameObject.AddComponent<LODGroup>();
            var persistent = Node("Persistent nature collision", core);
            var trunk = Node("Trunk / 1", persistent);
            trunk.position = tree.position;
            trunk.gameObject.layer = 8;
            var collider = trunk.gameObject.AddComponent<CapsuleCollider>();
            collider.radius = .3f; collider.height = 3.3f; collider.center = Vector3.up * 1.65f;
            var unmatched = Node("Trunk / 999", persistent);
            unmatched.position = new Vector3(100, 2, 60);
            unmatched.gameObject.AddComponent<CapsuleCollider>();
            Matrix4x4 original = trunk.localToWorldMatrix;

            var report = FieldGeometryBinding.AttachResidentGeometry(field);

            Assert.That(report.AttachedTrunks, Is.EqualTo(1));
            Assert.That(report.Unmatched, Has.Length.EqualTo(1));
            Assert.That(unmatched.parent, Is.EqualTo(persistent), "An unmatched object must never disappear.");
            Assert.That(trunk.parent, Is.EqualTo(tree));
            AssertMatrix(trunk.localToWorldMatrix, original);
            Assert.That(collider.gameObject.layer, Is.EqualTo(8));
            Assert.That(collider.radius, Is.EqualTo(.3f));
            Matrix4x4 beforeTree = tree.localToWorldMatrix;
            tree.position += new Vector3(5, 3, -2);
            tree.rotation = Quaternion.Euler(0, 101, 0);
            tree.localScale *= 1.4f;
            AssertMatrix(trunk.localToWorldMatrix, tree.localToWorldMatrix * beforeTree.inverse * original);
            Assert.That(FieldGeometryBinding.AttachResidentGeometry(field).AttachedTrunks, Is.Zero,
                "Repeated authoring must not add another collider.");
        }

        [Test] public void MeshAndClimbableExportPreservesEveryAncestorMatrixAndStripsOnlyEnvironmentCollision()
        {
            Mesh mesh = Tetrahedron();
            var structure = Node("Landmark", region);
            structure.position = new Vector3(11, 5, -7);
            structure.rotation = Quaternion.Euler(8, 37, 3);
            structure.localScale = new Vector3(2, 3, 1.5f);
            var visual = Node("LOD0", structure);
            visual.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var persistent = Node("Persistent landmarks / Zephyr", core);
            var original = Node("LOD0 / LOD0", persistent);
            original.position = visual.position;
            original.rotation = visual.rotation;
            original.localScale = visual.lossyScale;
            original.gameObject.layer = 8;
            var collider = original.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            Type climbableType = Type.GetType("Orbis.M2.ClimbableSurface, Orbis.M2.Runtime", true);
            original.gameObject.AddComponent(climbableType);
            var material = new PhysicsMaterial("Synthetic friction") { dynamicFriction = .17f, staticFriction = .31f };
            transient.Add(material); collider.sharedMaterial = material;

            var attach = FieldGeometryBinding.AttachResidentGeometry(field);
            Assert.That(attach.AttachedMeshes, Is.EqualTo(1));
            Assert.That(attach.Unmatched, Is.Empty);
            Assert.That(original.parent, Is.EqualTo(visual));
            // A rotated child under nonuniform ancestors cannot be flattened into a single equivalent TRS.
            region.localRotation = Quaternion.Euler(17, 31, 9);
            region.localScale = new Vector3(.8f, 1.7f, 1.2f);
            visual.localRotation = Quaternion.Euler(5, 19, 13);
            Matrix4x4 expected = original.localToWorldMatrix;
            var report = FieldGeometryBinding.ExtractResidentGeometry(field);

            Assert.That(report.ExtractedColliders, Is.EqualTo(1));
            Assert.That(report.ExtractedClimbable, Is.EqualTo(1));
            Assert.That(region.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(region.GetComponentsInChildren(climbableType, true), Is.Empty);
            Assert.That(visual.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(mesh));
            var resident = fixture.GetRootGameObjects().Single(g => g.name == FieldGeometryBinding.ResidentRootName);
            var exported = resident.GetComponentInChildren<MeshCollider>(true);
            Assert.That(exported, Is.Not.Null);
            AssertMatrix(exported.transform.localToWorldMatrix, expected);
            Assert.That(exported.sharedMesh, Is.EqualTo(mesh));
            Assert.That(exported.sharedMaterial, Is.EqualTo(material));
            Assert.That(exported.gameObject.layer, Is.EqualTo(8));
            Assert.That(exported.GetComponent(climbableType), Is.Not.Null);
            Assert.That(exported.gameObject.activeInHierarchy, Is.False, "The original inactive ancestor is preserved.");
            Object.DestroyImmediate(region.gameObject);
            Assert.That(exported, Is.Not.Null, "Unloading visual scenery must leave resident collision intact.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UnsafeEnvironmentComponentIsRejectedBeforeAnyCollisionMutation(bool rigidbody)
        {
            var node = Node("Unsupported gameplay", region);
            var collider = node.gameObject.AddComponent<BoxCollider>();
            if (rigidbody) node.gameObject.AddComponent<Rigidbody>();
            else node.gameObject.AddComponent<GameSceneEntry>();
            Assert.Throws<InvalidOperationException>(() => FieldGeometryBinding.ExtractResidentGeometry(field));
            Assert.That(collider, Is.Not.Null);
            Assert.That(region.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1));
            Assert.That(fixture.GetRootGameObjects().Any(g => g.name == FieldGeometryBinding.ResidentRootName), Is.False);
        }

        Mesh Tetrahedron()
        {
            var mesh = new Mesh { name = "Synthetic landmark" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            mesh.triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            mesh.RecalculateBounds(); transient.Add(mesh); return mesh;
        }
        static Transform Node(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            SceneManager.MoveGameObjectToScene(t.gameObject, parent.gameObject.scene);
            t.SetParent(parent, false); return t;
        }
        static void AssertMatrix(Matrix4x4 actual, Matrix4x4 expected)
        {
            for (int i = 0; i < 16; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(.002f), "Matrix element " + i);
        }
    }
}
