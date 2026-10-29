using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace VrcAvatarWorkspace.Branching
{
    internal static class AvatarForkText
    {
        internal const string PreferenceKey = "com.ainoobmanpg.avatar-fork.uiLanguage";
        internal static bool Japanese => EditorPrefs.GetString(PreferenceKey, "en") == "ja";
        internal static string Choose(string english, string japanese) { return Japanese ? japanese : english; }
        internal static string Format(string text, params object[] values) { return string.Format(CultureInfo.CurrentCulture, text, values); }

        internal static void LanguageSelector()
        {
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Language / 言語", Japanese ? 1 : 0, new[] { "English", "日本語" });
            if (!EditorGUI.EndChangeCheck()) return;
            EditorPrefs.SetString(PreferenceKey, selected == 1 ? "ja" : "en");
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window is AvatarBranchWindow) window.titleContent = new GUIContent(CopyTitle);
                else if (window is AvatarBranchRenameWindow) window.titleContent = new GUIContent(RenameTitle);
                else if (window is SharedWindow) window.titleContent = new GUIContent(SharedTitle);
                else continue;
                window.Repaint();
            }
        }

        internal static string Busy => Choose("Wait until Play Mode and compilation have finished.", "Play Modeとコンパイルが終わってから実行してください。");
        internal static string SelectHierarchy => Choose("Select an avatar in the Hierarchy.", "Hierarchyにあるアバターを選択してください。");
        internal static string UnnamedScene => Choose("An unnamed Scene has unsaved changes. Name and save it first. Unsaved changes in named Scenes can be copied.", "名前の付いていない未保存Sceneがあります。先にSceneへ名前を付けて保存してください。名前のあるSceneの未保存変更は、そのまま複製できます。");
        internal static string OutputExists => Choose("The destination already exists.", "出力先が既に存在します。");
        internal static string CopyTitle => Choose("Duplicate Avatar", "アバターを複製");
        internal static string ReadAvatar => Choose("Reading the current avatar", "現在の改変状態を読み取っています");
        internal static string SavePrefabFailed => Choose("Could not save the current avatar as a Prefab.", "現在のアバターをPrefabへ保存できませんでした。");
        internal static string BinaryPrefab => Choose("Binary Prefabs are not supported. Set Asset Serialization to Force Text first.", "バイナリ形式のPrefabには対応していません。Asset SerializationをForce Textにしてから実行してください。");
        internal static string CheckReferences => Choose("Checking references in the copied assets", "保存した素材の参照を検査しています");
        internal static string MissingComponent => Choose("Components are missing after copying.", "複製後にコンポーネントの欠落が発生しました。");
        internal static string MissingScript => Choose("The source avatar has a Missing Script. Check it before copying.", "元のアバターにMissing Scriptがあります。複製前に確認してください。");
        internal static string BlueprintUnavailable => Choose("Could not inspect the Pipeline Manager Blueprint ID. Copying has stopped.", "Pipeline ManagerのBlueprint IDを確認できません。複製を停止します。");
        internal static string NestedCopy => Choose("A copy cannot be created inside another copy. Choose a parent folder outside existing copies.", "既存のコピーの中には作れません。コピーの外の保存先を選んでください。");
        internal static string FolderReference => Choose("References to folders cannot be copied: ", "フォルダー自体への参照は複製できません: ");
        internal static string AssetMissing => Choose("Asset not found", "素材が見つかりません");
        internal static string AssetIdentity => Choose("Could not read the asset identity: ", "素材の識別情報を取得できません: ");
        internal static string BinaryAsset => Choose("Binary Unity assets are not supported: ", "バイナリ形式のUnity素材には対応していません: ");
        internal static string DirtyImporter => Choose("Asset import settings have unsaved changes. Apply them first: ", "素材の読込設定に未保存変更があります。先にApplyしてください: ");
        internal static string DuplicateIdentity => Choose("Asset name identifiers collided. Create a new destination.", "素材名の識別子が重複しました。保存先を作り直してください。");
        internal static string CopyAsset => Choose("Copying asset: ", "素材をコピー: ");
        internal static string DirtyImportedAsset => Choose("An imported asset has unsaved changes. Save it first: ", "未保存の変更を含む読込素材があります。先に保存してください: ");
        internal static string OverlayFailed => Choose("Could not transfer unsaved asset changes to the copy: ", "未保存の素材を複製へ転記できません: ");
        internal static string ModelStructure => Choose("The model structure changed during import: ", "モデルの構造が読み込み時に変わりました: ");
        internal static string ModelMaterialCount => Choose("The model material count changed during import: ", "モデルのマテリアル数が読み込み時に変わりました: ");
        internal static string ModelMaterialIdentity => Choose("Duplicate model material identifiers: ", "モデルのマテリアル識別子が重複しています: ");
        internal static string ExtractMaterial => Choose("Could not extract a model material: ", "モデルのマテリアルを分離できません: ");
        internal static string LoadMaterial => Choose("Could not load a copied model material: ", "モデルの複製マテリアルを読み込めません: ");
        internal static string ReferenceMissing => Choose("Reference not found: ", "参照先が見つかりません: ");
        internal static string OriginalReference => Choose("A reference to an original asset remains: ", "元の素材への参照が残っています: ");
        internal static string OriginalPath => Choose("An original asset path remains: ", "元の素材パスが残っています: ");
        internal static string OriginalDependency => Choose("Unity dependencies still include an original asset: ", "Unityの依存関係に元の素材が残っています: ");
        internal static string ExternalModelFile => Choose("An external model file is missing from the copy: ", "モデルの外部ファイルが複製内にありません: ");
        internal static string OriginalParentMaterial => Choose("A parent material still references the original: ", "親マテリアルが元を参照しています: ");
        internal static string BlueprintRemains => Choose("An upload Blueprint ID remains: ", "アップロード先のBlueprint IDが残っています: ");
        internal static string OriginalStringPath => Choose("A setting still contains an original asset path: ", "設定の文字列に元の素材パスが残っています: ");
        internal static string SceneInspection => Choose("This Unity version cannot inspect the saved Scene Blueprint ID in the background. Copying has stopped.", "このUnityでは保存SceneのBlueprint IDを非表示で検査できません。複製を停止します。");
        internal static string SceneBlueprintRemains => Choose("An upload Blueprint ID remains in the Scene: ", "Sceneのアップロード先のBlueprint IDが残っています: ");
        internal static string ReportMismatch => Choose("The copy report does not match this folder.", "複製の記録がこのフォルダーに一致しません。");
        internal static string OutputMissing => Choose("The copied Scene or Prefab is missing.", "複製したSceneまたはPrefabがありません。");
        internal static string SaveCopyScene => Choose("Could not save the copied Scene.", "複製Sceneを保存できませんでした。");
        internal static string SourceChanged => Choose("An original asset changed during processing: ", "処理中に元の素材が変更されました: ");
        internal static string IncludeMissing => Choose("Shader include not found: ", "Shaderのincludeが見つかりません: ");
        internal static string OutsideInclude => Choose("Shader includes outside the Project are not supported: ", "Project外のShader includeには対応していません: ");
        internal static string IncludeUnresolved => Choose("Could not resolve a Shader include: ", "Shaderのincludeを解決できません: ");
        internal static string InvalidName => Choose("This name contains characters or a pattern that cannot be used.", "名前に使えない文字や形式が含まれています。");
        internal static string LinkedDestination => Choose("Destinations containing symbolic links or junctions are not supported: ", "シンボリックリンクやジャンクションを含む保存先には対応していません: ");
        internal static string IgnoredDestination => Choose("The destination contains names Unity ignores or relative traversal.", "Unityが読み込まない名前や相対移動を含む保存先は使えません。");
        internal static string OutsideAssets => Choose("Choose a destination inside Assets.", "保存先はAssets内を指定してください。");
        internal static string CorruptRecovery => Choose("An interrupted operation record cannot be read. Keep Library and the recovery data: ", "判別できない中断記録があります。Libraryを削除せず、復元用データを保全してください: ");
        internal static string InterruptedRename => Choose("An interrupted rename needs recovery. Keep Library and follow the recovery instructions: ", "途中で止まった改名の記録があります。Libraryを削除せず、復元手順を確認してください: ");
        internal static string LinkedCopy => Choose("The copy contains a symbolic link or junction. Renaming has stopped to protect linked files: ", "複製内にシンボリックリンクやジャンクションがあります。リンク先のファイルを変更しないため、改名を停止します: ");
        internal static string SelectCopy => Choose("Select a copy folder created by Avatar Fork.", "Avatar Forkで作った複製のフォルダーを選んでください。");
        internal static string NamingRecord => Choose("A completed copy with naming records is required. For older copies, create a new copy first.", "完了した複製の命名記録が必要です。旧形式の場合は、新しいコピーを作ってください。");
        internal static string ContainsCopy => Choose("The target contains another copy. Separate them into different folders first.", "対象内に別のコピーがあります。別々のフォルダーへ分けてから実行してください。");
        internal static string RenameNested => Choose("A folder inside another copy cannot be renamed.", "別のコピーの中にあるフォルダーは改名できません。");
        internal static string RenameCollision => Choose("A destination with this name already exists. Choose another name.", "同じ名前の保存先があります。別の名前を指定してください。");
        internal static string DuplicateRecord => Choose("The copy report contains duplicate entries.", "複製の記録に重複があります。");
        internal static string RecordedAssetMissing => Choose("A recorded asset is missing. Check whether it was moved or deleted: ", "記録にある素材が見つかりません。移動や削除を確認してください: ");
        internal static string RenameFileCollision => Choose("File names would collide. Check the names of assets added after copying.", "改名先でファイル名が重複します。後から追加した素材の名前を確認してください。");
        internal static string AssetGuidMismatch => Choose("The asset GUID differs from the report: ", "素材のGUIDが記録と異なります: ");
        internal static string MaterialGuidMismatch => Choose("The material GUID differs from the report: ", "材質のGUIDが記録と異なります: ");
        internal static string ClosePrefabMode => Choose("Close Prefab Mode first.", "Prefab Modeを閉じてから実行してください。");
        internal static string RenameUnnamedScene => Choose("An unnamed Scene is open. Name and save it before renaming.", "名前の付いていないSceneがあります。先にSceneへ名前を付けて保存してから改名してください。");
        internal static string DirtyScene => Choose("A Scene using these assets has unsaved changes. Decide whether to save it before continuing.", "対象の素材を使うSceneに未保存変更があります。保存するかどうかを決めてから実行してください。");
        internal static string DirtyAsset => Choose("A target asset has unsaved changes. Decide whether to save it before continuing: ", "対象の素材に未保存変更があります。保存するかどうかを決めてから実行してください: ");
        internal static string CloseSceneFailed => Choose("Could not close the target Scene. No changes were made.", "対象Sceneを閉じられませんでした。変更は行いません。");
        internal static string RenameTitle => Choose("Rename Copy", "コピーの名前を変更");
        internal static string RenameProgress => Choose("Updating asset names and references", "素材の名前と参照を更新しています");
        internal static string GuidChanged => Choose("A GUID changed: ", "GUIDが変わりました: ");
        internal static string RestoreFilesMismatch => Choose("The restored file list does not match the backup.", "復元したファイルの一覧がバックアップと一致しません。");
        internal static string RestoreContentMismatch => Choose("A restored file does not match the backup: ", "復元したファイルがバックアップと一致しません: ");
        internal static string RestoreFailed => Choose("Renaming and automatic recovery failed. Recovery data: ", "名前の変更と自動復元に失敗しました。復元用データ: ");
        internal static string RenameRestored => Choose("Renaming failed and the original was restored. ", "名前の変更に失敗したため元へ戻しました。");
        internal static string ReopenFailed => Choose("Renaming completed, but the Scene could not be reopened: ", "改名は完了しました。Sceneを開き直せませんでした: ");
        internal static string ShaderMismatch => Choose("A Shader name does not match the report: ", "記録に一致しないShader名があります: ");
        internal static string AmbiguousAvatar => Choose("Could not identify a unique avatar in the saved Scene.", "保存Sceneのアバターを一意に確認できません。");
        internal static string SaveRenamedScene => Choose("Could not save the modified Scene.", "変更したSceneを保存できません。");
        internal static string OutsideSceneReference => Choose("{0} / {1} references a Scene object outside the avatar.", "{0} の {1} がアバター外のSceneオブジェクトを参照しています。");
        internal static string UnsavedReference => Choose("{0} / {1} references an unsaved asset. Save the asset before copying.", "{0} の {1} は未保存の素材です。素材を保存してから複製してください。");
        internal static string CopyMenu => Choose("Duplicate Avatar…", "アバターを複製…");
        internal static string RenameMenu => Choose("Rename Copy…", "コピーの名前を変更…");
        internal static string Source => Choose("Source avatar", "複製元");
        internal static string Candidates => Choose("Avatar", "複製するアバター");
        internal static string Deleted => Choose("Missing", "削除済み");
        internal static string CopyName => Choose("Copy name", "複製名");
        internal static string SaveIn => Choose("Save in", "保存する場所");
        internal static string Change => Choose("Change…", "変更…");
        internal static string UseAssets => Choose("Use Assets", "Assetsに戻す");
        internal static string ParentHelp => Choose("A folder named after the copy will be created here.", "この中に、複製名のフォルダーを作ります。");
        internal static string ChooseParent => Choose("Choose a parent folder inside Assets", "Assets内の親フォルダーを選択");
        internal static string Destination => Choose("Destination", "作成先");
        internal static string Outputs => Choose("Scene, Prefab, and referenced assets", "Scene・Prefab・参照する素材一式");
        internal static string OriginalNote => Choose("Source Scene and asset files are left unchanged.", "元のSceneや素材のファイルは保存し直しません。");
        internal static string BlueprintNote => Choose("The copy’s Blueprint ID is cleared for a new upload.", "複製先のBlueprint IDを空にし、新規アップロード用にします。");
        internal static string SharedNote => Choose("Unity built-ins, packages (SDK / MA / shaders), scripts and DLLs stay shared.", "Unity標準素材・Packages内のSDK/MA/Shader・スクリプト/DLLは共有します。");
        internal static string Details => Choose("What gets copied", "複製するもの");
        internal static string DetailsAssets => Choose("Materials and their parents, textures, meshes, animations, settings and custom shaders are copied as needed.", "マテリアルと親マテリアル、画像、メッシュ、アニメーション、設定、独自Shaderなどを必要に応じて複製します。");
        internal static string DetailsNames => Choose("Bone names, child object names and animation control names are preserved.", "骨・子オブジェクト・アニメーションの制御名は維持します。");
        internal static string DetailsCheck => Choose("The copy is checked for remaining references to original assets.", "元の素材への参照が残っていないか検査します。");
        internal static string Create => Choose("Create Copy", "複製する");
        internal static string Complete => Choose("Copy and reference check completed. Assets copied: {0}", "複製と参照の検査が完了しました。コピーした素材: {0}ファイル");
        internal static string ResultBlueprint => Choose("Blueprint ID is empty.", "Blueprint IDは空です。");
        internal static string OpenScene => Choose("Open Scene", "Sceneを開く");
        internal static string ShowProject => Choose("Show in Project", "Projectで表示");
        internal static string SharedItems => Choose("Shared items…", "共有するもの…");
        internal static string SharedTitle => Choose("Shared Items", "共有するもの");
        internal static string SharedHelp => Choose("Updating these programs or packages can affect avatars in the same Unity Project.", "この一覧のプログラムやパッケージ自体を更新した場合は、同じUnity Project内のアバターに影響します。");
        internal static string EmptyName => Choose("Enter a copy name.", "複製名を入力してください。");
        internal static string SourceMissing => Choose("The source avatar is missing. Select an avatar in the Hierarchy.", "複製元が見つかりません。Hierarchyでアバターを選んでください。");
        internal static string Collision => Choose("This name is already in use. The copy will be named “{0}”.", "同名があるため「{0}」として作成します。");
        internal static string DoubleFolder => Choose("Another folder named “{0}” will be created inside the selected folder.", "選んだフォルダーの中に、さらに「{0}」を作ります。");
        internal static string RejectedParent => Choose("This folder cannot be used. The location stays {0}.", "この場所は使えません。保存する場所は {0} のままです。");
        internal static string CopyFailed => Choose("The copy was not completed.", "複製は完了していません。");
        internal static string MissingReferences => Choose("{0} source references were already missing. They were cleared in the copy to prevent reconnection. Details are in BranchReport.json.", "元から参照先が存在しないデータが {0}件ありました。コピー側では参照を空にして、後から元へ再接続されないようにしています。詳細はBranchReport.jsonに記録しました。");
        internal static string RenameHeading => Choose("Rename the files belonging to this copy", "このコピーが持つ一式の名前を変更します");
        internal static string Target => Choose("Target", "対象");
        internal static string NewName => Choose("New name", "新しい名前");
        internal static string RenameNote => Choose("The root, Scene, Prefab, folder and generated asset names are updated together. Bone, child object and animation control names, and the upload identity are preserved.", "ルート・Scene・Prefab・保存先・素材名を揃えます。骨・子オブジェクト・アニメーションの制御名と、登録済みのアップロード先は維持します。");
        internal static string RenameCustomNote => Choose("Custom asset names, display names and later additions are preserved. Asset paths stored as strings outside this copy are not updated.", "手で変更した素材名や表示名、後から追加した素材の名前は維持します。コピーの外に文字列で保存した素材パスは、自動更新の対象外です。");
        internal static string PreviewRename => Choose("Preview Changes", "変更内容を確認");
        internal static string ResolvedName => Choose("Resulting name", "変更後の名前");
        internal static string RenamedDestination => Choose("Resulting destination", "変更後の保存先");
        internal static string ChangedFiles => Choose("Files to move or rename", "移動・改名するファイル");
        internal static string AdditionalFiles => Choose("Unrecorded files (names preserved)", "記録外のファイル（名前を維持）");
        internal static string FileChanges => Choose("Changes by file", "ファイルごとの変更");
        internal static string RenameUnsavedNote => Choose("Renaming stops if there are unsaved changes. Saved target Scenes are closed during processing and reopened afterwards.", "未保存変更がある場合は停止します。保存済みの対象Sceneは、処理中に閉じて変更後に開き直します。");
        internal static string ApplyRename => Choose("Rename Copy", "この内容で名前を変更する");
        internal static string RenameComplete => Choose("Renaming and reference checks completed.", "名前の変更と参照の検査が完了しました。");
    }
}
