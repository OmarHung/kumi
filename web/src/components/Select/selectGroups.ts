import type { SelectOption } from './Select';

/** 每一個選項所在的有名字的組，第一項的位置；不在有名字的組裡是 -1。帶 startsGroup 的選項開始一個沒有名字的組。 */
function groupStartsOf(all: readonly SelectOption[]): readonly number[] {
  let current = -1;
  return all.map((option, index) => {
    if (option.groupLabel !== undefined) current = index;
    else if (option.startsGroup === true) current = -1;
    return current;
  });
}

/**
 * 看得到的每一個選項前面要畫的組名（沒有是 undefined）。沒在篩選時就是選項自己帶的 groupLabel；
 * 篩選時一組的第一項可能被篩掉，組名改畫在那一組第一個看得到的選項前面，留下來的選項才看得出是哪一組的
 * （狀態的篩選依流程分組，兩個流程都有「內部驗收」）。positions 是看得到的選項在完整清單裡的位置。
 */
export function visibleGroupLabels(all: readonly SelectOption[], positions: readonly number[]): readonly (string | undefined)[] {
  const starts = groupStartsOf(all);
  return positions.map((position, index) => {
    const start = starts[position] ?? -1;
    const previous = index === 0 ? -1 : (starts[positions[index - 1] ?? -1] ?? -1);
    return start >= 0 && start !== previous ? all[start]?.groupLabel : undefined;
  });
}
