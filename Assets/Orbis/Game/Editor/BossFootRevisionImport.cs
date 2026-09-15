using System;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Orbis.Game.Editor
{
    /// <summary>Sequential explicit batch of fresh rest/motion candidates and actual same-camera poses.</summary>
    public static class BossFootRevisionImport
    {
        [Serializable] public sealed class Entry { public string name,restRun,motionRun,reviewRun,manifest,runtimeRun; }
        [Serializable] public sealed class Existing { public string source,run; }
        [Serializable] public sealed class Plan { public Entry[] entries; public Existing[] existingRuntimeSources; }
        public static void RunRequested()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-bossBatchPlan");
            if(i<0||i+1>=args.Length)throw new ArgumentException("Explicit batch JSON required.");
            string path=Path.GetFullPath(args[i+1]);
            string root=Path.GetFullPath("Tools/CharacterPipeline/BossBatchPlans")+Path.DirectorySeparatorChar;
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Reviewed local batch plan required.");
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(path));
            if(plan.entries==null||plan.entries.Length<1||plan.entries.Length>4||plan.entries.Select(e=>e.name).Distinct().Count()!=plan.entries.Length)
                throw new InvalidOperationException("One to four distinct original bosses required.");
            foreach(var entry in plan.entries)
            {
                BossPipelineImport.Import(entry.name,entry.restRun);
                string rest="Assets/Orbis/Game/Characters/BossCandidates/"+entry.restRun+"/"+entry.name;
                BossMotionImport.Import(Path.GetFullPath(entry.manifest),entry.motionRun,rest);
                BossMotionReview.Capture("Assets/Orbis/Game/Characters/BossMotionCandidates/"+entry.motionRun+"/"+entry.name,entry.reviewRun);
                if(!string.IsNullOrEmpty(entry.runtimeRun))BossRuntimeCandidate.Build("Assets/Orbis/Game/Characters/BossMotionCandidates/"+entry.motionRun+"/"+entry.name,entry.runtimeRun);
            }
            foreach(var existing in plan.existingRuntimeSources??Array.Empty<Existing>())BossRuntimeCandidate.Build(existing.source,existing.run);
            Debug.Log("ORBIS_ORIGINAL_BOSS_FOOT_REVISION_BATCH "+path);
        }
    }
}
