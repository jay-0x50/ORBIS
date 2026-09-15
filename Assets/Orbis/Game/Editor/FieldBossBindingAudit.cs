using System;
using System.IO;
using System.Linq;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.Game.Editor
{
    /// <summary>Read-only inventory for a narrowly scoped replacement of saved boss visuals.</summary>
    public static class FieldBossBindingAudit
    {
        [Serializable] public sealed class Child
        {
            public string name,prefab;public string[] components;
            public int renderers,enabledColliders,allColliders;
            public bool hasWeaknessCore;public Vector3 localPosition,localScale;
        }
        [Serializable] public sealed class Boss
        {
            public string region,name,actorId;public Vector3 position,euler,scale;
            public string[] rootComponents;public Child[] children;
        }
        [Serializable] public sealed class Report
        {
            public string scene="Assets/Scenes/Field.unity";
            public string policy="Read-only inventory. No scene save, actor replacement or terrain regeneration.";
            public Boss[] bosses;
        }
        public static void Run()
        {
            const string output="TestResults/CharacterPipeline/FieldIntegration/Before/BossBindings.json";
            if(File.Exists(output))throw new IOException("Preserve previous Field inventory.");
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Field.unity",OpenSceneMode.Single);
            var bootstrap=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<M4SceneBootstrap>(true)).Single();
            var entries=bootstrap.IslandRegions;
            if(entries.Length!=5)throw new InvalidOperationException("Expected existing five-region Field bindings.");
            var report=new Report {bosses=entries.Select(e=>
            {
                e.Authored.Validate();var root=e.Authored.BossObject;
                return new Boss {region=e.Id.ToString(),name=root.name,actorId=GlobalObjectId.GetGlobalObjectIdSlow(root).ToString(),
                    position=root.transform.position,euler=root.transform.eulerAngles,scale=root.transform.localScale,
                    rootComponents=root.GetComponents<Component>().Select(c=>c==null?"MISSING":c.GetType().Name).ToArray(),
                    children=root.transform.Cast<Transform>().Select(t=>new Child{name=t.name,
                        prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject),localPosition=t.localPosition,localScale=t.localScale,
                        components=t.GetComponents<Component>().Select(c=>c==null?"MISSING":c.GetType().Name).ToArray(),
                        renderers=t.GetComponentsInChildren<Renderer>(true).Length,
                        allColliders=t.GetComponentsInChildren<Collider>(true).Length,
                        enabledColliders=t.GetComponentsInChildren<Collider>(true).Count(c=>c.enabled),
                        hasWeaknessCore=t.GetComponentsInChildren<Renderer>(true).Any(r=>r.name=="Weakness Core")}).ToArray()};
            }).ToArray()};
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("ORBIS_FIELD_BOSS_BINDING_AUDIT "+output);
        }
    }
}
