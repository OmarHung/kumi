import { http } from 'msw';
import type { StorageUsage } from '../api/storage';
import { failure, ok } from './api';
import { server } from './server';

const GIB = 1024 ** 3;

/** 儲存空間用量的假資料：總量 1.2 GiB、上限 20 GiB，每人每日 2 GiB。 */
export function storageUsageOf(overrides: Partial<StorageUsage> = {}): StorageUsage {
  return {
    organizationLimitBytes: 20 * GIB,
    personDailyLimitBytes: 2 * GIB,
    totalBytes: 1_288_490_188,
    areas: [
      { area: 'item_attachments', bytes: 1_073_741_824, files: 312 },
      { area: 'knowledge_attachments', bytes: 214_748_364, files: 40 },
    ],
    ...overrides,
  };
}

/** 假的 GET /v1/storage-usage；rejectWith 是整個換成失敗（例如 403 storage.forbidden）。回傳收到的請求數。 */
export function serveStorageUsage(usage: StorageUsage = storageUsageOf(), rejectWith?: { readonly status: number; readonly code: string; readonly message: string }) {
  let requests = 0;
  server.use(
    http.get('/v1/storage-usage', () => {
      requests += 1;
      return rejectWith === undefined ? ok(usage) : failure(rejectWith.status, rejectWith.code, rejectWith.message);
    }),
  );
  return () => requests;
}
