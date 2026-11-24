import { describe, expect, it } from 'vitest';
import { simpleTasksBlueprint } from './blueprints';
import { matchesFieldParams, sortByFieldParam } from './fieldQuery';
import { smkItem } from './fixtures';

const ME = '0190a000-0000-7000-8000-000000000001';
const params = (text: string) => new URLSearchParams(text);

const bug = smkItem({ number: 1, fields: { category: 'bug', tags: ['docs', 'backend'], blocked: true, reviewer: ME } });
const feature = smkItem({ number: 2, fields: { category: 'feature', tags: [] } });
const bare = smkItem({ number: 3 });

describe('假伺服器的自訂欄位篩選', () => {
  const found = (query: string) => [bug, feature, bare].filter((item) => matchesFieldParams(item, params(query), ME)).map((item) => item.number);

  it('單選：等於其中一個；同一個欄位的多個值是「或」', () => {
    expect(found('field.category=bug')).toEqual([1]);
    expect(found('field.category=bug&field.category=feature')).toEqual([1, 2]);
  });

  it('多選：選了其中任何一個；_none 也符合空陣列', () => {
    expect(found('field.tags=backend')).toEqual([1]);
    expect(found('field.tags=_none')).toEqual([2, 3]);
  });

  it('人員：me 換成自己；_none 是沒有人', () => {
    expect(found('field.reviewer=me')).toEqual([1]);
    expect(found(`field.reviewer=${ME}`)).toEqual([1]);
    expect(found('field.reviewer=_none')).toEqual([2, 3]);
  });

  it('勾選：false 是沒有存這個欄位；不同欄位之間是「而且」', () => {
    expect(found('field.blocked=true')).toEqual([1]);
    expect(found('field.blocked=false')).toEqual([2, 3]);
    expect(found('field.blocked=false&field.category=feature')).toEqual([2]);
  });

  it('排序不當成條件', () => {
    expect(found('sort=field.category:asc')).toEqual([1, 2, 3]);
  });
});

describe('假伺服器的自訂欄位排序', () => {
  const numbers = (items: readonly { number: number }[]) => items.map((item) => item.number);

  it('沒有排序或不是自訂欄位時照編號遞增', () => {
    expect(numbers(sortByFieldParam([feature, bug], null))).toEqual([1, 2]);
    expect(numbers(sortByFieldParam([feature, bug], 'title:desc'))).toEqual([1, 2]);
  });

  it('沒有值的排最後，兩個方向都是；同值依編號', () => {
    const points = [smkItem({ number: 1, fields: { story_points: 5 } }), smkItem({ number: 2 }), smkItem({ number: 3, fields: { story_points: 2 } }), smkItem({ number: 4, fields: { story_points: 5 } })];

    expect(numbers(sortByFieldParam(points, 'field.story_points:asc'))).toEqual([3, 1, 4, 2]);
    expect(numbers(sortByFieldParam(points, 'field.story_points:desc'))).toEqual([1, 4, 3, 2]);
  });

  it('單選依藍圖的選項順序，勾選沒勾的在前', () => {
    const items = [smkItem({ number: 1, fields: { category: 'chore' } }), smkItem({ number: 2, fields: { category: 'feature' } }), smkItem({ number: 3, fields: { category: 'bug' } })];
    const flags = [smkItem({ number: 1, fields: { blocked: true } }), smkItem({ number: 2 })];

    expect(numbers(sortByFieldParam(items, 'field.category:asc', simpleTasksBlueprint))).toEqual([2, 3, 1]);
    expect(numbers(sortByFieldParam(flags, 'field.blocked:asc', simpleTasksBlueprint))).toEqual([2, 1]);
  });
});
