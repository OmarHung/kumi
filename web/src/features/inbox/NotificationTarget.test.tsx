import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { created, memberRef, serveActivity } from '../../test/activity';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { notification, serveNotifications } from '../../test/notifications';
import { renderApp } from '../../test/renderApp';

const COMMENT_ID = '0190c000-0000-7000-8000-000000000001';

const commented = notification({ kind: 'commented', itemKey: 'SMK-7', actor: memberRef, excerpt: '收到', commentId: COMMENT_ID });
const assigned = notification({ kind: 'assigned', itemKey: 'SMK-7' });
const other = notification({ kind: 'mentioned', itemKey: 'SMK-8', itemTitle: '登入流程', actor: memberRef });

function setup() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveNotifications([commented, assigned, other]);
  serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
  serveItem(detailOf(smkItem({ number: 8, title: '登入流程' })));
  serveActivity('SMK-7', [created()]);
  serveActivity('SMK-8', [created()]);
  return renderApp('/inbox');
}

describe('收件匣：點了由留言產生的通知', () => {
  it('有 commentId 的連結帶著那一則留言的 hash，沒有的不帶（開新分頁、複製連結也一樣）', async () => {
    // Arrange / Act
    setup();
    const withComment = await screen.findByRole('link', { name: /留言了/ });

    // Assert
    expect(withComment).toHaveAttribute('href', `/projects/SMK/items/SMK-7#comment-${COMMENT_ID}`);
    expect(screen.getByRole('link', { name: /指派給你/ })).toHaveAttribute('href', '/projects/SMK/items/SMK-7');
  });

  it('「回覆了你的留言」的連結帶著那則回覆的 hash，和留言了一樣', async () => {
    // Arrange
    const replied = notification({ kind: 'replied', itemKey: 'SMK-7', actor: memberRef, excerpt: '收到', commentId: COMMENT_ID });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveNotifications([replied]);
    serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
    serveActivity('SMK-7', [created()]);

    // Act
    renderApp('/inbox');

    // Assert
    const link = await screen.findByRole('link', { name: /回覆了你的留言/ });
    expect(link).toHaveAttribute('href', `/projects/SMK/items/SMK-7#comment-${COMMENT_ID}`);
  });

  it('一般的點擊在面板打開那個項目，網址帶著 hash', async () => {
    // Arrange
    const { user, router } = setup();
    await screen.findByRole('link', { name: /留言了/ });

    // Act
    await user.click(screen.getByRole('link', { name: /留言了/ }));

    // Assert
    await screen.findByRole('complementary', { name: '項目 SMK-7' });
    expect(router.state.location.search).toBe('?item=SMK-7');
    expect(router.state.location.hash).toBe(`#comment-${COMMENT_ID}`);
  });

  it('沒有 commentId 的通知點了之後網址沒有 hash', async () => {
    // Arrange
    const { user, router } = setup();
    await screen.findByRole('link', { name: /指派給你/ });

    // Act
    await user.click(screen.getByRole('link', { name: /指派給你/ }));

    // Assert
    await screen.findByRole('complementary', { name: '項目 SMK-7' });
    expect(router.state.location.hash).toBe('');
  });

  it('面板開著同一個項目再點一次同一則：照樣導航（換一個瀏覽紀錄的 key），好再閃一次', async () => {
    // Arrange
    const { user, router } = setup();
    await screen.findByRole('link', { name: /留言了/ });
    await user.click(screen.getByRole('link', { name: /留言了/ }));
    await screen.findByRole('complementary', { name: '項目 SMK-7' });
    const firstKey = router.state.location.key;

    // Act
    await user.click(screen.getByRole('link', { name: /留言了/ }));

    // Assert
    await waitFor(() => {
      expect(router.state.location.key).not.toBe(firstKey);
    });
    expect(router.state.location.hash).toBe(`#comment-${COMMENT_ID}`);
  });

  it('面板開著同一個項目、點的是沒有 hash 的那一則：不動', async () => {
    // Arrange
    const { user, router } = setup();
    await screen.findByRole('link', { name: /留言了/ });
    await user.click(screen.getByRole('link', { name: /留言了/ }));
    await screen.findByRole('complementary', { name: '項目 SMK-7' });
    const firstKey = router.state.location.key;

    // Act
    await user.click(screen.getByRole('link', { name: /指派給你/ }));

    // Assert
    expect(router.state.location.key).toBe(firstKey);
    expect(router.state.location.hash).toBe(`#comment-${COMMENT_ID}`);
  });

  it('在面板裡換到下一個項目：hash 不見', async () => {
    // Arrange
    const { user, router } = setup();
    await screen.findByRole('link', { name: /留言了/ });
    await user.click(screen.getByRole('link', { name: /留言了/ }));
    await screen.findByRole('complementary', { name: '項目 SMK-7' });

    // Act
    await user.click(screen.getByRole('button', { name: '下一個項目' }));

    // Assert
    await screen.findByRole('complementary', { name: '項目 SMK-8' });
    expect(router.state.location.hash).toBe('');
  });

  it('點另一個項目的通知（面板已開著別的項目）：換成那個項目', async () => {
    // Arrange
    const { user, router } = setup();
    await screen.findByRole('link', { name: /留言了/ });
    await user.click(screen.getByRole('link', { name: /留言了/ }));
    await screen.findByRole('complementary', { name: '項目 SMK-7' });

    // Act
    await user.click(screen.getByRole('link', { name: /提及你/ }));

    // Assert
    await screen.findByRole('complementary', { name: '項目 SMK-8' });
    expect(router.state.location.hash).toBe('');
  });
});
