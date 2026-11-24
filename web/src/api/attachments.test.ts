import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { http } from 'msw';
import type { FakeUpload} from '../test/attachments';
import { aFile, attachment, installFakeUploads } from '../test/attachments';
import { failure, ok } from '../test/api';
import { ApiError, ClientErrorCodes, CSRF_HEADER } from './http';
import { attachmentContentUrl, deleteAttachment, listAttachments, uploadAttachment } from './attachments';

let uploads: readonly FakeUpload[];

beforeEach(() => {
  uploads = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function lastUpload(): FakeUpload {
  const request = uploads.at(-1);
  if (request === undefined) throw new Error('沒有送出上傳');
  return request;
}

describe('uploadAttachment', () => {
  it('把檔案放在 multipart 的 file 欄位 POST 到項目的附件網址，帶 CSRF 標頭，不自己設 Content-Type', () => {
    // Arrange
    const file = aFile('規格.pdf');

    // Act
    void uploadAttachment('SMK-7', file);

    // Assert
    const request = lastUpload();
    expect(request.method).toBe('POST');
    expect(request.url).toBe('/v1/items/SMK-7/attachments');
    expect(request.headers.get(CSRF_HEADER)).toBe('1');
    expect(request.headers.get('Accept')).toBe('application/json');
    expect(request.headers.has('Content-Type')).toBe(false);
    expect(request.file?.name).toBe('規格.pdf');
  });

  it('成功（201）時回傳信封裡的附件', async () => {
    // Arrange
    const created = attachment(1);
    const result = uploadAttachment('SMK-7', aFile('a.pdf'));

    // Act
    lastUpload().respond(201, created);

    // Assert
    await expect(result).resolves.toEqual(created);
  });

  it('回報送出的進度；瀏覽器不知道總數時 total 是 null', () => {
    // Arrange
    const onProgress = vi.fn();
    void uploadAttachment('SMK-7', aFile('a.pdf'), { onProgress });

    // Act
    lastUpload().progress(512, 1024);
    lastUpload().progress(600, null);

    // Assert
    expect(onProgress.mock.calls).toEqual([
      [512, 1024],
      [600, null],
    ]);
  });

  it('信封裡的錯誤丟出 ApiError，狀態、代碼與訊息照信封', async () => {
    // Arrange
    const result = uploadAttachment('SMK-7', aFile('a.pdf'));
    const assertion = expect(result).rejects.toMatchObject({ status: 409, code: 'attachment.limit_reached', message: '附件太多了。' });

    // Act
    lastUpload().respondFailure(409, 'attachment.limit_reached', '附件太多了。');

    // Assert
    await assertion;
  });

  it('沒有信封的 413（代理伺服器擋的）說得出是檔案太大', async () => {
    // Arrange
    const result = uploadAttachment('SMK-7', aFile('a.pdf'));
    const assertion = expect(result).rejects.toMatchObject({ status: 413, code: 'attachment.too_large', message: '檔案超過 25 MB，無法上傳。' });

    // Act
    lastUpload().respondRaw(413, '<html>Request Entity Too Large</html>');

    // Assert
    await assertion;
  });

  it('沒有信封的 5xx 換成固定的說法，狀態與代碼照 HTTP', async () => {
    // Arrange
    const result = uploadAttachment('SMK-7', aFile('a.pdf'));
    const assertion = expect(result).rejects.toMatchObject({ status: 502, code: 'http.502', message: '伺服器暫時無法處理，請稍後再試。' });

    // Act
    lastUpload().respondRaw(502, 'Bad Gateway');

    // Assert
    await assertion;
  });

  it('網路錯誤是 client.network，狀態 0', async () => {
    // Arrange
    const result = uploadAttachment('SMK-7', aFile('a.pdf'));
    const assertion = expect(result).rejects.toMatchObject({ status: 0, code: ClientErrorCodes.network });

    // Act
    lastUpload().failNetwork();

    // Assert
    await assertion;
    await expect(result).rejects.toBeInstanceOf(ApiError);
  });

  it('取消時中止請求並丟出 AbortError，不是 ApiError', async () => {
    // Arrange
    const controller = new AbortController();
    const result = uploadAttachment('SMK-7', aFile('a.pdf'), { signal: controller.signal });
    const assertion = expect(result).rejects.toMatchObject({ name: 'AbortError' });

    // Act
    controller.abort();

    // Assert
    await assertion;
    expect(lastUpload().isAborted).toBe(true);
  });

  it('送出之前就已經取消的不送請求', async () => {
    // Arrange
    const controller = new AbortController();
    controller.abort();

    // Act
    const result = uploadAttachment('SMK-7', aFile('a.pdf'), { signal: controller.signal });

    // Assert
    await expect(result).rejects.toMatchObject({ name: 'AbortError' });
    expect(uploads).toHaveLength(0);
  });
});

describe('列出與刪除', () => {
  it('內容網址會編碼附件編號', () => {
    expect(attachmentContentUrl('a b')).toBe('/v1/attachments/a%20b/content');
  });

  it('列出項目的附件', async () => {
    // Arrange
    const list = [attachment(1)];
    server.use(http.get('/v1/items/SMK-7/attachments', () => ok(list)));

    // Act
    const result = await listAttachments('SMK-7');

    // Assert
    expect(result).toEqual(list);
  });

  it('刪除回傳刪掉的那一個；404 丟出 attachment.not_found', async () => {
    // Arrange
    const first = attachment(1);
    server.use(
      http.delete(`/v1/attachments/${first.id}`, () => ok(first)),
      http.delete('/v1/attachments/gone', () => failure(404, 'attachment.not_found', '找不到這個附件。')),
    );

    // Act & Assert
    await expect(deleteAttachment(first.id)).resolves.toEqual(first);
    await expect(deleteAttachment('gone')).rejects.toMatchObject({ status: 404, code: 'attachment.not_found' });
  });
});
