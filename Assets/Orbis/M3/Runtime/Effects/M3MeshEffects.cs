using System;
using System.Collections.Generic;
using Orbis.M0;
using Orbis.M1;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.M3
{
    /// <summary>Prewarmed spline, ring and mesh presentation. Never changes collider or shield gameplay.</summary>
    public sealed class M3MeshEffects : MonoBehaviour
    {
        private sealed class Ring { public Transform Transform; public Renderer Renderer; public float Age; public bool Active; public ElementType Element; }
        private sealed class Chain { public LineRenderer Line; public Vector3[] Points = new Vector3[32]; public ElementalActor From, To; public float Age; public bool Active; }
        private sealed class Ghost { public Transform Root; public Transform[] Parts = Array.Empty<Transform>(); public Renderer[] Renderers = Array.Empty<Renderer>(); public MeshFilter[] Filters = Array.Empty<MeshFilter>(); public Mesh[] Baked = Array.Empty<Mesh>(); public float Age; public bool Active; public ElementType Element; }
        private sealed class AvatarBinding { public Renderer[] Body; public Material[][] Original, Appearance, GhostMaterials; public Renderer[] Outline; public TrailRenderer Trail; }
        private readonly Dictionary<Animator, AvatarBinding> avatars = new Dictionary<Animator, AvatarBinding>();
        private AvatarBinding currentAvatar;
        private readonly Ring[] rings = new Ring[8];
        private readonly Chain[] chains = new Chain[12];
        private readonly Ghost[] ghosts = new Ghost[6];
        private MaterialPropertyBlock block;
        private PartyManager party;
        private BasicAttackCombo combat;
        private Renderer[] body;
        private Material[][] originalMaterials;
        private Renderer[] outlineParts;
        private Renderer shield;
        private TrailRenderer trail;
        private Material dissolveMaterial;
        private bool dissolving;
        private float dissolveAge;
        private bool outlined;
        private Mesh ringMesh;
        private Mesh shieldMesh;
        private ElementType currentElement;
        public int ActiveChains { get; private set; }
        public int ActiveRings { get; private set; }
        public bool ShieldVisible => shield != null && shield.enabled;
        public bool TrailEmitting => trail != null && trail.emitting;

        public void Configure(PartyManager owner, BasicAttackCombo attack, Animator avatar)
        {
            party = owner; combat = attack;
            if (block == null)
            {
                block = new MaterialPropertyBlock();
                dissolveMaterial = RequireMaterial("Dissolve");
                ringMesh = BuildQuad(); shieldMesh = BuildCrystalSphere();
                for (int i = 0; i < rings.Length; i++)
                {
                    var go = MeshObject("Pooled Reaction Ring " + i, ringMesh, RequireMaterial("SwirlRing"));
                    go.SetActive(false);
                    rings[i] = new Ring { Transform = go.transform, Renderer = go.GetComponent<Renderer>() };
                }
                for (int i = 0; i < chains.Length; i++)
                {
                    var go = new GameObject("Pooled Electrical Spline " + i);
                    go.transform.SetParent(transform, false);
                    var line = go.AddComponent<LineRenderer>();
                    line.sharedMaterial = RequireMaterial("WeaponTrail"); line.positionCount = 32;
                    line.useWorldSpace = true; line.textureMode = LineTextureMode.Stretch;
                    line.startWidth = .065f; line.endWidth = .025f; line.numCapVertices = 2;
                    line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false; line.enabled = false;
                    chains[i] = new Chain { Line = line };
                }
                for (int i = 0; i < ghosts.Length; i++)
                {
                    var root = new GameObject("Pooled Dissolve Afterimage " + i).transform;
                    root.SetParent(transform, false); root.gameObject.SetActive(false);
                    ghosts[i] = new Ghost { Root = root };
                }
                shield = MeshObject("Active Member Crystal Shield", shieldMesh, RequireMaterial("CrystalShield")).GetComponent<Renderer>();
                shield.enabled = false;
            }
            currentElement = party.ActiveMember.Actor.Element;
            RebindAvatar(avatar, null);
        }

        /// <summary>Swap body bindings without removing an already emitted outgoing afterimage.</summary>
        public void RebindAvatar(Animator avatar, Transform weaponTrailAnchor)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            if (block == null) throw new InvalidOperationException("Configure M3 mesh effects before rebinding a visual.");
            RestoreBodyMaterials();
            SetOutline(false, currentElement);
            if (trail != null) { trail.Clear(); trail.emitting = false; }
            if (!avatars.TryGetValue(avatar, out AvatarBinding binding))
            {
                binding = CreateAvatarBinding(avatar);
                avatars.Add(avatar, binding);
            }
            if (weaponTrailAnchor == null && binding.Trail == null)
            {
                // Legacy M0 fallback. Imported weapons provide an explicit authored tip instead.
                foreach (Renderer renderer in binding.Body)
                    if (renderer.name == "SwordBlade")
                    {
                        var tip = new GameObject("M0 Weapon Trail Anchor").transform;
                        tip.SetParent(renderer.transform, false); tip.localPosition = Vector3.forward * .5f;
                        weaponTrailAnchor = tip; break;
                    }
            }
            if (binding.Trail == null && weaponTrailAnchor != null)
            {
                var go = new GameObject("Elemental Weapon Trail");
                go.transform.SetParent(weaponTrailAnchor, false);
                binding.Trail = go.AddComponent<TrailRenderer>();
                binding.Trail.sharedMaterial = RequireMaterial("WeaponTrail");
                // 기존 M3 기본값 유지: .16초 수명, .03m 샘플링, .28m 시작 폭.
                binding.Trail.time = .16f; binding.Trail.minVertexDistance = .03f;
                binding.Trail.startWidth = .28f; binding.Trail.endWidth = 0f;
                binding.Trail.emitting = false; binding.Trail.autodestruct = false;
                binding.Trail.shadowCastingMode = ShadowCastingMode.Off; binding.Trail.receiveShadows = false;
            }
            else if (binding.Trail != null && weaponTrailAnchor != null)
                binding.Trail.transform.SetParent(weaponTrailAnchor, false);
            currentAvatar = binding; body = binding.Body; originalMaterials = binding.Original;
            outlineParts = binding.Outline; trail = binding.Trail;
            EnsureGhostCapacity(body.Length);
            SetElement(currentElement);
        }

        private AvatarBinding CreateAvatarBinding(Animator avatar)
        {
            var renderers = new List<Renderer>();
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.StartsWith("Art Outline", StringComparison.Ordinal) ||
                    renderer.name.StartsWith("M3 Silhouette", StringComparison.Ordinal)) continue;
                if (renderer is SkinnedMeshRenderer || renderer.GetComponent<MeshFilter>() != null) renderers.Add(renderer);
            }
            var binding = new AvatarBinding
            {
                Body = renderers.ToArray(), Original = new Material[renderers.Count][],
                Appearance = new Material[renderers.Count][], GhostMaterials = new Material[renderers.Count][],
                Outline = new Renderer[renderers.Count]
            };
            Material outline = RequireMaterial("Outline");
            for (int i = 0; i < binding.Body.Length; i++)
            {
                Renderer source = binding.Body[i];
                Material[] original = source.sharedMaterials;
                binding.Original[i] = original;
                binding.Appearance[i] = new Material[original.Length];
                for (int j = 0; j < original.Length; j++)
                    binding.Appearance[i][j] = original[j] != null && original[j].HasProperty("_Threshold") ? original[j] : dissolveMaterial;
                Mesh mesh = source is SkinnedMeshRenderer skin ? skin.sharedMesh : source.GetComponent<MeshFilter>().sharedMesh;
                int submeshes = mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 1;
                var outlineMaterials = new Material[submeshes]; binding.GhostMaterials[i] = new Material[submeshes];
                for (int j = 0; j < submeshes; j++) { outlineMaterials[j] = outline; binding.GhostMaterials[i][j] = dissolveMaterial; }
                var part = new GameObject("M3 Silhouette " + source.name); part.transform.SetParent(source.transform, false);
                part.layer = source.gameObject.layer;
                Renderer duplicate;
                if (source is SkinnedMeshRenderer skinned)
                {
                    var copy = part.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = skinned.sharedMesh; copy.bones = skinned.bones; copy.rootBone = skinned.rootBone;
                    copy.localBounds = skinned.localBounds; copy.updateWhenOffscreen = true; copy.quality = skinned.quality;
                    duplicate = copy;
                }
                else
                {
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    duplicate = part.AddComponent<MeshRenderer>();
                }
                duplicate.sharedMaterials = outlineMaterials; duplicate.shadowCastingMode = ShadowCastingMode.Off;
                duplicate.receiveShadows = false; duplicate.enabled = false; binding.Outline[i] = duplicate;
            }
            return binding;
        }

        private void EnsureGhostCapacity(int capacity)
        {
            foreach (Ghost ghost in ghosts)
            {
                int old = ghost.Parts.Length;
                if (old >= capacity) continue;
                Array.Resize(ref ghost.Parts, capacity); Array.Resize(ref ghost.Renderers, capacity);
                Array.Resize(ref ghost.Filters, capacity); Array.Resize(ref ghost.Baked, capacity);
                for (int i = old; i < capacity; i++)
                {
                    var part = new GameObject("Snapshot " + i); part.transform.SetParent(ghost.Root, false);
                    ghost.Parts[i] = part.transform; ghost.Filters[i] = part.AddComponent<MeshFilter>();
                    ghost.Renderers[i] = part.AddComponent<MeshRenderer>();
                    ghost.Renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                    ghost.Renderers[i].receiveShadows = false; ghost.Renderers[i].enabled = false;
                    ghost.Baked[i] = new Mesh { name = "Pooled Skinned Snapshot " + i };
                    ghost.Baked[i].MarkDynamic();
                }
            }
        }
        private static Material RequireMaterial(string name)
        {
            var material = Resources.Load<Material>("M3/Materials/" + name);
            if (material == null) throw new System.InvalidOperationException("M3 Shader Graph material missing: " + name);
            return material;
        }

        private GameObject MeshObject(string name, Mesh mesh, Material material)
        {
            var item = new GameObject(name);
            item.transform.SetParent(transform, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return item;
        }

        public void SetElement(ElementType element)
        {
            currentElement = element;
            if (trail != null)
            {
                trail.Clear();
                block.Clear(); M3Palette.Set(block, element); trail.SetPropertyBlock(block);
                trail.startColor = Color.white; trail.endColor = new Color(1f, 1f, 1f, 0f);
            }
        }

        public void BeginAppearance()
        {
            RestoreBodyMaterials();
            dissolving = true; dissolveAge = 0f;
            for (int i = 0; i < body.Length; i++) body[i].sharedMaterials = currentAvatar.Appearance[i];
            UpdateAppearance(0f);
        }

        public void EmitAfterimage(ElementType element)
        {
            if (body == null) return;
            Ghost chosen = ghosts[0];
            foreach (Ghost ghost in ghosts)
            {
                if (!ghost.Active) { chosen = ghost; break; }
                if (ghost.Age > chosen.Age) chosen = ghost;
            }
            chosen.Active = true; chosen.Age = 0f; chosen.Element = element;
            chosen.Root.gameObject.SetActive(true);
            for (int i = 0; i < chosen.Parts.Length; i++)
            {
                bool visible = i < body.Length && body[i] != null && body[i].enabled && body[i].gameObject.activeInHierarchy;
                chosen.Renderers[i].enabled = visible;
                if (!visible) continue;
                if (body[i] is SkinnedMeshRenderer skin)
                {
                    // CPU snapshot is taken only on emission. Each pooled slot owns its mesh so later poses cannot mutate older ghosts.
                    skin.BakeMesh(chosen.Baked[i], true);
                    chosen.Filters[i].sharedMesh = chosen.Baked[i];
                }
                else chosen.Filters[i].sharedMesh = body[i].GetComponent<MeshFilter>().sharedMesh;
                chosen.Renderers[i].sharedMaterials = currentAvatar.GhostMaterials[i];
                chosen.Parts[i].SetPositionAndRotation(body[i].transform.position, body[i].transform.rotation);
                // BakeMesh(true) compensates renderer scale; apply world scale once on the static snapshot transform.
                Vector3 parentScale = chosen.Root.lossyScale, sourceScale = body[i].transform.lossyScale;
                chosen.Parts[i].localScale = new Vector3(sourceScale.x / parentScale.x, sourceScale.y / parentScale.y, sourceScale.z / parentScale.z);
                block.Clear(); M3Palette.Set(block, element, .12f); chosen.Renderers[i].SetPropertyBlock(block);
            }
        }
        public void EmitRing(Vector3 center, ElementType element)
        {
            Ring chosen = rings[0];
            foreach (Ring ring in rings)
            {
                if (!ring.Active) { chosen = ring; break; }
                if (ring.Age > chosen.Age) chosen = ring;
            }
            if (!chosen.Active) ActiveRings++;
            chosen.Active = true; chosen.Age = 0f; chosen.Element = element;
            chosen.Transform.position = center + Vector3.up * 0.035f;
            chosen.Transform.localScale = Vector3.one * 0.1f;
            chosen.Transform.gameObject.SetActive(true);
            block.Clear(); M3Palette.Set(block, element); chosen.Renderer.SetPropertyBlock(block);
        }

        public void EmitChain(ElementalActor from, ElementalActor to)
        {
            if (from == null || to == null) return;
            Chain chosen = chains[0];
            foreach (Chain chain in chains)
            {
                if (!chain.Active) { chosen = chain; break; }
                if (chain.Age > chosen.Age) chosen = chain;
            }
            if (!chosen.Active) ActiveChains++;
            chosen.Active = true; chosen.Age = 0f; chosen.From = from; chosen.To = to;
            chosen.Line.enabled = true;
            block.Clear(); M3Palette.Set(block, ElementType.Lightning); chosen.Line.SetPropertyBlock(block);
            UpdateChain(chosen);
        }

        public void SetOutline(bool visible, ElementType element)
        {
            outlined = visible;
            if (outlineParts == null) return;
            foreach (Renderer renderer in outlineParts)
            {
                if (renderer == null) continue;
                renderer.enabled = visible;
                block.Clear(); M3Palette.Set(block, element); block.SetFloat("_Thickness", 0.035f);
                renderer.SetPropertyBlock(block);
            }
        }

        private void LateUpdate()
        {
            if (party == null) return;
            float dt = Time.unscaledDeltaTime;
            foreach (Ring ring in rings)
            {
                if (!ring.Active) continue;
                ring.Age += dt;
                // Unspecified ring defaults: .65 s, 3 m radius, matching the existing spread range.
                float progress = Mathf.Clamp01(ring.Age / 0.65f);
                ring.Transform.localScale = Vector3.one * Mathf.Lerp(0.1f, 6f, Mathf.Sqrt(progress));
                block.Clear(); M3Palette.Set(block, ring.Element, progress); ring.Renderer.SetPropertyBlock(block);
                if (progress >= 1f) { ring.Active = false; ActiveRings--; ring.Transform.gameObject.SetActive(false); }
            }
            foreach (Chain chain in chains)
            {
                if (!chain.Active) continue;
                chain.Age += dt;
                if (chain.Age >= 0.3f || chain.From == null || chain.To == null ||
                    !chain.From.IsOnField || !chain.To.IsOnField)
                { chain.Active = false; ActiveChains--; chain.Line.enabled = false; continue; }
                UpdateChain(chain);
            }
            foreach (Ghost ghost in ghosts)
            {
                if (!ghost.Active) continue;
                ghost.Age += dt;
                float progress = Mathf.Clamp01(ghost.Age / 0.45f);
                foreach (Renderer renderer in ghost.Renderers)
                {
                    if (renderer == null) continue;
                    block.Clear(); M3Palette.Set(block, ghost.Element, progress); renderer.SetPropertyBlock(block);
                }
                if (progress >= 1f) { ghost.Active = false; ghost.Root.gameObject.SetActive(false); }
            }
            if (outlined && currentAvatar != null)
                for (int i = 0; i < body.Length; i++)
                    if (body[i] is SkinnedMeshRenderer source && outlineParts[i] is SkinnedMeshRenderer copy && source.sharedMesh != null)
                        for (int shape = 0; shape < source.sharedMesh.blendShapeCount; shape++) copy.SetBlendShapeWeight(shape, source.GetBlendShapeWeight(shape));
            if (dissolving) UpdateAppearance(dt);
            if (trail != null)
            {
                bool emit = combat.IsAttacking;
                if (!emit && trail.emitting) trail.emitting = false;
                else if (emit) trail.emitting = true;
            }
            var actor = party.ActiveMember.Actor;
            shield.enabled = actor.IsOnField && actor.ShieldAmount > 0f;
            if (shield.enabled)
            {
                shield.transform.position = actor.EffectCenter;
                shield.transform.localScale = new Vector3(0.9f, 1.2f, 0.9f);
                block.Clear(); M3Palette.Set(block, actor.ShieldElement,
                    actor.ShieldRemainingTime < 0.5f ? 1f - actor.ShieldRemainingTime / 0.5f : 0f);
                shield.SetPropertyBlock(block);
            }
        }

        private void UpdateAppearance(float dt)
        {
            dissolveAge += dt;
            float progress = 1f - Mathf.Clamp01(dissolveAge / 0.28f);
            foreach (Renderer renderer in body)
            {
                renderer.GetPropertyBlock(block);
                M3Palette.Set(block, currentElement, progress);
                block.SetFloat("_Threshold", progress);
                renderer.SetPropertyBlock(block); block.Clear();
            }
            if (progress <= 0f) RestoreBodyMaterials();
        }

        private void RestoreBodyMaterials()
        {
            if (dissolving && body != null)
                for (int i = 0; i < body.Length; i++)
                {
                    if (body[i] == null) continue;
                    body[i].sharedMaterials = originalMaterials[i];
                    body[i].GetPropertyBlock(block); block.SetFloat("_Threshold", 0f); block.SetFloat("_Progress", 0f);
                    body[i].SetPropertyBlock(block); block.Clear();
                }
            dissolving = false;
        }
        private void UpdateChain(Chain chain)
        {
            Vector3 start = chain.From.EffectCenter, end = chain.To.EffectCenter;
            Vector3 side = Vector3.Cross(end - start, Vector3.up).normalized;
            Vector3 control1 = Vector3.Lerp(start, end, 0.33f) + Vector3.up * 0.28f;
            Vector3 control2 = Vector3.Lerp(start, end, 0.67f) - Vector3.up * 0.15f;
            for (int i = 0; i < chain.Points.Length; i++)
            {
                float t = i / (float)(chain.Points.Length - 1), u = 1f - t;
                // Cubic Bezier spline, with a tapered electrical ripple; endpoints stay on the hit actors.
                chain.Points[i] = u*u*u*start + 3f*u*u*t*control1 + 3f*u*t*t*control2 + t*t*t*end +
                    side * Mathf.Sin(i * 2.1f + chain.Age * 75f) * Mathf.Sin(t * Mathf.PI) * 0.1f;
            }
            chain.Line.SetPositions(chain.Points);
            chain.Line.widthMultiplier = 1f - chain.Age / 0.3f;
        }

        public void Clear()
        {
            if (block == null) return;
            RestoreBodyMaterials(); SetOutline(false, currentElement);
            foreach (Ring ring in rings) if (ring != null) { ring.Active = false; ring.Transform.gameObject.SetActive(false); }
            foreach (Chain chain in chains) if (chain != null) { chain.Active = false; chain.Line.enabled = false; }
            foreach (Ghost ghost in ghosts) if (ghost != null) { ghost.Active = false; ghost.Root.gameObject.SetActive(false); }
            ActiveChains = ActiveRings = 0;
            if (trail != null) { trail.Clear(); trail.emitting = false; }
            if (shield != null) shield.enabled = false;
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            foreach (Ghost ghost in ghosts)
                if (ghost != null) foreach (Mesh mesh in ghost.Baked) if (mesh != null) Destroy(mesh);
            if (ringMesh != null) Destroy(ringMesh);
            if (shieldMesh != null) Destroy(shieldMesh);
        }

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "M3 Ring Quad" };
            mesh.vertices = new[] { new Vector3(-.5f,0f,-.5f), new Vector3(-.5f,0f,.5f), new Vector3(.5f,0f,.5f), new Vector3(.5f,0f,-.5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0,1,2, 0,2,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        private static Mesh BuildCrystalSphere()
        {
            // Original low-poly octahedron subdivided once, flat normals preserve crystal facets.
            Vector3[] corners = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[] faces = { 0,4,3, 0,3,5, 0,5,2, 0,2,4, 1,3,4, 1,5,3, 1,2,5, 1,4,2 };
            var vertices = new Vector3[96]; var uv = new Vector2[96]; var indices = new int[96];
            int at = 0;
            for (int i = 0; i < faces.Length; i += 3)
            {
                Vector3 a=corners[faces[i]], b=corners[faces[i+1]], c=corners[faces[i+2]];
                Vector3 ab=(a+b).normalized, bc=(b+c).normalized, ca=(c+a).normalized;
                Vector3[] tris = { a,ab,ca, ab,b,bc, ca,bc,c, ab,bc,ca };
                foreach (Vector3 vertex in tris) { vertices[at]=vertex; indices[at]=at; uv[at]=new Vector2(vertex.x*.5f+.5f,vertex.y*.5f+.5f); at++; }
            }
            var mesh = new Mesh { name = "M3 Faceted Shield" };
            mesh.vertices=vertices; mesh.triangles=indices; mesh.uv=uv; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}



