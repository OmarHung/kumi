import { http } from 'msw';
import type { Person, SessionUser } from '../api/types';
import { failure, ok } from './api';
import { exampleMember, exampleOwner } from './fixtures';
import { server } from './server';

/** 登入的人在人員清單裡的樣子（GET /v1/people 回的形狀）。 */
export function personOf(user: SessionUser, overrides: Partial<Person> = {}): Person {
  return {
    personId: user.personId,
    displayName: user.displayName,
    email: user.email,
    role: user.role,
    disabled: false,
    ...overrides,
  };
}

export const ownerPerson: Person = personOf(exampleOwner);
export const memberPerson: Person = personOf(exampleMember);
export const adminPerson: Person = {
  personId: '0190a000-0000-7000-8000-000000000003',
  displayName: '範例管理員',
  email: 'admin@example.com',
  role: 'admin',
  disabled: false,
};
export const guestPerson: Person = {
  personId: '0190a000-0000-7000-8000-000000000005',
  displayName: '範例訪客',
  email: 'guest@example.com',
  role: 'guest',
  disabled: false,
};

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const MIN_PASSWORD = 10;
export const MAX_PASSWORD = 128;

type Action = 'disable' | 'enable' | 'password' | 'role';

/** 訊息照後端（src/Modules/PmTool.Identity/AccountAdministration.cs）：停用、重新啟用、重設密碼各自一套。 */
const MESSAGES: Readonly<Record<Action, { readonly notAdmin: string; readonly ownerOnly: string; readonly self: string }>> = {
  disable: {
    notAdmin: '只有組織的擁有者或管理員可以停用帳號。',
    ownerOnly: '只有擁有者可以停用擁有者或管理員的帳號。',
    self: '不能停用自己的帳號。',
  },
  enable: {
    notAdmin: '只有組織的擁有者或管理員可以重新啟用帳號。',
    ownerOnly: '只有擁有者可以重新啟用擁有者或管理員的帳號。',
    self: '不能重新啟用自己的帳號。',
  },
  password: {
    notAdmin: '只有組織的擁有者或管理員可以重設密碼。',
    ownerOnly: '只有擁有者可以重設擁有者或管理員的密碼。',
    self: '要改自己的密碼，請到「我的帳號」。',
  },
  role: {
    notAdmin: '只有組織的擁有者或管理員可以變更角色。',
    ownerOnly: '只有擁有者可以設定或變更擁有者、管理員的角色。',
    self: '不能變更自己的角色。',
  },
};

const ORGANIZATION_ROLES: readonly string[] = ['owner', 'admin', 'member', 'guest'];

/** 密碼長度不對時的 422：細項路徑是 newPassword（和真的後端一樣）。沒問題回 null。 */
export function passwordLengthFailure(password: string) {
  if (password.length < MIN_PASSWORD) {
    const message = `密碼至少要 ${String(MIN_PASSWORD)} 個字元。`;
    return failure(422, 'person.password_too_short', message, [{ code: 'person.password_too_short', path: 'newPassword', message }]);
  }
  if (password.length > MAX_PASSWORD) {
    const message = `密碼不能超過 ${String(MAX_PASSWORD)} 個字元。`;
    return failure(422, 'person.password_too_long', message, [{ code: 'person.password_too_long', path: 'newPassword', message }]);
  }
  return null;
}

export interface PeopleRequest {
  readonly action: Action;
  readonly personId: string;
  readonly body: unknown;
}

export interface PeopleBackend {
  /** 停用、重新啟用、重設密碼、改角色收到的請求（照收到的順序；被擋下來的也算）。 */
  readonly requests: PeopleRequest[];
  /** 現在的清單（停用、重新啟用之後的樣子）。 */
  readonly people: () => readonly Person[];
}

interface PeopleBackendOptions {
  /** 登入的人：權限照他的組織角色判斷。 */
  readonly actor: SessionUser;
  readonly people: readonly Person[];
  /** 沒有登入帳號的人：停用、重新啟用、重設密碼都是 409 person.no_account。 */
  readonly withoutAccount?: readonly string[];
}

type Check = ReturnType<typeof failure> | null;

/**
 * 權限的規則（第 2 節第 3、4 點），檢查的順序和後端一樣：不是擁有者或管理員 → 自己 → 組織裡沒有這個人
 * （路徑不是 GUID 也是）→ 管理員動擁有者或管理員 → 沒有登入帳號。
 */
function checkRules(options: PeopleBackendOptions, people: readonly Person[], personId: string, action: Action): Check {
  const { actor, withoutAccount = [] } = options;
  const messages = MESSAGES[action];
  if (actor.role !== 'owner' && actor.role !== 'admin') return failure(403, 'person.forbidden', messages.notAdmin);
  if (personId === actor.personId) return failure(409, 'person.is_self', messages.self);
  const target = GUID.test(personId) ? people.find((person) => person.personId === personId) : undefined;
  if (target === undefined) return failure(404, 'person.not_found', '組織裡找不到這個人。');
  if (actor.role === 'admin' && (target.role === 'owner' || target.role === 'admin')) {
    return failure(403, 'person.forbidden', messages.ownerOnly);
  }
  // 改角色是組織裡的事，沒有登入帳號的人也能改。
  if (action !== 'role' && withoutAccount.includes(target.personId)) return failure(409, 'person.no_account', '這個人沒有登入帳號。');
  return null;
}

async function bodyOf(request: Request): Promise<unknown> {
  const text = await request.text();
  return text === '' ? undefined : (JSON.parse(text) as unknown);
}

/**
 * 假的人員端點，照合約（internal/HANDOFF-security-local-ops.md 第 4 節）：GET /v1/people 回現在的清單；
 * POST /v1/people/{id}/disable、/enable 與 PUT /v1/people/{id}/password、/role 照權限的規則回 403、404、409，
 * 重設密碼另外檢查長度（422，細項路徑 newPassword）。成功時回那個人（停用之後 disabled 是 true）。
 */
export function servePeopleAdmin(options: PeopleBackendOptions): PeopleBackend {
  let people = options.people;
  const requests: PeopleRequest[] = [];

  function setDisabled(personId: string, disabled: boolean) {
    people = people.map((person) => (person.personId === personId ? { ...person, disabled } : person));
    return people.find((person) => person.personId === personId) ?? null;
  }

  /** 改角色：管理員不能把人設成擁有者或管理員（目前或要改成，只有擁有者能動）；角色不認得是 422；成功回新的樣子。 */
  function changeRole(personId: string, body: unknown) {
    const role = (body as { role?: unknown } | undefined)?.role;
    if (typeof role !== 'string' || !ORGANIZATION_ROLES.includes(role)) return failure(422, 'person.role_unknown', '不認得這個角色。');
    if (options.actor.role === 'admin' && (role === 'owner' || role === 'admin')) {
      return failure(403, 'person.forbidden', MESSAGES.role.ownerOnly);
    }
    people = people.map((person) => (person.personId === personId ? { ...person, role: role as Person['role'] } : person));
    return ok(people.find((person) => person.personId === personId) ?? null);
  }

  async function handle(action: Action, personId: string, request: Request) {
    const body = await bodyOf(request);
    requests.push({ action, personId, body });
    const blocked = checkRules(options, people, personId, action);
    if (blocked !== null) return blocked;
    if (action === 'role') return changeRole(personId, body);
    if (action !== 'password') return ok(setDisabled(personId, action === 'disable'));
    const password = (body as { newPassword?: unknown } | undefined)?.newPassword;
    const lengthProblem = passwordLengthFailure(typeof password === 'string' ? password : '');
    return lengthProblem ?? ok(people.find((person) => person.personId === personId) ?? null);
  }

  server.use(
    http.get('/v1/people', () => ok(people)),
    http.post('/v1/people/:personId/disable', ({ params, request }) => handle('disable', String(params.personId), request)),
    http.post('/v1/people/:personId/enable', ({ params, request }) => handle('enable', String(params.personId), request)),
    http.put('/v1/people/:personId/password', ({ params, request }) => handle('password', String(params.personId), request)),
    http.put('/v1/people/:personId/role', ({ params, request }) => handle('role', String(params.personId), request)),
  );
  return { requests, people: () => people };
}
