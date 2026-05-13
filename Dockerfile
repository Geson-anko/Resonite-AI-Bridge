# syntax=docker/dockerfile:1.7
#
# resonite-io 開発環境イメージ。
#   - debian:bookworm-slim ベース。
#   - .NET 10 SDK / uv / just / protoc を /usr/local 配下に root で固定インストール。
#   - host UID/GID 一致の `dev` ユーザーで実行 (bind 経由の deploy 物が host 所有になる)。
#   - **ソースコードは COPY しない**。/workspace は named volume、/source は host bind。
#     bootstrap (rsync + dotnet tool restore + uv sync) は scripts/container-init.sh が行う。

FROM debian:bookworm-slim

ARG USER_UID=1000
ARG USER_GID=1000
ARG DOTNET_CHANNEL=10.0
ARG PROTOC_VERSION=29.3

ENV DOTNET_ROOT=/usr/local/dotnet
ENV HOME=/home/dev
# $HOME/.local/bin は `uv tool install` の既定インストール先。
ENV PATH=/usr/local/dotnet:/usr/local/bin:/home/dev/.local/bin:$PATH
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1

# 最小限のシステム依存。python3 / build-essential は入れない (uv が独自 Python を引く)。
# libicu72 は .NET SDK の globalization 初期化に必要 (bookworm-slim には含まれない)。
# shellcheck / shfmt は scripts/ の lint 用 (pre-commit からも system binary として呼ばれる)。
# sudo は dev ユーザーから apt-get などを叩くため。NOPASSWD で透過運用 (後段)。
RUN apt-get update && apt-get install -y --no-install-recommends \
        git \
        curl \
        unzip \
        tar \
        ca-certificates \
        xz-utils \
        rsync \
        bash-completion \
        libicu72 \
        shellcheck \
        shfmt \
        sudo \
 && rm -rf /var/lib/apt/lists/*

# ツール一式を /usr/local 配下に固定。1 RUN にまとめてレイヤを抑える。
RUN set -eux; \
    # uv → /usr/local/bin/uv
    curl -LsSf https://astral.sh/uv/install.sh \
      | UV_INSTALL_DIR=/usr/local/bin INSTALLER_NO_MODIFY_PATH=1 sh; \
    # just → /usr/local/bin/just
    curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh \
      | bash -s -- --to /usr/local/bin; \
    # protoc → /usr/local/{bin,include}
    arch="$(uname -m | sed 's/aarch64/aarch_64/;s/arm64/aarch_64/')"; \
    curl -fsSL \
      "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-${arch}.zip" \
      -o /tmp/protoc.zip; \
    unzip -oq /tmp/protoc.zip -d /usr/local; \
    rm -f /tmp/protoc.zip; \
    # .NET SDK → /usr/local/dotnet
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; \
    chmod +x /tmp/dotnet-install.sh; \
    /tmp/dotnet-install.sh --channel "${DOTNET_CHANNEL}" --install-dir /usr/local/dotnet; \
    rm -f /tmp/dotnet-install.sh

# Tab completion を /etc/bash_completion.d/ に流し込む。dotnet は SDK バージョンによって
# `dotnet completions` のサブコマンド仕様が動くため対象外 (必要になれば dotnet-suggest を別途検討)。
# bookworm-slim の /etc/bash.bashrc は bash-completion ローダ部がコメントアウトされている
# ので明示的に有効化する (`docker exec dev bash` は非 login 対話 shell で
# /etc/profile.d/bash_completion.sh が読まれないため)。
RUN set -eux; \
    uv generate-shell-completion bash > /etc/bash_completion.d/uv; \
    uvx --generate-shell-completion bash > /etc/bash_completion.d/uvx; \
    just --completions bash > /etc/bash_completion.d/just; \
    echo '[ -f /usr/share/bash-completion/bash_completion ] && . /usr/share/bash-completion/bash_completion' \
      >> /etc/bash.bashrc

# dev ユーザーを host UID/GID 一致で作成し、named volume 用 mount point を先に dev 所有で作る。
# /workspace, /home/dev/.nuget/packages, /home/dev/.cache/uv を image 内に dev 所有で
# 用意することで、Docker が初回マウント時にディレクトリ属性を継承し named volume が
# dev 所有で初期化される (root 所有事故の予防)。
# NOPASSWD sudo を付与: host UID 一致を保ちつつ `sudo apt-get ...` 等の特権操作を可能に。
RUN groupadd -g ${USER_GID} dev \
 && useradd -m -u ${USER_UID} -g ${USER_GID} -s /bin/bash dev \
 && mkdir -p /workspace /home/dev/.nuget/packages /home/dev/.cache/uv \
 && chown -R dev:dev /workspace /home/dev \
 && echo 'dev ALL=(ALL) NOPASSWD: ALL' > /etc/sudoers.d/dev \
 && chmod 0440 /etc/sudoers.d/dev

USER dev
WORKDIR /workspace

CMD ["sleep", "infinity"]
