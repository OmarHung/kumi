import { afterEach, describe, expect, it, vi } from 'vitest';
import { makeAiOptions } from '../../test/assistant';
import { readStoredSelection, resolveSelection, SELECTION_STORAGE_KEY, storeSelection, withModel } from './selection';

const options = makeAiOptions();
const OPUS = { connectionId: 'conn-1', model: 'claude-opus-5-5', effort: 'low' } as const;

afterEach(() => {
  vi.restoreAllMocks();
});

describe('記在瀏覽器的選擇', () => {
  it('存了再讀得回來', () => {
    storeSelection(OPUS);
    expect(readStoredSelection()).toEqual(OPUS);
  });

  it('內容壞掉、格式不對、讀不到時當成沒記過', () => {
    localStorage.setItem(SELECTION_STORAGE_KEY, '{oops');
    expect(readStoredSelection()).toBeNull();
    localStorage.setItem(SELECTION_STORAGE_KEY, JSON.stringify({ connectionId: 'c', model: 'm', effort: 'huge' }));
    expect(readStoredSelection()).toBeNull();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(readStoredSelection()).toBeNull();
  });

  it('寫不進去也不丟錯誤', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(() => {
      storeSelection(OPUS);
    }).not.toThrow();
  });
});

describe('現在用哪一個', () => {
  it('記的還能用就用它', () => {
    expect(resolveSelection(options, OPUS)).toEqual(OPUS);
  });

  it('記的連線或模型不能選了：退回組織的預設', () => {
    expect(resolveSelection(options, { connectionId: 'gone', model: 'x', effort: null })).toEqual(options.default);
    expect(resolveSelection(options, { connectionId: 'conn-1', model: 'retired', effort: null })).toEqual(options.default);
  });

  it('模型還在、思考深度那個模型沒有：換成「預設」，不整個丟掉', () => {
    expect(resolveSelection(options, { connectionId: 'conn-1', model: 'claude-haiku-5', effort: 'high' })).toEqual({
      connectionId: 'conn-1',
      model: 'claude-haiku-5',
      effort: null,
    });
  });

  it('沒記過也沒有預設：取第一個開放的模型；一個都沒有是 null', () => {
    expect(resolveSelection({ ...options, default: null }, null)).toEqual({
      connectionId: 'conn-1',
      model: 'claude-opus-5-5',
      effort: null,
    });
    expect(resolveSelection({ ...options, connections: [], default: null }, null)).toBeNull();
  });

  it('換模型：思考深度保留（新模型有這一級）或換成「預設」', () => {
    expect(withModel(options, OPUS, 'conn-1', 'claude-haiku-5').effort).toBeNull();
    expect(withModel(options, OPUS, 'conn-1', 'claude-opus-5-5').effort).toBe('low');
  });
});

describe('現在用哪一個：不能用的主機來源', () => {
  const [anthropic] = options.connections;
  const hosted = (notice: string | null) => ({
    id: 'src-1',
    name: '主機上的 Claude Code',
    kind: 'claude_code' as const,
    personal: false,
    hosted: true,
    notice,
    models: [{ id: 'opus', displayName: 'Opus', efforts: [], defaultEffort: null }],
  });
  const chosen = { connectionId: 'src-1', model: 'opus', effort: null };

  it('記的是主機來源、它現在不能用：退回預設', () => {
    if (anthropic === undefined) throw new Error('測試資料缺連線');
    const withNotice = { ...options, connections: [anthropic, hosted('主機離線中。')] };
    expect(resolveSelection(withNotice, chosen)).toEqual(options.default);
  });

  it('主機來源能用時照樣用它', () => {
    if (anthropic === undefined) throw new Error('測試資料缺連線');
    expect(resolveSelection({ ...options, connections: [anthropic, hosted(null)] }, chosen)).toEqual(chosen);
  });

  it('只剩不能用的主機來源：沒有可選的（null）', () => {
    expect(resolveSelection({ ...options, default: null, connections: [hosted('主機離線中。')] }, null)).toBeNull();
  });
});
