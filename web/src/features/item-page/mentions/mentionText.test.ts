import { describe, expect, it } from 'vitest';
import type { Member } from '../../../api/types';
import { filterMembers, findMentionTrigger, insertMention } from './mentionText';

function member(personId: string, displayName: string): Member {
  return { personId, displayName, role: 'editor' };
}

describe('findMentionTrigger', () => {
  it.each([
    ['@', 1, 0, ''],
    ['@小', 2, 0, '小'],
    ['請 @小明', 5, 2, '小明'],
    ['@John Smith', 11, 0, 'John Smith'],
    ['請 @John ', 8, 2, 'John '],
    ['請@小明', 4, 1, '小明'],
    ['第一行\n@wa', 7, 4, 'wa'],
  ])('%j 游標在 %i：從 %i 開始，打了「%s」', (value, caret, start, query) => {
    expect(findMentionTrigger(value, caret)).toEqual({ start, query });
  });

  it.each([
    ['沒有 @', 'abc', 3],
    ['email 的 @（前面是英數字）', 'user@example', 12],
    ['@ 後面已經有兩個空白', '@小明 你 好', 7],
    ['@ 後面馬上是空白', '@ 小明', 4],
    ['@ 後面已經有換行', '@小明\n你好', 5],
    ['游標在 @ 前面', '@小明', 0],
    ['已經是提及的連結', '@[小明](mention:p-1)', 20],
    ['@@', '@@', 2],
    ['打太長', `@${'a'.repeat(31)}`, 32],
  ])('%s 不是提及', (_name, value, caret) => {
    expect(findMentionTrigger(value, caret)).toBeNull();
  });

  it('游標在字中間時只看游標前面的字', () => {
    expect(findMentionTrigger('@小明 你好', 2)).toEqual({ start: 0, query: '小' });
  });
});

describe('filterMembers', () => {
  const members = [member('1', '王小明'), member('2', '陳小華'), member('3', 'Alice Wang'), member('4', '小林')];

  it('沒打字時是全部', () => {
    expect(filterMembers(members, '')).toEqual(members);
  });

  it('名稱包含打的字就符合，不分大小寫', () => {
    expect(filterMembers(members, 'WANG').map((entry) => entry.personId)).toEqual(['3']);
    expect(filterMembers(members, '華').map((entry) => entry.personId)).toEqual(['2']);
  });

  it('開頭符合的排在前面，同一組維持原本的順序', () => {
    expect(filterMembers(members, '小').map((entry) => entry.personId)).toEqual(['4', '1', '2']);
  });

  it('沒有符合的是空陣列', () => {
    expect(filterMembers(members, '不存在')).toEqual([]);
  });
});

describe('insertMention', () => {
  const wang = member('p-1', '王小明');

  it('把 @ 與打的字換成 @[名稱](mention:編號) 加一個空白，游標在空白後面', () => {
    expect(insertMention('請 @小 看', { start: 2, query: '小' }, wang)).toEqual({
      value: '請 @[王小明](mention:p-1)  看',
      caret: '請 @[王小明](mention:p-1) '.length,
    });
  });

  it('游標後面接著的字不動', () => {
    expect(insertMention('@小明和他', { start: 0, query: '小明' }, wang).value).toBe('@[王小明](mention:p-1) 和他');
  });

  it('名稱裡的方括號與反斜線拿掉，不會弄壞連結的寫法', () => {
    expect(insertMention('@', { start: 0, query: '' }, member('p-2', '王[小]\\明')).value).toBe('@[王小明](mention:p-2) ');
  });

  it.each(['`', '*', '_', '<'])('名稱裡的 %s 拿掉，不會變成行內程式碼、粗斜體或 HTML 而弄壞後面的連結', (symbol) => {
    expect(insertMention('@', { start: 0, query: '' }, member('p-4', `王${symbol}明`)).value).toBe('@[王明](mention:p-4) ');
  });

  it('名稱剪完是空的就寫「成員」', () => {
    expect(insertMention('@', { start: 0, query: '' }, member('p-3', '[]')).value).toBe('@[成員](mention:p-3) ');
  });
});
