using UnityEngine;
namespace Orbis.M4
{
    /// <summary>Small player entry scene; the five world scenes live in local Addressables bundles.</summary>
    public sealed class M4Launcher : MonoBehaviour
    {
        private void Start() => M4RegionRouter.Instance.Travel(M4RegionId.Agnia);
        private void OnGUI()
        {
            var router = M4RegionRouter.Instance;
            GUI.Label(new Rect(24, 24, Screen.width - 48, 100),
                router.LastError ?? ("Loading ORBIS / " + (router.Progress * 100f).ToString("0") + "%"));
        }
    }
}
