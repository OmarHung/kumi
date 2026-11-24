import { createEvent, fireEvent, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CSRF_HEADER } from '../../api/http';
import { aFile, attachment, FakeUpload, installFakeUploads, openAttachments } from '../../test/attachments';
import { smkDetail } from '../../test/fixtures';

let uploads: readonly FakeUpload[];

beforeEach(() => {
  uploads = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const MB = 1024 * 1024;

async function pick(user: Awaited<ReturnType<typeof openAttachments>>['user'], section: HTMLElement, files: File[]) {
  await user.upload(within(section).getByLabelText('選擇要上傳的檔案'), files);
}

function progressOf(section: HTMLElement, name: string): HTMLProgressElement {
  return within(section).getByRole('progressbar', { name: `${name} 的上傳進度` });
}

function dragFiles(files: File[]) {
  return { dataTransfer: { types: ['Files'], files } };
}

describe('附件區：選檔上傳', () => {
  it('選了多個檔案：每個檔案一列，送出 multipart 的 file 欄位與 CSRF 標頭', async () => {
    // Arrange
    const { user, section } = await openAttachments();

    // Act
    await pick(user, section, [aFile('規格.pdf'), aFile('截圖.png', 2048, 'image/png')]);

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    expect(uploads.map((upload) => upload.file?.name)).toEqual(['規格.pdf', '截圖.png']);
    expect(uploads[0]?.url).toBe('/v1/items/SMK-7/attachments');
    expect(uploads[0]?.headers.get(CSRF_HEADER)).toBe('1');
    expect(progressOf(section, '規格.pdf')).toBeInTheDocument();
    expect(progressOf(section, '截圖.png')).toBeInTheDocument();
  });

  it('進度條跟著送出的位元組：role=progressbar、數值與百分比', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, [aFile('規格.pdf', 1000)]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    uploads[0]?.progress(420, 1000);

    // Assert
    await waitFor(() => {
      expect(progressOf(section, '規格.pdf').value).toBe(42);
    });
    expect(progressOf(section, '規格.pdf').max).toBe(100);
    expect(within(section).getByText('42%')).toBeInTheDocument();
  });

  it('瀏覽器不知道總數時進度條是不確定的，寫「上傳中…」', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, [aFile('規格.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    uploads[0]?.progress(100, null);

    // Assert
    expect(await within(section).findByText('上傳中…')).toBeInTheDocument();
    expect(progressOf(section, '規格.pdf')).not.toHaveAttribute('value');
  });

  it('同時最多傳 3 個，其他排隊；有一個傳完才輪到下一個', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    const names = ['a.pdf', 'b.pdf', 'c.pdf', 'd.pdf', 'e.pdf'];

    // Act
    await pick(user, section, names.map((name) => aFile(name)));

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(3);
    });
    expect(within(section).getAllByRole('progressbar')).toHaveLength(3);
    expect(within(section).getAllByText('等待上傳…')).toHaveLength(2);

    uploads[0]?.respond(201, attachment(1, { fileName: 'a.pdf' }));
    await waitFor(() => {
      expect(uploads).toHaveLength(4);
    });
    expect(uploads[3]?.file?.name).toBe('d.pdf');
    expect(within(section).getAllByText('等待上傳…')).toHaveLength(1);
  });

  it('傳完換成正式的列（不再有進度條），清單與活動重抓', async () => {
    // Arrange
    const { user, section, served, timeline } = await openAttachments();
    await pick(user, section, [aFile('規格.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    const created = attachment(1, { fileName: '規格.pdf' });
    const listBefore = served.listRequests();
    const activityBefore = timeline.requests.length;
    served.add(created);

    // Act
    uploads[0]?.respond(201, created);

    // Assert
    const link = await within(section).findByRole('link', { name: '規格.pdf' });
    expect(link).toHaveAttribute('href', `/v1/attachments/${created.id}/content`);
    expect(within(section).queryByRole('progressbar')).not.toBeInTheDocument();
    expect(within(section).getAllByRole('listitem')).toHaveLength(1);
    await waitFor(() => {
      expect(served.listRequests()).toBeGreaterThan(listBefore);
      expect(timeline.requests.length).toBeGreaterThan(activityBefore);
    });
    expect(within(section).getAllByRole('listitem')).toHaveLength(1);
  });

  it('傳完時焦點在那一列的「取消」上：換成正式的列之後焦點到那一列的「刪除」', async () => {
    // Arrange
    const { user, section, served } = await openAttachments();
    await pick(user, section, [aFile('規格.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    within(section).getByRole('button', { name: '取消 規格.pdf' }).focus();
    const created = attachment(1, { fileName: '規格.pdf' });
    served.add(created);

    // Act
    uploads[0]?.respond(201, created);

    // Assert
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '刪除 規格.pdf' })).toHaveFocus();
    });
  });
});

describe('附件區：StrictMode', () => {
  it('effect 跑兩次時每個檔案也只送一次，同時還是最多 3 個', async () => {
    // Arrange
    const { user, section } = await openAttachments({ strict: true });

    // Act
    await pick(user, section, ['a', 'b', 'c', 'd'].map((name) => aFile(`${name}.pdf`)));

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(3);
    });
    expect(uploads.map((upload) => upload.file?.name)).toEqual(['a.pdf', 'b.pdf', 'c.pdf']);
  });
});

describe('附件區：拖曳上傳', () => {
  it('檔案拖進來時整區高亮（data-dragging），離開就取消', async () => {
    // Arrange
    const { section } = await openAttachments();
    const files = [aFile('規格.pdf')];

    // Act
    fireEvent.dragEnter(section, dragFiles(files));

    // Assert
    expect(section).toHaveAttribute('data-dragging', 'true');
    fireEvent.dragLeave(section);
    expect(section).not.toHaveAttribute('data-dragging');
  });

  it('游標移到區域裡面的子元素不算離開（不閃）', async () => {
    // Arrange
    const { section } = await openAttachments();
    fireEvent.dragEnter(section, dragFiles([aFile('規格.pdf')]));

    // Act
    const leave = createEvent.dragLeave(section);
    Object.defineProperty(leave, 'relatedTarget', { value: within(section).getByRole('button', { name: '上傳檔案' }) });
    fireEvent(section, leave);

    // Assert
    expect(section).toHaveAttribute('data-dragging', 'true');
  });

  it('拖進來的不是檔案（一段文字）不高亮', async () => {
    // Arrange
    const { section } = await openAttachments();

    // Act
    fireEvent.dragEnter(section, { dataTransfer: { types: ['text/plain'], files: [] } });

    // Assert
    expect(section).not.toHaveAttribute('data-dragging');
  });

  it('放開之後上傳拖進來的檔案，高亮取消', async () => {
    // Arrange
    const { section } = await openAttachments();
    fireEvent.dragEnter(section, dragFiles([aFile('規格.pdf')]));

    // Act
    fireEvent.drop(section, dragFiles([aFile('規格.pdf'), aFile('報價.xlsx')]));

    // Assert
    await waitFor(() => {
      expect(uploads.map((upload) => upload.file?.name)).toEqual(['規格.pdf', '報價.xlsx']);
    });
    expect(section).not.toHaveAttribute('data-dragging');
  });

  it('沒有 item.update 的人拖進來不高亮也不上傳', async () => {
    // Arrange
    const { section } = await openAttachments({ detail: smkDetail({ myPermissions: ['item.read'] }) });

    // Act
    fireEvent.dragEnter(section, dragFiles([aFile('規格.pdf')]));
    fireEvent.drop(section, dragFiles([aFile('規格.pdf')]));

    // Assert
    expect(section).not.toHaveAttribute('data-dragging');
    expect(uploads).toHaveLength(0);
  });
});

describe('附件區：大小與失敗', () => {
  it('超過 25 MB 的檔案不送出，說明是哪一個；其他的照常上傳', async () => {
    // Arrange
    const { user, section } = await openAttachments();

    // Act
    await pick(user, section, [aFile('規格.zip', 26 * MB, 'application/zip'), aFile('小.pdf')]);

    // Assert
    expect(await within(section).findByText('超過 25 MB：規格.zip')).toBeInTheDocument();
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    expect(uploads[0]?.file?.name).toBe('小.pdf');
  });

  it('剛好 25 MB 可以傳；好幾個超過的列成清單，「知道了」收起說明', async () => {
    // Arrange
    const { user, section } = await openAttachments();

    // Act
    await pick(user, section, [aFile('剛好.bin', 25 * MB), aFile('a.zip', 26 * MB), aFile('b.zip', 30 * MB)]);

    // Assert
    const note = await within(section).findByText(/超過 25 MB，沒有上傳/);
    expect(within(note).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['a.zip', 'b.zip']);
    expect(uploads.map((upload) => upload.file?.name)).toEqual(['剛好.bin']);
    await user.click(within(section).getByRole('button', { name: '知道了（沒有上傳的檔案）' }));
    expect(within(section).queryByText(/超過 25 MB/)).not.toBeInTheDocument();
  });

  it('上傳失敗：那一列寫原因，有「重試」與「移除」；重試之後重新送出', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, [aFile('規格.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    uploads[0]?.respondFailure(409, 'attachment.limit_reached', '這個項目的附件已經滿了。');

    // Assert
    expect(await within(section).findByText('上傳失敗：這個項目的附件已經滿了。')).toBeInTheDocument();
    expect(within(section).queryByRole('progressbar')).not.toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '移除 規格.pdf' })).toBeInTheDocument();
    await user.click(within(section).getByRole('button', { name: '重試 規格.pdf' }));
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    expect(uploads[1]?.file?.name).toBe('規格.pdf');
    expect(await within(section).findByRole('progressbar', { name: '規格.pdf 的上傳進度' })).toBeInTheDocument();
  });

  it('網路錯誤也說原因；「移除」拿掉那一列，焦點到「上傳檔案」', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, [aFile('規格.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    uploads[0]?.failNetwork();
    await within(section).findByText(/上傳失敗：連不上伺服器/);

    // Act
    await user.click(within(section).getByRole('button', { name: '移除 規格.pdf' }));

    // Assert
    expect(within(section).queryByRole('button', { name: '移除 規格.pdf' })).not.toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '上傳檔案' })).toHaveFocus();
  });

  it('失敗的檔案不佔同時上傳的名額：排隊的接著傳', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, ['a', 'b', 'c', 'd'].map((name) => aFile(`${name}.pdf`)));
    await waitFor(() => {
      expect(uploads).toHaveLength(3);
    });

    // Act
    uploads[1]?.respondFailure(500, 'server.error', 'x');

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(4);
    });
    expect(uploads[3]?.file?.name).toBe('d.pdf');
  });
});

describe('附件區：取消', () => {
  it('取消傳送中的：中止請求、拿掉那一列，排隊的接著傳', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, ['a', 'b', 'c', 'd'].map((name) => aFile(`${name}.pdf`)));
    await waitFor(() => {
      expect(uploads).toHaveLength(3);
    });

    // Act
    await user.click(within(section).getByRole('button', { name: '取消 a.pdf' }));

    // Assert
    expect(uploads[0]?.isAborted).toBe(true);
    expect(within(section).queryByRole('progressbar', { name: 'a.pdf 的上傳進度' })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(uploads).toHaveLength(4);
    });
    expect(within(section).queryByText(/上傳失敗/)).not.toBeInTheDocument();
  });

  it('取消排隊中的：不會送出請求，輪到它的名額給後面的', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, ['a', 'b', 'c', 'd', 'e'].map((name) => aFile(`${name}.pdf`)));
    await waitFor(() => {
      expect(uploads).toHaveLength(3);
    });
    await user.click(within(section).getByRole('button', { name: '取消 d.pdf' }));

    // Act
    uploads[0]?.respond(201, attachment(1, { fileName: 'a.pdf' }));

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(4);
    });
    expect(uploads[3]?.file?.name).toBe('e.pdf');
    expect(uploads.map((upload) => upload.file?.name)).not.toContain('d.pdf');
  });

  it('取消之後焦點接手：到下一列的「取消」', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, [aFile('a.pdf'), aFile('b.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    await user.click(within(section).getByRole('button', { name: '取消 a.pdf' }));

    // Assert
    expect(within(section).getByRole('button', { name: '取消 b.pdf' })).toHaveFocus();
  });
});

describe('附件區：報讀', () => {
  it('上傳完成時唸「上傳完成：規格.pdf」', async () => {
    // Arrange
    const { user, section, served } = await openAttachments();
    await pick(user, section, [aFile('規格.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    const created = attachment(1, { fileName: '規格.pdf' });
    served.add(created);

    // Act
    uploads[0]?.respond(201, created);

    // Assert
    await waitFor(() => {
      expect(within(section).getAllByRole('status').map((element) => element.textContent)).toContain('上傳完成：規格.pdf');
    });
  });

  it('上傳失敗時唸檔名與原因', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, [aFile('規格.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    uploads[0]?.respondFailure(409, 'attachment.limit_reached', '這個項目的附件已經滿了。');

    // Assert
    await waitFor(() => {
      expect(within(section).getAllByRole('status').map((element) => element.textContent)).toContain('上傳失敗：規格.pdf，這個項目的附件已經滿了。');
    });
  });

  it('好幾個同時結束合成一句', async () => {
    // Arrange
    const { user, section } = await openAttachments();
    await pick(user, section, [aFile('a.pdf'), aFile('b.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });

    // Act
    uploads[0]?.respondFailure(500, 'server.error', 'x');
    uploads[1]?.respondFailure(500, 'server.error', 'x');

    // Assert
    await waitFor(() => {
      const texts = within(section).getAllByRole('status').map((element) => element.textContent);
      expect(texts.some((text) => text.includes('上傳失敗：a.pdf') && text.includes('上傳失敗：b.pdf'))).toBe(true);
    });
  });
});

describe('附件區：清單還沒有資料', () => {
  it('清單讀不到時沒有「上傳檔案」，拖進來也不收', async () => {
    // Arrange
    const { section } = await openAttachments({ isListFailing: true });
    await within(section).findByText('附件載入失敗');

    // Act
    fireEvent.dragEnter(section, dragFiles([aFile('規格.pdf')]));
    fireEvent.drop(section, dragFiles([aFile('規格.pdf')]));

    // Assert
    expect(within(section).queryByRole('button', { name: '上傳檔案' })).not.toBeInTheDocument();
    expect(section).not.toHaveAttribute('data-dragging');
    expect(uploads).toHaveLength(0);
  });
});

describe('附件區：資料夾與讀不到的檔案', () => {
  it('沒有大小也沒有類型的（資料夾）不送出，說明原因，其他的照常上傳', async () => {
    // Arrange
    const { user, section } = await openAttachments();

    // Act
    await pick(user, section, [aFile('資料夾', 0, ''), aFile('小.pdf')]);

    // Assert
    expect(await within(section).findByText('不是檔案或讀不到，沒有上傳：資料夾')).toBeInTheDocument();
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    expect(uploads[0]?.file?.name).toBe('小.pdf');
  });

  it('拖進來的資料夾（webkitGetAsEntry）不送出，即使它有大小', async () => {
    // Arrange
    const { section } = await openAttachments();
    const folder = aFile('文件', 4096, '');
    const items = [{ webkitGetAsEntry: () => ({ isDirectory: true, name: '文件' }) }];

    // Act
    fireEvent.drop(section, { dataTransfer: { types: ['Files'], files: [folder], items } });

    // Assert
    expect(await within(section).findByText('不是檔案或讀不到，沒有上傳：文件')).toBeInTheDocument();
    expect(uploads).toHaveLength(0);
  });

  it('XHR 沒有送出時 abort 什麼都不發，結束之後也是（和真的一樣）', () => {
    // Arrange
    const request = new FakeUpload();
    const onAbort = vi.fn();
    request.addEventListener('abort', onAbort);

    // Act
    request.abort();
    request.send(null);
    request.respond(201, {});
    request.abort();

    // Assert
    expect(onAbort).not.toHaveBeenCalled();
    expect(request.isAborted).toBe(false);
  });
});
