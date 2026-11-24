import { describe, expect, it } from 'vitest';
import { exampleEngine, exampleHostAgent, exampleHostSource } from '../../test/hostAgents';
import { draftFromSource, emptySourceDraft, modelChoices, toSourceInput, validateSource } from './sourceDraft';

const AGENT = exampleHostAgent();
const READY = emptySourceDraft([AGENT], []);

describe('新增來源的預設值', () => {
  it('第一台代理的 Claude Code：名稱、開放引擎回報的模型、能接工作所以開著', () => {
    expect(READY).toMatchObject({ agentId: 'agent-1', engine: 'claude_code', name: '主機上的 Claude Code', enabled: true, enabledModels: ['opus', 'sonnet'], acknowledged: false });
  });

  it('Claude Code 已經有來源：預設換成還沒有的 Codex；Codex 還不能接工作所以先存成停用', () => {
    const draft = emptySourceDraft([AGENT], [exampleHostSource()]);
    expect(draft).toMatchObject({ engine: 'codex', name: '主機上的 Codex', enabled: false, enabledModels: [] });
  });

  it('沒有任何代理：代理是空的，驗證會擋下', () => {
    expect(validateSource(emptySourceDraft([], [])).agentId).toBeDefined();
  });
});

describe('驗證', () => {
  it('開著卻沒確認 API 計費：擋下，說明在計費確認那一欄', () => {
    expect(validateSource({ ...READY, enabled: true, acknowledged: false }).acknowledgeApiBilling).toMatch(/API 用量計費/);
    expect(validateSource({ ...READY, enabled: true, acknowledged: true })).toEqual({});
  });

  it('關著的來源不用確認計費、也可以沒有模型', () => {
    expect(validateSource({ ...READY, enabled: false, enabledModels: [], acknowledged: false })).toEqual({});
  });

  it('開著時至少一個模型；預設的模型要在開放的模型裡', () => {
    const errors = validateSource({ ...READY, acknowledged: true, enabledModels: [], defaultModel: 'opus' });
    expect(errors.enabledModels).toBeDefined();
    expect(errors.defaultModel).toBeDefined();
  });

  it('名稱不能空白或超過 60 字', () => {
    expect(validateSource({ ...READY, acknowledged: true, name: '   ' }).name).toBeDefined();
    expect(validateSource({ ...READY, acknowledged: true, name: 'x'.repeat(61) }).name).toBeDefined();
  });

  it.each([['0'], ['-3'], ['1.5'], ['abc'], ['1000001']])('每人每天的則數「%s」不合法', (value) => {
    expect(validateSource({ ...READY, acknowledged: true, dailyRequestsPerPerson: value }).dailyRequestsPerPerson).toBeDefined();
  });

  it('token 上限可以到一千億；留白是不限', () => {
    expect(validateSource({ ...READY, acknowledged: true, monthlyTokens: '100000000000' })).toEqual({});
    expect(validateSource({ ...READY, acknowledged: true, monthlyTokens: '100000000001' }).monthlyTokens).toBeDefined();
    expect(validateSource({ ...READY, acknowledged: true, monthlyTokens: '' })).toEqual({});
  });
});

describe('換成要送的內容', () => {
  it('空白的上限是 null、預設是 null、名稱去掉前後空白', () => {
    const input = toSourceInput({ ...READY, name: '  主機  ', acknowledged: true, dailyRequestsPerPerson: '20', monthlyTokens: '1000' });
    expect(input).toEqual({
      agentId: 'agent-1',
      engine: 'claude_code',
      name: '主機',
      enabled: true,
      enabledModels: ['opus', 'sonnet'],
      defaultModel: null,
      defaultEffort: null,
      limits: { dailyRequestsPerPerson: 20, monthlyRequests: null, monthlyTokens: 1000 },
      acknowledgeApiBilling: true,
    });
  });

  it('編輯時照來源現在的值還原成草稿再送回去', () => {
    const source = exampleHostSource();
    expect(toSourceInput(draftFromSource(source))).toMatchObject({ enabled: true, enabledModels: ['opus'], defaultModel: 'opus', defaultEffort: 'high', limits: source.limits, acknowledgeApiBilling: true });
  });
});

describe('可勾選的模型', () => {
  it('引擎回報的加上來源已開放、但這次沒回報的', () => {
    const agent = exampleHostAgent({ engines: [exampleEngine({ models: [{ id: 'sonnet', displayName: 'Sonnet', efforts: [], defaultEffort: null }] })] });
    expect(modelChoices(agent, 'claude_code', exampleHostSource()).map((model) => model.id)).toEqual(['sonnet', 'opus']);
  });
});
