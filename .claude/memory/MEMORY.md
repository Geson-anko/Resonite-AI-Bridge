# Memory Index

resonite-io プロジェクトの規約・知見・ユーザーの好みを記録するインデックス。詳細は各ファイルを参照。

## Feedback

- [dotnet local tools を優先する](feedback_dotnet_local_tools.md) — .NET CLI ツールは `.config/dotnet-tools.json` で管理し、global tool + PATH 操作は避ける。

## サブエージェント由来のメモ

`.claude/agent-memory/<agent-type>/` に各サブエージェントが auto memory 機能で書き出した
作業メモが格納されている。harness が自動ロードする領域だが、本リポジトリでは git 管理する方針。

- [code-quality-reviewer/MEMORY.md](../agent-memory/code-quality-reviewer/MEMORY.md) — レビュー時に拾った reference / project メモのインデックス
