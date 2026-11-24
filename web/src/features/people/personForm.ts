import type { NewPersonRequest, OrganizationRole, SessionUser } from '../../api/types';
import type { FieldErrors, FieldMapping } from '../../lib/formErrors';

/** 和後端 IdentityRules 相同；前端先檢查只是為了早一點告訴使用者，最後仍以後端為準。 */
export const PASSWORD_MIN_LENGTH = 10;
export const PASSWORD_MAX_LENGTH = 128;
export const DISPLAY_NAME_MAX_LENGTH = 100;
export const EMAIL_MAX_LENGTH = 254;

export type PersonField = 'displayName' | 'email' | 'password' | 'role';

export const PERSON_FIELDS: readonly PersonField[] = ['displayName', 'email', 'password', 'role'];

export const PERSON_ERROR_MAPPING: FieldMapping<PersonField> = {
  fields: PERSON_FIELDS,
  codes: {
    'person.name_required': 'displayName',
    'person.name_too_long': 'displayName',
    'person.email_invalid': 'email',
    'person.email_taken': 'email',
    'person.password_too_short': 'password',
    'person.password_too_long': 'password',
  },
};

export const PASSWORD_HINT = `${String(PASSWORD_MIN_LENGTH)} 到 ${String(PASSWORD_MAX_LENGTH)} 個字元。請用別處沒用過的密碼，交給對方後請他自己記好。`;

/** 擁有者只能由擁有者新增；其他組織管理員的選單裡不出現。 */
export function assignableRoles(user: SessionUser): readonly OrganizationRole[] {
  const roles: readonly OrganizationRole[] = ['owner', 'admin', 'member', 'guest'];
  return user.role === 'owner' ? roles : roles.filter((role) => role !== 'owner');
}

/**
 * 密碼長度的問題；沒有問題是 undefined。noun 是欄位的名稱（「密碼」「新密碼」），訊息才對得上欄位的標籤。
 * 新增人員、重設別人的密碼、改自己的密碼共用。
 */
export function passwordLengthError(password: string, noun = '密碼'): string | undefined {
  if (password.length < PASSWORD_MIN_LENGTH) {
    return `${noun}至少要 ${String(PASSWORD_MIN_LENGTH)} 個字元。`;
  }
  return password.length > PASSWORD_MAX_LENGTH
    ? `${noun}不能超過 ${String(PASSWORD_MAX_LENGTH)} 個字元。`
    : undefined;
}

function emailError(email: string): string | undefined {
  if (email === '') {
    return '請輸入 Email。';
  }
  return email.includes('@') ? undefined : 'Email 的格式不正確。';
}

/** 回傳每個欄位的問題；沒有問題的欄位是 undefined。 */
export function validatePerson(request: NewPersonRequest): FieldErrors<PersonField> {
  return {
    displayName: request.displayName === '' ? '請輸入名稱。' : undefined,
    email: emailError(request.email),
    password: passwordLengthError(request.password),
  };
}
