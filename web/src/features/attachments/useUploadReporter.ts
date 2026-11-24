import { useCallback, useEffect, useRef, useState } from 'react';

/** 即時區域剛掛上時內容要晚一點才填：先掛空的，報讀軟體才認得它是即時區域、唸得出之後填進來的字。 */
const ANNOUNCER_MOUNT_DELAY_MS = 100;

/** 完成與失敗晚一點才唸：同一個空檔裡好幾個一起結束時合成一句，不互相蓋掉。 */
const MERGE_WINDOW_MS = 150;

export interface UploadReporter {
  /** 要使用者處理的訊息，寫在文字框下面；累加，由使用者關掉（下一次的訊息不蓋掉前面的）。 */
  readonly problems: readonly string[];
  readonly announcement: string;
  /** 用過上傳了：即時區域這時才掛上（見 EditorUploads.hasAnnouncer）。 */
  readonly isStarted: boolean;
  readonly showProblems: (messages: readonly string[]) => void;
  /** 馬上唸（被擋下、開始上傳）。 */
  readonly say: (text: string) => void;
  /** 完成與失敗：等一下再唸，同時結束的合成一句（用句號接起來）。 */
  readonly sayLater: (text: string) => void;
  readonly dismiss: () => void;
}

/** 上傳的訊息：文字框下面的提示（累加）與給報讀軟體的一句話。 */
export function useUploadReporter(): UploadReporter {
  const [problems, setProblems] = useState<readonly string[]>([]);
  const [announcement, setAnnouncement] = useState('');
  const [isStarted, setIsStarted] = useState(false);
  const startedRef = useRef(false);
  const pending = useRef<readonly string[]>([]);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const mountTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const say = useCallback((text: string) => {
    if (startedRef.current) {
      setAnnouncement(text);
      return;
    }
    startedRef.current = true;
    setIsStarted(true);
    if (text === '') return;
    // 才剛掛上：晚一點填；不能蓋掉在這之間說的別句。
    mountTimer.current = setTimeout(() => {
      setAnnouncement((current) => (current === '' ? text : current));
    }, ANNOUNCER_MOUNT_DELAY_MS);
  }, []);

  const sayLater = useCallback(
    (text: string) => {
      pending.current = [...pending.current, text];
      clearTimeout(timer.current);
      timer.current = setTimeout(() => {
        const merged = pending.current.join('。');
        pending.current = [];
        say(merged);
      }, MERGE_WINDOW_MS);
    },
    [say],
  );

  useEffect(
    () => () => {
      // 卸載之後兩個計時器都不能再改狀態（關掉編輯器時剛好有一句還沒唸）。
      clearTimeout(timer.current);
      clearTimeout(mountTimer.current);
    },
    [],
  );

  const showProblems = useCallback((messages: readonly string[]) => {
    if (messages.length === 0) return;
    // 完全一樣的不重複列（反覆貼上同一個被擋下的原因）。
    setProblems((current) => [...current, ...messages.filter((message) => !current.includes(message))]);
  }, []);

  const dismiss = useCallback(() => {
    setProblems([]);
  }, []);

  return { problems, announcement, isStarted, showProblems, say, sayLater, dismiss };
}
