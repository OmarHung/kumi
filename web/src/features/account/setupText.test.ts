import { describe, expect, it } from 'vitest';
import { macArmPackage, windowsPackage } from '../../test/cli';
import { installCommand, pathCommand } from './cliText';
import { oneLineCommand, setupCommand } from './setupText';

describe('設定精靈的指令', () => {
  it('macOS：安裝、PATH 包在大括號裡、用完整路徑啟動精靈，網址用雙引號', () => {
    // Arrange
    const origin = 'https://kumi.example.com';

    // Act
    const command = oneLineCommand(macArmPackage, origin);

    // Assert
    expect(command).toBe(`${installCommand(macArmPackage)} && { ${pathCommand('macos-arm64')} ; } && ~/.local/bin/kumi setup --server "https://kumi.example.com"`);
  });

  it('Windows：安裝、PATH、再用 $d 的完整路徑啟動精靈', () => {
    // Arrange
    const origin = 'https://kumi.example.com';

    // Act
    const command = oneLineCommand(windowsPackage, origin);

    // Assert
    expect(command).toBe(`${installCommand(windowsPackage)}; ${pathCommand('windows-x64')}; & "$d\\kumi.exe" setup --server "https://kumi.example.com"`);
  });

  it('IPv6 的網址有方括號：用雙引號包住，zsh 才不會當成萬用字元', () => {
    // Arrange
    const origin = 'http://[::1]:8080';

    // Act
    // Assert
    expect(setupCommand(origin)).toBe('kumi setup --server "http://[::1]:8080"');
    expect(oneLineCommand(macArmPackage, origin)).toContain('setup --server "http://[::1]:8080"');
  });

  it('網址含引號、空白或指令符號：不產生指令', () => {
    // Arrange
    const origin = "http://it's.example";

    // Act
    // Assert
    expect(setupCommand(origin)).toBeNull();
    expect(oneLineCommand(macArmPackage, origin)).toBeNull();
    expect(oneLineCommand(windowsPackage, 'https://x.example; id')).toBeNull();
  });
});
