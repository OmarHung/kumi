import { describe, expect, it } from 'vitest';
import { BUILT_IN_ROLE_PERMISSIONS, PERMISSION_ORDER } from './labels';

describe('內建角色的權限', () => {
  it('管理員是全部權限，但不含「只看自己的項目」（看得到全部就不需要，和後端 AllPermissions 一樣）', () => {
    // Arrange & Act
    const admin = BUILT_IN_ROLE_PERMISSIONS.admin;

    // Assert
    expect(admin).toContain('item.read');
    expect(admin).not.toContain('item.read_own');
    expect(admin).toEqual(PERMISSION_ORDER.filter((permission) => permission !== 'item.read_own'));
  });

  it('自訂角色能勾的清單仍有「只看自己的項目」', () => {
    // Arrange & Act & Assert
    expect(PERMISSION_ORDER).toContain('item.read_own');
  });

  it('執行者只有「只看自己的項目」，沒有「查看項目」', () => {
    // Arrange & Act
    const contributor = BUILT_IN_ROLE_PERMISSIONS.contributor;

    // Assert
    expect(contributor).toContain('item.read_own');
    expect(contributor).not.toContain('item.read');
  });
});
