import { describe, expect, it } from 'vitest';
import { safeNextPath } from './nextPath';

describe('safeNextPath', () => {
  it('站內的路徑照原樣保留，包含查詢字串與錨點', () => {
    // Arrange / Act / Assert
    expect(safeNextPath('/projects/WEB?view=table#top')).toBe('/projects/WEB?view=table#top');
  });

  it.each([
    ['沒有給', null],
    ['別的網站', 'https://evil.example.com/'],
    ['省略協定的別的網站', '//evil.example.com'],
    ['反斜線會被當成斜線', '/\\evil.example.com'],
    ['瀏覽器會刪掉網址裡的 Tab', '/\t/evil.example.com'],
    ['不是斜線開頭', 'projects'],
    ['登入頁本身', '/login?next=/projects'],
  ])('%s時回首頁', (_case, raw) => {
    // Arrange / Act / Assert
    expect(safeNextPath(raw)).toBe('/');
  });
});
