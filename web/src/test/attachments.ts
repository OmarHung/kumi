import { screen, waitFor, within } from '@testing-library/react';
import { expect, vi } from 'vitest';
import { http, type JsonBodyType } from 'msw';
import type { Attachment } from '../api/attachments';
import type { ProjectDetail } from '../api/types';
import { created, ownerRef, serveActivity } from './activity';
import { failure, ok, signedInAs } from './api';
import { exampleOwner, smkDetail, smkItem } from './fixtures';
import { detailOf, serveItem } from './items';
import { renderApp } from './renderApp';
import { server } from './server';

/** 附件（和合約一樣的形狀）。number 同時決定編號與預設的檔名「附件 N.pdf」。 */
export function attachment(number: number, overrides: Partial<Attachment> = {}): Attachment {
  return {
    id: `0190a100-0000-7000-8000-${String(number).padStart(12, '0')}`,
    itemKey: 'SMK-7',
    fileName: `附件 ${String(number)}.pdf`,
    contentType: 'application/pdf',
    sizeBytes: 1_258_291,
    sha256: 'a'.repeat(64),
    uploadedBy: ownerRef,
    uploadedAt: '2026-11-13T08:00:00Z',
    ...overrides,
  };
}

export interface AttachmentRejection {
  readonly status: number;
  readonly code: string;
  readonly message: string;
  readonly details?: JsonBodyType;
}

export interface ServedAttachments {
  /** GET 清單收到的請求數。 */
  readonly listRequests: () => number;
  /** 收到的刪除請求（附件編號，依序）。 */
  readonly deleted: string[];
  /** 伺服器上現在有的。 */
  readonly current: () => readonly Attachment[];
  /** 伺服器多了一個（上傳完成）。 */
  readonly add: (added: Attachment) => void;
  /** 之後的刪除請求都回這個錯誤。 */
  readonly rejectDelete: (rejection: AttachmentRejection) => void;
  /** 清單請求回 500（false 是恢復）。 */
  readonly failList: (isFailing?: boolean) => void;
}

/** 假的附件清單與刪除。刪除成功時伺服器上就少了那一個，回傳刪掉的附件。 */
export function serveAttachments(itemKey: string, initial: readonly Attachment[]): ServedAttachments {
  let list = initial;
  let listRequests = 0;
  let rejection: AttachmentRejection | null = null;
  let isListFailing = false;
  const deleted: string[] = [];
  server.use(
    http.get(`/v1/items/${itemKey}/attachments`, () => {
      listRequests += 1;
      return isListFailing ? failure(500, 'server.error', 'boom') : ok(list);
    }),
    http.delete('/v1/attachments/:id', ({ params }) => {
      const id = String(params.id);
      deleted.push(id);
      // 404 是「別處先刪了」：伺服器上也已經沒有它。
      if (rejection?.status === 404) list = list.filter((candidate) => candidate.id !== id);
      if (rejection !== null) return failure(rejection.status, rejection.code, rejection.message, rejection.details);
      const found = list.find((candidate) => candidate.id === id);
      if (found === undefined) return failure(404, 'attachment.not_found', '找不到這個附件。');
      list = list.filter((candidate) => candidate.id !== id);
      return ok(found);
    }),
  );
  return {
    listRequests: () => listRequests,
    deleted,
    current: () => list,
    add: (added) => {
      list = [...list, added];
    },
    rejectDelete: (next) => {
      rejection = next;
    },
    failList: (isFailing = true) => {
      isListFailing = isFailing;
    },
  };
}

/**
 * 假的 XMLHttpRequest：測試手動推進一次上傳（進度、成功、失敗）。
 * 不用 MSW 攔 XHR：它不會送出上傳的進度，也沒辦法停在「送出中」。
 */
export class FakeUpload extends EventTarget {
  static readonly instances: FakeUpload[] = [];

  readonly upload = new EventTarget();
  readonly headers = new Map<string, string>();
  method = '';
  url = '';
  body: unknown = null;
  isAborted = false;
  isSent = false;
  isFinished = false;
  responseType = '';
  status = 0;
  responseText = '';

  constructor() {
    super();
    FakeUpload.instances.push(this);
  }

  open(method: string, url: string): void {
    this.method = method;
    this.url = url;
  }

  setRequestHeader(name: string, value: string): void {
    this.headers.set(name, value);
  }

  send(body: unknown): void {
    this.body = body;
    this.isSent = true;
  }

  /** 和真的 XHR 一樣：還沒送出、或已經結束時 abort() 什麼都不發。 */
  abort(): void {
    if (!this.isSent || this.isFinished) return;
    this.isFinished = true;
    this.isAborted = true;
    this.dispatchEvent(new Event('abort'));
  }

  /** 上傳的檔案（multipart 的 file 欄位）。 */
  get file(): File | null {
    const value = this.body instanceof FormData ? this.body.get('file') : null;
    return value instanceof File ? value : null;
  }

  progress(loaded: number, total: number | null): void {
    this.upload.dispatchEvent(new ProgressEvent('progress', { loaded, total: total ?? 0, lengthComputable: total !== null }));
  }

  /** 伺服器回應：data 放進成功的信封。 */
  respond(status: number, data: JsonBodyType): void {
    this.isFinished = true;
    this.status = status;
    this.responseText = JSON.stringify({ success: true, data, error: null });
    this.dispatchEvent(new Event('load'));
  }

  respondFailure(status: number, code: string, message: string): void {
    this.isFinished = true;
    this.status = status;
    this.responseText = JSON.stringify({ success: false, data: null, error: { code, message, details: [] } });
    this.dispatchEvent(new Event('load'));
  }

  /** 沒有信封的回應（代理伺服器的頁面）。 */
  respondRaw(status: number, text: string): void {
    this.isFinished = true;
    this.status = status;
    this.responseText = text;
    this.dispatchEvent(new Event('load'));
  }

  failNetwork(): void {
    this.isFinished = true;
    this.dispatchEvent(new Event('error'));
  }
}

/** 把全域的 XMLHttpRequest 換成 FakeUpload；測試結束要 vi.unstubAllGlobals()。 */
export function installFakeUploads(): readonly FakeUpload[] {
  FakeUpload.instances.length = 0;
  vi.stubGlobal('XMLHttpRequest', FakeUpload);
  return FakeUpload.instances;
}

/** 一個測試用的檔案。 */
export function aFile(name: string, size = 1024, type = 'application/pdf'): File {
  const file = new File(['x'], name, { type });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

export const attachmentItem = smkItem({ number: 7, title: '首頁設計稿' });

interface OpenOptions {
  readonly attachments?: readonly Attachment[];
  readonly detail?: ProjectDetail;
  /** 項目頁的網址；預設是整頁。 */
  readonly path?: string;
  /** 清單一開始就讀不到。 */
  readonly isListFailing?: boolean;
  /** StrictMode（開發模式 effect 會跑兩次）。 */
  readonly strict?: boolean;
}

/** 開 SMK-7 的項目頁，等附件區出現。回傳 renderApp 的結果、附件區與假的伺服器。 */
export async function openAttachments({
  attachments = [],
  detail = smkDetail(),
  path = '/projects/SMK/items/SMK-7',
  isListFailing = false,
  strict = false,
}: OpenOptions = {}) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItem(detailOf(attachmentItem));
  const timeline = serveActivity(attachmentItem.key, [created()]);
  const served = serveAttachments(attachmentItem.key, attachments);
  served.failList(isListFailing);
  const view = renderApp(path, { strict });
  const section = await screen.findByRole('region', { name: '附件' });
  // 清單讀完（有資料或讀不到）才回來：之前沒有「上傳檔案」。
  await waitFor(() => {
    expect(within(section).queryByText('正在載入附件…')).not.toBeInTheDocument();
  });
  return { ...view, section, served, timeline };
}
