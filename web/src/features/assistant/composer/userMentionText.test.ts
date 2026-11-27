import { describe, expect, it } from 'vitest';
import { plainMentions, plainTitle } from './userMentionText';

describe('使用者訊息裡的提及', () => {
  it('@[名稱](mention:編號) 顯示與複製成 @名稱', () => {
    expect(plainMentions('請 @[小明](mention:p-1) 和 @[John Smith](mention:p-2) 看')).toBe('請 @小明 和 @John Smith 看');
  });

  it('不是提及的連結不動', () => {
    expect(plainMentions('[文件](https://example.com)')).toBe('[文件](https://example.com)');
  });
});

describe('plainTitle', () => {
  it('標題在提及中間被截斷時，照樣顯示成 @名稱', () => {
    // Arrange：伺服器早期存的標題是第一則訊息的前 40 個字，可能切在編號中間。
    const title = '請 @[Ivan](mention:019b2c3d-4e5f-7a6b-8c7';

    // Act／Assert
    expect(plainTitle(title)).toBe('請 @Ivan');
  });

  it('完整的提及與一般文字照 plainMentions', () => {
    expect(plainTitle('請 @[Ivan](mention:019b2c3d-4e5f-7a6b-8c7d-9e0f1a2b3c4d) 負責')).toBe('請 @Ivan 負責');
    expect(plainTitle('一般的標題')).toBe('一般的標題');
  });
});
