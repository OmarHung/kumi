import { act, render, screen } from '@testing-library/react';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SettingsSection, type SettingsSectionProps } from './SettingsSection';

function renderSection(props: Partial<SettingsSectionProps> = {}, path = '/') {
  const router = sectionRouter(props, path);
  return { ...render(<RouterProvider router={router} />), router };
}

function sectionRouter(props: Partial<SettingsSectionProps>, path: string) {
  return createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <SettingsSection title="密碼" {...props}>
            <button type="button">更新密碼</button>
          </SettingsSection>
        ),
      },
    ],
    { initialEntries: [path] },
  );
}

describe('SettingsSection', () => {
  afterEach(() => {
    // jsdom 沒有 scrollIntoView：測試放的 spy 要拿掉，不留到別的測試。
    Reflect.deleteProperty(Element.prototype, 'scrollIntoView');
  });

  it('是一個以標題命名的 region，標題是 h2，右欄的內容在裡面', () => {
    // Arrange / Act
    renderSection({ description: '至少 12 個字元。' });

    // Assert
    const region = screen.getByRole('region', { name: '密碼' });
    expect(region).toContainElement(screen.getByRole('heading', { level: 2, name: '密碼' }));
    expect(region).toContainElement(screen.getByRole('button', { name: '更新密碼' }));
    expect(region).toHaveTextContent('至少 12 個字元。');
  });

  it('descriptionId 與 headingId 標在說明與標題上，沒給 headingId 時自己產生並接到 region', () => {
    // Arrange / Act
    renderSection({ description: '說明一句', descriptionId: 'password-note', headingId: 'password-title' });

    // Assert
    expect(screen.getByText('說明一句')).toHaveAttribute('id', 'password-note');
    expect(screen.getByRole('heading', { name: '密碼' })).toHaveAttribute('id', 'password-title');
    expect(screen.getByRole('region')).toHaveAttribute('aria-labelledby', 'password-title');
  });

  it('沒給 headingId 時，region 的名稱仍然是標題', () => {
    // Arrange / Act
    renderSection();

    // Assert
    expect(screen.getByRole('region', { name: '密碼' })).toBeInTheDocument();
  });

  it('isContentStart 標 data-content-start，沒給就沒有', () => {
    // Arrange / Act
    const { unmount } = renderSection({ isContentStart: true });
    const marked = screen.getByRole('region', { name: '密碼' }).hasAttribute('data-content-start');
    unmount();
    renderSection();

    // Assert
    expect(marked).toBe(true);
    expect(screen.getByRole('region', { name: '密碼' })).not.toHaveAttribute('data-content-start');
  });

  it('網址的 #id 對到這一塊時，掛上之後捲過來', () => {
    // Arrange
    const scrollIntoView = vi.fn();
    Object.defineProperty(Element.prototype, 'scrollIntoView', { value: scrollIntoView, configurable: true, writable: true });

    // Act
    renderSection({ id: 'cli' }, '/account/integrations#cli');

    // Assert
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('region', { name: '密碼' })).toHaveAttribute('id', 'cli');
  });

  it('網址沒帶這一塊的錨點時不捲', () => {
    // Arrange
    const scrollIntoView = vi.fn();
    Object.defineProperty(Element.prototype, 'scrollIntoView', { value: scrollIntoView, configurable: true, writable: true });

    // Act
    renderSection({ id: 'cli' }, '/account/integrations#other');

    // Assert
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it('被錨點指到時焦點放到標題（按的連結跟著舊的頁面卸下，焦點要有去處）', () => {
    // Arrange / Act
    renderSection({ id: 'cli' }, '/account/integrations#cli');

    // Assert
    expect(screen.getByRole('heading', { level: 2, name: '密碼' })).toHaveFocus();
  });

  it('同一頁裡再導到同一個錨點也會再捲一次', async () => {
    // Arrange
    const scrollIntoView = vi.fn();
    Object.defineProperty(Element.prototype, 'scrollIntoView', { value: scrollIntoView, configurable: true, writable: true });
    const { router } = renderSection({ id: 'cli' }, '/account/integrations#cli');

    // Act
    await act(async () => {
      await router.navigate('/account/integrations#cli');
    });

    // Assert
    expect(scrollIntoView).toHaveBeenCalledTimes(2);
  });
});
