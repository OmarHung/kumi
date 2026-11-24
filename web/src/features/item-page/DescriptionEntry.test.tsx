import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ItemDetail, ProjectDetail } from '../../api/types';
import { serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { ALL_PROJECT_PERMISSIONS, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { flushPending } from '../../test/flush';
import { descriptionRevisionOf, detailOf, serveItem, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { findEditor, typeInEditor } from '../../test/richText';

/*
 * 說明的入口（docs/DESIGN.md 8.3「說明」）：有 item.update 時，沒有說明是整塊虛線框「新增說明」的按鈕；
 * 有說明時只有標題列的鉛筆（「編輯說明」）進入編輯，點文字沒有反應。
 */

const HINT = '寫下背景、目標或驗收條件，接手的人比較好懂。';
const withoutDescription = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
const withDescription = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '舊的第一行\n舊的第二行');
const readOnly = smkDetail({ myPermissions: ALL_PROJECT_PERMISSIONS.filter((permission) => permission !== 'item.update') });

async function openDescription(item: ItemDetail, detail: ProjectDetail = smkDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItem(item);
  serveActivity(item.key, []);
  const patches = servePatch(item);
  const view = renderApp(`/projects/SMK/items/${item.key}`);
  const section = await screen.findByRole('region', { name: '說明' });
  return { ...view, section, patches };
}

const LONG_DESCRIPTION = Array.from({ length: 20 }, (_, index) => `第 ${String(index + 1)} 行`).join('\n');

/** jsdom 沒有版面：讓說明那一塊量起來比收起來的高度高（超過 12 行），才有「顯示全部」。 */
function measureDescriptionAsLong() {
  const isDescription = (element: Element) => element.textContent === LONG_DESCRIPTION;
  vi.spyOn(Element.prototype, 'scrollHeight', 'get').mockImplementation(function (this: Element) {
    return isDescription(this) ? 600 : 0;
  });
  vi.spyOn(Element.prototype, 'clientHeight', 'get').mockImplementation(function (this: Element) {
    return isDescription(this) ? 300 : 0;
  });
}

describe('說明：沒有說明時', () => {
  it('整塊虛線框就是「新增說明」的按鈕：看得到的字以它開頭，提示連到按鈕；標題列沒有另一顆', async () => {
    // Arrange & Act
    const { section } = await openDescription(withoutDescription);

    // Assert
    const add = within(section).getByRole('button', { name: '新增說明' });
    expect(within(section).getAllByRole('button')).toEqual([add]);
    expect(add).toHaveTextContent(new RegExp(`^新增說明${HINT}$`));
    expect(add).toHaveAccessibleDescription(HINT);
    expect(within(section).queryByText(/還沒有說明/)).not.toBeInTheDocument();
  });

  it('點了進入編輯、焦點在文字框；取消之後焦點回到「新增說明」，再按 Enter 也進得去', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withoutDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '新增說明' }));

    // Assert
    const editor = await findEditor('說明', section);
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    expect(within(section).getByRole('button', { name: '儲存' })).toBeInTheDocument();

    // Act
    await user.click(within(section).getByRole('button', { name: '取消' }));

    // Assert
    expect(await within(section).findByRole('button', { name: '新增說明' })).toHaveFocus();

    // Act
    await user.keyboard('{Enter}');

    // Assert
    const reopened = await findEditor('說明', section);
    await waitFor(() => {
      expect(reopened).toHaveFocus();
    });
    await flushPending();
    expect(patches).toHaveLength(0);
  });

  it('沒有修改權限：原本的虛線提示，不是按鈕', async () => {
    // Arrange & Act
    const { section } = await openDescription(withoutDescription, readOnly);

    // Assert
    expect(within(section).getByText('還沒有說明。')).toBeInTheDocument();
    expect(within(section).queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('說明：有說明時', () => {
  it('只有鉛筆能進入編輯：焦點在編輯器、下面是「儲存」「取消」；儲存之後焦點回到鉛筆', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));

    // Assert
    const editor = await findEditor('說明', section);
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    expect(editor).toHaveTextContent('舊的第一行');
    expect(within(section).getByRole('button', { name: '取消' })).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '編輯說明' })).not.toBeInTheDocument();

    // Act
    await typeInEditor(user, editor, '！');
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '編輯說明' })).toHaveFocus();
    });
    expect(patches.map((request) => request.body)).toEqual([{ description: '舊的第一行\n舊的第二行！', expectedDescriptionRevision: descriptionRevisionOf('舊的第一行\n舊的第二行') }]);
  });

  it('鉛筆是圖示按鈕：名稱「編輯說明」，滑過時的提示一樣', async () => {
    // Arrange & Act
    const { section } = await openDescription(withDescription);

    // Assert
    const pencil = within(section).getByRole('button', { name: '編輯說明' });
    expect(pencil).toHaveAttribute('title', '編輯說明');
    expect(pencil).not.toHaveTextContent(/\S/);
  });

  it('點說明的文字沒有反應，連結照常可以點', async () => {
    // Arrange
    const { user, section } = await openDescription(detailOf(smkItem({ number: 7 }), '看[這裡](https://example.com/doc)的說明'));

    // Act
    await user.click(await within(section).findByText(/的說明/));
    await flushPending();

    // Assert
    expect(within(section).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    expect(await within(section).findByRole('link', { name: '這裡' })).toHaveAttribute('href', 'https://example.com/doc');
  });

  it('鉛筆進入、Esc 取消：焦點回到鉛筆', async () => {
    // Arrange
    const { user, section } = await openDescription(withDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    const editor = await findEditor('說明', section);
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '編輯說明' })).toHaveFocus();
    });
  });

  it('拖曳選取文字不會進入編輯，選取留著', async () => {
    // Arrange
    const { user, section } = await openDescription(withDescription);
    const text = within(section).getByText('舊的第一行 舊的第二行');

    // Act
    await user.pointer([{ keys: '[MouseLeft>]', target: text, offset: 0 }, { offset: 3 }, { keys: '[/MouseLeft]' }]);
    await flushPending();

    // Assert
    expect(document.getSelection()?.toString()).toBe('舊的第');
    expect(within(section).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
  });

  it('點「顯示全部」只是展開，不進入編輯', async () => {
    // Arrange
    measureDescriptionAsLong();
    const { user, section } = await openDescription(detailOf(smkItem({ number: 7 }), LONG_DESCRIPTION));

    // Act
    await user.click(await within(section).findByRole('button', { name: '顯示全部' }));

    // Assert
    expect(within(section).getByRole('button', { name: '收起' })).toBeInTheDocument();
    expect(within(section).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
  });

  it('沒有修改權限：說明點了沒有反應，也沒有「編輯說明」', async () => {
    // Arrange
    const { user, section } = await openDescription(withDescription, readOnly);

    // Act
    await user.click(within(section).getByText('舊的第一行 舊的第二行'));
    await flushPending();

    // Assert
    expect(within(section).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(section).queryByRole('button')).not.toBeInTheDocument();
  });
});
