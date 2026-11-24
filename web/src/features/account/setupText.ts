import type { CliPackage } from '../../api/cli';
import { COMMAND_NAME } from '../../config/product';
import { installCommand, isMac, pathCommand } from './cliText';

/** 設定精靈三步的標題（畫面與測試都用這一份）。 */
export const STEP_TITLES = {
  download: `下載 ${COMMAND_NAME}`,
  command: '在終端機貼上這一行',
  status: '完成的項目',
} as const;

export const WIZARD_NOTE = `一次設定好命令列工具、Claude Code／Codex 與本機代理。`;

export const COMMAND_NOTE = '會問你要設定哪些，再開瀏覽器請你按「允許」（Claude Code、Codex 各會再開一次授權頁）。';

export const STATUS_NOTE = '在終端機做完之後，這裡會自己打勾，不必重新整理。';

/** 單引號的跳脫：zsh（POSIX）是 '\''，PowerShell 是連寫兩個。網址只來自 window.location.origin，主機名稱不會有單引號，但路徑型的 origin 不能讓它拆開引號。 */
function quoted(origin: string, escapedQuote: string): string {
  return `'${origin.replaceAll("'", escapedQuote)}'`;
}

/** 網址用單引號包起來：IPv6 的 [::1] 在 zsh 會被當成萬用字元。 */
function setupArguments(origin: string, escapedQuote: string): string {
  return `setup --server ${quoted(origin, escapedQuote)}`;
}

const POSIX_QUOTE = "'\\''";
const POWERSHELL_QUOTE = "''";

/** 已經裝好 kumi 的：直接啟動精靈。 */
export function setupCommand(origin: string): string {
  return `${COMMAND_NAME} ${setupArguments(origin, POSIX_QUOTE)}`;
}

/**
 * 一行做完安裝、加進 PATH、啟動精靈（internal/HANDOFF-setup-wizard.md 5.2）。用完整路徑啟動：這個終端機的 PATH 還沒有剛加的資料夾。
 * macOS 的 PATH 指令是「grep 找不到才寫入」的 `||`，包在大括號裡才不會吃掉前面的 `&&`；Windows 的 pathCommand 重設 $d（和安裝時同一個值），
 * 最後的 `& "$d\\kumi.exe"` 才找得到。
 */
export function oneLineCommand(pkg: CliPackage, origin: string): string {
  if (isMac(pkg.platform)) {
    return `${installCommand(pkg)} && { ${pathCommand(pkg.platform)} ; } && ~/.local/bin/${COMMAND_NAME} ${setupArguments(origin, POSIX_QUOTE)}`;
  }
  return `${installCommand(pkg)}; ${pathCommand(pkg.platform)}; & "$d\\${COMMAND_NAME}.exe" ${setupArguments(origin, POWERSHELL_QUOTE)}`;
}
