import { apiRequest } from './http';
import type { NewPersonRequest, Person, ResetPasswordRequest, SetPersonProfileRequest, SetPersonRoleRequest } from './types';

export function listPeople(signal?: AbortSignal): Promise<readonly Person[]> {
  return apiRequest<readonly Person[]>('/v1/people', { signal });
}

/** 只有組織的擁有者與管理員可以。失敗代碼見 IdentityErrorCodes（person.email_taken 等）。 */
export function createPerson(request: NewPersonRequest): Promise<Person> {
  return apiRequest<Person>('/v1/people', { method: 'POST', body: request });
}

function personPath(personId: string, action: string): string {
  return `/v1/people/${encodeURIComponent(personId)}/${action}`;
}

/**
 * 停用一個人的帳號：他所有的登入立刻作廢、不能再登入，但照樣在人員清單裡。回傳停用之後的他（disabled: true）。
 * 失敗：403 person.forbidden（不是擁有者或管理員、管理員動擁有者或管理員）、404 person.not_found、
 * 409 person.is_self（自己）／person.no_account（沒有帳號）。
 */
export function disablePerson(personId: string): Promise<Person> {
  return apiRequest<Person>(personPath(personId, 'disable'), { method: 'POST' });
}

/** 重新啟用帳號，之後可以再登入。失敗的種類和 disablePerson 一樣。 */
export function enablePerson(personId: string): Promise<Person> {
  return apiRequest<Person>(personPath(personId, 'enable'), { method: 'POST' });
}

/**
 * 替別人設一組新密碼：他在每個裝置上的登入都作廢。失敗的種類和 disablePerson 一樣，另外是 422 的密碼長度
 * （person.password_too_short／person.password_too_long，細項路徑 newPassword）。
 */
export function resetPersonPassword(personId: string, request: ResetPasswordRequest): Promise<Person> {
  return apiRequest<Person>(personPath(personId, 'password'), { method: 'PUT', body: request });
}

/**
 * 改一個人在組織裡的角色（`PUT /v1/people/{personId}/role`，`{ role }`）。已經是那個角色時照樣成功。
 * 403 person.forbidden（不是擁有者或管理員；或動到擁有者、管理員而自己不是擁有者）、409 person.is_self（不能改自己的）、
 * 404 person.not_found。成功後那個人下一個請求就是新的角色（登入不作廢）。
 */
export function setPersonRole(personId: string, request: SetPersonRoleRequest): Promise<Person> {
  return apiRequest<Person>(personPath(personId, 'role'), { method: 'PUT', body: request });
}

/**
 * 設定一個人的職稱與負責範圍（`PUT /v1/people/{personId}/profile`）：只有組織的擁有者與管理員，可以改任何人（包括自己）。
 * 403 person.forbidden、404 person.not_found、422 person.profile_invalid（細項路徑 jobTitle、responsibilities）。
 */
export function setPersonProfile(personId: string, request: SetPersonProfileRequest): Promise<Person> {
  return apiRequest<Person>(personPath(personId, 'profile'), { method: 'PUT', body: request });
}
