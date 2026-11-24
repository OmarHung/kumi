import { ClientErrorCodes, errorMessage, isApiError } from '../../api/http';

const HTTP_PAYLOAD_TOO_LARGE = 413;

/**
 * 匯入失敗給使用者看的說明。413（請求太大）照伺服器的訊息，再補一句怎麼辦；
 * 沒有信封的 413（代理伺服器擋的）沒有可用的訊息，換成固定的說法。
 */
export function importErrorMessage(error: unknown): string {
  if (!isApiError(error) || error.status !== HTTP_PAYLOAD_TOO_LARGE) return errorMessage(error);
  const message = error.code === ClientErrorCodes.invalidResponse ? '匯入的資料太大，伺服器收不下。' : error.message;
  return `${message}請把檔案分成幾次匯入。`;
}
