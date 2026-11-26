import type { LocalAgentMachine } from '../../api/aiTypes';
import type { OAuthGrant } from '../../api/oauth';
import type { AccessToken } from '../../api/types';
import { isExpired } from './accessTokenText';

/** 一列的狀態：done 打勾、todo 還沒、muted 這個組織沒有這一項、pending 還在讀、error 讀不到。畫面上一律有文字，不只靠圖示。 */
export type StatusKind = 'done' | 'todo' | 'muted' | 'pending' | 'error';

export interface StatusLine {
  readonly kind: StatusKind;
  readonly text: string;
  /** 本機代理有登記、但沒有一台在線：第三步要接著教怎麼排查。 */
  readonly isOffline?: boolean;
}

function distinct(names: readonly string[]): readonly string[] {
  return [...new Set(names)];
}

/**
 * 命令列：瀏覽器登入建的（有電腦名稱）、不是本機代理用的（範圍沒有 agent:run）、還沒過期的權杖。
 * 同一台電腦重新登入會換掉舊的，但清單可能有兩把一瞬間共存，所以電腦名稱去重。
 */
export function cliLine(tokens: readonly AccessToken[], now: number): StatusLine {
  const machines = distinct(
    tokens.flatMap((token) =>
      token.machineName !== null && !token.scopes.includes('agent:run') && !isExpired(token, now) ? [token.machineName] : [],
    ),
  );
  return machines.length === 0 ? { kind: 'todo', text: '還沒有' } : { kind: 'done', text: `已登入：${machines.join('、')}` };
}

/** Claude Code／Codex：OAuth 授權清單裡的應用名稱（去重）。 */
export function agentsLine(grants: readonly OAuthGrant[]): StatusLine {
  const apps = distinct(grants.map((grant) => grant.clientName));
  return apps.length === 0 ? { kind: 'todo', text: '還沒有' } : { kind: 'done', text: `已授權：${apps.join('、')}` };
}

/**
 * 本機代理：在線的電腦名稱；有登記但都離線；沒有。組織不允許又沒有登記過的電腦時整列是淡色的「組織沒有開放」
 * （已經有電腦的照列：那些電腦還在，只是不能再新增）。
 */
export function localAgentLine(machines: readonly LocalAgentMachine[], isAllowed: boolean): StatusLine {
  if (machines.length === 0) return isAllowed ? { kind: 'todo', text: '還沒有' } : { kind: 'muted', text: '組織沒有開放' };
  const online = distinct(machines.filter((machine) => machine.online).map((machine) => machine.name));
  return online.length > 0 ? { kind: 'done', text: `在線：${online.join('、')}` } : { kind: 'todo', text: '已登記，目前離線', isOffline: true };
}
