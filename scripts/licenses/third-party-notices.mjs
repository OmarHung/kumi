#!/usr/bin/env node
// 產生 THIRD-PARTY-NOTICES.txt：映像檔與 kumi 執行檔散布出去的第三方套件、它們的授權全文與附帶的聲明。
// MIT、Apache-2.0、BSD 這類授權都要求散布時附上版權聲明與條文；套件有兩百多個，手寫一定會漏，
// 所以從實際解析出來的相依清單產生，內容不帶日期，同樣的相依產生出來的檔案一字不差。
//
// 用法：node scripts/licenses/third-party-notices.mjs           重新產生
//       node scripts/licenses/third-party-notices.mjs --check   只比對，過期就以結束代碼 1 結束
// 先決條件：dotnet restore 過，web/ 底下 npm ci 過（要讀套件裡附的授權檔）。
//
// 套件自己沒附授權檔時，條文從 texts/ 拿：先找 texts/by-package/<套件名小寫>.txt（上游 repo 的原文），
// 再找 texts/<SPDX 代號>.txt（標準條文）。兩個都沒有就失敗，逼加套件的人把條文補進來。
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const OUTPUT = path.join(ROOT, 'THIRD-PARTY-NOTICES.txt');
const TEXTS = path.join(ROOT, 'scripts/licenses/texts');
const NUGET_CACHE = process.env.NUGET_PACKAGES ?? path.join(os.homedir(), '.nuget/packages');
// 只列會散布出去的：API（連同它引用的模組）與 CLI。測試專案的套件不進映像檔。
const NUGET_PROJECTS = ['src/PmTool.Api/PmTool.Api.csproj', 'src/PmTool.Cli/PmTool.Cli.csproj'];
const LICENSE_FILE = /^((un)?licen[sc]e|copying)/i;
const NOTICE_FILE = /^(notice|third-party-notices)/i;
const NODE_MODULES = 'node_modules/';
const RULE = '='.repeat(72);
const THIN_RULE = '-'.repeat(72);

const ECOSYSTEMS = [
  { key: 'NuGet', title: 'NuGet packages: the server and the kumi command-line tool' },
  { key: 'runtime', title: '.NET runtime: bundled in the single-file kumi executables' },
  { key: 'npm', title: 'npm packages: the web application' },
];

// 單一執行檔的 kumi 把 .NET 執行環境包在裡面，它不是 NuGet 的相依、清單裡看不到，所以寫在這裡。
// 它附帶的第三方聲明和下面 System.* 套件帶的是同一份（都出自 dotnet/runtime）。
const BUNDLED_RUNTIME = {
  ecosystem: 'runtime',
  name: '.NET Runtime',
  version: '10.0',
  license: 'MIT',
  copyright: 'Copyright (c) .NET Foundation and Contributors',
  url: 'https://github.com/dotnet/runtime',
  licenseTexts: [],
  notices: [],
};

const XML_ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };

const normalize = (text) =>
  text.replace(/^\uFEFF/, '').replace(/\r\n?/g, '\n').split('\n').map((line) => line.trimEnd()).join('\n').trim();
const readText = (file) => normalize(fs.readFileSync(file, 'utf8'));
const label = (component) => `${component.name} ${component.version}`;
const decodeXml = (text) => text.replace(/&(amp|lt|gt|quot|apos);/g, (_, name) => XML_ENTITIES[name]);

function xmlTag(xml, tag) {
  const match = xml.match(new RegExp(`<${tag}(?: [^>]*)?>([^<]*)</${tag}>`));
  return match ? decodeXml(match[1].trim()) : '';
}

function shippedFiles(dir, pattern) {
  return fs.readdirSync(dir)
    .filter((name) => pattern.test(name) && fs.statSync(path.join(dir, name)).isFile())
    .sort()
    .map((name) => ({ file: name, text: readText(path.join(dir, name)) }));
}

function resolvedNugetPackages() {
  const found = new Map();
  for (const project of NUGET_PROJECTS) {
    const args = ['list', path.join(ROOT, project), 'package', '--include-transitive', '--format', 'json'];
    const report = JSON.parse(execFileSync('dotnet', args, { encoding: 'utf8' }));
    const frameworks = (report.projects ?? []).flatMap((p) => p.frameworks ?? []);
    const packages = frameworks.flatMap((f) => [...(f.topLevelPackages ?? []), ...(f.transitivePackages ?? [])]);
    for (const pkg of packages) found.set(`${pkg.id}/${pkg.resolvedVersion}`, { id: pkg.id, version: pkg.resolvedVersion });
  }
  return [...found.values()];
}

function readNugetPackage({ id, version }) {
  const dir = path.join(NUGET_CACHE, id.toLowerCase(), version.toLowerCase());
  if (!fs.existsSync(dir)) throw new Error(`NuGet 快取裡沒有 ${id} ${version}，先跑 dotnet restore。`);
  const nuspec = fs.readFileSync(path.join(dir, `${id.toLowerCase()}.nuspec`), 'utf8');
  const repository = nuspec.match(/<repository [^>]*url="([^"]+)"/);
  return {
    ecosystem: 'NuGet',
    name: id,
    version,
    license: xmlTag(nuspec, 'license'),
    copyright: xmlTag(nuspec, 'copyright') || xmlTag(nuspec, 'authors'),
    url: xmlTag(nuspec, 'projectUrl') || (repository ? repository[1] : ''),
    licenseTexts: shippedFiles(dir, LICENSE_FILE),
    notices: shippedFiles(dir, NOTICE_FILE),
  };
}

function authorName(author) {
  const raw = typeof author === 'string' ? author : author?.name ?? '';
  // 只留名字：信箱與個人網址不是版權聲明的一部分。
  return raw.replace(/<[^>]*>|\([^)]*\)/g, '').trim();
}

function repositoryUrl(manifest) {
  const repository = typeof manifest.repository === 'string' ? manifest.repository : manifest.repository?.url;
  return (manifest.homepage ?? repository ?? '').replace(/^git\+/, '').replace(/\.git$/, '');
}

function readNpmPackage(lockPath, entry) {
  const dir = path.join(ROOT, 'web', lockPath);
  if (!fs.existsSync(dir)) {
    // 選用的平台套件，這台機器沒裝，也不會進打包結果。
    if (entry.optional) return [];
    throw new Error(`web/${lockPath} 不存在，先在 web/ 底下跑 npm ci。`);
  }
  const manifest = JSON.parse(fs.readFileSync(path.join(dir, 'package.json'), 'utf8'));
  return [{
    ecosystem: 'npm',
    name: lockPath.slice(lockPath.lastIndexOf(NODE_MODULES) + NODE_MODULES.length),
    version: entry.version,
    license: entry.license ?? manifest.license ?? '',
    copyright: authorName(manifest.author),
    url: repositoryUrl(manifest),
    licenseTexts: shippedFiles(dir, LICENSE_FILE),
    notices: shippedFiles(dir, NOTICE_FILE),
  }];
}

function readNpmPackages() {
  const lock = JSON.parse(fs.readFileSync(path.join(ROOT, 'web/package-lock.json'), 'utf8'));
  // 開發用的套件（測試、建置工具）不進打包結果，不列。
  const shipped = Object.entries(lock.packages).filter(([lockPath, entry]) => lockPath !== '' && !entry.dev && !entry.link);
  const unique = new Map();
  for (const component of shipped.flatMap(([lockPath, entry]) => readNpmPackage(lockPath, entry))) {
    // 同一個版本裝在兩個位置的只列一次。
    if (!unique.has(label(component))) unique.set(label(component), component);
  }
  return [...unique.values()];
}

function fallbackText(component) {
  const candidates = [
    path.join(TEXTS, 'by-package', `${component.name.toLowerCase()}.txt`),
    path.join(TEXTS, `${component.license}.txt`),
  ];
  const file = candidates.find((candidate) => fs.existsSync(candidate));
  if (!file) {
    throw new Error(`${label(component)}（${component.license || '沒有標示授權'}）沒有附授權檔，scripts/licenses/texts/ 裡也沒有對應的條文。`
      + '確認它的授權可以用之後，把條文存成 texts/by-package/<套件名小寫>.txt 或 texts/<SPDX 代號>.txt。');
  }
  return { file: path.relative(TEXTS, file), text: readText(file), supplied: true };
}

const licenseSources = (component) => (component.licenseTexts.length > 0 ? component.licenseTexts : [fallbackText(component)]);

// 內容相同的全文只印一次，前面列出它適用的套件。
function groupTexts(components, sourcesOf, prefix) {
  const groups = new Map();
  for (const component of components) {
    for (const { file, text, supplied } of sourcesOf(component)) {
      const key = createHash('sha256').update(text).digest('hex');
      const group = groups.get(key) ?? { text, supplied: Boolean(supplied), members: [] };
      groups.set(key, { ...group, members: [...group.members, { owner: label(component), file }] });
    }
  }
  return [...groups.values()].map((group, index) => ({ ...group, id: `${prefix}${String(index + 1).padStart(3, '0')}` }));
}

const idsFor = (component, groups) =>
  groups.filter((group) => group.members.some((member) => member.owner === label(component))).map((group) => group.id);

function renderComponent(component, licenses) {
  const own = licenses.filter((group) => group.members.some((member) => member.owner === label(component)));
  const lines = [`- ${label(component)} (${component.license}) [${own.map((group) => group.id).join(', ')}]`];
  // 條文不是套件自己附的時候，版權聲明只有這裡看得到。
  if (own.some((group) => group.supplied) && component.copyright) lines.push(`  ${component.copyright}`);
  if (component.url) lines.push(`  ${component.url}`);
  return lines.join('\n');
}

function renderGroup(group, heading) {
  const members = group.members.map((member) => `  - ${member.owner} (${member.file})`);
  const note = group.supplied
    ? ['Not shipped inside the package. Text supplied from scripts/licenses/texts/;', 'the copyright statements are listed in section 1.']
    : [];
  return [THIN_RULE, `[${group.id}] ${heading}`, ...members, ...note, THIN_RULE, '', group.text, ''].join('\n');
}

function render(components) {
  const licenses = groupTexts(components, licenseSources, 'L');
  const notices = groupTexts(components, (component) => component.notices, 'N');
  const sections = ECOSYSTEMS.map(({ key, title }) => {
    const members = components.filter((component) => component.ecosystem === key);
    const rendered = members.map((component) => {
      const noticeIds = idsFor(component, notices);
      const suffix = noticeIds.length > 0 ? `\n  Additional notices: [${noticeIds.join(', ')}]` : '';
      return renderComponent(component, licenses) + suffix;
    });
    return [`${title} (${members.length})`, '', ...rendered, ''].join('\n');
  });
  return [
    'THIRD-PARTY NOTICES',
    '',
    'Kumi includes or depends on the third-party software listed below. Each',
    'component stays under its own license; the Kumi License in the LICENSE',
    'file covers Kumi itself and does not change the terms of any of them.',
    '',
    'Generated by scripts/licenses/third-party-notices.mjs from the resolved',
    'dependencies. Do not edit by hand; regenerate after a dependency changes.',
    '',
    RULE, '1. COMPONENTS', RULE, '',
    'The codes in square brackets point to the license texts in section 2.',
    '',
    ...sections,
    RULE, '2. LICENSE TEXTS', RULE, '',
    ...licenses.map((group) => renderGroup(group, 'Applies to:')),
    RULE, '3. ADDITIONAL NOTICES SHIPPED WITH COMPONENTS', RULE, '',
    ...notices.map((group) => renderGroup(group, 'Shipped with:')),
  ].join('\n').trimEnd() + '\n';
}

function collectComponents() {
  const components = [...resolvedNugetPackages().map(readNugetPackage), ...readNpmPackages(), BUNDLED_RUNTIME];
  const unlabelled = components.filter((component) => !component.license);
  if (unlabelled.length > 0) throw new Error(`這些套件沒有標示授權，先查清楚：${unlabelled.map(label).join('、')}`);
  return components.sort((a, b) => a.name.localeCompare(b.name, 'en') || a.version.localeCompare(b.version, 'en'));
}

function main() {
  const document = render(collectComponents());
  if (!process.argv.includes('--check')) {
    fs.writeFileSync(OUTPUT, document);
    console.error(`已寫入 ${path.relative(ROOT, OUTPUT)}（${document.length} 個字元）。`);
    return;
  }
  const current = fs.existsSync(OUTPUT) ? fs.readFileSync(OUTPUT, 'utf8') : '';
  if (current !== document) {
    throw new Error('THIRD-PARTY-NOTICES.txt 和目前的相依不一致，重新產生：node scripts/licenses/third-party-notices.mjs');
  }
  console.error('THIRD-PARTY-NOTICES.txt 是最新的。');
}

try {
  main();
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
