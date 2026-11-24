import { describe, expect, it } from 'vitest';
import { simpleTasksBlueprint } from '../test/blueprints';
import { EMPTY_FILTERS } from './itemFilters';
import { knownBoardFields, knownItemFilters, knownSort } from './knownFilters';

describe('knownSort', () => {
  it('核心欄位的排序原樣留著', () => {
    expect(knownSort({ field: 'title', direction: 'asc' }, simpleTasksBlueprint)).toEqual({ field: 'title', direction: 'asc' });
  });

  it('能排的自訂欄位留著', () => {
    expect(knownSort({ field: 'field.story_points', direction: 'desc' }, simpleTasksBlueprint)).toEqual({ field: 'field.story_points', direction: 'desc' });
  });

  it.each(['field.reviewer', 'field.tags', 'field.notes', 'field.gone'])('%s 不在或不能排時拿掉排序', (field) => {
    expect(knownSort({ field: field as `field.${string}`, direction: 'asc' }, simpleTasksBlueprint)).toBeNull();
  });

  it('沒有排序還是沒有', () => {
    expect(knownSort(null, simpleTasksBlueprint)).toBeNull();
  });
});

describe('knownItemFilters', () => {
  it('去掉不在藍圖裡與不能篩的欄位條件，也一併處理排序', () => {
    const filters = { ...EMPTY_FILTERS, fields: { category: 'bug', gone: 'x', summary: 'y' }, sort: { field: 'field.gone', direction: 'asc' } } as const;

    expect(knownItemFilters(filters, simpleTasksBlueprint)).toEqual({ ...EMPTY_FILTERS, fields: { category: 'bug' }, sort: null });
  });
});

describe('knownBoardFields', () => {
  it('只動欄位條件', () => {
    const filters = { q: 'a', fields: { blocked: 'true', gone: 'x' } };

    expect(knownBoardFields(filters, simpleTasksBlueprint)).toEqual({ q: 'a', fields: { blocked: 'true' } });
  });
});
