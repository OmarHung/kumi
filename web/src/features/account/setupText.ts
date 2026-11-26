import type { CliPackage } from '../../api/cli';
import { COMMAND_NAME, PRODUCT_NAME } from '../../config/product';
import { installCommand, isMac, pathCommand, quotedOrigin } from './cliText';

/** 設定精靈三步的標題（畫面與測試都用這一份）。 */
export const STEP_TITLES = {
  download: `下載 ${COMMAND_NAME}`,
  command: '在終端機貼上這一行',
  status: '完成的項目',
} as const;

export const WIZARD_INTRO = '一次設定好三件事：';

/** 精靈設定的三件事，名稱和第三步「完成的項目」的標籤同一套，後面一句說它是什麼（兩項都有 Claude Code／Codex、方向相反）。 */
export const WIZARD_ITEMS: readonly string[] = [
  '命令列工具',
  `在 AI 工具裡用 ${PRODUCT_NAME}：在你的 Claude Code／Codex 裡叫 ${PRODUCT_NAME} 的工具`,
  '本機代理：讓網頁的 AI 助理用你電腦上的 Claude Code／Codex',
];

export const COMMAND_NOTE = '會問你要設定哪些，再開瀏覽器請你按「允許」（Claude Code、Codex 各會再開一次授權頁）。';

export const STATUS_NOTE = '在終端機做完之後，這裡會自己打勾，不必重新整理。';

/** 網址用雙引號包起來；形狀不合（含引號、空白等）的不產生指令。 */
function setupArguments(origin: string): string | null {
  const server = quotedOrigin(origin);
  return server === null ? null : `setup --server ${server}`;
}

/** 已經裝好 kumi 的：直接啟動精靈。 */
export function setupCommand(origin: string): string | null {
  const args = setupArguments(origin);
  return args === null ? null : `${COMMAND_NAME} ${args}`;
}

/**
 * 一行做完安裝、加進 PATH、啟動精靈（internal/HANDOFF-setup-wizard.md 5.2）。用完整路徑啟動：這個終端機的 PATH 還沒有剛加的資料夾。
 * macOS 的 PATH 指令是「grep 找不到才寫入」的 `||`，包在大括號裡才不會吃掉前面的 `&&`；Windows 的 pathCommand 重設 $d（和安裝時同一個值），
 * 最後的 `& "$d\\kumi.exe"` 才找得到。
 */
export function oneLineCommand(pkg: CliPackage, origin: string): string | null {
  const args = setupArguments(origin);
  if (args === null) return null;
  if (isMac(pkg.platform)) {
    return `${installCommand(pkg)} && { ${pathCommand(pkg.platform)} ; } && ~/.local/bin/${COMMAND_NAME} ${args}`;
  }
  return `${installCommand(pkg)}; ${pathCommand(pkg.platform)}; & "$d\\${COMMAND_NAME}.exe" ${args}`;
}
