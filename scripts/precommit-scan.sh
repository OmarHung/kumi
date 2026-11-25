#!/bin/bash
# 提交前的掃描：關閉警告的寫法、BOM、看不見的字元、過大的檔案、寫死的密碼、不是 example 的網域，
# 以及機密與內部資訊的樣式（權杖、私鑰、私有與共享網段的位址、本機的路徑、內部的主機名）。
# 權杖那幾種（私鑰、各家金鑰、JWT 等）和 src/Shared/PmTool.Abstractions/SecretText.cs 是同一組，改了任何一邊要同步另一邊；
# 路徑、位址、主機名只有這裡掃。
# 前面幾項有輸出不一定是錯，逐項看過再決定；最後一項（機密、內部資訊）有命中就以非 0 結束。
# 結束代碼：0 乾淨、1 有命中、2 掃描本身出錯（沒有 perl、允許清單讀不到或格式錯、git 讀不出內容）。
# 模式：
#   （不帶參數）  暫存區的內容，加上工作目錄裡尚未暫存的修改與新增；輸出標明命中在哪一邊
#   --staged      只掃暫存區的內容（pre-commit hook 用：掃的就是會進這次提交的那一份）
#   --worktree    只掃工作目錄裡修改與新增、還沒暫存的檔案（手動檢查用）
#   --all         git 追蹤的全部檔案（含已暫存的新檔）
#   --diff <基準> 從基準到 HEAD 有變動的檔案，加上範圍內每個提交新增的行（中途加入又刪掉的內容，
#                 公開之後一樣看得到），給 CI 看一次 PR 或 push 的差異
# 允許清單預設是 scripts/precommit-scan-allowlist.txt，環境變數 KUMI_SCAN_ALLOWLIST 可以指到別的檔案
# （CI 用基準提交的那一份，讓一次推送沒辦法靠同時改允許清單讓自己過關）。格式見該檔的檔頭。
# 版本號（例如 10.0.100.1）和私有位址長得一樣，會被當成 private-ip 誤報；寧可誤報，
# 確定是版本號就在允許清單用「值的正規式」放行。
# macOS 內建的是 bash 3.2，沒有 mapfile 與關聯陣列。
cd "$(git rev-parse --show-toplevel)" || exit 2

readonly DEFAULT_ALLOWLIST="scripts/precommit-scan-allowlist.txt"
allowlist="${KUMI_SCAN_ALLOWLIST:-$DEFAULT_ALLOWLIST}"
mode="both"
base=""
case "${1:-}" in
  "") ;;
  --staged) mode="staged" ;;
  --worktree) mode="worktree" ;;
  --all) mode="all" ;;
  --diff)
    mode="diff"; base="${2:-}"
    [ -n "$base" ] || { echo "用法：--diff <基準>" >&2; exit 2; }
    ;;
  *) echo "用法：bash scripts/precommit-scan.sh [--staged | --worktree | --all | --diff <基準>]" >&2; exit 2 ;;
esac

if ! command -v perl >/dev/null 2>&1; then
  echo "掃描錯誤：找不到 perl，無法掃描。" >&2
  exit 2
fi
if [ ! -r "$allowlist" ]; then
  echo "掃描錯誤：讀不到允許清單 ${allowlist}。" >&2
  exit 2
fi

# 掃描的來源：每筆是「種類 NUL 路徑 NUL」。S 暫存區、W 工作目錄、A 工作目錄（--all，不標位置）、
# R 基準到 HEAD 的差異裡 HEAD 的版本、L 範圍內每個提交新增的行（路徑欄放基準）。
sources_file="$(mktemp)"
paths_file="$(mktemp)"
trap 'rm -f "$sources_file" "$paths_file"' EXIT

# 各清單都是 NUL 分隔，檔名有空白、中文或以 - 開頭都對。
staged_list() { git diff --cached -z --name-only --diff-filter=ACMR; }
worktree_list() { git ls-files -z -mo --exclude-standard; }
emit() { # emit <種類>：把標準輸入的 NUL 分隔路徑轉成來源記錄
  while IFS= read -r -d '' p; do printf '%s\0%s\0' "$1" "$p"; done
}

case "$mode" in
  staged) staged_list | emit S ;;
  worktree) worktree_list | emit W ;;
  both) { staged_list | emit S; worktree_list | emit W; } ;;
  all) git ls-files -z --cached --others --exclude-standard | emit A ;;
  diff)
    if git rev-parse --verify -q "${base}^{commit}" >/dev/null; then
      git diff --name-only -z --diff-filter=ACMR "${base}" HEAD | emit R
      printf 'L\0%s\0' "$base"
    else
      echo "找不到基準 ${base}，改掃全部追蹤的檔案。" >&2
      git ls-files -z | emit A
    fi
    ;;
esac > "$sources_file"

# 前幾項「只提示」的檢查看的檔案清單（工作目錄裡的現況）。
tr '\0' '\n' < "$sources_file" | awk 'NR % 2 == 0' | sort -u | tr '\n' '\0' \
  | while IFS= read -r -d '' f; do
      case "$f" in
        node_modules/*|*/node_modules/*|*/dist/*|scripts/precommit-scan.sh) continue ;;
        *.png|*.ico|*.svg|*.woff|*.woff2|*.jpg|*.jpeg|*.gif|*.webp|*.pdf|*.zip|*.gz) continue ;;
      esac
      [ -f "$f" ] && printf '%s\0' "$f"
    done > "$paths_file"

# 前幾項「只提示」的檢查：一次 perl 掃完全部的檔案（每個檔案各開一個程序在整個 repo 上要將近一分鐘）。
# 輸出的格式與順序和逐項用 grep 時一樣。
read -r -d '' SOFT <<'PERL'
use strict; use warnings;
my (@warn, @bom, @invis, @long, @pass, @mail);
local $/ = "\0";
while (defined(my $f = <STDIN>)) {
  chomp $f;
  open(my $in, "<:raw", $f) or next;
  local $/; my $c = <$in>; close $in;
  next unless defined $c;
  next if substr($c, 0, 8000) =~ /\0/;
  push @bom, $f if $c =~ /\A\xEF\xBB\xBF/;
  my @lines = split /\n/, $c, -1;
  my $count = ($c =~ tr/\n//);
  push @long, [$count, $f] if $count > 400 && $f !~ m{Migrations/} && $f !~ /package-lock\.json\z/;
  my $n = 0;
  for my $l (@lines) {
    $n++;
    push @warn, "$f:$n:$l" if $f !~ /\.md\z/ && $l =~ /#pragma|NoWarn|eslint-disable|\@ts-ignore|\@ts-expect-error|console\.log|dangerouslySetInnerHTML/;
    push @pass, "$f:$n:$l" if $l =~ /Password=[^;"{ ]+/i;
    while ($l =~ /([a-z0-9._-]+\@[a-z0-9.-]+\.(?:com|tw|net|org|io))/gi) {
      my $m = $1;
      push @mail, "$f:$n:$m" unless $m =~ /\@example\.(?:com|org|net)/i;
    }
    my $d = $l; utf8::decode($d);
    push @invis, "$f:$n" if $d =~ /[\x{200B}-\x{200F}\x{2028}-\x{202E}\x{2060}-\x{2064}\x{FEFF}]/;
  }
}
sub show { my ($max, @l) = @_; my $i = 0; for (@l) { last if $max && $i++ >= $max; print "$_\n" } }
print "== 關閉警告、除錯輸出、innerHTML ==\n"; show(20, @warn);
print "== BOM（EF 產生的遷移檔常有） ==\n"; show(0, @bom);
print "== 看不見的字元 ==\n"; show(20, @invis);
print "== 超過 400 行（遷移檔與 lock 檔除外；上限 800） ==\n";
show(0, map { "$_->[0] $_->[1]" } sort { $b->[0] <=> $a->[0] } @long);
print "== 寫死的連線字串密碼 ==\n"; show(10, @pass);
print "== 不是 example 的 Email 網域 ==\n"; show(10, @mail);
PERL
perl -e "$SOFT" < "$paths_file" || { echo "掃描錯誤：提示項目的檢查失敗。" >&2; exit 2; }

# 機密與內部資訊的樣式。只印位置與樣式名稱，不印比對到的內容。一次 perl 處理全部的來源。
echo "== 機密與內部資訊的樣式（命中即失敗） =="
read -r -d '' SCANNER <<'PERL'
use strict; use warnings;
my ($allow_path, $self_allow) = @ARGV;

# --- 樣式 ---
my @pat = (
  ["kumi-token",     qr/\b(?:pmt|pmr|kha|kjt)_[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])/],
  ["private-key",    qr/-----BEGIN [A-Z ]*PRIVATE KEY(?: BLOCK)?-----/],
  ["anthropic-key",  qr/sk-ant-(?:api|admin)\d\d-[A-Za-z0-9_-]{80,}/],
  ["openrouter-key", qr/sk-or-v1-[0-9a-f]{64}/],
  ["openai-key",     qr/sk-proj-[A-Za-z0-9_-]{40,}|(?<![A-Za-z0-9_-])sk-(?!ant-|or-|proj-)[A-Za-z0-9]{40,}/],
  ["google-key",     qr/AIza[0-9A-Za-z_-]{35}/],
  ["github-token",   qr/\bgh[opsur]_[A-Za-z0-9]{36,}|\bgithub_pat_[A-Za-z0-9_]{30,}/],
  ["aws-access-key", qr/\b(?:AKIA|ASIA)[0-9A-Z]{16}\b/],
  ["slack-token",    qr/\bxapp-[0-9]-[A-Z0-9]{8,}-[0-9]{8,}-[A-Za-z0-9]{20,}|\bxox[baprs]-(?:[0-9]{1,3}-)*[0-9]{8,}-[A-Za-z0-9-]{10,}/],
  ["gitlab-token",   qr/\bglpat-[A-Za-z0-9_-]{20,}/],
  ["jwt",            qr/(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}/],
  ["stripe-key",     qr/\b(?:sk|rk)_(?:live|test)_[A-Za-z0-9]{20,}/],
  ["npm-token",      qr/\bnpm_[A-Za-z0-9]{36}(?![A-Za-z0-9])/],
  ["sendgrid-key",   qr/\bSG\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}/],
  ["azure-storage-key", qr{AccountKey=[A-Za-z0-9+/]{60,}={0,2}}],
  ["url-credential", qr{://[^/\s:\@]+:[^/\s\@]+\@}],
  ["private-ip",     qr/(?<![\d.])(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})(?![\d.]*\d)/],
  ["user-home-path", qr{/Users/[A-Za-z0-9._-]+/}],
  ["linux-home-path", qr{(?<![A-Za-z0-9_.-])/home/[A-Za-z0-9._-]+/}],
  ["windows-home-path", qr{\b[A-Za-z]:\\{1,2}Users\\{1,2}(?!(?:Public|Default|Default User|All Users)\\)[A-Za-z0-9._-]+\\}i],
  ["cgnat-ip",       qr/(?<![\d.])100\.(?:6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.\d{1,3}\.\d{1,3}(?![\d.]*\d)/],
  ["link-local-ip",  qr/(?<![\d.])169\.254\.\d{1,3}\.\d{1,3}(?![\d.]*\d)/],
  ["volumes-path",   qr{/Volumes/}],
  ["internal-host",  qr/\b[a-z0-9][a-z0-9-]*\.(?:local|internal|corp|lan)(?![\w-])(?!\.[A-Za-z0-9])/],
);
my %known = map { $_->[0] => 1 } @pat;

# --- 允許清單：路徑的 glob、樣式名稱、值的正規式（比對到的整段都要符合） ---
my (@allow, $bad);
open(my $fh, "<:raw", $allow_path) or do { print STDERR "掃描錯誤：讀不到允許清單 $allow_path\n"; exit 2 };
my $ln = 0;
while (my $l = <$fh>) {
  $ln++;
  $l =~ s/\r?\n\z//;
  next if $l =~ /^\s*(?:#|\z)/;
  $l =~ s/^\s+//; $l =~ s/\s+\z//;
  my @f = split /\s*\t\s*|\s{2,}/, $l, 3;
  my $err;
  if (@f != 3) { $err = "要有三欄（路徑的 glob、樣式名稱、值的正規式）" }
  elsif ($f[0] =~ /\A\*+\z/) { $err = "路徑不能只是 * 或 **" }
  elsif (!$known{$f[1]}) { $err = "不認得的樣式名稱" }
  else {
    (my $core = $f[2]) =~ s/\A(?:\^|\\A)//; $core =~ s/(?:\$|\\z)\z//;
    $core =~ s/\A\(\?:(.*)\)\z/$1/;
    if ($core =~ /\A\.[*+]\z/ || $core eq "") { $err = "值的正規式不能是空的或 .*" }
  }
  my ($gre, $vre);
  if (!$err) {
    my $g = quotemeta($f[0]); $g =~ s/\\\*\\\*/.*/g; $g =~ s/\\\*/.*/g; $g =~ s/\\\?/./g;
    $gre = qr/\A$g\z/;
    $vre = eval { qr/\A(?:$f[2])\z/ };
    $err = "值的正規式無法編譯" unless $vre;
  }
  if ($err) { print STDERR "掃描錯誤：允許清單第 $ln 行：$err\n"; $bad = 1; next }
  push @allow, { g => $gre, n => $f[1], v => $vre };
}
close $fh;
exit 2 if $bad;

# --- 要略過的檔案 ---
sub skip {
  my ($p) = @_;
  return 1 if $p =~ m{(?:\A|/)node_modules/} || $p =~ m{/dist/} || $p eq "scripts/precommit-scan.sh";
  return 1 if $p =~ /\.(?:png|ico|svg|woff2?|jpe?g|gif|webp|pdf|zip|gz)\z/i;
  return 0;
}

my @hits;
my $fail = 0;
sub check_line {
  my ($where, $path, $n, $line, $use_allow) = @_;
  for my $p (@pat) {
    my $found = 0;
    while ($line =~ /$p->[1]/g) {
      my $m = $&;
      next if $use_allow && grep { $path =~ $_->{g} && $_->{n} eq $p->[0] && $m =~ $_->{v} } @allow;
      $found = 1; last;
    }
    next unless $found;
    push @hits, ($n eq "" ? "$where$path: $p->[0]" : "$where$path:$n: $p->[0]");
  }
}

sub git_read {
  my ($spec) = @_;
  open(my $in, "-|", "git", "cat-file", "blob", $spec) or return undef;
  local $/; my $c = <$in>;
  close $in or return undef;
  return $c;
}

sub scan_blob {
  my ($where, $path, $content) = @_;
  return if substr($content, 0, 8000) =~ /\0/;
  my $use_allow = $path ne $self_allow;
  my $n = 0;
  for my $line (split /\n/, $content, -1) { $n++; check_line($where, $path, $n, $line, $use_allow) }
}

sub scan_log {
  my ($base) = @_;
  open(my $in, "-|", "git", "-c", "core.quotepath=false", "log", "-p", "-U0", "--no-color", "--no-renames",
       "--format=\@\@commit %h", "$base..HEAD") or do { print STDERR "掃描錯誤：git log 失敗\n"; $fail = 1; return };
  local $/ = "\n";
  my ($commit, $path, $hdr) = ("", undef, 0);
  while (my $l = <$in>) {
    chomp $l;
    if ($l =~ /^\@\@commit (\S+)/) { $commit = $1; $path = undef; $hdr = 0; next }
    if ($l =~ /^diff --git /) { $hdr = 1; $path = undef; next }
    if ($hdr) {
      if ($l =~ m{^\+\+\+ (?:b/(.*?)|/dev/null)\t?$}) { $path = $1; $path =~ s/^"|"$//g if defined $path }
      $hdr = 0 if $l =~ /^\@\@ /;
      next;
    }
    next unless defined $path && $l =~ /^\+/ && !skip($path);
    my $text = substr($l, 1);
    check_line("提交 $commit ", $path, "", $text, $path ne $self_allow);
  }
  close $in or do { print STDERR "掃描錯誤：git log 以非零代碼結束\n"; $fail = 1 };
}

# --- 讀來源記錄 ---
my %where = (S => "暫存區 ", W => "工作目錄 ", A => "", R => "");
{
  local $/ = "\0";
  while (defined(my $kind = <STDIN>)) {
    chomp $kind;
    my $path = <STDIN>; last unless defined $path; chomp $path;
    if ($kind eq "L") { scan_log($path); next }
    next if skip($path);
    my $content;
    if ($kind eq "S") {
      $content = git_read(":0:$path");
      if (!defined $content) { print STDERR "掃描錯誤：讀不到暫存的內容 $path\n"; $fail = 1; next }
    } elsif ($kind eq "R") {
      $content = git_read("HEAD:$path");
      if (!defined $content) { print STDERR "掃描錯誤：讀不到 HEAD 的內容 $path\n"; $fail = 1; next }
    } else {
      next unless -f $path;
      open(my $in, "<:raw", $path) or do { print STDERR "掃描錯誤：讀不到 $path\n"; $fail = 1; next };
      local $/; $content = <$in>; close $in;
    }
    scan_blob($where{$kind}, $path, $content);
  }
}
exit 2 if $fail;
if (@hits) { my %s; print "$_\n" for grep { !$s{$_}++ } @hits; exit 1 }
exit 0
PERL

secret_hits="$(perl -e "$SCANNER" "$allowlist" "$DEFAULT_ALLOWLIST" < "$sources_file")"
status=$?
if [ "$status" -eq 0 ]; then
  echo "（沒有命中）"
  exit 0
fi
if [ "$status" -eq 1 ]; then
  printf '%s\n' "$secret_hits"
  echo "命中上面的樣式。確定是假值（測試或文件的範例）才加進 ${allowlist}（路徑、樣式名稱、值的正規式）；真的機密要先換掉。" >&2
  echo "若是四段的版本號被當成 private-ip 誤報，一樣用允許清單的值放行。" >&2
  exit 1
fi
printf '%s\n' "$secret_hits"
echo "掃描本身出錯（結束代碼 ${status}），結果不可信。" >&2
exit 2
