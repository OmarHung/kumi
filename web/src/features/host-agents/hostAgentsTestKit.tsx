import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import type { HostAgentOverview } from '../../api/hostAgentTypes';
import type { SessionUser } from '../../api/types';
import { ok, signedInAs } from '../../test/api';
import { serveAiSettings } from '../../test/aiSettings';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { serveHostAgents } from '../../test/hostAgents';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

export { exampleMember };

export const HOST_PATH = '/ai-settings/host';
export const exampleAdmin: SessionUser = { ...exampleOwner, personId: '0190a000-0000-7000-8000-000000000003', displayName: '範例管理員', email: 'admin@example.com', role: 'admin' };

/** 擁有者打開「主機 AI 代理」頁，等到載入完（有「新增主機代理」鈕）。 */
export async function openHostSection(initial: Partial<HostAgentOverview> = {}) {
  signedInAs(exampleOwner);
  serveAiSettings();
  const backend = serveHostAgents(initial);
  const view = renderApp(HOST_PATH);
  const add = await screen.findByRole('button', { name: '新增主機代理' });
  return { ...view, backend, add };
}

/**
 * 管理員、一般成員（或擁有者當對照）打開「主機 AI 代理」頁：記下有沒有人去讀主機的 API。
 * 載入完之後再多讓 MSW 與查詢跑一輪，「沒有讀」的斷言才不會因為請求還沒發出而假通過。
 */
export async function openAs(user: SessionUser) {
  signedInAs(user);
  serveAiSettings();
  const reads = serveHostReads();
  const view = renderApp(HOST_PATH);
  if (user.role === 'owner') await screen.findByRole('button', { name: '新增主機代理' });
  else await screen.findByText('只有組織的擁有者可以設定主機 AI 代理。');
  await settle();
  return { ...view, reads };
}

const SETTLE_MS = 150;

/** 讓 MSW 與查詢多跑一輪，「沒有讀」的斷言才不會因為請求還沒發出而假通過。 */
export async function settle() {
  await new Promise((resolve) => setTimeout(resolve, SETTLE_MS));
}

/** 記下有沒有人去讀主機的 API。 */
export function serveHostReads(): string[] {
  const reads: string[] = [];
  server.use(
    http.get('/v1/ai/host', () => {
      reads.push('read');
      return ok({ agents: [], sources: [] });
    }),
  );
  return reads;
}

/** 查詢快取與 mutation 快取序列化之後的內容（配對碼、登入碼不能在裡面）。 */
export function serializedCaches(client: ReturnType<typeof renderApp>['queryClient']): string {
  return JSON.stringify([client.getQueryCache().getAll().map((query) => query.state.data), client.getMutationCache().getAll().map((mutation) => mutation.state.data)]);
}

/** 列上的「更多」選單裡選一個動作。 */
export async function chooseMenuAction(user: ReturnType<typeof renderApp>['user'], menuTriggerName: string, action: string) {
  await user.click(screen.getByRole('button', { name: menuTriggerName }));
  await user.click(await screen.findByRole('menuitem', { name: action }));
}

export function engineRow(label: string): HTMLElement {
  return within(screen.getByRole('list', { name: /的引擎$/ })).getByRole('listitem', { name: label });
}
