/** mdast 的節點，只寫這裡用到的部分（不為了型別多一個直接依賴）。 */
interface MdastNode {
  type: string;
  value?: string;
  url?: string;
  children?: MdastNode[];
  position?: { start: { offset?: number } };
}

interface SourceFile {
  value?: unknown;
}

/**
 * 全形與中文的標點（、。，：；！？（）「」【】…）。GFM 的自動連結只在空白處結束，中文的句子裡網址後面常常緊接標點，
 * 「https://…/edit），麻煩照這份改」整串都被當成網址。網址裡幾乎不會有這些字；中文字本身不算（網址的路徑可以是中文）。
 */
const CJK_PUNCTUATION = /[\u3000-\u303F\uFF01-\uFF0F\uFF1A-\uFF20\uFF3B-\uFF40\uFF5B-\uFF65]/u;

function textOf(link: MdastNode): string | undefined {
  const [only, ...rest] = link.children ?? [];
  return only?.type === 'text' && rest.length === 0 ? only.value : undefined;
}

/** 自動產生的連結：文字就是網址（www. 開頭的補了 http://，Email 補了 mailto:）。明確寫的 [文字](網址) 不算。 */
function autolinkText(link: MdastNode, source: string): string | undefined {
  const text = textOf(link);
  const url = link.url;
  if (link.type !== 'link' || text === undefined || url === undefined || !url.endsWith(text)) return undefined;
  // <網址> 是使用者自己標出範圍的，照他寫的。
  const offset = link.position?.start.offset;
  return offset !== undefined && source[offset] === '<' ? undefined : text;
}

/** 在第一個中文標點處切開：前面留在連結裡，後面還給文字。沒有要切的回 undefined。 */
function split(link: MdastNode, source: string): readonly MdastNode[] | undefined {
  const text = autolinkText(link, source);
  const cut = text === undefined ? -1 : text.search(CJK_PUNCTUATION);
  if (text === undefined || cut <= 0 || link.url === undefined) return undefined;
  const kept = text.slice(0, cut);
  const url = link.url.slice(0, link.url.length - (text.length - cut));
  return [
    { ...link, url, children: [{ type: 'text', value: kept }] },
    { type: 'text', value: text.slice(cut) },
  ];
}

function splitChildren(node: MdastNode, source: string): MdastNode {
  const children = node.children;
  if (children === undefined) return node;
  return {
    ...node,
    children: children.flatMap((child) => split(child, source) ?? [splitChildren(child, source)]),
  };
}

/** remark 外掛：GFM 的自動連結停在中文標點前（在 remark-gfm 之後執行）。 */
export function remarkCjkAutolinks() {
  return (tree: MdastNode, file: SourceFile): MdastNode => {
    const source = typeof file.value === 'string' ? file.value : '';
    return splitChildren(tree, source);
  };
}
