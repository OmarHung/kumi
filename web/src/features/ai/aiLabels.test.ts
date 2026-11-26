import { describe, expect, it } from 'vitest';
import { OPTION_LABELS, PROVIDER_LABELS, toolLabel } from './aiLabels';

describe('AI 種類的說法', () => {
  it('本機的引擎叫 Claude Code 與 Codex', () => {
    expect(OPTION_LABELS.claude_code).toBe('Claude Code');
    expect(OPTION_LABELS.codex).toBe('Codex');
  });

  it('能選的種類包含所有供應商，供應商的名稱不變；能存的供應商裡沒有本機的引擎', () => {
    expect(OPTION_LABELS.anthropic).toBe(PROVIDER_LABELS.anthropic);
    expect(Object.keys(PROVIDER_LABELS)).not.toContain('claude_code');
    expect(Object.keys(PROVIDER_LABELS)).not.toContain('codex');
  });
});

describe('工具的說法', () => {
  it('項目的三個工具', () => {
    // Arrange / Act / Assert
    expect(toolLabel('get_item')).toBe('讀取項目');
    expect(toolLabel('get_item', 'WEB-12')).toBe('讀取 WEB-12');
    expect(toolLabel('get_item', null)).toBe('讀取項目');
    expect(toolLabel('search_items')).toBe('搜尋項目');
    expect(toolLabel('propose_item_changes')).toBe('建議修改');
    expect(toolLabel('propose_new_items')).toBe('建議新增項目');
  });

  it('不選專案的兩個工具', () => {
    // Arrange / Act / Assert
    expect(toolLabel('list_projects')).toBe('查看你的專案清單');
    expect(toolLabel('propose_project')).toBe('提出新專案');
  });

  it('派工的三個工具', () => {
    // Arrange / Act / Assert
    expect(toolLabel('get_project')).toBe('讀專案的成員與設定');
    expect(toolLabel('list_people')).toBe('查組織的人員');
    expect(toolLabel('propose_member_changes')).toBe('建議成員變動');
  });

  it('不認得的名稱照原文', () => {
    // Arrange / Act / Assert
    expect(toolLabel('something_new')).toBe('something_new');
  });
});
