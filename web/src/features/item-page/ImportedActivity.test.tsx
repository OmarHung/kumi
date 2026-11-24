import { screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { Activity } from '../../api/types';
import { attachmentAdded, commented, created, IMPORTED_FROM, memberRef, serveActivity, showAllActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';

// 建立、附件那些行在預設的「留言」裡看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
const IMPORTED_TEXT = `從 ${IMPORTED_FROM} 匯入`;
const OLD = '2026-03-02T08:00:00Z';

async function openTimeline(history: readonly Activity[]) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(task);
  serveActivity(task.key, history);
  renderApp('/projects/SMK/items/SMK-7');
  return screen.findByRole('list', { name: '活動與留言' });
}

function entryOf(history: HTMLElement, text: RegExp | string): HTMLElement {
  const entry = within(history).getByText(text).closest('li');
  if (entry === null) throw new Error(`找不到含「${String(text)}」的那一筆`);
  return entry;
}

describe('匯入的活動', () => {
  it('匯入的留言，時間後面標「從 monday 匯入」，時間的 title 是完整時間', async () => {
    // Arrange / Act
    const history = await openTimeline([commented('舊系統的留言', { importedFrom: IMPORTED_FROM, at: OLD })]);

    // Assert
    const entry = entryOf(history, '舊系統的留言');
    expect(within(entry).getByText(new RegExp(IMPORTED_TEXT))).toBeInTheDocument();
    expect(entry.querySelector('time')).toHaveAttribute('title', expect.stringMatching(/^2026\/03\/02 \d\d:\d\d$/));
    expect(entry.querySelector('time')).toHaveTextContent('2026/03/02');
  });

  it('匯入的回覆也標', async () => {
    // Arrange
    const parent = commented('最上層', { at: OLD });
    const reply = commented('舊系統的回覆', { replyToId: parent.id, actor: memberRef, importedFrom: IMPORTED_FROM, at: OLD });

    // Act
    const history = await openTimeline([created(), parent, reply]);

    // Assert
    expect(within(entryOf(history, '舊系統的回覆')).getByText(new RegExp(IMPORTED_TEXT))).toBeInTheDocument();
    // 最上層那一筆的 li 也包著回覆，只看它自己的那一行（第一個 p）。
    expect(entryOf(history, '最上層').querySelector('p')).not.toHaveTextContent(IMPORTED_TEXT);
  });

  it('匯入的建立也標', async () => {
    // Arrange / Act
    const history = await openTimeline([created({ importedFrom: IMPORTED_FROM, at: OLD })]);

    // Assert
    expect(within(entryOf(history, '建立了這個項目')).getByText(new RegExp(IMPORTED_TEXT))).toBeInTheDocument();
  });

  it('匯入的附件標，並成一行的附件只要其中一筆是匯入的就標', async () => {
    // Arrange
    const first = attachmentAdded('a.png', { importedFrom: IMPORTED_FROM, at: OLD });
    const second = attachmentAdded('b.png', { at: '2026-03-02T08:01:00Z' });

    // Act
    const history = await openTimeline([first, second]);

    // Assert
    expect(within(entryOf(history, /附加了 2 個檔案/)).getByText(new RegExp(IMPORTED_TEXT))).toBeInTheDocument();
  });

  it('標示用來源的名字', async () => {
    // Arrange / Act
    const history = await openTimeline([commented('別的來源', { importedFrom: 'trello' })]);

    // Assert
    expect(within(entryOf(history, '別的來源')).getByText(/從 trello 匯入/)).toBeInTheDocument();
  });

  it('不是匯入的活動看不到這個標示', async () => {
    // Arrange / Act
    const history = await openTimeline([created(), commented('這裡寫的留言'), attachmentAdded('a.png')]);

    // Assert
    expect(within(history).queryByText(/匯入/)).not.toBeInTheDocument();
  });
});
