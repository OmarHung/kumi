#!/bin/bash
# 提交前的掃描：關閉警告的寫法、BOM、看不見的字元、過大的檔案、寫死的密碼、不是 example 的網域。
# 只看還沒提交的檔案（修改過的與新增的）。有輸出不一定是錯，逐項看過再決定。
# macOS 內建的是 bash 3.2，沒有 mapfile，所以每一段各自用 while read 讀檔案清單。
# 用法：bash scripts/precommit-scan.sh
cd "$(git rev-parse --show-toplevel)" || exit 1

changed_files() {
  # 排除這個腳本自己：它的搜尋樣式本身就會命中。
  git ls-files -mo --exclude-standard | grep -vE "node_modules|/dist/|\.(png|ico|svg|woff2?)$|^scripts/precommit-scan\.sh$"
}

echo "== 關閉警告、除錯輸出、innerHTML =="
changed_files | while IFS= read -r f; do
  [ -f "$f" ] && grep -HnE "#pragma|NoWarn|eslint-disable|@ts-ignore|@ts-expect-error|console\.log|dangerouslySetInnerHTML" "$f"
done | grep -vE "^[^:]*\.md:" | head -20

echo "== BOM（EF 產生的遷移檔常有） =="
changed_files | while IFS= read -r f; do
  [ -f "$f" ] && head -c3 "$f" | xxd -p | grep -q '^efbbbf' && echo "$f"
done

echo "== 看不見的字元 =="
changed_files | while IFS= read -r f; do
  [ -f "$f" ] && perl -CSD -ne 'print "$ARGV:$.\n" if /[\x{200B}-\x{200F}\x{2028}-\x{202E}\x{2060}-\x{2064}\x{FEFF}]/' "$f"
done | head -20

echo "== 超過 400 行（遷移檔與 lock 檔除外；上限 800） =="
changed_files | while IFS= read -r f; do
  [ -f "$f" ] || continue
  case "$f" in *Migrations/*|*package-lock.json) continue;; esac
  n=$(wc -l < "$f"); [ "$n" -gt 400 ] && echo "$n $f"
done | sort -rn

echo "== 寫死的連線字串密碼 =="
changed_files | while IFS= read -r f; do
  [ -f "$f" ] && grep -HnEi "Password=[^;\"{ ]+" "$f"
done | head -10

echo "== 不是 example 的 Email 網域 =="
changed_files | while IFS= read -r f; do
  [ -f "$f" ] && grep -HnoEi "[a-z0-9._-]+@[a-z0-9.-]+\.(com|tw|net|org|io)" "$f" | grep -viE "@example\.(com|org|net)"
done | head -10
