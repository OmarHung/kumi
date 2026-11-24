import { useEffect, useState } from 'react';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';

/** 預設的對象是項目的「這段說明」；知識庫的頁面傳「這一頁」。 */
const DEFAULT_SUBJECT = '這段說明';

export const DESCRIPTION_CHANGED_TEXT = `${DEFAULT_SUBJECT}在你編輯時被別人改過了。`;

/**
 * changed：編輯中重抓到的說明版本變了（即時更新或背景重抓帶來的），不打斷編輯，提示並讓使用者選。
 * conflict：儲存時伺服器回 409（item.description_conflict），什麼都沒改；自己的內容還在編輯器裡。
 */
export type DescriptionConflict = 'changed' | 'conflict';

interface DescriptionConflictNoticeProps {
  readonly conflict: DescriptionConflict;
  /** 看新的內容：捨棄自己的修改，換成伺服器上現在的，留在編輯中。 */
  readonly onReload: () => void;
  /** changed：關掉提示繼續編輯。 */
  readonly onKeepEditing: () => void;
  /** conflict：用現在的版本再送一次，蓋掉別人的修改。 */
  readonly onOverwrite: () => void;
  readonly isBusy: boolean;
  /** 被別人改過的是什麼：項目說明是「這段說明」（預設），知識庫的頁面是「這一頁」。 */
  readonly subject?: string;
}

/** 掛上之後下一輪才是 true：和區域一起出現的文字，報讀軟體常常不唸（和 RestoredDraftNotice 一樣）。 */
function useFilledLater(): boolean {
  const [isFilled, setIsFilled] = useState(false);
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setIsFilled(true);
    }, 0);
    return () => {
      window.clearTimeout(timer);
    };
  }, []);
  return isFilled;
}

/**
 * 說明的編輯衝突（docs/DESIGN.md 8.3「說明」）：警告的提示放在編輯器上方，和 RestoredDraftNotice 同一個位置與做法，
 * 出現、消失只多一個提示的高度，推動的只有下面的編輯器。區域先掛著、下一輪才填文字與動作（報讀軟體才唸得到）。
 * 兩種動作：changed 是「看新的內容」「繼續編輯」，conflict 是「看新的內容」「用我的版本覆蓋」。
 */
export function DescriptionConflictNotice({ conflict, onReload, onKeepEditing, onOverwrite, isBusy, subject = DEFAULT_SUBJECT }: DescriptionConflictNoticeProps) {
  const isFilled = useFilledLater();
  // aria-disabled：焦點留在按鈕上，報讀軟體仍唸得到；忙的時候按了不做事（連按不重送）。
  const guard = (action: () => void) => () => {
    if (!isBusy) action();
  };
  const actions = (
    <>
      <Button size="sm" variant="ghost" aria-disabled={isBusy || undefined} onClick={guard(onReload)}>
        看新的內容
      </Button>
      {conflict === 'conflict' ? (
        <Button size="sm" variant="ghost" aria-disabled={isBusy || undefined} onClick={guard(onOverwrite)}>
          用我的版本覆蓋
        </Button>
      ) : (
        <Button size="sm" variant="ghost" onClick={onKeepEditing}>
          繼續編輯
        </Button>
      )}
    </>
  );
  return (
    <Notice tone="warning" role="status" actions={isFilled ? actions : undefined}>
      {isFilled ? `${subject}在你編輯時被別人改過了。` : ''}
    </Notice>
  );
}
