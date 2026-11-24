import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { notification, serveNotifications } from '../../test/notifications';
import { created, memberRef, serveActivity } from '../../test/activity';
import { renderApp } from '../../test/renderApp';
import { currentNotificationId } from './currentNotification';

// 同一個項目（SMK-7）有兩則通知，另一則是 SMK-8。
const assigned = notification({ kind: 'assigned', itemKey: 'SMK-7', itemTitle: '首頁設計稿' });
const commented = notification({ kind: 'commented', itemKey: 'SMK-7', itemTitle: '首頁設計稿', actor: memberRef, excerpt: '收到' });
const other = notification({ kind: 'mentioned', itemKey: 'SMK-8', itemTitle: '登入流程', actor: memberRef, excerpt: '看一下' });

function setup(path: string) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveNotifications([assigned, commented, other]);
  serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
  serveItem(detailOf(smkItem({ number: 8, title: '登入流程' })));
  serveActivity('SMK-7', [created()]);
  serveActivity('SMK-8', [created()]);
  return renderApp(path);
}

const assignedLink = () => screen.getByRole('link', { name: /指派給你/ });
const commentedLink = () => screen.getByRole('link', { name: /留言了/ });
const otherLink = () => screen.getByRole('link', { name: /提及你/ });

describe('收件匣：面板開著的是哪一則', () => {
  it('同一個項目有好幾則時，只有點的那一則是選取的（aria-current），同一個項目的其他則不是', async () => {
    // Arrange
    const { user } = setup('/inbox');
    await screen.findByRole('link', { name: /留言了/ });

    // Act
    await user.click(commentedLink());

    // Assert
    await screen.findByRole('complementary', { name: '項目 SMK-7' });
    expect(commentedLink()).toHaveAttribute('aria-current', 'true');
    expect(assignedLink()).not.toHaveAttribute('aria-current');
    expect(otherLink()).not.toHaveAttribute('aria-current');
  });

  it('網址直接帶著 item（沒有點過哪一則）：選取那個項目的第一則', async () => {
    // Arrange / Act
    setup('/inbox?item=SMK-7');

    // Assert
    await screen.findByRole('complementary', { name: '項目 SMK-7' });
    await waitFor(() => {
      expect(assignedLink()).toHaveAttribute('aria-current', 'true');
    });
    expect(commentedLink()).not.toHaveAttribute('aria-current');
  });

  it('關上面板時焦點回到點的那一則，不是同一個項目的第一則', async () => {
    // Arrange
    const { user } = setup('/inbox');
    await screen.findByRole('link', { name: /留言了/ });
    await user.click(commentedLink());
    await screen.findByRole('complementary', { name: '項目 SMK-7' });

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('complementary', { name: '項目 SMK-7' })).not.toBeInTheDocument();
    });
    expect(commentedLink()).toHaveFocus();
  });

  it('在面板裡換到下一個項目：選取跟著換到那個項目的那一則', async () => {
    // Arrange
    const { user } = setup('/inbox');
    await screen.findByRole('link', { name: /留言了/ });
    await user.click(commentedLink());
    await screen.findByRole('complementary', { name: '項目 SMK-7' });

    // Act
    await user.click(screen.getByRole('button', { name: '下一個項目' }));

    // Assert
    await screen.findByRole('complementary', { name: '項目 SMK-8' });
    expect(otherLink()).toHaveAttribute('aria-current', 'true');
    expect(commentedLink()).not.toHaveAttribute('aria-current');
    expect(assignedLink()).not.toHaveAttribute('aria-current');
  });
});

describe('currentNotificationId', () => {
  const list = [assigned, commented, other];

  it('面板沒開：沒有選取的', () => {
    // Arrange / Act
    const id = currentNotificationId(list, null, commented.notificationId);

    // Assert
    expect(id).toBeNull();
  });

  it('點的那一則就是面板開著的項目：選它', () => {
    // Arrange / Act
    const id = currentNotificationId(list, 'SMK-7', commented.notificationId);

    // Assert
    expect(id).toBe(commented.notificationId);
  });

  it('點的那一則不是面板開著的項目（或沒點過）：選那個項目的第一則', () => {
    // Arrange / Act
    const fromOther = currentNotificationId(list, 'SMK-7', other.notificationId);
    const fromNone = currentNotificationId(list, 'SMK-7', null);

    // Assert
    expect(fromOther).toBe(assigned.notificationId);
    expect(fromNone).toBe(assigned.notificationId);
  });

  it('清單上沒有面板開著的項目（例如在面板裡點了上層）：沒有選取的', () => {
    // Arrange / Act
    const id = currentNotificationId(list, 'SMK-1', assigned.notificationId);

    // Assert
    expect(id).toBeNull();
  });
});
