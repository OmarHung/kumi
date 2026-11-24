import type { RefObject } from 'react';
import type { ProjectDetail } from '../../../api/types';
import { updateThenFocus, type FocusTarget } from '../../../lib/updateThenFocus';
import { FillFieldsDialog, type FillCloseReason } from '../../item-table/cells/FillFieldsDialog';
import type { FillRequest } from '../../item-table/cells/useFillSubmit';
import { ProjectScope } from './ProjectScope';
import type { MyWorkBoardMoves } from './useMyWorkBoardMoves';

/** 卡片的「移到…」。卡片換欄時元素會重新掛上，所以等對話框關上（畫面更新完）才找，找到的是它現在的位置。 */
function moveButtonOf(itemKey: string): RefObject<HTMLElement | null> {
  return {
    get current() {
      return document.querySelector<HTMLElement>(`[data-card="${itemKey}"] button[aria-haspopup="menu"]`);
    },
  };
}

interface MyWorkMoveFillProps {
  readonly request: FillRequest;
  readonly detail: ProjectDetail;
  readonly moves: MyWorkBoardMoves;
  /** 卡片不在畫面上（條件換了、移到看不到的欄）時，焦點的去處。 */
  readonly focusFallback: readonly FocusTarget[];
}

/**
 * 看板上被「要先填欄位」擋下的移動（docs/DESIGN.md 8.3「補填再前進」）：卡片已經回到原欄，這裡開補填的對話框，
 * 對話框用那個項目自己專案的藍圖與權限；欄位存好之後的改狀態走 useMyWorkBoardMoves（和拖曳、「移到…」同一條路）。
 * 好幾張接連被擋下時一次開一個；最後一個關上之後焦點回到那張卡片的「移到…」。去「成員」時全部收起來。
 */
export function MyWorkMoveFill({ request, detail, moves, focusFallback }: MyWorkMoveFillProps) {
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
  return (
    <ProjectScope detail={detail}>
      <FillFieldsDialog
        request={request}
        transition={moves.moveFromDialog}
        onClose={close}
        onMoved={() => {
          close();
        }}
      />
    </ProjectScope>
  );
}
