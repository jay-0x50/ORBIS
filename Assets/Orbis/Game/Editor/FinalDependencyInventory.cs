#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.Game.Editor
{
    /// <summary>Read-only final dependency closure after hero Commit and normal Field export. Writes audit JSON only.</summary>
    public static class FinalDependencyInventory
    {
        const string Field = "Assets/Scenes/Field.unity";
        const string Catalog = "Assets/Orbis/Art/Resources/Art/Catalog.asset";
        const string State = "Assets/Orbis/Game/World/Authoring/FieldExportState.json";
        const string Target = "D:/Project/ORBIS";
        static readonly string[] ExpectedRoots = {
            Field, Catalog,
            "Assets/Orbis/Game/Characters/Candidates/Stella12GripRuntime01/Stella/Stella.prefab",
            "Assets/Orbis/Game/Characters/Candidates/Motion06GripRuntime01/Polaris/Polaris.prefab",
            "Assets/Orbis/Game/Characters/Bosses/Original01/FireBoss/FireBoss.prefab",
            "Assets/Orbis/Game/Characters/Bosses/Original01/WaterBoss/WaterBoss.prefab",
            "Assets/Orbis/Game/Characters/Bosses/Original01/WindBoss/WindBoss.prefab",
            "Assets/Orbis/Game/Characters/Bosses/Original01/RockBoss/RockBoss.prefab",
            "Assets/Orbis/Game/Characters/Bosses/Original01/LightningBoss/LightningBoss.prefab" };
        static readonly string[] ExpectedOutputs = {
            "Assets/Orbis/Game/Scenes/Orbis_Island.unity",
            "Assets/Orbis/Game/World/Scenes/Environment_Agnia.unity",
            "Assets/Orbis/Game/World/Scenes/Environment_Teluna.unity",
            "Assets/Orbis/Game/World/Scenes/Environment_Zephyr.unity",
            "Assets/Orbis/Game/World/Scenes/Environment_Granite.unity",
            "Assets/Orbis/Game/World/Scenes/Environment_Voltheim.unity",
            "Assets/Orbis/Game/World/Resources/World/StreamCatalog.asset" };
        static readonly string[] ExplicitRuntimeAndShaderChanges = {
            "Assets/Orbis/M0/Runtime/Combat/BasicAttackCombo.cs", "Assets/Orbis/M0/Runtime/Player/PlayerMotor.cs",
            "Assets/Orbis/M16/Runtime/Combat/ExplorerController.cs", "Assets/Orbis/M3/Runtime/Presentation/M3Presentation.cs",
            "Assets/Orbis/M2/Runtime/Traversal/ExplorationMotor.cs", "Assets/Orbis/Art/Runtime/Characters/ArtCharacterRoster.cs",
            "Assets/Orbis/Game/LookDev/Shaders/ExplorerToon.shadergraph", "Assets/Orbis/Game/LookDev/Shaders/ExplorerToonLighting.hlsl",
            "Assets/Orbis/M16/Runtime/Presentation/ExplorerSelectionScreen.cs" };

        [Serializable] sealed class Digest { public string path, sha256; }
        [Serializable] sealed class ExportState { public string sourceDependencyHash; public bool needsOcclusionBake; public Digest[] outputs; }
        [Serializable] sealed class Commit
        {
            public int protectedWitnessVersion;
            public string status, catalog, catalogAfterSha256, catalogMetaSha256;
            public bool onlyApprovedCatalogFieldsChanged, assetDependenciesUnchanged;
        }
        [Serializable] sealed class FileStamp
        {
            public string path, sha256, guid, kind, targetSha256, targetStatus, draftStagingSha256;
            public long bytes, targetBytes;
            public string[] inclusion;
        }
        [Serializable] sealed class ExternalDependency { public string path, kind, packageName, packageVersion; }
        [Serializable] sealed class HeroManifest
        {
            public string status, stagingRoot, targetRoot, bindingCommitSha256, runtimeDraft, runtimeDraftSha256;
            public string[] roots;
            public FileStamp[] files, auditEvidenceFiles, projectDependencyDeclarations;
            public ExternalDependency[] externalDependencies;
        }
        [Serializable] sealed class FinalManifest
        {
            public int schemaVersion = 1;
            public string status = "Read-only exact final dependency inventory; no Assets saved, export triggered, or target files copied.";
            public string createdUtc, unityVersion, stagingRoot, targetRoot, heroBindingRun, bindingCommitSha256;
            public string heroManifestPath, heroManifestSha256, fieldSha256, fieldExportStateSha256, sourceDependencyHash;
            public bool heroCommitValidated, fieldExportValidated, needsOcclusionBake, sourcesUnchangedDuringInventory;
            public string[] roots, explicitRuntimeRoots, notes;
            public Digest[] generatedOutputs;
            public FileStamp[] files, auditEvidenceFiles, projectDependencyDeclarations;
            public ExternalDependency[] externalDependencies;
            public int assetFileCount;
            public long totalAssetBytes, targetDifferentOrMissingBytes;
        }

        public static void ExportRequested()
        {
            RequireIdle();
            string run = Label(Arg("-heroBindingRun"));
            string heroName = Label(Arg("-heroDependencyName"));
            string finalName = Label(Arg("-finalDependencyName"));
            string folder = "TestResults/CharacterPipeline/HeroFinalBinding/" + run;
            string commitPath = folder + "/Commit.json";
            string heroPath = folder + "/" + heroName + ".json";
            string output = folder + "/" + finalName + ".json";
            if (string.Equals(heroPath, output, StringComparison.OrdinalIgnoreCase) || heroPath == commitPath || output == commitPath ||
                heroName == "Review" || finalName == "Review" || File.Exists(heroPath) || File.Exists(output))
                throw new IOException("Use two distinct fresh manifest names; preserve Review/Commit and previous manifests.");
            var commit = Read<Commit>(commitPath);
            if (commit.status != "committed_catalog_only" || commit.protectedWitnessVersion != 2 || commit.catalog != Catalog ||
                !commit.onlyApprovedCatalogFieldsChanged || !commit.assetDependenciesUnchanged ||
                Hash(Catalog) != commit.catalogAfterSha256 || Hash(Catalog + ".meta") != commit.catalogMetaSha256)
                throw new IOException("A matching successful HeroFinalBinding Commit is required.");
            string fieldHash = Hash(Field), stateHash = Hash(State), commitHash = Hash(commitPath);
            var export = RequireCurrentExport();

            // Reuse all existing exact binding/protected dependency/grip checks. This public
            // helper makes a temporary validation instance, restores it, and writes only audit JSON.
            // It does not commit the Catalog or call Field export.
            HeroFinalBinding.ExportDependencyManifest();
            var hero = Read<HeroManifest>(heroPath);
            if (hero.bindingCommitSha256 != commitHash || hero.roots == null ||
                !Ordered(hero.roots).SequenceEqual(Ordered(ExpectedRoots)) || hero.files == null)
                throw new IOException("Hero dependency report does not describe the expected committed nine roots.");
            if (Path.GetFullPath(hero.stagingRoot) != Path.GetFullPath(Directory.GetCurrentDirectory()) ||
                !string.Equals(Path.GetFullPath(hero.targetRoot), Path.GetFullPath(Target), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Hero manifest roots disagree with this staging/target inventory.");

            var roots = ExpectedRoots.Concat(ExpectedOutputs).Concat(new[] { State }).ToArray();
            var reasons = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var external = new Dictionary<string, ExternalDependency>(StringComparer.Ordinal);
            foreach (var item in hero.externalDependencies ?? Array.Empty<ExternalDependency>()) external[item.path] = item;
            foreach (var item in hero.files) AddAsset(reasons, item.path, "committed hero/runtime inventory");
            foreach (string path in roots)
            {
                RequireAssetPath(path);
                if (AssetDatabase.LoadMainAssetAtPath(path) == null) throw new FileNotFoundException("Final dependency root missing", path);
                AddAsset(reasons, path, "final root");
            }
            foreach (string path in AssetDatabase.GetDependencies(roots, true))
            {
                if (path.StartsWith("Assets/", StringComparison.Ordinal)) AddAsset(reasons, path, "recursive final-root dependency");
                else
                {
                    var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                    external[path] = new ExternalDependency { path = path, kind = package == null ? "Unity built-in" : "package manager",
                        packageName = package?.name, packageVersion = package?.version };
                }
            }
            // Only these two installed runtime directories are enumerated. Never scan
            // Candidates, ImportedAssets, Img, blend, Tools or TestResults for delivery files.
            var runtime = ExplicitRuntimeAndShaderChanges
                .Concat(Directory.GetFiles("Assets/Orbis/M0/Runtime/Animation", "*.cs", SearchOption.TopDirectoryOnly))
                .Concat(Directory.GetFiles("Assets/Orbis/Game/Runtime/Animation", "*.cs", SearchOption.TopDirectoryOnly))
                .Select(p => p.Replace('\\', '/')).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (string path in runtime) AddAsset(reasons, path, "explicit final runtime/shader/UI source");
            var historical = hero.files.ToDictionary(f => f.path, f => f.draftStagingSha256, StringComparer.Ordinal);
            var files = reasons.Keys.OrderBy(p => p, StringComparer.Ordinal).Select(path =>
            {
                var stamp = Stamp(path, true); stamp.inclusion = Ordered(reasons[path]).ToArray();
                if (historical.TryGetValue(path, out var previous)) stamp.draftStagingSha256 = previous;
                return stamp;
            }).ToArray();
            var declarations = new[] { "Packages/manifest.json", "Packages/packages-lock.json" }.Select(p => Stamp(p, true)).ToArray();
            var evidencePaths = (hero.auditEvidenceFiles ?? Array.Empty<FileStamp>()).Select(f => f.path)
                .Concat(new[] { heroPath, commitPath }).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (string path in evidencePaths) RequireRelative(path, "TestResults/CharacterPipeline/");
            var evidence = evidencePaths.Select(p => Stamp(p, false)).ToArray();
            var manifest = new FinalManifest { createdUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                stagingRoot = Path.GetFullPath(Directory.GetCurrentDirectory()), targetRoot = Path.GetFullPath(Target), heroBindingRun = run,
                bindingCommitSha256 = commitHash, heroManifestPath = heroPath, heroManifestSha256 = Hash(heroPath), fieldSha256 = fieldHash,
                fieldExportStateSha256 = stateHash, sourceDependencyHash = export.sourceDependencyHash,
                heroCommitValidated = true, fieldExportValidated = true, needsOcclusionBake = export.needsOcclusionBake,
                roots = roots, generatedOutputs = export.outputs, explicitRuntimeRoots = runtime, files = files,
                projectDependencyDeclarations = declarations, auditEvidenceFiles = evidence,
                externalDependencies = external.Values.OrderBy(e => e.path, StringComparer.Ordinal).ToArray(), assetFileCount = files.Length,
                totalAssetBytes = files.Sum(f => f.bytes), targetDifferentOrMissingBytes = files.Where(f => f.targetStatus != "same-bytes").Sum(f => f.bytes),
                notes = new[] {
                    "Exactly 17 recursive roots: nine committed final roots + seven verified Field export outputs + FieldExportState. No candidate directory sweep.",
                    "Includes asset/meta and ancestor folder-meta bytes. Earlier candidate-folder files are included only when actually referenced.",
                    "Runtime draft SHA values are historical witnesses, not current file hashes. UI and both installed Runtime/Animation source directories are explicitly refreshed.",
                    "Package/built-in dependencies are inventory only, never copied from PackageCache. Current manifest/lock declarations and target hashes are separate.",
                    "A missing target and a target differing from staging are not automatic overwrite authorization. Baseline conflict checks and fresh hashes belong to delivery.",
                    "String-only Resources/Addressables references outside these roots are not automatically discovered. Existing target content is not deleted.",
                    "If needsOcclusionBake is true, this is a current inventory before final PVS validation. Generate a fresh inventory after bake or any source/output change.",
                    "Named audit JSON files are listed separately; no TestResults tree, raw source model tree, rendered image folder, or external package is a bulk-copy input." } };
            // Fail instead of recording a mixture if another writer changes a source mid-inventory.
            if (Hash(Field) != fieldHash || Hash(State) != stateHash || Hash(commitPath) != commitHash)
                throw new IOException("Field/export/binding witness changed during dependency inventory.");
            RequireCurrentExport();
            foreach (var stamp in files.Concat(declarations).Concat(evidence))
                if (Hash(stamp.path) != stamp.sha256 || new FileInfo(stamp.path).Length != stamp.bytes)
                    throw new IOException("Source changed during inventory: " + stamp.path);
            manifest.sourcesUnchangedDuringInventory = true;
            // The parent folder already exists because a successful Commit was mandatory.
            File.WriteAllText(output, JsonUtility.ToJson(manifest, true));
            Debug.Log("ORBIS_FINAL_DEPENDENCIES " + output + " assets=" + files.Length + " SHA256=" + Hash(output));
        }

        static ExportState RequireCurrentExport()
        {
            var loaded = SceneManager.GetSceneByPath(Field);
            if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty) throw new IOException("Save and normally export Field before inventory.");
            var state = Read<ExportState>(State);
            if (string.IsNullOrEmpty(state.sourceDependencyHash) || state.sourceDependencyHash != AssetDatabase.GetAssetDependencyHash(Field).ToString() ||
                state.outputs == null || state.outputs.Length != 7 || !Ordered(state.outputs.Select(d => d.path)).SequenceEqual(Ordered(ExpectedOutputs)))
                throw new IOException("Field dependency hash or exact seven-output set is stale. Run normal Field export, not a legacy generator.");
            foreach (var digest in state.outputs)
                if (Hash(digest.path) != digest.sha256) throw new IOException("Field output hash mismatch: " + digest.path);
            return state;
        }
        static void AddAsset(Dictionary<string, HashSet<string>> files, string path, string reason)
        {
            RequireAssetPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("Required asset/meta missing", path);
            Add(files, path, reason);
            if (!path.EndsWith(".meta", StringComparison.Ordinal)) Add(files, RequireFile(path + ".meta"), "asset metadata");
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            while (!string.IsNullOrEmpty(parent) && parent != "Assets")
            {
                Add(files, RequireFile(parent + ".meta"), "ancestor folder metadata");
                parent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
            }
        }
        static void Add(Dictionary<string, HashSet<string>> files, string path, string reason)
        { if (!files.TryGetValue(path, out var reasons)) files[path] = reasons = new HashSet<string>(StringComparer.Ordinal); reasons.Add(reason); }
        static FileStamp Stamp(string path, bool target)
        {
            RequireRelative(path, path.StartsWith("Assets/", StringComparison.Ordinal) ? "Assets/" : path.StartsWith("Packages/", StringComparison.Ordinal) ? "Packages/" : "TestResults/CharacterPipeline/");
            var info = new FileInfo(RequireFile(path));
            string sourceAsset = path.EndsWith(".meta", StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path;
            var stamp = new FileStamp { path = path, sha256 = Hash(path), bytes = info.Length,
                kind = path.EndsWith(".meta", StringComparison.Ordinal) ? Directory.Exists(sourceAsset) ? "folder metadata" : "asset metadata" : "file",
                guid = path.StartsWith("Assets/", StringComparison.Ordinal) ? AssetDatabase.AssetPathToGUID(sourceAsset) : null,
                targetBytes = -1, targetStatus = "not-a-copy-input" };
            if (target)
            {
                string destination = Path.GetFullPath(Path.Combine(Target, path));
                if (!destination.StartsWith(Path.GetFullPath(Target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Target path escaped its named root: " + path);
                if (File.Exists(destination)) { stamp.targetSha256 = Hash(destination); stamp.targetBytes = new FileInfo(destination).Length; }
                stamp.targetStatus = stamp.targetSha256 == null ? "absent" : stamp.targetSha256 == stamp.sha256 ? "same-bytes" : "different-requires-baseline-review";
            }
            return stamp;
        }
        static T Read<T>(string path) where T : class
        { return JsonUtility.FromJson<T>(File.ReadAllText(RequireFile(path))) ?? throw new IOException("Invalid JSON: " + path); }
        static string RequireFile(string path) { if (!File.Exists(path)) throw new FileNotFoundException("Required witness missing", path); return path; }
        static void RequireAssetPath(string path) => RequireRelative(path, "Assets/");
        static void RequireRelative(string path, string prefix)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix, StringComparison.Ordinal) || Path.IsPathRooted(path) || path.Contains('\\') ||
                path.Split('/').Any(s => s == "." || s == ".." || s.Length == 0)) throw new ArgumentException("Invalid scoped path: " + path);
        }
        static IEnumerable<string> Ordered(IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.Ordinal);
        static string Label(string value)
        { if (string.IsNullOrEmpty(value) || value.Length > 64 || !value.All(c => char.IsLetterOrDigit(c) || c == '_')) throw new ArgumentException("Use explicit simple -heroBindingRun, -heroDependencyName and -finalDependencyName values."); return value; }
        static string Arg(string key) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        static void RequireIdle()
        { if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || AnimationMode.InAnimationMode()) throw new InvalidOperationException("Requires idle Edit Mode, no import/compile/play/Animation Mode."); }
        static string Hash(string path)
        { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif
