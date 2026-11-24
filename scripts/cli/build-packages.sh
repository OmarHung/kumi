#!/bin/bash
# 建出伺服器提供下載的 kumi 執行檔：macOS（Apple 晶片、Intel）與 Windows，各一個自帶執行環境的單一檔案。
# 裝的人不用先裝 .NET；伺服器（GET /v1/cli）照檔名認出版本與平台，所以檔名的格式是合約（internal/HANDOFF-cli-downloads.md 第 1 節）。
# Dockerfile 的 cli 階段用它；本機想先試的話也可以直接跑（需要 .NET 10 SDK）。
# macOS 內建的是 bash 3.2，沒有關聯陣列，平台清單用「RID 平台 副檔名」一行一筆。
# 用法：bash scripts/cli/build-packages.sh <輸出資料夾>（資料夾不能已經有東西）
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "用法：bash scripts/cli/build-packages.sh <輸出資料夾>" >&2
  exit 2
fi

output=$1
# 不清掉既有的檔案：輸出資料夾給錯（例如給到 repo 根目錄）時，寧可停下來也不要刪東西。
if [ -e "$output" ] && [ -n "$(ls -A "$output")" ]; then
  echo "輸出資料夾 $output 已經有東西了，請給一個空的或不存在的資料夾。" >&2
  exit 2
fi
mkdir -p "$output"
output=$(cd "$output" && pwd)

# 從腳本的位置找 repo 根目錄：容器裡沒有 .git，不能用 git rev-parse。
cd "$(dirname "$0")/../.."
project=src/PmTool.Cli/PmTool.Cli.csproj

version=$(dotnet msbuild "$project" -getProperty:Version -nologo | tr -d '[:space:]')
if ! printf '%s' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo "讀不到 kumi 的版本（得到「$version」），伺服器只認三段數字的版本。" >&2
  exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

while read -r rid platform extension; do
  echo "== kumi $version $platform（$rid）"
  # KumiStandalone 讓 kumi update 知道這份可以自己換掉自己；DebugType=none 不產生 .pdb，只留一個檔案。
  # 標準輸入接 /dev/null：迴圈讀的是下面的平台清單，不能被 dotnet 吃掉。
  dotnet publish "$project" -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:KumiStandalone=true -p:DebugType=none \
    -o "$work/$rid" -nologo -v quiet </dev/null
  cp "$work/$rid/kumi$extension" "$output/kumi-$version-$platform$extension"
done <<'PLATFORMS'
osx-arm64 macos-arm64
osx-x64 macos-x64
win-x64 windows-x64 .exe
PLATFORMS

ls -l "$output"
