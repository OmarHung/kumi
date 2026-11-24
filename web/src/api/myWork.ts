import { apiRequest } from './http';
import type { MyWork } from './types';

/** 跨專案、我負責而且還沒結束的項目。 */
export function getMyWork(signal?: AbortSignal): Promise<MyWork> {
  return apiRequest<MyWork>('/v1/my-work', { signal });
}
