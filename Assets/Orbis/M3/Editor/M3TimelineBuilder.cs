using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Orbis.M3.Editor
{
    /// <summary>Builds the actual five-stage Timeline and Signal assets used by the M3 ultimate director.</summary>
    public static class M3TimelineBuilder
    {
        public const string ResourceFolder = "Assets/Orbis/M3/Resources/M3/Timeline";
        public const string AssetPath = ResourceFolder + "/Ultimate.playable";
        private static readonly M3UltimateStage[] Stages =
        {
            M3UltimateStage.CloseUp, M3UltimateStage.SlowMotion, M3UltimateStage.ElementBurst,
            M3UltimateStage.SoundImpact, M3UltimateStage.RestoreAndResidue
        };
        private static readonly double[] Times = { 0d, 0.3d, 0.7d, 0.75d, 0.95d };

        public static TimelineAsset Build()
        {
            EnsureFolder(ResourceFolder);
            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(AssetPath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.name = "M3 Ultimate — Five Stages";
                timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
                timeline.fixedDuration = 1.05d;
                AssetDatabase.CreateAsset(timeline, AssetPath);
            }
            SignalTrack track = null;
            foreach (TrackAsset existing in timeline.GetOutputTracks())
                if (existing is SignalTrack signalTrack) { track = signalTrack; break; }
            if (track == null) track = timeline.CreateTrack<SignalTrack>(null, "Ultimate stages (unscaled)");
            for (int i = 0; i < Stages.Length; i++)
            {
                string signalPath = ResourceFolder + "/" + Stages[i] + ".signal";
                SignalAsset signal = AssetDatabase.LoadAssetAtPath<SignalAsset>(signalPath);
                if (signal == null)
                {
                    signal = ScriptableObject.CreateInstance<SignalAsset>();
                    signal.name = Stages[i].ToString();
                    AssetDatabase.CreateAsset(signal, signalPath);
                }
                SignalEmitter marker = null;
                foreach (IMarker existing in track.GetMarkers())
                    if (existing is SignalEmitter emitter && emitter.asset == signal) { marker = emitter; break; }
                if (marker == null)
                {
                    marker = track.CreateMarker<SignalEmitter>(Times[i]);
                    marker.name = (i + 1) + ". " + Stages[i];
                    marker.asset = signal;
                    marker.emitOnce = true;
                    marker.retroactive = true;
                    EditorUtility.SetDirty(marker);
                }
            }
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            Validate();
            return timeline;
        }

        public static void Validate()
        {
            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(AssetPath);
            if (timeline == null) throw new InvalidOperationException("M3 ultimate Timeline is missing.");
            if (timeline.durationMode != TimelineAsset.DurationMode.FixedLength || Math.Abs(timeline.fixedDuration - 1.05d) > 0.001d)
                throw new InvalidOperationException("M3 ultimate Timeline must use a 1.05 second fixed duration.");
            var found = new HashSet<M3UltimateStage>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!(track is SignalTrack)) continue;
                foreach (IMarker marker in track.GetMarkers())
                {
                    if (!(marker is SignalEmitter emitter) || emitter.asset == null ||
                        !Enum.TryParse(emitter.asset.name, out M3UltimateStage stage)) continue;
                    int index = Array.IndexOf(Stages, stage);
                    if (index < 0 || !found.Add(stage) || Math.Abs(emitter.time - Times[index]) > 0.001d ||
                        !emitter.retroactive || !emitter.emitOnce)
                        throw new InvalidOperationException("M3 ultimate signal timing or repetition flags are invalid: " + stage);
                }
            }
            if (found.Count != Stages.Length) throw new InvalidOperationException("M3 ultimate Timeline requires all five stage signals.");
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
