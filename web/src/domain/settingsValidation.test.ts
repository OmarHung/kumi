import { describe, expect, it } from 'vitest';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import { buildChangeSet } from './settingsChangeSet';
import {
  addField,
  addState,
  draftFromBlueprint,
  removeState,
  renameField,
  renameOption,
  setFieldTypes,
  setMoveTarget,
  updateState,
} from './settingsDraft';
import { locateIssue } from './settingsIssues';
import { updateItemType } from './settingsTypes';
import {
  fieldKeyError,
  nameError,
  optionKeyError,
  stateKeyError,
  suggestKey,
  summaryError,
  SUMMARY_MAX_LENGTH,
  validateDraft,
} from './settingsValidation';

const smk = simpleTasksBlueprint;
const agy = agencyBlueprint;

describe('設定的 key', () => {
  it.each([
    ['', '請輸入 key。'],
    ['Review', 'key 要用小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元。'],
    ['r', 'key 要用小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元。'],
    ['1st', 'key 要用小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元。'],
    [`a${'b'.repeat(40)}`, 'key 要用小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元。'],
    ['doing', '這個流程已經有 key 是 doing 的狀態。'],
    ['review', undefined],
  ])('狀態 key「%s」', (key, expected) => {
    // Arrange
    const draft = draftFromBlueprint(smk);

    // Act & Assert
    expect(stateKeyError(smk, draft, 'task_flow', key)).toBe(expected);
  });

  it('要刪除的狀態的 key 也不能再用', () => {
    // Arrange
    const draft = removeState(smk, draftFromBlueprint(smk), 'task_flow', 'doing');

    // Act & Assert
    expect(stateKeyError(smk, draft, 'task_flow', 'doing')).toBe('這個流程已經有 key 是 doing 的狀態。');
  });

  it.each([
    ['title', '「title」是內建欄位的名字，自訂欄位不能用。'],
    ['item_type', '「item_type」是內建欄位的名字，自訂欄位不能用。'],
    ['notes', '已經有 key 是 notes 的欄位。'],
    ['site_url', undefined],
  ])('欄位 key「%s」', (key, expected) => {
    // Arrange & Act & Assert
    expect(fieldKeyError(smk, draftFromBlueprint(smk), key)).toBe(expected);
  });

  it('選項 key 在欄位裡不能重複', () => {
    // Arrange
    const draft = draftFromBlueprint(smk);

    // Act & Assert
    expect(optionKeyError(draft, 'category', 'bug')).toBe('這個欄位已經有 key 是 bug 的選項。');
    expect(optionKeyError(draft, 'category', 'urgent')).toBeUndefined();
  });

  it('建議的 key：英文名稱轉成小寫加底線，其他的用前綴加編號，避開已經用過的', () => {
    // Arrange
    const taken = new Set(['state_1', 'qa_review']);

    // Act & Assert
    expect(suggestKey('QA Review', 'state', taken)).toBe('qa_review_2');
    expect(suggestKey('Code review!', 'state', taken)).toBe('code_review');
    expect(suggestKey('審查中', 'state', taken)).toBe('state_2');
    expect(suggestKey('', 'field', new Set())).toBe('field_1');
  });
});

describe('設定的名稱', () => {
  it.each([
    ['', '請輸入名稱。'],
    ['   ', '請輸入名稱。'],
    ['a'.repeat(101), '名稱最多 100 個字。'],
    [`審查${String.fromCharCode(7)}中`, '名稱不能有控制字元。'],
    ['審查中', undefined],
  ])('「%s」', (name, expected) => {
    // Arrange & Act & Assert
    expect(nameError(name)).toBe(expected);
  });

  it('摘要最多 500 個字（和伺服器的上限一樣），前後的空白不算', () => {
    // Arrange & Act & Assert
    expect(SUMMARY_MAX_LENGTH).toBe(500);
    expect(summaryError('字'.repeat(500))).toBeUndefined();
    expect(summaryError(` ${'字'.repeat(500)} `)).toBeUndefined();
    expect(summaryError('字'.repeat(501))).toBe('摘要最多 500 個字。');
  });
});

describe('整份草稿的檢查', () => {
  it('沒有問題時是空的', () => {
    // Arrange & Act & Assert
    expect(validateDraft(smk, draftFromBlueprint(smk))).toEqual([]);
  });

  it('列出每一個問題與它的位置：搬移目標、完成類的狀態、名稱、選項', () => {
    // Arrange
    const draft = renameOption(
      renameField(
        updateState(removeState(smk, draftFromBlueprint(smk), 'task_flow', 'doing'), 'task_flow', 'done', { category: 'in_progress', name: ' ' }),
        'notes',
        '',
      ),
      'category',
      'bug',
      '',
    );
    const withEmptySelect = addField(draft, { key: 'severity', name: '嚴重度', type: 'single_select', options: [], appliesTo: [] });

    // Act
    const issues = validateDraft(smk, withEmptySelect);

    // Assert
    expect(issues).toEqual([
      { target: { kind: 'workflow', workflow: 'task_flow' }, message: '流程「任務流程」至少要有一個類別是「完成」的狀態。' },
      { target: { kind: 'state', workflow: 'task_flow', state: 'done' }, message: '請輸入名稱。' },
      { target: { kind: 'state', workflow: 'task_flow', state: 'doing' }, message: '請選擇停在「進行中」的項目要搬到哪個狀態。' },
      { target: { kind: 'option', field: 'category', option: 'bug' }, message: '請輸入名稱。' },
      { target: { kind: 'field', field: 'notes' }, message: '請輸入名稱。' },
      { target: { kind: 'field', field: 'severity' }, message: '選項型的欄位至少要有一個選項。' },
    ]);
  });

  it('流程裡的狀態全部刪掉時說至少要留一個', () => {
    // Arrange
    const base = draftFromBlueprint(smk);
    const emptied = ['todo', 'doing', 'done'].reduce((draft, state) => removeState(smk, draft, 'task_flow', state), base);

    // Act
    const [first] = validateDraft(smk, emptied);

    // Assert
    expect(first).toEqual({ target: { kind: 'workflow', workflow: 'task_flow' }, message: '流程「任務流程」至少要留一個狀態。' });
  });
});

describe('伺服器的問題對到設定頁的哪一列', () => {
  it('路徑指向送出的文件：狀態、流程、欄位、類型的欄位、搬移、封存', () => {
    // Arrange
    const draft = setMoveTarget(
      removeState(agy, addState(draftFromBlueprint(agy), 'delivery', { key: 'review', name: '審查', category: 'in_progress' }, 1), 'delivery', 'quoting'),
      'delivery',
      'quoting',
      'confirming',
    );
    const request = buildChangeSet(agy, 1, draft, '');

    // Act & Assert
    expect(locateIssue('/blueprint/workflows/0/states/1', request)).toEqual({ kind: 'state', workflow: 'delivery', state: 'review' });
    expect(locateIssue('/blueprint/workflows/0/states/1/key', request)).toEqual({ kind: 'state', workflow: 'delivery', state: 'review' });
    expect(locateIssue('/blueprint/workflows/1/states', request)).toEqual({ kind: 'workflow', workflow: 'task_flow' });
    expect(locateIssue('/blueprint/fields/3/options', request)).toEqual({ kind: 'field', field: 'impact' });
    expect(locateIssue('/blueprint/fields/3/options/1/key', request)).toEqual({ kind: 'option', field: 'impact', option: 'medium' });
    expect(locateIssue('/blueprint/itemTypes/1/fields/0', request)).toEqual({ kind: 'field', field: 'change_reason' });
    expect(locateIssue('/migrations/moveStates/0/to', request)).toEqual({ kind: 'state', workflow: 'delivery', state: 'quoting' });
    expect(locateIssue('/migrations/dropFields/0', { ...request, migrations: { dropFields: ['notes'] } })).toEqual({
      kind: 'field',
      field: 'notes',
    });
  });

  it('轉換的問題（包含要先填的欄位、允許的角色）標在那條轉換上', () => {
    // Arrange
    const request = buildChangeSet(agy, 1, setFieldTypes(draftFromBlueprint(agy), 'client_signoff_date', ['requirement']), '');
    const transition = { kind: 'transition', workflow: 'delivery', from: 'client_qa', to: 'closed' };

    // Act & Assert
    expect(locateIssue('/blueprint/workflows/0/transitions/7/requireFields/0', request)).toEqual(transition);
    expect(locateIssue('/blueprint/workflows/0/transitions/7/allowedRoles/0', request)).toEqual(transition);
    expect(locateIssue('/blueprint/workflows/0/transitions/7', request)).toEqual(transition);
    expect(locateIssue('/blueprint/workflows/0/transitions/99', request)).toEqual({ kind: 'workflow', workflow: 'delivery' });
  });

  it('類型與角色的問題標在那個類型、角色；類型的 fields 仍標在欄位', () => {
    // Arrange
    const request = buildChangeSet(agy, 1, draftFromBlueprint(agy), '');

    // Act & Assert
    expect(locateIssue('/blueprint/itemTypes/2/parents/0', request)).toEqual({ kind: 'itemType', itemType: 'task' });
    expect(locateIssue('/blueprint/itemTypes/1/workflow', request)).toEqual({ kind: 'itemType', itemType: 'change_request' });
    expect(locateIssue('/blueprint/itemTypes/1/fields/1', request)).toEqual({ kind: 'field', field: 'impact' });
    expect(locateIssue('/blueprint/itemTypes/9', request)).toBeNull();
    expect(locateIssue('/blueprint/roles/1/key', request)).toEqual({ kind: 'role', role: 'client' });
    expect(locateIssue('/blueprint/roles/5', request)).toBeNull();
  });

  it('類型換流程的搬移標在那條搬移（新流程, 舊狀態），共用它的類型都看得到；刪除狀態的搬移標在狀態', () => {
    // Arrange
    const switched = updateItemType(updateItemType(draftFromBlueprint(agy), 'change_request', { workflow: 'task_flow' }), 'requirement', { workflow: 'task_flow' });
    const draft = setMoveTarget(removeState(agy, switched, 'task_flow', 'doing'), 'task_flow', 'doing', 'todo');
    const request = buildChangeSet(agy, 1, draft, '');
    const context = { base: agy, draft };

    // Act & Assert
    expect(request.migrations?.moveStates?.[0]).toEqual({ workflow: 'task_flow', from: 'doing', to: 'todo' });
    expect(locateIssue('/migrations/moveStates/0/to', request, context)).toEqual({ kind: 'state', workflow: 'task_flow', state: 'doing' });
    expect(request.migrations?.moveStates?.[1]?.from).toBe('confirming');
    expect(locateIssue('/migrations/moveStates/1/to', request, context)).toEqual({ kind: 'typeMove', workflow: 'task_flow', from: 'confirming' });
    expect(locateIssue('/migrations/moveStates/1', request)).toEqual({ kind: 'state', workflow: 'task_flow', state: 'confirming' });
  });

  it('對不到的路徑（自動化、檢視、根）回 null', () => {
    // Arrange
    const request = buildChangeSet(agy, 1, draftFromBlueprint(agy), '');

    // Act & Assert
    expect(locateIssue('/blueprint/automations/0/actions/0/to', request)).toBeNull();
    expect(locateIssue('/blueprint/workflows/9/states/0', request)).toBeNull();
    expect(locateIssue('/baseVersion', request)).toBeNull();
    expect(locateIssue(null, request)).toBeNull();
  });
});
