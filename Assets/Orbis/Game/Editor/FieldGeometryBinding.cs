using System;
using System.Collections.Generic;
using System.Linq;
using Orbis.Game.World;
using Orbis.M2;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    [Serializable]
    public sealed class FieldGeometryReport
    {
        public int AttachedTrunks, AttachedMeshes, ExtractedColliders, ExtractedClimbable;
        public string[] Unmatched = Array.Empty<string>();
    }

    /// <summary>Bind static collision to editable scenery; split it only on a disposable export copy.</summary>
    public static class FieldGeometryBinding
    {
        public const string ResidentRootName = "Field resident collision";

        public static FieldGeometryReport AttachResidentGeometry(FieldAuthoring field)
        {
            RequireField(field);
            var report = new FieldGeometryReport();
            var unmatched = new List<string>();
            var roots = field.Regions.Select(r => r.Root.transform).ToArray();
            var meshes = roots.SelectMany(t => t.GetComponentsInChildren<MeshFilter>(true))
                .Where(f => f.sharedMesh != null).GroupBy(f => f.sharedMesh).ToDictionary(g => g.Key, g => g.ToArray());
            var trees = roots.SelectMany(t => t.GetComponentsInChildren<LODGroup>(true))
                .Where(t => t.name.LastIndexOf(" / ", StringComparison.Ordinal) >= 0).ToArray();
            var core = field.Entry.World.transform;
            var trunkRoots = core.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Persistent nature collision").ToArray();
            foreach (var capsule in trunkRoots.SelectMany(t => t.GetComponentsInChildren<CapsuleCollider>(true)).ToArray())
            {
                ValidateCollisionOnly(capsule.transform);
                int.TryParse(capsule.name.Substring(capsule.name.LastIndexOf('/') + 1).Trim(), out int index);
                var matches = trees.Where(t => t.name.EndsWith(" / " + (index - 1), StringComparison.Ordinal) &&
                    Vector3.Distance(t.transform.position, capsule.transform.position) < .02f).ToArray();
                if (matches.Length != 1 || !ReparentExactly(capsule.transform, matches[0].transform))
                    unmatched.Add("Trunk: " + Path(capsule.transform));
                else report.AttachedTrunks++;
            }
            var landmarkRoots = core.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Persistent landmarks / ", StringComparison.Ordinal)).ToArray();
            foreach (var collider in landmarkRoots.SelectMany(t => t.GetComponentsInChildren<MeshCollider>(true)).ToArray())
            {
                ValidateCollisionOnly(collider.transform);
                MeshFilter[] candidates = collider.sharedMesh != null && meshes.TryGetValue(collider.sharedMesh, out var found) ? found : Array.Empty<MeshFilter>();
                var matches = candidates.Where(f => SameMatrix(f.transform.localToWorldMatrix, collider.transform.localToWorldMatrix)).ToArray();
                if (matches.Length != 1 || !ReparentExactly(collider.transform, matches[0].transform))
                    unmatched.Add("Mesh: " + Path(collider.transform));
                else report.AttachedMeshes++;
            }
            // Do not delete unmatched objects or guess their owner. They remain valid resident gameplay collision.
            report.Unmatched = unmatched.ToArray();
            return report;
        }

        public static FieldGeometryReport ExtractResidentGeometry(FieldAuthoring field)
        {
            RequireField(field);
            if (field.gameObject.scene.path == FieldAuthoring.ScenePath)
                throw new InvalidOperationException("Extract collision on an export copy, never on Assets/Scenes/Field.unity.");
            var roots = field.Regions.Select(r => r.Root.transform).ToArray();
            // Validate everything before mutation. Unrecognised gameplay must be made explicitly resident by the designer.
            foreach (var root in roots)
                foreach (var component in root.GetComponentsInChildren<Component>(true)) ValidateEnvironmentComponent(component, root);
            var colliders = roots.SelectMany(t => t.GetComponentsInChildren<Collider>(true)).ToArray();
            var climbable = roots.SelectMany(t => t.GetComponentsInChildren<ClimbableSurface>(true)).ToArray();
            if (field.gameObject.scene.GetRootGameObjects().Any(g => g.name == ResidentRootName))
                throw new InvalidOperationException("Export collision already exists. Start from a fresh source copy.");
            var report = new FieldGeometryReport();
            if (colliders.Length == 0 && climbable.Length == 0) return report;
            var resident = new GameObject(ResidentRootName);
            SceneManager.MoveGameObjectToScene(resident, field.gameObject.scene);
            var clones = new Dictionary<Transform, Transform>();
            foreach (var source in colliders)
            {
                var destination = CloneChain(source.transform, resident.transform, clones);
                var clone = destination.gameObject.AddComponent(source.GetType());
                EditorUtility.CopySerialized(source, clone);
                if (!SameMatrix(source.transform.localToWorldMatrix, destination.localToWorldMatrix))
                    throw new InvalidOperationException("Collider export changed its world matrix: " + Path(source.transform));
                report.ExtractedColliders++;
            }
            foreach (var source in climbable)
            {
                var destination = CloneChain(source.transform, resident.transform, clones);
                var clone = destination.gameObject.AddComponent<ClimbableSurface>();
                EditorUtility.CopySerialized(source, clone);
                report.ExtractedClimbable++;
            }
            foreach (var source in colliders) Object.DestroyImmediate(source);
            foreach (var source in climbable) Object.DestroyImmediate(source);
            return report;
        }

        static void RequireField(FieldAuthoring field)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Field authoring requires Edit Mode.");
            if (field == null || field.Entry == null || field.Entry.World == null || field.Regions == null ||
                field.Regions.Any(r => r == null || r.Root == null || r.Root.scene != field.gameObject.scene))
                throw new ArgumentException("Field needs its saved world and region roots in the same scene.");
            for (int i = 0; i < field.Regions.Length; i++)
                for (int j = i + 1; j < field.Regions.Length; j++)
                    if (field.Regions[i].Root.transform.IsChildOf(field.Regions[j].Root.transform) ||
                        field.Regions[j].Root.transform.IsChildOf(field.Regions[i].Root.transform))
                        throw new ArgumentException("Region roots must be separate non-overlapping hierarchies.");
        }

        static void ValidateCollisionOnly(Transform t)
        {
            foreach (var c in t.GetComponentsInChildren<Component>(true))
                if (c == null || !(c is Transform || c is Collider || c is ClimbableSurface))
                    throw new InvalidOperationException("Unexpected component on persistent collision: " + Path(t) + " / " + (c == null ? "Missing script" : c.GetType().Name));
        }

        static void ValidateEnvironmentComponent(Component c, Transform root)
        {
            if (c is Transform || c is MeshFilter || c is MeshRenderer || c is LODGroup || c is Collider ||
                c is ClimbableSurface || c is WorldGrassField || c is WorldLandmark || c is WorldWindmill) return;
            throw new InvalidOperationException("Environment export refuses " + (c == null ? "a missing script" : c.GetType().Name) +
                " in " + Path(c == null ? root : c.transform) + ". Keep gameplay/Rigidbody components in the resident world.");
        }

        static Transform CloneChain(Transform source, Transform resident, Dictionary<Transform, Transform> clones)
        {
            if (clones.TryGetValue(source, out var existing)) return existing;
            Transform parent = source.parent != null ? CloneChain(source.parent, resident, clones) : resident;
            var go = new GameObject(source.name) { layer = source.gameObject.layer, tag = source.gameObject.tag };
            var clone = go.transform;
            clone.SetParent(parent, false);
            clone.localPosition = source.localPosition;
            clone.localRotation = source.localRotation;
            clone.localScale = source.localScale;
            go.SetActive(source.gameObject.activeSelf);
            clones.Add(source, clone);
            return clone;
        }

        static bool ReparentExactly(Transform source, Transform destination)
        {
            Matrix4x4 world = source.localToWorldMatrix;
            Transform oldParent = source.parent;
            int index = source.GetSiblingIndex();
            Vector3 position = source.localPosition, scale = source.localScale;
            Quaternion rotation = source.localRotation;
            source.SetParent(destination, true);
            if (SameMatrix(world, source.localToWorldMatrix)) return true;
            // Unity cannot represent every sheared matrix as one TRS. Keep the old geometry instead of distorting it.
            source.SetParent(oldParent, false);
            source.localPosition = position; source.localRotation = rotation; source.localScale = scale;
            source.SetSiblingIndex(index);
            return false;
        }

        static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .002f) return false;
            return true;
        }
        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
