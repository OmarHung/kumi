import type { QueryClient } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { draftFromBlueprint } from '../../domain/settingsDraft';
import { authRequired, ok, signedInAs } from '../../test/api';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { draftToResume, hasProposedDraft, releaseProposedDraft, rememberProposedDraft, type ProposedDraft } from './settingsDraftMemory';

const draft: ProposedDraft = {
  personId: exampleOwner.personId,
  projectKey: 'SMK',
  session: { base: simpleTasksBlueprint, baseVersion: 1, draft: draftFromBlueprint(simpleTasksBlueprint) },
  summary: '拿掉待辦',
  section: 'workflows',
};

/** 一份還在等待核准（SMK #7），一份按過「回去修改」、等設定頁接回去（AGY #2）。 */
function rememberBoth(queryClient: QueryClient) {
  rememberProposedDraft(queryClient, draft, 7);
  rememberProposedDraft(queryClient, { ...draft, projectKey: 'AGY' }, 2);
  releaseProposedDraft(queryClient, 'AGY', 1, 2);
}

function expectForgotten(queryClient: QueryClient) {
  expect(hasProposedDraft(queryClient, 'SMK', 1, 7, exampleOwner.personId)).toBe(false);
  expect(draftToResume(queryClient, 'AGY', exampleOwner.personId)).toBeUndefined();
}

/** 共用電腦：設定頁送出後記在記憶體的草稿是上一個人的修改，登出之後不能留著。 */
describe('登出時清掉設定頁記在記憶體的草稿', () => {
  it('從帳號選單登出：記下的草稿都清掉', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.delete('/v1/session', () => ok(null)));
    const { user, queryClient } = renderApp('/my-work');
    rememberBoth(queryClient);
    await user.click(await screen.findByRole('button', { name: '範例使用者（範例組織 · 擁有者）' }));

    // Act
    await user.click(within(screen.getByRole('menu', { name: '帳號' })).getByRole('menuitem', { name: '登出' }));

    // Assert
    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    expectForgotten(queryClient);
  });

  it('工作階段失效（任何請求收到 401）導回登入頁時也清掉', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    server.use(http.get('/v1/my-work', () => authRequired()));

    // Act
    const { queryClient } = renderApp('/my-work');
    rememberBoth(queryClient);

    // Assert
    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    expectForgotten(queryClient);
  });
});
