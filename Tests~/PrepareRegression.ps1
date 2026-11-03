param([Parameter(Mandatory=$true)][string]$Project)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'))) { throw '検証専用のUnityプロジェクトを指定してください。' }
if (Test-Path -LiteralPath (Join-Path $projectRoot 'Temp/UnityLockfile')) { throw '対象のUnityを閉じてから準備してください。' }
$target = Join-Path $projectRoot 'Assets/AvatarForkRegression'
if (Test-Path -LiteralPath $target) { throw '検証コードの配置先が既にあります。上書きしません。' }
$encoding = [System.Text.UTF8Encoding]::new($false, $true)
$null = New-Item -ItemType Directory -Path (Join-Path $target 'Editor')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BranchFixtureData.cs') -Destination $target
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BranchSharedFixture.cs') -Destination $target
$defaults = Join-Path $target 'SharedDefaults'
$null = New-Item -ItemType Directory -Path $defaults
$iconGuid = 'a3b0c41cdf214d63b9012f5b991137a1'
$textureGuid = 'a3b0c41cdf214d63b9012f5b991137a2'
$materialGuid = 'a3b0c41cdf214d63b9012f5b991137a3'
$png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVQIHWNkYPgPBAwMLP8ZGIGQgQEAPhQFAzqQ9h0AAAAASUVORK5CYII=')
foreach ($entry in @(@('Icon.png',$iconGuid),@('Texture.png',$textureGuid))) {
    [IO.File]::WriteAllBytes((Join-Path $defaults $entry[0]), $png)
    [IO.File]::WriteAllText((Join-Path $defaults ($entry[0] + '.meta')), "fileFormatVersion: 2`nguid: $($entry[1])`nTextureImporter:`n  externalObjects: {}`n  serializedVersion: 12`n", $encoding)
}
$material = @"
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_Name: SharedDefaultMaterial
  m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}
  m_Parent: {fileID: 0}
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _MainTex:
        m_Texture: {fileID: 2800000, guid: $textureGuid, type: 3}
        m_Scale: {x: 1, y: 1}
        m_Offset: {x: 0, y: 0}
    m_Ints: []
    m_Floats: []
    m_Colors: []
"@
[IO.File]::WriteAllText((Join-Path $defaults 'Material.mat'), $material, $encoding)
[IO.File]::WriteAllText((Join-Path $defaults 'Material.mat.meta'), "fileFormatVersion: 2`nguid: $materialGuid`nNativeFormatImporter:`n  externalObjects: {}`n  mainObjectFileID: 2100000`n", $encoding)
$scriptMetadata = @"
fileFormatVersion: 2
guid: a3b0c41cdf214d63b9012f5b991137a4
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences:
  - texture: {fileID: 2800000, guid: $textureGuid, type: 3}
  - material: {fileID: 2100000, guid: $materialGuid, type: 2}
  executionOrder: 0
  icon: {fileID: 2800000, guid: $iconGuid, type: 3}
"@
[IO.File]::WriteAllText((Join-Path $target 'BranchSharedFixture.cs.meta'), $scriptMetadata, $encoding)
$fixturePackage = Join-Path $projectRoot 'Packages/com.ainoobmanpg.avatar-fork.regression-fixture'
if (Test-Path -LiteralPath $fixturePackage) { throw '検証用パッケージの配置先が既にあります。上書きしません。' }
$null = New-Item -ItemType Directory -Path $fixturePackage
[IO.File]::WriteAllText((Join-Path $fixturePackage 'package.json'), '{"name":"com.ainoobmanpg.avatar-fork.regression-fixture","version":"1.0.0","displayName":"Avatar Fork regression fixture"}', $encoding)
[IO.File]::WriteAllText((Join-Path $fixturePackage 'Shared.mat'), $material, $encoding)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PlacementPlayModeRegression.cs') -Destination (Join-Path $target 'Editor')
foreach ($suite in @('Regression','RenameRegression','RenameSafetyRegression','ReleaseRegression','LocalizationRegression','UiReviewRegression','HierarchyRegression','PlacementIdentityRegression','PlacementSafetyRegression','StableReleaseRegression','SharedDependencyRegression')) {
    $source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot ($suite + '.cs')), $encoding)
    $source = $source.Replace('class EditorCommand', ('public class AvatarForkTest' + $suite)).Replace('static object Execute()', 'public static object Execute()')
    [System.IO.File]::WriteAllText((Join-Path $target ('Editor/' + $suite + '.cs')), $source, $encoding)
}
$runner = @'
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;
public static class AvatarForkRegressionRunner
{
    [MenuItem("Tools/Avatar Fork Tests/Run regression tests (test project only)")]
    public static void RunAll()
    {
        try
        {
            if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.isDirty))
                throw new InvalidOperationException("Save the test project's scenes before running regression tests.");
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/AvatarForkRegression/Empty.unity");
            const string languageKey = "com.ainoobmanpg.avatar-fork.uiLanguage";
            bool hadLanguage = EditorPrefs.HasKey(languageKey);
            string originalLanguage = EditorPrefs.GetString(languageKey);
            var result = new JObject();
            try
            {
                foreach (string language in new[] { "en", "ja" })
                {
                    EditorPrefs.SetString(languageKey, language);
                    var suites = new JObject();
                    result[language] = suites;
                    suites["copy"] = JToken.FromObject(AvatarForkTestRegression.Execute());
                    suites["rename"] = JToken.FromObject(AvatarForkTestRenameRegression.Execute());
                    suites["safety"] = JToken.FromObject(AvatarForkTestRenameSafetyRegression.Execute());
                    suites["release"] = JToken.FromObject(AvatarForkTestReleaseRegression.Execute());
                    suites["localization"] = JToken.FromObject(AvatarForkTestLocalizationRegression.Execute());
                    suites["uiReview"] = JToken.FromObject(AvatarForkTestUiReviewRegression.Execute());
                    suites["hierarchy"] = JToken.FromObject(AvatarForkTestHierarchyRegression.Execute());
                    suites["placementIdentity"] = JToken.FromObject(AvatarForkTestPlacementIdentityRegression.Execute());
                    suites["placementSafety"] = JToken.FromObject(AvatarForkTestPlacementSafetyRegression.Execute());
                    suites["stable"] = JToken.FromObject(AvatarForkTestStableReleaseRegression.Execute());
                    suites["sharedDependencies"] = JToken.FromObject(AvatarForkTestSharedDependencyRegression.Execute());
                }
            }
            finally
            {
                if (hadLanguage) EditorPrefs.SetString(languageKey, originalLanguage); else EditorPrefs.DeleteKey(languageKey);
            }
            Directory.CreateDirectory("Library/AvatarFork/Regression");
            File.WriteAllText("Library/AvatarFork/Regression/result.json", result.ToString());
            if (result.Properties().Any(locale => ((JObject)locale.Value).Properties().Any(suite => !(bool)suite.Value["success"]))) throw new Exception("A regression suite failed.");
            Debug.Log("Avatar Fork regression tests passed. Library/AvatarFork/Regression/result.json");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw;
        }
    }
}
'@
[System.IO.File]::WriteAllText((Join-Path $target 'Editor/AvatarForkRegressionRunner.cs'), $runner, $encoding)
[pscustomobject]@{ project=$projectRoot; fixtures=$target; executeMethod='AvatarForkRegressionRunner.RunAll' } | ConvertTo-Json
