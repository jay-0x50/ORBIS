using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Orbis.Game.Editor
{
    // Also covers the normal Unity Build button, before Addressables' build callback reads scene assets.
    public sealed class FieldBuildExportHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue + 1;
        public void OnPreprocessBuild(BuildReport report)
        {
            FieldSceneAuthoring.EnsureExported();
            Orbis.EditorSupport.FieldSceneBuildPolicy.ValidateBuildLayout();
        }
    }
}
