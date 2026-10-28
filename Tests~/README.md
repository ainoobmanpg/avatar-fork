# 回帰試験の実行

購入素材を使わない合成データの試験です。制作中のプロジェクトではなく、Windows・Unity 2022.3.22f1の検証専用Avatarsプロジェクトで実行します。VRChat SDK AvatarsとBase、Unity Test Frameworkが必要です。確認したSDKは3.10.5、Test Frameworkは1.1.29です。Avatar Fork本体を先に導入してください。

## 通常のUnityで実行する

1. 対象のUnityを閉じ、`./Tests~/PrepareRegression.ps1 -Project "検証専用Unityプロジェクトのパス"`を実行します。`Assets/AvatarForkRegression`へ自作の試験コードだけを配置します。既存の同名フォルダーには上書きしません。
2. Unityを開き、コンパイルが終わったらSceneを保存します。`Tools > Avatar Fork > Run regression tests (test project only)`を実行します。
3. Consoleの完了表示と`Library/AvatarFork/Regression/result.json`の各`success:true`を確認します。失敗した場合はConsoleの例外も確認します。
4. 試験は`Assets/99_検証用/AvatarBrancher_Regression_*`、`AvatarFork_Rename_*`、`AvatarFork_RenameSafety_*`、`AvatarFork_Release_*`に合成素材を作ります。結果の`work`が今回のフォルダーです。検証プロジェクトごと保全し、制作プロジェクトへ混ぜないでください。

バッチ実行の場合は同じ準備の後、Unity Editorへ`-batchmode -projectPath "検証専用Unityプロジェクトのパス" -executeMethod AvatarForkRegressionRunner.RunAll -quit -logFile "ログのパス"`を渡します。失敗時は終了コード1です。既存のUnityが同じプロジェクトを開いていないことを確認してください。

複製42条件、一括改名24条件、Scene保護4条件に加え、`ReleaseRegression.cs`で禁止名・入れ子・中断記録・バイナリ素材の停止などを確認します。外部フォルダーを参照するUPM導入時は、Packagesパスの解決も確認します。Unity MCPがなくても実行できます。

## Unity MCP等で個別に実行する

`Regression.cs` はEditor内で実行する `EditorCommand.Execute()` の本体。通常インストールへ含めない。

1. `BranchFixtureData.cs` を検証用Projectの通常のAssetsフォルダーへ一時配置する。Editorフォルダーには置かない。
2. コンパイル完了後、`Regression.cs` をUnity MCPのEditorコマンド等で実行する。必要ならEditorプロセスの環境変数 `AVATAR_FORK_TEST_REPORT` にJSON出力先を設定する。未設定でも結果は戻り値で得られる。
3. 成功時は42項目の結果を確認する。作られた `Assets/99_検証用/AvatarBrancher_Regression_*` のうち、戻り値のworkに示された今回のフォルダーと、一時配置した型だけを除去する。元Sceneを保存し直さない。

購入アバターを使う個別試験のコード・素材・生ログはGit管理外へ保全し、合成試験と配布物には含めない。

購入素材・生成済みアバター・個人の検証ログをGitへ追加しない。代表ギミック・SDKの既存検証と、VRChat内の全操作確認は別工程である。

`RenameRegression.cs` も同じ型を使い、Unity内のEditorコマンドとして実行する。日本語・空白・大文字小文字だけの改名、手で付けた名前、後追加ファイル、GUID・参照・再分岐・登録済みBlueprint IDの維持と、未保存時の停止、失敗後の復元を確認する。`AVATAR_FORK_RENAME_REPORT` を指定するとJSONを保存する。試験後は戻り値のworkに示された今回の `Assets/99_検証用/AvatarFork_Rename_*` と一時配置した型を除去する。

`RenameSafetyRegression.cs` は追加の合成試験。Sceneで手動変更したルート名、複数Sceneの並び順、閉じたSceneの見出しを改名後も維持することと、名前のないSceneがある場合に変更せず停止する4条件を確認する。`AVATAR_FORK_SAFETY_REPORT` を指定するとJSONを保存する。戻り値のsuccessがtrueであることを確認し、workに示された今回の `Assets/99_検証用/AvatarFork_RenameSafety_*` だけを除去する。

## 配布物の確認

Python 3で`python scripts~/build-release.py --output 配布先の空フォルダー`を実行し、`python Tests~/CheckRelease.py --dir 同じフォルダー`で内容・GUID・ライセンス・ハッシュを照合する。ZIPはVPM用、unitypackageはUnityへの直接導入用。生成した`index.json`をVPMリポジトリに反映し、対応するGitHub Releaseのファイルと同時に公開する。過去に公開した版の配布ファイルと一覧を置き換えたり削除したりしない。

CIではこの梱包検査を行う。Unity内の回帰試験やアップロードをCIで実行したことにはならない。
