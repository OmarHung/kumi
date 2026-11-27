import { QueryClient } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { queryKeys } from '../../api/queryKeys';
import { refreshAfterWithdraw, withdrewProposal } from './withdrawRefresh';

describe('withdrewProposal', () => {
  it('只有撤回工具成功時才算', () => {
    // Arrange
    const done = { type: 'tool', tool: { name: 'withdraw_proposal', status: 'done' } } as const;
    const failed = { type: 'tool', tool: { name: 'withdraw_proposal', status: 'failed' } } as const;
    const other = { type: 'tool', tool: { name: 'propose_item_deletions', status: 'done' } } as const;

    // Act／Assert
    expect(withdrewProposal(done)).toBe(true);
    expect(withdrewProposal(failed)).toBe(false);
    expect(withdrewProposal(other)).toBe(false);
    expect(withdrewProposal({ type: 'text' })).toBe(false);
  });
});

describe('refreshAfterWithdraw', () => {
  it('讓每一種建議的卡片都重讀，對話與別的查詢不動', async () => {
    // Arrange：助理撤回的是哪一份，工具段落沒有帶編號；卡片的狀態要重讀才會變成「已略過」。
    const client = new QueryClient();
    const proposalKeys = [
      queryKeys.itemProposal('a'),
      queryKeys.newItemProposal('b'),
      queryKeys.memberProposal('c'),
      queryKeys.projectProposal('d'),
      queryKeys.itemDeletionProposal('e'),
      queryKeys.itemBatchProposal('f'),
    ];
    for (const key of proposalKeys) client.setQueryData(key, { status: 'pending' });
    client.setQueryData(queryKeys.aiOptions, {});

    // Act
    await refreshAfterWithdraw(client);

    // Assert
    for (const key of proposalKeys) expect(client.getQueryState(key)?.isInvalidated).toBe(true);
    expect(client.getQueryState(queryKeys.aiOptions)?.isInvalidated).toBe(false);
  });
});
