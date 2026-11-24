import type { RefObject } from 'react';
import { FillFieldsDialog, type FillCloseReason } from '../item-table/cells/FillFieldsDialog';
import type { FillRequest } from '../item-table/cells/useFillSubmit';
import { updateThenFocus, type FocusTarget } from '../../lib/updateThenFocus';
import type { BoardMoves } from './useBoardMoves';

/**
 * 卡片的「移到…」。卡片換欄時元素會重新掛上，所以等對話框關上（畫面更新完）才找，找到的是它現在的位置。
 */
function moveButtonOf(itemKey: string): RefObject<HTMLElement | null> {
  return {
    get current() {
      return document.querySelector<HTMLElement>(`[data-card="${itemKey}"] button[aria-haspopup="menu"]`);
    },
  };
}

interface MoveFillProps {
  readonly request: FillRequest;
  readonly moves: BoardMoves;
  /** 卡片不在畫面上（條件換了、移到看不到的欄）時，焦點的去處：工具列的搜尋框（窄螢幕是「篩選」）。 */
  readonly focusFallback: readonly FocusTarget[];
}

/**
 * 看板上被「要先填欄位」擋下的移動（docs/DESIGN.md 8.3「補填再前進」）：卡片已經回到原欄，這裡開補填的對話框；
 * 欄位存好之後的改狀態走 useBoardMoves（和拖曳、「移到…」同一條路），成功了卡片才移過去。
 * 好幾張接連被擋下時一次開一個：後面還有的，關上之後接著開下一個（新的對話框自己拿焦點）；最後一個關上（取消或移好了）
 * 之後焦點回到那張卡片的「移到…」：拖曳打開的時候焦點不在卡片上，對話框自己放不回去。
 * 去「成員」時全部收起來（還沒開的改成看板上方的說明），焦點先放回卡片，成員對話框關上後回到這裡。
 */
export function MoveFill({ request, moves, focusFallback }: MoveFillProps) {
  const backToCard = [moveButtonOf(request.item.key), ...focusFallback];
  const close = (reason?: FillCloseReason) => {
    if (reason === 'members') {
      updateThenFocus(moves.leaveFills, backToCard);
    } else if (moves.hasMoreFills) {
      moves.closeFill();
    } else {
      updateThenFocus(moves.closeFill, backToCard);
    }
  };
  return <FillFieldsDialog request={request} transition={moves.moveFromDialog} onClose={close} onMoved={() => { close(); }} />;
}
