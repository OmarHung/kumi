// 一個字就認得出來的文字（中日韓）：頭像只放第一個字。
const WIDE_SCRIPT = /\p{Script=Han}|\p{Script=Hiragana}|\p{Script=Katakana}|\p{Script=Hangul}/u;

/** 第一個字（以碼位計，罕用字不會被切成一半）。 */
function firstCharacter(word: string | undefined): string {
  return word === undefined ? '' : (Array.from(word)[0] ?? '');
}

/**
 * 頭像上的字：中文取第一個字，英文取前兩個單字的字首（大寫）。名稱是空白時用「?」。
 * 英文後面接的是中文時只取英文的字首（「QA 主管」→「Q」），不會混成「Q主」。
 */
export function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter((word) => word !== '');
  const first = firstCharacter(words[0]);
  if (first === '') {
    return '?';
  }
  if (WIDE_SCRIPT.test(first)) {
    return first;
  }
  const second = firstCharacter(words[1]);
  const useSecond = second !== '' && !WIDE_SCRIPT.test(second);
  return `${first}${useSecond ? second : ''}`.toLocaleUpperCase();
}
