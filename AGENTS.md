# Avatar Fork

Unity Editor拡張の専用リポジトリ。制作中のUnityプロジェクト、購入素材、認証情報をGitへ追加しない。

- コードに説明コメントを追加しない。動作・制約の説明はREADMEとDocumentation~/へ置く。

- 本体はEditor/、手動実行のUnity内検証はTests~/、仕様と検証の要約はDocumentation~/。詳細証跡はGit除外のreports~/、個別相談資料はGit除外のprivate~/。
- 通常の操作はHierarchyの右クリック。複製元の未保存状態とファイルを保ち、素材参照を分離し、複製先のBlueprint IDをクリアする。SDK・MA・コード・組込み素材の共有範囲はREADMEに明記する。
- 既存コピーのBranchReport.jsonを保持し、再分岐の元名・識別子を維持する。子オブジェクト・骨・Animatorの制御名を一括変更しない。
- 公開APIのnamespace VrcAvatarWorkspace.Branchingは互換性のため維持している。旧Workspaceへの実装依存はない。
- Editorでの変更前に対象プロジェクト・Scene・アバター・未保存状態を確認する。アップロードは別途明示承認が必要。
- 購入素材を使わない回帰試験と、購入素材を使う個別アバター検証を区別する。後者の素材・生データはプッシュしない。
- NAS作業日誌を使い、標準メモリ・PersonalKnowledgeは使わない。現在の会話と実機・Repo文書を根拠にする。
