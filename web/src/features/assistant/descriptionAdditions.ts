import type { Member } from '../../api/types';
import { markdownLinks } from '../../components/Markdown/markdownLinks';
import { NOT_A_MEMBER } from '../../domain/fields';

/** 說明裡畫得出來的連結（解析之後的完整網址 → 實際的主機）、信箱連結（完整網址 → 信箱的網域）與提及的人員編號。 */
export interface LinkTargets {
  /** http、https 的連結；主機含埠，是瀏覽器點下去真正會去的地方，不是連結上寫的字。 */
  readonly links: ReadonlyMap<string, string>;
  readonly mailboxes: ReadonlyMap<string, readonly string[]>;
  readonly mentionIds: ReadonlySet<string>;
}

function mailboxDomains(url: URL): readonly string[] {
  const addresses = decodeURIComponentSafe(url.pathname).split(',');
  return addresses.map((address) => address.slice(address.lastIndexOf('@') + 1).trim().toLowerCase()).filter((domain) => domain !== '');
}

function decodeURIComponentSafe(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

/**
 * 說明裡的連結與提及：用顯示 Markdown 的同一套解析（markdownLinks），所以摘要列出的就是畫面上會出現的。
 * 網址和瀏覽器一樣對著目前的頁面解析（`http:example.com` 這種寫法是什麼就是什麼）；解析不了的略過。
 */
export function linkTargets(text: string): LinkTargets {
  const { externalUrls, mentionIds } = markdownLinks(text);
  const links = new Map<string, string>();
  const mailboxes = new Map<string, readonly string[]>();
  for (const raw of externalUrls) {
    try {
      const url = new URL(raw, window.location.href);
      if (url.protocol === 'mailto:') mailboxes.set(url.href, mailboxDomains(url));
      else links.set(url.href, url.host);
    } catch {
      // 網址解析不了的，畫面上點了也去不了，不算連結。
    }
  }
  return { links, mailboxes, mentionIds: new Set(mentionIds) };
}

function listOf(values: Iterable<string>): string {
  return [...new Set(values)].join('、');
}

/**
 * 建議的說明裡的外部連結（顯示實際的網域）、信箱連結與提及，寫成一行；沒有就是 null。
 * 套用之後這些會直接出現在別人看得到的說明裡，使用者按「套用」之前該先看到。
 *
 * before 是項目現在的說明：有的話只列比現在多出來的（新增）；讀不到（undefined）就列建議裡全部的，不能把本來就有的說成新增。
 * members 是專案的成員：還沒載入（undefined）時只說幾個人，不把人說成「不是成員的人」；提及的名字查成員，不信說明裡自己寫的字。
 */
export function descriptionAdditions(before: string | undefined, after: string, members: readonly Member[] | undefined): string | null {
  const current = linkTargets(after);
  const known = before === undefined ? null : linkTargets(before);
  const links = [...current.links].filter(([href]) => known?.links.has(href) !== true);
  const mailboxes = [...current.mailboxes].filter(([href]) => known?.mailboxes.has(href) !== true);
  const mentions = [...current.mentionIds].filter((id) => known?.mentionIds.has(id) !== true);
  const verb = known === null ? '有' : '新增';
  const parts = [
    links.length > 0 ? `${verb} ${String(links.length)} 個連結：${listOf(links.map(([, host]) => host))}` : null,
    mailboxes.length > 0 ? `${verb} ${String(mailboxes.length)} 個信箱連結：${listOf(mailboxes.flatMap(([, domains]) => domains))}` : null,
    mentions.length > 0 ? mentionPart(mentions, members) : null,
  ].filter((part): part is string => part !== null);
  if (parts.length === 0) return null;
  return known === null ? `這份說明裡${parts.join('；')}。` : `這份說明會${parts.join('；')}。`;
}

function mentionPart(ids: readonly string[], members: readonly Member[] | undefined): string {
  const count = `提及 ${String(ids.length)} 個人`;
  if (members === undefined) return count;
  const names = ids.map((id) => members.find((member) => member.personId === id)?.displayName ?? NOT_A_MEMBER);
  return `${count}：${names.join('、')}`;
}
