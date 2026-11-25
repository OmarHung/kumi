import { describe, expect, it } from 'vitest';
import { ApiError } from '../api/http';
import { firstInvalidField, hasErrors, mapApiErrorToFields, stripPathPrefix, type FieldMapping } from './formErrors';

type ItemField = 'title' | 'due_date';

const mapping: FieldMapping<ItemField> = {
  fields: ['title', 'due_date'],
  codes: { 'item.title_required': 'title' },
};

function invalid(details: ApiError['details'], code = 'item.invalid_value'): ApiError {
  return new ApiError({ status: 422, code, message: '欄位有問題。', details });
}

describe('mapApiErrorToFields', () => {
  it('每個欄位只留第一則訊息，對不到的放在表單上方', () => {
    // Arrange
    const error = invalid([
      { code: 'item.invalid_value', path: 'due_date', message: '日期不正確。' },
      { code: 'item.invalid_value', path: 'due_date', message: '第二則。' },
      { code: 'item.unknown_field', path: 'color', message: '沒有這個欄位。' },
    ]);

    // Act
    const mapped = mapApiErrorToFields(error, mapping);

    // Assert
    expect(mapped.fieldErrors).toEqual({ due_date: '日期不正確。' });
    expect(mapped.formError).toBe('沒有這個欄位。');
  });

  it('沒有細項時用代碼對到欄位', () => {
    // Arrange
    const error = invalid([], 'item.title_required');

    // Act
    const mapped = mapApiErrorToFields(error, mapping);

    // Assert
    expect(mapped).toEqual({ fieldErrors: { title: '欄位有問題。' }, formError: null });
  });

  it('代碼對到的欄位不在這個表單上時，放在表單上方，不會不見', () => {
    // Arrange
    const error = invalid([{ code: 'item.parent_not_allowed', path: 'parent', message: '不能放在這底下。' }]);
    const titleOnly: FieldMapping<ItemField | 'parent'> = {
      fields: ['title'],
      codes: { 'item.parent_not_allowed': 'parent' },
    };

    // Act
    const mapped = mapApiErrorToFields(error, titleOnly);

    // Assert
    expect(mapped.fieldErrors).toEqual({});
    expect(mapped.formError).toBe('不能放在這底下。');
  });

  it('不是 API 的錯誤只給表單層級的訊息', () => {
    // Arrange / Act
    const mapped = mapApiErrorToFields(new Error('bug'), mapping);

    // Assert
    expect(mapped.fieldErrors).toEqual({});
    expect(mapped.formError).toBe('發生未預期的錯誤，請重新整理頁面再試。');
  });
});

describe('firstInvalidField 與 hasErrors', () => {
  it('依欄位順序找第一個有問題的', () => {
    // Arrange
    const errors = { due_date: '日期不正確。', title: undefined };

    // Act / Assert
    expect(firstInvalidField(mapping.fields, errors)).toBe('due_date');
    expect(hasErrors(errors)).toBe(true);
    expect(hasErrors({ title: undefined })).toBe(false);
  });
});

describe('stripPathPrefix', () => {
  it('細項路徑有前綴就拿掉，其他欄位照舊，429 的等待秒數也帶著', () => {
    // Arrange
    const error = new ApiError({
      status: 429,
      code: 'rate_limited',
      message: '太快了。',
      details: [{ code: 'x', path: 'limits.monthlyTokens', message: 'a' }, { code: 'y', path: 'enabled', message: 'b' }],
      data: { k: 1 },
      retryAfterSeconds: 7,
    });

    // Act
    const result = stripPathPrefix(error, 'limits.');

    // Assert
    expect(result).toBeInstanceOf(ApiError);
    const stripped = result as ApiError;
    expect(stripped.details.map((detail) => detail.path)).toEqual(['monthlyTokens', 'enabled']);
    expect(stripped.retryAfterSeconds).toBe(7);
    expect(stripped.status).toBe(429);
    expect(stripped.data).toEqual({ k: 1 });
  });

  it('沒有前綴的路徑原樣保留', () => {
    // Arrange
    const error = invalid([{ code: 'x', path: 'monthlyTokens', message: 'a' }]);

    // Act
    const result = stripPathPrefix(error, 'limits.') as ApiError;

    // Assert
    expect(result.details.map((detail) => detail.path)).toEqual(['monthlyTokens']);
  });

  it('不是 ApiError 就原樣交回', () => {
    // Arrange
    const error = new Error('bug');

    // Act / Assert
    expect(stripPathPrefix(error, 'limits.')).toBe(error);
  });
});
