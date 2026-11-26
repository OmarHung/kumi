import { describe, expect, it } from 'vitest';
import type { SavedView, SavedViewQuery } from '../api/types';
import { agyDetail } from '../test/fixtures';
import { EMPTY_FILTERS } from './itemFilters';
import { queryFromTableFilters, savedViewParams, viewMatches } from './savedViews';

const BLUEPRINT = agyDetail().blueprint;
const NOTHING: SavedViewQuery = { type: null, state: null, assignee: null, q: null, sort: null, due: null, priority: null, cycle: null, milestone: null, fields: null, group: null, category: null, project: null, createdBy: null, mine: null };

function savedView(layout: SavedView['layout'], query: Partial<SavedViewQuery>): SavedView {
  return { id: 'v-1', name: '停滯的', layout, query: { ...NOTHING, ...query }, createdAt: '2026-11-18T01:00:00Z', updatedAt: '2026-11-18T01:00:00Z' };
}

describe('存的檢視：還沒結束、停滯、7 天內到期', () => {
  it('表格存 open 與 stale（存的是這個說法，不是日期），沒開時不放進去', () => {
    // Arrange / Act
    const both = queryFromTableFilters({ ...EMPTY_FILTERS, open: true, stale: true });
    const none = queryFromTableFilters(EMPTY_FILTERS);

    // Assert
    expect(both).toEqual({ ...NOTHING, open: true, stale: true });
    expect(none).toEqual(NOTHING);
    expect(none).not.toHaveProperty('open');
  });

  it('到期 soon 照原樣存、套用時寫回網址', () => {
    // Arrange / Act
    const query = queryFromTableFilters({ ...EMPTY_FILTERS, due: 'soon' });

    // Assert
    expect(query.due).toBe('soon');
    expect(savedViewParams(savedView('table', { due: 'soon' }), BLUEPRINT).toString()).toBe('due=soon');
  });

  it('套用時 open、stale 寫回網址；舊的檢視（沒有這兩個欄位）不受影響', () => {
    // Arrange
    const withFlags = savedView('table', { open: true, stale: true });
    const legacy = { ...savedView('table', {}), query: { ...NOTHING } };

    // Act / Assert
    expect(savedViewParams(withFlags, BLUEPRINT).toString()).toBe('open=1&stale=1');
    expect(savedViewParams(legacy, BLUEPRINT).toString()).toBe('');
  });

  it('看板、行事曆與時間軸不帶 open、stale（只有表格有）', () => {
    // Arrange / Act / Assert
    expect(savedViewParams(savedView('board', { open: true, stale: true }), BLUEPRINT).has('open')).toBe(false);
    expect(savedViewParams(savedView('calendar', { stale: true }), BLUEPRINT).has('stale')).toBe(false);
  });

  it('目前的條件和存的檢視一樣就是同一個檢視，多了停滯就不是', () => {
    // Arrange
    const view = savedView('table', { open: true });
    const same = { layout: 'table', query: queryFromTableFilters({ ...EMPTY_FILTERS, open: true }) } as const;
    const more = { layout: 'table', query: queryFromTableFilters({ ...EMPTY_FILTERS, open: true, stale: true }) } as const;

    // Act / Assert
    expect(viewMatches(view, same, BLUEPRINT)).toBe(true);
    expect(viewMatches(view, more, BLUEPRINT)).toBe(false);
  });
});
