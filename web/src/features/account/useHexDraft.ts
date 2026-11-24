import { useState, type ChangeEvent, type KeyboardEvent } from 'react';
import { normalizeHex } from '../../lib/hexColor';
import { isCommitEnter } from '../../lib/keyboard';

// 也收三碼的簡寫與不打 #，所以只舉例、不說「6 碼」。
const FORMAT_ERROR = '請輸入像 #1a2b3c 這樣的色碼。';
const FULL_HEX = /^#?[0-9a-f]{6}$/i;

/**
 * 取色器的「色碼」欄位：打到一半的字（還不合格式）與要不要說錯。draft 是 null 時照目前的顏色顯示。
 *
 * 打字中只送出打完的六碼：三碼的簡寫常是打到一半（#00f 之後還要打 #00ff00）或一個字一個字刪的時候經過的樣子，
 * 當場換顏色會閃一下別的顏色；三碼等按 Enter 或離開欄位再算。也是那時候才說錯（打到一半本來就不合格式）。
 * Tab 出去時取色器的浮層跟著關上，打到一半的就丟掉、顏色不變。
 */
export function useHexDraft(value: string, onHex: (hex: string) => void) {
  const [draft, setDraft] = useState<string | null>(null);
  const [hasFormatError, setHasFormatError] = useState(false);

  function finish() {
    if (draft === null) return;
    const hex = normalizeHex(draft);
    if (hex === null) {
      setHasFormatError(true);
      return;
    }
    onHex(hex);
    setDraft(null);
  }

  return {
    /** 碰了滑桿：打到一半的字與錯誤都清掉，欄位換成滑桿選的顏色。 */
    reset: () => {
      setDraft(null);
      setHasFormatError(false);
    },
    fieldProps: {
      value: draft ?? value,
      error: hasFormatError ? FORMAT_ERROR : undefined,
      onChange: (event: ChangeEvent<HTMLInputElement>) => {
        const text = event.target.value;
        setDraft(text);
        setHasFormatError(false);
        const hex = FULL_HEX.test(text.trim()) ? normalizeHex(text) : null;
        if (hex !== null) onHex(hex);
      },
      onBlur: finish,
      onKeyDown: (event: KeyboardEvent<HTMLInputElement>) => {
        if (!isCommitEnter(event)) return;
        event.preventDefault();
        finish();
      },
    },
  };
}
