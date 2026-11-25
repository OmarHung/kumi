/**
 * 連結的文字與實際會去的網域是否一致：文字寫成 www.bank.example、網址卻指到別的主機時，
 * 看的人在點之前要看得出來。純函式，顯示時對每一個連結各判斷一次（MarkdownContent 的 Link）。
 */

const WWW_PREFIX = /^www\./;
const PUNYCODE_LABEL = /(?:^|\.)xn--/;
// 主機名稱的三種寫法：網域（\p{L} 讓同形字的主機也被認出來）、IPv4、localhost。
const DOMAIN = String.raw`(?:[\p{L}\p{N}-]+\.)+\p{L}{2,}`;
const IPV4 = String.raw`(?:\d{1,3}\.){3}\d{1,3}`;
const HOST = `(?:${DOMAIN}|${IPV4}|localhost)`;
// 一個片段整段就是網址或主機：可選的協定、可選的「帳號@」、主機、可選的連接埠與路徑。
const SCHEME = String.raw`(?:[a-z][a-z0-9+.-]*://)?`;
const USER_INFO = String.raw`(?:([^\s@/?#]*)@)?`;
const URL_LIKE = new RegExp(`^${SCHEME}${USER_INFO}(${HOST})(?::\\d{1,5})?(?:[/?#]\\S*)?$`, 'iu');
const EMAIL_LIKE = /^[^\s@/:]+@(?:[\p{L}\p{N}-]+\.)+\p{L}{2,}$/iu;
const HAS_SCHEME = /^[a-z][a-z0-9+.-]*:\/\//i;
// 片段前後的標點（句子裡的括號、引號、逗號）；結尾的點也算，句末的網域後面常接句號。
const EDGE_PUNCTUATION = /^[\s"'“”‘’()[\]{}<>,;:!?，；：！？、「」『』（）.]+|[\s"'“”‘’()[\]{}<>,;:!?，；：！？、「」『』（）.]+$/gu;

// 片段之間的分隔：空白，以及句子中間也會出現的括號與引號（「（bank.example）登入」）。網址裡用得到的 : ? , 不算。
const FRAGMENT_SEPARATORS = /[\s"'“”‘’()[\]{}<>，；！？、「」『』（）]+/u;
const CJK_RUN = /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}]+/u;

/** 文字像檔名或套件名稱：結尾是這些副檔名，而且它們不是實際存在的頂級網域。 */
const FILE_EXTENSIONS: ReadonlySet<string> = new Set([
  'js', 'mjs', 'cjs', 'jsx', 'ts', 'tsx', 'json', 'yml', 'yaml', 'toml', 'xml', 'csv', 'txt', 'log', 'ini', 'cfg', 'lock',
  'css', 'scss', 'html', 'htm', 'pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'png', 'jpg', 'jpeg', 'gif', 'svg', 'webp',
  'mp3', 'mp4', 'wav', 'tar', 'gz', 'exe', 'dll', 'dmg', 'bak', 'tmp', 'sql', 'cpp', 'java',
]);
/** 同時是實際存在的頂級網域：只有名稱裡有大寫字母（README.md、Dockerfile.py 這種寫法）才當成檔名。 */
const AMBIGUOUS_EXTENSIONS: ReadonlySet<string> = new Set(['md', 'sh', 'py', 'rs', 'zip', 'mov', 'cs']);

interface Claim {
  /** 文字宣稱的主機（已換成 punycode、小寫）；Email 是整個位址。 */
  readonly host: string;
  /** 帳密寫法（https://bank.example@other.example）：不論實際的主機是什麼都要提示。 */
  readonly isForced: boolean;
}

function decodeSafe(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

/** 瀏覽器點下去真正去的主機：http(s) 是主機名稱（國際化網域是 punycode），mailto 是位址本身；其他協定是 null。 */
export function actualHostOf(href: string): string | null {
  if (/^mailto:/i.test(href)) {
    const address = decodeSafe(href.slice('mailto:'.length).split('?')[0] ?? '').trim().toLowerCase();
    return address === '' ? null : address;
  }
  if (!/^https?:/i.test(href)) return null;
  try {
    const { hostname } = new URL(href);
    const host = hostname.replace(/\.$/, '');
    return host === '' ? null : host;
  } catch {
    return null;
  }
}

/** 全形的點、斜線、冒號等寫法換成半形（NFKC 沒涵蓋的 。｡ 另外列），並去掉零寬空白、軟連字號這類格式字元。 */
function normalizeText(text: string): string {
  return text
    .replace(/\p{Cf}/gu, '')
    .normalize('NFKC')
    .replace(/[。｡．]/gu, '.')
    .replace(/\p{Cf}/gu, '');
}

function punycodeHost(host: string): string | null {
  try {
    return new URL(`http://${host}`).hostname.replace(/\.$/, '');
  } catch {
    return null;
  }
}

/** 一個片段（不含空白）宣稱的主機；不像網址、主機或 Email 就是 null。 */
function claimOfToken(token: string): Claim | null {
  if (EMAIL_LIKE.test(token) && !HAS_SCHEME.test(token)) return { host: token.toLowerCase(), isForced: false };
  const match = URL_LIKE.exec(token);
  const host = match?.[2];
  if (host === undefined) return null;
  const converted = punycodeHost(host);
  if (converted === null) return null;
  return { host: converted, isForced: match?.[1] !== undefined && HAS_SCHEME.test(token) };
}

/** 整段文字就是檔名或套件名稱（README.md、Node.js、package.json）：不當成網域。 */
function looksLikeFileName(text: string): boolean {
  if (/\s|\//u.test(text)) return false;
  const dot = text.lastIndexOf('.');
  if (dot <= 0) return false;
  const extension = text.slice(dot + 1).toLowerCase();
  if (FILE_EXTENSIONS.has(extension)) return true;
  return AMBIGUOUS_EXTENSIONS.has(extension) && /\p{Lu}/u.test(text.slice(0, dot));
}

function claimsOfTokens(tokens: readonly string[]): readonly Claim[] {
  return tokens.flatMap((token) => {
    const trimmed = token.replace(EDGE_PUNCTUATION, '');
    const claim = trimmed === '' ? null : claimOfToken(trimmed);
    return claim === null ? [] : [claim];
  });
}

/**
 * 一個片段宣稱的主機。中文的句子不用空白分字（「請到bank.example登入」），所以先把中日韓的字當成分隔看看：
 * 切出來的片段有像網址的就用它們；沒有才把整段當成一個主機（中文的國際化網域）。
 */
function claimsOfFragment(fragment: string): readonly Claim[] {
  const parts = fragment.split(CJK_RUN);
  const inner = parts.length > 1 ? claimsOfTokens(parts) : [];
  return inner.length > 0 ? inner : claimsOfTokens([fragment]);
}

/** 文字裡每一個像網址、主機名稱或 Email 的片段宣稱的主機（句子裡夾著網域也算）。 */
function claimsOf(text: string): readonly Claim[] {
  const normalized = normalizeText(text).trim();
  if (normalized === '' || looksLikeFileName(normalized)) return [];
  return normalized.split(FRAGMENT_SEPARATORS).flatMap(claimsOfFragment);
}

/** 連結的文字宣稱的主機：整段文字像網址或主機名稱就回它的主機（換成 punycode），像 Email 就回位址，否則 null。 */
export function claimedHostOf(text: string): string | null {
  const normalized = normalizeText(text).trim();
  if (/\s/u.test(normalized)) return null;
  return claimsOf(normalized)[0]?.host ?? null;
}

function domainOf(host: string): string {
  return host.includes('@') ? host.slice(host.lastIndexOf('@') + 1) : host;
}

function bare(host: string): string {
  return host.replace(WWW_PREFIX, '');
}

function isSameHost(claimed: string, actual: string): boolean {
  if (claimed.includes('@') || actual.includes('@')) return sameMailbox(claimed, actual);
  return bare(claimed) === bare(actual);
}

/** 實際的主機含 punycode（國際化網域）：同形字的網域和真的長得一樣，文字與連結用同一個時兩邊相同，看的人分不出來。 */
export function isPunycodeHost(host: string): boolean {
  return PUNYCODE_LABEL.test(domainOf(host));
}

/** 文字宣稱的主機和實際的不同時回實際的主機（含信箱位址）；一致、或文字不像網址，回 null。文字是一句話時每個片段都比對。 */
export function hostMismatch(text: string, href: string): string | null {
  const actual = actualHostOf(href);
  if (actual === null) return null;
  const isMismatch = claimsOf(text).some((claim) => claim.isForced || !isSameHost(claim.host, actual));
  return isMismatch ? actual : null;
}

/** 位址對位址要完全相同；一邊只寫了主機名稱時比網域。 */
function sameMailbox(claimed: string, actual: string): boolean {
  if (claimed.includes('@') && actual.includes('@')) return claimed === actual;
  return bare(domainOf(claimed)) === bare(domainOf(actual));
}

export type LinkHostNote =
  | { readonly kind: 'mismatch'; readonly host: string; readonly isPunycode: boolean }
  | { readonly kind: 'punycode'; readonly host: string };

/** 連結後面要不要提示：文字與實際的主機不一致優先；一致但實際的主機是國際化網域也要標出來；其他是 null。 */
export function linkHostNote(text: string, href: string): LinkHostNote | null {
  const mismatched = hostMismatch(text, href);
  if (mismatched !== null) return { kind: 'mismatch', host: mismatched, isPunycode: isPunycodeHost(mismatched) };
  const actual = actualHostOf(href);
  return actual !== null && isPunycodeHost(actual) ? { kind: 'punycode', host: actual } : null;
}
