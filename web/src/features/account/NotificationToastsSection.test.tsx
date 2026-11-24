import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { readToastMode, TOAST_MODE_STORAGE_KEY } from '../notification-toasts/toastMode';

async function openSection() {
  signedInAs(exampleOwner);
  const view = renderApp('/account/notifications');
  const section = await screen.findByRole('region', { name: '通知提示' });
  return { ...view, section, group: within(section).getByRole('group', { name: '提示的顯示方式' }) };
}

describe('我的帳號：通知提示', () => {
  it('預設是完整；說明只影響這台電腦的瀏覽器', async () => {
    // Arrange & Act
    const { section, group } = await openSection();

    // Assert
    expect(within(group).getByRole('button', { name: '完整' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group).getByRole('button', { name: '精簡' })).toHaveAttribute('aria-pressed', 'false');
    expect(within(group).getByRole('button', { name: '不顯示' })).toHaveAttribute('aria-pressed', 'false');
    expect(within(section).getByText(/只影響這台電腦的瀏覽器。/)).toBeInTheDocument();
  });

  it('按了立刻換、記在這個瀏覽器', async () => {
    // Arrange
    const { user, group } = await openSection();

    // Act
    await user.click(within(group).getByRole('button', { name: '精簡' }));

    // Assert
    expect(within(group).getByRole('button', { name: '精簡' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group).getByRole('button', { name: '完整' })).toHaveAttribute('aria-pressed', 'false');
    expect(localStorage.getItem(TOAST_MODE_STORAGE_KEY)).toBe('compact');
    expect(readToastMode()).toBe('compact');
  });

  it('讀到存好的設定', async () => {
    // Arrange
    localStorage.setItem(TOAST_MODE_STORAGE_KEY, 'off');

    // Act
    const { group } = await openSection();

    // Assert
    expect(within(group).getByRole('button', { name: '不顯示' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('示意圖跟著選的方式換：完整有留言的內容、精簡只有誰做了什麼、不顯示只有側欄的數字', async () => {
    // Arrange
    const { user, section, group } = await openSection();
    const preview = () => within(section).getByRole('img', { name: /^示意圖/ });
    const excerpt = '設計稿更新了，請看一下第二頁的按鈕位置。';

    // Assert（完整）
    expect(preview()).toHaveAccessibleName(/留言的內容/);
    expect(within(preview()).getByText(excerpt)).toBeInTheDocument();
    expect(within(preview()).getByText('範例成員')).toBeInTheDocument();

    // Act
    await user.click(within(group).getByRole('button', { name: '精簡' }));

    // Assert（精簡）
    expect(preview()).not.toHaveAccessibleName(/留言的內容/);
    expect(within(preview()).queryByText(excerpt)).toBeNull();
    expect(within(preview()).getByText('範例成員')).toBeInTheDocument();

    // Act
    await user.click(within(group).getByRole('button', { name: '不顯示' }));

    // Assert（不顯示）
    expect(preview()).toHaveAccessibleName(/不跳提示/);
    expect(within(preview()).queryByText('範例成員')).toBeNull();
    expect(within(preview()).getByText('收件匣')).toBeInTheDocument();
  });
});

