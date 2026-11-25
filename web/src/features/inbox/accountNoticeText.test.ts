import { describe, expect, it } from 'vitest';
import { accountNotification } from '../../test/notifications';
import { accountNoticeSentence } from './accountNoticeText';

const sentence = (...args: Parameters<typeof accountNotification>) => accountNoticeSentence(accountNotification(...args).account);

describe('帳號通知的一句話', () => {
  it('手動建立權杖：名稱、範圍、來源位址', () => {
    expect(
      sentence({
        event: 'access_token_created',
        tokens: [{ name: 'CI', scopes: ['items:read', 'items:write'], expiresAt: null }],
        sourceAddress: '203.0.113.5',
      }),
    ).toBe('建立了存取權杖「CI」（讀取項目、修改項目），從 203.0.113.5');
  });

  it('命令列登入：電腦名稱與建立了幾把', () => {
    expect(sentence({ event: 'cli_login_tokens_issued', machineName: 'MacBook-Pro', sourceAddress: null })).toBe(
      '命令列在「MacBook-Pro」登入，建立了 2 把存取權杖',
    );
  });

  it('命令列登入沒有電腦名稱時不留空的引號', () => {
    expect(sentence({ event: 'cli_login_tokens_issued', machineName: null, sourceAddress: null })).toBe('命令列登入，建立了 2 把存取權杖');
  });

  it('授權 AI 用戶端：用戶端的名稱', () => {
    expect(
      sentence({ event: 'oauth_app_authorized', tokens: [], clientName: 'Claude Code', sourceAddress: null }),
    ).toBe('授權了「Claude Code」連到你的帳號');
  });

  it('登入碼被重複使用：撤銷了幾把', () => {
    expect(
      sentence({ event: 'cli_login_code_reused', tokens: [], revokedTokens: 2, sourceAddress: '203.0.113.5' }),
    ).toBe('登入碼被重複使用，已撤銷 2 把存取權杖，從 203.0.113.5');
  });

  it('本機代理從新的電腦登記：電腦名稱與來源位址', () => {
    expect(
      sentence({ event: 'local_agent_machine_added', tokens: [], machineName: 'Unknown-PC', sourceAddress: '203.0.113.9' }),
    ).toBe('本機代理第一次從新的電腦「Unknown-PC」登記，從 203.0.113.9');
    expect(sentence({ event: 'local_agent_machine_added', tokens: [], machineName: null, sourceAddress: null })).toBe('本機代理第一次從新的電腦登記');
  });

  it('不認得的範圍照原名寫出來，不讓人看不到有這一項', () => {
    expect(
      sentence({
        event: 'access_token_created',
        tokens: [{ name: '新的', scopes: ['reports:read'], expiresAt: null }],
        sourceAddress: null,
      }),
    ).toBe('建立了存取權杖「新的」（reports:read）');
  });

  it('一次建立好幾把（沒有單一名稱可寫）只說幾把', () => {
    expect(sentence({ event: 'access_token_created', sourceAddress: null })).toBe('建立了 2 把存取權杖');
  });
});
