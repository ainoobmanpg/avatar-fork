using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VrcAvatarWorkspace.Branching
{
    [Serializable]
    public sealed class CopiedAsset
    {
        public string source;
        public string destination;
        public string sourceGuid;
        public string destinationGuid;
        public string originalName;
        public string nameKey;
        public string originalShaderName;
    }

    [Serializable]
    public sealed class ExtractedMaterial
    {
        public string sourceModel;
        public long sourceLocalId;
        public string destination;
        public string originalName;
        public string nameKey;
        public string destinationGuid;
    }

    public sealed class BranchDestination
    {
        public string name;
        public string folder;
        public string prefab;
        public string scene;
    }

    [Serializable]
    public sealed class BranchReport
    {
        public int version = 1;
        public string status;
        public string branchName;
        public string sourceAvatar;
        public string sourceScene;
        public string folder;
        public string prefab;
        public string scene;
        public CopiedAsset[] assets = Array.Empty<CopiedAsset>();
        public ExtractedMaterial[] extractedMaterials = Array.Empty<ExtractedMaterial>();
        public string[] shared = Array.Empty<string>();
        public string[] missingSourceReferences = Array.Empty<string>();
        public string[] problems = Array.Empty<string>();
    }

    public static partial class AvatarBrancher
    {
        public const string ReportName = "BranchReport.json";
        public const string DefaultParent = "Assets/07_改変用/独立複製";
        static readonly Regex GuidReference = new Regex(@"\bguid:\s*([0-9a-fA-F]{32})\b", RegexOptions.Compiled);
        static readonly Regex ObjectReference = new Regex(@"\{\s*fileID:\s*-?\d+\s*,\s*guid:\s*([0-9a-fA-F]{32})\s*,\s*type:\s*\d+\s*\}", RegexOptions.Compiled);
        static readonly Regex Include = new Regex("(#\\s*include(?:_with_pragmas)?\\s*)\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex ShaderName = new Regex("\\bShader\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex WavefrontReference = new Regex(@"(?m)^(\s*(?:mtllib|map_\w+|bump|disp|decal)\s+)([^\r\n]+)", RegexOptions.Compiled);
        static readonly Regex QuotedAsset = new Regex("\"(Assets/[^\"\r\n]+)\"", RegexOptions.Compiled);
        static readonly Regex PlainAsset = new Regex(@"(?m)(?<prefix>^\s*(?:-\s+)?(?!(?:m_Name|m_EditorClassIdentifier):)[\w.]+:\s*|^\s*-\s+)(?<path>Assets/[^\r\n""']+?)\s*$", RegexOptions.Compiled);
        static readonly HashSet<string> NativeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".asset", ".mat", ".anim", ".controller", ".overrideController", ".prefab", ".unity", ".mask", ".playable", ".renderTexture", ".physicMaterial", ".physicsMaterial2D", ".lighting", ".guiskin", ".flare", ".mixer" };
        static readonly HashSet<string> ShaderExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".shader", ".cginc", ".hlsl", ".compute", ".glslinc" };
        static readonly HashSet<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".cs", ".dll", ".asmdef", ".asmref", ".js", ".boo", ".rsp" };
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        sealed class Plan
        {
            public DirtyAssets dirty;
            public string branchName;
            public readonly Dictionary<string, CopiedAsset> assets = new Dictionary<string, CopiedAsset>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> missingGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> missingSources = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> shaders = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> originalHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Queue<string> pending = new Queue<string>();
            public readonly Dictionary<string, BranchReport> previousReports = new Dictionary<string, BranchReport>(StringComparer.OrdinalIgnoreCase);
            public ExtractedMaterial[] extractedMaterials = Array.Empty<ExtractedMaterial>();
        }

        sealed class DirtyAssets : IDisposable
        {
            readonly Object[] originals;
            readonly HashSet<int> ids;
            readonly Dictionary<int, HideFlags> flags;
            public DirtyAssets()
            {
                originals = Resources.FindObjectsOfTypeAll<Object>().Where(o => o != null
                    && EditorUtility.IsDirty(o) && AssetDatabase.GetAssetPath(o).StartsWith("Assets/", StringComparison.Ordinal)).ToArray();
                ids = new HashSet<int>(originals.Select(o => o.GetInstanceID()));
                flags = originals.ToDictionary(o => o.GetInstanceID(), o => o.hideFlags);
                foreach (var original in originals.Where(o => EditorUtility.IsPersistent(o) && NativeExtensions.Contains(Path.GetExtension(AssetDatabase.GetAssetPath(o)))))
                {
                    original.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    EditorUtility.ClearDirty(original);
                }
            }
            public bool Contains(Object asset) { return asset != null && ids.Contains(asset.GetInstanceID()); }
            public HideFlags OriginalFlags(Object asset) { return flags.TryGetValue(asset.GetInstanceID(), out var value) ? value : asset.hideFlags; }
            public void Dispose() { foreach (var original in originals) if (original != null) { original.hideFlags = flags[original.GetInstanceID()]; EditorUtility.SetDirty(original); } }
        }

        public static GameObject AvatarRoot(GameObject selected)
        {
            if (selected == null || EditorUtility.IsPersistent(selected)) return null;
            for (var t = selected.transform; t != null; t = t.parent)
                if (t.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"))
                    return t.gameObject;
            return selected.transform.root.gameObject;
        }

        public static BranchReport Create(GameObject source, string parent, string branchName)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Play Modeとコンパイルが終わってから実行してください。");
            if (source == null || EditorUtility.IsPersistent(source) || !source.scene.IsValid() || EditorSceneManager.IsPreviewScene(source.scene))
                throw new ArgumentException("Hierarchyにあるアバターを選択してください。");
            if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => string.IsNullOrEmpty(s.path) && s.isDirty && !EditorSceneManager.IsPreviewScene(s)))
                throw new InvalidOperationException("名前の付いていない未保存Sceneがあります。先にSceneへ名前を付けて保存してください。名前のあるSceneの未保存変更は、そのまま複製できます。");
            var destination = ResolveDestination(parent, branchName);
            CheckHierarchy(source);

            using (var dirty = new DirtyAssets())
            {
            Directory.CreateDirectory(Path.GetDirectoryName(destination.folder));
            AssetDatabase.Refresh();
            destination = ResolveDestination(parent, branchName);
            string folder = destination.folder;
            RequireAssetsPath(folder);
            if (Directory.Exists(folder)) throw new IOException("出力先が既に存在します。");
            Directory.CreateDirectory(folder);
            var report = new BranchReport {
                status = "Preparing", sourceAvatar = source.name, sourceScene = source.scene.path,
                branchName = destination.name, folder = folder, prefab = destination.prefab, scene = destination.scene
            };
            var plan = new Plan { dirty = dirty, branchName = destination.name };
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject anchor = null;
            GameObject clone = null;
            try
            {
                EditorUtility.DisplayProgressBar("アバターを独立して複製", "現在の改変状態を読み取っています", .02f);
                anchor = EditorUtility.CreateGameObjectWithHideFlags("AvatarBranchWorkspace", HideFlags.HideAndDontSave);
                SceneManager.MoveGameObjectToScene(anchor, preview);
                clone = Object.Instantiate(source, anchor.transform, false);
                clone.transform.SetParent(null, true);
                clone.name = destination.name;
                Unpack(clone);
                ClearUploadIdentity(clone);
                if (PrefabUtility.SaveAsPrefabAsset(clone, report.prefab) == null)
                    throw new IOException("現在のアバターをPrefabへ保存できませんでした。");
                if (!IsText(report.prefab))
                    throw new InvalidOperationException("バイナリ形式のPrefabには対応していません。Asset SerializationをForce Textにしてから実行してください。");
                foreach (var path in AssetDatabase.GetDependencies(report.prefab, true)) Enqueue(plan, path, folder);
                foreach (Match match in GuidReference.Matches(File.ReadAllText(report.prefab, Utf8))) DiscoverGuid(plan, match.Groups[1].Value, folder, report.prefab);
                CollectLiveReferences(plan, source, folder);
                Discover(plan, folder);
                AssignDestinations(plan, report);
                report.assets = plan.assets.Values.OrderBy(a => a.source, StringComparer.Ordinal).ToArray();
                report.shared = plan.shared.OrderBy(p => p, StringComparer.Ordinal).ToArray();
                report.missingSourceReferences = plan.missingSources.OrderBy(p => p, StringComparer.Ordinal).ToArray();
                SnapshotHashes(plan);
                CopyFiles(plan, report);
                OverlayUnsavedAssets(plan);
                RemapModelMaterials(plan, report);
                RewriteStringReferences(plan, report);
                RewriteCopies(plan, report);
                ImportCopies(report);

                EditorUtility.DisplayProgressBar("アバターを独立して複製", "保存した素材の参照を検査しています", .9f);
                var problems = Validate(report);
                CheckHashes(plan, problems);
                if (problems.Count != 0)
                    throw new InvalidOperationException(string.Join("\n", problems.Take(12)));

                var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(report.prefab);
                if (avatar == null || MissingScripts(avatar) != MissingScripts(source))
                    throw new InvalidOperationException("複製後にコンポーネントの欠落が発生しました。");
                SaveScene(report, avatar);
                problems = Validate(report);
                CheckHashes(plan, problems);
                if (problems.Count != 0) throw new InvalidOperationException(string.Join("\n", problems.Take(12)));
                report.status = "Complete";
                WriteReport(report);
                AssetDatabase.ImportAsset(folder + "/" + ReportName);
                return report;
            }
            catch (Exception error)
            {
                report.status = "Failed";
                report.assets = plan.assets.Values.ToArray();
                report.shared = plan.shared.ToArray();
                report.missingSourceReferences = plan.missingSources.ToArray();
                report.problems = new[] { error.Message };
                WriteReport(report);
                throw;
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                if (anchor != null) Object.DestroyImmediate(anchor);
                EditorSceneManager.ClosePreviewScene(preview);
                EditorUtility.ClearProgressBar();
            }
            }
        }

        static void CheckHierarchy(GameObject root)
        {
            if (MissingScripts(root) != 0) throw new InvalidOperationException("元のアバターにMissing Scriptがあります。複製前に確認してください。");
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                var property = new SerializedObject(component).GetIterator();
                while (property.Next(true))
                {
                    if (PrefabLink(property.name)) continue;
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    var transform = value is Component c ? c.transform : (value as GameObject)?.transform;
                    if (transform != null && !EditorUtility.IsPersistent(value) && transform != root.transform && !transform.IsChildOf(root.transform))
                        throw new InvalidOperationException(component.name + " の " + property.propertyPath + " がアバター外のSceneオブジェクトを参照しています。");
                    if (value != null && transform == null && !EditorUtility.IsPersistent(value) && !(value is MonoScript))
                        throw new InvalidOperationException(component.name + " の " + property.propertyPath + " は未保存の素材です。素材を保存してから複製してください。");
                }
            }
        }

        static void Unpack(GameObject root)
        {
            while (true)
            {
                var instance = root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject)
                    .FirstOrDefault(PrefabUtility.IsAnyPrefabInstanceRoot);
                if (instance == null) break;
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
        }

        static bool IsPipeline(Object asset) { return asset != null && asset.GetType().FullName == "VRC.Core.PipelineManager"; }

        static SerializedProperty BlueprintId(SerializedObject serialized)
        {
            var property = serialized.FindProperty("blueprintId");
            if (property == null || property.propertyType != SerializedPropertyType.String)
                throw new InvalidOperationException("Pipeline ManagerのBlueprint IDを確認できません。複製を停止します。");
            return property;
        }

        public static void ClearUploadIdentity(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            foreach (var component in root.GetComponentsInChildren<Component>(true).Where(IsPipeline))
            {
                var serialized = new SerializedObject(component);
                BlueprintId(serialized).stringValue = "";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static BranchDestination ResolveDestination(string parent, string requestedName)
        {
            parent = Normalize(parent).TrimEnd('/');
            RequireAssetsPath(parent);
            RequireUnlinkedPath(parent);
            if (FindBranchFolder(parent + "/destination") != null)
                throw new ArgumentException("独立複製の中へ別のコピーは作れません。コピーの外の保存先を選んでください。");
            string name = RequireBranchName(requestedName);
            string folder = parent + "/" + name;
            for (int suffix = 1; Directory.Exists(folder) || File.Exists(folder) || File.Exists(folder + ".meta") || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(folder)); suffix++)
                folder = parent + "/" + name + " " + suffix;
            name = Path.GetFileName(folder);
            return new BranchDestination { name = name, folder = folder, prefab = folder + "/" + name + ".prefab", scene = folder + "/" + name + ".unity" };
        }

        static void Enqueue(Plan plan, string path, string folder)
        {
            path = Normalize(path);
            if (string.IsNullOrEmpty(path) || Inside(path, folder)) return;
            if (Shared(path)) { plan.shared.Add(path); return; }
            RequireAssetsPath(path);
            if (AssetDatabase.IsValidFolder(path)) throw new InvalidOperationException("フォルダー自体への参照は複製できません: " + path);
            if (!File.Exists(path)) throw new FileNotFoundException("素材が見つかりません", path);
            if (plan.assets.ContainsKey(path)) return;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (guid.Length != 32 || !File.Exists(path + ".meta")) throw new InvalidOperationException("素材の識別情報を取得できません: " + path);
            var previous = PreviousAsset(plan, path);
            string originalName = previous?.originalName ?? Path.GetFileName(path);
            var asset = new CopiedAsset {
                source = path, sourceGuid = guid, destinationGuid = GUID.Generate().ToString(), originalName = originalName,
                nameKey = string.IsNullOrEmpty(previous?.nameKey) ? guid : previous.nameKey,
                originalShaderName = previous?.originalShaderName
            };
            plan.assets.Add(path, asset);
            plan.pending.Enqueue(path);
        }

        static void Discover(Plan plan, string folder)
        {
            string shaderPrefix = "VrcAvatarBranch/" + plan.branchName + "/" + GUID.Generate().ToString().Substring(0, 12) + "/";
            while (plan.pending.Count > 0)
            {
                string path = plan.pending.Dequeue();
                if (NativeExtensions.Contains(Path.GetExtension(path)) && !IsText(path))
                    throw new InvalidOperationException("バイナリ形式のUnity素材には対応していません: " + path);
                foreach (string dependency in AssetDatabase.GetDependencies(path, false)) Enqueue(plan, dependency, folder);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null || asset is Texture || asset is Mesh || asset is AudioClip) continue;
                    CollectSerializedReferences(plan, asset, folder);
                    if (asset is Material material && material.parent != null) Enqueue(plan, AssetDatabase.GetAssetPath(material.parent), folder);
                }
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null)
                {
                    if (EditorUtility.IsDirty(importer) || plan.dirty.Contains(importer)) throw new InvalidOperationException("素材の読込設定に未保存変更があります。先にApplyしてください: " + path);
                    foreach (var value in importer.GetExternalObjectMap().Values) Enqueue(plan, AssetDatabase.GetAssetPath(value), folder);
                }
                foreach (string textPath in new[] { path, path + ".meta" })
                {
                    if (!IsText(textPath)) continue;
                    string text = File.ReadAllText(textPath, Utf8);
                    foreach (Match match in GuidReference.Matches(text)) DiscoverGuid(plan, match.Groups[1].Value, folder, textPath);
                    foreach (Match match in QuotedAsset.Matches(text))
                        if (File.Exists(match.Groups[1].Value)) Enqueue(plan, match.Groups[1].Value, folder);
                    foreach (Match match in PlainAsset.Matches(text))
                        if (File.Exists(match.Groups["path"].Value)) Enqueue(plan, match.Groups["path"].Value, folder);
                }
                if (Wavefront(path))
                    foreach (Match match in WavefrontReference.Matches(File.ReadAllText(path, Utf8)))
                        Enqueue(plan, ResolveInclude(path, match.Groups[2].Value.Trim()), folder);
                if (!ShaderExtensions.Contains(Path.GetExtension(path))) continue;
                string shaderText = File.ReadAllText(path, Utf8);
                foreach (Match match in Include.Matches(shaderText))
                {
                    string include = ResolveInclude(path, match.Groups[2].Value);
                    if (include != null) Enqueue(plan, include, folder);
                }
                foreach (Match match in ShaderName.Matches(shaderText))
                {
                    var entry = plan.assets[path];
                    if (string.IsNullOrEmpty(entry.originalShaderName))
                        entry.originalShaderName = PreviousAsset(plan, path) == null ? match.Groups[1].Value
                            : Regex.Replace(match.Groups[1].Value, @"^(?:VrcAvatarBranch/[0-9a-f]{12}/)+", "");
                    plan.shaders[match.Groups[1].Value] = shaderPrefix + entry.originalShaderName;
                }
                foreach (Match match in Regex.Matches(shaderText, "\\b(?:Fallback|UsePass)\\s*\"([^\"]+)\""))
                {
                    string name = match.Groups[1].Value;
                    var shader = Shader.Find(name) ?? (name.Contains('/') ? Shader.Find(name.Substring(0, name.LastIndexOf('/'))) : null);
                    if (shader != null) Enqueue(plan, AssetDatabase.GetAssetPath(shader), folder);
                }
            }
        }

        static void DiscoverGuid(Plan plan, string guid, string folder, string owner)
        {
            if (BuiltinGuid(guid)) return;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                plan.missingGuids.Add(guid);
                plan.missingSources.Add(owner + " / " + guid);
                return;
            }
            Enqueue(plan, path, folder);
        }

        static void CollectLiveReferences(Plan plan, GameObject source, string folder)
        {
            foreach (var component in source.GetComponentsInChildren<Component>(true)) CollectSerializedReferences(plan, component, folder);
        }

        static void CollectSerializedReferences(Plan plan, Object asset, string folder)
        {
            var property = new SerializedObject(asset).GetIterator();
            while (property.Next(true))
            {
                if (PrefabLink(property.name)) continue;
                if (property.propertyType == SerializedPropertyType.String && property.name != "m_Name"
                    && property.stringValue.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(property.stringValue))
                    Enqueue(plan, property.stringValue, folder);
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                {
                    string path = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                    if (!string.IsNullOrEmpty(path)) Enqueue(plan, path, folder);
                }
            }
        }

        static void SnapshotHashes(Plan plan)
        {
            foreach (var asset in plan.assets.Values)
                foreach (var path in new[] { asset.source, asset.source + ".meta" }) plan.originalHashes[path] = Hash(path);
        }

        static string AssetCategory(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".mat") return "Materials";
            if (ShaderExtensions.Contains(extension)) return "Shaders";
            if (AssetImporter.GetAtPath(path) is ModelImporter || extension == ".mtl") return "Models";
            if (AssetDatabase.GetMainAssetTypeAtPath(path) is Type type && typeof(Texture).IsAssignableFrom(type)) return "Textures";
            if (extension == ".controller" || extension == ".overridecontroller") return "Controllers";
            if (extension == ".anim" || extension == ".mask") return "Animations";
            if (extension == ".prefab") return "Prefabs";
            if (extension == ".asset") return "Settings";
            if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(AudioClip)) return "Audio";
            return "Other";
        }

        static string SafeStem(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(name.Trim(' ', '.')) ? "Material" : name.Trim(' ', '.');
        }

        static string AssetDestination(string folder, string category, string originalName, string branchName, string key = null)
        {
            string suffix = "";
            if (key != null)
                using (var sha = SHA256.Create()) suffix = "__" + BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(key))).Replace("-", "").Substring(0, 8).ToLowerInvariant();
            return folder + "/" + category + "/" + Path.GetFileNameWithoutExtension(originalName) + suffix + "__" + branchName + Path.GetExtension(originalName);
        }

        static void AssignDestinations(Plan plan, BranchReport report)
        {
            foreach (var group in plan.assets.Values.GroupBy(a => AssetDestination(report.folder, AssetCategory(a.source), a.originalName, report.branchName), StringComparer.OrdinalIgnoreCase))
            {
                bool collision = group.Count() > 1;
                var repeatedKeys = new HashSet<string>(group.GroupBy(a => a.nameKey).Where(g => g.Count() > 1).Select(g => g.Key));
                foreach (var asset in group)
                {
                    if (repeatedKeys.Contains(asset.nameKey)) asset.nameKey += "_" + asset.sourceGuid;
                    asset.destination = collision ? AssetDestination(report.folder, AssetCategory(asset.source), asset.originalName, report.branchName, asset.nameKey) : group.Key;
                }
            }
            if (plan.assets.Values.Select(a => a.destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.assets.Count)
                throw new InvalidOperationException("素材名の識別子が重複しました。保存先を作り直してください。");
        }

        static void CopyFiles(Plan plan, BranchReport report)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                int index = 0;
                foreach (var asset in plan.assets.Values)
                {
                    EditorUtility.DisplayProgressBar("アバターを独立して複製", "素材をコピー: " + asset.originalName, .1f + .55f * index++ / Math.Max(1, plan.assets.Count));
                    Directory.CreateDirectory(Path.GetDirectoryName(asset.destination));
                    File.Copy(asset.source, asset.destination, false);
                    File.WriteAllText(asset.destination + ".meta", Rewrite(File.ReadAllText(asset.source + ".meta", Utf8), asset.source + ".meta", plan), Utf8);
                    if (IsText(asset.source))
                        File.WriteAllText(asset.destination, Rewrite(File.ReadAllText(asset.source, Utf8), asset.source, plan), Utf8);
                    else if (NativeExtensions.Contains(Path.GetExtension(asset.source)))
                        throw new InvalidOperationException("バイナリ形式のUnity素材には対応していません: " + asset.source);
                }
                File.WriteAllText(report.prefab, Rewrite(File.ReadAllText(report.prefab, Utf8), report.prefab, plan), Utf8);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void OverlayUnsavedAssets(Plan plan)
        {
            foreach (var asset in plan.assets.Values)
            {
                var sourceObjects = AssetDatabase.LoadAllAssetsAtPath(asset.source);
                if (!sourceObjects.Any(o => o != null && (EditorUtility.IsDirty(o) || plan.dirty.Contains(o)))) continue;
                if (!NativeExtensions.Contains(Path.GetExtension(asset.source)))
                {
                    if (sourceObjects.Any(o => o != null && !(o is Shader) && (EditorUtility.IsDirty(o) || plan.dirty.Contains(o))))
                        throw new InvalidOperationException("未保存の変更を含む読込素材があります。先に保存してください: " + asset.source);
                    continue;
                }
                var copies = AssetDatabase.LoadAllAssetsAtPath(asset.destination);
                foreach (var source in sourceObjects.Where(o => o != null && (EditorUtility.IsDirty(o) || plan.dirty.Contains(o))))
                {
                    var copy = copies.SingleOrDefault(o => SameLocalObject(source, o));
                    if (copy == null) throw new InvalidOperationException("未保存の素材を複製へ転記できません: " + asset.source);
                    EditorUtility.CopySerialized(source, copy);
                    copy.hideFlags = plan.dirty.OriginalFlags(source);
                    EditorUtility.SetDirty(copy);
                    AssetDatabase.SaveAssetIfDirty(copy);
                }
            }
        }

        static void RemapModelMaterials(Plan plan, BranchReport report)
        {
            var extracted = new List<ExtractedMaterial>();
            foreach (var entry in plan.assets.Values.Where(a => AssetImporter.GetAtPath(a.source) is ModelImporter))
            {
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(entry.source);
                var importer = (ModelImporter)AssetImporter.GetAtPath(entry.destination);
                var materials = AssetDatabase.LoadAllAssetsAtPath(entry.source).OfType<Material>().ToArray();
                var mappings = materials.ToDictionary(m => m.name, m => m, StringComparer.Ordinal);
                var sourceRenderers = original.GetComponentsInChildren<Renderer>(true);
                var copyRenderers = AssetDatabase.LoadAssetAtPath<GameObject>(entry.destination).GetComponentsInChildren<Renderer>(true);
                if (sourceRenderers.Length != copyRenderers.Length) throw new InvalidOperationException("モデルの構造が読み込み時に変わりました: " + entry.source);
                for (int r = 0; r < sourceRenderers.Length; r++)
                {
                    var before = sourceRenderers[r].sharedMaterials;
                    var after = copyRenderers[r].sharedMaterials;
                    if (before.Length != after.Length) throw new InvalidOperationException("モデルのマテリアル数が読み込み時に変わりました: " + entry.source);
                    for (int m = 0; m < before.Length; m++)
                        if (before[m] != null && after[m] != null && AssetDatabase.GetAssetPath(after[m]) == entry.destination)
                        {
                            string identifier = after[m].name;
                            if (mappings.TryGetValue(identifier, out var existing) && existing != before[m])
                                throw new InvalidOperationException("モデルのマテリアル識別子が重複しています: " + entry.source + " / " + identifier);
                            mappings[identifier] = before[m];
                        }
                }
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                var copies = new Dictionary<Material, Material>();
                foreach (var material in mappings.Values.Distinct())
                {
                    string path = AssetDatabase.GetAssetPath(material);
                    Material copy;
                    if (path == entry.source)
                    {
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string _, out long id);
                        string originalName = SafeStem(Path.GetFileNameWithoutExtension(entry.originalName) + "_" + material.name) + ".mat";
                        string nameKey = entry.nameKey + "_" + id;
                        string destination = AssetDestination(report.folder, "Materials", originalName, report.branchName);
                        if (File.Exists(destination)) destination = AssetDestination(report.folder, "Materials", originalName, report.branchName, nameKey);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        copy = new Material(material) { name = Path.GetFileNameWithoutExtension(destination), hideFlags = HideFlags.None };
                        AssetDatabase.CreateAsset(copy, destination);
                        extracted.Add(new ExtractedMaterial { sourceModel = entry.source, sourceLocalId = id, destination = destination,
                            originalName = originalName, nameKey = nameKey, destinationGuid = AssetDatabase.AssetPathToGUID(destination) });
                    }
                    else if (plan.assets.TryGetValue(path, out var target)) copy = AssetDatabase.LoadAssetAtPath<Material>(target.destination);
                    else if (Shared(path)) copy = material;
                    else throw new InvalidOperationException("モデルのマテリアルを分離できません: " + path);
                    if (copy == null) throw new InvalidOperationException("モデルの複製マテリアルを読み込めません: " + path);
                    copies[material] = copy;
                }
                foreach (var mapping in mappings) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mapping.Key), copies[mapping.Value]);
                AssetDatabase.WriteImportSettingsIfDirty(entry.destination);
            }
            report.extractedMaterials = extracted.ToArray();
            plan.extractedMaterials = report.extractedMaterials;
        }

        static IEnumerable<Object> SerializedObjects(string path)
        {
            if (Path.GetExtension(path).Equals(".unity", StringComparison.OrdinalIgnoreCase)) yield break;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset == null || asset is Texture || asset is Mesh || asset is AudioClip) continue;
                yield return asset;
                if (asset is GameObject go && AssetDatabase.IsMainAsset(go))
                    foreach (var component in go.GetComponentsInChildren<Component>(true)) if (component != null) yield return component;
            }
        }

        static void RewriteStringReferences(Plan plan, BranchReport report)
        {
            foreach (string path in report.assets.Select(a => a.destination).Concat(report.extractedMaterials.Select(a => a.destination)).Concat(new[] { report.prefab }))
                foreach (var asset in SerializedObjects(path))
                {
                    var serialized = new SerializedObject(asset);
                    bool modified = IsPipeline(asset) && !string.IsNullOrEmpty(BlueprintId(serialized).stringValue);
                    if (modified) BlueprintId(serialized).stringValue = "";
                    if (AssetDatabase.IsMainAsset(asset) && (asset is Material || asset is RuntimeAnimatorController || asset is ScriptableObject))
                    {
                        var name = serialized.FindProperty("m_Name");
                        string displayName = Path.GetFileNameWithoutExtension(path);
                        if (name != null && name.stringValue != displayName) { name.stringValue = displayName; modified = true; }
                    }
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.String && property.name != "m_Name"
                            && plan.assets.TryGetValue(property.stringValue, out var target))
                        { property.stringValue = target.destination; modified = true; }
                    if (!modified) continue;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
        }

        static bool SameLocalObject(Object source, Object copy)
        {
            if (copy == null || copy.GetType() != source.GetType()) return false;
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long sourceId)
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(copy, out string _, out long copyId) && sourceId == copyId;
        }

        static void RewriteCopies(Plan plan, BranchReport report)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var asset in plan.assets.Values)
                    foreach (string path in new[] { asset.destination, asset.destination + ".meta" })
                        if (IsText(path)) File.WriteAllText(path, Rewrite(File.ReadAllText(path, Utf8), asset.source + (path.EndsWith(".meta", StringComparison.Ordinal) ? ".meta" : ""), plan), Utf8);
                foreach (var material in report.extractedMaterials)
                    File.WriteAllText(material.destination, Rewrite(File.ReadAllText(material.destination, Utf8), material.destination, plan), Utf8);
                File.WriteAllText(report.prefab, Rewrite(File.ReadAllText(report.prefab, Utf8), report.prefab, plan), Utf8);
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }

        static string Rewrite(string text, string source, Plan plan)
        {
            text = ObjectReference.Replace(text, match => {
                if (plan.missingGuids.Contains(match.Groups[1].Value)) return "{fileID: 0}";
                string id = Regex.Match(match.Value, @"fileID:\s*(-?\d+)").Groups[1].Value;
                foreach (var material in plan.extractedMaterials)
                {
                    var model = plan.assets[material.sourceModel];
                    if (id == material.sourceLocalId.ToString() && (match.Groups[1].Value == model.sourceGuid || match.Groups[1].Value == model.destinationGuid))
                        return "{fileID: 2100000, guid: " + AssetDatabase.AssetPathToGUID(material.destination) + ", type: 2}";
                }
                return match.Value;
            });
            var guids = plan.assets.Values.ToDictionary(a => a.sourceGuid, a => a.destinationGuid, StringComparer.OrdinalIgnoreCase);
            text = GuidReference.Replace(text, match => guids.TryGetValue(match.Groups[1].Value, out string guid) ? match.Value.Replace(match.Groups[1].Value, guid) : match.Value);
            if (ShaderExtensions.Contains(Path.GetExtension(source)))
            {
                text = Include.Replace(text, match => {
                    string path = ResolveInclude(source, match.Groups[2].Value);
                    return path != null && plan.assets.TryGetValue(path, out var entry) ? match.Groups[1].Value + "\"" + entry.destination + "\"" : match.Value;
                });
                foreach (var shader in plan.shaders.OrderByDescending(p => p.Key.Length))
                {
                    text = text.Replace("\"" + shader.Key + "\"", "\"" + shader.Value + "\"");
                    text = text.Replace("\"" + shader.Key + "/", "\"" + shader.Value + "/");
                }
            }
            if (Wavefront(source) && plan.assets.TryGetValue(source, out var wavefront))
                text = WavefrontReference.Replace(text, match => {
                    string current = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(wavefront.destination), match.Groups[2].Value.Trim()));
                    if (plan.assets.Values.Any(a => string.Equals(Path.GetFullPath(a.destination), current, StringComparison.OrdinalIgnoreCase))) return match.Value;
                    string path = ResolveInclude(source, match.Groups[2].Value.Trim());
                    if (path == null || !plan.assets.TryGetValue(path, out var target)) return match.Value;
                    var owner = new Uri(Path.GetFullPath(Path.GetDirectoryName(wavefront.destination)) + Path.DirectorySeparatorChar);
                    return match.Groups[1].Value + Uri.UnescapeDataString(owner.MakeRelativeUri(new Uri(Path.GetFullPath(target.destination))).ToString());
                });
            text = QuotedAsset.Replace(text, match => plan.assets.TryGetValue(match.Groups[1].Value, out var asset) ? "\"" + asset.destination + "\"" : match.Value);
            text = PlainAsset.Replace(text, match => plan.assets.TryGetValue(match.Groups["path"].Value, out var asset) ? match.Groups["prefix"].Value + asset.destination : match.Value);
            if (source.EndsWith(".meta", StringComparison.Ordinal) && text.Contains("ModelImporter:"))
                text = Regex.Replace(text, @"(?m)^(\s*materialLocation:)\s*\d+", "$1 1");
            return text;
        }

        static void ImportCopies(BranchReport report)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var asset in report.assets) AssetDatabase.ImportAsset(asset.destination, ImportAssetOptions.ForceUpdate);
                foreach (var material in report.extractedMaterials) AssetDatabase.ImportAsset(material.destination, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(report.prefab, ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static List<string> Validate(BranchReport report, bool requireEmptyBlueprintId = true)
        {
            var problems = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(report.folder, "*", SearchOption.AllDirectories).Select(Normalize)
                .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal) && p != report.folder + "/" + ReportName))
            {
                foreach (var file in new[] { path, path + ".meta" }.Where(File.Exists))
                {
                    if (!IsText(file)) continue;
                    string text = File.ReadAllText(file, Utf8);
                    foreach (Match match in GuidReference.Matches(text))
                    {
                        string guid = match.Groups[1].Value;
                        if (BuiltinGuid(guid)) continue;
                        string dependency = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrEmpty(dependency)) problems.Add("参照先が見つかりません: " + file + " / " + guid);
                        else if (!Inside(dependency, report.folder) && !Shared(dependency)) problems.Add("元の素材への参照が残っています: " + file + " → " + dependency);
                    }
                    foreach (Match match in QuotedAsset.Matches(text))
                    {
                        string dependency = match.Groups[1].Value;
                        if (File.Exists(dependency) && !Inside(dependency, report.folder) && !Shared(dependency)) problems.Add("元の素材パスが残っています: " + file + " → " + dependency);
                    }
                    foreach (Match match in PlainAsset.Matches(text))
                    {
                        string dependency = match.Groups["path"].Value;
                        if (File.Exists(dependency) && !Inside(dependency, report.folder) && !Shared(dependency)) problems.Add("元の素材パスが残っています: " + file + " → " + dependency);
                    }
                }
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                    if (!Inside(dependency, report.folder) && !Shared(dependency)) problems.Add("Unityの依存関係に元の素材が残っています: " + path + " → " + dependency);
                if (Wavefront(path))
                    foreach (Match match in WavefrontReference.Matches(File.ReadAllText(path, Utf8)))
                    {
                        string absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), match.Groups[2].Value.Trim()));
                        string root = Path.GetFullPath(report.folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (!File.Exists(absolute) || !absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase)) problems.Add("モデルの外部ファイルが複製内にありません: " + path + " / " + match.Groups[2].Value);
                    }
                if (Path.GetExtension(path).Equals(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material != null && material.parent != null)
                    {
                        string dependency = AssetDatabase.GetAssetPath(material.parent);
                        if (!Inside(dependency, report.folder) && !Shared(dependency)) problems.Add("親マテリアルが元を参照しています: " + path);
                    }
                }
                foreach (var asset in SerializedObjects(path))
                {
                    var serialized = new SerializedObject(asset);
                    if (requireEmptyBlueprintId && IsPipeline(asset) && !string.IsNullOrEmpty(BlueprintId(serialized).stringValue))
                        problems.Add("アップロード先のBlueprint IDが残っています: " + path);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.String && property.name != "m_Name")
                        {
                            string dependency = property.stringValue;
                            if (dependency.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(dependency)
                                && !Inside(dependency, report.folder) && !Shared(dependency))
                                problems.Add("設定の文字列に元の素材パスが残っています: " + path + " / " + property.propertyPath + " → " + dependency);
                        }
                }
            }
            if (requireEmptyBlueprintId && File.Exists(report.scene))
            {
                var open = typeof(EditorSceneManager).GetMethod("OpenPreviewScene",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null, new[] { typeof(string) }, null);
                if (open == null) throw new InvalidOperationException("このUnityでは保存SceneのBlueprint IDを非表示で検査できません。複製を停止します。");
                var scene = (Scene)open.Invoke(null, new object[] { report.scene });
                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var component in root.GetComponentsInChildren<Component>(true).Where(IsPipeline))
                            if (!string.IsNullOrEmpty(BlueprintId(new SerializedObject(component)).stringValue))
                                problems.Add("Sceneのアップロード先のBlueprint IDが残っています: " + report.scene);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            return problems.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        public static BranchReport Verify(string folder)
        {
            RequireAssetsPath(folder);
            var report = JsonUtility.FromJson<BranchReport>(File.ReadAllText(folder + "/" + ReportName, Utf8));
            if (report == null || report.version != 1 || !string.Equals(report.folder, folder, StringComparison.Ordinal))
                throw new InvalidOperationException("独立複製の記録がこのフォルダーに一致しません。");
            report.problems = Validate(report).ToArray();
            if (!File.Exists(report.prefab) || !File.Exists(report.scene))
                report.problems = report.problems.Concat(new[] { "複製したSceneまたはPrefabがありません。" }).ToArray();
            report.status = report.problems.Length == 0 ? "Complete" : "Failed";
            return report;
        }

        static void SaveScene(BranchReport report, GameObject asset)
        {
            Scene previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                PrefabUtility.InstantiatePrefab(asset, scene);
                if (!EditorSceneManager.SaveScene(scene, report.scene)) throw new IOException("複製Sceneを保存できませんでした。");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static void CheckHashes(Plan plan, ICollection<string> problems)
        {
            foreach (var entry in plan.originalHashes)
                if (!File.Exists(entry.Key) || Hash(entry.Key) != entry.Value) problems.Add("処理中に元の素材が変更されました: " + entry.Key);
        }

        static CopiedAsset PreviousAsset(Plan plan, string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            string directory = Normalize(Path.GetDirectoryName(path));
            while (directory.StartsWith("Assets/", StringComparison.Ordinal))
            {
                string reportPath = directory + "/" + ReportName;
                if (File.Exists(reportPath))
                {
                    if (!plan.previousReports.TryGetValue(reportPath, out var report))
                    {
                        report = JsonUtility.FromJson<BranchReport>(File.ReadAllText(reportPath, Utf8));
                        plan.previousReports.Add(reportPath, report);
                    }
                    var asset = report?.assets?.FirstOrDefault(a => a.destination == path || a.destinationGuid == guid);
                    if (asset != null) return asset;
                    var material = report?.extractedMaterials?.FirstOrDefault(a => a.destination == path || (!string.IsNullOrEmpty(a.destinationGuid) && a.destinationGuid == guid));
                    if (material != null)
                    {
                        string originalName = material.originalName;
                        string nameKey = material.nameKey;
                        if (string.IsNullOrEmpty(originalName))
                        {
                            var model = report.assets.First(a => a.source == material.sourceModel);
                            var embedded = AssetDatabase.LoadAllAssetsAtPath(material.sourceModel).OfType<Material>().FirstOrDefault(m => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string _, out long id) && id == material.sourceLocalId);
                            originalName = SafeStem(Path.GetFileNameWithoutExtension(model.originalName) + "_" + (embedded == null ? material.sourceLocalId.ToString() : embedded.name)) + ".mat";
                            nameKey = model.sourceGuid + "_" + material.sourceLocalId;
                        }
                        return new CopiedAsset { originalName = originalName, nameKey = nameKey };
                    }
                }
                directory = Normalize(Path.GetDirectoryName(directory));
            }
            return null;
        }

        public static string FindBranchFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string directory = Normalize(Path.GetDirectoryName(assetPath));
            while (directory.StartsWith("Assets/", StringComparison.Ordinal))
            {
                if (File.Exists(directory + "/" + ReportName)) return directory;
                directory = Normalize(Path.GetDirectoryName(directory));
            }
            return null;
        }

        static string ResolveInclude(string owner, string include)
        {
            if (include.StartsWith("Assets/", StringComparison.Ordinal) || include.StartsWith("Packages/", StringComparison.Ordinal))
                return File.Exists(include) ? Normalize(include) : throw new FileNotFoundException("Shaderのincludeが見つかりません: " + owner, include);
            string absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(owner) ?? "", include));
            string project = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (File.Exists(absolute))
            {
                if (!absolute.StartsWith(project, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Project外のShader includeには対応していません: " + include);
                return Normalize(absolute.Substring(project.Length));
            }
            string builtin = Path.Combine(EditorApplication.applicationContentsPath, "CGIncludes", include);
            if (File.Exists(builtin)) return null;
            throw new FileNotFoundException("Shaderのincludeを解決できません: " + owner, include);
        }

        static bool IsText(string path)
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) || ShaderExtensions.Contains(extension) || Wavefront(path)) return true;
            if (!NativeExtensions.Contains(extension)) return false;
            using (var stream = File.OpenRead(path))
            {
                var header = new byte[8];
                int count = stream.Read(header, 0, header.Length);
                int offset = count >= 3 && header[0] == 0xef && header[1] == 0xbb && header[2] == 0xbf ? 3 : 0;
                return count >= offset + 5 && Encoding.ASCII.GetString(header, offset, 5) == "%YAML";
            }
        }
        static bool Wavefront(string path) { string extension = Path.GetExtension(path);return extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mtl", StringComparison.OrdinalIgnoreCase); }

        static bool Shared(string path)
        {
            return path.StartsWith("Packages/", StringComparison.Ordinal) || path.StartsWith("Resources/", StringComparison.Ordinal)
                || path.StartsWith("Library/", StringComparison.Ordinal) || path == "ProjectSettings/ProjectSettings.asset"
                || CodeExtensions.Contains(Path.GetExtension(path));
        }
        static bool BuiltinGuid(string guid) { return guid.StartsWith("0000000000000000", StringComparison.Ordinal); }
        static bool PrefabLink(string name) { return name == "m_PrefabInstance" || name == "m_CorrespondingSourceObject" || name == "m_PrefabAsset"; }
        static bool Inside(string path, string folder) { return path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal); }
        static string Normalize(string path) { return (path ?? "").Replace('\\', '/'); }
        static string RequireBranchName(string requestedName)
        {
            string name = (requestedName ?? "").Trim();
            if (name.Length == 0 || name.EndsWith(".", StringComparison.Ordinal) || IgnoredName(name)
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0
                || name.Any(char.IsControl) || Regex.IsMatch(name, @"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new ArgumentException("複製名に使えない文字や名前があります。");
            return name;
        }
        static bool IgnoredName(string name)
        {
            return name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal)
                || name.Equals("cvs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
        }
        static void RequireUnlinkedPath(string path)
        {
            string project = Path.GetFullPath(".");
            for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
            {
                if ((current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    || (File.Exists(current.FullName + ".meta") && (File.GetAttributes(current.FullName + ".meta") & FileAttributes.ReparsePoint) != 0))
                    throw new InvalidOperationException("シンボリックリンクやジャンクションを含む保存先には対応していません: " + path);
                if (string.Equals(current.FullName, project, StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        static void RequireAssetsPath(string path)
        {
            if (Normalize(path).Split('/').Skip(1).Any(p => p == ".." || IgnoredName(p)))
                throw new ArgumentException("Unityが読み込まない名前や相対移動を含む保存先は使えません。");
            string full = Path.GetFullPath(path);
            string assets = Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Normalize(path).StartsWith("Assets/", StringComparison.Ordinal) || !full.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("保存先はAssets内を指定してください。");
        }
        static int MissingScripts(GameObject root) { return root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)); }
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        static void WriteReport(BranchReport report) { File.WriteAllText(report.folder + "/" + ReportName, JsonUtility.ToJson(report, true), Utf8); }
    }
}
