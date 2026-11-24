import { describe, expect, it } from 'vitest';
import type { SelectOption } from './Select';
import { visibleGroupLabels } from './selectGroups';

const OPTIONS: readonly SelectOption[] = [
  { value: '', label: '所有狀態' },
  { value: 'confirming', label: '需求確認', groupLabel: '需求' },
  { value: 'internal_qa', label: '內部驗收' },
  { value: 'todo', label: '待辦', groupLabel: '任務' },
  { value: 'internal_review', label: '內部驗收' },
];

describe('看得到的選項前面的組名', () => {
  it('沒在篩選：就是選項自己帶的組名', () => {
    // Arrange / Act
    const labels = visibleGroupLabels(OPTIONS, [0, 1, 2, 3, 4]);

    // Assert
    expect(labels).toEqual([undefined, '需求', undefined, '任務', undefined]);
  });

  it('篩選掉一組的第一項：組名畫在那一組第一個看得到的選項前面', () => {
    // Arrange / Act（打「內部」：只剩兩個「內部驗收」）
    const labels = visibleGroupLabels(OPTIONS, [2, 4]);

    // Assert
    expect(labels).toEqual(['需求', '任務']);
  });

  it('同一組留下兩項：組名只畫一次', () => {
    // Arrange / Act
    const labels = visibleGroupLabels(OPTIONS, [3, 4]);

    // Assert
    expect(labels).toEqual(['任務', undefined]);
  });

  it('不在有名字的組裡的選項不畫組名；startsGroup 開始的是沒有名字的組', () => {
    // Arrange
    const options: readonly SelectOption[] = [
      { value: 'a', label: 'A', groupLabel: '第一組' },
      { value: 'b', label: 'B', startsGroup: true },
      { value: 'c', label: 'C' },
    ];

    // Act
    const labels = visibleGroupLabels(options, [2]);

    // Assert
    expect(labels).toEqual([undefined]);
  });
});
