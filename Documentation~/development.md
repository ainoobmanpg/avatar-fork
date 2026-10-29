# 開発者向けの導入・検証と関連文書

通常の導入と操作は[README](../README.md)を参照してください。この文書は、ソースから導入する場合と、実装・検証の記録を調べる場合の案内です。

## 外部フォルダーから導入する

1. 制作プロジェクト全体をバックアップし、対象のUnityを閉じます。
2. Unityを開き直し、Package Managerの「Add package from disk…」で、cloneしたフォルダーの`package.json`を指定します。

Windowsでは、Unityを閉じた状態で[導入スクリプト](https://github.com/ainoobmanpg/avatar-fork/blob/main/scripts~/install.ps1)を使うこともできます。PowerShell 7で確認しています。

```powershell
./scripts~/install.ps1 -Project "Unityプロジェクトのパス"
```

変更前の`Packages/manifest.json`は`Library/AvatarFork/Install/`へ保存し、実行結果の`backup`に場所を表示します。Unityが参照している間は、外部フォルダーを移動・削除しないでください。旧`Assets/Editor/AvatarBranching`やunitypackage版とは併用しないでください。

VRCアバターを扱う場合は、VRChat Creator CompanionでAvatarsプロジェクトを用意してください。SDKや購入素材をこのツールが導入することはありません。

## 外部フォルダー参照を更新する

1. プロジェクトをバックアップし、使用中の版を`package.json`で記録します。Unityを閉じます。
2. cloneした場合は、確認したリリースのtagへ切り替えます。ZIPの場合は新しい版を別のフォルダーに展開し、古い版を残します。
3. 同じclone先を使う場合はUnityを開き直します。ZIPの展開先を変えた場合は、Package Managerで新しい`package.json`を選び直します。導入スクリプトで登録先を変更しても構いません。
4. Consoleにコンパイルエラーがなく、Hierarchyの右クリックから操作を開けることを確認します。まず試験用のコピーで複製・名前変更と見た目を確認します。

以前の版へ戻す場合も、Unityを閉じ、保全した以前のパッケージを参照し直します。生成済みのSceneや素材は更新前へ自動復元されません。生成データを戻す必要があれば、プロジェクトのバックアップを使ってください。

## 外部フォルダー参照を削除する

UnityのPackage ManagerでAvatar Forkを選び、「Remove」を押します。削除後はHierarchyの操作が消え、Consoleにエラーがないことを確認してください。外部パッケージフォルダーは、他のプロジェクトが参照していないことを確認してから取り除いてください。

生成済みのScene・Prefab・素材と`BranchReport.json`は制作プロジェクト内に残します。生成データや復元用の`Library/AvatarFork`を削除する操作とは区別してください。SDK・MA・Shaderなどの共有プログラムは引き続き必要です。

導入スクリプトでmanifestを戻す場合はUnityを閉じ、現在のmanifestを別途保全してから、表示された`backup`を`Packages/manifest.json`へ戻します。バックアップ後に追加した別パッケージの登録も戻るため、Avatar Forkだけを削除する場合はPackage Managerを使ってください。

## 検証APIと回帰試験

公開APIのnamespaceは、互換性のため`VrcAvatarWorkspace.Branching`を維持しています。旧Workspaceへの実装依存はありません。

`AvatarBrancher.Verify(folder)`は、複製直後の未登録コピーを検査するAPIです。Blueprint IDが空かどうかも検査するため、アップロード後にIDが設定されたコピーは、その理由でFailedになります。登録後のIDを消してこの検査へ合わせないでください。報告JSON内の元パスは出典の記録であり、アバターが使う参照には含みません。

[回帰試験の実行方法](https://github.com/ainoobmanpg/avatar-fork/blob/main/Tests~/README.md)に前提条件と手順があります。`Tests~/Regression.cs`はUnity内で実行する検証用C#、`Tests~/BranchFixtureData.cs`は検証時だけ通常のAssetsフォルダーへ導入する型です。後者はEditorフォルダーへ置かないでください。これらは通常のインストールには含めません。

購入素材を使わない回帰試験と、購入アバターを使う個別試験を区別します。後者の素材・コード・生データは配布しません。詳細な画像・ログはローカルのGit管理外フォルダーに保持します。

## 検証の記録と未確認の範囲

日付ごとの結果は[実装・検証の記録](validation-history.md)にあります。命名・再分岐・参照の独立性・Blueprint IDを含む回帰試験も、この文書から確認できます。

検証コピー1体のWindows向けPrivateアップロードと、VRChatのDesktopモードでの装着・代表的な表示や姿勢の変化は確認済みです。全メニューを直接操作した結果ではありません。VR機器・掴み・着火・接触・発声・他クライアントからの同期は未確認です。

## 関連文書

- [用語](../GLOSSARY.md)
- [MITライセンスとクレジットの範囲](adr/0001-license-and-attribution.md)
- [専用リポジトリへ分離した経緯](repository-separation.md)
- [更新履歴](../CHANGELOG.md)
