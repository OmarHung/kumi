import type { KeyboardEvent } from 'react';
import { describe, expect, it } from 'vitest';
import { resizeKeyTarget, type KeyResizeTarget } from './resizeKeys';

const TARGET: Omit<KeyResizeTarget, 'edge'> = { width: 800, min: 640, max: 1600 };

function keyOn(key: string, { shiftKey = false, dir = 'ltr' }: { readonly shiftKey?: boolean; readonly dir?: 'ltr' | 'rtl' } = {}) {
  const element = document.createElement('div');
  element.dir = dir;
  document.body.append(element);
  // 只用到這幾個屬性（lib/keyboard.ts 的 isComposingKey 看 nativeEvent）。
  return { key, shiftKey, altKey: false, ctrlKey: false, metaKey: false, currentTarget: element, nativeEvent: { isComposing: false } } as unknown as KeyboardEvent<HTMLElement>;
}

describe('resizeKeyTarget：調整寬度的把手上的按鍵', () => {
  it.each([
    ['start', 'ArrowLeft', 816],
    ['start', 'ArrowRight', 784],
    ['end', 'ArrowRight', 816],
    ['end', 'ArrowLeft', 784],
  ] as const)('把手在 %s 那一邊：%s 是 %i（往把手那一邊按是加寬）', (edge, key, expected) => {
    // Arrange / Act
    const width = resizeKeyTarget(keyOn(key), { ...TARGET, edge });

    // Assert
    expect(width).toBe(expected);
  });

  it('從右到左的文字方向，左右反過來；Shift 一次 64px', () => {
    // Arrange / Act / Assert
    expect(resizeKeyTarget(keyOn('ArrowLeft', { dir: 'rtl' }), { ...TARGET, edge: 'end' })).toBe(816);
    expect(resizeKeyTarget(keyOn('ArrowRight', { shiftKey: true }), { ...TARGET, edge: 'end' })).toBe(864);
  });

  it('Home 最窄、End 最寬（沒有上限時不動）、Enter 回到預設；其他按鍵與 Shift 加別的鍵不處理', () => {
    // Arrange / Act / Assert
    expect(resizeKeyTarget(keyOn('Home'), { ...TARGET, edge: 'end' })).toBe(640);
    expect(resizeKeyTarget(keyOn('End'), { ...TARGET, edge: 'end' })).toBe(1600);
    expect(resizeKeyTarget(keyOn('End'), { ...TARGET, max: null, edge: 'end' })).toBe(800);
    expect(resizeKeyTarget(keyOn('Enter'), { ...TARGET, edge: 'end' })).toBe('reset');
    expect(resizeKeyTarget(keyOn('a'), { ...TARGET, edge: 'end' })).toBeNull();
    expect(resizeKeyTarget(keyOn('Home', { shiftKey: true }), { ...TARGET, edge: 'end' })).toBeNull();
  });
});

describe('resizeKeyTarget：調整高度的把手（axis: y）', () => {
  it.each([
    ['start', 'ArrowUp', 816],
    ['start', 'ArrowDown', 784],
    ['end', 'ArrowDown', 816],
    ['end', 'ArrowUp', 784],
  ] as const)('把手在 %s 那一邊：%s 是 %i（往把手那一邊按是加高）', (edge, key, expected) => {
    // Arrange / Act
    const height = resizeKeyTarget(keyOn(key), { ...TARGET, edge, axis: 'y' });

    // Assert
    expect(height).toBe(expected);
  });

  it('不看文字方向，左右鍵不處理；Shift 一次 64px；Home／End／Enter 同寬度', () => {
    // Arrange / Act / Assert
    expect(resizeKeyTarget(keyOn('ArrowUp', { dir: 'rtl' }), { ...TARGET, edge: 'start', axis: 'y' })).toBe(816);
    expect(resizeKeyTarget(keyOn('ArrowLeft'), { ...TARGET, edge: 'start', axis: 'y' })).toBeNull();
    expect(resizeKeyTarget(keyOn('ArrowUp', { shiftKey: true }), { ...TARGET, edge: 'start', axis: 'y' })).toBe(864);
    expect(resizeKeyTarget(keyOn('Home'), { ...TARGET, edge: 'start', axis: 'y' })).toBe(640);
    expect(resizeKeyTarget(keyOn('End'), { ...TARGET, edge: 'start', axis: 'y' })).toBe(1600);
    expect(resizeKeyTarget(keyOn('Enter'), { ...TARGET, edge: 'start', axis: 'y' })).toBe('reset');
  });
});
