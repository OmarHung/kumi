import type { Nodes, Root } from 'mdast';
import remarkParse from 'remark-parse';
import { unified } from 'unified';
import { attachmentIdOf, isAllowedUrl, isMentionUrl, mentionIdOf } from './markdownUrls';
import { REMARK_PLUGINS } from './markdownPlugins';

/** 把 Markdown 解析成 mdast：外掛與顯示端同一份（REMARK_PLUGINS），文字原文交給外掛，自動連結的切斷才和畫面一樣。 */
export function parseMarkdown(text: string): Root {
  const processor = unified().use(remarkParse).use(REMARK_PLUGINS);
  // parse 一定產生 root；runSync 的型別只說是 Node。
  return processor.runSync(processor.parse(text), text) as Root;
}

/** 文字裡畫得出來的連結：網址（照顯示端認的協定）與提及的人員編號，依出現的順序、不去重複。 */
export interface MarkdownLinks {
  /** http、https、mailto 的連結網址（原文，沒解析過）：顯示端會畫成可以點的連結。 */
  readonly externalUrls: readonly string[];
  readonly mentionIds: readonly string[];
}

const EXTERNAL = /^(https?:|mailto:)/i;
const IMAGE_AS_LINK = /^https?:/i;

type DefinitionUrls = ReadonlyMap<string, string>;

/** 參考式連結的定義：同一個識別字只認第一個（和 mdast-util-to-hast 一樣）。 */
function collectDefinitions(node: Nodes, found: Map<string, string>): void {
  if (node.type === 'definition' && !found.has(node.identifier)) found.set(node.identifier, node.url);
  if ('children' in node) node.children.forEach((child) => { collectDefinitions(child, found); });
}

interface Collector {
  readonly urls: string[];
  readonly mentions: string[];
}

function addLink(url: string, into: Collector): void {
  if (!isAllowedUrl(url)) return;
  if (isMentionUrl(url)) {
    const id = mentionIdOf(url);
    if (id !== '') into.mentions.push(id);
    return;
  }
  if (attachmentIdOf(url) === null && EXTERNAL.test(url)) into.urls.push(url);
}

/** 圖片：只有 attachment: 會載入；外部的 http(s) 圖片在連結裡面是純文字，不在連結裡面才畫成連結（Image 元件）。 */
function addImage(url: string, isInsideLink: boolean, into: Collector): void {
  if (!isInsideLink && isAllowedUrl(url) && IMAGE_AS_LINK.test(url)) into.urls.push(url);
}

/**
 * 連結的內容會被包進 <a>：網址要認得、不是提及，也不是附件（附件在沒有附件可對的文字裡只剩文字，
 * 這時裡面的外部圖片會畫成連結；寧可多列，不要漏列）。
 */
function isRenderedAsAnchor(node: Nodes, definitions: DefinitionUrls): boolean {
  const url = node.type === 'link' ? node.url : node.type === 'linkReference' ? definitions.get(node.identifier) : undefined;
  return url !== undefined && isAllowedUrl(url) && !isMentionUrl(url) && attachmentIdOf(url) === null;
}

function walk(node: Nodes, definitions: DefinitionUrls, isInsideLink: boolean, into: Collector): void {
  switch (node.type) {
    case 'link':
      addLink(node.url, into);
      break;
    case 'linkReference': {
      const url = definitions.get(node.identifier);
      if (url !== undefined) addLink(url, into);
      break;
    }
    case 'image':
      addImage(node.url, isInsideLink, into);
      break;
    case 'imageReference': {
      const url = definitions.get(node.identifier);
      if (url !== undefined) addImage(url, isInsideLink, into);
      break;
    }
    default:
  }
  if (!('children' in node)) return;
  // 沒有定義的參考式連結，顯示端只畫原文，不當連結；裡面的圖片也不在連結裡面。
  const isLink = isRenderedAsAnchor(node, definitions);
  node.children.forEach((child) => { walk(child, definitions, isInsideLink || isLink, into); });
}

/** 這段 Markdown 在畫面上會出現的連結與提及；寫在程式碼裡的網址不會被畫成連結，所以不在裡面。 */
export function markdownLinks(text: string): MarkdownLinks {
  const tree = parseMarkdown(text);
  const definitions = new Map<string, string>();
  collectDefinitions(tree, definitions);
  const collector: Collector = { urls: [], mentions: [] };
  walk(tree, definitions, false, collector);
  return { externalUrls: collector.urls, mentionIds: collector.mentions };
}
