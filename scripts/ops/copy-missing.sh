#!/bin/bash
# 把來源資料夾裡「目的還沒有的檔案」複製過去，只補、不覆蓋、不刪。在容器裡跑（要 GNU 的 find 與 cp）：
# backup-loop.sh 備份附件、restore.sh 與 restore-drill.sh 把備份裡的附件放回去，都用它。
# 用法：copy-missing.sh <來源資料夾> <目的資料夾> [擁有者的 UID]
#   給了 UID 的話，新建的檔案與資料夾改成這個擁有者（api 容器不是 root，要讀得到、寫得進去）。
# 標準輸出只印最後一行「copied=<這次複製了幾個>」；說明與錯誤寫到標準錯誤。有檔案複製失敗就不是 0。
#
# 附件的檔案建好之後不會再改（鍵是附件的編號），所以「目的已經有」就不用再看。
# 寫一半的不會被當成完成的：先複製成目的資料夾裡的暫存檔、大小對得上才改成正式的名字，
# 不然被中斷的複製會留下一個截斷的檔案，之後「已經有了」就永遠不會補。
# 來源裡 api 自己的暫存檔（開頭是 . 或結尾是 .tmp、.partial）不算。
set -euo pipefail

# 暫存檔超過這麼久還在，就是之前被中斷的複製留下來的。
readonly STALE_PARTIAL_MINUTES=180

log() {
  printf '%s\n' "$*" >&2
}

# 新建的資料夾（含中間層）改擁有者，只動 dst 底下新建的。
make_dir() {
  local dir="$1" owner="$2" parent
  [[ -d "$dir" ]] && return 0
  parent="$(dirname "$dir")"
  make_dir "$parent" "$owner" || return 1
  # 同時有另一個備份建了同一個資料夾：已經在了就算成功。
  mkdir -- "$dir" 2>/dev/null || [[ -d "$dir" ]] || return 1
  if [[ -n "$owner" ]]; then chown "$owner:$owner" -- "$dir" || return 1; fi
}

copy_one() {
  local src="$1" dst="$2" rel="$3" owner="$4" target tmp expected actual
  target="$dst/$rel"
  # 清單做好之後、api 刪掉的檔案：不用備份，不算失敗。
  [[ -f "$src/$rel" ]] || return 0
  make_dir "$(dirname "$target")" "$owner" || { log "建不了資料夾：$rel"; return 1; }
  # 暫存檔名帶亂數（mktemp，建在同一個資料夾）：排程備份和手動、部署前的備份可能同時跑（backup-loop 沒有鎖），
  # 固定的名字會讓兩邊寫同一個暫存檔，一邊改名走後另一邊失敗。名字仍是 .*.partial，備份與 status.sh 都排除。
  tmp="$(mktemp --suffix=.partial "$(dirname "$target")/.${target##*/}.XXXXXX")" || { log "建不了暫存檔：$rel"; return 1; }
  if ! cp -- "$src/$rel" "$tmp"; then
    rm -f -- "$tmp"
    log "複製失敗：$rel（磁碟空間或權限？）"
    return 1
  fi
  expected="$(stat -c %s "$src/$rel" 2>/dev/null || echo -1)"
  actual="$(stat -c %s "$tmp")"
  # 來源在複製的當下被刪掉：expected 讀不到，不算失敗。
  if [[ "$expected" != -1 && "$expected" != "$actual" ]]; then
    rm -f -- "$tmp"
    log "複製失敗：$rel 複製出來的大小（$actual）和來源（$expected）不一樣。"
    return 1
  fi
  # copy_one 是在 if 裡被呼叫的，set -e 在這裡不生效：chown、chmod 失敗要自己接住，
  # 不然檔案會留成 root 的，api（UID 1654）讀不到。
  if [[ -n "$owner" ]]; then
    chown "$owner:$owner" -- "$tmp" || { rm -f -- "$tmp"; log "改不了擁有者：$rel"; return 1; }
  fi
  chmod 600 -- "$tmp" || { rm -f -- "$tmp"; log "改不了權限：$rel"; return 1; }
  # -n：同時有別人補了同一個檔案時不蓋掉；-T：目標是資料夾時不要塞進去。
  mv -nT -- "$tmp" "$target" || { rm -f -- "$tmp"; log "改名失敗：$rel"; return 1; }
  # -n：目的已經被另一個備份補好時 mv 什麼都不做也回報成功，暫存檔還在，要自己刪。
  rm -f -- "$tmp"
}

main() {
  local src="${1:-}" dst="${2:-}" owner="${3:-}" list rel copied=0 failed=0
  [[ -n "$src" && -n "$dst" ]] || { log "用法：copy-missing.sh <來源資料夾> <目的資料夾> [擁有者的 UID]"; return 2; }
  [[ -d "$src" ]] || { log "來源資料夾 $src 不存在（沒有掛進容器？）。"; return 1; }
  [[ -z "$owner" || "$owner" =~ ^[0-9]+$ ]] || { log "擁有者要是 UID（數字），現在是「$owner」。"; return 2; }
  umask 077
  make_dir "$dst" ""
  find "$dst" -type f -name '.*.partial' -mmin "+$STALE_PARTIAL_MINUTES" -delete 2>/dev/null || true

  list="$(mktemp)"
  # 雙引號：現在就把路徑代進去（函式結束後 $list 不在了，set -u 會報錯）。
  # shellcheck disable=SC2064 # 要現在就代入。
  trap "rm -f -- '$list'" EXIT
  find "$src" -type f ! -name '.*' ! -name '*.tmp' ! -name '*.partial' -printf '%P\0' >"$list" ||
    { log "列不出 $src 裡的檔案。"; return 1; }
  while IFS= read -r -d '' rel; do
    [[ -e "$dst/$rel" ]] && continue
    if copy_one "$src" "$dst" "$rel" "$owner"; then
      copied=$((copied + 1))
    else
      failed=$((failed + 1))
    fi
  done <"$list"
  printf 'copied=%d\n' "$copied"
  if ((failed > 0)); then
    log "有 $failed 個檔案沒有複製成功。"
    return 1
  fi
}

# 整支腳本包在 main 裡、同一行結束：和 scripts/ops/ 其他腳本一樣，執行中被更新也不會讀到改了一半的內容。
main "$@"; exit $?
