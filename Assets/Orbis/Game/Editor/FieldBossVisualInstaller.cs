using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Orbis.Art;
using Orbis.Game.Animation;
using Orbis.M0;
using Orbis.M1;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>
    /// Review first, then commit the exact reviewed scene/dependencies. Never regenerates terrain,
    /// changes gameplay colliders, or exports Field automatically. Pending source until installed by the owner.
    /// </summary>
    public static class FieldBossVisualInstaller
    {
        const string Field = "Assets/Scenes/Field.unity";
        const string Audit = "TestResults/CharacterPipeline/FieldIntegration/Before/BossBindings.json";
        const string Runtime = "Assets/Orbis/Game/Characters/Bosses/Original01/";
        const string VisualPrefix = "Original Boss Visual / ";
        const int WitnessFormatVersion = 2;
        static readonly string[] Labels = { "FireBoss", "WaterBoss", "WindBoss", "RockBoss", "LightningBoss" };
        // Presentation defaults, not combat tuning: close to the previous 2.6m guardian.
        // Height is an upper bound; horizontal extent is limited to 90% of the existing telegraph radius.
        static readonly float[] Heights = { 2.6f, 2.4f, 2.8f, 3.1f, 2.8f };

        [Serializable] sealed class Origin { public string mode; public float source_reference_z, export_mesh_bounds_min_z; }
        [Serializable] sealed class Motion { public string label, restFbxSha256, motionFbxSha256; public bool motionFbxRoundtripPass; public Origin originContract; }
        [Serializable] sealed class Witness
        {
            public string region, rootId, rootName, rootTag, actorId, dummyId, parentId, authoredJson;
            public int layer, staticFlags; public bool active;
            public Vector3 position, localPosition, localScale; public Quaternion rotation, localRotation;
            public float[] worldMatrix;
            public string[] componentIds, componentJson, coreIds, coreJson;
            public string[] colliderIds, colliderJson;
        }
        [Serializable] sealed class Entry
        {
            public string region, label, prefab, profile, prefabDependencyHash, profileDependencyHash, sourceManifestSha256;
            public string outlineMaterial, outlineDependencyHash;
            public int additionalOutlineRenderers, additionalOutlineSubmeshDraws;
            public string originMode, sizingNote;
            public float desiredHeight, scale, measuredArenaRadius, attackRadius, sourceHorizontalRadius, horizontalRadius;
            public Vector3 localPosition; public Quaternion localRotation;
            public Bounds sourceBounds, worldVisualBounds;
            public Witness before, after;
            public CameraSpec[] cameras;
        }
        [Serializable] sealed class CameraSpec
        {
            public string name; public Vector3 position; public Quaternion rotation;
            public float fieldOfView = 35f, aspect = 1.5f, near = .03f, far = 6500f;
            public int width = 1536, height = 1024;
        }
        [Serializable] sealed class Report
        {
            public int witnessFormatVersion;
            public string status, sourceScene = Field, sourceSceneSha256, auditSha256, savedSceneSha256;
            public string note = "Visual children only. Existing actor/root/core/colliders preserved. Field export is a separate final operation.";
            public Entry[] bosses;
        }
        sealed class Work
        {
            public M4AuthoredRegion authored; public GameObject root, prefab; public BossMotionProfile profile;
            public Transform core; public GameObject[] remove; public Entry entry; public Material outline;
        }

        public static void ReviewRequested() => Run(false);
        public static void CommitRequested() => Run(true);

        static void Run(bool commit)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                FieldSceneAuthoring.IsExporting || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Requires idle Edit Mode without export or Animation Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scenes before running the installer.");
            string run = Arg("-bossFieldRun");
            if (string.IsNullOrEmpty(run) || !run.All(c => char.IsLetterOrDigit(c) || c == '_'))
                throw new ArgumentException("Supply a fresh -bossFieldRun using letters/digits/underscore.");
            string folder = "TestResults/CharacterPipeline/FieldIntegration/" + run;
            string reviewPath = folder + "/Review.json", commitPath = folder + "/Commit.json";
            if (!commit && Directory.Exists(folder)) throw new IOException("Preserve the previous review; use a fresh run name.");
            if (commit && !File.Exists(reviewPath)) throw new IOException("Review this exact run before committing.");
            var reviewed = commit ? JsonUtility.FromJson<Report>(File.ReadAllText(reviewPath)) : null;
            var completed = commit && File.Exists(commitPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(commitPath)) : null;
            if (commit && (reviewed.witnessFormatVersion != WitnessFormatVersion ||
                (completed != null && completed.witnessFormatVersion != WitnessFormatVersion)))
                throw new InvalidOperationException("The witness format changed. Preserve the old evidence and make a fresh screenshot review before committing.");
            var audit = JsonUtility.FromJson<FieldBossBindingAudit.Report>(File.ReadAllText(Audit));
            if (audit.scene != Field || audit.bosses == null || audit.bosses.Length != 5)
                throw new InvalidOperationException("Expected the real five-boss Field audit.");
            string originalHash = Hash(Field);
            if (commit && Hash(Audit) != reviewed.auditSha256) throw new InvalidOperationException("The audit changed after review.");
            if (completed != null && originalHash != completed.savedSceneSha256)
                throw new InvalidOperationException("Field changed after this committed run; do not overwrite it.");
            if (commit && completed == null && originalHash != reviewed.sourceSceneSha256)
                throw new InvalidOperationException("Field changed after review; obtain a fresh review.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool saved = false;
            try
            {
                var scene = EditorSceneManager.OpenScene(Field, OpenSceneMode.Single);
                if (completed != null)
                {
                    ValidateInstalled(scene, completed);
                    Debug.Log("ORBIS_FIELD_BOSSES_ALREADY_INSTALLED " + commitPath); return;
                }
                var jobs = Prepare(scene, audit);
                var report = new Report { witnessFormatVersion = WitnessFormatVersion, status = commit ? "commit_pending" : "review_only_unsaved", sourceSceneSha256 = originalHash,
                    auditSha256 = Hash(Audit), bosses = jobs.Select(w => w.entry).ToArray() };
                if (commit) RequireSamePlan(reviewed, report);
                else
                {
                    Directory.CreateDirectory(folder);
                    File.Copy(Field, folder + "/Field.before.unity", false);
                    CaptureAll(scene, jobs, folder, "Before");
                }
                foreach (var job in jobs) Install(job);
                foreach (var job in jobs)
                {
                    job.authored.Validate(); job.entry.after = Snapshot(job.authored, job.entry.region);
                    RequirePreserved(job.entry.before, job.entry.after);
                }
                ValidateInstalled(scene, report);
                if (!commit)
                {
                    CaptureAll(scene, jobs, folder, "After");
                    // Both captures use the same fixed camera/scene lights. No pose, sky or material substitution.
                    if (Hash(Field) != originalHash) throw new IOException("Review must not write the Field scene.");
                    File.WriteAllText(reviewPath, JsonUtility.ToJson(report, true));
                    Debug.Log("ORBIS_FIELD_BOSS_REVIEW " + reviewPath);
                }
                else
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene, Field)) throw new IOException("Could not save Field.");
                    saved = true;
                    report.status = "committed_export_pending"; report.savedSceneSha256 = Hash(Field);
                    foreach (var job in jobs)
                    {
                        job.entry.after = Snapshot(job.authored, job.entry.region);
                        RequirePreserved(job.entry.before, job.entry.after);
                    }
                    ValidateInstalled(scene, report);
                    File.WriteAllText(commitPath, JsonUtility.ToJson(report, true));
                    Debug.Log("ORBIS_FIELD_BOSSES_COMMITTED " + commitPath + "; export not run");
                }
            }
            finally
            {
                // Discard only this method's in-memory scene mutations. Original disk scene is immutable during review.
                // A committed disk scene is kept. No overwrite-based rollback of user changes or generated outputs.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // Batch mode can begin without a saved/active scene. Keep the new empty
                // scene in that case; restoring an empty setup would mask the real error.
                if (setup.Any(s => s.isLoaded) && setup.Count(s => s.isActive) == 1)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                if (!saved && Hash(Field) != originalHash)
                    Debug.LogError("Field changed on disk during a non-saving review; inspect externally changed files.");
            }
        }

        static Work[] Prepare(Scene scene, FieldBossBindingAudit.Report audit)
        {
            var world = All<M4SceneBootstrap>(scene).Single(); world.ValidateWorldBindings();
            if (world.IslandRegions.Length != 5 || world.InitializeOnAwake) throw new InvalidOperationException("Expected deferred five-region Field.");
            var catalog = AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>("Assets/Orbis/Art/Resources/Art/Catalog.asset");
            if (catalog == null || catalog.ExplorerOutlineMaterial == null || catalog.ExplorerToonMaterial == null ||
                !EditorUtility.IsPersistent(catalog.ExplorerOutlineMaterial))
                throw new InvalidOperationException("Existing persistent Explorer outline/toon materials are required.");
            var outline = catalog.ExplorerOutlineMaterial;
            var jobs = new List<Work>();
            foreach (var region in world.IslandRegions)
            {
                var old = audit.bosses.Single(b => b.region == region.Id.ToString());
                var root = region.Authored.BossObject;
                if (Id(root) != old.actorId || root.name != old.name || root.transform.position != old.position ||
                    (root.transform.localScale - old.scale).sqrMagnitude > 1e-10f || Quaternion.Angle(root.transform.rotation, Quaternion.Euler(old.euler)) > .001f)
                    throw new InvalidOperationException(old.region + ": original root identity/transform changed after audit.");
                if (!root.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name).SequenceEqual(old.rootComponents))
                    throw new InvalidOperationException(old.region + ": root components differ from audited source.");
                var direct = root.transform.Cast<Transform>().ToArray();
                if (!direct.Select(t => t.name).OrderBy(x => x).SequenceEqual(old.children.Select(c => c.name).OrderBy(x => x)))
                    throw new InvalidOperationException(old.region + ": direct child inventory changed; never guess deletion targets.");
                var keeper = old.children.Single(c => c.name == "Weakness Core" && c.hasWeaknessCore);
                var visual = old.children.Single(c => c.prefab == "Assets/Orbis/Art/Resources/Art/Environment/enemy.prefab");
                string[] names = { visual.name, "Element crest 0", "Element crest 1", "Element crest 2" };
                var remove = names.Select(n => direct.Single(t => t.name == n).gameObject).ToArray();
                foreach (var obj in remove)
                {
                    var child = old.children.Single(c => c.name == obj.name);
                    if (child.hasWeaknessCore || child.allColliders != 0 ||
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(obj) != child.prefab ||
                        (obj.transform.localPosition - child.localPosition).sqrMagnitude > 1e-8f ||
                        (obj.transform.localScale - child.localScale).sqrMagnitude > 1e-8f)
                        throw new InvalidOperationException("Audited visual changed: " + obj.name);
                    // The audited Kenney enemy prefab contains its own visual Animator.
                    // It is inside the exact approved removal child, never the gameplay owner.
                    RequireVisualOnly(obj, true);
                }
                int index = (int)region.Id; string label = Labels[index], folder = Runtime + label;
                var binding = JsonUtility.FromJson<BossRuntimeCandidate.Binding>(File.ReadAllText(folder + "/RuntimeBinding.json"));
                if (binding.label != label || binding.prefab != folder + "/" + label + ".prefab" || binding.profile != folder + "/MotionProfile.asset")
                    throw new InvalidOperationException(label + ": unexpected runtime binding paths.");
                string manifestPath = binding.sourceFolder + "/SourceManifest.json";
                var motion = JsonUtility.FromJson<Motion>(File.ReadAllText(manifestPath));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(binding.prefab);
                var profile = AssetDatabase.LoadAssetAtPath<BossMotionProfile>(binding.profile);
                if (prefab == null || profile == null) throw new InvalidOperationException(label + ": reviewed runtime assets missing.");
                profile.Validate(); RequireVisualOnly(prefab, true);
                var sourceSkin = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                if (sourceSkin.sharedMesh == null || sourceSkin.sharedMesh.subMeshCount != 1 || sourceSkin.sharedMaterials.Length != 1 ||
                    sourceSkin.sharedMaterial == null || sourceSkin.sharedMaterial.shader != catalog.ExplorerToonMaterial.shader ||
                    !EditorUtility.IsPersistent(sourceSkin.sharedMaterial) || sourceSkin.sharedMaterial.renderQueue >= 2500)
                    throw new InvalidOperationException(label + ": preserve one original opaque ExplorerToon submesh; do not silently multiply hull draws or convert materials.");
                if (motion.label != label || !motion.motionFbxRoundtripPass || motion.restFbxSha256 != binding.restFbxSha256 ||
                    motion.motionFbxSha256 != binding.motionFbxSha256 || profile.SourceRigSha256 != binding.restFbxSha256 ||
                    motion.originContract == null || motion.originContract.mode != (label == "WaterBoss" ? "swim_body" : "standing_feet"))
                    throw new InvalidOperationException(label + ": provenance/origin contract mismatch.");
                var anim = prefab.GetComponent<Animator>();
                if (anim == null || prefab.GetComponentsInChildren<Animator>(true).Length != 1 || anim.avatar == null ||
                    !anim.avatar.isValid || anim.avatar.isHuman || anim.applyRootMotion || anim.runtimeAnimatorController != profile.Controller)
                    throw new InvalidOperationException(label + ": expected reviewed Generic Animator without root motion.");
                if (prefab.transform.localPosition != Vector3.zero || prefab.transform.localRotation != Quaternion.identity || prefab.transform.localScale != Vector3.one)
                    throw new InvalidOperationException(label + ": reviewed visual wrapper must retain its documented identity transform before sizing.");
                var core = direct.Single(t => t.name == keeper.name);
                float arena = ArenaRadius(root);
                Bounds bounds = MeasurePrefab(prefab, out float radial);
                float attackRadius = M4BossProfile.ForElement(M4RegionCatalog.Get(region.Id).Element).AttackRadius;
                float maximumRadius = Mathf.Min(arena * .65f, attackRadius * .9f);
                float scale = Mathf.Min(Heights[index] / bounds.size.y, maximumRadius / radial);
                if (!float.IsFinite(scale) || scale <= 0 || !float.IsFinite(radial) || radial <= 0)
                    throw new InvalidOperationException(label + ": invalid measured geometry.");
                // Reviewed FBX faces +Z. Only the visual wrapper turns toward the existing core/approach (-Z).
                Quaternion yaw = Quaternion.Euler(0, 180, 0);
                // Grounded exports already place measured support at Y=0; never bottom-align the Wind tail.
                // The legless water body's Z=0 source swim anchor maps to the existing core's world height.
                Vector3 offset = label == "WaterBoss" ? Vector3.up * core.localPosition.y : Vector3.zero;
                var entry = new Entry { region = old.region, label = label, prefab = binding.prefab, profile = binding.profile,
                    prefabDependencyHash = AssetDatabase.GetAssetDependencyHash(binding.prefab).ToString(),
                    profileDependencyHash = AssetDatabase.GetAssetDependencyHash(binding.profile).ToString(),
                    outlineMaterial = AssetDatabase.GetAssetPath(outline), outlineDependencyHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(outline)).ToString(),
                    additionalOutlineRenderers = 1, additionalOutlineSubmeshDraws = 1,
                    sourceManifestSha256 = Hash(manifestPath), originMode = motion.originContract.mode, desiredHeight = Heights[index],
                    scale = scale, localPosition = offset, localRotation = yaw, sourceBounds = bounds,
                    measuredArenaRadius = arena, attackRadius = attackRadius, sourceHorizontalRadius = radial, horizontalRadius = radial * scale,
                    sizingNote = "Uniform scale=min(height cap, 0.90*existing attack radius/radial extent, 0.65*measured arena radius/radial extent). No combat/collider edits. Standing feet use exported origin; water body uses retained Weakness Core height. Wind rest-tail extent below its sole plane is retained; authored Idle supplies tail clearance.",
                    before = Snapshot(region.Authored, old.region) };
                entry.worldVisualBounds = TransformBounds(bounds, root.transform.localToWorldMatrix * Matrix4x4.TRS(offset, yaw, Vector3.one * scale));
                var frame = entry.worldVisualBounds;
                foreach (var renderer in remove.SelectMany(x => x.GetComponentsInChildren<Renderer>(true))) if (renderer.enabled) frame.Encapsulate(renderer.bounds);
                frame.Encapsulate(root.transform.position);
                entry.cameras = Cameras(frame, root.transform.position);
                jobs.Add(new Work { authored = region.Authored, root = root, core = core, remove = remove, prefab = prefab, profile = profile, outline = outline, entry = entry });
            }
            // Validate all five BEFORE removing a single original visual. Reject external references
            // to an old renderer/transform instead of silently breaking an unrelated serialized binding.
            var removedRoots = jobs.SelectMany(w => w.remove).Select(o => o.transform).ToArray();
            foreach (var behaviour in All<MonoBehaviour>(scene))
            {
                if (behaviour == null) throw new InvalidOperationException("Missing script in authored Field.");
                if (removedRoots.Any(t => behaviour.transform.IsChildOf(t))) continue;
                var so = new SerializedObject(behaviour); var p = so.GetIterator();
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                    Transform referenced = p.objectReferenceValue is GameObject obj ? obj.transform :
                        p.objectReferenceValue is Component component ? component.transform : null;
                    if (referenced != null && removedRoots.Any(t => referenced.IsChildOf(t)))
                        throw new InvalidOperationException(behaviour.name + "." + p.propertyPath + " references a removal target; preserve and resolve this binding first.");
                }
            }
            return jobs.ToArray();
        }

        static void Install(Work w)
        {
            var encounter = w.root.AddComponent<M4FieldBoss>(); // Bootstrap GetComponent reuses this same owner at Play.
            var view = (GameObject)PrefabUtility.InstantiatePrefab(w.prefab, w.root.transform);
            view.name = VisualPrefix + w.entry.label;
            view.transform.localPosition = w.entry.localPosition; view.transform.localRotation = w.entry.localRotation;
            view.transform.localScale = Vector3.one * w.entry.scale;
            var animator = view.GetComponent<Animator>(); animator.applyRootMotion = false;
            foreach (var skin in view.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.forceMatrixRecalculationPerRender = true;
            SaveOutline(view.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(), w.outline);
            var presenter = view.AddComponent<BossAnimationPresenter>();
            // Serialize only. Calling Configure/Animator.Update in Edit Mode would evaluate/rebind authored bones.
            var so = new SerializedObject(presenter);
            so.FindProperty("encounter").objectReferenceValue = encounter;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("profile").objectReferenceValue = w.profile;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(view.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            foreach (var skin in view.GetComponentsInChildren<SkinnedMeshRenderer>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
            foreach (var child in w.remove) Object.DestroyImmediate(child);
            RequireVisualOnly(view, true, true);
            var measured = MeasureInstance(view);
            if ((measured.center - w.entry.worldVisualBounds.center).sqrMagnitude > 1e-6f ||
                (measured.size - w.entry.worldVisualBounds.size).sqrMagnitude > 1e-6f)
                throw new InvalidOperationException(w.entry.label + ": installed mesh bounds disagree with the measured fit.");
        }

        static void SaveOutline(SkinnedMeshRenderer source, Material outline)
        {
            // Persist only a scene-instance inverted hull. Original prefab, mesh, body materials and
            // effect-overlay slots stay untouched. Five single-submesh bosses add at most five hull
            // submesh submissions per common camera pass; shadows are disabled on these copies.
            var node = new GameObject("Art Outline / " + source.name); node.layer = source.gameObject.layer;
            node.transform.SetParent(source.transform, false);
            var hull = node.AddComponent<SkinnedMeshRenderer>(); hull.sharedMesh = source.sharedMesh;
            hull.bones = source.bones; hull.rootBone = source.rootBone; hull.localBounds = source.localBounds;
            hull.quality = source.quality; hull.updateWhenOffscreen = source.updateWhenOffscreen;
            hull.forceMatrixRecalculationPerRender = true;
            hull.sharedMaterials = new[] { outline }; hull.shadowCastingMode = ShadowCastingMode.Off;
            hull.receiveShadows = false; hull.enabled = source.enabled;
            node.AddComponent<ArtOutlineSync>().Configure(source, hull);
        }

        static void RequireVisualOnly(GameObject value, bool animated, bool presenter = false)
        {
            if (value.GetComponentsInChildren<Collider>(true).Length != 0 || value.GetComponentsInChildren<ElementalActor>(true).Length != 0 ||
                value.GetComponentsInChildren<TrainingDummy>(true).Length != 0 || value.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                throw new InvalidOperationException(value.name + ": visual child must have no gameplay/collision components.");
            foreach (var component in value.GetComponentsInChildren<Component>(true))
                if (component == null || !(component is Transform || component is MeshFilter || component is MeshRenderer ||
                    component is SkinnedMeshRenderer || (animated && component is Animator) ||
                    (presenter && component is BossAnimationPresenter) || component.GetType().FullName == "Orbis.Art.ArtOutlineSync"))
                    throw new InvalidOperationException(value.name + ": unapproved visual component " + (component == null ? "MISSING" : component.GetType().FullName));
            if (value.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Weakness Core"))
                throw new InvalidOperationException(value.name + ": never delete or replace a Weakness Core subtree.");
        }

        static Witness Snapshot(M4AuthoredRegion authored, string region)
        {
            var root = authored.BossObject; var t = root.transform;
            var core = t.Cast<Transform>().Single(x => x.name == "Weakness Core");
            var components = root.GetComponents<Component>().Where(c => !(c is Transform) && !(c is M4FieldBoss)).ToArray();
            var coreComponents = core.GetComponentsInChildren<Transform>(true).SelectMany(x =>
                new Object[] { x.gameObject }.Concat(x.GetComponents<Component>())).ToArray();
            var colliders = root.GetComponentsInChildren<Collider>(true);
            return new Witness { region = region, rootId = Id(root), rootName = root.name, rootTag = root.tag,
                layer = root.layer, active = root.activeSelf, staticFlags = (int)GameObjectUtility.GetStaticEditorFlags(root), actorId = Id(root.GetComponent<ElementalActor>()),
                dummyId = Id(root.GetComponent<TrainingDummy>()), parentId = Id(t.parent), authoredJson = StableProperties(authored),
                position = t.position, rotation = t.rotation, localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale,
                worldMatrix = Enumerable.Range(0, 16).Select(i => t.localToWorldMatrix[i]).ToArray(),
                componentIds = components.Select(Id).ToArray(), componentJson = components.Select(StableProperties).ToArray(),
                coreIds = coreComponents.Select(Id).ToArray(), coreJson = coreComponents.Select(StableProperties).ToArray(),
                colliderIds = colliders.Select(Id).ToArray(), colliderJson = colliders.Select(StableProperties).ToArray() };
        }
        static string StableProperties(Object value)
        {
            // EditorJsonUtility can serialize ephemeral instance IDs. Property paths with GlobalObjectId
            // references remain comparable after scene reload and in a different review/commit process.
            var text = new StringBuilder(); var so = new SerializedObject(value); var p = so.GetIterator();
            string Number(double number) => number.ToString("R", CultureInfo.InvariantCulture);
            bool enterChildren = true;
            while (p.Next(enterChildren))
            {
                // The ObjectReference value below already records its complete GlobalObjectId.
                // Unity 6000.6 exposes internal m_FileID/m_PathID children whose values depend
                // on the current process/load order. Do not descend into that native handle.
                // Do not skip fields by NAME: a real authored integer called m_FileID must
                // still be checked. Structs/arrays and ExposedReference keys still descend.
                enterChildren = p.propertyType != SerializedPropertyType.ObjectReference;
                string data;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Generic: continue; // Descend into every array/struct member.
                    case SerializedPropertyType.Integer: data = p.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: data = p.boolValue ? "true" : "false"; break;
                    case SerializedPropertyType.Float: data = Number(p.doubleValue); break;
                    case SerializedPropertyType.String: data = p.stringValue; break;
                    case SerializedPropertyType.ObjectReference: data = Id(p.objectReferenceValue); break;
                    case SerializedPropertyType.ExposedReference: data = Id(p.exposedReferenceValue); break;
                    case SerializedPropertyType.Enum: case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.ArraySize: case SerializedPropertyType.Character: data = p.intValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Color: var c = p.colorValue; data = Number(c.r)+","+Number(c.g)+","+Number(c.b)+","+Number(c.a); break;
                    case SerializedPropertyType.Vector2: var v2 = p.vector2Value; data = Number(v2.x)+","+Number(v2.y); break;
                    case SerializedPropertyType.Vector3: var v3 = p.vector3Value; data = Number(v3.x)+","+Number(v3.y)+","+Number(v3.z); break;
                    case SerializedPropertyType.Vector4: var v4 = p.vector4Value; data = Number(v4.x)+","+Number(v4.y)+","+Number(v4.z)+","+Number(v4.w); break;
                    case SerializedPropertyType.Quaternion: var q = p.quaternionValue; data = Number(q.x)+","+Number(q.y)+","+Number(q.z)+","+Number(q.w); break;
                    case SerializedPropertyType.Rect: var r = p.rectValue; data = Number(r.x)+","+Number(r.y)+","+Number(r.width)+","+Number(r.height); break;
                    case SerializedPropertyType.Bounds: var b = p.boundsValue; data = JsonUtility.ToJson(b); break;
                    case SerializedPropertyType.Vector2Int: data = p.vector2IntValue.ToString(); break;
                    case SerializedPropertyType.Vector3Int: data = p.vector3IntValue.ToString(); break;
                    case SerializedPropertyType.RectInt: data = p.rectIntValue.ToString(); break;
                    case SerializedPropertyType.BoundsInt: data = p.boundsIntValue.ToString(); break;
                    case SerializedPropertyType.Hash128: data = p.hash128Value.ToString(); break;
                    default: throw new InvalidOperationException(value.name + ": unsupported preservation property " + p.propertyPath + " / " + p.propertyType);
                }
                text.Append(p.propertyPath.Length).Append(':').Append(p.propertyPath).Append('|').Append(p.propertyType)
                    .Append('|').Append(data.Length).Append(':').Append(data).Append('\n');
            }
            return text.ToString();
        }
        static void RequirePreserved(Witness before, Witness after)
        {
            if (JsonUtility.ToJson(before) != JsonUtility.ToJson(after))
                throw new InvalidOperationException(before.region + ": root/world matrix/actor/core/collider/authored references changed. " + WitnessDifference(before, after));
        }
        static string WitnessDifference(Witness before, Witness after)
        {
            foreach (var field in typeof(Witness).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                object a = field.GetValue(before), b = field.GetValue(after);
                if (a is Array aa && b is Array ba)
                {
                    if (aa.Length != ba.Length) return field.Name + ".Length: " + aa.Length + " -> " + ba.Length;
                    for (int i = 0; i < aa.Length; i++)
                        if (!Equals(aa.GetValue(i), ba.GetValue(i))) return Difference(field.Name + "[" + i + "]", aa.GetValue(i), ba.GetValue(i));
                }
                else if (!Equals(a, b)) return Difference(field.Name, a, b);
            }
            return "Serialized witness text differs; inspect the fresh review rather than accepting a mismatch.";
        }
        static string Difference(string field, object before, object after)
        {
            string a = before?.ToString() ?? "<null>", b = after?.ToString() ?? "<null>";
            int at = 0; while (at < a.Length && at < b.Length && a[at] == b[at]) at++;
            string property = "";
            if (a.Length > 0)
            {
                int start = a.LastIndexOf('\n', Math.Min(at, a.Length - 1)) + 1;
                int separator = a.IndexOf('|', start);
                if (separator >= start && separator - start < 256) property = " property " + a.Substring(start, separator - start);
            }
            string Excerpt(string text)
            {
                int start = Math.Max(0, Math.Min(at, text.Length) - 48);
                return text.Substring(start, Math.Min(180, text.Length - start)).Replace("\r", "\\r").Replace("\n", "\\n");
            }
            return field + property + " first difference @" + at + "; before=[" + Excerpt(a) + "]; after=[" + Excerpt(b) + "]";
        }
        static void RequireSamePlan(Report reviewed, Report current)
        {
            if (reviewed.bosses.Length != current.bosses.Length) throw new InvalidOperationException("Review size changed.");
            foreach (var e in current.bosses)
            {
                var r = reviewed.bosses.Single(x => x.region == e.region);
                RequirePreserved(r.before, e.before);
                if (r.prefab != e.prefab || r.profile != e.profile || r.prefabDependencyHash != e.prefabDependencyHash ||
                    r.profileDependencyHash != e.profileDependencyHash || r.sourceManifestSha256 != e.sourceManifestSha256 ||
                    r.outlineMaterial != e.outlineMaterial || r.outlineDependencyHash != e.outlineDependencyHash ||
                    r.scale != e.scale || r.localPosition != e.localPosition || r.localRotation != e.localRotation || r.measuredArenaRadius != e.measuredArenaRadius)
                    throw new InvalidOperationException(e.label + ": assets/sizing changed since the actual screenshot review.");
            }
        }
        static void ValidateInstalled(Scene scene, Report report)
        {
            var world = All<M4SceneBootstrap>(scene).Single(); world.ValidateWorldBindings();
            foreach (var entry in report.bosses)
            {
                var authored = world.IslandRegions.Single(x => x.Id.ToString() == entry.region).Authored;
                RequirePreserved(entry.before, Snapshot(authored, entry.region));
                var root = authored.BossObject;
                if (root.transform.childCount != 2 || root.GetComponents<M4FieldBoss>().Length != 1)
                    throw new InvalidOperationException(entry.label + ": expected retained core and one original visual.");
                var view = root.transform.Cast<Transform>().Single(x => x.name == VisualPrefix + entry.label).gameObject;
                RequireVisualOnly(view, true, true);
                var body = view.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => !s.name.StartsWith("Art Outline / ", StringComparison.Ordinal));
                var hulls = view.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith("Art Outline / ", StringComparison.Ordinal)).ToArray();
                if (hulls.Length != 1 || hulls[0].sharedMesh != body.sharedMesh || hulls[0].rootBone != body.rootBone ||
                    !hulls[0].bones.SequenceEqual(body.bones) || hulls[0].localBounds != body.localBounds ||
                    hulls[0].transform.parent != body.transform || hulls[0].transform.localPosition != Vector3.zero ||
                    hulls[0].transform.localRotation != Quaternion.identity || hulls[0].transform.localScale != Vector3.one ||
                    hulls[0].shadowCastingMode != ShadowCastingMode.Off || hulls[0].receiveShadows ||
                    hulls[0].sharedMaterials.Length != 1 || hulls[0].sharedMaterial != AssetDatabase.LoadAssetAtPath<Material>(entry.outlineMaterial) ||
                    AssetDatabase.GetAssetDependencyHash(entry.outlineMaterial).ToString() != entry.outlineDependencyHash)
                    throw new InvalidOperationException(entry.label + ": persistent outline bones/material/transform contract failed.");
                var sync = hulls[0].GetComponent<ArtOutlineSync>();
                if (sync == null) throw new InvalidOperationException(entry.label + ": saved outline sync missing.");
                var syncSerialized = new SerializedObject(sync);
                if (syncSerialized.FindProperty("source").objectReferenceValue != body || syncSerialized.FindProperty("target").objectReferenceValue != hulls[0])
                    throw new InvalidOperationException(entry.label + ": outline sync must retain the source overlay threshold binding.");
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view) != entry.prefab ||
                    view.transform.localPosition != entry.localPosition || view.transform.localRotation != entry.localRotation ||
                    view.transform.localScale != Vector3.one * entry.scale)
                    throw new InvalidOperationException(entry.label + ": installed visual transform/prefab changed.");
                var presenters = view.GetComponentsInChildren<BossAnimationPresenter>(true);
                if (presenters.Length != 1) throw new InvalidOperationException(entry.label + ": expected one Presenter.");
                var so = new SerializedObject(presenters[0]); var animator = view.GetComponent<Animator>();
                if (so.FindProperty("encounter").objectReferenceValue != root.GetComponent<M4FieldBoss>() ||
                    so.FindProperty("animator").objectReferenceValue != animator ||
                    so.FindProperty("profile").objectReferenceValue != AssetDatabase.LoadAssetAtPath<BossMotionProfile>(entry.profile) ||
                    animator == null || animator.applyRootMotion ||
                    AssetDatabase.GetAssetDependencyHash(entry.prefab).ToString() != entry.prefabDependencyHash ||
                    AssetDatabase.GetAssetDependencyHash(entry.profile).ToString() != entry.profileDependencyHash)
                    throw new InvalidOperationException(entry.label + ": installed binding/dependencies changed.");
            }
        }

        static float ArenaRadius(GameObject root)
        {
            // Field authoring can place environmental scenery in a different stream group from its
            // resident encounter. Match the named existing pad by world center, not a guessed parent path.
            var floor = All<MeshFilter>(root.scene).Where(f => f.name == "Weathered guardian circle" && f.sharedMesh != null &&
                Vector2.Distance(new Vector2(f.transform.position.x, f.transform.position.z),
                    new Vector2(root.transform.position.x, root.transform.position.z)) < .1f).ToArray();
            if (floor.Length != 1 || floor[0].sharedMesh == null) throw new InvalidOperationException(root.name + ": audited arena floor is missing/ambiguous.");
            var bounds = TransformBounds(floor[0].sharedMesh.bounds, floor[0].transform.localToWorldMatrix);
            float radius = Mathf.Min(bounds.size.x, bounds.size.z) * .5f;
            if (radius < 4f || Vector2.Distance(new Vector2(bounds.center.x, bounds.center.z), new Vector2(root.transform.position.x, root.transform.position.z)) > .1f)
                throw new InvalidOperationException(root.name + ": arena geometry/center no longer matches the encounter.");
            return radius;
        }
        static Bounds MeasurePrefab(GameObject prefab, out float horizontalRadius)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject instance = null;
            try
            {
                instance = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(instance, scene);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); instance.transform.localScale = Vector3.one;
                return MeasureInstance(instance, out horizontalRadius);
            }
            finally { if (instance != null) Object.DestroyImmediate(instance); EditorSceneManager.ClosePreviewScene(scene); }
        }
        static Bounds MeasureInstance(GameObject instance) => MeasureInstance(instance, out _);
        static Bounds MeasureInstance(GameObject instance, out float horizontalRadius)
        {
            var skin = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => !s.name.StartsWith("Art Outline / ", StringComparison.Ordinal)); var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh, true); var points = mesh.vertices;
                if (points.Length == 0) throw new InvalidOperationException("Reviewed mesh has no vertices.");
                var bounds = new Bounds(skin.transform.TransformPoint(points[0]), Vector3.zero);
                horizontalRadius = 0f;
                foreach (var p in points)
                {
                    Vector3 world = skin.transform.TransformPoint(p); bounds.Encapsulate(world);
                    Vector3 offset = world - instance.transform.position;
                    horizontalRadius = Mathf.Max(horizontalRadius, new Vector2(offset.x, offset.z).magnitude);
                }
                if (bounds.size.y <= 0) throw new InvalidOperationException("Invalid measured mesh bounds.");
                return bounds;
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var output = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++) output.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return output;
        }
        static CameraSpec[] Cameras(Bounds full, Vector3 anchor)
        {
            var direction = new Vector3(.62f, .24f, -1).normalized;
            CameraSpec Fit(string name, Bounds bounds)
            {
                var spec = new CameraSpec { name = name };
                var rotation = Quaternion.LookRotation(-direction, Vector3.up);
                Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;
                float halfWidth = Vector3.Dot(new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z)), bounds.extents);
                float halfHeight = Vector3.Dot(new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z)), bounds.extents);
                // 12% composition margin plus model depth: sufficient body/ground detail, fixed for both shots.
                float distance = Mathf.Max(halfHeight, halfWidth / spec.aspect) * 1.12f / Mathf.Tan(spec.fieldOfView * Mathf.Deg2Rad * .5f) + bounds.extents.magnitude * .45f;
                spec.position = bounds.center + direction * distance; spec.rotation = rotation; return spec;
            }
            var lower = new Bounds(anchor + Vector3.up * .65f, new Vector3(2.6f, 1.5f, 2.6f));
            return new[] { Fit("Full", full), Fit("GroundCore", lower) };
        }
        static void CaptureAll(Scene scene, IEnumerable<Work> jobs, string folder, string stage)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Actual Before/After requires the URP graphics device.");
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var node = new GameObject("Temporary Field Boss Review Camera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(node, scene);
            var camera = node.AddComponent<Camera>(); camera.enabled = false; camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true; camera.allowMSAA = true; camera.useOcclusionCulling = false;
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
            data.volumeLayerMask = ~0; data.volumeTrigger = camera.transform;
            try
            {
                // Candidate shaders may have queued compilation before this method disabled async.
                // Compile the actual installed materials before the first After render as well.
                foreach (var material in jobs.SelectMany(j => j.root.GetComponentsInChildren<Renderer>(true))
                    .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
                    for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass);
                foreach (var job in jobs) foreach (var spec in job.entry.cameras)
                {
                    camera.transform.SetPositionAndRotation(spec.position, spec.rotation); camera.fieldOfView = spec.fieldOfView;
                    camera.aspect = spec.aspect; camera.nearClipPlane = spec.near; camera.farClipPlane = spec.far;
                    var target = new RenderTexture(spec.width, spec.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var old = RenderTexture.active; Texture2D pixels = null; VolumeStack oldStack = null;
                    try
                    {
                        target.Create();
                        // Match the proven runtime capture path: bind this camera's real Volume
                        // stack and warm the URP request before reading the first saved pixels.
                        if (!VolumeManager.instance.isInitialized)
                            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
                        camera.UpdateVolumeStack(); oldStack = VolumeManager.instance.stack;
                        VolumeManager.instance.stack = data.volumeStack;
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        RenderTexture.active = target; pixels = new Texture2D(spec.width, spec.height, TextureFormat.RGB24, false);
                        pixels.ReadPixels(new Rect(0, 0, spec.width, spec.height), 0, 0); pixels.Apply();
                        File.WriteAllBytes(folder + "/" + job.entry.label + "_" + spec.name + "_" + stage + ".png", pixels.EncodeToPNG());
                    }
                    finally { RenderTexture.active = old; if (oldStack != null) VolumeManager.instance.stack = oldStack;
                        if (pixels != null) Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target); }
                }
            }
            finally { Object.DestroyImmediate(node); ShaderUtil.allowAsyncCompilation = async; }
        }
        static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
        static string Id(Object value) => value == null ? "null" : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string Arg(string key) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    }
}
