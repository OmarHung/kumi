import { useEffect } from 'react';

/** 有還沒送出的修改時，重新整理或關掉分頁用瀏覽器自己的提醒（內容由瀏覽器決定）。站內換頁另外用 React Router 的 blocker 問。 */
export function useBeforeUnloadWarning(shouldWarn: boolean): void {
  useEffect(() => {
    if (!shouldWarn) {
      return undefined;
    }
    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener('beforeunload', warn);
    return () => {
      window.removeEventListener('beforeunload', warn);
    };
  }, [shouldWarn]);
}
