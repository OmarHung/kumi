import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { attachmentAdded, attachmentRemoved, memberRef, showAllActivity } from '../../test/activity';
import type { FakeUpload} from '../../test/attachments';
import { aFile, attachment, installFakeUploads, openAttachments } from '../../test/attachments';

// 這個檔案看的是變更、附件、狀態那些行，或在等「活動與留言」那份清單：預設的「留言」看不到，所以選「全部」。
beforeEach(() => {
  showAllActivity();
});

let uploads: readonly FakeUpload[];

beforeEach(() => {
  uploads = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openWithTimeline() {
  const opened = await openAttachments();
  const history = await screen.findByRole('list', { name: '活動與留言' });
  return { ...opened, history };
}

describe('活動：附件', () => {
  it('附加了檔案寫「附加了檔案「規格.pdf」」，檔名只是文字、不是連結', async () => {
    // Arrange
    const { history, timeline, queryClient } = await openWithTimeline();
    timeline.add(attachmentAdded('規格.pdf', { actor: memberRef }));

    // Act
    await queryClient.invalidateQueries({ queryKey: ['item', 'SMK-7', 'activity'] });

    // Assert
    const entry = await within(history).findByText(/附加了檔案/);
    expect(entry).toHaveTextContent('附加了檔案「規格.pdf」');
    expect(within(entry).queryByRole('link')).not.toBeInTheDocument();
  });

  it('刪除了附件寫「刪除了附件「規格.pdf」」', async () => {
    // Arrange
    const { history, timeline, queryClient } = await openWithTimeline();
    timeline.add(attachmentRemoved('規格.pdf', { actor: memberRef }));

    // Act
    await queryClient.invalidateQueries({ queryKey: ['item', 'SMK-7', 'activity'] });

    // Assert
    const entry = await within(history).findByText(/刪除了附件/);
    expect(entry).toHaveTextContent('刪除了附件「規格.pdf」');
  });

  it('上傳完成後活動重抓，報讀摘要是同一句話', async () => {
    // Arrange
    const { user, section, served, timeline, history } = await openWithTimeline();
    await user.upload(within(section).getByLabelText('選擇要上傳的檔案'), aFile('規格.pdf'));
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    const created = attachment(1, { fileName: '規格.pdf' });
    served.add(created);
    timeline.add(attachmentAdded('規格.pdf', { actor: memberRef }));

    // Act
    uploads[0]?.respond(201, created);

    // Assert
    expect(await within(history).findByText(/附加了檔案/)).toBeInTheDocument();
    await waitFor(() => {
      const announcements = screen.getAllByRole('status').map((element) => element.textContent);
      expect(announcements).toContain('範例成員 附加了檔案「規格.pdf」');
    });
  });

  it('同一個人連續附加的檔案（5 分鐘內）併成一行，檔名是值；報讀也是同一句', async () => {
    // Arrange
    const { history, timeline, queryClient } = await openWithTimeline();
    ['a.png', 'b.png', 'c.png'].forEach((name, index) => {
      timeline.add(attachmentAdded(name, { actor: memberRef, at: `2026-11-13T09:0${String(index)}:00Z` }));
    });

    // Act
    await queryClient.invalidateQueries({ queryKey: ['item', 'SMK-7', 'activity'] });

    // Assert
    const entry = await within(history).findByText(/附加了 3 個檔案/);
    expect(entry).toHaveTextContent('附加了 3 個檔案：a.png、b.png、c.png');
    expect(within(history).getAllByRole('listitem')).toHaveLength(2);
    await waitFor(() => {
      const announcements = screen.getAllByRole('status').map((element) => element.textContent);
      expect(announcements).toContain('範例成員 附加了 3 個檔案：a.png、b.png、c.png');
    });
  });

  it('刪除的附件照舊一筆一行，不併', async () => {
    // Arrange
    const { history, timeline, queryClient } = await openWithTimeline();
    timeline.add(attachmentRemoved('a.pdf', { actor: memberRef, at: '2026-11-13T09:00:00Z' }));
    timeline.add(attachmentRemoved('b.pdf', { actor: memberRef, at: '2026-11-13T09:01:00Z' }));

    // Act
    await queryClient.invalidateQueries({ queryKey: ['item', 'SMK-7', 'activity'] });

    // Assert
    const entries = await within(history).findAllByText(/刪除了附件/);
    expect(entries.map((entry) => entry.textContent)).toEqual(['刪除了附件「a.pdf」', '刪除了附件「b.pdf」']);
  });
});
