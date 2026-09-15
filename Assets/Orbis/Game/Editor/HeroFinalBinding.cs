#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Orbis.Art;
using Orbis.M0.Animation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Explicit final binding; no model/clip edits, scene save, export or target copy.</summary>
    public static class HeroFinalBinding
    {
        const string CatalogPath="Assets/Orbis/Art/Resources/Art/Catalog.asset";
        const string FieldPath="Assets/Scenes/Field.unity";
        const string DraftPath="TestResults/CharacterPipeline/DeliveryPreparation/RuntimeDeliveryDraft02.json";
        static readonly string[] Ids={"stella","polaris"}; // Read from the current Catalog; these stable save identities are never changed.
        static readonly string[] HeroPrefabs={
            "Assets/Orbis/Game/Characters/Candidates/Stella12GripRuntime01/Stella/Stella.prefab",
            "Assets/Orbis/Game/Characters/Candidates/Motion06GripRuntime01/Polaris/Polaris.prefab"};
        static readonly string[] BossNames={"FireBoss","WaterBoss","WindBoss","RockBoss","LightningBoss"};
        static readonly string[] ChangedRequired={
            "Assets/Orbis/M0/Runtime/Combat/BasicAttackCombo.cs","Assets/Orbis/M0/Runtime/Player/PlayerMotor.cs",
            "Assets/Orbis/M16/Runtime/Combat/ExplorerController.cs","Assets/Orbis/M3/Runtime/Presentation/M3Presentation.cs",
            "Assets/Orbis/M2/Runtime/Traversal/ExplorationMotor.cs","Assets/Orbis/Art/Runtime/Characters/ArtCharacterRoster.cs",
            "Assets/Orbis/Game/LookDev/Shaders/ExplorerToon.shadergraph","Assets/Orbis/Game/LookDev/Shaders/ExplorerToonLighting.hlsl"};

        [Serializable] sealed class Grip
        {
            public string prefab,profile,controller,gripEvidence,gripEvidenceSha256;
            public Vector3 socketLocalPosition,socketLocalEuler,socketLocalScale;
        }
        [Serializable] sealed class FileStamp
        {
            public string path,sha256,guid,kind,targetSha256,targetStatus,draftStagingSha256;
            public long bytes;
        }
        [Serializable] sealed class Binding
        {
            public string sourceId,displayName,prefab,avatar,controller,profile,measuredGripBinding;
            public string gripEvidence,gripEvidenceSha256,weapon,weaponBoneName;
            public int explorerIndex;
            public Vector3 socketLocalPosition,socketLocalEuler,socketLocalScale,trailTipLocalPosition;
        }
        [Serializable] sealed class Report
        {
            public int protectedWitnessVersion=2;
            public string status,catalog=CatalogPath,catalogBeforeSha256,catalogAfterSha256,catalogMetaSha256;
            public string protectedCatalogSha256,fieldBeforeSha256,fieldAfterSha256,createdUtc;
            public bool onlyApprovedCatalogFieldsChanged,assetDependenciesUnchanged;
            public Binding[] bindings;
            public FileStamp[] protectedFiles;
        }
        [Serializable] sealed class DraftFile { public string path,stagingSha256,baselineSha256,targetSha256; }
        [Serializable] sealed class Draft
        {
            public DraftFile[] changedExistingFiles,newRuntimeFiles,requiredExistingAssemblies,unchangedCompanionMetaWitnesses;
        }
        [Serializable] sealed class ExternalDependency { public string path,kind,packageName,packageVersion; }
        [Serializable] sealed class DependencyManifest
        {
            public string status,createdUtc,stagingRoot,targetRoot,runtimeDraft,runtimeDraftSha256,bindingCommitSha256;
            public string[] roots,notes;
            public FileStamp[] files,auditEvidenceFiles,projectDependencyDeclarations;
            public ExternalDependency[] externalDependencies;
            public long totalAssetBytes,targetDifferentOrMissingBytes;
            public int assetFileCount;
        }
        sealed class Plan
        {
            public ArtAssetCatalog catalog;
            public ArtExplorerAsset[] original,replacement;
            public Report report;
        }

        public static void ReviewRequested() => BindRequested(false);
        public static void CommitRequested() => BindRequested(true);

        static void BindRequested(bool commit)
        {
            RequireIdle();
            string folder=RunFolder(),reviewPath=folder+"/Review.json",commitPath=folder+"/Commit.json";
            if(!commit && Directory.Exists(folder)) throw new IOException("Use a new -heroBindingRun; preserve previous review evidence.");
            if(commit && !File.Exists(reviewPath)) throw new IOException("Review this exact run before commit.");
            var plan=Prepare();
            var report=plan.report;
            var reviewed=commit?JsonUtility.FromJson<Report>(File.ReadAllText(reviewPath)):null;
            if(commit)
            {
                RequireSamePlan(reviewed,report);
                if(File.Exists(commitPath))
                {
                    var completed=JsonUtility.FromJson<Report>(File.ReadAllText(commitPath));
                    if(Hash(CatalogPath)!=completed.catalogAfterSha256) throw new IOException("Catalog changed after this commit.");
                    RequireInstalled(plan);
                    Debug.Log("ORBIS_HERO_BINDING_ALREADY_INSTALLED "+commitPath); return;
                }
                if(Hash(CatalogPath)!=reviewed.catalogBeforeSha256) throw new IOException("Catalog changed after review; obtain a new review.");
            }
            Directory.CreateDirectory(folder);
            if(!commit) File.Copy(CatalogPath,folder+"/Catalog.before.asset",false);
            bool installed=false;
            try
            {
                plan.catalog.Explorers=plan.replacement;
                RequireInstalled(plan);
                if(ProtectedCatalog(plan.catalog)!=report.protectedCatalogSha256)
                    throw new InvalidOperationException("An unrelated catalog property changed.");
                RequireFileStamps(report.protectedFiles);
                if(Hash(CatalogPath+".meta")!=report.catalogMetaSha256) throw new IOException("Catalog GUID metadata changed.");
                report.onlyApprovedCatalogFieldsChanged=true;
                report.assetDependenciesUnchanged=true;
                if(commit)
                {
                    EditorUtility.SetDirty(plan.catalog);
                    AssetDatabase.SaveAssetIfDirty(plan.catalog); // Never SaveAssets: unrelated dirty assets remain untouched.
                    installed=true;
                    report.catalogAfterSha256=Hash(CatalogPath);
                    report.status="committed_catalog_only";
                    RequireInstalled(plan);
                    RequireFileStamps(report.protectedFiles);
                    if(Hash(CatalogPath+".meta")!=report.catalogMetaSha256) throw new IOException("Catalog metadata changed during save.");
                }
                else
                {
                    report.catalogAfterSha256=Hash(CatalogPath);
                    if(report.catalogAfterSha256!=report.catalogBeforeSha256) throw new IOException("Review wrote the catalog.");
                    report.status="review_only_not_installed";
                }
                report.fieldAfterSha256=Hash(FieldPath);
                if(report.fieldAfterSha256!=report.fieldBeforeSha256) throw new IOException("Field changed during this catalog operation.");
                File.WriteAllText(commit?commitPath:reviewPath,JsonUtility.ToJson(report,true));
                Debug.Log((commit?"ORBIS_HERO_BINDING_COMMITTED ":"ORBIS_HERO_BINDING_REVIEW ")+(commit?commitPath:reviewPath));
            }
            finally
            {
                if(!installed)
                {
                    plan.catalog.Explorers=plan.original;
                    if(!commit && (EditorUtility.IsDirty(plan.catalog) || Hash(CatalogPath)!=report.catalogBeforeSha256))
                        throw new IOException("Review did not restore the clean original catalog.");
                }
            }
        }

        static Plan Prepare()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(CatalogPath);
            if(catalog==null || EditorUtility.IsDirty(catalog)) throw new InvalidOperationException("A clean saved Art Catalog is required.");
            if(catalog.Explorers==null || catalog.Explorers.Length!=2 ||
                Ids.Any(id=>catalog.Explorers.Count(x=>x!=null && x.SourceId==id && x.Character!=null)!=1))
                throw new InvalidOperationException("Expected exactly the existing stella/polaris Explorer definitions.");
            var original=catalog.Explorers;
            var replacements=original.Select(x=>new ArtExplorerAsset{SourceId=x.SourceId,Character=x.Character}).ToArray();
            var bindings=new List<Binding>();
            var files=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<Ids.Length;i++)
            {
                int index=Array.FindIndex(original,x=>x.SourceId==Ids[i]);
                var previous=original[index].Character;
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(HeroPrefabs[i]);
                if(prefab==null) throw new FileNotFoundException("Reviewed hero candidate missing.",HeroPrefabs[i]);
                var animators=prefab.GetComponentsInChildren<Animator>(true);
                if(animators.Length!=1) throw new InvalidOperationException("Exactly one reviewed Humanoid Animator is required.");
                var animator=animators[0];
                var driver=animator.GetComponent<HumanAnimationDriver>();
                if(animator.avatar==null || !animator.avatar.isHuman || !animator.avatar.isValid || animator.applyRootMotion ||
                    animator.runtimeAnimatorController==null || driver==null || driver.Profile==null ||
                    animator.GetComponent<HumanFootIK>()==null)
                    throw new InvalidOperationException("Candidate Humanoid/driver/profile/IK/root-motion contract failed.");
                driver.Profile.ValidateForBuild();
                if(driver.Profile.Clips.Any(c=>c.Clip.events.Length!=0)) throw new InvalidOperationException("Presentation clips must not add animation damage events.");
                string bindingPath=Path.GetDirectoryName(HeroPrefabs[i]).Replace('\\','/')+"/GripBinding.json";
                var grip=JsonUtility.FromJson<Grip>(File.ReadAllText(bindingPath));
                RequireAssetPath(grip.prefab); RequireAssetPath(grip.profile); RequireAssetPath(grip.controller);
                if(grip.prefab!=HeroPrefabs[i] || grip.profile!=AssetDatabase.GetAssetPath(driver.Profile) ||
                    grip.controller!=AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) ||
                    !Finite(grip.socketLocalPosition) || !Finite(grip.socketLocalEuler) || !Finite(grip.socketLocalScale) ||
                    grip.socketLocalScale.x<=0 || grip.socketLocalScale.y<=0 || grip.socketLocalScale.z<=0)
                    throw new InvalidOperationException("Measured grip binding does not match the selected prefab/profile/controller.");
                RequireProjectFile(grip.gripEvidence,"TestResults/CharacterPipeline/");
                if(Hash(grip.gripEvidence)!=grip.gripEvidenceSha256) throw new IOException("Measured grip evidence hash mismatch.");
                if(previous.Weapon==null || string.IsNullOrEmpty(previous.WeaponBoneName))
                    throw new InvalidOperationException("Preserve the existing Wayfarer weapon/socket definition.");
                ValidateLiveContract(prefab,previous.WeaponBoneName);
                replacements[index].Character=new ArtCharacterAsset{
                    DisplayName=previous.DisplayName,Element=previous.Element,Prefab=prefab,Avatar=animator.avatar,
                    Controller=animator.runtimeAnimatorController,Weapon=previous.Weapon,WeaponBoneName=previous.WeaponBoneName,
                    WeaponLocalPosition=grip.socketLocalPosition,WeaponLocalEuler=grip.socketLocalEuler,WeaponLocalScale=grip.socketLocalScale,
                    TrailTipLocalPosition=previous.TrailTipLocalPosition};
                bindings.Add(new Binding{sourceId=original[index].SourceId,displayName=previous.DisplayName,explorerIndex=index,
                    prefab=HeroPrefabs[i],avatar=AssetDatabase.GetAssetPath(animator.avatar),controller=grip.controller,profile=grip.profile,
                    measuredGripBinding=bindingPath,gripEvidence=grip.gripEvidence,gripEvidenceSha256=grip.gripEvidenceSha256,
                    socketLocalPosition=grip.socketLocalPosition,socketLocalEuler=grip.socketLocalEuler,socketLocalScale=grip.socketLocalScale,
                    weapon=AssetDatabase.GetAssetPath(previous.Weapon),weaponBoneName=previous.WeaponBoneName,
                    trailTipLocalPosition=previous.TrailTipLocalPosition});
                foreach(string path in AssetDatabase.GetDependencies(new[]{HeroPrefabs[i],grip.profile,grip.controller,AssetDatabase.GetAssetPath(previous.Weapon)},true))
                    if(path.StartsWith("Assets/",StringComparison.Ordinal)) AddAssetAndMeta(files,path);
                AddAssetAndMeta(files,bindingPath); files.Add(grip.gripEvidence);
            }
            return new Plan{catalog=catalog,original=original,replacement=replacements,report=new Report{
                catalogBeforeSha256=Hash(CatalogPath),catalogMetaSha256=Hash(CatalogPath+".meta"),fieldBeforeSha256=Hash(FieldPath),
                protectedCatalogSha256=ProtectedCatalog(catalog),createdUtc=DateTime.UtcNow.ToString("o"),bindings=bindings.ToArray(),
                protectedFiles=files.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>Stamp(p)).ToArray()}};
        }

        static void ValidateLiveContract(GameObject prefab,string socketName)
        {
            var host=new GameObject("Hero binding validation only"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var pivot=new GameObject("Visual Facing").transform; pivot.SetParent(host.transform,false);
                var instance=Object.Instantiate(prefab,pivot,false);
                var animator=instance.GetComponentInChildren<Animator>(true);
                var driver=animator.GetComponent<HumanAnimationDriver>();
                animator.Rebind(); driver.Configure(driver.Profile,pivot);
                // The normal runtime receives its first root observation from PlayerMotor. This
                // temporary validation has no PlayerMotor, so supply that prerequisite explicitly.
                // Non-grounded + zero dt initializes IK bones without solving against an editor scene.
                driver.Observe(new CharacterMotionObservation{Root=host.transform,DeltaTime=0f,Grounded=false,
                    Discontinuity=true,GameplayState=Orbis.M0.PlayerActionState.Idle});
                foreach(var bone in new[]{HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,
                    HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot})
                    if(animator.GetBoneTransform(bone)==null) throw new InvalidOperationException("IK binding bone missing: "+bone);
                animator.Update(0f);
                if(!driver.IsReady || driver.PresentationPivot!=pivot || driver.MotionRoot!=host.transform ||
                    driver.AllowFootIK || animator.applyRootMotion)
                    throw new InvalidOperationException("Live facing-wrapper contract failed: "+driver.ValidationError);
                // ArtCharacterRoster prefers a matching named socket and otherwise uses Humanoid RightHand.
                if(!instance.GetComponentsInChildren<Transform>(true).Any(t=>t.name==socketName) &&
                    animator.GetBoneTransform(HumanBodyBones.RightHand)==null)
                    throw new InvalidOperationException("The existing weapon socket has no named/Humanoid attachment target.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        static void RequireInstalled(Plan plan)
        {
            foreach(var expected in plan.report.bindings)
            {
                var actual=plan.catalog.Explorer(expected.sourceId);
                if(actual==null || AssetDatabase.GetAssetPath(actual.Prefab)!=expected.prefab ||
                    AssetDatabase.GetAssetPath(actual.Avatar)!=expected.avatar || AssetDatabase.GetAssetPath(actual.Controller)!=expected.controller ||
                    !actual.WeaponLocalPosition.Equals(expected.socketLocalPosition) || !actual.WeaponLocalEuler.Equals(expected.socketLocalEuler) ||
                    !actual.WeaponLocalScale.Equals(expected.socketLocalScale)) throw new InvalidOperationException("Final hero binding differs: "+expected.sourceId);
            }
            if(ProtectedCatalog(plan.catalog)!=plan.report.protectedCatalogSha256)
                throw new InvalidOperationException("Unapproved catalog fields differ.");
        }

        static string ProtectedCatalog(ArtAssetCatalog catalog)
        {
            // Traverse every serialized value, including arrays and future fields. Only the six approved
            // Character properties (three references and three vectors) are omitted; SourceId is retained.
            var ignored=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<catalog.Explorers.Length;i++)
                if(Ids.Contains(catalog.Explorers[i].SourceId))
                    foreach(string field in new[]{"Prefab","Avatar","Controller","WeaponLocalPosition","WeaponLocalEuler","WeaponLocalScale"})
                        ignored.Add("Explorers.Array.data["+i+"].Character."+field);
            var data=new SerializedObject(catalog); data.Update(); var cursor=data.GetIterator(); var text=new StringBuilder();
            bool enterChildren=true;
            while(cursor.Next(enterChildren))
            {
                // Once normalized, a value is a leaf. In particular, never descend into a PPtr:
                // its m_FileID/m_PathID implementation children can contain process-local identity
                // even though the parent was already recorded with its persistent GUID/localFileId.
                enterChildren=false;
                string path=cursor.propertyPath;
                if(ignored.Any(p=>path==p || path.StartsWith(p+".",StringComparison.Ordinal))) continue;
                string value;
                switch(cursor.propertyType)
                {
                    case SerializedPropertyType.Generic: enterChildren=true; continue;
                    case SerializedPropertyType.Integer: value=cursor.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.ArraySize: value=cursor.intValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: value=cursor.boolValue?"true":"false"; break;
                    case SerializedPropertyType.Float: value=cursor.doubleValue.ToString("R",CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.String: value=cursor.stringValue; break;
                    case SerializedPropertyType.Enum: value=cursor.intValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Vector3: value=JsonUtility.ToJson(cursor.vector3Value); break;
                    case SerializedPropertyType.ObjectReference:
                        var obj=cursor.objectReferenceValue;
                        if(obj==null) value="null";
                        else if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out string guid,out long local))
                            value=guid+":"+local.ToString(CultureInfo.InvariantCulture);
                        else throw new InvalidOperationException("Catalog contains an unsaved reference: "+path);
                        break;
                    default: throw new InvalidOperationException("Add a deliberate preservation witness for property type "+cursor.propertyType+" at "+path);
                }
                text.Append(path.Length).Append(':').Append(path).Append('=').Append(value.Length).Append(':').Append(value).Append('\n');
            }
            using(var hash=SHA256.Create()) return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        static void RequireSamePlan(Report before,Report after)
        {
            if(before==null || before.protectedWitnessVersion!=2 || after.protectedWitnessVersion!=2 ||
                before.catalog!=CatalogPath || before.protectedCatalogSha256!=after.protectedCatalogSha256 ||
                before.catalogMetaSha256!=after.catalogMetaSha256 || JsonUtility.ToJson(new Bindings{items=before.bindings})!=JsonUtility.ToJson(new Bindings{items=after.bindings}) ||
                !before.protectedFiles.Select(f=>f.path+":"+f.sha256).SequenceEqual(after.protectedFiles.Select(f=>f.path+":"+f.sha256)))
                throw new IOException("Reviewed hero binding inputs changed; obtain a fresh review.");
        }
        [Serializable] sealed class Bindings { public Binding[] items; }

        public static void ExportDependencyManifest()
        {
            RequireIdle();
            string folder=RunFolder(),commitPath=folder+"/Commit.json";
            if(!File.Exists(commitPath)) throw new IOException("Commit the approved hero binding before exporting final dependencies.");
            var completed=JsonUtility.FromJson<Report>(File.ReadAllText(commitPath));
            var plan=Prepare(); RequireInstalled(plan); RequireSamePlan(completed,plan.report);
            if(Hash(CatalogPath)!=completed.catalogAfterSha256) throw new IOException("Catalog no longer matches the binding commit.");
            string output=folder+"/DependencyManifest.json";
            string name=OptionalArg("-heroDependencyName");
            if(!string.IsNullOrEmpty(name)) { RequireLabel(name); output=folder+"/"+name+".json"; }
            if(File.Exists(output)) throw new IOException("Dependency report exists; use a fresh name.");
            var roots=new[]{FieldPath,CatalogPath}.Concat(HeroPrefabs)
                .Concat(BossNames.Select(n=>"Assets/Orbis/Game/Characters/Bosses/Original01/"+n+"/"+n+".prefab")).ToArray();
            foreach(string root in roots) { RequireAssetPath(root); if(AssetDatabase.LoadMainAssetAtPath(root)==null) throw new FileNotFoundException("Final root missing",root); }
            var draft=JsonUtility.FromJson<Draft>(File.ReadAllText(DraftPath));
            var draftRows=(draft.changedExistingFiles??Array.Empty<DraftFile>()).Concat(draft.newRuntimeFiles??Array.Empty<DraftFile>())
                .Concat(draft.requiredExistingAssemblies??Array.Empty<DraftFile>()).Concat(draft.unchangedCompanionMetaWitnesses??Array.Empty<DraftFile>()).ToArray();
            var files=new HashSet<string>(StringComparer.Ordinal); var external=new List<ExternalDependency>();
            foreach(string path in AssetDatabase.GetDependencies(roots,true))
            {
                if(path.StartsWith("Assets/",StringComparison.Ordinal)) AddAssetAndMeta(files,path);
                else
                {
                    var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                    external.Add(new ExternalDependency{path=path,kind=package==null?"Unity built-in":"package manager",packageName=package?.name,packageVersion=package?.version});
                }
            }
            foreach(string path in draftRows.Select(r=>r.path).Concat(ChangedRequired)
                .Concat(Directory.GetFiles("Assets/Orbis/M0/Runtime/Animation","*.cs"))
                .Concat(Directory.GetFiles("Assets/Orbis/Game/Runtime/Animation","*.cs"))
                .Concat(plan.report.bindings.Select(b=>b.measuredGripBinding))) AddAssetAndMeta(files,path.Replace('\\','/'));
            var ordered=files.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>Stamp(p,true)).ToArray();
            foreach(var file in ordered) file.draftStagingSha256=draftRows.FirstOrDefault(r=>r.path==file.path)?.stagingSha256;
            var manifest=new DependencyManifest{
                status="Exact recursive dependency inventory only; nothing copied to target.",createdUtc=DateTime.UtcNow.ToString("o"),
                stagingRoot=Directory.GetCurrentDirectory(),targetRoot="D:/Project/ORBIS",runtimeDraft=DraftPath,runtimeDraftSha256=Hash(DraftPath),
                bindingCommitSha256=Hash(commitPath),roots=roots,files=ordered,assetFileCount=ordered.Length,totalAssetBytes=ordered.Sum(f=>f.bytes),
                targetDifferentOrMissingBytes=ordered.Where(f=>f.targetStatus!="same-bytes").Sum(f=>f.bytes),externalDependencies=external.ToArray(),
                auditEvidenceFiles=plan.report.bindings.Select(b=>Stamp(b.gripEvidence)).Concat(new[]{Stamp(commitPath),Stamp(DraftPath)}).ToArray(),
                projectDependencyDeclarations=new[]{"Packages/manifest.json","Packages/packages-lock.json"}.Where(File.Exists).Select(p=>Stamp(p,true)).ToArray(),
                notes=new[]{"AssetDatabase.GetDependencies recursively from exactly nine final roots; no candidate-directory sweep.",
                    "Some selected models/clips legitimately live in earlier candidate folders: include only the referenced files and metas.",
                    "Runtime draft hashes are historical witnesses; current SHA256 is recorded after final binding and IK fixes.",
                    "This does not export Field or build Addressables. Add the final generated streaming scenes/catalog closure after the separate Field export.",
                    "Only named small audit JSON files are listed outside Assets; no render folders or TestResults tree copy.",
                    "Target hashes are read-only current evidence. Recheck baseline conflicts and all source/destination hashes before any copy."}};
            File.WriteAllText(output,JsonUtility.ToJson(manifest,true)); Debug.Log("ORBIS_HERO_DEPENDENCIES "+output);
        }

        static void AddAssetAndMeta(HashSet<string> files,string path)
        {
            RequireAssetPath(path);
            if(!File.Exists(path)) throw new FileNotFoundException("Dependency missing",path);
            files.Add(path);
            if(!path.EndsWith(".meta",StringComparison.Ordinal))
            { if(!File.Exists(path+".meta")) throw new FileNotFoundException("Dependency meta missing",path+".meta"); files.Add(path+".meta"); }
            string parent=Path.GetDirectoryName(path).Replace('\\','/');
            while(parent!="Assets" && !string.IsNullOrEmpty(parent))
            {
                if(!File.Exists(parent+".meta")) throw new FileNotFoundException("Folder meta missing",parent+".meta");
                files.Add(parent+".meta"); parent=Path.GetDirectoryName(parent)?.Replace('\\','/');
            }
        }
        static FileStamp Stamp(string path,bool target=false)
        {
            var file=new FileInfo(path); if(!file.Exists) throw new FileNotFoundException("Witness missing",path);
            var stamp=new FileStamp{path=path,sha256=Hash(path),bytes=file.Length,
                guid=path.StartsWith("Assets/",StringComparison.Ordinal)?AssetDatabase.AssetPathToGUID(path.EndsWith(".meta")?path.Substring(0,path.Length-5):path):null,
                kind=path.EndsWith(".meta")?"metadata":"file"};
            if(target)
            {
                string destination=Path.Combine("D:/Project/ORBIS",path);
                stamp.targetSha256=File.Exists(destination)?Hash(destination):null;
                stamp.targetStatus=stamp.targetSha256==null?"absent":stamp.targetSha256==stamp.sha256?"same-bytes":"different-requires-baseline-review";
            }
            return stamp;
        }
        static void RequireFileStamps(FileStamp[] files)
        { foreach(var file in files) if(Hash(file.path)!=file.sha256) throw new IOException("Protected dependency changed: "+file.path); }
        static void RequireIdle()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Requires idle Edit Mode; no Play/compile/Animation Mode.");
        }
        static string RunFolder() { string run=OptionalArg("-heroBindingRun"); RequireLabel(run); return "TestResults/CharacterPipeline/HeroFinalBinding/"+run; }
        static void RequireLabel(string value)
        { if(string.IsNullOrEmpty(value) || value.Length>64 || !value.All(c=>char.IsLetterOrDigit(c) || c=='_')) throw new ArgumentException("Use a simple fresh -heroBindingRun/name."); }
        static string OptionalArg(string name)
        { var args=Environment.GetCommandLineArgs(); for(int i=0;i<args.Length-1;i++) if(args[i]==name)return args[i+1]; return null; }
        static void RequireAssetPath(string path) => RequireProjectFile(path,"Assets/");
        static void RequireProjectFile(string path,string prefix)
        {
            if(string.IsNullOrEmpty(path) || !path.StartsWith(prefix,StringComparison.Ordinal) || Path.IsPathRooted(path) ||
                path.Contains('\\') || path.Split('/').Any(p=>p==".." || p=="." || p.Length==0)) throw new ArgumentException("Invalid scoped project path: "+path);
        }
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        static string Hash(string path) { using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create()) return Hex(sha.ComputeHash(stream)); }
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
    }
}
#endif
