import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useLocation } from 'react-router';
import { removeUndoFromHistory } from './historyState';
import { errorMessage } from '../../api/http';
import { bulkRestoreItems } from '../../api/itemRecovery';
import { Button } from '../../components/Button/Button';
import { ArrivalNotice } from '../../components/StatusViews/StatusViews';
import { undoOf, type ArrivalUndo } from '../../lib/useArrivalNotice';
import { useItemPanelLink } from '../item-page/ItemPanelContext';
import { refreshAfterRestore } from '../item-table/itemCache';
import { itemPagePath } from '../work-items/itemLinks';

/** 按了「復原」之後的結果：同一個位置換成這個。 */
type Outcome =
  | { readonly kind: 'done'; readonly text: string; readonly openKey: string | null }
  /** 沒復原：提示與「復原」照舊留著，下面多一行原因，可以再按一次。 */
  | { readonly kind: 'failed'; readonly text: string };

interface Remembered {
  /** 屬於哪一筆瀏覽紀錄：換到別的網址就不再顯示（和提示本身一樣）。 */
  readonly locationKey: string;
  readonly outcome: Outcome | null;
  readonly isPending: boolean;
}

function OpenLink({ projectKey, itemKey }: { readonly projectKey: string; readonly itemKey: string }) {
  const panelLink = useItemPanelLink(itemKey);
  return (
    <Link to={itemPagePath(projectKey, itemKey)} {...panelLink}>
      {`開啟 ${itemKey}`}
    </Link>
  );
}

/** 復原的結果寫成一句話：一個項目帶編號與標題，多個只說幾個；沒復原的說原因（第一個）。 */
async function restore(undo: ArrivalUndo, afterRestore: () => void): Promise<Outcome> {
  const result = await bulkRestoreItems(undo.projectKey, undo.keys);
  afterRestore();
  const [first] = result.restored;
  if (first === undefined) {
    return { kind: 'failed', text: result.failed[0]?.error.message ?? '沒有復原任何項目。' };
  }
  const text = result.restored.length === 1 ? `已復原 ${first.key}「${first.title}」。` : `已復原 ${String(result.restored.length)} 個項目。`;
  return { kind: 'done', text, openKey: result.restored.length === 1 ? first.key : null };
}

function UndoButton({ isPending, onClick }: { readonly isPending: boolean; readonly onClick: () => void }) {
  return (
    <Button size="sm" aria-disabled={isPending || undefined} onClick={isPending ? undefined : onClick}>
      {isPending ? '復原中…' : '復原'}
    </Button>
  );
}

interface UndoableArrivalProps {
  readonly message: string;
}

/**
 * 換頁後的提示（ArrivalNotice）：瀏覽紀錄帶了 undo 時旁邊有「復原」。
 * 按了送出，成功就把同一個位置換成「已復原 AGY-7「…」。」加連到項目的連結，焦點移到提示；
 * 失敗（沒復原、網路）時「已刪除 …」與「復原」照舊留著，下面多一行原因（不自己消失），可以再按一次。
 * 取代各頁的 ArrivalNotice（頁面把 useArrivalNotice 的字給它）。復原的是一次刪除：沒有提示（message 是空的）時不畫按鈕，提示掛著但看不見。
 */
export function UndoableArrival({ message }: UndoableArrivalProps) {
  const location = useLocation();
  const state: unknown = location.state;
  const locationKey = location.key;
  const queryClient = useQueryClient();
  const undo = message === '' ? null : undoOf(state);
  const [remembered, setRemembered] = useState<Remembered | null>(null);
  const current = remembered !== null && remembered.locationKey === locationKey ? remembered : null;

  const run = async (target: ArrivalUndo) => {
    setRemembered({ locationKey, outcome: null, isPending: true });
    let outcome: Outcome;
    try {
      outcome = await restore(target, () => {
        refreshAfterRestore(queryClient, target.projectKey, target.keys);
      });
    } catch (error) {
      outcome = { kind: 'failed', text: errorMessage(error) };
    }
    setRemembered({ locationKey, outcome, isPending: false });
    // 復原了：重新整理後不要又出現「已刪除」與「復原」（再刪一次之後按舊的「復原」會救回來）。
    if (outcome.kind === 'done') removeUndoFromHistory(locationKey);
  };

  const outcome = current?.outcome ?? null;
  const noticeRef = useRef<HTMLDivElement>(null);
  const isDone = outcome?.kind === 'done';
  // 成功之後「復原」跟著不見，焦點移到提示本身（不然掉到頁面本體）；失敗時按鈕還在，焦點不動。
  useEffect(() => {
    if (isDone) noticeRef.current?.focus();
  }, [isDone]);
  if (undo !== null && outcome?.kind === 'done') {
    const link: ReactNode = outcome.openKey === null ? undefined : <OpenLink projectKey={undo.projectKey} itemKey={outcome.openKey} />;
    return <ArrivalNotice message={outcome.text} actions={link} noticeRef={noticeRef} />;
  }
  const action =
    undo === null ? undefined : (
      <UndoButton
        isPending={current?.isPending ?? false}
        onClick={() => {
          void run(undo);
        }}
      />
    );
  return <ArrivalNotice message={message} actions={action} detail={outcome?.kind === 'failed' ? outcome.text : undefined} noticeRef={noticeRef} />;
}
