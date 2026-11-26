import { describe, expect, it } from 'vitest';
import { grantOf } from '../../test/oauth';
import { exampleMachine } from '../../test/personalAi';
import { tokenOf } from '../../test/account';
import { agentsLine, cliLine, localAgentLine } from './setupStatus';

const NOW = Date.parse('2026-11-24T00:00:00Z');

describe('設定精靈：命令列的狀態', () => {
  it('沒有瀏覽器登入建的權杖（網頁上自己建的沒有電腦名稱）：還沒有', () => {
    expect(cliLine([tokenOf()], NOW)).toEqual({ kind: 'todo', text: '還沒有' });
  });

  it('有電腦名稱的命令列權杖：已登入，電腦名稱去重', () => {
    // Arrange
    const tokens = [tokenOf({ tokenId: 'a', machineName: 'MacBook-Pro' }), tokenOf({ tokenId: 'b', machineName: 'MacBook-Pro' }), tokenOf({ tokenId: 'c', machineName: 'Desktop' })];

    // Act
    const line = cliLine(tokens, NOW);

    // Assert
    expect(line).toEqual({ kind: 'done', text: '已登入：MacBook-Pro、Desktop' });
  });

  it('本機代理用的權杖（有 agent:run）與已經過期的不算', () => {
    // Arrange
    const tokens = [
      tokenOf({ tokenId: 'a', machineName: 'MacBook-Pro', scopes: ['agent:run', 'items:read'] }),
      tokenOf({ tokenId: 'b', machineName: 'Old', expiresAt: '2026-11-01T00:00:00Z' }),
    ];

    // Act
    // Assert
    expect(cliLine(tokens, NOW).kind).toBe('todo');
  });
});

describe('設定精靈：Claude Code／Codex 的狀態', () => {
  it('沒有授權：還沒有；有：應用名稱去重', () => {
    expect(agentsLine([])).toEqual({ kind: 'todo', text: '還沒有' });
    expect(agentsLine([grantOf({ grantId: 'a' }), grantOf({ grantId: 'b' }), grantOf({ grantId: 'c', clientName: 'Codex' })])).toEqual({
      kind: 'done',
      text: '已授權：Claude Code、Codex',
    });
  });
});

describe('設定精靈：本機代理的狀態', () => {
  it('在線：列在線的電腦名稱', () => {
    expect(localAgentLine([exampleMachine(), exampleMachine({ id: 'm2', name: '桌機', online: false })], true)).toEqual({ kind: 'done', text: '在線：範例筆電' });
  });

  it('都離線：已登記，目前離線', () => {
    expect(localAgentLine([exampleMachine({ online: false })], true)).toEqual({ kind: 'todo', text: '已登記，目前離線', isOffline: true });
  });

  it('沒有電腦：允許是還沒有，不允許是組織沒有開放；已經有電腦時不允許也照列', () => {
    expect(localAgentLine([], true)).toEqual({ kind: 'todo', text: '還沒有' });
    expect(localAgentLine([], false)).toEqual({ kind: 'muted', text: '組織沒有開放' });
    expect(localAgentLine([exampleMachine()], false).kind).toBe('done');
  });
});
