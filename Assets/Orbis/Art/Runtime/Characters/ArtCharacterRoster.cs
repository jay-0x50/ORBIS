using System;
using System.Collections.Generic;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M3;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Art
{
    /// <summary>Prewarmed visual-only party roster. Gameplay actors, controller and camera stay on the shared pawn.</summary>
    [DisallowMultipleComponent]
    public sealed class ArtCharacterRoster : MonoBehaviour
    {
        private sealed class View
        {
            public GameObject Root;
            public Animator Animator;
            public Transform TrailAnchor;
        }
        private readonly Dictionary<ElementType, View> views = new Dictionary<ElementType, View>();
        private PartyManager party;
        private PlayerMotor motor;
        private BasicAttackCombo combat;
        private ExplorationMotor traversal;
        private M3MeshEffects meshEffects;
        private M3Presentation presentation;
        private Transform visualHost;
        private bool subscribed;
        private View active;
        private View permanentView;
        public GameObject PermanentVisual => permanentView?.Root;
        public string PermanentSourceId { get; private set; }
        public bool UsesExplorerModel { get; private set; }
        public Animator ActiveAnimator => active?.Animator;
        public GameObject ActiveVisual => active?.Root;
        public ElementType ActiveElement { get; private set; }
        public int PrewarmedCount => views.Count;

        public void Configure(M4SceneBootstrap scene, ArtAssetCatalog catalog)
        {
            if (scene == null || catalog == null || catalog.Characters == null)
                throw new ArgumentNullException("Character art requires a scene and catalog.");
            if (visualHost != null) throw new InvalidOperationException("A scene character roster may only be configured once.");
            var elements = new HashSet<ElementType>();
            foreach (ArtCharacterAsset character in catalog.Characters)
                if (character == null || character.Element == ElementType.None || !elements.Add(character.Element) ||
                    character.Prefab == null || character.Controller == null)
                    throw new ArgumentException("Each art character needs a unique element, prefab and seven-state controller.");
            if (elements.Count != 5) throw new ArgumentException("The art roster requires all five existing elemental characters.");
            party = scene.Party; traversal = scene.Traversal;
            motor = traversal.GetComponent<PlayerMotor>(); combat = motor.GetComponent<BasicAttackCombo>();
            presentation = scene.Presentation;
            meshEffects = presentation.MeshEffects;
            Animator placeholder = motor.GetComponentInChildren<Animator>();

            visualHost = new GameObject("Art Character Visuals").transform;
            visualHost.SetParent(motor.transform, false);
            foreach (ArtCharacterAsset character in catalog.Characters)
            {
                View view = BuildView(character, catalog);
                views.Add(character.Element, view);
                // Allocate outlines, trails and reusable BakeMesh buffers once during initial loading.
                meshEffects.RebindAvatar(view.Animator, view.TrailAnchor);
                view.Root.SetActive(false);
            }
            if (party.PermanentMember != null)
            {
                PermanentSourceId = party.PermanentMember.Actor.SourceId;
                ArtCharacterAsset explorer = catalog.Explorer(PermanentSourceId);
                if (explorer != null)
                {
                    if (explorer.Prefab == null || explorer.Controller == null || explorer.Avatar == null || !explorer.Avatar.isHuman)
                        throw new ArgumentException("The selected explorer art needs a prefab, Humanoid Avatar and seven-state controller.");
                    // Only the selected protagonist is instantiated. Its five elements share this same visual identity.
                    permanentView = BuildView(explorer,catalog);
                    UsesExplorerModel = true;
                    meshEffects.RebindAvatar(permanentView.Animator,permanentView.TrailAnchor);
                    permanentView.Root.SetActive(false);
                }
                else if (placeholder != null)
                    permanentView = new View { Root = placeholder.gameObject, Animator = placeholder };
            }            combat.CancelAttack();
            if (placeholder != null) placeholder.gameObject.SetActive(false);
            ShowMember(party.ActiveMember);
            if (meshEffects.isActiveAndEnabled) meshEffects.BeginAppearance();
            Subscribe();
        }

        private View BuildView(ArtCharacterAsset asset, ArtAssetCatalog catalog)
        {
            var root = new GameObject("Art " + asset.DisplayName);
            root.transform.SetParent(visualHost, false);
            GameObject model = Instantiate(asset.Prefab, root.transform, false);
            model.name = asset.DisplayName + " Model";
            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = asset.Controller;
            if (asset.Avatar != null) animator.avatar = asset.Avatar;
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Play("Idle", 0, 0f); animator.Update(0f);
            NormalizeVisibleModelHeight(root, 1.8f, root.transform.position);
            Transform trailAnchor = null;
            if (asset.Weapon != null)
            {
                Transform hand = FindBone(model.transform, asset.WeaponBoneName);
                if (hand == null && animator.isHuman) hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand == null) hand = FindBone(model.transform, "handslot.r") ?? FindBone(model.transform, "hand.r");
                if (hand == null) throw new InvalidOperationException("Right-hand weapon socket missing: " + asset.DisplayName);
                GameObject weapon = Instantiate(asset.Weapon, hand, false);
                weapon.name = asset.DisplayName + " Weapon";
                weapon.transform.localPosition = asset.WeaponLocalPosition;
                weapon.transform.localRotation = Quaternion.Euler(asset.WeaponLocalEuler);
                weapon.transform.localScale = asset.WeaponLocalScale;
                trailAnchor = new GameObject("Weapon Trail Tip").transform;
                trailAnchor.SetParent(weapon.transform, false);
                trailAnchor.localPosition = asset.TrailTipLocalPosition;
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            SetPlayerLayer(root.transform);
            ArtStyle.ApplyToHierarchy(root, catalog, true);
            return new View { Root = root, Animator = animator, TrailAnchor = trailAnchor };
        }

        /// <summary>Fit actual posed geometry to a world height and feet point; call before attaching weapons or outlines.</summary>
        public static void NormalizeVisibleModelHeight(GameObject root, float targetHeight, Vector3 feet)
        {
            if (root == null || targetHeight <= 0f || float.IsNaN(targetHeight) || float.IsInfinity(targetHeight))
                throw new ArgumentException("A visible model and positive finite target height are required.");
            bool found = false;
            Bounds bounds = default;
            var vertices = new List<Vector3>();
            var baked = new Mesh { name = "Character normalization scratch" };
            try
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        if (skin.sharedMesh == null) continue;
                        // Imported bounds may cover a rest pose or only one armor piece. Measure the actual idle skin.
                        skin.updateWhenOffscreen = true;
                        baked.Clear();
                        // useScale=true compensates renderer scale; TransformPoint below applies world scale exactly once.
                        skin.BakeMesh(baked, true);
                        mesh = baked;
                    }
                    else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                    else continue;
                    if (mesh == null) continue;
                    vertices.Clear(); mesh.GetVertices(vertices);
                    Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in vertices)
                    {
                        Vector3 point = toWorld.MultiplyPoint3x4(vertex);
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
            }
            finally
            {
                if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            }
            if (!found || bounds.size.y <= .001f || float.IsNaN(bounds.size.y) || float.IsInfinity(bounds.size.y))
                throw new InvalidOperationException("Character model has no measurable visible body.");
            // 기획서 미정: 무기 장착 전 실제 몸 정점 전체를 높이 1.8m로 맞춘다. 머리/몸 비율은 유지한다.
            float factor = targetHeight / bounds.size.y;
            Vector3 pivot = root.transform.position;
            root.transform.localScale *= factor;
            // Uniform scaling moves the measured bounds around the root pivot; no stale Renderer.bounds read is needed.
            Vector3 scaledCenter = pivot + (bounds.center - pivot) * factor;
            float scaledFeet = pivot.y + (bounds.min.y - pivot.y) * factor;
            root.transform.position += new Vector3(feet.x - scaledCenter.x,
                feet.y - scaledFeet, feet.z - scaledCenter.z);
        }
        private void ShowMember(PartyMember member)
        {
            if (member == null) return;
            View incoming;
            if (member.IsPermanent) incoming = permanentView;
            else views.TryGetValue(member.Actor.Element, out incoming);
            if (incoming == null) return;
            // PartyManager has already cancelled the outgoing attack. No gameplay reset or new actor is created here.
            if (active != null) active.Root.SetActive(false);
            active = incoming; ActiveElement = member.Actor.Element;
            active.Root.SetActive(true);
            motor.SetVisualAnimator(active.Animator);
            combat.SetVisualAnimator(active.Animator);
            traversal.SetVisualAnimator(active.Animator);
            meshEffects.RebindAvatar(active.Animator, active.TrailAnchor);
            meshEffects.SetElement(member.Actor.Element);
        }

        private static Transform FindBone(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == name) return bone;
            return null;
        }
        private static void SetPlayerLayer(Transform root)
        {
            root.gameObject.layer = 10;
            foreach (Transform child in root) SetPlayerLayer(child);
        }
        private void Subscribe()
        {
            if (subscribed || party == null || presentation == null || !isActiveAndEnabled) return;
            // M3 invokes this explicitly after capturing the outgoing avatar, regardless of event subscription order.
            presentation.AvatarSwitchRequested += ShowMember;
            party.ActiveMemberChanged += OnMemberChangedWhilePresentationDisabled;
            party.ActiveElementChanged += OnElementChanged;
            subscribed = true;
        }
        private void OnElementChanged(PartyMember member)
        {
            // Do not activate/deactivate or rebind an animator when the same explorer changes element.
            if (member == party.ActiveMember) ActiveElement = member.Actor.Element;
        }

        private void OnMemberChangedWhilePresentationDisabled(PartyMember member)
        {
            if (presentation != null && presentation.isActiveAndEnabled) return;
            // Disabled effects have no LateUpdate to finish an appearance; keep the replacement fully visible.
            ShowMember(member);
        }
        private void Unsubscribe()
        {
            if (subscribed)
            {
                if (party != null) { party.ActiveMemberChanged -= OnMemberChangedWhilePresentationDisabled; party.ActiveElementChanged -= OnElementChanged; }
                if (presentation != null) presentation.AvatarSwitchRequested -= ShowMember;
            }
            subscribed = false;
        }
        private void OnEnable()
        {
            Subscribe();
            if (party == null || visualHost == null || views.Count == 0) return;
            ShowMember(party.ActiveMember);
            if (meshEffects.isActiveAndEnabled) meshEffects.BeginAppearance();
        }
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();
    }
}



