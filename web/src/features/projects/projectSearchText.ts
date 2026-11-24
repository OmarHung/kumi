/** 伺服器的上限：去掉頭尾空白後最多這麼多字，超過是 422（query.invalid）。 */
export const PROJECT_SEARCH_MAX_LENGTH = 200;

/** C0（含換行、NUL）、DEL 與 C1 控制字元：伺服器一律擋下，送出去必定 422。用字碼判斷，不必關掉 lint 的 no-control-regex。 */
function isControlCharacter(character: string): boolean {
  const code = character.charCodeAt(0);
  return code <= 0x1f || (code >= 0x7f && code <= 0x9f);
}

/**
 * 把輸入或網址 `?q=` 的字整理成可以送給專案搜尋的樣子：拿掉控制字元、去頭尾空白、超過上限的截掉。
 * 不整理的話，貼進來的長字或手改的網址會送出一個一定 422 的請求，整張清單換成錯誤。
 */
export function normalizeProjectSearchText(raw: string): string {
  const withoutControls = Array.from(raw)
    .filter((character) => !isControlCharacter(character))
    .join('');
  return withoutControls.trim().slice(0, PROJECT_SEARCH_MAX_LENGTH).trim();
}
