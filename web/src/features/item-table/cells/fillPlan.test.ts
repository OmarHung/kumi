import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../api/http';
import { agyItem } from '../../../test/fixtures';
import { clearedFields, fieldErrorsOf, mergePatches, missingFieldPaths, planFill, type FillFieldRef } from './fillPlan';
import { invalidCommand, patchCommand, UNCHANGED } from './useItemCommand';

const signoff: FillFieldRef = { key: 'client_signoff_date', label: '客戶驗收日', errorPaths: ['client_signoff_date'] };
const assignee: FillFieldRef = { key: 'assignee', label: '負責人', errorPaths: ['assignee', 'assigneeId'] };

function fieldsRequired(paths: readonly (string | null)[]): ApiError {
  return new ApiError({
    status: 422,
    code: 'item.transition_fields_required',
    message: '要先填欄位才能進入「結案」。',
    details: paths.map((path) => ({ code: 'item.transition_fields_required', path, message: `要先填「${String(path)}」。` })),
  });
}

describe('missingFieldPaths', () => {
  it('要先填欄位的錯誤：列出 details 的 path，去掉重複與沒有 path 的', () => {
    // Arrange
    const error = fieldsRequired(['client_signoff_date', null, 'assignee', 'client_signoff_date']);

    // Act
    const paths = missingFieldPaths(error);

    // Assert
    expect(paths).toEqual(['client_signoff_date', 'assignee']);
  });

  it('別的錯誤（角色、流程沒有這條路、不是 API 的錯誤）不是補填的情形', () => {
    // Arrange
    const role = new ApiError({ status: 403, code: 'item.transition_role_required', message: '只有專案經理可以做。' });

    // Act
    const results = [missingFieldPaths(role), missingFieldPaths(new Error('網路斷了'))];

    // Assert
    expect(results).toEqual([null, null]);
  });
});

describe('planFill', () => {
  it('合併每個欄位的修改成一個 PATCH：核心欄位在外層、自訂欄位放在 fields', () => {
    // Arrange
    const commands = new Map([
      [signoff.key, patchCommand({ fields: { client_signoff_date: '2026-11-20' } })],
      [assignee.key, patchCommand({ assigneeId: 'p-1' })],
    ]);

    // Act
    const plan = planFill([signoff, assignee], commands, new Set());

    // Assert
    expect(plan).toEqual({ kind: 'ready', patch: { assigneeId: 'p-1', fields: { client_signoff_date: '2026-11-20' } } });
  });

  it('還沒填（沒有修改、這次也還沒存過）的欄位說要填，不送出', () => {
    // Arrange
    const commands = new Map([[assignee.key, patchCommand({ assigneeId: 'p-1' })]]);

    // Act
    const plan = planFill([signoff, assignee], commands, new Set());

    // Assert
    expect(plan).toEqual({ kind: 'invalid', errors: { client_signoff_date: '請填「客戶驗收日」。' } });
  });

  it('前端看得出來的錯（數字打錯）照編輯器的說明', () => {
    // Arrange
    const commands = new Map([[signoff.key, invalidCommand('請輸入數字。')]]);

    // Act
    const plan = planFill([signoff], commands, new Set());

    // Assert
    expect(plan).toEqual({ kind: 'invalid', errors: { client_signoff_date: '請輸入數字。' } });
  });

  it('欄位都存好了、沒有再改：不必再 PATCH，只重送改狀態', () => {
    // Arrange
    const commands = new Map([[signoff.key, UNCHANGED]]);

    // Act
    const plan = planFill([signoff], commands, new Set([signoff.key]));

    // Assert
    expect(plan).toEqual({ kind: 'ready', patch: null });
  });
});

describe('mergePatches', () => {
  it('沒有修改時是 null；只有核心欄位時不帶 fields', () => {
    // Act
    const results = [mergePatches([]), mergePatches([{ priority: 'high' }, { dueDate: '2026-12-01' }])];

    // Assert
    expect(results).toEqual([null, { priority: 'high', dueDate: '2026-12-01' }]);
  });
});

describe('fieldErrorsOf', () => {
  it('對到欄位的細項放在那個欄位（任何一個 errorPaths 都算），對不到的放在上方', () => {
    // Arrange
    const error = new ApiError({
      status: 422,
      code: 'validation.failed',
      message: '有欄位不對。',
      details: [
        { code: 'item.assignee_not_member', path: 'assigneeId', message: '這個人不是專案的成員。' },
        { code: 'item.version_conflict', path: null, message: '項目剛被別人改過。' },
      ],
    });

    // Act
    const mapped = fieldErrorsOf(error, [signoff, assignee]);

    // Assert
    expect(mapped).toEqual({ fieldErrors: { assignee: '這個人不是專案的成員。' }, formError: '項目剛被別人改過。' });
  });

  it('不是 API 的錯誤：整個放在上方', () => {
    // Act
    const mapped = fieldErrorsOf(new Error('連不上伺服器。'), [signoff]);

    // Assert
    expect(mapped.fieldErrors).toEqual({});
    expect(mapped.formError).not.toBeNull();
  });
});

describe('clearedFields', () => {
  it('後端說還空著的欄位當成空的：核心欄位清成沒有值（優先度是「無」），自訂欄位拿掉，其他照舊', () => {
    // Arrange
    const item = agyItem({
      priority: 'high',
      dueDate: '2026-12-01',
      estimate: 3,
      fields: { client_signoff_date: '2026-11-20', quote_amount: 12000 },
    });

    // Act
    const cleared = clearedFields(item, ['priority', 'due_date', 'client_signoff_date']);

    // Assert
    expect(cleared).toEqual({ ...item, priority: 'none', dueDate: null, fields: { quote_amount: 12000 } });
  });
});
