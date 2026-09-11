using UnityEngine;

namespace Orbis.M4
{
    /// <summary>World-space feet positions; content systems add their own actors and interaction models.</summary>
    public sealed class M4RegionLayout
    {
        public Vector3 Spawn { get; }
        public Vector3[] PuzzleStatuePositions { get; }
        public Vector3 Chest { get; }
        public Vector3[] ChallengeTargets { get; }
        public Vector3 ChallengeEntry { get; }
        public Vector3 BossCenter { get; }
        public Vector3 Npc { get; }
        public Vector3 Portal { get; }

        internal M4RegionLayout(Vector3 spawn, Vector3[] puzzle, Vector3 chest, Vector3[] challenge,
            Vector3 challengeEntry, Vector3 boss, Vector3 npc, Vector3 portal)
        {
            Spawn = spawn; PuzzleStatuePositions = (Vector3[])puzzle.Clone(); Chest = chest;
            ChallengeTargets = (Vector3[])challenge.Clone(); ChallengeEntry = challengeEntry;
            BossCenter = boss; Npc = npc; Portal = portal;
        }
    }
}
