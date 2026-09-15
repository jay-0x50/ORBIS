using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M0.Animation;
using Orbis.M4;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    // Explicit opt-in evidence. Static test geometry only: no production terrain or locomotion tuning.
    public static class CharacterMotionTerrainChecks
    {
        [Serializable] sealed class SurfaceFoot
        {
            public int frame; public string side;
            public int[] pointIds, triangles;
            public float[] signedDistances;
            public Vector3[] normals;
        }
        [Serializable] sealed class Evidence
        {
            public string scope = "Actual keyboard-controlled Animator/motor/IK. Surface samples are raycasts on the unchanged test collider at the saved actual skin vertices, not bone/flat-Y approximations.";
            public string scenario; public float slopeDegrees, controllerSlopeLimit;
            public Vector3 origin; public int frames;
            public float minimumDistance, maximumDistance;
            public int missingSurfaceSamples;
            public List<SurfaceFoot> feet = new List<SurfaceFoot>();
        }

        public static IEnumerator Run(M4SceneBootstrap world, Animator animator, PlayerMotor motor,
            M0Input input, Keyboard keyboard, string stage, string run, string character, string output, Material material)
        {
            var driver = animator.GetComponent<HumanAnimationDriver>();
            var controller = motor.GetComponent<CharacterController>();
            Assert.That(driver != null && driver.IsReady && controller != null, Is.True);
            var orbit = Object.FindAnyObjectByType<M0CameraRig>();
            bool downhillOnly = Environment.GetCommandLineArgs().Contains("-motionTerrainDownhillOnly");
            foreach (string scenario in downhillOnly ? new[] { "Downhill24" } : new[] { "Slope12", "Slope24", "CrossSlopeTurns" })
            {
                float angle = scenario == "Slope12" ? 12f : 24f;
                bool turns = scenario == "CrossSlopeTurns";
                bool downhill = scenario == "Downhill24";
                Assert.That(angle, Is.LessThan(controller.slopeLimit));
                var origin = new Vector3(3200, 100, 3000);
                var owner = new GameObject("Motion terrain evidence / " + scenario);
                owner.layer = 8; owner.transform.position = origin;
                var mesh = SurfaceMesh(angle, downhill ? 36f : turns ? 24f : 12f);
                owner.AddComponent<MeshFilter>().sharedMesh = mesh;
                owner.AddComponent<MeshRenderer>().sharedMaterial = material;
                var collider = owner.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                var recorder = owner.AddComponent<CharacterMotionRecorder>();
                try
                {
                    Assert.That(Directory.Exists(Path.Combine(output, scenario)), Is.False, "Preserve previous terrain evidence.");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    world.Presentation.Ultimate.Cancel();
                    float z = downhill ? 30f : turns ? 8f : -3f;
                    world.Traversal.Teleport(origin + new Vector3(0, Mathf.Tan(angle * Mathf.Deg2Rad) * Mathf.Max(0, z) + .03f, z));
                    motor.transform.rotation = downhill ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
                    orbit.SetOrbit(0, 15); input.SetPresentationLocked(false); driver.ResetPresentation();
                    Physics.SyncTransforms();
                    for (int i = 0; i < 35; i++) yield return null;
                    Assert.That(motor.IsGrounded, Is.True);
                    recorder.Begin(stage, run, character, Path.Combine(output, scenario), animator, motor, input, keyboard,
                        origin, i => downhill ? DownhillKeys(i) : Keys(i, turns), i => downhill ? DownhillPhase(i) : Phase(i, turns));
                    recorder.Report.recipeVersion = 3;
                    // A +4.5m uphill camera at the flat recipe's height can be underground.
                    // Terrain-only raised view; record it explicitly and keep flat captures unchanged.
                    recorder.Report.cameraWorldOffset = new Vector3(3.2f, 3f, 4.5f);
                    recorder.Report.groundMetric = "Static ramp collider sampled per actual fixed skin vertex; see terrain_surface.json. No flat Y contact assertions.";
                    recorder.Report.phases = downhill
                        ? new[] { "0..29 Idle on upper slope", "30..119 S downhill", "120..179 S+Shift downhill", "180..209 Stop", "210..269 W uphill return", "270..299 Stop" }
                        : turns
                        ? new[] { "0..29 Idle on slope", "30..89 W", "90..149 A (90-degree input)", "150..209 D (180-degree input)", "210..239 Stop", "240..269 W+Shift", "270..299 Stop" }
                        : new[] { "0..29 Idle", "30..119 W uphill", "120..179 W+Shift over crest", "180..209 Stop", "210..269 S downhill", "270..299 Stop" };
                    float deadline = Time.realtimeSinceStartup + 600;
                    while (recorder.Recording && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(recorder.Error, Is.Null.Or.Empty, recorder.Error);
                    Assert.That(recorder.Recording, Is.False);
                    Assert.That(recorder.FrameCount, Is.EqualTo(300));
                    Assert.That(recorder.Report.frames.All(f => f.finite), Is.True);
                    Assert.That(recorder.Report.frames.Any(f => f.measuredSpeed > 2), Is.True);
                    Assert.That(recorder.Report.frames.Any(f => f.rootPosition.y > origin.y + 1), Is.True,
                        "Must traverse actual inclined geometry, not simply record on flat ground.");
                    var proof = new Evidence { scenario = scenario, slopeDegrees = angle, origin = origin,
                        controllerSlopeLimit = controller.slopeLimit, frames = recorder.FrameCount,
                        minimumDistance = float.PositiveInfinity, maximumDistance = float.NegativeInfinity };
                    foreach (var frame in recorder.Report.frames)
                    foreach (var foot in frame.skinFeet)
                    {
                        var surface = new SurfaceFoot { frame = frame.index, side = foot.side, pointIds = foot.pointIds,
                            triangles = new int[foot.pointIds.Length], signedDistances = new float[foot.pointIds.Length],
                            normals = new Vector3[foot.pointIds.Length] };
                        for (int i = 0; i < foot.pointIds.Length; i++)
                        {
                            Vector3 p = foot.pointsWorld[i];
                            if (collider.Raycast(new Ray(p + Vector3.up * 5, Vector3.down), out var hit, 15))
                            {
                                float d = Vector3.Dot(p - hit.point, hit.normal);
                                surface.triangles[i] = hit.triangleIndex; surface.signedDistances[i] = d;
                                surface.normals[i] = hit.normal;
                                proof.minimumDistance = Mathf.Min(proof.minimumDistance, d);
                                proof.maximumDistance = Mathf.Max(proof.maximumDistance, d);
                            }
                            else { surface.triangles[i] = -1; proof.missingSurfaceSamples++; }
                        }
                        proof.feet.Add(surface);
                    }
                    File.WriteAllText(Path.Combine(output, scenario, "terrain_surface.json"), JsonUtility.ToJson(proof, true));
                    Assert.That(proof.missingSurfaceSamples, Is.Zero, "All measured soles must remain over the owned test surface.");
                    // Numeric proof is deliberately descriptive. Visual contact/turn acceptance requires reviewing the actual frames.
                }
                finally { recorder.StopAndRelease(); Object.Destroy(owner); Object.Destroy(mesh); }
                yield return null;
            }
        }
        // Dedicated descent: start on a long ramp, not the old crest recipe's six inclined frames.
        static Key[] DownhillKeys(int i)
        {
            if (i >= 30 && i < 120) return new[] { Key.S };
            if (i >= 120 && i < 180) return new[] { Key.S, Key.LeftShift };
            if (i >= 210 && i < 270) return new[] { Key.W };
            return Array.Empty<Key>();
        }
        static string DownhillPhase(int i) => i < 30 ? "Idle" : i < 120 ? "Downhill" :
            i < 180 ? "DownhillRun" : i < 210 ? "Stop" : i < 270 ? "UphillReturn" : "FinalStop";
        static Key[] Keys(int i, bool turns)
        {
            if (turns)
            {
                if (i >= 30 && i < 90) return new[] { Key.W };
                if (i >= 90 && i < 150) return new[] { Key.A };
                if (i >= 150 && i < 210) return new[] { Key.D };
                if (i >= 240 && i < 270) return new[] { Key.W, Key.LeftShift };
            }
            else
            {
                if (i >= 30 && i < 120) return new[] { Key.W };
                if (i >= 120 && i < 180) return new[] { Key.W, Key.LeftShift };
                if (i >= 210 && i < 270) return new[] { Key.S };
            }
            return Array.Empty<Key>();
        }
        static string Phase(int i, bool turns) => turns
            ? i < 30 ? "Idle" : i < 90 ? "AcrossW" : i < 150 ? "Turn90A" : i < 210 ? "Turn180D" : i < 240 ? "Stop" : i < 270 ? "Run" : "FinalStop"
            : i < 30 ? "Idle" : i < 120 ? "Uphill" : i < 180 ? "CrestRun" : i < 210 ? "Stop" : i < 270 ? "Downhill" : "FinalStop";
        static Mesh SurfaceMesh(float angle, float length)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            // Shared seam vertices prevent an artificial collision step. 12/24 degrees are validation conditions only.
            foreach (float z in new[] { -24f, 0f, length, 48f })
            {
                float y = Mathf.Tan(angle * Mathf.Deg2Rad) * Mathf.Clamp(z, 0, length);
                vertices.Add(new Vector3(-24, y, z)); vertices.Add(new Vector3(24, y, z));
            }
            for (int i = 0; i < 3; i++)
            { int a = i * 2; triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            var mesh = new Mesh { name = "Continuous validation ramp " + angle };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
