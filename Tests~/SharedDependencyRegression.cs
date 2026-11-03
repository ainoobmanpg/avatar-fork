using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrcAvatarWorkspace.Branching;
using Object = UnityEngine.Object;

class EditorCommand
{
    static readonly List<string> Passed = new List<string>();
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Passed.Add(name); }
    static string Hash(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }

    public static void Run()
    {
        if (string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/AvatarForkRegression/Empty.unity");
        var result = Newtonsoft.Json.Linq.JObject.FromObject(Execute());
        Directory.CreateDirectory("Library/AvatarFork/Regression");
        File.WriteAllText("Library/AvatarFork/Regression/shared-dependencies.json", result.ToString());
        Debug.Log(result.ToString());
        if (UnityEngine.Application.isBatchMode && !(bool)result["success"]) EditorApplication.Exit(1);
    }

    static object Execute()
    {
        Passed.Clear();
        string work = "Assets/99_検証用/AvatarFork_Shared_" + GUID.Generate().ToString().Substring(0, 8);
        Directory.CreateDirectory(work);
        AssetDatabase.Refresh();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var dependencies = new Dictionary<string, string[]>();
        try
        {
            EditorSceneManager.SetActiveScene(scene);
            var root = new GameObject("SharedFixture");
            var component = root.AddComponent<BranchSharedFixture>();
            string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(component));
            var importer = (MonoImporter)AssetImporter.GetAtPath(scriptPath);
            var icon = importer.GetIcon();
            var defaultTexture = (Texture2D)importer.GetDefaultReference("texture");
            var defaultMaterial = (Material)importer.GetDefaultReference("material");
            string defaultMaterialPath = AssetDatabase.GetAssetPath(defaultMaterial);
            dependencies["fixture"] = new[] { "icon=" + AssetDatabase.GetAssetPath(icon), "texture=" + AssetDatabase.GetAssetPath(defaultTexture), "material=" + defaultMaterialPath };
            Check(icon != null && defaultTexture != null && defaultMaterial != null, "shared script fixture has an icon and default references");
            component.texture = null;
            component.material = null;
            EditorSceneManager.SaveScene(scene, work + "/Source.unity");
            string sourcePrefab = work + "/Source.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, sourcePrefab);
            dependencies["prefabDirect"] = AssetDatabase.GetDependencies(sourcePrefab, false);
            dependencies["prefabRecursive"] = AssetDatabase.GetDependencies(sourcePrefab, true);
            dependencies["scriptDirect"] = AssetDatabase.GetDependencies(scriptPath, false);
            Check(dependencies["prefabRecursive"].Contains(AssetDatabase.GetAssetPath(icon)), "script icon appears in recursive prefab dependencies");
            Check(dependencies["prefabRecursive"].Contains(defaultMaterialPath), "script default material appears in recursive prefab dependencies");
            var originals = new[] { scriptPath, scriptPath + ".meta", AssetDatabase.GetAssetPath(icon), defaultMaterialPath, AssetDatabase.GetAssetPath(defaultTexture), work + "/Source.unity" }.ToDictionary(p => p, Hash);
            var report = AvatarBrancher.Create(root, work, "ScriptOnly");
            Check(report.status == "Complete" && AvatarBrancher.Verify(report.folder).status == "Complete", "copy with shared script icon and default references completes");
            Check(report.shared.Contains(scriptPath), "script remains shared");
            Check(report.assets.Length == 0, "script-only icon and default assets are not copied");
            Check(originals.All(p => Hash(p.Key) == p.Value), "shared script metadata and source assets stay unchanged");

            string packageMaterialPath = "Packages/com.ainoobmanpg.avatar-fork.regression-fixture/Shared.mat";
            var packageMaterial = AssetDatabase.LoadAssetAtPath<Material>(packageMaterialPath);
            Check(packageMaterial != null && packageMaterial.mainTexture == defaultTexture, "package fixture references a texture in Assets");
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = packageMaterial;
            var packageCopy = AvatarBrancher.Create(root, work, "PackageOnly");
            Check(packageCopy.status == "Complete" && packageCopy.assets.Length == 0 && AvatarBrancher.Verify(packageCopy.folder).status == "Complete", "package-only dependencies stay shared without copying their Assets texture");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(packageCopy.prefab).GetComponent<MeshRenderer>().sharedMaterial == packageMaterial, "package material remains shared");
            Object.DestroyImmediate(renderer);

            component.texture = icon;
            component.material = defaultMaterial;
            var direct = AvatarBrancher.Create(root, work, "DirectReferences");
            var copied = AssetDatabase.LoadAssetAtPath<GameObject>(direct.prefab).GetComponent<BranchSharedFixture>();
            string copiedIconPath = direct.assets.Single(a => a.source == AssetDatabase.GetAssetPath(icon)).destination;
            string copiedMaterialPath = direct.assets.Single(a => a.source == defaultMaterialPath).destination;
            string copiedTexturePath = direct.assets.Single(a => a.source == AssetDatabase.GetAssetPath(defaultTexture)).destination;
            Check(AssetDatabase.GetAssetPath(copied.texture) == copiedIconPath, "same script icon is copied when a component directly references it");
            Check(AssetDatabase.GetAssetPath(copied.material) == copiedMaterialPath && AssetDatabase.GetAssetPath(copied.material.mainTexture) == copiedTexturePath, "direct material and its nested texture are separated");
            Check(AvatarBrancher.Verify(direct.folder).status == "Complete", "shared indirect references coexist with independent direct assets");
            copied.texture = icon;
            EditorUtility.SetDirty(copied);
            PrefabUtility.SavePrefabAsset(copied.gameObject);
            var invalid = AvatarBrancher.Verify(direct.folder);
            Check(invalid.status == "Failed" && invalid.problems.Any(p => p.Contains(AssetDatabase.GetAssetPath(icon))), "direct original icon reference is still rejected");
            copied.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(copiedIconPath);
            PrefabUtility.SavePrefabAsset(copied.gameObject);
            copied.material.mainTexture = defaultTexture;
            EditorUtility.SetDirty(copied.material);
            AssetDatabase.SaveAssetIfDirty(copied.material);
            invalid = AvatarBrancher.Verify(direct.folder);
            Check(invalid.status == "Failed" && invalid.problems.Any(p => p.Contains(AssetDatabase.GetAssetPath(defaultTexture))), "nested original texture reference is still rejected");
            copied.material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(copiedTexturePath);
            EditorUtility.SetDirty(copied.material);
            AssetDatabase.SaveAssetIfDirty(copied.material);
            var renamed = AvatarBrancher.Rename(direct.folder, "RenamedDirect");
            Check(AvatarBrancher.Verify(renamed.folder).status == "Complete", "shared script dependencies remain valid after rename");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(renamed.prefab), scene);
            var second = AvatarBrancher.Create(instance, work, "SecondCopy");
            Check(second.status == "Complete" && AvatarBrancher.Verify(second.folder).status == "Complete", "copy of a copy retains shared dependencies and separated assets");
            Check(originals.All(p => Hash(p.Key) == p.Value), "all source and shared files stay unchanged after repeated operations");
            return new { success = true, checks = Passed.ToArray(), work, dependencies };
        }
        catch (Exception error)
        {
            return new { success = false, checks = Passed.ToArray(), work, dependencies, error = error.ToString() };
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
}
