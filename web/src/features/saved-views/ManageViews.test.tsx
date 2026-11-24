import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Cycle } from '../../api/planning';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { cycleOf, serveCycles } from '../../test/planning';
import { renderApp } from '../../test/renderApp';
import { savedViewOf, serveSavedViews } from '../../test/savedViews';

type App = ReturnType<typeof renderApp>;

async function openAgy(
  path: string,
  views = [savedViewOf('本週要交', { due: 'week', priority: 'high' })],
  options?: Parameters<typeof serveSavedViews>[2],
  cycles: readonly Cycle[] = [],
) {
  signedInAs(exampleOwner, { details: [agyDetail()] });
  serveCycles('AGY', cycles);
  serveItemsByQuery('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const backend = serveSavedViews('AGY', views, options);
  const app = renderApp(path);
  await (path.includes('view=board') ? screen.findByRole('region', { name: '看板' }) : screen.findByRole('table', { name: '項目' }));
  return { ...app, backend };
}

async function chooseItem(app: App, name: string) {
  await app.user.click(await screen.findByRole('button', { name: /^檢視/ }));
  const menu = await screen.findByRole('menu', { name: '檢視' });
  await app.user.click(within(menu).queryByRole('menuitemradio', { name }) ?? within(menu).getByRole('menuitem', { name }));
}

describe('另存成檢視', () => {
  it('名稱預設是目前條件的簡短說法；儲存之後存下目前的版面與條件，按鈕寫新檢視的名稱，焦點回到按鈕', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=today&priority=urgent&sort=title:desc', []);

    // Act
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });
    const name = within(dialog).getByRole('textbox', { name: '名稱' });
    expect(name).toHaveValue('今天到期・緊急');
    await app.user.clear(name);
    await app.user.type(name, '  今天要處理的  {Enter}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(app.backend.created[0]?.body).toEqual({
      name: '今天要處理的',
      layout: 'table',
      query: { type: null, state: null, assignee: null, q: null, sort: 'title:desc', due: 'today', priority: 'urgent', cycle: null, milestone: null, fields: null, group: null, category: null, project: null },
    });
    const trigger = await screen.findByRole('button', { name: '檢視，目前是「今天要處理的」' });
    expect(trigger).toHaveFocus();
  });

  it('看板上另存，存的是看板的版面與看板有的條件', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?view=board&assignee=me&due=week', []);
    // Act
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveValue('看板・我・本週到期');
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(app.backend.created).toHaveLength(1);
    });
    expect(app.backend.created[0]?.body).toMatchObject({ layout: 'board', query: { assignee: 'me', due: 'week', sort: null } });
  });

  it('名稱空白時不送，說明並把焦點放回名稱', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY', []);
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });
    const name = within(dialog).getByRole('textbox', { name: '名稱' });
    expect(name).toHaveValue('表格');

    // Act
    await app.user.clear(name);
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(within(dialog).getByText('請輸入檢視的名稱。')).toBeInTheDocument();
    expect(name).toHaveFocus();
    expect(app.backend.created).toHaveLength(0);
  });

  it('名稱太長時不送', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY', []);
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });

    // Act
    await app.user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '長'.repeat(51));
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(within(dialog).getByText('名稱最多 50 個字。')).toBeInTheDocument();
    expect(app.backend.created).toHaveLength(0);
  });

  it('名稱重複時留在欄位上，改了名稱就能再送', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=today', [savedViewOf('今天')], {
      rejectWith: { status: 409, code: 'saved_view.name_taken', message: '已經有叫這個名字的檢視。' },
    });
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    const name = within(dialog).getByRole('textbox', { name: '名稱' });
    expect(await within(dialog).findByText('已經有叫這個名字的檢視。')).toBeInTheDocument();
    expect(name).toHaveAccessibleDescription('已經有叫這個名字的檢視。');
    await app.user.type(name, '2');
    expect(within(dialog).queryByText('已經有叫這個名字的檢視。')).not.toBeInTheDocument();
  });

  it('檢視滿了時，說明放在表單上方', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=today', [], {
      rejectWith: { status: 409, code: 'saved_view.limit_reached', message: '最多只能存 50 個檢視，請先刪掉不用的。' },
    });
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('最多只能存 50 個檢視');
  });

  it('取消與 Esc 關上對話框，什麼都不存，焦點回到按鈕', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=today', []);
    await chooseItem(app, '另存成檢視…');
    await screen.findByRole('dialog', { name: '另存成檢視' });

    // Act
    await app.user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '檢視' })).toHaveFocus();
    expect(app.backend.created).toHaveLength(0);
  });
});

describe('重新命名與刪除', () => {
  it('重新命名只換名稱，版面與條件照舊', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=week&priority=high');
    await chooseItem(app, '重新命名…');
    const dialog = await screen.findByRole('dialog', { name: '重新命名檢視' });
    const name = within(dialog).getByRole('textbox', { name: '名稱' });
    expect(name).toHaveValue('本週要交');

    // Act
    await app.user.clear(name);
    await app.user.type(name, '本週交付{Enter}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(app.backend.replaced[0]?.body).toEqual({
      name: '本週交付',
      layout: 'table',
      query: { type: null, state: null, assignee: null, q: null, sort: null, due: 'week', priority: 'high', cycle: null, milestone: null, fields: null, group: null, category: null, project: null },
    });
    expect(await screen.findByRole('button', { name: '檢視，目前是「本週交付」' })).toBeInTheDocument();
  });

  it('刪除先確認；確認之後刪掉，條件照舊、按鈕回到「檢視」', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=week&priority=high');
    await chooseItem(app, '刪除「本週要交」');
    const dialog = await screen.findByRole('dialog', { name: '刪除檢視「本週要交」？' });
    expect(app.backend.deleted).toHaveLength(0);

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(app.backend.deleted).toHaveLength(1);
    expect(await screen.findByRole('button', { name: '檢視' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: '到期' })).toHaveTextContent('本週到期');
  });

  it('刪除的確認按「取消」就不刪', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=week&priority=high');
    await chooseItem(app, '刪除「本週要交」');
    const dialog = await screen.findByRole('dialog', { name: '刪除檢視「本週要交」？' });

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    expect(app.backend.deleted).toHaveLength(0);
    expect(screen.getByRole('button', { name: '檢視，目前是「本週要交」' })).toBeInTheDocument();
  });
});

describe('送出中與別處已經改過', () => {
  it('刪除送出中：確認鈕標 aria-disabled、焦點不掉，結果回來才關上', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const app = await openAgy('/projects/AGY?due=week&priority=high', undefined, { gate });
    await chooseItem(app, '刪除「本週要交」');
    const dialog = await screen.findByRole('dialog', { name: '刪除檢視「本週要交」？' });

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    const pending = await within(dialog).findByRole('button', { name: '刪除中…' });
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    release();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('重新命名送出中：送出鈕標 aria-disabled、焦點不掉', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const app = await openAgy('/projects/AGY?due=week&priority=high', undefined, { gate });
    await chooseItem(app, '重新命名…');
    const dialog = await screen.findByRole('dialog', { name: '重新命名檢視' });
    await app.user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '2');

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    const pending = await within(dialog).findByRole('button', { name: '儲存中…' });
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    release();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('檢視在別處已經被刪掉（404）：說明原因，清單重抓之後按鈕回到「檢視」', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=week&priority=high');
    await chooseItem(app, '刪除「本週要交」');
    const dialog = await screen.findByRole('dialog', { name: '刪除檢視「本週要交」？' });
    app.backend.drop('view-本週要交');

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await within(dialog).findByText('找不到這個檢視。')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: '檢視' })).toBeInTheDocument();
  });

  it('重新命名時檢視已經被刪掉（404）：清單也跟著更新', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=week&priority=high');
    await chooseItem(app, '重新命名…');
    const dialog = await screen.findByRole('dialog', { name: '重新命名檢視' });
    app.backend.drop('view-本週要交');

    // Act
    await app.user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '2{Enter}');

    // Assert
    expect(await within(dialog).findByText('找不到這個檢視。')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: '檢視' })).toBeInTheDocument();
  });

  it('另存對話框一打開，名稱就整個選取（直接打字就是換掉預設名稱）', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=today', []);

    // Act
    await chooseItem(app, '另存成檢視…');

    // Assert
    const name = within(await screen.findByRole('dialog', { name: '另存成檢視' })).getByRole<HTMLInputElement>('textbox', { name: '名稱' });
    expect(name.value).toBe('今天到期');
    expect([name.selectionStart, name.selectionEnd]).toEqual([0, name.value.length]);
  });

  it('名稱重複（409）時焦點放回名稱欄位', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=today', [savedViewOf('今天')], {
      rejectWith: { status: 409, code: 'saved_view.name_taken', message: '已經有叫這個名字的檢視。' },
    });
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });

    // Act
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await within(dialog).findByText('已經有叫這個名字的檢視。');
    await waitFor(() => {
      expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveFocus();
    });
  });

  it('套用的檢視網址和目前一樣時不導航，瀏覽紀錄不多一筆', async () => {
    // Arrange
    const app = await openAgy('/projects/AGY?due=week&priority=high');
    const before = app.router.state.location.key;

    // Act
    await chooseItem(app, '本週要交');

    // Assert
    expect(app.router.state.location.key).toBe(before);
  });
});

describe('另存成檢視：週期與里程碑', () => {
  it('篩了目前的週期：名稱預設「目前的週期」，存的是 current 不是編號', async () => {
    // Arrange
    // 「目前的週期」要有一個今天所在的週期，不然表格不送請求、顯示空白狀態。
    const app = await openAgy('/projects/AGY?cycle=current&milestone=none', [], undefined, [cycleOf('衝刺', '2000-01-01', '2099-12-31')]);

    // Act
    await chooseItem(app, '另存成檢視…');
    const dialog = await screen.findByRole('dialog', { name: '另存成檢視' });
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveValue('目前的週期・沒有里程碑');
    await app.user.click(within(dialog).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(app.backend.created).toHaveLength(1);
    });
    expect(app.backend.created[0]?.body).toMatchObject({ layout: 'table', query: { cycle: 'current', milestone: 'none' } });
  });
});
