#!/usr/bin/env sh
set -eu

REPO_OWNER="Krusty93"
REPO_NAME="relego"

VERSION="${RELEGO_VERSION:-}"
DRY_RUN=0
BIN_DIR=""
TARGET_PATH=""

usage() {
  cat <<'EOF'
Usage: install.sh [options]

Install the latest Relego CLI binary for macOS or Linux.
The binary is always installed to ~/.local/bin/relego.

Options:
  -v, --version <ver>   Install a specific CLI version instead of the latest
  -n, --dry-run         Print the resolved version, URL, and install path
  -h, --help            Show this help text

Environment variables:
  RELEGO_VERSION        Same as --version
EOF
}

info() {
  printf 'install.sh: %s\n' "$*" >&2
}

fail() {
  info "$*"
  exit 1
}

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "missing required command: $1"
}

normalize_version() {
  printf '%s' "$1" | sed -e 's#^cli/##' -e 's#^v##'
}

set_install_dir() {
  [ -n "${HOME:-}" ] || fail "HOME is not set; cannot determine the install directory"
  BIN_DIR="${HOME}/.local/bin"
  TARGET_PATH="${BIN_DIR}/relego"
}

latest_version() {
  curl -fsSL \
    -H 'Accept: application/vnd.github+json' \
    -H 'User-Agent: relego-install-script' \
    -H 'X-GitHub-Api-Version: 2022-11-28' \
    "https://api.github.com/repos/${REPO_OWNER}/${REPO_NAME}/releases?per_page=100" |
    grep -oE '"tag_name":[[:space:]]*"cli/v[^"]+"' |
    head -n 1 |
    sed -E 's/"tag_name":[[:space:]]*"cli\/v([^"]+)"/\1/'
}

detect_rid() {
  os_name=$(uname -s)
  arch_name=$(uname -m)

  case "$os_name" in
    Darwin)
      case "$arch_name" in
        arm64|aarch64)
          printf 'osx-arm64\n'
          ;;
        x86_64|amd64)
          printf 'osx-x64\n'
          ;;
        *)
          fail "unsupported macOS architecture: $arch_name"
          ;;
      esac
      ;;
    Linux)
      case "$arch_name" in
        x86_64|amd64)
          printf 'linux-x64\n'
          ;;
        arm64|aarch64)
          printf 'linux-arm64\n'
          ;;
        arm|armv6l|armv7l|armv8l|armhf)
          printf 'linux-arm\n'
          ;;
        *)
          fail "unsupported Linux architecture: $arch_name"
          ;;
      esac
      ;;
    MINGW*|MSYS*|CYGWIN*)
      fail "Windows installation is handled by install.ps1"
      ;;
    *)
      fail "unsupported operating system: $os_name"
      ;;
  esac
}

print_path_hint() {
  case ":${PATH:-}:" in
    *:"$BIN_DIR":*)
      return
      ;;
    *)
      info "$BIN_DIR is not on PATH; add it to run relego without a full path"
      ;;
  esac
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    -v|--version)
      [ "$#" -ge 2 ] || fail "missing value for $1"
      VERSION="$2"
      shift 2
      ;;
    -n|--dry-run)
      DRY_RUN=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "unknown argument: $1"
      ;;
  esac
done

need_cmd curl
need_cmd grep
need_cmd sed
need_cmd head
need_cmd uname
need_cmd mktemp
need_cmd install

set_install_dir

if [ -n "$VERSION" ]; then
  VERSION=$(normalize_version "$VERSION")
else
  VERSION=$(latest_version)
fi

[ -n "$VERSION" ] || fail "could not resolve the latest CLI version from GitHub Releases"

RID=$(detect_rid)
ASSET_NAME="relego-${VERSION}-${RID}"
DOWNLOAD_URL="https://github.com/${REPO_OWNER}/${REPO_NAME}/releases/download/cli%2Fv${VERSION}/${ASSET_NAME}"
TMP_FILE=$(mktemp "${TMPDIR:-/tmp}/relego.XXXXXX")

cleanup() {
  rm -f "$TMP_FILE"
}

trap cleanup EXIT HUP INT TERM

if [ "$DRY_RUN" -eq 1 ]; then
  info "version=$VERSION"
  info "asset=$ASSET_NAME"
  info "url=$DOWNLOAD_URL"
  info "target_path=$TARGET_PATH"
  exit 0
fi

info "downloading ${ASSET_NAME}"
curl -fsSL \
  -H 'Accept: application/octet-stream' \
  -H 'User-Agent: relego-install-script' \
  "$DOWNLOAD_URL" \
  -o "$TMP_FILE"

chmod 0755 "$TMP_FILE"
mkdir -p "$BIN_DIR" 2>/dev/null || fail "cannot create $BIN_DIR"
[ -w "$BIN_DIR" ] || fail "cannot write to $BIN_DIR"

install -m 0755 "$TMP_FILE" "$TARGET_PATH"

info "saved executable to $TARGET_PATH"
info "installed relego ${VERSION}"
print_path_hint
