using System;
using System.Collections.Generic;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using UnityEngine;

namespace Orbis.M4
{
    /// <summary>Serialized world references. Runtime initialization binds these objects without rebuilding or moving them.</summary>
    [DisallowMultipleComponent]
    public sealed class M4AuthoredRegion : MonoBehaviour
    {
        public Transform Spawn;
        public Transform Chest;
        public Transform ChallengeEntry;
        public Transform Npc;
        public Transform Portal;
        // Optional explicit survey marker; legacy scenes retain Portal + world-forward 3m.
        public Transform Survey;
        public ElementalActor[] Statues = Array.Empty<ElementalActor>();
        public ElementalActor[] Targets = Array.Empty<ElementalActor>();
        public GameObject BossObject;
        public Renderer[] StatueRenderers = Array.Empty<Renderer>();
        public Renderer[] ChallengeRenderers = Array.Empty<Renderer>();
        public Renderer ChestRenderer;
        public FieldElementPuzzle Puzzle;
        public ChallengeRoom Challenge;

        public void Validate()
        {
            if (Spawn == null || Chest == null || ChallengeEntry == null || Npc == null || Portal == null ||
                BossObject == null || ChestRenderer == null || Puzzle == null || Challenge == null)
                throw new InvalidOperationException("The authored region needs spawn, interaction markers, content components and renderers.");
            if (Statues == null || Targets == null || StatueRenderers == null || ChallengeRenderers == null ||
                Statues.Length != 3 || Targets.Length != 3 || StatueRenderers.Length != 3 || ChallengeRenderers.Length != 3)
                throw new InvalidOperationException("Bind exactly three ordered statues and challenge targets, with their body renderers.");
            var actors = new HashSet<ElementalActor>();
            for (int i = 0; i < 3; i++)
            {
                ValidateActor(Statues[i], actors);
                ValidateActor(Targets[i], actors);
                if (StatueRenderers[i] == null || ChallengeRenderers[i] == null ||
                    !StatueRenderers[i].transform.IsChildOf(Statues[i].transform) ||
                    !ChallengeRenderers[i].transform.IsChildOf(Targets[i].transform))
                    throw new InvalidOperationException("Each state renderer must belong to its bound actor.");
            }
            ValidateActor(BossObject.GetComponent<ElementalActor>(), actors);
        }

        private void ValidateActor(ElementalActor actor, HashSet<ElementalActor> actors)
        {
            if (actor == null || !actors.Add(actor) || actor.gameObject.scene != gameObject.scene ||
                actor.gameObject.layer != 9 || actor.GetComponent<TrainingDummy>() == null)
                throw new InvalidOperationException("Authored targets and boss must be distinct same-scene layer-9 actors with TrainingDummy.");
            bool hasHurtbox = false;
            foreach (Collider collider in actor.GetComponentsInChildren<Collider>(true))
                if (!collider.isTrigger && collider.gameObject.layer == 9) { hasHurtbox = true; break; }
            if (!hasHurtbox) throw new InvalidOperationException("An authored actor needs a non-trigger layer-9 hurtbox.");
        }

        public M4RegionLayout CreateLayout()
        {
            Validate();
            var statues = new Vector3[3];
            var targets = new Vector3[3];
            for (int i = 0; i < 3; i++) { statues[i] = Statues[i].transform.position; targets[i] = Targets[i].transform.position; }
            return new M4RegionLayout(Spawn.position, statues, Chest.position, targets, ChallengeEntry.position,
                BossObject.transform.position, Npc.position, Portal.position);
        }
    }
}
