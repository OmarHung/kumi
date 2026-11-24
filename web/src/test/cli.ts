import { http } from 'msw';
import type { CliCatalog, CliPackage } from '../api/cli';
import { failure, ok } from './api';
import { server } from './server';

const VERSION = '0.3.0';
const SHA = 'a'.repeat(64);

export const macArmPackage: CliPackage = { platform: 'macos-arm64', fileName: `kumi-${VERSION}-macos-arm64`, sizeBytes: 37_313_301, sha256: SHA };
export const macIntelPackage: CliPackage = { platform: 'macos-x64', fileName: `kumi-${VERSION}-macos-x64`, sizeBytes: 38_000_000, sha256: SHA };
export const windowsPackage: CliPackage = { platform: 'windows-x64', fileName: `kumi-${VERSION}-windows-x64.exe`, sizeBytes: 38_522_641, sha256: SHA };

export const exampleCliPackages: readonly CliPackage[] = [macArmPackage, macIntelPackage, windowsPackage];

/** GET /v1/cli 的假資料：預設三個平台都有、版本 0.3.0；packages 可以只留幾個，null 的 version 表示沒有提供下載。 */
export function exampleCliCatalog(overrides: Partial<CliCatalog> = {}): CliCatalog {
  return { version: VERSION, packages: exampleCliPackages, ...overrides };
}

/** 蓋掉預設的 /v1/cli；failing 為 true 時回 500。 */
export function serveCli(catalog: CliCatalog = exampleCliCatalog(), failing = false): void {
  server.use(http.get('/v1/cli', () => (failing ? failure(500, 'server.error', '伺服器暫時無法處理，請稍後再試。') : ok(catalog))));
}
