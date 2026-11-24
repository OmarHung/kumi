import { afterEach, describe, expect, it, vi } from 'vitest';
import source from '../../public/theme-init.js?raw';
import { THEME_CHOICES, THEME_STORAGE_KEY } from './theme';

// 手動選的主題（跟著系統以外的）：值從 lib/theme.ts 來，兩邊的選項不一致就會失敗。
const MANUAL_CHOICES = THEME_CHOICES.filter((choice) => choice !== 'system');

// public/theme-init.js 原樣複製到輸出、在 <head> 裡用一般的 <script src> 載入。
// 測試照瀏覽器的方式執行同一份內容（jsdom 會執行插進文件的 <script>），鍵名用 lib/theme.ts 的常數：兩邊不一致就會失敗。
function runThemeInit(): void {
  const script = document.createElement('script');
  script.textContent = source;
  document.head.append(script);
  script.remove();
}

afterEach(() => {
  document.documentElement.removeAttribute('data-theme');
});

describe('載入時套用主題（theme-init.js）', () => {
  it('手動的選項有淺色與深色', () => {
    // Arrange & Act & Assert
    expect(MANUAL_CHOICES).toEqual(['light', 'dark']);
  });

  it.each(MANUAL_CHOICES)('記住的是 %s 時設到 <html data-theme>', (choice) => {
    // Arrange
    localStorage.setItem(THEME_STORAGE_KEY, choice);

    // Act
    runThemeInit();

    // Assert
    expect(document.documentElement).toHaveAttribute('data-theme', choice);
  });

  it('沒有設定時跟著系統，不設 data-theme', () => {
    // Arrange & Act
    runThemeInit();

    // Assert
    expect(document.documentElement).not.toHaveAttribute('data-theme');
  });

  it('不認得的值當成跟著系統', () => {
    // Arrange
    localStorage.setItem(THEME_STORAGE_KEY, 'sepia');

    // Act
    runThemeInit();

    // Assert
    expect(document.documentElement).not.toHaveAttribute('data-theme');
  });

  it('讀不到 localStorage（私密瀏覽、被擋下）時跟著系統，不丟出錯誤', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const errors: unknown[] = [];
    const onError = (event: ErrorEvent) => {
      errors.push(event.error);
    };
    window.addEventListener('error', onError);

    // Act
    runThemeInit();
    window.removeEventListener('error', onError);

    // Assert
    expect(errors).toEqual([]);
    expect(document.documentElement).not.toHaveAttribute('data-theme');
  });
});
