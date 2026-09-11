using System;
using Orbis.M1;
using UnityEngine;
using UnityEngine.VFX;

namespace Orbis.M3
{
    public enum M3EffectKind { Impact, Vaporize, ElectroCharged, Overload, Swirl, Crystallize, Burst, Residue }

    /// <summary>Fixed, prewarmed GPU effect pool. Hits never Instantiate/Destroy or grow this pool.</summary>
    public sealed class M3VfxPool : MonoBehaviour
    {
        private sealed class Entry
        {
            public VisualEffect Vfx;
            public M3EffectKind Kind;
            public float Age;
            public float Lifetime;
            public bool Active;
        }

        private Entry[] entries;
        public int Capacity => entries == null ? 0 : entries.Length;
        public int ActiveCount { get; private set; }
        public int PeakActiveCount { get; private set; }
        public int RecycledWhileActive { get; private set; }
        public int PlayCount { get; private set; }
        public M3EffectKind LastPlayed { get; private set; }
        public bool AutoTick { get; set; } = true;
        public bool IsReady => entries != null;

        public void Prewarm()
        {
            if (IsReady) return;
            // Unspecified capacity defaults: 12 concurrent impacts, 4 of each other effect (40 total).
            entries = new Entry[40];
            int index = 0;
            foreach (M3EffectKind kind in Enum.GetValues(typeof(M3EffectKind)))
            {
                var asset = Resources.Load<VisualEffectAsset>("M3/Effects/" + kind);
                if (asset == null) throw new InvalidOperationException("M3 VFX Graph missing: " + kind);
                int count = kind == M3EffectKind.Impact ? 12 : 4;
                for (int i = 0; i < count; i++)
                {
                    var item = new GameObject("Pooled " + kind + " " + i);
                    item.SetActive(false);
                    item.transform.SetParent(transform, false);
                    var vfx = item.AddComponent<VisualEffect>();
                    vfx.visualEffectAsset = asset;
                    vfx.resetSeedOnPlay = true;
                    entries[index++] = new Entry { Vfx = vfx, Kind = kind };
                }
            }
        }

        public VisualEffect Play(M3EffectKind kind, Vector3 position, ElementType element, float scale = 1f)
        {
            if (!isActiveAndEnabled) return null;
            if (!IsReady) throw new InvalidOperationException("Prewarm the M3 pool during scene setup.");
            Entry chosen = null;
            foreach (Entry item in entries)
            {
                if (item.Kind != kind) continue;
                if (!item.Active) { chosen = item; break; }
                if (chosen == null || item.Age > chosen.Age) chosen = item;
            }
            if (chosen == null) return null;
            if (chosen.Active)
            {
                // Saturation replaces the oldest effect of the same kind; no allocations or unbounded bursts.
                RecycledWhileActive++;
                Release(chosen);
            }
            chosen.Age = 0f;
            // All one-shot graphs emit at t=0. Four-second residue uses a separate graph/lifetime.
            chosen.Lifetime = kind == M3EffectKind.Residue ? 4f : 3f;
            chosen.Active = true;
            chosen.Vfx.transform.SetPositionAndRotation(position, Quaternion.identity);
            chosen.Vfx.transform.localScale = Vector3.one;
            chosen.Vfx.gameObject.SetActive(true);
            // Vaporize steam is white; its pale edge uses the exact Water secondary palette.
            Color primary = kind == M3EffectKind.Vaporize ? Color.white : M3Palette.Primary(element);
            chosen.Vfx.SetVector4("PrimaryColor", primary.linear);
            chosen.Vfx.SetVector4("SecondaryColor", M3Palette.Secondary(element).linear);
            chosen.Vfx.SetFloat("Scale", Mathf.Max(0.05f, scale));
            chosen.Vfx.pause = false;
            chosen.Vfx.playRate = 1f;
            // Reinit emits initial OnPlay itself. Calling Play as well would double each burst.
            chosen.Vfx.Reinit();
            ActiveCount++;
            PeakActiveCount = Mathf.Max(PeakActiveCount, ActiveCount);
            PlayCount++;
            LastPlayed = kind;
            return chosen.Vfx;
        }

        private void Update() { if (AutoTick) Tick(Time.unscaledDeltaTime); }

        public void Tick(float unscaledDeltaTime)
        {
            if (!IsReady || unscaledDeltaTime <= 0f) return;
            foreach (Entry item in entries)
            {
                if (!item.Active) continue;
                item.Age += unscaledDeltaTime;
                if (item.Age >= item.Lifetime) Release(item);
            }
        }

        private void Release(Entry item)
        {
            item.Vfx.Stop();
            item.Vfx.gameObject.SetActive(false);
            item.Active = false;
            ActiveCount--;
        }

        public void Clear()
        {
            if (!IsReady) return;
            foreach (Entry item in entries) if (item != null && item.Active) Release(item);
        }

        private void OnDisable() => Clear();
    }
}
