import { createEvent, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { WorkflowState } from '../../api/blueprint';
import { failure, ok, signedInAs } from '../../test/api';
import { holdTransition, serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { BOARD_ANNOUNCEMENTS, dropTarget } from './boardDnd';

const todo: WorkflowState = { key: 'todo', name: '待辦', category: 'not_started' };
const doing: WorkflowState = { key: 'doing', name: '進行中', category: 'in_progress' };
const card = smkItem({ number: 1 });

function active(data: unknown) {
  return { data: { current: data as Record<string, unknown> } };
}

function over(state: WorkflowState, blockedReason: string | null = null) {
  return { data: { current: { kind: 'column', state, blockedReason } } };
}

describe('dropTarget', () => {
  it('放到別的欄就改成那個狀態', () => {
    // Arrange / Act / Assert
    expect(dropTarget(active({ kind: 'card', item: card }), over(doing))).toEqual({ item: card, to: 'doing' });
  });

  it('放回原欄（同一欄）、拖出看板（不在任何一欄上）都不改', () => {
    // Arrange / Act / Assert
    expect(dropTarget(active({ kind: 'card', item: card }), over(todo))).toBeNull();
    expect(dropTarget(active({ kind: 'card', item: card }), null)).toBeNull();
  });

  it('不是卡片或不是欄的資料不理會', () => {
    // Arrange / Act / Assert
    expect(dropTarget(active(undefined), over(doing))).toBeNull();
    expect(dropTarget(active({ kind: 'card', item: card }), { data: { current: { kind: 'other' } } })).toBeNull();
  });
});

describe('拖曳的無障礙文字', () => {
  it('用繁體中文說明拖到哪裡、走不走得到、放下之後會不會改', () => {
    // Arrange
    const dragged = { ...active({ kind: 'card', item: card }), id: card.key, rect: { current: { initial: null, translated: null } } };
    const column = (state: WorkflowState, reason: string | null = null) => ({
      ...over(state, reason),
      id: state.key,
      rect: { width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0 },
      disabled: false,
    });

    // Act / Assert
    expect(BOARD_ANNOUNCEMENTS.onDragStart({ active: dragged })).toBe('開始拖曳 SMK-1「首頁設計稿」。');
    expect(BOARD_ANNOUNCEMENTS.onDragOver({ active: dragged, over: column(doing, '流程沒有這條路') })).toBe(
      'SMK-1「首頁設計稿」 在「進行中」欄上，看起來走不到：流程沒有這條路。',
    );
    expect(BOARD_ANNOUNCEMENTS.onDragOver({ active: dragged, over: null })).toBe('SMK-1「首頁設計稿」 不在任何一欄上。');
    // 真的要送出時不唸（回空字串清掉 dnd-kit 唸的區域），結果由看板自己的 status 與 alert 唸。
    expect(BOARD_ANNOUNCEMENTS.onDragEnd({ active: dragged, over: column(doing) })).toBe('');
    expect(BOARD_ANNOUNCEMENTS.onDragEnd({ active: dragged, over: column(todo) })).toBe('SMK-1「首頁設計稿」 放回原處，狀態沒有改。');
    expect(BOARD_ANNOUNCEMENTS.onDragCancel({ active: dragged, over: null })).toBe('取消拖曳，SMK-1「首頁設計稿」 放回原處。');
  });
});

/** jsdom 沒有版面（每個元素的位置都是 0），放下時不會落在任何一欄上：正好可以測「拖出看板不送請求」。 */
describe('在看板上拖曳', () => {
  async function openAgyBoard() {
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItemsByQuery('AGY', [agyItem({ number: 3, title: '官網需求', state: 'internal_qa', stateCategory: 'in_progress' })]);
    const view = renderApp('/projects/AGY?view=board');
    const board = await screen.findByRole('region', { name: '看板' });
    const article = await within(board).findByRole('article', { name: '官網需求' });
    return { ...view, board, article };
  }

  it('滑鼠移動不到 6px 不算拖；超過才開始，走不到的欄寫出原因；拖出看板放開不送請求', async () => {
    // Arrange
    const { board, article } = await openAgyBoard();
    fireEvent.pointerDown(article, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 100, clientY: 100 });

    // Act：小小的移動
    fireEvent.pointerMove(document, { clientX: 103, clientY: 101 });
    expect(screen.queryByText(/開始拖曳/)).not.toBeInTheDocument();

    // Act：真的拖
    fireEvent.pointerMove(document, { clientX: 140, clientY: 100 });

    // Assert
    expect(await screen.findByText('開始拖曳 AGY-3「官網需求」。')).toBeInTheDocument();
    const clientQa = within(board).getByRole('region', { name: '客戶驗收' });
    expect(within(clientQa).getByText('走不到：需要的角色：專案經理')).toBeInTheDocument();
    expect(within(within(board).getByRole('region', { name: '結案' })).getByText('走不到：流程沒有這條路')).toBeInTheDocument();
    expect(within(within(board).getByRole('region', { name: '開發' })).queryByText(/走不到/)).not.toBeInTheDocument();
    expect(within(within(board).getByRole('region', { name: '內部驗收' })).queryByText(/走不到/)).not.toBeInTheDocument();
    fireEvent.pointerUp(document, { clientX: 140, clientY: 100 });
    expect(await screen.findByText('AGY-3「官網需求」 放回原處，狀態沒有改。')).toBeInTheDocument();
    expect(within(board).queryByText(/走不到/)).not.toBeInTheDocument();
    expect(within(board).queryByText(/正在把/)).not.toBeInTheDocument();
  });

  it('Esc 取消拖曳', async () => {
    // Arrange
    await openAgyBoard();
    const article = screen.getByRole('article', { name: '官網需求' });
    fireEvent.pointerDown(article, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 140, clientY: 100 });
    await screen.findByText(/開始拖曳/);

    // Act
    fireEvent.keyDown(document, { key: 'Escape', code: 'Escape' });

    // Assert
    expect(await screen.findByText('取消拖曳，AGY-3「官網需求」 放回原處。')).toBeInTheDocument();
  });

  it('手指一碰就滑是捲動，不會開始拖；按住一下才開始', async () => {
    // Arrange
    const { article } = await openAgyBoard();

    // Act：手指的 pointer 事件不會讓 Pointer 感應器接手
    fireEvent.pointerDown(article, { pointerType: 'touch', isPrimary: true, button: 0, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 100, clientY: 160 });
    expect(screen.queryByText(/開始拖曳/)).not.toBeInTheDocument();
    fireEvent.touchStart(article);
    expect(screen.queryByText(/開始拖曳/)).not.toBeInTheDocument();

    // Assert：按住超過延遲才開始
    await waitFor(
      () => {
        expect(screen.getByText('開始拖曳 AGY-3「官網需求」。')).toBeInTheDocument();
      },
      { timeout: 1000 },
    );
    fireEvent.touchEnd(article);
  });

  it('手指按住時移動超過 8px 就是捲動：取消，延遲過了也不會開始拖', async () => {
    // Arrange
    const { article } = await openAgyBoard();

    // Act
    touch('touchStart', article, 100, 100);
    touch('touchMove', article, 100, 120);
    await new Promise((resolve) => setTimeout(resolve, 400));

    // Assert
    expect(screen.queryByText(/開始拖曳/)).not.toBeInTheDocument();
    touch('touchEnd', article, 100, 120);
  });

  it('按住時只動了一點點（8px 以內）還算按住，延遲過了照樣開始拖', async () => {
    // Arrange
    const { article } = await openAgyBoard();

    // Act
    touch('touchStart', article, 100, 100);
    touch('touchMove', article, 104, 103);

    // Assert
    await waitFor(
      () => {
        expect(screen.getByText('開始拖曳 AGY-3「官網需求」。')).toBeInTheDocument();
      },
      { timeout: 1000 },
    );
    touch('touchEnd', article, 104, 103);
  });

  it('觸控筆和滑鼠一樣：移動超過 6px 就開始拖', async () => {
    // Arrange
    const { article } = await openAgyBoard();

    // Act
    fireEvent.pointerDown(article, { pointerType: 'pen', isPrimary: true, button: 0, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 140, clientY: 100 });

    // Assert
    expect(await screen.findByText('開始拖曳 AGY-3「官網需求」。')).toBeInTheDocument();
    fireEvent.pointerUp(document, { clientX: 140, clientY: 100 });
  });
});

/** jsdom 的 TouchEvent 不收一般物件當 touches，建好事件後再補上手指的位置。 */
function touch(type: 'touchStart' | 'touchMove' | 'touchEnd', target: Element, clientX: number, clientY: number) {
  const event = createEvent[type](target);
  const point = [{ clientX, clientY }];
  Object.defineProperty(event, 'touches', { value: type === 'touchEnd' ? [] : point });
  Object.defineProperty(event, 'changedTouches', { value: point });
  fireEvent(target, event);
}

/**
 * 給每一欄一個位置（一欄 300px 寬、並排），dnd-kit 才算得出指標在哪一欄上；卡片在它的欄裡。
 * 只替換 jsdom 的版面量測，看板與 dnd-kit 的程式照常跑。
 */
const COLUMN_WIDTH = 300;

/** 看板（欄的捲動容器）看得到的範圍：沒給時剛好放得下每一欄。 */
interface LanesViewport {
  readonly left: number;
  readonly width: number;
}

function layOutColumns(names: readonly string[], viewport: LanesViewport = { left: 0, width: names.length * COLUMN_WIDTH }) {
  const rect = (left: number, top: number, width: number, height: number) => DOMRect.fromRect({ x: left, y: top, width, height });
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    if (this.querySelector(':scope > section[aria-labelledby]') !== null) {
      return rect(viewport.left, 0, viewport.width, 600);
    }
    const section = this.closest('section[aria-labelledby]');
    const index = names.indexOf(section?.querySelector('h2')?.textContent ?? '');
    if (index === -1) return rect(0, 0, 0, 0);
    const left = index * COLUMN_WIDTH;
    return this.tagName === 'ARTICLE' ? rect(left + 10, 60, 260, 80) : rect(left, 0, 280, 600);
  });
}

describe('拖到別的欄', () => {
  it('放在別的欄上就送出改狀態，卡片先移過去；放回原欄不送', async () => {
    // Arrange
    const item = smkItem({ number: 1 });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [item]);
    const held = holdTransition(item, () => ok(detailOf({ ...item, state: 'doing', stateCategory: 'in_progress' })));
    renderApp('/projects/SMK?view=board');
    const board = await screen.findByRole('region', { name: '看板' });
    const article = await within(board).findByRole('article', { name: '首頁設計稿' });
    layOutColumns(['待辦', '進行中', '完成']);

    // Act：在原欄裡拖一下又放下
    fireEvent.pointerDown(article, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 130, clientY: 120 });
    // 唸的區域只留最新的一句：一開始拖，馬上就變成「在哪一欄上」。
    expect(await screen.findByText('SMK-1「首頁設計稿」 在「待辦」欄上。')).toBeInTheDocument();
    fireEvent.pointerUp(document, { clientX: 130, clientY: 120 });
    expect(await screen.findByText('SMK-1「首頁設計稿」 放回原處，狀態沒有改。')).toBeInTheDocument();
    expect(held.bodies).toEqual([]);

    // Act：拖到「進行中」
    fireEvent.pointerDown(article, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 140, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 400, clientY: 120 });
    expect(await screen.findByText('SMK-1「首頁設計稿」 在「進行中」欄上。')).toBeInTheDocument();
    fireEvent.pointerUp(document, { clientX: 400, clientY: 120 });

    // Assert
    const doing = within(board).getByRole('region', { name: '進行中' });
    expect(await within(doing).findByRole('article', { name: '首頁設計稿' })).toHaveAttribute('aria-busy', 'true');
    await waitFor(() => {
      expect(held.bodies).toEqual([{ to: 'doing' }]);
    });
    held.release();
    expect(await within(board).findByText('已把 SMK-1 移到「進行中」。')).toBeInTheDocument();
  });

  it('真的送出時 dnd-kit 不再唸「在哪一欄上」；被擋下來只由看板的說明唸', async () => {
    // Arrange
    const item = smkItem({ number: 1 });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [item]);
    const held = holdTransition(item, () => failure(422, 'item.transition_not_allowed', '現在不能改成「進行中」。'));
    renderApp('/projects/SMK?view=board');
    const board = await screen.findByRole('region', { name: '看板' });
    const article = await within(board).findByRole('article', { name: '首頁設計稿' });
    layOutColumns(['待辦', '進行中', '完成']);

    // Act
    fireEvent.pointerDown(article, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 140, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 400, clientY: 120 });
    await screen.findByText('SMK-1「首頁設計稿」 在「進行中」欄上。');
    fireEvent.pointerUp(document, { clientX: 400, clientY: 120 });

    // Assert：送出中只有看板的 status
    expect(await within(board).findByText('正在把 SMK-1 移到「進行中」…')).toBeInTheDocument();
    expect(screen.queryByText(/欄上。|正在改狀態/)).not.toBeInTheDocument();
    held.release();
    const alert = await within(board).findByRole('alert');
    expect(alert).toHaveTextContent('現在不能改成「進行中」。');
    expect(screen.queryByText(/欄上。|正在改狀態/)).not.toBeInTheDocument();
  });

  it('捲出看板可見範圍的欄放不進去：指標在看板外（例如側欄上）放開不送請求', async () => {
    // Arrange：看板從 x=300 開始看得到；「待辦」被捲到左邊看不見的地方（0～280）
    const item = smkItem({ number: 1, state: 'doing', stateCategory: 'in_progress' });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItemsByQuery('SMK', [item]);
    const held = holdTransition(item, () => ok(detailOf({ ...item, state: 'done', stateCategory: 'done' })));
    renderApp('/projects/SMK?view=board');
    const board = await screen.findByRole('region', { name: '看板' });
    const article = await within(board).findByRole('article', { name: '首頁設計稿' });
    layOutColumns(['待辦', '進行中', '完成'], { left: 300, width: 600 });

    // Act：拖到「待辦」原本的位置（看不見）放開
    fireEvent.pointerDown(article, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 400, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 440, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 100, clientY: 120 });
    expect(await screen.findByText('SMK-1「首頁設計稿」 不在任何一欄上。')).toBeInTheDocument();
    fireEvent.pointerUp(document, { clientX: 100, clientY: 120 });

    // Assert
    expect(await screen.findByText('SMK-1「首頁設計稿」 放回原處，狀態沒有改。')).toBeInTheDocument();
    expect(held.bodies).toEqual([]);
    expect(within(within(board).getByRole('region', { name: '進行中' })).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();

    // Act：看得到的「完成」照常放得進去
    fireEvent.pointerDown(article, { pointerType: 'mouse', isPrimary: true, button: 0, clientX: 400, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 440, clientY: 100 });
    fireEvent.pointerMove(document, { clientX: 700, clientY: 120 });
    await screen.findByText('SMK-1「首頁設計稿」 在「完成」欄上。');
    fireEvent.pointerUp(document, { clientX: 700, clientY: 120 });

    // Assert
    await waitFor(() => {
      expect(held.bodies).toEqual([{ to: 'done' }]);
    });
    held.release();
    expect(await within(board).findByText('已把 SMK-1 移到「完成」。')).toBeInTheDocument();
  });
});
