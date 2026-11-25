import type { AccountNotice, AccountNoticeToken } from '../../api/notificationTypes';
import { SCOPE_OPTIONS } from '../account/accessTokenText';

/** 帳號通知連到的頁面：「設定 → 命令列與整合」，撤銷權杖與授權都在那裡。 */
export const ACCOUNT_NOTICE_PATH = '/account/integrations';

/** 收件匣的列與提示的第二行：這一類通知的名字（項目的通知這個位置是項目的編號與標題）。 */
export const ACCOUNT_NOTICE_LABEL = '帳號安全';

const SCOPE_LABELS: ReadonlyMap<string, string> = new Map(SCOPE_OPTIONS.map((option) => [option.scope, option.label]));

/** 範圍的說法和「存取權杖」頁一樣；不認得的（之後新增的）照原名，不讓人看不到有這一項。 */
export function scopeText(scopes: readonly string[]): string {
  return scopes.map((scope) => SCOPE_LABELS.get(scope) ?? scope).join('、');
}

function tokenText({ name, scopes }: AccountNoticeToken): string {
  return scopes.length === 0 ? `「${name}」` : `「${name}」（${scopeText(scopes)}）`;
}

function tokensSentence(account: AccountNotice): string {
  const [only] = account.tokens;
  return account.tokens.length === 1 && only !== undefined ? `建立了存取權杖${tokenText(only)}` : `建立了 ${String(account.tokens.length)} 把存取權杖`;
}

function eventSentence(account: AccountNotice): string {
  switch (account.event) {
    case 'access_token_created':
      return tokensSentence(account);
    case 'cli_login_tokens_issued': {
      const created = `建立了 ${String(account.tokens.length)} 把存取權杖`;
      return account.machineName === null ? `命令列登入，${created}` : `命令列在「${account.machineName}」登入，${created}`;
    }
    case 'oauth_app_authorized':
      return `授權了「${account.clientName ?? 'AI 用戶端'}」連到你的帳號`;
    case 'cli_login_code_reused':
      return `登入碼被重複使用，已撤銷 ${String(account.revokedTokens)} 把存取權杖`;
    case 'local_agent_machine_added':
      return account.machineName === null ? '本機代理第一次從新的電腦登記' : `本機代理第一次從新的電腦「${account.machineName}」登記`;
  }
}

/** 一句話（沒有做事的人：做事的就是你自己）：「建立了存取權杖「CI」（讀取項目、修改項目），從 203.0.113.5」。 */
export function accountNoticeSentence(account: AccountNotice): string {
  const sentence = eventSentence(account);
  return account.sourceAddress === null ? sentence : `${sentence}，從 ${account.sourceAddress}`;
}
