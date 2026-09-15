#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Orbis.Game.Animation;
using Orbis.Game.World;
using Orbis.M0;
using Orbis.M1;
using Orbis.M3;
using Orbis.M4;
using Orbis.M16;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    /// <summary>Loads the actual saved Field, retains its five encounters, and observes normal Update/Animator playback.</summary>
    public sealed class FieldBossSavedSceneTests
    {
        const string ScenePath = "Assets/Scenes/Field.unity";
        static readonly string[] Labels = { "FireBoss", "WaterBoss", "WindBoss", "RockBoss", "LightningBoss" };
        Scene loaded;
        M4SceneBootstrap world;
        string temporaryProfile, output, originalSceneHash;
        float originalTimeScale, originalCaptureDelta;
        bool initialized, originalBackground, originalCursorVisible;
        CursorLockMode originalCursorLock;
        readonly List<Binding> bindings = new List<Binding>();
        Report report;

        sealed class Binding
        {
            public M4IslandRegion region;
            public M4FieldBoss boss;
            public ElementalActor actor;
            public TrainingDummy dummy;
            public BossAnimationPresenter presenter;
            public Animator animator;
            public BossMotionProfile profile;
            public SkinnedMeshRenderer skin;
            public Transform core;
            public Renderer coreRenderer;
            public Collider[] colliders;
            public string[] colliderJson;
            public Matrix4x4 rootMatrix, coreMatrix;
            public Vector3 coreScale;
            public Transform[] bones;
            public Vector3[] firstBonePositions;
            public Evidence evidence;
        }
        [Serializable] sealed class Report
        {
            public string scene = ScenePath, sceneSha256, graphics;
            public string source = "Saved Field scene loaded directly in PlayMode. No boss clones, Configure/Tick/Animator.Update calls, altered clip clocks, or collider changes.";
            public string groundNote = "Root and weighted foot/tail minima are measured against actual layer-8 colliders; visible arena pad is recorded separately. Clearances are evidence, not an automatic artistic approval.";
            public int idleFrames = 36;
            public int ownedSceneHandlesAfterCleanup;
            public bool savedSceneUnchanged;
            public List<Evidence> bosses = new List<Evidence>();
        }
        [Serializable] sealed class Evidence
        {
            public string label, region, profile, actorId;
            public Vector3 savedRootPosition, savedCorePosition, savedCoreScale;
            public string rootEntityId, coreEntityId;
            public int coreColliderCount, attackEvents;
            public bool preserved, defaultCoreColor, exposedCoreColor, coreRendererBound, environmentBlocksCore;
            public float maximumRootDrift, maximumIdleBoneMotion, maximumAttackTime;
            public List<GroundSample> samples = new List<GroundSample>();
            public List<SkinMeasurement> skinMeasurements = new List<SkinMeasurement>();
        }
        [Serializable] sealed class GroundSample
        {
            public string phase, state;
            public float rootAboveCollider, visiblePadAboveRoot, bodyMinimumAboveCollider;
            public float tailMinimumAboveCollider;
            public bool tailMeasured;
            public Vector3 rootPosition, corePosition;
            public List<FootSample> feet = new List<FootSample>();
        }
        [Serializable] sealed class FootSample
        { public string bone; public int weightedVertices; public float minimumAboveCollider; public Vector3 minimumPoint; }
        [Serializable] sealed class SkinMeasurement
        {
            public string phase, skin, rootBone, quality;
            public int vertices, bones, blendShapes;
            public Vector3 rootPosition, skinPosition, localScale, lossyScale, referenceMinimum, referenceMaximum;
            public Matrix4x4 skinLocalToWorld, rootBoneLocalToWorld;
            public float maximumWeightSumError;
            public string reference = "Sum of bone.localToWorldMatrix * mesh.bindposes * restVertex * original weight; no weight normalization, Animator evaluation, or transform changes.";
            public List<BakeComparison> routes = new List<BakeComparison>();
        }
        [Serializable] sealed class BakeComparison
        {
            public string route;
            public Vector3 minimum, maximum, worstPoint, referencePoint;
            public float maximumError, rootMeanSquareError;
            public int worstVertex;
        }

        [UnityTest]
        public IEnumerator SavedFieldKeepsFiveBossBindingsAndPlaysIdleAttackAndExposure()
        {
            string run = Arg("-fieldBossFinalRun");
            if (string.IsNullOrEmpty(run)) Assert.Ignore("Opt in with -fieldBossFinalRun <fresh_label> after saving the reviewed Field.");
            Assert.That(run.All(c => char.IsLetterOrDigit(c) || c == '_'), Is.True);
            output = "TestResults/CharacterPipeline/FieldFinal/" + run;
            Assert.That(Directory.Exists(output), Is.False, "Use a fresh evidence directory.");
            Directory.CreateDirectory(output);
            originalSceneHash = Hash(ScenePath);
            originalTimeScale = Time.timeScale; originalCaptureDelta = Time.captureDeltaTime;
            originalBackground = Application.runInBackground; originalCursorVisible = Cursor.visible; originalCursorLock = Cursor.lockState;
            initialized = true;
            Time.timeScale = 1f; Time.captureDeltaTime = 1f / 30f; Application.runInBackground = true;
            temporaryProfile = Path.Combine(Path.GetTempPath(), "Orbis-Field-Final-" + Guid.NewGuid().ToString("N"));
            var progress = new M4ProgressService(Path.Combine(temporaryProfile, "profile.json"));
            ExplorerJourney.Stop(); M4Session.UseProgressForTests(progress);
            // Leave selection pending long enough to prove these are saved objects, not runtime construction.
            // Field is the edit-time product scene; the test scope only enables legacy runtime exports.
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            loaded = SceneManager.GetActiveScene();
            Assert.That(loaded.path, Is.EqualTo(ScenePath));
            for (int i = 0; i < 3; i++) yield return null;
            var field = SceneObjects<FieldAuthoring>().Single();
            var entry = field.Entry;
            Assert.That(entry, Is.Not.Null);
            world = entry.World;
            Assert.That(world.IsInitialized, Is.False, "An unselected temporary profile must preserve the saved Field before gameplay starts.");
            world.ValidateWorldBindings();
            Assert.That(world.IslandRegions.Length, Is.EqualTo(5));
            Assert.That(SceneObjects<M4FieldBoss>().Length, Is.EqualTo(5), "All five encounter components must already be serialized.");
            Assert.That(SceneObjects<BossAnimationPresenter>().Length, Is.EqualTo(5));
            report = new Report { sceneSha256 = originalSceneHash, graphics = SystemInfo.graphicsDeviceName };
            foreach (var region in world.IslandRegions.OrderBy(r => (int)r.Id))
            {
                var b = ReadBinding(region);
                bindings.Add(b); report.bosses.Add(b.evidence);
                b.evidence.samples.Add(MeasureGround(b, "SavedBeforeInitialize"));
            }
            Assert.That(progress.TrySelectExplorer(ExplorerChoice.Stella), Is.True);
            Assert.That(entry.BeginWorld(), Is.True, entry.LastError);
            Assert.That(world.IsInitialized, Is.True);
            Assert.That(world.Regions.Count, Is.EqualTo(5));
            world.Traversal.GetComponent<M0Input>().SetPresentationLocked(true);
            foreach (var b in bindings)
            {
                Assert.That(world.GetRegion(b.region.Id).Boss, Is.SameAs(b.boss));
                Assert.That(b.boss.AutoTick, Is.True);
                Assert.That(b.boss.Profile, Is.Not.Null);
                Assert.That(b.boss.Actor, Is.SameAs(b.actor));
                Assert.That(b.boss.Actor.SourceId, Is.EqualTo("M4.Boss." + b.region.Id));
                b.evidence.actorId = b.boss.Actor.SourceId;
                Assert.That(b.boss.UsesCoreExposure, Is.True);
                Assert.That(b.animator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));
                b.firstBonePositions = b.bones.Select(t => t.position).ToArray();
                b.boss.AttackExecuted += () => b.evidence.attackEvents++;
            }
            // The saved spawn is outside all engagement circles; do not teleport bosses or change AI ticking.
            for (int frame = 0; frame < report.idleFrames; frame++)
            {
                yield return null;
                foreach (var b in bindings)
                {
                    Assert.That(b.boss.State, Is.EqualTo(M4BossState.Dormant), b.evidence.label + " spawn unexpectedly engages boss.");
                    TrackMotion(b);
                }
            }
            foreach (var b in bindings)
            {
                VerifyPreserved(b);
                Assert.That(b.evidence.maximumIdleBoneMotion, Is.GreaterThan(.0001f), b.evidence.label + " saved Generic skin does not animate.");
                Assert.That(b.animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"), Is.True);
                b.evidence.defaultCoreColor = ColorMatches(b.coreRenderer, M3Palette.Primary(b.boss.Weakness));
                Assert.That(b.evidence.defaultCoreColor, Is.True, b.evidence.label + " Bootstrap did not tint the retained Weakness Core.");
                // Internal runtime binding is inspected read-only; no replacement or Configure call.
                var runtime = world.GetRegion(b.region.Id);
                var boundCore = typeof(M4RegionRuntime).GetField("BossCore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(runtime);
                b.evidence.coreRendererBound = ReferenceEquals(boundCore, b.coreRenderer);
                Assert.That(b.evidence.coreRendererBound, Is.True);
                b.evidence.samples.Add(MeasureGround(b, "Idle36"));
            }
            // One real encounter attack is enough for this integration fixture; the five isolated
            // species playback tests own the exhaustive per-species clock/defeat coverage.
            var fire = bindings.Single(b => b.region.Id == M4RegionId.Agnia);
            world.Traversal.Teleport(fire.boss.transform.position + Vector3.back * (fire.boss.AttackRadius - .3f) + Vector3.up * .1f);
            Physics.SyncTransforms();
            float deadline = Time.realtimeSinceStartup + 15f;
            while (fire.evidence.attackEvents == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                foreach (var b in bindings) TrackRoot(b);
                fire.evidence.maximumAttackTime = Mathf.Max(fire.evidence.maximumAttackTime, fire.animator.GetFloat("AttackTime"));
            }
            Assert.That(fire.evidence.attackEvents, Is.EqualTo(1));
            Assert.That(fire.evidence.maximumAttackTime, Is.GreaterThan(.01f), "The saved Presenter must clock the real windup.");
            Assert.That(fire.boss.State, Is.EqualTo(M4BossState.Recover));
            fire.evidence.samples.Add(MeasureGround(fire, "FirstActualAttack"));
            // Existing reaction API, same retained Actor: three weak direct hits expose the core.
            for (int hit = 0; hit < 3; hit++) world.Manager.Apply(fire.actor, fire.boss.Weakness, world.Party.ActiveMember.Actor, .1f);
            Assert.That(fire.boss.State, Is.EqualTo(M4BossState.Exposed));
            for (int i = 0; i < 3; i++) yield return null;
            fire.evidence.exposedCoreColor = ColorMatches(fire.coreRenderer, Color.white);
            Assert.That(fire.evidence.exposedCoreColor, Is.True);
            Assert.That(fire.animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Exposed") || fire.animator.GetNextAnimatorStateInfo(0).IsName("Base Layer.Exposed"), Is.True);
            foreach (var b in bindings) VerifyPreserved(b);
            Assert.That(bindings.Sum(b => b.evidence.attackEvents), Is.EqualTo(1));
            report.savedSceneUnchanged = Hash(ScenePath) == originalSceneHash;
            Assert.That(report.savedSceneUnchanged, Is.True);
            SaveReport();
        }

        Binding ReadBinding(M4IslandRegion region)
        {
            string label = Labels[(int)region.Id];
            var root = region.Authored.BossObject;
            var b = new Binding { region = region, boss = root.GetComponent<M4FieldBoss>(), actor = root.GetComponent<ElementalActor>(), dummy = root.GetComponent<TrainingDummy>(), rootMatrix = root.transform.localToWorldMatrix };
            Assert.That(b.boss, Is.Not.Null, label + " is not saved in Field.");
            b.presenter = root.GetComponentsInChildren<BossAnimationPresenter>(true).Single();
            var so = new SerializedObject(b.presenter);
            Assert.That(so.FindProperty("encounter").objectReferenceValue, Is.SameAs(b.boss));
            b.animator = (Animator)so.FindProperty("animator").objectReferenceValue;
            b.profile = (BossMotionProfile)so.FindProperty("profile").objectReferenceValue;
            Assert.That(b.profile, Is.Not.Null); b.profile.Validate();
            Assert.That(AssetDatabase.GetAssetPath(b.profile), Is.EqualTo("Assets/Orbis/Game/Characters/Bosses/Original01/" + label + "/MotionProfile.asset"));
            Assert.That(b.animator.avatar != null && b.animator.avatar.isValid && !b.animator.avatar.isHuman, Is.True);
            Assert.That(b.animator.applyRootMotion, Is.False);
            Assert.That(b.animator.runtimeAnimatorController, Is.SameAs(b.profile.Controller));
            b.skin = b.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => !s.name.StartsWith("Art Outline / ", StringComparison.Ordinal));
            b.bones = b.skin.bones;
            b.core = root.transform.Cast<Transform>().Single(t => t.name == "Weakness Core");
            b.coreRenderer = b.core.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "Weakness Core");
            b.coreMatrix = b.core.localToWorldMatrix; b.coreScale = b.core.localScale;
            Assert.That(b.coreRenderer.enabled && b.coreRenderer.gameObject.activeInHierarchy, Is.True);
            Assert.That(b.core.GetComponentsInChildren<Collider>(true).Length, Is.Zero);
            Assert.That(b.presenter.GetComponentsInChildren<Collider>(true).Length, Is.Zero, "The new visual must not change root-targeted combat.");
            b.colliders = root.GetComponentsInChildren<Collider>(true);
            Assert.That(b.colliders.Any(c => c is CapsuleCollider && c.enabled && !c.isTrigger && c.gameObject.layer == 9), Is.True);
            b.colliderJson = b.colliders.Select(EditorJsonUtility.ToJson).ToArray();
            b.evidence = new Evidence { label = label, region = region.Id.ToString(), profile = AssetDatabase.GetAssetPath(b.profile),
                actorId = b.actor.SourceId, rootEntityId = root.GetEntityId().ToString(), coreEntityId = b.core.GetEntityId().ToString(),
                savedRootPosition = root.transform.position, savedCorePosition = b.core.position, savedCoreScale = b.coreScale, coreColliderCount = 0 };
            Vector3 approach = root.transform.position + Vector3.back * 3f + Vector3.up * 1.1f;
            b.evidence.environmentBlocksCore = Physics.Linecast(approach, b.coreRenderer.bounds.center, 1 << 8, QueryTriggerInteraction.Ignore);
            return b;
        }

        void VerifyPreserved(Binding b)
        {
            Assert.That(b.region.Authored.BossObject.GetComponent<M4FieldBoss>(), Is.SameAs(b.boss));
            Assert.That(b.region.Authored.BossObject.GetComponent<ElementalActor>(), Is.SameAs(b.actor));
            Assert.That(b.region.Authored.BossObject.GetComponent<TrainingDummy>(), Is.SameAs(b.dummy));
            AssertMatrix(b.rootMatrix, b.boss.transform.localToWorldMatrix, b.evidence.label + " root");
            AssertMatrix(b.coreMatrix, b.core.localToWorldMatrix, b.evidence.label + " saved Core transform");
            Assert.That(b.core.localScale, Is.EqualTo(b.coreScale));
            CollectionAssert.AreEqual(b.colliders, b.boss.GetComponentsInChildren<Collider>(true));
            CollectionAssert.AreEqual(b.colliderJson, b.colliders.Select(EditorJsonUtility.ToJson).ToArray());
            Assert.That(b.evidence.maximumRootDrift, Is.LessThan(.0001f));
            b.evidence.preserved = true;
        }
        static void AssertMatrix(Matrix4x4 a, Matrix4x4 b, string label)
        { for (int i = 0; i < 16; i++) Assert.That(b[i], Is.EqualTo(a[i]).Within(.0001f), label + " matrix[" + i + "]"); }
        static bool ColorMatches(Renderer renderer, Color expected)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); Color actual = block.GetColor("_BaseColor");
            return Mathf.Abs(actual.r-expected.r)<.001f && Mathf.Abs(actual.g-expected.g)<.001f && Mathf.Abs(actual.b-expected.b)<.001f;
        }
        static void TrackRoot(Binding b)
        { b.evidence.maximumRootDrift = Mathf.Max(b.evidence.maximumRootDrift, Vector3.Distance(b.boss.transform.position, b.evidence.savedRootPosition)); }
        static void TrackMotion(Binding b)
        { TrackRoot(b); for (int i = 0; i < b.bones.Length; i++) b.evidence.maximumIdleBoneMotion = Mathf.Max(b.evidence.maximumIdleBoneMotion, Vector3.Distance(b.bones[i].position, b.firstBonePositions[i])); }
        static float GroundY(Vector3 point)
        {
            var hits = Physics.RaycastAll(point + Vector3.up * 5f, Vector3.down, 10f, 1 << 8, QueryTriggerInteraction.Ignore);
            Assert.That(hits.Length, Is.GreaterThan(0), "No actual arena ground collider at " + point);
            return hits.OrderBy(h => h.distance).First().point.y;
        }
        GroundSample MeasureGround(Binding b, string phase)
        {
            Physics.SyncTransforms();
            var root = b.boss.transform.position;
            var sample = new GroundSample { phase = phase, state = b.boss.State.ToString(), rootPosition = root, corePosition = b.core.position, rootAboveCollider = root.y - GroundY(root) };
            var pad = SceneObjects<MeshFilter>().Single(m => m.name == "Weathered guardian circle" && new Vector2(m.transform.position.x-root.x,m.transform.position.z-root.z).magnitude < .1f);
            sample.visiblePadAboveRoot = pad.transform.TransformPoint(pad.sharedMesh.bounds.center).y - root.y;
            {
                var vertices = MeasureWorldVertices(b, phase);
                var weights = b.skin.sharedMesh.boneWeights;
                Assert.That(vertices.Length, Is.EqualTo(weights.Length));
                var bottom = vertices.OrderBy(v => v.y).First();
                sample.bodyMinimumAboveCollider = bottom.y - GroundY(bottom);
                var tailIndices = b.bones.Select((t,i) => new {t,i}).Where(x => x.t.name.StartsWith("Tail", StringComparison.Ordinal)).Select(x => x.i).ToHashSet();
                int tail = LowestWeighted(vertices, weights, tailIndices, out _);
                if (tail >= 0) { sample.tailMeasured = true; sample.tailMinimumAboveCollider = vertices[tail].y-GroundY(vertices[tail]); }
                // The approved Wind/Lightning anatomy has four existing three-bone leg chains.
                if (b.region.Id == M4RegionId.Zephyr || b.region.Id == M4RegionId.Voltheim)
                    foreach (string name in new[] { "FrontLeg.L3", "FrontLeg.R3", "RearLeg.L3", "RearLeg.R3" })
                    {
                        int bone = Array.FindIndex(b.bones, t => t.name == name);
                        Assert.That(bone, Is.GreaterThanOrEqualTo(0), b.evidence.label + " missing approved " + name);
                        int vertex = LowestWeighted(vertices, weights, new HashSet<int> { bone }, out int count);
                        Assert.That(vertex, Is.GreaterThanOrEqualTo(0), name + " has no weighted visible surface.");
                        sample.feet.Add(new FootSample { bone = name, weightedVertices = count, minimumPoint = vertices[vertex], minimumAboveCollider = vertices[vertex].y-GroundY(vertices[vertex]) });
                    }
            }
            return sample;
        }
        Vector3[] MeasureWorldVertices(Binding b, string phase)
        {
            var source = b.skin.sharedMesh;
            var rest = source.vertices;
            var weights = source.boneWeights;
            var bindposes = source.bindposes;
            Assert.That(rest.Length, Is.EqualTo(weights.Length));
            Assert.That(bindposes.Length, Is.EqualTo(b.bones.Length));
            Assert.That(source.blendShapeCount, Is.Zero, "This independent LBS reference does not evaluate blend shapes.");
            var matrices = b.bones.Select((bone, i) => bone.localToWorldMatrix * bindposes[i]).ToArray();
            var reference = new Vector3[rest.Length];
            var evidence = new SkinMeasurement { phase = phase, skin = b.skin.name, rootBone = b.skin.rootBone != null ? b.skin.rootBone.name : "null",
                quality = b.skin.quality + "/" + QualitySettings.skinWeights, vertices = rest.Length, bones = b.bones.Length, blendShapes = source.blendShapeCount,
                rootPosition = b.boss.transform.position, skinPosition = b.skin.transform.position, localScale = b.skin.transform.localScale,
                lossyScale = b.skin.transform.lossyScale, skinLocalToWorld = b.skin.transform.localToWorldMatrix,
                rootBoneLocalToWorld = b.skin.rootBone != null ? b.skin.rootBone.localToWorldMatrix : Matrix4x4.identity };
            for (int i = 0; i < rest.Length; i++)
            {
                var w = weights[i];
                reference[i] = WeightedPoint(matrices, rest[i], w.boneIndex0, w.weight0) + WeightedPoint(matrices, rest[i], w.boneIndex1, w.weight1)
                    + WeightedPoint(matrices, rest[i], w.boneIndex2, w.weight2) + WeightedPoint(matrices, rest[i], w.boneIndex3, w.weight3);
                evidence.maximumWeightSumError = Mathf.Max(evidence.maximumWeightSumError, Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f));
            }
            var referenceBounds = new Bounds(reference[0], Vector3.zero);
            foreach (var point in reference) referenceBounds.Encapsulate(point);
            evidence.referenceMinimum = referenceBounds.min; evidence.referenceMaximum = referenceBounds.max;
            var baked = new Mesh();
            try
            {
                // Keep the rejected route as diagnostic evidence, not as a fallback or a camera fit.
                b.skin.BakeMesh(baked, false);
                var noScale = baked.vertices;
                evidence.routes.Add(CompareBake("Bake(false) + full renderer matrix (rejected SavedField02 contract)", noScale, b.skin.transform.localToWorldMatrix, reference, out _));
                // This is the existing, visually validated Installer/RestPreview/RuntimeCapture contract.
                // Imported FBX renderer scale is non-unit; the bool must match those measurements.
                b.skin.BakeMesh(baked, true);
                var withScale = baked.vertices;
                var accepted = CompareBake("Bake(true) + full renderer matrix", withScale, b.skin.transform.localToWorldMatrix, reference, out var worldPoints);
                evidence.routes.Add(accepted);
                evidence.routes.Add(CompareBake("Bake(true) + translation/rotation only (diagnostic)", withScale,
                    Matrix4x4.TRS(b.skin.transform.position, b.skin.transform.rotation, Vector3.one), reference, out _));
                b.evidence.skinMeasurements.Add(evidence);
                // Persist coordinates and matrix evidence before any numerical or ground-collider gate.
                SaveReport();
                Assert.That(evidence.maximumWeightSumError, Is.LessThan(.0001f), b.evidence.label + " has non-normalized source weights.");
                // 2 mm is a computation-agreement gate at this Field's hundreds-of-metres world origin,
                // not permission for a 2 mm visual displacement or for changing the ground ray range.
                Assert.That(accepted.maximumError, Is.LessThan(.002f), b.evidence.label + " Bake(true) world positions disagree with independent LBS; inspect skinMeasurements.");
                return worldPoints;
            }
            finally { Object.Destroy(baked); }
        }
        static Vector3 WeightedPoint(Matrix4x4[] matrices, Vector3 point, int index, float weight)
        { return weight == 0f ? Vector3.zero : matrices[index].MultiplyPoint3x4(point) * weight; }
        static BakeComparison CompareBake(string route, Vector3[] baked, Matrix4x4 toWorld, Vector3[] reference, out Vector3[] world)
        {
            Assert.That(baked.Length, Is.EqualTo(reference.Length));
            world = new Vector3[baked.Length];
            var result = new BakeComparison { route = route };
            var bounds = new Bounds(toWorld.MultiplyPoint3x4(baked[0]), Vector3.zero);
            double sumSquared = 0;
            for (int i = 0; i < baked.Length; i++)
            {
                world[i] = toWorld.MultiplyPoint3x4(baked[i]); bounds.Encapsulate(world[i]);
                float error = Vector3.Distance(world[i], reference[i]);
                Assert.That(float.IsNaN(error) || float.IsInfinity(error), Is.False, route + " has non-finite coordinates.");
                sumSquared += (double)error * error;
                if (error > result.maximumError) { result.maximumError = error; result.worstVertex = i; result.worstPoint = world[i]; result.referencePoint = reference[i]; }
            }
            result.minimum = bounds.min; result.maximum = bounds.max;
            result.rootMeanSquareError = (float)Math.Sqrt(sumSquared / baked.Length);
            return result;
        }
        static int LowestWeighted(Vector3[] vertices, BoneWeight[] weights, HashSet<int> group, out int count)
        {
            count = 0; int lowest = -1;
            for (int i = 0; i < vertices.Length; i++)
            {
                var w = weights[i]; float sum = (group.Contains(w.boneIndex0)?w.weight0:0) + (group.Contains(w.boneIndex1)?w.weight1:0) + (group.Contains(w.boneIndex2)?w.weight2:0) + (group.Contains(w.boneIndex3)?w.weight3:0);
                if (sum <= .25f) continue;
                count++; if (lowest < 0 || vertices[i].y < vertices[lowest].y) lowest = i;
            }
            return lowest;
        }
        T[] SceneObjects<T>() where T : Component => loaded.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        void SaveReport() { if (report != null && output != null) File.WriteAllText(output + "/SavedFieldBosses.json", JsonUtility.ToJson(report, true)); }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (!initialized) yield break;
            SaveReport();
            if (world != null && world.Presentation != null) world.Presentation.Ultimate.Cancel();
            ExplorerJourney.Stop();
            var empty = SceneManager.CreateScene("Field final cleanup " + Guid.NewGuid().ToString("N")); SceneManager.SetActiveScene(empty);
            if (loaded.IsValid() && loaded.isLoaded) yield return SceneManager.UnloadSceneAsync(loaded);
            var router = Object.FindAnyObjectByType<M4RegionRouter>(); if (router != null) Object.Destroy(router.gameObject);
            yield return null;
            // The destroyed streamer owns the additive handles and releases them asynchronously.
            // Do not independently unload its environment scenes while those releases are pending.
            float cleanupDeadline = Time.realtimeSinceStartup + 60f;
            while (WorldRegionStreamer.OwnedSceneHandleCount != 0 && Time.realtimeSinceStartup < cleanupDeadline) yield return null;
            if (report != null) report.ownedSceneHandlesAfterCleanup = WorldRegionStreamer.OwnedSceneHandleCount;
            SaveReport(); M4Session.UseProgressForTests(null);
            Time.timeScale = originalTimeScale; Time.captureDeltaTime = originalCaptureDelta; Application.runInBackground = originalBackground;
            Cursor.lockState = originalCursorLock; Cursor.visible = originalCursorVisible;
            string resolved = Path.GetFullPath(temporaryProfile);
            Assert.That(Path.GetFileName(resolved), Does.StartWith("Orbis-Field-Final-"));
            Assert.That(Path.GetDirectoryName(resolved), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            Assert.That(Hash(ScenePath), Is.EqualTo(originalSceneHash), "PlayMode must not overwrite the saved Field.");
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount, Is.Zero, "Saved Field fixture must release its owned Addressables scene handles.");
        }
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string Arg(string key) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1] : null; }
    }
}
#endif
