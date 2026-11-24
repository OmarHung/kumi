/** 設定頁的 key 與名稱的格式，和伺服器的規則一樣（KeyRules、StrictText）。 */

/** 藍圖的 key：小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元。 */
export const BLUEPRINT_KEY_PATTERN = /^[a-z][a-z0-9_]{1,39}$/;

export const NAME_MAX_LENGTH = 100;

export const KEY_HINT = '小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元。送出後不能再改。';

// 藍圖存在 PostgreSQL 的 jsonb，NUL 存不進去；其他控制字元在畫面上也看不到，一起擋下。
const CONTROL_CHARACTER = /\p{Cc}/u;

export function hasControlCharacter(text: string): boolean {
  return CONTROL_CHARACTER.test(text);
}

export function keyFormatError(key: string): string | undefined {
  if (key === '') {
    return '請輸入 key。';
  }
  return BLUEPRINT_KEY_PATTERN.test(key) ? undefined : 'key 要用小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元。';
}

/** 名稱檢查的是去掉前後空白之後的樣子（送出時也會去掉）。 */
export function nameError(name: string): string | undefined {
  const trimmed = name.trim();
  if (trimmed === '') return '請輸入名稱。';
  if (trimmed.length > NAME_MAX_LENGTH) return `名稱最多 ${String(NAME_MAX_LENGTH)} 個字。`;
  return hasControlCharacter(trimmed) ? '名稱不能有控制字元。' : undefined;
}

/** 訊息裡的名稱：去掉前後空白；還沒填時用 key，才看得出是哪一個。 */
export function displayName(entry: { readonly key: string; readonly name: string }): string {
  return entry.name.trim() === '' ? entry.key : entry.name.trim();
}
