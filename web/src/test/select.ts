import { waitFor, within } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';

/*
 * 操作自己做的下拉（components/Select）的測試輔助，取代原生下拉的 user.selectOptions（internal/HANDOFF-custom-controls.md 3A.2）。
 * 清單只有打開時才在 DOM 裡，而且 CI 比本機慢：打開之後一律等清單與選項出現，不用同步的 getBy*。
 */

/** 按鈕的 aria-controls 指到的清單；還沒打開（或已經關上）時丟錯，給 waitFor 重試。 */
function listboxOf(select: HTMLElement): HTMLElement {
  const id = select.getAttribute('aria-controls');
  const listbox = id === null ? null : document.getElementById(id);
  if (listbox === null || select.getAttribute('aria-expanded') !== 'true') {
    throw new Error(`下拉「${select.getAttribute('aria-label') ?? select.id}」的清單沒有打開`);
  }
  return listbox;
}

async function openListbox(user: UserEvent, select: HTMLElement): Promise<HTMLElement> {
  if (select.getAttribute('aria-expanded') !== 'true') {
    await user.click(select);
  }
  return waitFor(() => listboxOf(select));
}

/** 先找名稱一樣的選項，沒有再找 value（data-value）一樣的；找到兩個以上是測試寫得不夠清楚。 */
function optionIn(listbox: HTMLElement, option: string): HTMLElement {
  const byName = within(listbox).queryAllByRole('option', { name: option });
  const candidates = byName.length > 0 ? byName : within(listbox).queryAllByRole('option').filter((element) => element.dataset.value === option);
  const [found, ...rest] = candidates;
  if (found === undefined) throw new Error(`清單裡沒有「${option}」這個選項`);
  if (rest.length > 0) throw new Error(`清單裡有 ${String(candidates.length)} 個「${option}」`);
  return found;
}

/** 打開下拉（還沒開的話）再點選項，和使用者的操作一樣。option 可以是選項的文字或 value。 */
export async function chooseOption(user: UserEvent, select: HTMLElement, option: string): Promise<void> {
  const listbox = await openListbox(user, select);
  const target = await waitFor(() => optionIn(listbox, option));
  await user.click(target);
}

/**
 * 打開下拉（還沒開的話），回傳照順序排的選項元素。看完不會自己關。
 * 只回真的選項（帶 data-value）：「沒有可以選的」「沒有符合的選項」那一列說明也是 role="option"（給報讀軟體），不算。
 */
export async function openOptions(user: UserEvent, select: HTMLElement): Promise<HTMLElement[]> {
  const listbox = await openListbox(user, select);
  return within(listbox)
    .queryAllByRole('option')
    .filter((option) => option.dataset.value !== undefined);
}
