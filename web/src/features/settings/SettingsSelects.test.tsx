import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { signedInAs } from '../../test/api';
import { captureProposals, serveChangeSets } from '../../test/changesets';
import { agyDetail, exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';

/*
 * 設定頁的下拉（components/Select，docs/DESIGN.md 7.3）：列上的與對話框裡的都只更新草稿或表單的值，
 * 只有明確選了才變（方向鍵、打字只移動反白，Esc 關上）；送出還是靠「預覽變更」或對話框的「加入」。
 * 狀態類別與搬移目標的選項前面是狀態類別的形狀，同一個清單裡每一個都有小圖。
 */

async function openSettings(detail: ProjectDetail, section = 'workflows') {
  signedInAs(exampleOwner, { details: [detail] });
  serveChangeSets(detail.project.key, []);
  const proposals = captureProposals(detail);
  const view = renderApp(`/projects/${detail.project.key}/settings/${section}`);
  await screen.findByRole('heading', { level: 1, name: '設定' });
  return { ...view, proposals };
}

/** 選項或按鈕前面的小圖裡的形狀（Icon 的 data-icon）；沒有小圖時是 null。 */
function shapeOf(element: HTMLElement): string | null {
  return element.querySelector('[aria-hidden="true"] [data-icon]')?.getAttribute('data-icon') ?? null;
}

/** 選項前面有沒有小圖：Select 把小圖包在 aria-hidden 的 span 裡，是選項的第一個子元素。 */
function hasLeading(option: HTMLElement): boolean {
  return option.firstElementChild?.getAttribute('aria-hidden') === 'true';
}

describe('設定頁的下拉：選了才變', () => {
  it('狀態的類別：方向鍵只移動反白、Esc 關上，草稿不變；打開後按 Enter 選了才改', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    const category = screen.getByRole('combobox', { name: '「進行中」的類別' });
    category.focus();

    // Act
    await user.keyboard('{ArrowDown}{ArrowDown}');
    await screen.findByRole('listbox', { name: '「進行中」的類別' });
    await user.keyboard('{Escape}');

    // Assert
    expect(category).toHaveAttribute('aria-expanded', 'false');
    expect(category).toHaveTextContent('進行中');
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();

    // Act：進行中的下一個是完成。
    await user.keyboard('{ArrowDown}');
    await screen.findByRole('listbox', { name: '「進行中」的類別' });
    await user.keyboard('{ArrowDown}{Enter}');

    // Assert
    expect(category).toHaveTextContent('完成');
    expect(category).toHaveFocus();
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
  });

  it('類型的流程：點在外面關上清單，不換流程、不出現搬移', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail(), 'types');
    const workflow = screen.getByRole('combobox', { name: '類型「需求」的流程' });
    await openOptions(user, workflow);

    // Act
    await user.click(screen.getByRole('heading', { level: 1, name: '設定' }));

    // Assert
    expect(workflow).toHaveAttribute('aria-expanded', 'false');
    expect(workflow).toHaveTextContent('交付流程');
    expect(screen.queryByRole('combobox', { name: /^類型「需求」停在/ })).not.toBeInTheDocument();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });
});

describe('設定頁的下拉：狀態的形狀', () => {
  it('類別的選項都帶類別的形狀，按鈕上目前的值也是', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    const category = screen.getByRole('combobox', { name: '「完成」的類別' });

    // Act
    const options = await openOptions(user, category);

    // Assert
    expect(options.map((option) => shapeOf(option))).toEqual(['stateBacklog', 'stateNotStarted', 'stateInProgress', 'stateDone', 'stateCanceled']);
    expect(shapeOf(category)).toBe('stateDone');
  });

  it('刪除狀態的搬移：還沒選時按鈕上是灰字的「選擇狀態」（不是選項）；每個選項都帶它的類別的形狀', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '刪除「進行中」' }));
    const target = screen.getByRole('combobox', { name: '停在「進行中」的項目搬到' });

    // Act
    const options = await openOptions(user, target);

    // Assert
    expect(target).toHaveTextContent('選擇狀態');
    expect(options.every(hasLeading)).toBe(true);
    expect(options.map((option) => shapeOf(option))).toEqual(['stateNotStarted', 'stateDone']);
    await chooseOption(user, target, '完成');
    await waitFor(() => {
      expect(shapeOf(target)).toBe('stateDone');
    });
  });

  it('類型換流程的搬移：選項是新流程的狀態、都帶類別的形狀；預先選好的值在按鈕上也帶形狀', async () => {
    // Arrange
    const { user } = await openSettings(agyDetail(), 'types');
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「任務」的流程' }), '交付流程');
    const move = screen.getByRole('combobox', { name: '類型「任務」停在「完成」的項目搬到' });

    // Act
    const options = await openOptions(user, move);

    // Assert
    expect(options.every(hasLeading)).toBe(true);
    expect(options.map((option) => option.dataset.value)).toEqual(['confirming', 'quoting', 'designing', 'developing', 'internal_qa', 'client_qa', 'closed']);
    expect(options.map((option) => shapeOf(option))).toEqual([
      'stateNotStarted',
      'stateNotStarted',
      'stateInProgress',
      'stateInProgress',
      'stateInProgress',
      'stateInProgress',
      'stateDone',
    ]);
    expect(move).toHaveTextContent('結案');
    expect(shapeOf(move)).toBe('stateDone');
  });

  it('類型換流程的搬移沒有預選：按鈕上是灰字的「選擇狀態」（不是選項），選了之後就是那個狀態', async () => {
    // Arrange：舊流程只有「開放」與「結束」；新流程沒有「開放」這個 key，類別也對不上兩個類型，不預選。
    const flow = (key: string, name: string, category: 'not_started' | 'in_progress') => ({
      key,
      name,
      states: [{ key: 'open', name: '開放', category }, { key: 'end', name: '結束', category: 'done' as const }],
    });
    const detail = agyDetail();
    const blueprint = {
      ...detail.blueprint,
      fields: [],
      workflows: [
        flow('alpha', '甲流程', 'not_started'),
        flow('beta', '乙流程', 'in_progress'),
        { key: 'gamma', name: '新流程', states: [{ key: 'new', name: '新的', category: 'not_started' as const }, { key: 'finish', name: '完成', category: 'done' as const }] },
      ],
      itemTypes: [
        { key: 'first', name: '第一種', workflow: 'alpha', fields: [], parents: [] },
        { key: 'second', name: '第二種', workflow: 'beta', fields: [], parents: [] },
      ],
      roles: [],
    };
    const { user } = await openSettings(agyDetail({ blueprint }), 'types');
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「第一種」的流程' }), '新流程');
    await chooseOption(user, screen.getByRole('combobox', { name: '類型「第二種」的流程' }), '新流程');
    const move = screen.getByRole('combobox', { name: '類型「第一種」停在「開放」的項目搬到' });
    expect(move).toHaveTextContent('選擇狀態');

    // Act
    const options = await openOptions(user, move);
    await chooseOption(user, move, '新的');

    // Assert
    expect(options.map((option) => option.dataset.value)).toEqual(['new', 'finish']);
    expect(move).toHaveTextContent('新的');
    expect(screen.getByRole('combobox', { name: '類型「第二種」停在「開放」的項目搬到' })).toHaveTextContent('新的');
  });
});

describe('對話框裡的下拉', () => {
  it('新增狀態：清單開著時 Esc 只關清單，對話框還在；關著時 Enter 打開清單，不送出', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '在「任務流程」新增狀態' }));
    const dialog = screen.getByRole('dialog', { name: '新增狀態' });
    await user.type(within(dialog).getByLabelText('名稱'), 'Review');
    const category = within(dialog).getByRole('combobox', { name: '類別' });
    category.focus();

    // Act
    await user.keyboard('{Enter}');
    await within(dialog).findByRole('listbox', { name: '類別' });
    await user.keyboard('{Escape}');

    // Assert
    expect(category).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('dialog', { name: '新增狀態' })).toBeInTheDocument();
    expect(category).toHaveFocus();
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
  });

  it('新增狀態：選了類別只是填好表單，按「加入」才進草稿', async () => {
    // Arrange
    const { user } = await openSettings(smkDetail());
    await user.click(screen.getByRole('button', { name: '在「任務流程」新增狀態' }));
    const dialog = screen.getByRole('dialog', { name: '新增狀態' });
    await user.type(within(dialog).getByLabelText('名稱'), 'Review');

    // Act
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '類別' }), '待規劃');

    // Assert
    expect(within(dialog).getByRole('combobox', { name: '類別' })).toHaveTextContent('待規劃');
    expect(screen.getByText('還沒有修改')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: '加入' }));
    expect(screen.getByRole('combobox', { name: '「review」的類別' })).toHaveTextContent('待規劃');
    expect(screen.getByText('還沒送出的修改：1 項')).toBeInTheDocument();
  });
});
