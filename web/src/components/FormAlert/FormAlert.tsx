import { Notice } from '../Notice/Notice';

interface FormAlertProps {
  readonly id?: string;
  /** null 時不顯示。 */
  readonly message: string | null;
}

/** 表單上方、對不到單一欄位的錯誤訊息。用 role="alert"，出現時螢幕報讀器會唸出來。長相是共用的 Notice。 */
export function FormAlert({ id, message }: FormAlertProps) {
  if (message === null) {
    return null;
  }
  return (
    <Notice id={id} tone="danger" role="alert">
      {message}
    </Notice>
  );
}
