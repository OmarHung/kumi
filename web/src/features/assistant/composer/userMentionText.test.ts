import { describe, expect, it } from 'vitest';
import { plainMentions } from './userMentionText';

describe('使用者訊息裡的提及', () => {
  it('@[名稱](mention:編號) 顯示與複製成 @名稱', () => {
    expect(plainMentions('請 @[小明](mention:p-1) 和 @[John Smith](mention:p-2) 看')).toBe('請 @小明 和 @John Smith 看');
  });

  it('不是提及的連結不動', () => {
    expect(plainMentions('[文件](https://example.com)')).toBe('[文件](https://example.com)');
  });
});
