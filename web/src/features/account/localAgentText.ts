import type { AccessTokenScope } from '../../api/types';
import type { LocalAgentEngineSummary, LocalAgentMachine } from '../../api/aiTypes';
import { COMMAND_NAME } from '../../config/product';
import { formatDate, toCalendarDate } from '../../domain/dates';
import { formatRelativeTime } from '../../domain/time';
import { OPTION_LABELS } from '../ai/aiLabels';
import { quotedOrigin } from './cliText';

/** 本機代理用的權杖：`agent:run` 領工作，後三個給引擎的 MCP 讀設定、提出變更集、讀項目（internal/HANDOFF-local-agent.md 第 2 節）。 */
export const LOCAL_AGENT_SCOPES: readonly AccessTokenScope[] = ['agent:run', 'blueprint:read', 'blueprint:write', 'items:read'];
export const LOCAL_AGENT_TOKEN_DAYS = 90;

/** 項目的讀取是後來加的範圍：之前建立的權杖沒有它。 */
export const OLD_TOKEN_NOTE = '之前建立的權杖讀不到項目，要讀項目請重新建立一把。';

/** 兩個引擎的名稱：說明裡整段不斷行（左欄窄，原本會在斜線的地方折成兩行）。 */
export const LOCAL_ENGINES = 'Claude Code／Codex';
export const LOCAL_AGENT_NOTE_BEFORE = '只有你能用。用你電腦上已經登入的 ';
export const LOCAL_AGENT_NOTE_AFTER = '，可以是你自己的訂閱（Claude、ChatGPT），費用照你的訂閱或帳號算。它只能讀這個專案的設定與項目、提出變更集與修改建議，不能碰你電腦上的檔案。';

/** 權杖的名稱：加上建立的日期，清單裡才分得出新舊。 */
export function localAgentTokenName(now: Date): string {
  return `本機代理 ${formatDate(toCalendarDate(now))}`;
}

/** 本機代理自己的登入（存的是另一把權杖，不會蓋掉命令列的）：瀏覽器按「允許」。 */
export function loginCommand(origin: string): string | null {
  const server = quotedOrigin(origin);
  return server === null ? null : `${COMMAND_NAME} agent login --server ${server}`;
}

/** 沒辦法開瀏覽器（透過 SSH）時：貼上網頁建的本機代理權杖。 */
export function pasteLoginCommand(origin: string): string | null {
  const login = loginCommand(origin);
  return login === null ? null : `${login} --paste`;
}

/** 登入電腦時自動在背景啟動（macOS、Windows）。 */
export const ENABLE_COMMAND = `${COMMAND_NAME} agent enable`;

/** 在這個終端機視窗啟動，視窗要一直開著。 */
export const START_COMMAND = `${COMMAND_NAME} agent start`;

/** 「Claude Code 2.1.330」。 */
export function engineText(engine: LocalAgentEngineSummary): string {
  return `${OPTION_LABELS[engine.kind]} ${engine.version}`;
}

export function enginesText(machine: LocalAgentMachine): string {
  return machine.engines.length === 0 ? '沒有可用的引擎' : machine.engines.map(engineText).join('、');
}

/** 文字說明在不在線，不靠顏色。 */
export function onlineText(machine: LocalAgentMachine, now: number): string {
  if (machine.online) return '在線';
  return `離線（最後在 ${formatRelativeTime(machine.lastSeenAt, now)}）`;
}
