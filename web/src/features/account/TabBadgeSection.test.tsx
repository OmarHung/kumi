import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { PRODUCT_NAME } from '../../config/product';
import { signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { badgedFaviconUrl, DEFAULT_TAB_BADGE } from '../inbox/faviconBadge';
import { readTabBadge, TAB_BADGE_STORAGE_KEY } from '../inbox/tabBadgePreference';

async function openSection(unreadCount = 0) {
  signedInAs(exampleOwner, { unreadCount });
  const view = renderApp('/account/notifications');
  const section = await screen.findByRole('region', { name: '分頁的未讀提示' });
  const group = (name: string) => within(section).getByRole('group', { name });
  const preview = () => within(section).getByRole('img', { name: /^示意圖/ });
  return { ...view, section, group, preview };
}

describe('通知設定：分頁的未讀提示', () => {
  // 測試環境沒有 index.html，自己掛一個和它一樣的圖示 link。
  let icon: HTMLLinkElement;

  beforeEach(() => {
    icon = document.createElement('link');
    icon.rel = 'icon';
    icon.setAttribute('href', '/favicon.svg');
    document.head.append(icon);
  });

  afterEach(() => {
    icon.remove();
  });

  it('預設是右上、中、紅色；說明只影響這台電腦的瀏覽器', async () => {
    // Arrange & Act
    const { section, group } = await openSection();

    // Assert
    expect(within(group('位置')).getByRole('button', { name: '右上' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group('大小')).getByRole('button', { name: '中' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group('顏色')).getByRole('button', { name: '紅色' })).toHaveAttribute('aria-pressed', 'true');
    // 六個預設的顏色加「自訂顏色」。
    expect(within(group('顏色')).getAllByRole('button')).toHaveLength(7);
    expect(within(section).getByText(/只影響這台電腦的瀏覽器。/)).toBeInTheDocument();
  });

  it('選了位置、大小、顏色立刻換、記在這個瀏覽器', async () => {
    // Arrange
    const { user, group } = await openSection();

    // Act
    await user.click(within(group('位置')).getByRole('button', { name: '左下' }));
    await user.click(within(group('大小')).getByRole('button', { name: '大' }));
    await user.click(within(group('顏色')).getByRole('button', { name: '藍色' }));

    // Assert
    expect(within(group('位置')).getByRole('button', { name: '左下' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group('位置')).getByRole('button', { name: '右上' })).toHaveAttribute('aria-pressed', 'false');
    expect(within(group('大小')).getByRole('button', { name: '大' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group('顏色')).getByRole('button', { name: '藍色' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group('顏色')).getByRole('button', { name: '紅色' })).toHaveAttribute('aria-pressed', 'false');
    expect(readTabBadge()).toEqual({ position: 'bottom-left', size: 'large', color: 'blue', customColor: null });
    expect(localStorage.getItem(TAB_BADGE_STORAGE_KEY)).not.toBeNull();
  });

  it('讀到存好的設定', async () => {
    // Arrange
    localStorage.setItem(TAB_BADGE_STORAGE_KEY, JSON.stringify({ position: 'top-left', size: 'small', color: 'green', customColor: null }));

    // Act
    const { group } = await openSection();

    // Assert
    expect(within(group('位置')).getByRole('button', { name: '左上' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group('大小')).getByRole('button', { name: '小' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group('顏色')).getByRole('button', { name: '綠色' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('示意圖說出現在的樣子，跟著選的換', async () => {
    // Arrange
    const { user, group, preview } = await openSection();
    const before = preview().getAttribute('aria-label');

    // Act
    await user.click(within(group('位置')).getByRole('button', { name: '右下' }));
    await user.click(within(group('顏色')).getByRole('button', { name: '綠色' }));

    // Assert
    expect(before).toBe(`示意圖：分頁的圖示右上角有一個紅色的圓點（中），標題前面是未讀數：「(3) 我的工作 · ${PRODUCT_NAME}」。`);
    expect(preview()).toHaveAccessibleName(`示意圖：分頁的圖示右下角有一個綠色的圓點（中），標題前面是未讀數：「(3) 我的工作 · ${PRODUCT_NAME}」。`);
  });

  it('有未讀時，換了設定這個分頁的圖示馬上跟著換', async () => {
    // Arrange
    const { user, group } = await openSection(3);
    await waitFor(() => {
      expect(icon).toHaveAttribute('href', badgedFaviconUrl(DEFAULT_TAB_BADGE));
    });

    // Act
    await user.click(within(group('顏色')).getByRole('button', { name: '橘色' }));

    // Assert
    expect(icon).toHaveAttribute('href', badgedFaviconUrl({ ...DEFAULT_TAB_BADGE, color: 'orange' }));
  });

  it('按「自訂顏色」打開選擇器，從目前的顏色開始', async () => {
    // Arrange
    const { user, section, group } = await openSection();

    // Act
    await user.click(within(group('顏色')).getByRole('button', { name: /^自訂顏色/ }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '自訂顏色' });
    expect(within(dialog).getByRole('textbox', { name: '色碼' })).toHaveValue('#e5322b');
    expect(within(group('顏色')).getByRole('button', { name: /^自訂顏色/ })).toHaveAttribute('aria-pressed', 'true');
    expect(within(group('顏色')).getByRole('button', { name: '紅色' })).toHaveAttribute('aria-pressed', 'false');
    expect(readTabBadge()).toEqual({ ...DEFAULT_TAB_BADGE, color: 'custom', customColor: '#e5322b' });
    // 名稱帶上目前的自訂色碼，報讀軟體聽得出選的是什麼。
    expect(within(group('顏色')).getByRole('button', { name: '自訂顏色（#e5322b）' })).toBeInTheDocument();
    expect(within(section).getByRole('img', { name: /^示意圖/ })).toHaveAccessibleName(/自訂顏色（#e5322b）的圓點/);
  });

  it('在選擇器裡打色碼，分頁的圖示與示意圖馬上換', async () => {
    // Arrange
    const { user, group, preview } = await openSection(3);
    await user.click(within(group('顏色')).getByRole('button', { name: /^自訂顏色/ }));
    const field = within(await screen.findByRole('dialog', { name: '自訂顏色' })).getByRole('textbox', { name: '色碼' });

    // Act
    await user.clear(field);
    await user.type(field, '#00aa88');

    // Assert
    const expected = badgedFaviconUrl({ ...DEFAULT_TAB_BADGE, color: 'custom', customColor: '#00aa88' });
    expect(icon).toHaveAttribute('href', expected);
    expect(preview()).toHaveAccessibleName(/自訂顏色（#00aa88）的圓點/);
    // 取色器蓋住了下面的示意圖，裡面另外放一個跟著換的圖示（裝飾，名稱在示意圖）。
    const dialog = screen.getByRole('dialog', { name: '自訂顏色' });
    expect(within(dialog).getByRole('presentation')).toHaveAttribute('src', expected);
  });

  it('Esc 關上選擇器、焦點回到「自訂顏色」；選回預設的顏色之後，自訂的色碼還留著', async () => {
    // Arrange
    const { user, group } = await openSection();
    const custom = within(group('顏色')).getByRole('button', { name: /^自訂顏色/ });
    await user.click(custom);
    const field = within(await screen.findByRole('dialog', { name: '自訂顏色' })).getByRole('textbox', { name: '色碼' });
    await user.clear(field);
    await user.type(field, '#00aa88');

    // Act
    await user.keyboard('{Escape}');
    const focusedAfterEscape = document.activeElement;
    await user.click(within(group('顏色')).getByRole('button', { name: '綠色' }));
    await user.click(custom);

    // Assert
    expect(focusedAfterEscape).toBe(custom);
    expect(readTabBadge()).toEqual({ ...DEFAULT_TAB_BADGE, color: 'custom', customColor: '#00aa88' });
    const reopened = await screen.findByRole('dialog', { name: '自訂顏色' });
    expect(within(reopened).getByRole('textbox', { name: '色碼' })).toHaveValue('#00aa88');
  });
});
