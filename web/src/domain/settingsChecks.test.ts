import { describe, expect, it } from 'vitest';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import { addState, draftFromBlueprint, removeState, setFieldTypes, setMoveTarget } from './settingsDraft';
import { itemTypeKeyError, roleKeyError } from './settingsChecks';
import { addRole, removeRole, updateRole } from './settingsRoles';
import { addStateTransitions, setRestricted, setTransition, updateTransition } from './settingsTransitions';
import { addItemType, updateItemType } from './settingsTypes';
import { validateDraft } from './settingsValidation';

const agy = agencyBlueprint;
const smk = simpleTasksBlueprint;

describe('送出前的檢查：轉換', () => {
  it('有限制的流程：走不到的狀態與沒有出路的狀態標在那個狀態上', () => {
    // Arrange
    const state = { key: 'review', name: '審查', category: 'in_progress' } as const;

    // Act
    const draft = addState(draftFromBlueprint(agy), 'delivery', state, 6);

    // Assert
    expect(validateDraft(agy, draft)).toEqual([
      { target: { kind: 'state', workflow: 'delivery', state: 'review' }, message: '從起始狀態「需求確認」走不到「審查」：請在「轉換」勾一條轉進來的路。' },
      { target: { kind: 'state', workflow: 'delivery', state: 'review' }, message: '「審查」不是完成或已取消類，卻沒有可以轉出去的轉換。' },
    ]);
    expect(validateDraft(agy, addStateTransitions(draft, 'delivery', 'review', ['client_qa'], ['closed']))).toEqual([]);
  });

  it('刪除中間的狀態讓後面的走不到時先擋下；完成類的狀態沒有出路沒關係', () => {
    // Arrange & Act
    const draft = setMoveTarget(removeState(agy, draftFromBlueprint(agy), 'delivery', 'internal_qa'), 'delivery', 'internal_qa', 'developing');

    // Assert
    expect(validateDraft(agy, draft).map((issue) => [issue.target, issue.message])).toEqual([
      [{ kind: 'state', workflow: 'delivery', state: 'developing' }, '「開發」不是完成或已取消類，卻沒有可以轉出去的轉換。'],
      [{ kind: 'state', workflow: 'delivery', state: 'client_qa' }, '從起始狀態「需求確認」走不到「客戶驗收」：請在「轉換」勾一條轉進來的路。'],
      [{ kind: 'state', workflow: 'delivery', state: 'closed' }, '從起始狀態「需求確認」走不到「結案」：請在「轉換」勾一條轉進來的路。'],
    ]);
    expect(validateDraft(agy, setTransition(agy, draft, 'delivery', 'developing', 'client_qa', true))).toEqual([]);
  });

  it('不限制的流程不檢查走不走得到；一條路都沒有時每個後面的狀態都走不到', () => {
    // Arrange
    const restricted = setRestricted(smk, draftFromBlueprint(smk), 'task_flow', true);
    const none = ['todo->doing', 'todo->done', 'doing->todo', 'doing->done', 'done->todo', 'done->doing'].reduce((draft, pair) => {
      const [from = '', to = ''] = pair.split('->');
      return setTransition(smk, draft, 'task_flow', from, to, false);
    }, restricted);

    // Act & Assert
    expect(validateDraft(smk, draftFromBlueprint(smk))).toEqual([]);
    expect(validateDraft(smk, restricted)).toEqual([]);
    expect(validateDraft(smk, none).map((issue) => issue.message)).toEqual([
      '「待辦」不是完成或已取消類，卻沒有可以轉出去的轉換。',
      '從起始狀態「待辦」走不到「進行中」：請在「轉換」勾一條轉進來的路。',
      '「進行中」不是完成或已取消類，卻沒有可以轉出去的轉換。',
      '從起始狀態「待辦」走不到「完成」：請在「轉換」勾一條轉進來的路。',
    ]);
  });

  it('要先填的自訂欄位要套用在每個用這個流程的類型；標在那條轉換上，說出缺的類型', () => {
    // Arrange & Act
    const draft = setFieldTypes(draftFromBlueprint(agy), 'client_signoff_date', ['requirement']);

    // Assert
    expect(validateDraft(agy, draft)).toEqual([
      {
        target: { kind: 'transition', workflow: 'delivery', from: 'client_qa', to: 'closed' },
        message: '「需求變更單」沒有套用「客戶驗收日」：用「交付流程」的類型都要有這個欄位，這條轉換才走得過去。',
      },
    ]);
  });

  it('要先填上層項目時，每個用這個流程的類型都要能有上層；說明與核心欄位都可以', () => {
    // Arrange
    const draft = updateTransition(draftFromBlueprint(agy), 'delivery', 'confirming', 'quoting', { requireFields: ['parent', 'description'] });

    // Act
    const issues = validateDraft(agy, draft);

    // Assert
    expect(issues.map((issue) => issue.message)).toEqual([
      '「需求」不能掛在任何類型底下：這條轉換要求先填「上層項目」，「需求」永遠走不過去。',
      '「需求變更單」不能掛在任何類型底下：這條轉換要求先填「上層項目」，「需求變更單」永遠走不過去。',
    ]);
    expect(validateDraft(agy, updateItemType(updateItemType(draft, 'requirement', { parents: ['task'] }), 'change_request', { parents: ['task'] }))).toEqual([
      { target: { kind: 'itemType', itemType: 'change_request' }, message: '「需求變更單」把「任務」當成上層，上層關係因此繞成一圈。' },
      { target: { kind: 'itemType', itemType: 'task' }, message: '「任務」把「需求」當成上層，上層關係因此繞成一圈。' },
    ]);
  });

  it('類型換流程造成的缺欄位也算；被刪除的欄位不再檢查', () => {
    // Arrange
    const switched = updateItemType(draftFromBlueprint(agy), 'task', { workflow: 'delivery' });

    // Act
    const issues = validateDraft(agy, switched);

    // Assert
    expect(issues).toEqual([
      {
        target: { kind: 'transition', workflow: 'delivery', from: 'client_qa', to: 'closed' },
        message: '「任務」沒有套用「客戶驗收日」：用「交付流程」的類型都要有這個欄位，這條轉換才走得過去。',
      },
    ]);
  });
});

describe('送出前的檢查：轉換允許的角色', () => {
  const closing = { kind: 'transition', workflow: 'delivery', from: 'client_qa', to: 'closed' } as const;

  it('轉換只允許沒有「改變狀態」權限的角色：沒有人走得了，標在那條轉換上', () => {
    // Arrange & Act
    const draft = updateTransition(draftFromBlueprint(agy), 'delivery', 'client_qa', 'closed', { allowedRoles: ['client'] });
    const two = updateTransition(draft, 'delivery', 'client_qa', 'closed', { allowedRoles: ['client', 'commenter'] });

    // Assert
    expect(validateDraft(agy, draft)).toEqual([
      { target: closing, message: '只允許「客戶」，但這個角色沒有「改變狀態」的權限，沒有人走得了這條轉換。' },
    ]);
    expect(validateDraft(agy, two).map((issue) => issue.message)).toEqual([
      '只允許「客戶」、「留言者」，但這些角色都沒有「改變狀態」的權限，沒有人走得了這條轉換。',
    ]);
  });

  it('自訂角色取消「改變狀態」而它正被用在轉換上，也要擋；有任何一個角色能改變狀態就不擋', () => {
    // Arrange
    const withoutTransition = updateRole(draftFromBlueprint(agy), 'project_manager', { permissions: ['item.read', 'item.update', 'blueprint.read'] });

    // Act
    const withEditor = updateTransition(withoutTransition, 'delivery', 'internal_qa', 'client_qa', { allowedRoles: ['project_manager', 'editor'] });

    // Assert
    expect(validateDraft(agy, withoutTransition).map((issue) => issue.target)).toEqual([
      { kind: 'transition', workflow: 'delivery', from: 'internal_qa', to: 'client_qa' },
      closing,
    ]);
    expect(validateDraft(agy, withEditor).map((issue) => issue.target)).toEqual([closing]);
  });

  it('被刪除的角色送出時會拿掉，不算在允許的角色裡（拿掉之後不限角色就不擋）', () => {
    // Arrange
    const draft = updateTransition(draftFromBlueprint(agy), 'delivery', 'client_qa', 'closed', { allowedRoles: ['client'] });

    // Act
    const removed = removeRole(agy, draft, 'client');

    // Assert
    expect(validateDraft(agy, removed)).toEqual([]);
  });
});

describe('送出前的檢查：類型與角色', () => {
  it('類型與角色的名稱必填', () => {
    // Arrange
    const draft = updateRole(updateItemType(draftFromBlueprint(agy), 'task', { name: ' ' }), 'client', { name: '' });

    // Act & Assert
    expect(validateDraft(agy, draft)).toEqual([
      { target: { kind: 'itemType', itemType: 'task' }, message: '請輸入名稱。' },
      { target: { kind: 'role', role: 'client' }, message: '請輸入名稱。' },
    ]);
  });

  it('新類型的 key：格式、和目前藍圖與草稿裡的類型都不重複', () => {
    // Arrange
    const draft = addItemType(draftFromBlueprint(agy), { key: 'bug', name: '錯誤', workflow: 'task_flow', parents: [] }, []);

    // Act & Assert
    expect(itemTypeKeyError(agy, draft, 'Bug')).toMatch(/小寫英文字母開頭/);
    expect(itemTypeKeyError(agy, draft, 'task')).toBe('已經有 key 是 task 的類型。');
    expect(itemTypeKeyError(agy, draft, 'bug')).toBe('已經有 key 是 bug 的類型。');
    expect(itemTypeKeyError(agy, draft, 'story')).toBeUndefined();
  });

  it('新角色的 key：格式、不能是內建角色、和目前藍圖與草稿裡的（包含要刪除的）都不重複', () => {
    // Arrange
    const draft = addRole(removeRole(agy, draftFromBlueprint(agy), 'client'), { key: 'qa_lead', name: 'QA', permissions: [] });

    // Act & Assert
    expect(roleKeyError(agy, draft, '')).toBe('請輸入 key。');
    expect(roleKeyError(agy, draft, 'viewer')).toBe('「viewer」是內建角色的 key，自訂角色不能用。');
    expect(roleKeyError(agy, draft, 'client')).toBe('已經有 key 是 client 的角色。');
    expect(roleKeyError(agy, draft, 'qa_lead')).toBe('已經有 key 是 qa_lead 的角色。');
    expect(roleKeyError(agy, draft, 'designer')).toBeUndefined();
  });

  it('類型換流程的搬移沒有目標時（新流程的狀態都刪掉了）要選', () => {
    // Arrange
    const empty = ['todo', 'doing', 'done'].reduce((draft, state) => removeState(agy, draft, 'task_flow', state), draftFromBlueprint(agy));

    // Act
    const draft = updateItemType(empty, 'requirement', { workflow: 'task_flow' });

    // Assert
    expect(validateDraft(agy, draft).filter((issue) => issue.target?.kind === 'typeMove')[0]).toEqual({
      target: { kind: 'typeMove', workflow: 'task_flow', from: 'confirming' },
      message: '請選擇「需求」停在「需求確認」的項目要搬到哪個狀態。',
    });
  });
});
