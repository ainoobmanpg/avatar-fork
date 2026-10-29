using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VrcAvatarWorkspace.Branching
{
    [Serializable]
    public sealed class BranchRenamePreview
    {
        public string previousName;
        public string name;
        public string folder;
        public string destination;
        public string[] changes;
        public int additionalFiles;
    }

    public static partial class AvatarBrancher
    {
        internal const string RecoveryRequiredKey = "AvatarFork.RecoveryRequired";

        static Exception RecoveryRequired(Exception error)
        {
            error.Data[RecoveryRequiredKey] = true;
            return error;
        }

        sealed class RenamePlan
        {
            public BranchReport report;
            public BranchRenamePreview preview;
            public Dictionary<string, string> paths;
            public Dictionary<string, string> guids;
            public Dictionary<string, string> displayNames;
            public Dictionary<string, string> references;
        }

        [Serializable]
        sealed class RenameMoveRecord
        {
            public string source;
            public string destination;
            public bool completed;
        }

        public static BranchRenamePreview PreviewRename(string folder, string requestedName)
        {
            return PrepareRename(folder, requestedName).preview;
        }

        static RenamePlan PrepareRename(string folder, string requestedName)
        {
            folder = Normalize(folder).TrimEnd('/');
            RequireAssetsPath(folder);
            RequireUnlinkedPath(folder);
            string recoveryRoot = "Library/AvatarFork/Rename";
            if (Directory.Exists(recoveryRoot))
                foreach (string operation in Directory.GetDirectories(recoveryRoot).Select(p => p + "/operation.json").Where(File.Exists))
                {
                    string recordFolder = Path.GetDirectoryName(operation);
                    if (File.Exists(recordFolder + "/complete.txt") || File.Exists(recordFolder + "/restored.txt")) continue;
                    BranchRenamePreview interrupted;
                    try { interrupted = JsonUtility.FromJson<BranchRenamePreview>(File.ReadAllText(operation, Utf8)); }
                    catch (Exception error) when (error is ArgumentException || error is IOException)
                    { throw RecoveryRequired(new InvalidOperationException(AvatarForkText.CorruptRecovery + operation, error)); }
                    if (interrupted == null || string.IsNullOrEmpty(interrupted.folder) || string.IsNullOrEmpty(interrupted.destination))
                        throw RecoveryRequired(new InvalidOperationException(AvatarForkText.CorruptRecovery + operation));
                    if (interrupted != null && (string.Equals(folder, interrupted.folder, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(folder, interrupted.destination, StringComparison.OrdinalIgnoreCase)))
                        throw RecoveryRequired(new InvalidOperationException(AvatarForkText.InterruptedRename + recordFolder));
                }
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
                foreach (string path in Directory.GetFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException(AvatarForkText.LinkedCopy + path);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            if (!File.Exists(folder + "/" + ReportName))
                throw new InvalidOperationException(AvatarForkText.SelectCopy);
            string reportPath = folder + "/" + ReportName;
            BranchReport report;
            try { report = JsonUtility.FromJson<BranchReport>(File.ReadAllText(reportPath, Utf8)); }
            catch (ArgumentException error) { throw RecoveryRequired(new InvalidOperationException(AvatarForkText.UnreadableReport + reportPath, error)); }
            if (report == null) throw RecoveryRequired(new InvalidOperationException(AvatarForkText.UnreadableReport + reportPath));
            if (report.version != 1) throw new InvalidOperationException(AvatarForkText.UnsupportedReport + reportPath);
            if (report.status != "Complete") throw new InvalidOperationException(AvatarForkText.IncompleteCopy + reportPath);
            if (string.IsNullOrEmpty(report.branchName)) throw new InvalidOperationException(AvatarForkText.NamingRecord + reportPath);
            if (report.folder != folder || report.branchName != Path.GetFileName(folder))
                throw new InvalidOperationException(AvatarForkText.RenameRecordMismatch + reportPath);
            if (Directory.GetFiles(folder, ReportName, SearchOption.AllDirectories).Length != 1)
                throw new InvalidOperationException(AvatarForkText.ContainsCopy);
            if (FindBranchFolder(folder) != null)
                throw new InvalidOperationException(AvatarForkText.RenameNested);
            string name = RequireBranchName(requestedName);
            string parent = Normalize(Path.GetDirectoryName(folder));
            string destination = parent + "/" + name;
            if (!string.Equals(folder, destination, StringComparison.OrdinalIgnoreCase)
                && (Directory.Exists(destination) || File.Exists(destination) || File.Exists(destination + ".meta") || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(destination))))
                throw new InvalidOperationException(AvatarForkText.RenameCollision);
            name = Path.GetFileName(destination);
            var paths = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(Normalize)
                .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))
                .ToDictionary(p => p, p => destination + p.Substring(folder.Length), StringComparer.Ordinal);
            var recordedPaths = report.assets.Select(a => new KeyValuePair<string, string>(a.destination, a.destinationGuid))
                .Concat(report.extractedMaterials.Select(a => new KeyValuePair<string, string>(a.destination, a.destinationGuid))).ToArray();
            foreach (var asset in report.assets)
            {
                string current = AssetDatabase.GUIDToAssetPath(asset.destinationGuid);
                if (!string.IsNullOrEmpty(current)) asset.destination = current;
            }
            foreach (var asset in report.extractedMaterials)
            {
                string current = AssetDatabase.GUIDToAssetPath(asset.destinationGuid);
                if (!string.IsNullOrEmpty(current)) asset.destination = current;
            }
            var tracked = report.assets.Select(a => a.destination).Concat(report.extractedMaterials.Select(a => a.destination))
                .Concat(new[] { report.prefab, report.scene }).ToArray();
            if (tracked.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tracked.Length)
                throw new InvalidOperationException(AvatarForkText.DuplicateRecord);
            foreach (string path in tracked)
                if (!Inside(path, folder) || !paths.ContainsKey(path) || !File.Exists(path + ".meta"))
                    throw new InvalidOperationException(AvatarForkText.RecordedAssetMissing + path);
            foreach (string path in report.assets.Select(a => a.destination).Concat(report.extractedMaterials.Select(a => a.destination)))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                string suffix = "__" + report.branchName;
                if (stem.EndsWith(suffix, StringComparison.Ordinal))
                    paths[path] = destination + Normalize(Path.GetDirectoryName(path)).Substring(folder.Length)
                        + "/" + stem.Substring(0, stem.Length - suffix.Length) + "__" + name + Path.GetExtension(path);
            }
            paths[report.prefab] = destination + "/" + name + ".prefab";
            paths[report.scene] = destination + "/" + name + ".unity";
            if (paths.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
                throw new InvalidOperationException(AvatarForkText.RenameFileCollision);
            var guids = paths.Keys.Concat(Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Select(Normalize)).Concat(new[] { folder })
                .ToDictionary(p => p, AssetDatabase.AssetPathToGUID, StringComparer.Ordinal);
            foreach (var entry in report.assets)
                if (guids[entry.destination] != entry.destinationGuid) throw new InvalidOperationException(AvatarForkText.AssetGuidMismatch + entry.destination);
            foreach (var entry in report.extractedMaterials)
                if (guids[entry.destination] != entry.destinationGuid) throw new InvalidOperationException(AvatarForkText.MaterialGuidMismatch + entry.destination);
            var preview = new BranchRenamePreview {
                previousName = report.branchName, name = name, folder = folder, destination = destination,
                changes = paths.Where(p => p.Key != p.Value).Select(p => p.Key + " → " + p.Value).ToArray(),
                additionalFiles = paths.Count - tracked.Length - 1
            };
            var names = tracked.Where(p => !p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p, p => AssetDatabase.LoadMainAssetAtPath(p)?.name, StringComparer.Ordinal);
            var references = new Dictionary<string, string>(paths, StringComparer.Ordinal);
            foreach (string directory in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Select(Normalize).Concat(new[] { folder }))
                references[directory] = destination + directory.Substring(folder.Length);
            foreach (var entry in recordedPaths)
                if (!references.ContainsKey(entry.Key) && paths.TryGetValue(AssetDatabase.GUIDToAssetPath(entry.Value), out var current))
                    references[entry.Key] = current;
            return new RenamePlan { report = report, preview = preview, paths = paths, guids = guids, displayNames = names, references = references };
        }

        static void CheckRenameState(string folder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException(AvatarForkText.Busy);
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException(AvatarForkText.ClosePrefabMode);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(scene.path))
                    throw new InvalidOperationException(AvatarForkText.RenameUnnamedScene);
                if (scene.isDirty && (Inside(scene.path, folder) || scene.GetRootGameObjects()
                    .SelectMany(g => EditorUtility.CollectDependencies(new Object[] { g }))
                    .Any(o => o != null && Inside(AssetDatabase.GetAssetPath(o), folder))))
                    throw new InvalidOperationException(AvatarForkText.DirtyScene);
            }
            foreach (var asset in Resources.FindObjectsOfTypeAll<Object>())
                if (asset != null && !(asset is Shader) && EditorUtility.IsDirty(asset) && Inside(AssetDatabase.GetAssetPath(asset), folder))
                    throw new InvalidOperationException(AvatarForkText.DirtyAsset + AssetDatabase.GetAssetPath(asset));
        }

        public static BranchReport Rename(string folder, string requestedName)
        {
            CheckRenameState(Normalize(folder).TrimEnd('/'));
            using (var dirty = new DirtyAssets()) return RenameSaved(folder, requestedName);
        }

        static BranchReport RenameSaved(string folder, string requestedName)
        {
            var plan = PrepareRename(folder, requestedName);
            var problems = Validate(plan.report, false);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join("\n", problems.Take(12)));
            if (plan.preview.changes.Length == 0 && plan.preview.previousName == plan.preview.name) return plan.report;
            string backup = "Library/AvatarFork/Rename/" + GUID.Generate();
            Directory.CreateDirectory(backup);
            foreach (string path in Directory.GetFiles(plan.preview.folder, "*", SearchOption.AllDirectories).Select(Normalize).Concat(new[] { plan.preview.folder + ".meta" }))
            {
                string target = backup + "/files" + path.Substring(plan.preview.folder.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(path, target);
            }
            File.WriteAllText(backup + "/operation.json", JsonUtility.ToJson(plan.preview, true), Utf8);
            var moves = new List<KeyValuePair<string, string>>();
            var openScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => Inside(s.path, plan.preview.folder)).ToArray();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Scene temporaryScene = default;
            try
            {
                using (var dirty = new DirtyAssets())
                {
                    if (openScenes.Length == SceneManager.sceneCount)
                        temporaryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    foreach (var scene in openScenes)
                        if (!EditorSceneManager.CloseScene(scene, true)) throw new IOException(AvatarForkText.CloseSceneFailed);
                    EditorUtility.DisplayProgressBar(AvatarForkText.RenameTitle, AvatarForkText.RenameProgress, .2f);
                    var staged = new Dictionary<string, string>();
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (var path in plan.paths.Where(p => Path.GetFileName(p.Key) != Path.GetFileName(p.Value)))
                        {
                            string temporary = Normalize(Path.GetDirectoryName(path.Key)) + "/AvatarForkRename_" + GUID.Generate() + Path.GetExtension(path.Key);
                            RenameMove(path.Key, temporary, moves, backup);
                            staged[path.Key] = temporary;
                        }
                    }
                    finally { AssetDatabase.StopAssetEditing(); }
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (var path in staged)
                            RenameMove(path.Value, plan.preview.folder + plan.paths[path.Key].Substring(plan.preview.destination.Length), moves, backup);
                    }
                    finally { AssetDatabase.StopAssetEditing(); }
                    if (plan.preview.folder != plan.preview.destination)
                    {
                        string temporary = Normalize(Path.GetDirectoryName(plan.preview.folder)) + "/AvatarForkRename_" + GUID.Generate();
                        RenameMove(plan.preview.folder, temporary, moves, backup);
                        RenameMove(temporary, plan.preview.destination, moves, backup);
                    }
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        RewriteRenamedFiles(plan);
                    }
                    finally { AssetDatabase.StopAssetEditing(); }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    RenameDisplayNames(plan);
                    foreach (var asset in plan.report.assets) asset.destination = plan.paths[asset.destination];
                    foreach (var asset in plan.report.extractedMaterials) asset.destination = plan.paths[asset.destination];
                    plan.report.scene = plan.paths[plan.report.scene];
                    plan.report.prefab = plan.paths[plan.report.prefab];
                    plan.report.folder = plan.preview.destination;
                    plan.report.branchName = plan.preview.name;
                    WriteReport(plan.report);
                    AssetDatabase.ImportAsset(plan.report.folder + "/" + ReportName, ImportAssetOptions.ForceSynchronousImport);
                    problems = Validate(plan.report, false);
                    foreach (var entry in plan.guids)
                    {
                        string path = plan.paths.TryGetValue(entry.Key, out var target) ? target : plan.preview.destination + entry.Key.Substring(plan.preview.folder.Length);
                        if (AssetDatabase.AssetPathToGUID(path) != entry.Value) problems.Add(AvatarForkText.GuidChanged + path);
                    }
                    if (problems.Count > 0) throw new InvalidOperationException(string.Join("\n", problems.Take(12)));
                }
                File.WriteAllText(backup + "/complete.txt", "Complete", Utf8);
            }
            catch (Exception error)
            {
                try
                {
                    var ownedGuids = new HashSet<string>(plan.guids.Values.Where(g => !string.IsNullOrEmpty(g)), StringComparer.OrdinalIgnoreCase);
                    foreach (var asset in Resources.FindObjectsOfTypeAll<Object>())
                        if (asset != null && EditorUtility.IsDirty(asset)
                            && ownedGuids.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))))
                            EditorUtility.ClearDirty(asset);
                    for (int i = moves.Count - 1; i >= 0; i--) RenameMove(moves[i].Value, moves[i].Key, null);
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (string path in Directory.GetFiles(backup + "/files", "*", SearchOption.AllDirectories).Select(Normalize).Concat(new[] { backup + "/files.meta" }))
                        {
                            string target = plan.preview.folder + path.Substring((backup + "/files").Length);
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            File.Copy(path, target, true);
                        }
                    }
                    finally { AssetDatabase.StopAssetEditing(); }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var expectedFiles = Directory.GetFiles(backup + "/files", "*", SearchOption.AllDirectories).Select(Normalize)
                        .Select(p => p.Substring((backup + "/files").Length)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                    var restoredFiles = Directory.GetFiles(plan.preview.folder, "*", SearchOption.AllDirectories).Select(Normalize)
                        .Select(p => p.Substring(plan.preview.folder.Length)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                    if (!expectedFiles.SequenceEqual(restoredFiles)) throw new IOException(AvatarForkText.RestoreFilesMismatch);
                    foreach (string path in Directory.GetFiles(backup + "/files", "*", SearchOption.AllDirectories).Select(Normalize).Concat(new[] { backup + "/files.meta" }))
                    {
                        string target = plan.preview.folder + path.Substring((backup + "/files").Length);
                        if (!File.Exists(target) || Hash(path) != Hash(target))
                            throw new IOException(AvatarForkText.RestoreContentMismatch + target);
                    }
                    ReopenRenamedScenes(setup, plan, false);
                    if (temporaryScene.IsValid() && temporaryScene.isLoaded && SceneManager.sceneCount > 1)
                        EditorSceneManager.CloseScene(temporaryScene, true);
                    File.WriteAllText(backup + "/restored.txt", "Restored", Utf8);
                }
                catch (Exception restoreError)
                {
                    throw RecoveryRequired(new IOException(AvatarForkText.RestoreFailed + backup + "\n" + error.Message + "\n" + restoreError.Message, error));
                }
                throw new InvalidOperationException(AvatarForkText.RenameRestored + error.Message, error);
            }
            finally { EditorUtility.ClearProgressBar(); }
            try { ReopenRenamedScenes(setup, plan, true); }
            catch (Exception error) { Debug.LogWarning(AvatarForkText.ReopenFailed + plan.report.scene + "\n" + error.Message); }
            finally
            {
                if (temporaryScene.IsValid() && temporaryScene.isLoaded && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(temporaryScene, true);
            }
            return plan.report;
        }

        static void RenameMove(string source, string destination, List<KeyValuePair<string, string>> moves, string backup = null)
        {
            var record = new RenameMoveRecord { source = source, destination = destination };
            if (backup != null) File.AppendAllText(backup + "/moves.jsonl", JsonUtility.ToJson(record) + "\n", Utf8);
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) throw new IOException(source + " → " + destination + ": " + error);
            moves?.Add(new KeyValuePair<string, string>(source, destination));
            record.completed = true;
            if (backup != null) File.AppendAllText(backup + "/moves.jsonl", JsonUtility.ToJson(record) + "\n", Utf8);
        }

        static void RewriteRenamedFiles(RenamePlan plan)
        {
            var shaders = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var asset in plan.report.assets.Where(a => Path.GetExtension(a.destination).Equals(".shader", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (Match match in ShaderName.Matches(File.ReadAllText(plan.paths[asset.destination], Utf8)))
                {
                    string prefix = "VrcAvatarBranch/" + plan.preview.previousName + "/";
                    if (!match.Groups[1].Value.StartsWith(prefix, StringComparison.Ordinal))
                        throw new InvalidOperationException(AvatarForkText.ShaderMismatch + asset.destination);
                    shaders[match.Groups[1].Value] = "VrcAvatarBranch/" + plan.preview.name + "/" + match.Groups[1].Value.Substring(prefix.Length);
                }
            }
            var replacements = plan.references;
            foreach (var entry in plan.paths.Where(p => Path.GetFileName(p.Key) != ReportName))
                foreach (string path in new[] { entry.Value, entry.Value + ".meta" }.Where(File.Exists))
                {
                    if (!IsText(path)) continue;
                    if (!path.EndsWith(".meta", StringComparison.Ordinal) && NativeExtensions.Contains(Path.GetExtension(path))) continue;
                    string text = File.ReadAllText(path, Utf8);
                    if (ShaderExtensions.Contains(Path.GetExtension(entry.Key)) && !path.EndsWith(".meta", StringComparison.Ordinal))
                    {
                        text = Include.Replace(text, match => {
                            string include = match.Groups[2].Value;
                            if (include.StartsWith("Assets/", StringComparison.Ordinal) || include.StartsWith("Packages/", StringComparison.Ordinal))
                                return match.Value;
                            string absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry.Key), include));
                            var target = plan.paths.FirstOrDefault(p => string.Equals(Path.GetFullPath(p.Key), absolute, StringComparison.OrdinalIgnoreCase));
                            return target.Key == null ? match.Value : match.Groups[1].Value + "\"" + target.Value + "\"";
                        });
                        foreach (var shader in shaders.OrderByDescending(p => p.Key.Length))
                        {
                            text = text.Replace("\"" + shader.Key + "\"", "\"" + shader.Value + "\"");
                            text = text.Replace("\"" + shader.Key + "/", "\"" + shader.Value + "/");
                        }
                    }
                    if (Wavefront(entry.Key) && !path.EndsWith(".meta", StringComparison.Ordinal))
                        text = WavefrontReference.Replace(text, match => {
                            string absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry.Key), match.Groups[2].Value.Trim()));
                            var target = plan.paths.FirstOrDefault(p => string.Equals(Path.GetFullPath(p.Key), absolute, StringComparison.OrdinalIgnoreCase));
                            if (target.Key == null) return match.Value;
                            var owner = new Uri(Path.GetFullPath(Path.GetDirectoryName(entry.Value)) + Path.DirectorySeparatorChar);
                            return match.Groups[1].Value + Uri.UnescapeDataString(owner.MakeRelativeUri(new Uri(Path.GetFullPath(target.Value))).ToString());
                        });
                    text = QuotedAsset.Replace(text, match => replacements.TryGetValue(match.Groups[1].Value, out var target) ? "\"" + target + "\"" : match.Value);
                    text = PlainAsset.Replace(text, match => replacements.TryGetValue(match.Groups["path"].Value, out var target) ? match.Groups["prefix"].Value + target : match.Value);
                    File.WriteAllText(path, text, Utf8);
                }
        }

        static void RenameDisplayNames(RenamePlan plan)
        {
            foreach (string path in plan.paths.Values.Where(p => !p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(p) != ReportName))
            {
                foreach (var asset in SerializedObjects(path))
                    if (RewriteRenameProperties(new[] { asset }, plan.references)) AssetDatabase.SaveAssetIfDirty(asset);
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && RewriteRenameProperties(new Object[] { importer }, plan.references)) AssetDatabase.WriteImportSettingsIfDirty(path);
            }
            foreach (string path in plan.report.assets.Select(a => plan.paths[a.destination]).Concat(plan.report.extractedMaterials.Select(a => plan.paths[a.destination])))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null || asset is GameObject || !NativeExtensions.Contains(Path.GetExtension(path))) continue;
                string oldPath = plan.paths.First(p => p.Value == path).Key;
                string before = plan.displayNames[oldPath];
                bool followsFileName = asset is Material || asset is RuntimeAnimatorController || asset is ScriptableObject;
                asset.name = followsFileName && before == Path.GetFileNameWithoutExtension(oldPath) ? Path.GetFileNameWithoutExtension(path) : before;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            string prefab = plan.paths[plan.report.prefab];
            var root = PrefabUtility.LoadPrefabContents(prefab);
            try
            {
                root.name = plan.preview.name;
                RewriteRenameProperties(root.GetComponentsInChildren<Component>(true), plan.references);
                PrefabUtility.SaveAsPrefabAsset(root, prefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            foreach (string scenePath in plan.paths.Values.Where(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
                RenameSceneContents(scenePath, prefab, plan);
        }

        static void RenameSceneContents(string scenePath, string prefab, RenamePlan plan)
        {
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                bool modified = false;
                if (scenePath == plan.paths[plan.report.scene])
                {
                    var roots = scene.GetRootGameObjects().Where(g => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == prefab).ToArray();
                    if (roots.Length != 1) throw new InvalidOperationException(AvatarForkText.AmbiguousAvatar);
                    if (roots[0].name == plan.preview.previousName) { roots[0].name = plan.preview.name; modified = true; }
                }
                foreach (var sceneRoot in scene.GetRootGameObjects()) modified |= RewriteRenameProperties(sceneRoot.GetComponentsInChildren<Component>(true), plan.references);
                if (modified && !EditorSceneManager.SaveScene(scene)) throw new IOException(AvatarForkText.SaveRenamedScene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }

        static bool RewriteRenameProperties(IEnumerable<Object> objects, Dictionary<string, string> paths)
        {
            bool changed = false;
            foreach (var asset in objects.Where(o => o != null))
            {
                var serialized = new SerializedObject(asset);
                var property = serialized.GetIterator();
                bool modified = false;
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.String && property.name != "m_Name"
                        && paths.TryGetValue(property.stringValue, out var target) && property.stringValue != target)
                    { property.stringValue = target; modified = true; }
                if (modified) { serialized.ApplyModifiedPropertiesWithoutUndo(); changed = true; }
            }
            return changed;
        }

        static void ReopenRenamedScenes(SceneSetup[] setup, RenamePlan plan, bool renamed)
        {
            foreach (var entry in setup.Where(s => Inside(s.path, plan.preview.folder)))
            {
                string path = renamed && plan.paths.TryGetValue(entry.path, out var target) ? target : entry.path;
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || (entry.isLoaded && !scene.isLoaded))
                    scene = EditorSceneManager.OpenScene(path, entry.isLoaded ? OpenSceneMode.Additive : OpenSceneMode.AdditiveWithoutLoading);
                if (entry.isActive) SceneManager.SetActiveScene(scene);
            }
            Scene previous = default;
            foreach (var entry in setup)
            {
                string path = renamed && plan.paths.TryGetValue(entry.path, out var target) ? target : entry.path;
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid()) continue;
                if (previous.IsValid()) EditorSceneManager.MoveSceneAfter(scene, previous);
                previous = scene;
            }
        }
    }
}
