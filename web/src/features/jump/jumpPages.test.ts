import { describe, expect, it } from 'vitest';
import { agyDetail, exampleMember, exampleOwner } from '../../test/fixtures';
import type { SessionUser } from '../../api/types';
import { SETTINGS_HUB_PAGES } from '../settings-hub/settingsHubPages';
import { currentProjectKey, jumpPages } from './jumpPages';

describe('currentProjectKey', () => {
  it('專案頁與專案底下的每一頁都算在那個專案裡', () => {
    // Arrange
    const paths = ['/projects/AGY', '/projects/AGY/', '/projects/AGY/items/AGY-3', '/projects/AGY/settings/fields', '/projects/A%20B/changesets'];

    // Act
    const keys = paths.map(currentProjectKey);

    // Assert
    expect(keys).toEqual(['AGY', 'AGY', 'AGY', 'AGY', 'A B']);
  });

  it('專案清單與其他頁不在任何專案裡', () => {
    // Arrange
    const paths = ['/projects', '/projects/', '/my-work', '/people', '/', '/projects/%E0%A4%A'];

    // Act
    const keys = paths.map(currentProjectKey);

    // Assert
    expect(keys).toEqual([null, null, null, null, null, null]);
  });
});

const exampleAdmin: SessionUser = { ...exampleOwner, role: 'admin' };
const PERSONAL_PAGES = ['我的帳號', '通知', 'AI 帳號', '命令列與整合'];

describe('jumpPages', () => {
  it('不在專案裡：我的工作、知識庫、專案，加上個人的設定頁；成員沒有人員與組織的設定頁', () => {
    // Arrange、Act
    const pages = jumpPages({ user: exampleMember, project: null });

    // Assert
    expect(pages.map((page) => page.label)).toEqual(['我的工作', '知識庫', '專案', ...PERSONAL_PAGES]);
    expect(pages.slice(0, 3).map((page) => page.action)).toEqual([
      { kind: 'navigate', to: '/my-work' },
      { kind: 'navigate', to: '/knowledge' },
      { kind: 'navigate', to: '/projects' },
    ]);
  });

  it('設定頁的名稱與路徑直接來自設定中心的頁面表，旁邊寫是個人還是組織的設定', () => {
    // Arrange、Act
    const pages = jumpPages({ user: exampleOwner, project: null });

    // Assert
    const hubPages = pages.filter((page) => page.hint === '個人設定' || page.hint === '組織設定');
    expect(hubPages.map((page) => [page.label, page.hint, page.action])).toEqual(
      SETTINGS_HUB_PAGES.map((page) => [
        page.label,
        page.group === 'personal' ? '個人設定' : '組織設定',
        { kind: 'navigate', to: page.path },
      ]),
    );
  });

  it('管理員多了人員、說明格式、寄信設定、儲存空間、AI 設定，沒有主機 AI 代理', () => {
    // Arrange、Act
    const pages = jumpPages({ user: exampleAdmin, project: null });

    // Assert
    expect(pages.map((page) => page.label)).toEqual(['我的工作', '知識庫', '專案', '人員', ...PERSONAL_PAGES, '說明格式', '寄信設定', '儲存空間', 'AI 設定']);
    expect(pages.map((page) => page.action)).toContainEqual({ kind: 'navigate', to: '/people' });
  });

  it('擁有者四個組織的設定頁都有，主機 AI 代理只有擁有者', () => {
    // Arrange、Act
    const pages = jumpPages({ user: exampleOwner, project: null });

    // Assert
    expect(pages.at(-1)).toMatchObject({ label: '主機 AI 代理', action: { kind: 'navigate', to: '/ai-settings/host' } });
  });

  it('在專案裡、看得到設定：設定、成員、變更集，旁邊寫出是哪個專案', () => {
    // Arrange、Act
    const pages = jumpPages({ user: exampleMember, project: agyDetail() });

    // Assert
    expect(pages.slice(3 + PERSONAL_PAGES.length).map((page) => [page.label, page.hint, page.action])).toEqual([
      ['設定', '接案測試', { kind: 'navigate', to: '/projects/AGY/settings' }],
      ['成員', '接案測試', { kind: 'members', projectKey: 'AGY' }],
      ['變更集', '接案測試', { kind: 'navigate', to: '/projects/AGY/changesets' }],
    ]);
  });

  it('沒有 blueprint.read 就沒有設定與變更集，成員照樣看得到', () => {
    // Arrange
    const project = agyDetail({ myPermissions: ['item.read', 'item.create', 'item.update', 'item.transition'] });

    // Act
    const pages = jumpPages({ user: exampleMember, project });

    // Assert
    expect(pages.map((page) => page.label)).toEqual(['我的工作', '知識庫', '專案', ...PERSONAL_PAGES, '成員']);
  });

  it('每一頁都有圖示，鍵在全部頁面裡不重複', () => {
    // Arrange、Act
    const pages = jumpPages({ user: exampleOwner, project: agyDetail() });

    // Assert
    expect(pages.every((page) => page.icon !== undefined)).toBe(true);
    expect(new Set(pages.map((page) => page.key)).size).toBe(pages.length);
  });
});
