import { useEffect, useState } from 'react';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';

export const RESTORED_DRAFT_TEXT = '已還原上次沒送出的內容。';

/** 還原的草稿是在舊的內容上打的（別人之後改過）：換成警告，「捨棄」說成看得到結果的動作。 */
export interface OutdatedDraft {
  readonly message: string;
  /** 看得到的按鈕文字，例如「看伺服器上的內容」。名稱是「<這段文字>（捨棄<對象>）」。 */
  readonly discardText: string;
}

interface RestoredDraftNoticeProps {
  /** 「捨棄」的對象，例如「沒送出的留言」（名稱是「捨棄沒送出的留言」）：留言與說明同時還原時，兩顆按鈕的名稱才分得開。 */
  readonly target: string;
  readonly onDiscard: () => void;
  readonly outdated?: OutdatedDraft | null;
  readonly className?: string;
}

/** 掛上之後下一輪才是 true：和區域一起出現的文字，報讀軟體常常不唸。 */
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
 * 回到項目時還原了上次沒送出的內容（docs/DESIGN.md 8.3「草稿」）：旁邊說一聲，並給「捨棄」。
 * 區域先掛著、下一輪才填文字；「捨棄」跟著文字一起出現，看不見的區域裡不會有停得到的按鈕。
 * 草稿是在舊的內容上打的（outdated）時是警告：說儲存會蓋掉新的內容，按鈕說成「看伺服器上的內容」（就是捨棄）。
 * 開著時才知道別人改過（背景重抓）也會換成警告，同一個區域換字，報讀軟體照樣唸得到。
 */
export function RestoredDraftNotice({ target, onDiscard, outdated = null, className }: RestoredDraftNoticeProps) {
  const isFilled = useFilledLater();
  // 名稱以看得到的字開頭（說得出看到的字就按得到），帶上對象。
  const label = outdated === null ? '捨棄' : outdated.discardText;
  const name = outdated === null ? `捨棄${target}` : `${outdated.discardText}（捨棄${target}）`;
  const discard = (
    <Button size="sm" variant="ghost" aria-label={name} onClick={onDiscard}>
      {label}
    </Button>
  );
  const message = outdated?.message ?? RESTORED_DRAFT_TEXT;
  return (
    <Notice tone={outdated === null ? 'info' : 'warning'} role="status" className={className} actions={isFilled ? discard : undefined}>
      {isFilled ? message : ''}
    </Notice>
  );
}
