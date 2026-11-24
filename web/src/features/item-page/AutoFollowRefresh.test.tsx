import { act, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { QueryClient } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AUTO_FOLLOW_REFRESH_MS, refreshSubscriptionLater } from '../../api/notificationCache';
import { queryKeys } from '../../api/queryKeys';
import { created, serveActivity, serveComment } from '../../test/activity';
import { ok, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { editorText, findEditor, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '舊的說明');

afterEach(() => {
  vi.useRealTimers();
});

describe('refreshSubscriptionLater', () => {
  it('延遲約 2.5 秒才讓這個項目的關注查詢失效，不是馬上', () => {
    // Arrange
    vi.useFakeTimers();
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    // Act
    refreshSubscriptionLater(queryClient, 'SMK-7');
    vi.advanceTimersByTime(AUTO_FOLLOW_REFRESH_MS - 1);

    // Assert
    expect(invalidate).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.itemSubscription('SMK-7') });
  });
});

/** 假的關注狀態：後端「自動關注」要等 trigger() 之後才生效，GET 會讀到當時的狀態。 */
function serveAutoFollow(itemKey: string) {
  let following = false;
  const state = { gets: 0, trigger: () => { following = true; } };
  server.use(
    http.get(`/v1/items/${itemKey}/subscription`, () => {
      state.gets += 1;
      return ok({ following, reason: following ? 'commenter' : null, canFollow: true });
    }),
  );
  return state;
}

describe('自己做了會自動關注的事之後，關注的按鈕跟上', () => {
  it('留言送出：約 2.5 秒後重問關注的狀態，按鈕變成 aria-pressed', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(task);
    serveActivity(task.key, [created()]);
    serveComment(task.key);
    const subscription = serveAutoFollow(task.key);
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    const button = await screen.findByRole('button', { name: '關注' });
    expect(button).toHaveAttribute('aria-pressed', 'false');
    const editor = await findEditor('留言');
    await typeInEditor(user, editor, '好的');
    vi.useFakeTimers({ shouldAdvanceTime: true });

    // Act
    await user.keyboard('{Control>}{Enter}{/Control}');
    await waitFor(() => {
      expect(editorText(editor)).toBe('');
    });
    subscription.trigger();
    const before = subscription.gets;

    // Assert：馬上不重問
    expect(button).toHaveAttribute('aria-pressed', 'false');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(AUTO_FOLLOW_REFRESH_MS);
    });
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '關注' })).toHaveAttribute('aria-pressed', 'true');
    });
    expect(subscription.gets).toBeGreaterThan(before);
  });

  it('改說明儲存成功：同樣延後重問', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(task);
    serveActivity(task.key, [created()]);
    servePatch(task);
    const subscription = serveAutoFollow(task.key);
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('button', { name: '關注' });
    const section = await screen.findByRole('region', { name: '說明' });
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    await typeInEditor(user, await findEditor('說明', section), '加一行');
    vi.useFakeTimers({ shouldAdvanceTime: true });

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));
    await waitFor(() => {
      expect(within(section).queryByRole('textbox')).not.toBeInTheDocument();
    });
    subscription.trigger();

    // Assert
    expect(screen.getByRole('button', { name: '關注' })).toHaveAttribute('aria-pressed', 'false');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(AUTO_FOLLOW_REFRESH_MS);
    });
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '關注' })).toHaveAttribute('aria-pressed', 'true');
    });
  });
});
