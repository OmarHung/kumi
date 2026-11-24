import { describe, expect, it } from 'vitest';
import { findSettingsHubPage, isSettingsHubPath } from './settingsHubPages';

describe('設定中心的頁面表', () => {
  it('網址完全對到才是那一頁；結尾的斜線不算', () => {
    // Arrange / Act / Assert
    expect(findSettingsHubPage('/account')?.key).toBe('account');
    expect(findSettingsHubPage('/account/ai/')?.key).toBe('ai-accounts');
    expect(findSettingsHubPage('/account/other')).toBeUndefined();
  });

  it('不分大小寫：React Router 的路由不分，/Account/AI 也掛得上，頁名與目前所在要對得上', () => {
    // Arrange / Act / Assert
    expect(findSettingsHubPage('/Account/AI')?.key).toBe('ai-accounts');
    expect(findSettingsHubPage('/Email-Settings')?.key).toBe('email-settings');
    expect(isSettingsHubPath('/AI-Settings/Host')).toBe(true);
    expect(isSettingsHubPath('/Description-Templates/new')).toBe(true);
  });

  it('說明格式的新增與編輯頁算在設定中心（側欄的「設定」是目前所在），其他頁不算', () => {
    // Arrange / Act / Assert
    expect(isSettingsHubPath('/description-templates/new')).toBe(true);
    expect(isSettingsHubPath('/description-templates/abc')).toBe(true);
    expect(isSettingsHubPath('/my-work')).toBe(false);
    expect(isSettingsHubPath('/projects/AGY/settings')).toBe(false);
  });
});
