#!/usr/bin/env python3
"""Container-side client for the host debug bridge.

container 内 shell から ``just resonite-{start,stop,status}`` 経由で呼ばれ、
host 常駐の ``scripts/host_agent.py`` に UDS で 1 リクエスト送って 1
レスポンスを受け取って表示する薄い CLI。
"""

from __future__ import annotations

import argparse
import json
import os
import socket
import string
import sys
from pathlib import Path
from typing import Any

DEFAULT_SOCKET_REL = "resonite-io-debug/host-agent.sock"
CONNECT_TIMEOUT_SEC = 5.0
READ_TIMEOUT_SEC = 30.0  # stop は最大 3 秒待ち + α
MAX_RESPONSE_BYTES = 65536

# プロファイル名に許す文字 (host_agent と同じ規約)。
_PROFILE_ALLOWED = set(string.ascii_letters + string.digits + "._-")

# Exit codes
EXIT_OK = 0
EXIT_ACTION_FAILED = 1
EXIT_USAGE = 2
EXIT_NO_SOCKET = 3


def _default_socket_path() -> Path:
    rt = os.environ.get("XDG_RUNTIME_DIR", "")
    if not rt:
        print(
            "ERROR: XDG_RUNTIME_DIR が未設定です。systemd-logind セッション内で実行してください。",
            file=sys.stderr,
        )
        sys.exit(EXIT_NO_SOCKET)
    return Path(rt) / DEFAULT_SOCKET_REL


def _resolve_profile(arg: str | None) -> str:
    """``--profile`` 引数 → ``$GaleProfile`` env → 失敗時 exit 2 で fail-fast。"""
    profile = arg or os.environ.get("GaleProfile", "")
    profile = profile.strip()
    if not profile:
        print(
            "ERROR: profile が未指定です。`--profile <name>` または .env の GaleProfile を設定してください。",
            file=sys.stderr,
        )
        sys.exit(EXIT_USAGE)
    if not all(c in _PROFILE_ALLOWED for c in profile):
        print(
            f"ERROR: profile に許可外文字が含まれています: {profile!r} (許容: [A-Za-z0-9._-])",
            file=sys.stderr,
        )
        sys.exit(EXIT_USAGE)
    return profile


def _send_request(sock_path: Path, request: dict[str, Any]) -> dict[str, Any]:
    if not sock_path.exists():
        print(
            f"ERROR: host-agent socket が見当たりません ({sock_path})。"
            "host 側 GUI session の端末で `just host-agent` を起動してください。",
            file=sys.stderr,
        )
        sys.exit(EXIT_NO_SOCKET)
    payload = (json.dumps(request) + "\n").encode("utf-8")
    try:
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as sock:
            sock.settimeout(CONNECT_TIMEOUT_SEC)
            sock.connect(str(sock_path))
            sock.sendall(payload)
            sock.shutdown(socket.SHUT_WR)
            sock.settimeout(READ_TIMEOUT_SEC)
            buf = bytearray()
            while True:
                chunk = sock.recv(4096)
                if not chunk:
                    break
                buf.extend(chunk)
                if len(buf) >= MAX_RESPONSE_BYTES:
                    break
    except (ConnectionRefusedError, FileNotFoundError):
        print(
            f"ERROR: host-agent に接続できません ({sock_path})。"
            "host 側で `just host-agent` が動作しているか確認してください。",
            file=sys.stderr,
        )
        sys.exit(EXIT_NO_SOCKET)
    except socket.timeout:
        print("ERROR: host-agent の応答が timeout しました。", file=sys.stderr)
        sys.exit(EXIT_ACTION_FAILED)
    except OSError as e:
        print(f"ERROR: UDS 通信エラー: {e}", file=sys.stderr)
        sys.exit(EXIT_ACTION_FAILED)

    line, _, _ = bytes(buf).partition(b"\n")
    try:
        parsed = json.loads(line.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as e:
        print(f"ERROR: host-agent から不正な応答: {e}", file=sys.stderr)
        sys.exit(EXIT_ACTION_FAILED)
    if not isinstance(parsed, dict):
        print("ERROR: host-agent から JSON オブジェクト以外が返されました。", file=sys.stderr)
        sys.exit(EXIT_ACTION_FAILED)
    return parsed


def _print_response(response: dict[str, Any]) -> int:
    print(json.dumps(response, ensure_ascii=False, indent=2))
    return EXIT_OK if response.get("ok") else EXIT_ACTION_FAILED


def _parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Container-side client for the host debug bridge.")
    parser.add_argument(
        "--socket",
        type=Path,
        default=None,
        help="override UDS path (default: $XDG_RUNTIME_DIR/resonite-io-debug/host-agent.sock)",
    )
    sub = parser.add_subparsers(dest="action", required=True)

    start = sub.add_parser("start", help="Resonite を Gale 経由で起動する")
    start.add_argument(
        "--profile",
        default=None,
        help="Gale profile 名 (省略時は .env の GaleProfile を使用)",
    )

    sub.add_parser("stop", help="Resonite / Renderite を SIGTERM→SIGKILL で停止する")
    sub.add_parser("status", help="Resonite / Renderite の実行状態を表示する")

    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = _parse_args(sys.argv[1:] if argv is None else argv)
    sock_path: Path = args.socket if args.socket is not None else _default_socket_path()

    request: dict[str, Any]
    if args.action == "start":
        request = {"action": "start", "profile": _resolve_profile(args.profile)}
    elif args.action == "stop":
        request = {"action": "stop"}
    elif args.action == "status":
        request = {"action": "status"}
    else:
        print(f"ERROR: unknown action: {args.action!r}", file=sys.stderr)
        return EXIT_USAGE

    response = _send_request(sock_path, request)
    return _print_response(response)


if __name__ == "__main__":
    sys.exit(main())
