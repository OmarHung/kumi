import { fireEvent } from '@testing-library/react';
import { http } from 'msw';
import { vi } from 'vitest';
import type { StateCategory } from '../../../api/blueprint';
import type { Item } from '../../../api/types';
import { ok } from '../../../test/api';
import { agencyBlueprint, simpleTasksBlueprint } from '../../../test/blueprints';
import { filterAcrossProjects } from '../../../test/crossProjectItems';
import { exampleOwner } from '../../../test/fixtures';
import { detailOf } from '../../../test/items';
import { server } from '../../../test/server';

const CHANGED_AT = '2026-11-16T09:00:00Z';

function categoryOf(state: string, extra: Readonly<Record<string, StateCategory>>): StateCategory {
  if (Object.hasOwn(extra, state)) return extra[state] as StateCategory;
  const states = [...agencyBlueprint.workflows, ...simpleTasksBlueprint.workflows].flatMap((workflow) => workflow.states);
  return states.find((candidate) => candidate.key === state)?.category ?? 'in_progress';
}

export interface MovableServer {
  /** 收到的改狀態請求：項目編號與內文。 */
  readonly transitions: { readonly key: string; readonly body: unknown }[];
  /** 收到的清單請求數。 */
  readonly listRequests: () => number;
  /** 讓卡在送出中的改狀態回應。 */
  readonly release: () => void;
}

/**
 * 假的跨專案清單加改狀態：改好之後清單真的換成新的狀態（重抓時看到的和後端一樣），
 * 「我的工作」的看板移到條件以外時，伺服器的清單就不再給那張卡片。hold 為 true 時改狀態先不回應，等 release。
 */
export function serveMovable(initial: readonly Item[], options: { readonly hold?: boolean; readonly categories?: Readonly<Record<string, StateCategory>> } = {}): MovableServer {
  let items = initial;
  let lists = 0;
  const transitions: { key: string; body: unknown }[] = [];
  let open: () => void = () => undefined;
  const gate = options.hold === true ? new Promise<void>((resolve) => { open = resolve; }) : Promise.resolve();
  server.use(
    http.get('/v1/items', ({ request }) => {
      lists += 1;
      return ok(filterAcrossProjects(items, new URL(request.url), exampleOwner.personId));
    }),
    http.post('/v1/items/:key/transitions', async ({ params, request }) => {
      const body = (await request.json()) as { to: string };
      transitions.push({ key: String(params.key), body });
      await gate;
      const current = items.find((item) => item.key === params.key);
      if (current === undefined) return new Response(null, { status: 404 });
      const moved = { ...current, state: body.to, stateCategory: categoryOf(body.to, options.categories ?? {}), updatedAt: CHANGED_AT };
      items = items.map((item) => (item.key === moved.key ? moved : item));
      return ok(detailOf(moved));
    }),
  );
  return { transitions, listRequests: () => lists, release: open };
}

const COLUMN_WIDTH = 300;
const LANES_HEIGHT = 600;

/**
 * jsdom 沒有版面（每個元素的位置都是 0），放下時不會落在任何一欄上。
 * 這裡讓每一欄（section）依順序各佔一個寬 300 的長條，欄的容器涵蓋全部：指標的 x 在哪一條就落在哪一欄。
 */
export function stubBoardLayout(): () => void {
  const rect = (left: number, width: number): DOMRect => ({
    left,
    right: left + width,
    top: 0,
    bottom: LANES_HEIGHT,
    width,
    height: LANES_HEIGHT,
    x: left,
    y: 0,
    toJSON: () => ({}),
  });
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    if (this instanceof HTMLElement && this.tagName === 'SECTION' && this.parentElement !== null) {
      const index = [...this.parentElement.children].indexOf(this);
      return rect(index * COLUMN_WIDTH, COLUMN_WIDTH);
    }
    if (this instanceof HTMLElement && this.querySelector(':scope > section') !== null) {
      return rect(0, COLUMN_WIDTH * this.children.length);
    }
    return rect(0, 0);
  });
  return () => {
    vi.restoreAllMocks();
  };
}

/** 滑鼠把卡片拖到第 index 欄（從 0 算）再放開。 */
export function dragCardToColumn(card: HTMLElement, index: number): void {
  const x = index * COLUMN_WIDTH + COLUMN_WIDTH / 2;
  fireEvent.pointerDown(card, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 10, clientY: 100 });
  fireEvent.pointerMove(document, { clientX: 40, clientY: 100 });
  fireEvent.pointerMove(document, { clientX: x, clientY: 100 });
  fireEvent.pointerUp(document, { clientX: x, clientY: 100 });
}

/** 滑鼠按住卡片、拖出 6px 以上（開始拖，還沒放開）。 */
export function dragStart(card: HTMLElement): void {
  fireEvent.pointerDown(card, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 10, clientY: 100 });
  fireEvent.pointerMove(document, { clientX: 40, clientY: 100 });
}
