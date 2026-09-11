using System;
using Orbis.M2;
using UnityEngine;

namespace Orbis.M4
{
    /// <summary>A saved island district; every district shares the scene's player and reaction manager.</summary>
    [Serializable]
    public sealed class M4IslandRegion
    {
        public M4RegionId Id;
        public M4AuthoredRegion Authored;
        public Transform Center;
    }

    /// <summary>Owns one district's progress edge detection independently from the visible HUD district.</summary>
    public sealed class M4RegionRuntime
    {
        public M4RegionId Id { get; }
        public M4AuthoredRegion Authored { get; }
        public M4RegionLayout Layout { get; }
        public M4RegionContent Content { get; }
        public M4FieldBoss Boss { get; internal set; }
        public Vector3 Center { get; }
        public Vector3 SurveyPosition => Authored != null && Authored.Survey != null
            ? Authored.Survey.position : Layout.Portal + Vector3.forward * 3f;
        internal Renderer BossCore;
        internal bool PuzzleWasUnlocked;
        internal ChallengeState PreviousChallengeState;
        internal Action PuzzleChanged;
        internal Action ChallengeChanged;

        internal M4RegionRuntime(M4RegionId id, M4AuthoredRegion authored, M4RegionLayout layout,
            M4RegionContent content, Vector3 center)
        {
            Id = id; Authored = authored; Layout = layout; Content = content; Center = center;
            PuzzleWasUnlocked = content.Puzzle.Model.IsUnlocked;
            PreviousChallengeState = content.Challenge.State;
        }
    }

    /// <summary>Optional same-scene transport. Legacy demos continue to use the Addressables route.</summary>
    public interface IM4LocalRegionTravel
    {
        bool CanTravelWithinWorld(M4RegionId region);
        bool TryTravelWithinWorld(M4RegionId region);
    }
}
