import { describe, expect, it } from 'vitest';
import { smkItem } from '../../test/fixtures';
import { groupAnnouncements, groupDropTarget, groupInstructions } from './groupDnd';

const card = smkItem({ number: 1, fields: { category: 'bug' } });
const keyOf = (item: typeof card) => (typeof item.fields.category === 'string' ? item.fields.category : '_none');

function active(data: unknown) {
  return { data: { current: data as Record<string, unknown> } };
}

function over(columnKey: string, blockedReason: string | null = null) {
  return { data: { current: { kind: 'group-column', columnKey, name: columnKey, blockedReason } } };
}

describe('groupDropTarget', () => {
  it('放到別的欄就改成那一欄', () => {
    expect(groupDropTarget(keyOf, active({ kind: 'card', item: card }), over('feature'))).toEqual({ item: card, to: 'feature' });
  });

  it('放回原欄、拖出看板、放不進去的欄（類型沒有這個欄位）都不送', () => {
    expect(groupDropTarget(keyOf, active({ kind: 'card', item: card }), over('bug'))).toBeNull();
    expect(groupDropTarget(keyOf, active({ kind: 'card', item: card }), null)).toBeNull();
    expect(groupDropTarget(keyOf, active({ kind: 'card', item: card }), over('feature', '這個類型沒有「分類」'))).toBeNull();
  });

  it('不是卡片或不是欄的資料不理會', () => {
    expect(groupDropTarget(keyOf, active(undefined), over('feature'))).toBeNull();
    expect(groupDropTarget(keyOf, active({ kind: 'card', item: card }), { data: { current: { kind: 'column' } } })).toBeNull();
  });
});

describe('groupAnnouncements', () => {
  it('用繁體中文說明拖到哪一欄、放不放得進去、放下之後會不會改', () => {
    // Arrange
    const dragged = { ...active({ kind: 'card', item: card }), id: card.key, rect: { current: { initial: null, translated: null } } };
    const column = (key: string, reason: string | null = null) => ({
      ...over(key, reason),
      id: key,
      rect: { width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0 },
      disabled: false,
    });
    const announcements = groupAnnouncements('分類', (a, o) => groupDropTarget(keyOf, a, o) !== null);

    // Act / Assert
    expect(announcements.onDragOver({ active: dragged, over: column('feature') })).toBe('SMK-1「首頁設計稿」 在「feature」欄上。');
    expect(announcements.onDragOver({ active: dragged, over: column('chore', '這個類型沒有「分類」') })).toBe(
      'SMK-1「首頁設計稿」 在「chore」欄上，放不進去：這個類型沒有「分類」。',
    );
    expect(announcements.onDragEnd({ active: dragged, over: column('feature') })).toBe('');
    expect(announcements.onDragEnd({ active: dragged, over: column('bug') })).toBe('SMK-1「首頁設計稿」 放回原處，分類沒有改。');
  });

  it('操作說明帶分組的名稱', () => {
    expect(groupInstructions('負責人').draggable).toContain('別的負責人欄');
  });
});
