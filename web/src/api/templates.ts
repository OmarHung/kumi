import { apiRequest } from './http';
import type { Template } from './types';

export function listTemplates(signal?: AbortSignal): Promise<readonly Template[]> {
  return apiRequest<readonly Template[]>('/v1/templates', { signal });
}
