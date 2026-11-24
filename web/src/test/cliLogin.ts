import { http } from 'msw';
import type { CliLoginApproval, CliLoginCheck, CliLoginRequest } from '../api/cliLogin';
import { failure, ok } from './api';
import { server } from './server';

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

/** 假的瀏覽器登入端點：check 回 check，approve 回導回的網址。要失敗的測試用 server.use 蓋掉。 */
export function serveCliLogin({
  check = cliLoginCheckOf(),
  approveRedirect = 'http://127.0.0.1:45678/kumi-login?code=abc&state=st4te_-1',
}: CliLoginOptions = {}): CliLoginBackend {
  const backend: CliLoginBackend = { checks: [], approvals: [] };
  server.use(
    http.post('/v1/cli-login/check', async ({ request }) => {
      backend.checks.push((await request.json()) as CliLoginRequest);
      return ok(check);
    }),
    http.post('/v1/cli-login/approve', async ({ request }) => {
      backend.approvals.push((await request.json()) as CliLoginApproval);
      return ok({ redirectTo: approveRedirect });
    }),
  );
  return backend;
}

/** approve 失敗：伺服器的代碼與訊息原樣顯示。 */
export function failCliLoginApprove(status: number, code: string, message: string): void {
  server.use(http.post('/v1/cli-login/approve', () => failure(status, code, message)));
}
