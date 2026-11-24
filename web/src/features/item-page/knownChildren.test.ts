import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/http';
import { childKeysOf } from './knownChildren';

describe('伺服器擋下時列出的下層', () => {
  it('從 details 的 path 取出下層的編號；開頭有沒有斜線都認得，其他的略過', () => {
    // Arrange
    const error = new ApiError({
      status: 409,
      code: 'item.has_children',
      message: '還有下層項目。',
      details: [
        { code: 'item.has_children', path: 'children/AGY-8', message: '' },
        { code: 'item.has_children', path: '/children/AGY-9', message: '' },
        { code: 'item.has_children', path: 'parent', message: '' },
        { code: 'item.has_children', path: null, message: '' },
      ],
    });

    // Act & Assert
    expect(childKeysOf(error)).toEqual(['AGY-8', 'AGY-9']);
    expect(childKeysOf(new Error('不是 API 的錯誤'))).toEqual([]);
  });
});
