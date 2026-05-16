# syntax=docker/dockerfile:1.7
#
# resonite-io 開発環境イメージ。
#   - debian:bookworm-slim ベース。
#   - .NET 10 SDK / uv / just / protoc を /usr/local 配下に root で固定インストール。
#   - host UID/GID 一致の `dev` ユーザーで実行 (bind 経由の成果物が host 所有になる)。
#   - **ソースコードは COPY しない**。/workspace は host repo を直接 bind mount するので、
#     host 編集が即座に container 側に反映される。bootstrap copy は不要。
#     deps restore (dotnet tool restore + uv sync + pre-commit install) は
#     scripts/container-init.sh が冪等に行う。

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

# python3 / build-essential は入れない (uv が独自 Python を引く)。
# libicu72 は .NET SDK の globalization 初期化に必要 (bookworm-slim には含まれない)。
RUN apt-get update && apt-get install -y --no-install-recommends \
        git \
        curl \
        unzip \
        tar \
        ca-certificates \
        xz-utils \
        rsync \
        bash-completion \
        libatomic1 \
        libicu72 \
        netcat-openbsd \
        shellcheck \
        shfmt \
        sudo \
 && rm -rf /var/lib/apt/lists/*

# ツール一式を /usr/local 配下に 1 RUN でインストール (レイヤ削減)。
RUN set -eux; \
    curl -LsSf https://astral.sh/uv/install.sh \
      | UV_INSTALL_DIR=/usr/local/bin INSTALLER_NO_MODIFY_PATH=1 sh; \
    curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh \
      | bash -s -- --to /usr/local/bin; \
    arch="$(uname -m | sed 's/aarch64/aarch_64/;s/arm64/aarch_64/')"; \
    curl -fsSL \
      "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-${arch}.zip" \
      -o /tmp/protoc.zip; \
    unzip -oq /tmp/protoc.zip -d /usr/local; \
    rm -f /tmp/protoc.zip; \
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; \
    chmod +x /tmp/dotnet-install.sh; \
    /tmp/dotnet-install.sh --channel "${DOTNET_CHANNEL}" --install-dir /usr/local/dotnet; \
    rm -f /tmp/dotnet-install.sh

# bookworm-slim の /etc/bash.bashrc は bash-completion ローダ部がコメントアウトされて
# おり、`docker exec dev bash` は非 login 対話 shell で /etc/profile.d/bash_completion.sh
# も読まれないため、明示的にローダ呼び出しを追記する。
RUN set -eux; \
    uv generate-shell-completion bash > /etc/bash_completion.d/uv; \
    uvx --generate-shell-completion bash > /etc/bash_completion.d/uvx; \
    just --completions bash > /etc/bash_completion.d/just; \
    echo '[ -f /usr/share/bash-completion/bash_completion ] && . /usr/share/bash-completion/bash_completion' \
      >> /etc/bash.bashrc

# /workspace は host repo を bind するため、image 内の空ディレクトリは mount 時に
# 隠される。それでも先行作成しておくと bind 失敗時に container が `/workspace` 上で
# 起動できるため fallback として残す。/home/dev/.claude は container-init.sh が
# settings.container.json への symlink を張る前提でディレクトリだけ用意する。
# キャッシュ系 (.nuget / .cache/uv) は named volume のマウント先で、dev 所有で
# 先行作成しておくことで Docker が初回マウント時に属性を継承する。
RUN groupadd -g ${USER_GID} dev \
 && useradd -m -u ${USER_UID} -g ${USER_GID} -s /bin/bash dev \
 && mkdir -p /workspace /home/dev/.nuget/packages /home/dev/.cache/uv /home/dev/.claude \
 && chown -R dev:dev /workspace /home/dev \
 && echo 'dev ALL=(ALL) NOPASSWD: ALL' > /etc/sudoers.d/dev \
 && chmod 0440 /etc/sudoers.d/dev

USER dev
WORKDIR /workspace

CMD ["sleep", "infinity"]
