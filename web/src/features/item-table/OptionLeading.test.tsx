import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { http } from 'msw';
import { ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { memberPerson, ownerPerson } from '../../test/people';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

/*
 * 下拉的選項前面的小圖（docs/DESIGN.md 7.3）：狀態是狀態類別的形狀、人員是頭像；同一個清單裡要嘛全部有、要嘛全部沒有，
 * 字才對齊（「所有人」「沒有負責人」「所有狀態」這類用佔位的小圖）。選到的值在按鈕上也畫同一個小圖。
 * 另外確認值只有明確選了才變：方向鍵、Esc 不改篩選、排序與快速新增的上層。
 */

/** 選項或按鈕前面的小圖：Select 把它包在第一個 aria-hidden 的 span 裡。沒有小圖時是 null。 */
function leadingOf(element: HTMLElement): HTMLElement | null {
  const first = element.firstElementChild;
  return first instanceof HTMLElement && first.tagName === 'SPAN' && first.getAttribute('aria-hidden') === 'true' ? first : null;
}

/** 小圖裡的圖示名稱（Icon 的 data-icon）：狀態的形狀。 */
function shapeOf(element: HTMLElement): string | null | undefined {
  return leadingOf(element)?.querySelector('[data-icon]')?.getAttribute('data-icon');
}

/** 佔位的小圖（work-items/selectOptionMarks.tsx 的 data-mark）。 */
function markOf(element: HTMLElement): string | null | undefined {
  return leadingOf(element)?.querySelector('[data-mark]')?.getAttribute('data-mark');
}

function optionNamed(options: readonly HTMLElement[], name: string): HTMLElement {
  const found = options.find((option) => option.getAttribute('data-value') !== null && within(option).queryByText(name, { exact: true }) !== null);
  if (found === undefined) throw new Error(`沒有「${name}」這個選項`);
  return found;
}

function urlParams(router: ReturnType<typeof renderApp>['router']): URLSearchParams {
  return new URLSearchParams(router.state.location.search);
}

async function openAgyTable(path = '/projects/AGY') {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  const requests = serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const view = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  const filters = screen.getByRole('group', { name: '篩選項目' });
  return { ...view, requests, filters };
}

async function openSmkRow(item: Item) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', [item]);
  const view = renderApp('/projects/SMK');
  const table = await screen.findByRole('table', { name: '項目' });
  const row = within(table).getByRole('row', { name: new RegExp(item.key) });
  return { ...view, row };
}

describe('下拉選項前面的小圖：狀態', () => {
  it('表格工具列的狀態篩選：每個選項都有小圖，狀態是類別的形狀；選了之後按鈕上也畫同一個形狀', async () => {
    // Arrange
    const { user, filters } = await openAgyTable();
    const select = within(filters).getByRole('combobox', { name: '狀態' });
    expect(markOf(select)).toBe('anyState');

    // Act
    const options = await openOptions(user, select);

    // Assert
    expect(options.every((option) => leadingOf(option) !== null)).toBe(true);
    expect(markOf(optionNamed(options, '所有狀態'))).toBe('anyState');
    expect(shapeOf(optionNamed(options, '需求確認'))).toBe('stateNotStarted');
    expect(shapeOf(optionNamed(options, '開發'))).toBe('stateInProgress');
    expect(shapeOf(optionNamed(options, '結案'))).toBe('stateDone');

    // Act
    await chooseOption(user, select, '開發');

    // Assert
    await waitFor(() => {
      expect(select).toHaveTextContent('開發');
    });
    expect(shapeOf(select)).toBe('stateInProgress');
  });

  it('狀態格：目前的狀態與走得到的狀態都帶類別的形狀，按鈕上是格子原本的狀態標籤（同一個形狀）', async () => {
    // Arrange
    const { user, row } = await openSmkRow(smkItem({ number: 1 }));

    // Act
    await user.click(within(row).getByRole('button', { name: '狀態：待辦' }));
    const select = await within(row).findByRole('combobox', { name: '狀態' });
    const options = await openOptions(user, select);

    // Assert
    expect(options.map((option) => shapeOf(option))).toEqual(['stateNotStarted', 'stateInProgress', 'stateDone']);
    // 格子裡的按鈕是格子平常的樣子（docs/DESIGN.md 8.3「就地編輯」）：形狀在狀態標籤裡，不是選項的小圖。
    expect(select.querySelector('[data-icon]')?.getAttribute('data-icon')).toBe('stateNotStarted');
    expect(select).toHaveTextContent(/^待辦$/);
  });
});

describe('下拉選項前面的小圖：人員', () => {
  it('負責人篩選：所有人、我、沒有負責人與成員都有小圖，「我」與成員是頭像；選了成員按鈕上也畫頭像', async () => {
    // Arrange
    const { user, filters } = await openAgyTable();
    const select = within(filters).getByRole('combobox', { name: '負責人' });

    // Act
    const options = await openOptions(user, select);

    // Assert
    expect(options.every((option) => leadingOf(option) !== null)).toBe(true);
    expect(shapeOf(optionNamed(options, '所有人'))).toBe('people');
    expect(leadingOf(optionNamed(options, '我'))).toHaveTextContent('範');
    expect(markOf(optionNamed(options, '沒有負責人'))).toBe('nobody');
    expect(leadingOf(optionNamed(options, '範例成員'))).toHaveTextContent('範');

    // Act
    await chooseOption(user, select, '範例成員');

    // Assert
    await waitFor(() => {
      expect(select).toHaveTextContent('範例成員');
    });
    expect(leadingOf(select)).toHaveTextContent('範');
  });

  it('負責人格：「沒有負責人」是佔位的小圖、成員是頭像；按鈕上是格子原本的樣子（名字）', async () => {
    // Arrange
    const { user, row } = await openSmkRow(smkItem({ number: 1 }));

    // Act
    await user.click(within(row).getByRole('button', { name: '負責人：範例使用者' }));
    const select = await within(row).findByRole('combobox', { name: '負責人' });
    const options = await openOptions(user, select);

    // Assert
    expect(markOf(optionNamed(options, '沒有負責人'))).toBe('nobody');
    expect(leadingOf(optionNamed(options, '範例使用者'))).toHaveTextContent('範');
    expect(leadingOf(optionNamed(options, '範例成員'))).toHaveTextContent('範');
    // 格子裡的按鈕是格子平常的樣子（docs/DESIGN.md 8.3「就地編輯」）：表格的負責人格只有名字。
    expect(select).toHaveTextContent(/^範例使用者$/);
  });

  it('負責人格：成員有職稱時寫在名稱下面（人員清單來的），沒有職稱的沒有', async () => {
    // Arrange
    server.use(http.get('/v1/people', () => ok([{ ...memberPerson, jobTitle: '前端工程師' }, ownerPerson])));
    const { user, row } = await openSmkRow(smkItem({ number: 1 }));

    // Act
    await user.click(within(row).getByRole('button', { name: '負責人：範例使用者' }));
    const select = await within(row).findByRole('combobox', { name: '負責人' });
    await openOptions(user, select);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('option', { name: '範例成員' })).toHaveAccessibleDescription('前端工程師');
    });
    expect(screen.getByRole('option', { name: '範例使用者' })).not.toHaveAttribute('aria-describedby');
  });

  it('人員欄位的值是已經不是成員的人：選項接在最後，小圖是沒有字的頭像（名字查不到）', async () => {
    // Arrange
    const { user, row } = await openSmkRow(smkItem({ number: 1, fields: { reviewer: 'former-person' } }));

    // Act
    await user.click(within(row).getByRole('button', { name: /^審查者：/ }));
    const select = await within(row).findByRole('combobox', { name: '審查者' });
    const options = await openOptions(user, select);

    // Assert
    expect(options.every((option) => leadingOf(option) !== null)).toBe(true);
    expect(markOf(optionNamed(options, '沒有人'))).toBe('nobody');
    const former = options.at(-1);
    expect(former?.getAttribute('data-value')).toBe('former-person');
    expect(former === undefined ? null : markOf(former)).toBe('unknownPerson');
    expect(former === undefined ? null : leadingOf(former)?.textContent).toBe('');
  });

  it('不是狀態、也不是人員的清單完全沒有小圖：優先度', async () => {
    // Arrange
    const { user, row } = await openSmkRow(smkItem({ number: 1 }));

    // Act
    await user.click(within(row).getByRole('button', { name: /^優先度：/ }));
    const options = await openOptions(user, await within(row).findByRole('combobox', { name: '優先度' }));

    // Assert
    expect(options.length).toBeGreaterThan(0);
    expect(options.every((option) => leadingOf(option) === null)).toBe(true);
  });
});

describe('值只有明確選了才變', () => {
  it('狀態篩選：方向鍵只移動反白、Esc 關上，篩選都不變；Enter 選了才套用', async () => {
    // Arrange
    const { user, filters, router, requests } = await openAgyTable();
    const select = within(filters).getByRole('combobox', { name: '狀態' });
    const sentBefore = requests.length;

    // Act
    select.focus();
    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}');
    await screen.findByRole('listbox', { name: '狀態' });
    await user.keyboard('{Escape}');

    // Assert
    expect(select).toHaveAttribute('aria-expanded', 'false');
    expect(select).toHaveTextContent('所有狀態');
    expect(urlParams(router).get('state')).toBeNull();
    expect(requests).toHaveLength(sentBefore);

    // Act
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    // Assert
    await waitFor(() => {
      expect(urlParams(router).get('state')).toBe('confirming');
    });
    expect(select).toHaveTextContent('需求確認');
  });

  it('快速新增的上層項目：Esc 關上不換值；選了才換，選項晚一點才讀到也一樣', async () => {
    // Arrange
    const { user } = await openAgyTable();
    const form = screen.getByRole('form', { name: '新增項目' });
    await chooseOption(user, within(form).getByRole('combobox', { name: '類型' }), '任務');
    const parent = await within(form).findByRole('combobox', { name: '上層項目' });
    expect(parent).toHaveTextContent('沒有上層項目');

    // Act
    await user.click(parent);
    const listbox = await screen.findByRole('listbox', { name: '上層項目' });
    await within(listbox).findByRole('option', { name: 'AGY-1 官網需求' });
    await user.keyboard('{ArrowDown}{Escape}');

    // Assert
    expect(parent).toHaveAttribute('aria-expanded', 'false');
    expect(parent).toHaveTextContent('沒有上層項目');

    // Act
    const options = await openOptions(user, parent);
    await chooseOption(user, parent, 'AGY-1 官網需求');

    // Assert
    expect(options.every((option) => leadingOf(option) === null)).toBe(true);
    await waitFor(() => {
      expect(parent).toHaveTextContent('AGY-1 官網需求');
    });
  });
});
