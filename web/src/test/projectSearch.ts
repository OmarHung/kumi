import { http } from 'msw';
import type { ProjectSearchHit } from '../api/types';
import { ok } from './api';
import { server } from './server';

/**
 * 假的 GET /v1/search/projects：代號、名稱或摘錄包含 q（不分大小寫）的才回，順序照給的（伺服器排好的順序），
 * 超過 limit 的截掉。回傳收到的每一個請求的網址。
 */
export function serveProjectSearch(hits: readonly ProjectSearchHit[]): URL[] {
  const requests: URL[] = [];
  server.use(
    http.get('/v1/search/projects', ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      const q = (url.searchParams.get('q') ?? '').toLowerCase();
      const limit = Number(url.searchParams.get('limit') ?? '8');
      const found = hits.filter(({ project, snippet }) =>
        [project.key, project.name, snippet ?? ''].some((text) => text.toLowerCase().includes(q)),
      );
      return ok(found.slice(0, limit));
    }),
  );
  return requests;
}
