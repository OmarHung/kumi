import { describe, expect, it } from 'vitest';
import { isInboxPath, openItemKeysOf } from './openItem';

describe('畫面上開著的項目', () => {
  it('項目頁是網址裡的項目編號（大寫）', () => {
    expect(openItemKeysOf('/projects/SMK/items/smk-7', '')).toEqual(['SMK-7']);
  });

  it('清單旁邊的面板與浮動視窗是網址的 ?item=', () => {
    expect(openItemKeysOf('/projects/SMK', '?view=board&item=SMK-9')).toEqual(['SMK-9']);
    expect(openItemKeysOf('/my-work', '?item=agy-3')).toEqual(['AGY-3']);
  });

  it('項目頁上又開著面板時兩個都算', () => {
    expect(openItemKeysOf('/projects/SMK/items/SMK-7', '?item=SMK-8')).toEqual(['SMK-7', 'SMK-8']);
  });

  it('沒有開著項目的頁面是空的', () => {
    expect(openItemKeysOf('/projects/SMK', '')).toEqual([]);
    expect(openItemKeysOf('/inbox', '?item=')).toEqual([]);
  });

  it('只有收件匣頁（含底下的路徑）算在收件匣', () => {
    expect(isInboxPath('/inbox')).toBe(true);
    expect(isInboxPath('/inbox/')).toBe(true);
    expect(isInboxPath('/inbox-archive')).toBe(false);
    expect(isInboxPath('/my-work')).toBe(false);
  });
});
