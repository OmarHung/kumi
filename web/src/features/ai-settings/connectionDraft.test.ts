import { describe, expect, it } from 'vitest';
import { exampleConnection } from '../../test/aiSettings';
import {
  draftFrom,
  emptyDraft,
  needsNewApiKey,
  toInput,
  withCustomModel,
  withDiscovered,
  withKind,
  withModelToggled,
  withoutCustomModel,
} from './connectionDraft';

describe('連線草稿', () => {
  it('換供應商：名稱還是自動填的就跟著換；改過名稱就不換', () => {
    // Arrange
    const untouched = emptyDraft();
    const edited = { ...emptyDraft(), name: '公司的帳號', isNameEdited: true };

    // Act / Assert
    expect(withKind(untouched, 'openai').name).toBe('OpenAI');
    expect(withKind(edited, 'openai').name).toBe('公司的帳號');
  });

  it('換供應商：上一家讀到的模型清單清掉，手動加的留著', () => {
    // Arrange
    const draft = withCustomModel(withDiscovered(emptyDraft(), [{ id: 'a', displayName: 'A', efforts: [] }]), 'mine').draft;
    const opened = withModelToggled(draft, 'a');

    // Act
    const next = withKind(opened, 'google');

    // Assert
    expect(next.listed).toEqual([]);
    expect(next.custom.map((model) => model.id)).toEqual(['mine']);
    expect(next.enabled).toEqual(['mine']);
  });

  it('讀到新的清單：已經開放的保持勾選，清單裡沒有的不再開放', () => {
    // Arrange
    const draft = { ...draftFrom(exampleConnection()) };

    // Act
    const next = withDiscovered(draft, [
      { id: 'claude-opus-5-5', displayName: 'Claude Opus 5.5', efforts: ['low'] },
      { id: 'claude-new', displayName: 'Claude New', efforts: [] },
    ]);

    // Assert
    expect(next.enabled).toEqual(['claude-opus-5-5']);
    expect(next.listed.map((model) => model.id)).toEqual(['claude-opus-5-5', 'claude-new']);
  });

  it('手動加模型：空的不行；加進來就開放；已經在清單裡只是打開；可以移除', () => {
    // Arrange
    const base = withDiscovered(emptyDraft(), [{ id: 'a', displayName: 'A', efforts: [] }]);

    // Act
    const empty = withCustomModel(base, '  ');
    const added = withCustomModel(base, ' x-1 ');
    const existing = withCustomModel(base, 'a');
    const removed = withoutCustomModel(added.draft, 'x-1');

    // Assert
    expect(empty.error).not.toBeNull();
    expect(added.draft.custom.map((model) => model.id)).toEqual(['x-1']);
    expect(added.draft.enabled).toEqual(['x-1']);
    expect(existing.draft.custom).toEqual([]);
    expect(existing.draft.enabled).toEqual(['a']);
    expect(removed.custom).toEqual([]);
    expect(removed.enabled).toEqual([]);
  });

  it('要存的連線：金鑰留白是 null；只有相容端點送基底網址；手動加的代號分開送', () => {
    // Arrange
    const draft = withCustomModel({ ...emptyDraft(), kind: 'openai' as const, baseUrl: 'http://x/v1', name: ' 我的 ' }, 'm1').draft;

    // Act
    const input = toInput(draft);

    // Assert
    expect(input).toEqual({ name: '我的', kind: 'openai', baseUrl: null, apiKey: null, enabledModels: ['m1'], customModels: ['m1'] });
    expect(toInput({ ...draft, kind: 'openai_compatible', apiKey: ' k ' })).toMatchObject({ baseUrl: 'http://x/v1', apiKey: 'k' });
  });

  it('編輯時換了供應商或網址而金鑰留白：要重新輸入；沒存過金鑰就不用', () => {
    // Arrange
    const stored = exampleConnection();
    const draft = draftFrom(stored);

    // Act / Assert
    expect(needsNewApiKey(draft, stored)).toBe(false);
    expect(needsNewApiKey({ ...draft, kind: 'openai' }, stored)).toBe(true);
    expect(needsNewApiKey({ ...draft, kind: 'openai', apiKey: 'sk-new' }, stored)).toBe(false);
    expect(needsNewApiKey({ ...draft, kind: 'openai' }, exampleConnection({ hasApiKey: false, apiKeyHint: null }))).toBe(false);
  });
});
