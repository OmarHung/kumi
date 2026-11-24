import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { attachment, openAttachments } from '../../test/attachments';

const FIRST = attachment(1, { fileName: '規格.pdf' });
const SECOND = attachment(2, { fileName: '截圖.png', contentType: 'image/png' });
const THIRD = attachment(3, { fileName: '報價.xlsx' });

async function askToDelete(user: Awaited<ReturnType<typeof openAttachments>>['user'], section: HTMLElement, name: string) {
  await user.click(await within(section).findByRole('button', { name: `刪除 ${name}` }));
  return screen.findByRole('dialog', { name: `刪除「${name}」？` });
}

describe('附件區：刪除', () => {
  it('先確認：標題寫出檔名，說明不能復原，確認鍵是危險的「刪除」；取消什麼都不做', async () => {
    // Arrange
    const { user, section, served } = await openAttachments({ attachments: [FIRST] });

    // Act
    const dialog = await askToDelete(user, section, '規格.pdf');
    await user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(served.deleted).toEqual([]);
    expect(within(section).getByRole('button', { name: '刪除 規格.pdf' })).toHaveFocus();
  });

  it('對話框說明刪除之後無法復原', async () => {
    // Arrange
    const { user, section } = await openAttachments({ attachments: [FIRST] });

    // Act
    const dialog = await askToDelete(user, section, '規格.pdf');

    // Assert
    expect(dialog).toHaveTextContent('無法復原');
    expect(within(dialog).getByRole('button', { name: '刪除' })).toBeInTheDocument();
  });

  it('刪除成功：那一列不見、對話框關上、清單與活動重抓，焦點到下一列的「刪除」', async () => {
    // Arrange
    const { user, section, served, timeline } = await openAttachments({ attachments: [FIRST, SECOND, THIRD] });
    const dialog = await askToDelete(user, section, '截圖.png');
    const listBefore = served.listRequests();
    const activityBefore = timeline.requests.length;

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(within(section).queryByRole('link', { name: '截圖.png' })).not.toBeInTheDocument();
    });
    expect(served.deleted).toEqual([SECOND.id]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(within(section).getAllByRole('listitem')).toHaveLength(2);
    expect(within(section).getByRole('button', { name: '刪除 報價.xlsx' })).toHaveFocus();
    await waitFor(() => {
      expect(served.listRequests()).toBeGreaterThan(listBefore);
      expect(timeline.requests.length).toBeGreaterThan(activityBefore);
    });
  });

  it('刪的是最後一列：焦點到上一列的「刪除」', async () => {
    // Arrange
    const { user, section } = await openAttachments({ attachments: [FIRST, SECOND] });
    const dialog = await askToDelete(user, section, '截圖.png');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '刪除 規格.pdf' })).toHaveFocus();
    });
  });

  it('刪掉唯一的一個：焦點到「上傳檔案」，區塊說還沒有附件', async () => {
    // Arrange
    const { user, section } = await openAttachments({ attachments: [FIRST] });
    const dialog = await askToDelete(user, section, '規格.pdf');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '上傳檔案' })).toHaveFocus();
    });
    expect(within(section).getByText(/還沒有附件/)).toBeInTheDocument();
  });

  it('失敗時對話框留著、說明原因，那一列還在；可以再按一次', async () => {
    // Arrange
    const { user, section, served } = await openAttachments({ attachments: [FIRST, SECOND] });
    served.rejectDelete({ status: 403, code: 'auth.forbidden', message: '你沒有權限刪除這個附件。' });
    const dialog = await askToDelete(user, section, '規格.pdf');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await within(dialog).findByText('你沒有權限刪除這個附件。')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '刪除「規格.pdf」？' })).toBeInTheDocument();
    expect(within(section).getByRole('link', { name: '規格.pdf' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: '刪除' })).toBeInTheDocument();
  });

  it('別處先刪了（404）也當成刪掉了：那一列不見', async () => {
    // Arrange
    const { user, section, served } = await openAttachments({ attachments: [FIRST, SECOND] });
    served.rejectDelete({ status: 404, code: 'attachment.not_found', message: '找不到這個附件。' });
    const dialog = await askToDelete(user, section, '規格.pdf');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(within(section).queryByRole('link', { name: '規格.pdf' })).not.toBeInTheDocument();
  });
});

describe('附件區：剛傳完的列被別人刪掉', () => {
  it('伺服器上已經沒有它：按刪除（404）之後，傳完的那一列也拿掉', async () => {
    // Arrange
    const { installFakeUploads, aFile } = await import('../../test/attachments');
    const uploads = installFakeUploads();
    const { user, section } = await openAttachments();
    await user.upload(within(section).getByLabelText('選擇要上傳的檔案'), aFile('規格.pdf'));
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    // 伺服器收下了，但清單重抓時它已經被別人刪了（served 沒有它）。
    uploads[0]?.respond(201, attachment(9, { fileName: '規格.pdf' }));
    const dialog = await askToDelete(user, section, '規格.pdf');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    await waitFor(() => {
      expect(within(section).queryByRole('link', { name: '規格.pdf' })).not.toBeInTheDocument();
    });
    vi.unstubAllGlobals();
  });
});
