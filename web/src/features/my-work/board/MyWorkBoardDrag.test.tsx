import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { failure, signedInAs } from '../../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../../test/fixtures';
import { server } from '../../../test/server';
import { dragCardToColumn, dragStart, serveMovable, stubBoardLayout } from './boardTestSupport';
import { renderMyWorkBoard } from './renderMyWorkBoard';

const mine = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
const todo = smkItem({ number: 1, title: '首頁設計稿', assignee: mine });
const developing = agyItem({ number: 5, title: '開發中的需求', state: 'developing', stateCategory: 'in_progress', assignee: mine });

let restoreLayout: () => void = () => undefined;
beforeEach(() => {
  restoreLayout = stubBoardLayout();
});
afterEach(() => {
  restoreLayout();
});

function column(name: string): HTMLElement {
  return screen.getByRole('region', { name });
}

describe('拖曳', () => {
  it('拖到只有一個走得到的狀態的欄：直接移，先畫在新的欄（送出中），成功後留在那裡', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], items: [todo] });
    const backend = serveMovable([todo], { hold: true });
    renderMyWorkBoard();
    const card = await screen.findByRole('article', { name: '首頁設計稿' });

    // Act：欄的順序是 待規劃、未開始、進行中、完成
    dragCardToColumn(card, 2);

    // Assert
    await waitFor(() => {
      expect(backend.transitions).toEqual([{ key: 'SMK-1', body: { to: 'doing' } }]);
    });
    const pendingCard = await within(column('進行中')).findByRole('article', { name: '首頁設計稿' });
    expect(pendingCard).toHaveAttribute('aria-busy', 'true');
    expect(await screen.findByText('正在把 SMK-1 移到「進行中」…')).toBeInTheDocument();
    backend.release();
    expect(await screen.findByText('已把 SMK-1 移到「進行中」。')).toBeInTheDocument();
    expect(within(column('進行中')).getByRole('article', { name: '首頁設計稿' })).not.toHaveAttribute('aria-busy');
    expect(within(column('未開始')).queryByRole('article')).not.toBeInTheDocument();
  });

  it('拖到條件以外的欄（完成）：卡片留在那一欄，重抓之後伺服器不再給它也一樣', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], items: [todo] });
    const backend = serveMovable([todo]);
    const { queryClient } = renderMyWorkBoard();
    const card = await screen.findByRole('article', { name: '首頁設計稿' });

    // Act
    dragCardToColumn(card, 3);

    // Assert
    await waitFor(() => {
      expect(backend.transitions).toEqual([{ key: 'SMK-1', body: { to: 'done' } }]);
    });
    expect(await screen.findByText('已把 SMK-1 移到「完成」。')).toBeInTheDocument();
    expect(within(column('完成')).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
    // 伺服器的清單已經不給它（改成完成、不符合「未結束」）：重抓之後照樣留在原處，直到離開這一頁。
    await queryClient.invalidateQueries({ queryKey: ['my-work'] });
    await waitFor(() => {
      expect(backend.listRequests()).toBeGreaterThan(1);
    });
    expect(within(column('完成')).getByRole('article', { name: '首頁設計稿' })).toBeInTheDocument();
    expect(within(column('未開始')).queryByRole('article')).not.toBeInTheDocument();
  });

  it('放回原欄、拖到別的欄之外都不送請求', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()], items: [todo] });
    const backend = serveMovable([todo]);
    renderMyWorkBoard();
    const card = await screen.findByRole('article', { name: '首頁設計稿' });

    // Act
    dragCardToColumn(card, 1);

    // Assert
    expect(await screen.findByText('SMK-1「首頁設計稿」 放回原處，狀態沒有改。')).toBeInTheDocument();
    expect(backend.transitions).toEqual([]);
  });

  it('拖曳中，走不到的欄寫出原因（依那張卡片自己專案的藍圖與角色）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail(), smkDetail()], items: [developing, todo] });
    serveMovable([developing, todo]);
    renderMyWorkBoard();
    const card = await screen.findByRole('article', { name: '開發中的需求' });

    // Act
    await screen.findByRole('article', { name: '首頁設計稿' });
    await waitFor(() => {
      expect(within(card).getByText('開發')).toBeInTheDocument();
    });
    dragStart(card);

    // Assert
    expect(await within(column('完成')).findByText('走不到：流程沒有這條路')).toBeInTheDocument();
    expect(within(column('完成')).getByText('走不到：流程沒有這條路')).toBeInTheDocument();
    expect(within(column('待規劃')).getByText('走不到：流程沒有「待規劃」的狀態')).toBeInTheDocument();
    expect(within(column('未開始')).getByText('走不到：流程沒有這條路')).toBeInTheDocument();
  });

  it('一個都走不到：照樣送出，後端的說明在看板上方，卡片留在原欄', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()], items: [developing] });
    serveMovable([developing]);
    server.use(
      http.post('/v1/items/AGY-5/transitions', () => failure(422, 'item.transition_not_allowed', '流程沒有從「開發」到「結案」的轉換。')),
    );
    renderMyWorkBoard();
    const card = await screen.findByRole('article', { name: '開發中的需求' });
    await waitFor(() => {
      expect(within(card).getByText('開發')).toBeInTheDocument();
    });

    // Act
    dragCardToColumn(card, 3);

    // Assert
    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('AGY-5「開發中的需求」沒有移到「結案」，留在「開發」。')).toBeInTheDocument();
    expect(within(alert).getByText('流程沒有從「開發」到「結案」的轉換。')).toBeInTheDocument();
    expect(within(column('進行中')).getByRole('article', { name: '開發中的需求' })).toBeInTheDocument();
  });
});
