import { isMentionUrl } from './markdownUrls';

/** mdast 的節點，只寫這裡用到的部分（不為了型別多一個直接依賴）。 */
interface MdastNode {
  type: string;
  value?: string;
  url?: string;
  children?: MdastNode[];
}

function isMentionLink(node: MdastNode | undefined): boolean {
  return node?.type === 'link' && node.url !== undefined && isMentionUrl(node.url);
}

/** @[名稱](mention:編號) 的 @ 屬於提及：畫面上由提及自己畫「@名稱」，前面文字結尾的 @ 拿掉才不會變成兩個。 */
function stripMentionAt(node: MdastNode): void {
  const children = node.children;
  if (children === undefined) return;
  children.forEach((child, index) => {
    const previous = children[index - 1];
    if (isMentionLink(child) && previous?.type === 'text' && previous.value?.endsWith('@') === true) {
      previous.value = previous.value.slice(0, -1);
    }
    stripMentionAt(child);
  });
}

/** remark 外掛：先處理好提及的 @，渲染器只看連結。 */
export function remarkMentions() {
  return (tree: MdastNode) => {
    stripMentionAt(tree);
  };
}
