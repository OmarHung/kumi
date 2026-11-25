import { createHash } from 'node:crypto';
import { http } from 'msw';
import type { CliLoginApproval, CliLoginCheck, CliLoginRequest } from '../api/cliLogin';
import { failure, ok } from './api';
import { MAX_PORT, MIN_PORT } from '../features/cli-login/cliLoginText';
import { server } from './server';

const CONFIRMATION_PREFIX = 'kumi-cli-login-confirm\n';
const CODE_MODULUS = 1_000_000;
const CODE_LENGTH = 6;

/**
 * 伺服器算確認碼的方法（和 kumi 一樣）：SHA-256(前綴 + challenge) 的前 4 個位元組當大端整數，對 1,000,000 取餘數補零到 6 位。
 * 只放在測試支援裡：正式的網頁不能算得出正確的碼，使用者要從終端機抄。
 */
export function confirmationCodeOf(challenge: string): string {
  const digest = createHash('sha256').update(`${CONFIRMATION_PREFIX}${challenge}`, 'utf8').digest();
  return String(digest.readUInt32BE(0) % CODE_MODULUS).padStart(CODE_LENGTH, '0');
}

/** 使用者輸入的確認碼：去掉空白與 -，剛好 6 個 ASCII 數字且和算出來的一樣才算對（和伺服器一樣）。 */
function confirmationMatches(challenge: string, input: unknown): boolean {
  if (typeof input !== 'string') return false;
  const digits = input.replace(/[\s-]/g, '');
  return /^[0-9]{6}$/.test(digits) && digits === confirmationCodeOf(challenge);
}

/** 電腦名稱裡抽出所有數字之後含有這次的確認碼：伺服器兩個端點都拒絕（422 cli_login.request_invalid，細項路徑 machine）。 */
function machineEmbedsCode(machine: unknown, challenge: string): boolean {
  return typeof machine === 'string' && machine.replace(/\D/g, '').includes(confirmationCodeOf(challenge));
}

function machineRejected() {
  const issue = { code: 'cli_login.request_invalid', path: 'machine', message: '電腦名稱不能帶著確認碼。' };
  return failure(422, issue.code, issue.message, [issue]);
}

/** 瀏覽器登入檢查的結果（假資料）；沒給的欄位是「命令列與本機代理都要、組織允許、四個範圍」。 */
export function cliLoginCheckOf(overrides: Partial<CliLoginCheck> = {}): CliLoginCheck {
  return {
    machineName: 'MacBook-Pro',
    tokens: ['cli', 'agent'],
    scopes: ['items:read', 'items:write', 'blueprint:read'],
    agentScopes: ['agent:run', 'blueprint:read', 'blueprint:write', 'items:read'],
    agentAllowed: true,
    account: { displayName: '範例使用者', email: 'owner@example.com', organizationName: '範例組織' },
    ...overrides,
  };
}

export interface CliLoginBackend {
  readonly checks: CliLoginRequest[];
  readonly approvals: CliLoginApproval[];
}

interface CliLoginOptions {
  readonly check?: CliLoginCheck;
  readonly approveRedirect?: string;
}

/**
 * 假的瀏覽器登入端點：check 回 check（不帶確認碼：網頁要靠使用者從終端機抄；電腦名稱帶了確認碼回 422 cli_login.request_invalid、路徑 machine），approve 先比對確認碼（422 cli_login.confirmation_mismatch，
 * 細項路徑 confirmationCode）再回導回的網址。要失敗的測試用 server.use 蓋掉。
 */
export function serveCliLogin({
  check = cliLoginCheckOf(),
  approveRedirect = 'http://127.0.0.1:45678/kumi-login?code=abc&state=st4te_-1',
}: CliLoginOptions = {}): CliLoginBackend {
  const backend: CliLoginBackend = { checks: [], approvals: [] };
  server.use(
    http.post('/v1/cli-login/check', async ({ request }) => {
      const body = (await request.json()) as CliLoginRequest;
      backend.checks.push(body);
      return machineEmbedsCode(body.machine, body.challenge) ? machineRejected() : ok(check);
    }),
    http.post('/v1/cli-login/approve', async ({ request }) => {
      const approval = (await request.json()) as CliLoginApproval;
      backend.approvals.push(approval);
      if (machineEmbedsCode((approval as { machine?: unknown }).machine, approval.challenge)) return machineRejected();
      if (!confirmationMatches(approval.challenge, approval.confirmationCode)) {
        const issue = { code: 'cli_login.confirmation_mismatch', path: 'confirmationCode', message: '確認碼不對。' };
        return failure(422, issue.code, issue.message, [issue]);
      }
      if ((approval.expiresInDays as number | null) === null) {
        const issue = { code: 'access_token.expiry_invalid', path: 'expiresInDays', message: '期限一定要有天數。' };
        return failure(422, issue.code, issue.message, [issue]);
      }
      if (approval.port < MIN_PORT || approval.port > MAX_PORT) {
        return failure(422, 'cli_login.request_invalid', '連接埠要在 32768 到 65535 之間。');
      }
      return ok({ redirectTo: approveRedirect });
    }),
  );
  return backend;
}

/** approve 失敗：伺服器的代碼與訊息原樣顯示。 */
export function failCliLoginApprove(status: number, code: string, message: string): void {
  server.use(http.post('/v1/cli-login/approve', () => failure(status, code, message)));
}
