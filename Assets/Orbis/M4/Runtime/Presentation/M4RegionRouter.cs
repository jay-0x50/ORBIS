using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Orbis.M4
{
    /// <summary>One region at a time. A persistent loader survives Single scene unload and releases failures.</summary>
    public sealed class M4RegionRouter : MonoBehaviour
    {
        private static M4RegionRouter instance;
        private IM4LocalRegionTravel localWorld;
        public static M4RegionRouter Instance
        {
            get
            {
                if (instance == null)
                {
                    var root = new GameObject("M4 Addressable Region Loader");
                    instance = root.AddComponent<M4RegionRouter>();
                    DontDestroyOnLoad(root);
                }
                return instance;
            }
        }
        // Set only by the authored game entry; old milestone demos retain their Addressables route.
        public string AgniaSceneOverride { get; set; }
        public bool IsLoading { get; private set; }
        public float Progress { get; private set; }
        public string LastError { get; private set; }
        public M4RegionId Destination { get; private set; }
        public event Action<M4RegionId> Loaded;

        public static string Address(M4RegionId region)
        {
            M4RegionCatalog.Get(region);
            return "orbis.region." + region.ToString().ToLowerInvariant();
        }

        public void RegisterLocalWorld(IM4LocalRegionTravel world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (localWorld != null && !ReferenceEquals(localWorld,world))
                throw new InvalidOperationException("Only one shared island world can own local travel.");
            localWorld = world;
        }
        public void UnregisterLocalWorld(IM4LocalRegionTravel world)
        {
            if (ReferenceEquals(localWorld,world)) localWorld = null;
        }
        public bool Travel(M4RegionId region)
        {
            if (!Enum.IsDefined(typeof(M4RegionId), region) || IsLoading) return false;
            if (localWorld != null && localWorld.CanTravelWithinWorld(region))
            {
                if (!localWorld.TryTravelWithinWorld(region)) return false;
                Destination = region; Progress = 1f; LastError = null;
                Loaded?.Invoke(region);
                return true;
            }
            IsLoading = true; Destination = region; Progress = 0f; LastError = null;
            StartCoroutine(LoadRegion(region));
            return true;
        }

        private IEnumerator LoadRegion(M4RegionId region)
        {
            var previous = FindFirstObjectByType<M4SceneBootstrap>();
            if (previous != null) previous.PrepareTravel();
            // Single unload disposes the prior reaction singleton before the next region's Awake.
            // Addressables 2.11's default SceneReleaseMode releases successful handles on scene unload.
            if (region == M4RegionId.Agnia && !string.IsNullOrEmpty(AgniaSceneOverride) &&
                Application.CanStreamedLevelBeLoaded(AgniaSceneOverride))
            {
                AsyncOperation loading = null;
                try { loading = SceneManager.LoadSceneAsync(AgniaSceneOverride, LoadSceneMode.Single); }
                catch (Exception exception) { LastError = exception.Message; }
                if (loading == null && LastError == null) LastError = "Authored region load did not start.";
                if (loading != null)
                    while (!loading.isDone) { Progress = loading.progress; yield return null; }
                // The authored world's Start initializes selection-dependent systems after scene activation.
                yield return null;
                CompleteTravel(region, previous);
                yield break;
            }
            AsyncOperationHandle<SceneInstance> operation = default;
            try { operation = Addressables.LoadSceneAsync(Address(region), LoadSceneMode.Single, true); }
            catch (Exception exception) { LastError = exception.Message; }
            if (operation.IsValid())
            {
                while (!operation.IsDone) { Progress = operation.PercentComplete; yield return null; }
                if (operation.Status != AsyncOperationStatus.Succeeded)
                {
                    LastError = operation.OperationException != null ? operation.OperationException.Message : "Region load failed.";
                    Addressables.Release(operation);
                }
            }
            // Defer completion until Start has initialized an authored entry, if the loaded scene contains one.
            yield return null;
            CompleteTravel(region, previous);
        }

        private void CompleteTravel(M4RegionId region, M4SceneBootstrap previous)
        {
            IsLoading = false;
            if (LastError != null)
            {
                if (previous != null) previous.FinishTravel();
                Debug.LogWarning("M4 region travel failed: " + LastError, this);
                return;
            }
            Progress = 1f;
            var arrived = FindFirstObjectByType<M4SceneBootstrap>();
            if (arrived != null && arrived.IsInitialized) arrived.FinishTravel();
            Loaded?.Invoke(region);
        }
        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
