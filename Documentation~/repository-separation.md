# Avatar Forkへの分離（2026-10-28）

Editor拡張のソース・回帰試験・仕様と検証要約を専用リポジトリへ分離した。移行時点の本体2ファイルは移動前と同一で、既存の公開APIのnamespace `VrcAvatarWorkspace.Branching` を互換性のため維持した。

Unityパッケージの定義とEditor専用のAssembly Definitionを追加した。制作プロジェクトは外部のcloneフォルダーを参照する。旧Assets版とパッケージ版を同時に配置しない。詳細ログ・画像・購入素材を含む個別資料はGitへ追加しない。

## 分離後の確認

- Unity 2022.3.22f1で `com.ainoobmanpg.avatar-fork` 0.1.0を外部フォルダーから読み込み、`AvatarFork.Editor` としてコンパイルされた。
- 購入素材を使わない回帰42項目が成功。試験用の型と生成物は除去した。
- 個別アバターはTransform 1,168・Component 1,881・MA 36・Missing Script 0を保持。原本Sceneと比較用材質のSHA-256は移行前後で一致した。
- 移行工程ではSDKビルド・アップロードを実行していない。後続の改名機能・SDKビルドと未確認範囲は[検証履歴](validation-history.md)を参照する。制作プロジェクト自体は配布しない。

ローカルパッケージとAssembly DefinitionはUnity公式の[ローカルパス](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-localpath.html)と[パッケージのAssembly Definition](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-asmdef.html)に従う。
