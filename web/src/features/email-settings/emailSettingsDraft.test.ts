import { describe, expect, it } from 'vitest';
import type { EmailSettings } from '../../api/emailSettings';
import { hasDraftChanges, needsNewPassword, toDraft, toInput, visibleFields, withSecurity } from './emailSettingsDraft';

const CONTEXT = { organizationName: '範例組織', origin: 'https://pm.example.com' };

const EMPTY: EmailSettings = {
  enabled: false,
  provider: 'gmail',
  host: null,
  port: null,
  security: null,
  username: null,
  hasPassword: false,
  fromAddress: null,
  fromName: null,
  siteUrl: null,
  status: { lastSentAt: null, lastFailedAt: null, lastError: null },
  updatedAt: null,
  updatedBy: null,
};

describe('寄信設定的草稿', () => {
  it('沒存過：寄件人名稱預先填組織名稱、網站網址預先填目前的網址、連接埠是 587', () => {
    // Arrange / Act
    const draft = toDraft(EMPTY, CONTEXT);

    // Assert
    expect(draft).toMatchObject({ fromName: '範例組織', siteUrl: 'https://pm.example.com', port: '587', security: 'start_tls', password: '' });
  });

  it('存過的值照用，不被預先填的蓋掉', () => {
    // Arrange
    const saved = { ...EMPTY, fromName: 'Example 公司', siteUrl: 'https://pm.example.org', port: 465, security: 'tls' as const };

    // Act
    const draft = toDraft(saved, CONTEXT);

    // Assert
    expect(draft).toMatchObject({ fromName: 'Example 公司', siteUrl: 'https://pm.example.org', port: '465', security: 'tls' });
  });

  it('Gmail：主機、連接埠、加密方式、寄件人地址送 null；密碼留白是 null（沿用）', () => {
    // Arrange
    const draft = { ...toDraft(EMPTY, CONTEXT), enabled: true, username: ' team@example.com ' };

    // Act
    const input = toInput(draft);

    // Assert
    expect(input).toEqual({
      enabled: true,
      provider: 'gmail',
      host: null,
      port: null,
      security: null,
      username: 'team@example.com',
      password: null,
      fromAddress: null,
      fromName: '範例組織',
      siteUrl: 'https://pm.example.com',
    });
  });

  it('SMTP：連接埠轉成數字，空欄位送 null', () => {
    // Arrange
    const draft = { ...toDraft(EMPTY, CONTEXT), provider: 'smtp' as const, host: 'smtp.example.com', password: 'secret' };

    // Act
    const input = toInput(draft);

    // Assert
    expect(input).toMatchObject({ provider: 'smtp', host: 'smtp.example.com', port: 587, security: 'start_tls', username: null, password: 'secret', fromAddress: null });
  });

  it('SMTP 不加密：帳號送 null、密碼送空字串（清掉已經存的）', () => {
    // Arrange
    const draft = { ...toDraft(EMPTY, CONTEXT), provider: 'smtp' as const, security: 'none' as const, username: 'someone', password: 'secret' };

    // Act
    const input = toInput(draft);

    // Assert
    expect(input).toMatchObject({ security: 'none', username: null, password: '' });
  });

  it('換加密方式：連接埠還是慣用值就跟著換，自己選過的不動', () => {
    // Arrange
    const base = toDraft(EMPTY, CONTEXT);

    // Act / Assert
    expect(withSecurity(base, 'tls').port).toBe('465');
    expect(withSecurity(withSecurity(base, 'tls'), 'none').port).toBe('25');
    expect(withSecurity({ ...base, port: '2525' }, 'tls').port).toBe('2525');
  });

  it('畫面上的欄位：Gmail 只有四個，SMTP 有八個', () => {
    expect(visibleFields('gmail')).toEqual(['username', 'password', 'fromName', 'siteUrl']);
    expect(visibleFields('smtp')).toHaveLength(8);
  });

  it('有沒有改過：任何一個欄位不一樣就算', () => {
    // Arrange
    const base = toDraft(EMPTY, CONTEXT);

    // Act / Assert
    expect(hasDraftChanges(base, toDraft(EMPTY, CONTEXT))).toBe(false);
    expect(hasDraftChanges({ ...base, password: 'x' }, base)).toBe(true);
  });

  it('密碼只打空白：當作沒填（null）', () => {
    // Arrange
    const gmail = { ...toDraft(EMPTY, CONTEXT), password: '   ' };

    // Act / Assert
    expect(toInput(gmail).password).toBeNull();
    expect(toInput({ ...gmail, provider: 'smtp' }).password).toBeNull();
  });

  it('要不要重新輸入密碼：已經存了密碼、而且寄信服務、主機、連接埠、帳號有一項不一樣', () => {
    // Arrange
    const saved: EmailSettings = { ...EMPTY, provider: 'smtp', host: 'smtp.example.com', port: 587, security: 'start_tls', username: 'mailer', hasPassword: true };
    const same = toDraft(saved, CONTEXT);

    // Act / Assert
    expect(needsNewPassword(same, saved)).toBe(false);
    expect(needsNewPassword({ ...same, fromName: '別的名稱' }, saved)).toBe(false);
    expect(needsNewPassword({ ...same, host: 'other.example.com' }, saved)).toBe(true);
    expect(needsNewPassword({ ...same, port: '465' }, saved)).toBe(true);
    expect(needsNewPassword({ ...same, username: 'someone' }, saved)).toBe(true);
    expect(needsNewPassword({ ...same, provider: 'gmail' }, saved)).toBe(true);
    expect(needsNewPassword(same, { ...saved, hasPassword: false })).toBe(false);
  });
});
