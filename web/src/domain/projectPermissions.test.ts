import { describe, expect, it } from 'vitest';
import { canReadItems, readsAllItems } from './projectPermissions';

describe('看得到專案的項目', () => {
  it('item.read 或 item.read_own 都算看得到；只有 item.read 算看得到全部', () => {
    // Arrange
    const all = ['item.read', 'blueprint.read'] as const;
    const own = ['item.read_own', 'item.update'] as const;
    const none = ['blueprint.read'] as const;

    // Act & Assert
    expect([canReadItems(all), canReadItems(own), canReadItems(none)]).toEqual([true, true, false]);
    expect([readsAllItems(all), readsAllItems(own)]).toEqual([true, false]);
  });
});
