#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Orbis.M0.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Orbis.Game.Editor
{
    // Explicit installation preparation API; neither menu nor automatic Assets mutation.
    public static class CharacterMotionProfileBuilder
    {
        [Serializable] sealed class Point { public float time, value; }
        [Serializable] sealed class Slot
        {
            public string slot, clip, source, sourceLicense, calibrationEvidence;
            public float duration, cycleDistanceMetres;
            public Point[] leftPlant, rightPlant;
        }
        [Serializable] sealed class Sole
        {
            public bool calibrated;
            public string sourceModelHash, evidence;
            public Vector3 solePointInFootLocal, soleNormalInFootLocal, forwardInFootLocal;
        }
        [Serializable] sealed class Manifest
        {
            public string character, sourceModelHash;
            public float humanScale;
            public Sole leftSole, rightSole;
            public Slot[] clips;
        }
        [Serializable] sealed class PoseEvidence { public bool finite; public Vector3 leftSole, rightSole; }
        [Serializable] sealed class ClipEvidence
        {
            public string slot, clip;
            public bool humanoid;
            public float previewScaleRatio;
            public PoseEvidence[] samples;
        }
        [Serializable] sealed class Evidence { public string sourceManifest, candidateModelSha256; public ClipEvidence[] clips; }
        public sealed class Result
        {
            public CharacterMotionProfile Profile;
            public AnimatorController Controller;
        }
        public static Result BuildReviewed(string manifestPath,string playbackEvidencePath,string outputFolder)
        {
            // Callers must first review the actual images/video. This verifies provenance and
            // completeness, not an artistic approval or actual gameplay/contact success.
            var source = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            var evidence = JsonUtility.FromJson<Evidence>(File.ReadAllText(playbackEvidencePath));
            if (evidence.sourceManifest != manifestPath || evidence.candidateModelSha256 != source.sourceModelHash)
                throw new InvalidOperationException("Playback evidence must match this exact manifest and model hash.");
            if (evidence.clips == null || evidence.clips.Length != source.clips.Length)
                throw new InvalidOperationException("Review every motion slot before building a runtime profile; a quick3-clip review is insufficient.");
            foreach (var slot in source.clips)
            {
                var reviewed = evidence.clips.SingleOrDefault(c => c.slot == slot.slot);
                if (reviewed == null || reviewed.clip != slot.clip || !reviewed.humanoid || reviewed.samples == null || reviewed.samples.Length < 61 ||
                    reviewed.samples.Any(p => !p.finite) || reviewed.previewScaleRatio <= 0f || float.IsNaN(reviewed.previewScaleRatio) || float.IsInfinity(reviewed.previewScaleRatio))
                    throw new InvalidOperationException("Incomplete/mismatched actual playback evidence: " + slot.slot);
            }
            if (source.humanScale < .7f || source.humanScale > 1.6f)
                throw new InvalidOperationException("This 1.8m hero pipeline requires the verified grounded Avatar; inspect origin/scale before installation.");
            outputFolder = outputFolder.Replace('\\','/').TrimEnd('/');
            if (!outputFolder.StartsWith("Assets/Orbis/Characters/Animation/",StringComparison.Ordinal) ||
                outputFolder.Split('/').Any(s => s == "." || s == "..") || Directory.Exists(outputFolder))
                throw new InvalidOperationException("Use a fresh candidate animation output folder.");
            var profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            profile.name = source.character + " Motion";
            profile.Clips = source.clips.Select(s => new HumanMotionClip
            {
                Slot = (HumanMotionSlot)Enum.Parse(typeof(HumanMotionSlot),s.slot),
                Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(s.clip),
                // Runtime fits the new Idle skin to1.8m. The author initially used the old
                // CC0 idle fit; transfer only the measured world stride by that fit ratio.
                // Foot-local sole coordinates remain invariant and must NOT be scaled twice.
                CycleDistanceMetres = s.cycleDistanceMetres*evidence.clips.Single(c => c.slot == s.slot).previewScaleRatio,
                LeftPlant = Curve(s.leftPlant), RightPlant = Curve(s.rightPlant),
                SourceLicense = s.source + " | " + s.sourceLicense,
                CalibrationEvidence = manifestPath + " | " + playbackEvidencePath + " | new-Idle world stride scale ratio=" +
                    evidence.clips.Single(c => c.slot == s.slot).previewScaleRatio.ToString("R",System.Globalization.CultureInfo.InvariantCulture) + " | " + s.calibrationEvidence
            }).ToArray();
            var idle = evidence.clips.Single(c => c.slot == "Idle").samples[0];
            profile.LeftSole = Calibration(source.leftSole,manifestPath,playbackEvidencePath,idle.leftSole);
            profile.RightSole = Calibration(source.rightSole,manifestPath,playbackEvidencePath,idle.rightSole);
            try { profile.ValidateForBuild(); }
            catch { UnityEngine.Object.DestroyImmediate(profile); throw; }
            Directory.CreateDirectory(outputFolder); AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(profile,outputFolder + "/MotionProfile.asset");
            var controller = CharacterMotionControllerBuilder.Build(profile,outputFolder);
            AssetDatabase.SaveAssets();
            return new Result { Profile = profile,Controller = controller };
        }
        static HumanSoleCalibration Calibration(Sole source,string manifest,string evidence,Vector3 neutral) => new HumanSoleCalibration
        {
            Calibrated = source.calibrated, SolePointInFootLocal = source.solePointInFootLocal,
            NeutralSoleInRoot = neutral,
            SoleNormalInFootLocal = source.soleNormalInFootLocal, ForwardInFootLocal = source.forwardInFootLocal,
            SourceModelHash = source.sourceModelHash, Evidence = manifest + " | " + evidence + " | " + source.evidence
        };
        static AnimationCurve Curve(Point[] source)
        {
            if (source == null || source.Length == 0) throw new InvalidOperationException("Missing contact annotation.");
            var result = new AnimationCurve(source.Select(p => new Keyframe(p.time,p.value)).ToArray());
            for (int i = 0; i < result.length; ++i)
            {
                AnimationUtility.SetKeyLeftTangentMode(result,i,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(result,i,AnimationUtility.TangentMode.Linear);
            }
            return result;
        }
    }
}
#endif
