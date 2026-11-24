import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ItemSubscription } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { serveSubscription } from '../../test/notifications';
import { flushPending } from '../../test/flush';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));

function openItem(subscription: Omit<ItemSubscription, 'canFollow'> & { readonly canFollow?: boolean }, path = '/projects/SMK/items/SMK-7') {
  signedInAs(exampleOwner, { details: [smkDetail()], myWork: [smkItem({ number: 7, title: '首頁設計稿' })] });
  serveItem(task);
  serveActivity(task.key, [created()]);
  const puts = serveSubscription(task.key, subscription);
  const view = renderApp(path);
  return { ...view, puts };
}

describe('關注的切換按鈕', () => {
  it('項目頁的頁列有「關注」：沒有在關注時 aria-pressed 是 false，按了送出 following: true，名稱不變，aria-pressed 變 true', async () => {
    // Arrange
    const { user, puts } = openItem({ following: false, reason: null });
    const button = await screen.findByRole('button', { name: '關注' });
    expect(button).toHaveAttribute('aria-pressed', 'false');

    // Act
    await user.click(button);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '關注' })).toHaveAttribute('aria-pressed', 'true');
    });
    expect(screen.queryByRole('button', { name: '關注中' })).not.toBeInTheDocument();
    expect(puts).toEqual([true]);
    expect(screen.getByRole('button', { name: '關注' })).toHaveFocus();
  });

  it('在關注中（aria-pressed 是 true，字還是「關注」）再按一下取消關注', async () => {
    // Arrange
    const { user, puts } = openItem({ following: true, reason: 'manual' });
    const button = await screen.findByRole('button', { name: '關注' });
    expect(button).toHaveAttribute('aria-pressed', 'true');
    expect(button).toHaveTextContent(/^關注$/);

    // Act
    await user.click(button);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '關注' })).toHaveAttribute('aria-pressed', 'false');
    });
    expect(puts).toEqual([false]);
  });

  it.each([
    ['assignee', '你是負責人，所以自動關注'],
    ['creator', '你是建立的人，所以自動關注'],
    ['commenter', '你留過言，所以自動關注'],
    ['mentioned', '你被提及，所以自動關注'],
    ['manual', '你自己選了關注'],
  ] as const)('關注的原因是 %s：說明寫「%s」', async (reason, text) => {
    // Arrange & Act
    openItem({ following: true, reason });

    // Assert
    const button = await screen.findByRole('button', { name: '關注' });
    expect(button).toHaveAccessibleDescription(new RegExp(text));
    expect(button).toHaveAttribute('title', expect.stringContaining(text));
  });

  it('沒有在關注時說明關注會怎樣', async () => {
    // Arrange & Act
    openItem({ following: false, reason: null });

    // Assert
    expect(await screen.findByRole('button', { name: '關注' })).toHaveAccessibleDescription(/有留言或改了狀態時會通知你/);
  });

  it('失敗時回到原本的狀態，說一聲，可以再按', async () => {
    // Arrange
    const { user } = openItem({ following: false, reason: null });
    const button = await screen.findByRole('button', { name: '關注' });
    server.use(http.put('/v1/items/SMK-7/subscription', () => failure(500, 'server.error', '伺服器暫時有問題。')));

    // Act
    await user.click(button);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('無法更新關注，請再試一次。');
    expect(screen.getByRole('button', { name: '關注' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('讀不到關注狀態（沒有權限、項目看不到）時不放按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(task);
    serveActivity(task.key, [created()]);
    server.use(http.get('/v1/items/SMK-7/subscription', () => failure(403, 'work.forbidden', '沒有權限。')));

    // Act
    renderApp('/projects/SMK/items/SMK-7');

    // Assert
    await screen.findByRole('heading', { name: '首頁設計稿', level: 1 });
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /^關注/ })).not.toBeInTheDocument();
    });
  });

  it('項目面板的標題列只有圖示（讓位給項目的編號）：名稱、aria-pressed、說明都在，和整頁共用同一份狀態', async () => {
    // Arrange
    const { user, puts } = openItem({ following: false, reason: null }, '/my-work?item=SMK-7');
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-7' });
    const button = await within(panel).findByRole('button', { name: '關注' });
    expect(button).toHaveTextContent('');
    expect(button).toHaveAttribute('title', expect.stringContaining('有留言或改了狀態時會通知你'));

    // Act
    await user.click(button);

    // Assert
    await waitFor(() => {
      expect(within(panel).getByRole('button', { name: '關注' })).toHaveAttribute('aria-pressed', 'true');
    });
    expect(puts).toEqual([true]);
    expect(within(panel).getAllByRole('heading', { level: 2 })[0]).toHaveTextContent('SMK-7');
  });
});

describe('不是專案成員（canFollow 為 false）', () => {
  it('按鈕是 aria-disabled（停得到），說明「加入專案成員才能關注」，按了不送出', async () => {
    // Arrange
    const { user, puts } = openItem({ following: false, reason: null, canFollow: false });
    const button = await screen.findByRole('button', { name: '關注' });

    // Act
    await user.click(button);
    await flushPending();

    // Assert
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).not.toBeDisabled();
    expect(button).toHaveAccessibleDescription('加入專案成員才能關注。');
    expect(button).toHaveAttribute('title', '加入專案成員才能關注。');
    expect(puts).toEqual([]);
  });

  it('後端回 409 subscription.not_member（畫面上的狀態過時了）：照同樣的說法提示，按鈕變成不能關注', async () => {
    // Arrange
    const { user } = openItem({ following: false, reason: null });
    const button = await screen.findByRole('button', { name: '關注' });
    server.use(
      http.put('/v1/items/SMK-7/subscription', () => failure(409, 'subscription.not_member', '要先加入這個專案，才能關注它的項目。')),
    );

    // Act
    await user.click(button);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('加入專案成員才能關注。');
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '關注' })).toHaveAttribute('aria-disabled', 'true');
    });
  });
});
