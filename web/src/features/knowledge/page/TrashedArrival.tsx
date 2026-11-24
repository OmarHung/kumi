import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type RefObject } from 'react';
import { Link, useLocation } from 'react-router';
import { errorMessage } from '../../../api/http';
import { restorePage } from '../../../api/knowledge';
import { queryKeys } from '../../../api/queryKeys';
import { Button } from '../../../components/Button/Button';
import { ArrivalNotice } from '../../../components/StatusViews/StatusViews';
import { useArrivalNotice } from '../../../lib/useArrivalNotice';
import { knowledgePageUrl } from '../knowledgeLinks';

export interface Trashed {
  readonly spaceKey: string;
  readonly pageNumber: number;
  readonly title: string;
}

/**
 * 刪的不是正在看的頁面：不換頁（換頁會搬走焦點、打斷正在編輯的頁面），提示照樣出現在這個位置、有「復原」。
 * locationKey：刪的當下所在的那一筆瀏覽紀錄，換到別的網址就不再顯示。
 */
export interface InPlaceTrashed {
  readonly id: number;
  readonly locationKey: string;
  readonly trashed: Trashed;
}

/** 瀏覽紀錄的 state 裡帶的「剛放進垃圾桶的頁面」；沒有或形狀不對（舊的紀錄、別處導過來的）是 null。 */
function trashedOf(state: unknown): Trashed | null {
  if (typeof state !== 'object' || state === null || !('trashed' in state)) return null;
  const { trashed } = state;
  if (typeof trashed !== 'object' || trashed === null) return null;
  const { spaceKey, pageNumber, title } = trashed as Record<string, unknown>;
  return typeof spaceKey === 'string' && typeof pageNumber === 'number' && typeof title === 'string' ? { spaceKey, pageNumber, title } : null;
}

type Outcome = { readonly kind: 'done' } | { readonly kind: 'failed'; readonly text: string };

interface Remembered {
  /** 屬於哪一個提示（哪一筆瀏覽紀錄、或哪一次原地的刪除）：換到別的就不再顯示（和提示本身一樣）。 */
  readonly locationKey: string;
  readonly outcome: Outcome | null;
  readonly isPending: boolean;
}

/**
 * 刪除之後換頁帶來的提示（docs/design/08-interaction.md 8.3「刪除與垃圾桶」）：「已把「…」放進垃圾桶。」旁邊有「復原」。
 * 按了送出，成功就把同一個位置換成「已復原「…」。」加連到那一頁的連結（留佔位，內容不跳），焦點移到提示；
 * 失敗時提示與「復原」照舊留著，下面多一行原因，可以再按一次。長相與 UndoableArrival（項目的刪除）一樣。
 */
export function TrashedArrival({ headingRef, inPlace = null }: { readonly headingRef: RefObject<HTMLElement | null>; readonly inPlace?: InPlaceTrashed | null }) {
  const arrivalMessage = useArrivalNotice(headingRef);
  const location = useLocation();
  const here = inPlace !== null && inPlace.locationKey === location.key ? inPlace : null;
  // 這一筆紀錄的記憶（復原做到哪）要分得開：換頁來的提示用紀錄的 key，原地的提示用刪除的流水號。
  const stateKey = here === null ? location.key : `in-place-${String(here.id)}`;
  const message = here === null ? arrivalMessage : `已把「${here.trashed.title}」放進垃圾桶。`;
  const queryClient = useQueryClient();
  const noticeRef = useRef<HTMLDivElement>(null);
  const [remembered, setRemembered] = useState<Remembered | null>(null);
  const trashed = here !== null ? here.trashed : message === '' ? null : trashedOf(location.state);
  const current = remembered !== null && remembered.locationKey === stateKey ? remembered : null;
  const outcome = current?.outcome ?? null;
  const isDone = outcome?.kind === 'done';

  // 復原成功之後「復原」跟著不見，焦點移到提示本身（不然掉到頁面本體）；失敗時按鈕還在，焦點不動。
  useEffect(() => {
    if (isDone) noticeRef.current?.focus();
  }, [isDone]);

  async function run(target: Trashed) {
    setRemembered({ locationKey: stateKey, outcome: null, isPending: true });
    let result: Outcome;
    try {
      await restorePage(target.spaceKey, target.pageNumber);
      await queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeSpace(target.spaceKey) });
      result = { kind: 'done' };
    } catch (error) {
      result = { kind: 'failed', text: errorMessage(error) };
    }
    setRemembered({ locationKey: stateKey, outcome: result, isPending: false });
  }

  if (trashed !== null && isDone) {
    const link = <Link to={knowledgePageUrl(trashed.spaceKey, trashed.pageNumber)}>看這一頁</Link>;
    return <ArrivalNotice message={`已復原「${trashed.title}」。`} actions={link} noticeRef={noticeRef} />;
  }
  const isPending = current?.isPending ?? false;
  const action =
    trashed === null ? undefined : (
      <Button
        size="sm"
        aria-disabled={isPending || undefined}
        onClick={() => {
          if (!isPending) void run(trashed);
        }}
      >
        {isPending ? '復原中…' : '復原'}
      </Button>
    );
  return <ArrivalNotice message={message} actions={action} detail={outcome?.kind === 'failed' ? outcome.text : undefined} noticeRef={noticeRef} />;
}
