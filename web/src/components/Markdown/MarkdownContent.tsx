import { Children, createContext, isValidElement, memo, useContext, useMemo, useState, type ComponentProps, type ReactNode } from 'react';
import ReactMarkdown, { type Components } from 'react-markdown';
import { attachmentContentUrl } from '../../api/attachments';
import { Icon } from '../Icon/Icon';
import { actualHostOf, linkHostNote } from './linkHosts';
import { attachmentIdOf, isMentionUrl, markdownUrlTransform, mentionIdOf } from './markdownUrls';
import { REMARK_PLUGINS } from './markdownPlugins';
import styles from './Markdown.module.css';

export interface MarkdownProps {
  readonly text: string;
  /** 人員編號對應的名稱（目前的成員）：提及畫成「@名稱」時用；找不到的用連結本來的文字。 */
  readonly mentionNames: ReadonlyMap<string, string>;
  /** false：這段文字沒有附件可以對（專案說明）：attachment: 的連結與圖片只留文字，不發出請求。預設 true。 */
  readonly hasAttachments?: boolean;
  /** 附件編號對應的下載網址；預設是項目的附件（知識庫的頁面傳 knowledgeAttachmentContentUrl）。 */
  readonly attachmentUrl?: (attachmentId: string) => string;
  /**
   * 外部連結旁的網域提示：'mismatch'（預設）只在連結的文字像網址、而且主機和實際的不同時提示；
   * 'always' 每一個外部連結都接上實際的網域（AI 的回覆：內容不是人寫的，沒辦法靠上下文判斷）。
   */
  readonly linkHosts?: LinkHostsMode;
}

export type LinkHostsMode = 'mismatch' | 'always';

const IMAGE_URL = /^https?:/i;

/** 內容裡的標題不搶頁面的層級：頁面的 h1／h2 與區塊的 h2／h3 之後，最高從 h4 開始（h1、h2 都是 h4，樣式照原本的層級）。 */
const HEADING_TAGS = { 1: 'h4', 2: 'h4', 3: 'h5', 4: 'h5', 5: 'h6', 6: 'h6' } as const;

function headingOf(level: keyof typeof HEADING_TAGS) {
  const Tag = HEADING_TAGS[level];
  return function Heading({ children }: { readonly children?: ReactNode }) {
    return (
      <Tag className={styles.heading} data-level={level}>
        {children}
      </Tag>
    );
  };
}

function plainText(children: ReactNode): string {
  return Children.toArray(children)
    .map((child) => (typeof child === 'string' || typeof child === 'number' ? String(child) : ''))
    .join('');
}

/** 連結顯示出來的文字（含粗體等巢狀元素裡的字）：判斷文字像不像網址要看使用者實際讀到的。 */
function visibleText(children: ReactNode): string {
  return Children.toArray(children)
    .map((child) => {
      if (typeof child === 'string' || typeof child === 'number') return String(child);
      if (isValidElement<{ children?: ReactNode }>(child)) return visibleText(child.props.children);
      return '';
    })
    .join('');
}

/**
 * 人員編號對應的名稱，用 Context 傳給連結：元件的函式本身永遠是同一個（模組層級的 COMPONENTS）。
 * 要是每次渲染都產生新的元件函式，React 會把整段 DOM 拆掉重建（選取、捲動位置、說明的「顯示全部」量到的高度都跟著丟）。
 */
const MentionNamesContext = createContext<ReadonlyMap<string, string>>(new Map());

interface AttachmentsValue {
  /** 這段文字有沒有附件可以對；沒有時 attachment: 的連結與圖片不載入。 */
  readonly enabled: boolean;
  readonly urlOf: (attachmentId: string) => string;
}

const AttachmentsContext = createContext<AttachmentsValue>({ enabled: true, urlOf: attachmentContentUrl });

const LinkHostsContext = createContext<LinkHostsMode>('mismatch');

/**
 * 連結後面的網域提示：不一致時是警告樣式的「（實際連到 …）」；實際的主機是國際化網域（punycode）時，
 * 不論文字與連結一不一致都標出來；'always' 時一致的也列出網域；其他是 null。
 */
function HostNote({ href, children }: { readonly href: string; readonly children: ReactNode }) {
  const mode = useContext(LinkHostsContext);
  const note = linkHostNote(visibleText(children), href);
  if (note?.kind === 'mismatch') {
    return (
      <span className={styles.hostWarning} title={href}>
        {note.isPunycode ? `（實際連到 ${note.host}，網域含非英文字母）` : `（實際連到 ${note.host}）`}
      </span>
    );
  }
  if (note?.kind === 'punycode') {
    return (
      <span className={styles.hostWarning} title={href}>
        {`（網域含非英文字母：${note.host}）`}
      </span>
    );
  }
  const host = mode === 'always' ? actualHostOf(href) : null;
  if (host === null) return null;
  return (
    <span className={styles.hostNote} title={href}>
      {`（${host}）`}
    </span>
  );
}

/** 在連結裡面：裡面的圖片不再自己包一層 <a>（巢狀的連結不合法，輔助科技與點擊都會亂）。 */
const InsideLinkContext = createContext(false);

function Link({ href, children }: { readonly href?: string; readonly children?: ReactNode }) {
  const mentionNames = useContext(MentionNamesContext);
  const { enabled: hasAttachments, urlOf } = useContext(AttachmentsContext);
  // 不認的協定：urlTransform 已經把網址拿掉了，只剩文字。
  if (href === undefined) return <span>{children}</span>;
  if (isMentionUrl(href)) {
    const id = mentionIdOf(href);
    const name = mentionNames.get(id) ?? plainText(children);
    // 找不到人（不是這個專案的成員、編號亂寫）：普通文字，沒有底色，不要看起來像真的提及。
    const isKnown = mentionNames.has(id);
    return (
      <span className={isKnown ? styles.mention : undefined} data-unknown-mention={isKnown ? undefined : true}>
        {name === '' ? <>@{children}</> : `@${name}`}
      </span>
    );
  }
  const attachmentId = attachmentIdOf(href);
  if (attachmentId !== null && !hasAttachments) return <span>{children}</span>;
  const anchor = (
    <a href={attachmentId === null ? href : urlOf(attachmentId)} target="_blank" rel="noopener noreferrer">
      <InsideLinkContext value>{children}</InsideLinkContext>
    </a>
  );
  if (attachmentId !== null) return anchor;
  return (
    <>
      {anchor}
      <HostNote href={href}>{children}</HostNote>
    </>
  );
}

/**
 * 自己項目的附件圖片：同網域的下載端點（內容安全政策允許），點了在新分頁開原圖。載入失敗（附件被刪、不是圖片）
 * 換成一行字，名稱沿用寫在文字裡的檔名；不跳版面的意思是：換成的是同一行的行內文字，不會多出一塊空白的方框。
 */
function AttachmentImage({ attachmentId, alt }: { readonly attachmentId: string; readonly alt: string }) {
  const [hasFailed, setHasFailed] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const isInsideLink = useContext(InsideLinkContext);
  const { urlOf } = useContext(AttachmentsContext);
  // 沒寫說明文字的圖片（![](…)）：名稱退回「圖片」，不留空的名稱。
  const name = alt.trim() === '' ? '圖片' : alt;
  if (hasFailed) return <span className={styles.imageGone}>圖片已刪除或無法顯示：{name}</span>;
  const url = urlOf(attachmentId);
  const image = (
    <img
      src={url}
      alt={name}
      loading="lazy"
      className={styles.image}
      // 載入之前先留一點高度（樣式的 data-loading）：通知連過來捲到留言之後，圖片才載好也不會把那一則推走。
      data-loading={isLoading || undefined}
      onLoad={() => {
        setIsLoading(false);
      }}
      onError={() => {
        setHasFailed(true);
      }}
    />
  );
  if (isInsideLink) return image;
  return (
    <a href={url} target="_blank" rel="noopener noreferrer" className={styles.imageLink}>
      {image}
    </a>
  );
}

/** 外部網址的圖片不載入（內容安全政策只允許同網域）：http(s) 的畫成連結，說明文字是名稱，沒有說明就用網址；其他只剩說明文字。 */
function Image({ src, alt }: { readonly src?: string; readonly alt?: string }) {
  const text = alt ?? '';
  const isInsideLink = useContext(InsideLinkContext);
  const { enabled: hasAttachments } = useContext(AttachmentsContext);
  const attachmentId = src === undefined ? null : attachmentIdOf(src);
  // key：換成另一個附件時重來，前一張的「載入失敗」不帶過來。
  if (attachmentId !== null && hasAttachments) return <AttachmentImage key={attachmentId} attachmentId={attachmentId} alt={text} />;
  if (src === undefined || !IMAGE_URL.test(src)) return <span>{text}</span>;
  if (isInsideLink) return <span>{text === '' ? src : text}</span>;
  return (
    <a href={src} target="_blank" rel="noopener noreferrer">
      {text === '' ? src : text}
    </a>
  );
}

/** 工作清單（- [x]）是唯讀的：不畫可以按的勾選框，用圖示加「已完成」「未完成」說出狀態。 */
function TaskBox({ type, checked }: ComponentProps<'input'>) {
  if (type !== 'checkbox') return null;
  const isChecked = checked === true;
  return (
    <span className={styles.taskBox} role="img" aria-label={isChecked ? '已完成' : '未完成'} data-checked={isChecked}>
      {isChecked && <Icon name="check" />}
    </span>
  );
}

const COMPONENTS: Components = {
    h1: headingOf(1),
    h2: headingOf(2),
    h3: headingOf(3),
    h4: headingOf(4),
    h5: headingOf(5),
    h6: headingOf(6),
    a: ({ href, children }) => <Link href={href}>{children}</Link>,
    img: ({ src, alt }) => <Image src={typeof src === 'string' ? src : undefined} alt={alt} />,
    input: (props) => <TaskBox {...props} />,
    li: ({ className, children }) => (
      <li data-task={className?.includes('task-list-item') === true || undefined}>{children}</li>
    ),
    // 寬的表格與程式碼在自己的方塊裡橫向捲動（不撐開頁面）；鍵盤也要捲得動，所以停得到。
    table: ({ children }) => (
      <div className={styles.scroll} role="region" aria-label="表格" tabIndex={0}>
        <table>{children}</table>
      </div>
    ),
    pre: ({ children }) => (
      <pre className={styles.scroll} role="region" aria-label="程式碼" tabIndex={0}>
        {children}
      </pre>
    ),
};

/**
 * 說明與留言的 Markdown 顯示（docs/DESIGN.md 7.4）：GFM（清單、粗體、程式碼、連結、表格、刪除線），單一換行就是換行
 * （既有的內容都是照純文字打的）。安全：不開原始的 HTML（沒有 rehype-raw，照文字顯示）、連結只認 http、https、mailto、
 * mention、自己附件的 attachment:；圖片只載入自己附件的，外部網址的不載入。這個檔案帶著 react-markdown 全家，只經過 Markdown.tsx 延後載入。
 */
export const MarkdownContent = memo(function MarkdownContent({ text, mentionNames, hasAttachments = true, attachmentUrl = attachmentContentUrl, linkHosts = 'mismatch' }: MarkdownProps) {
  // 值要穩定：memo 的元件重畫時不能每次都換一個新物件，不然 Context 的使用者全部重畫。
  const attachments = useMemo(() => ({ enabled: hasAttachments, urlOf: attachmentUrl }), [hasAttachments, attachmentUrl]);
  return (
    <MentionNamesContext value={mentionNames}>
      <AttachmentsContext value={attachments}>
        <LinkHostsContext value={linkHosts}>
          <div className={styles.markdown} data-state="rendered">
            <ReactMarkdown remarkPlugins={REMARK_PLUGINS} urlTransform={markdownUrlTransform} components={COMPONENTS}>
              {text}
            </ReactMarkdown>
          </div>
        </LinkHostsContext>
      </AttachmentsContext>
    </MentionNamesContext>
  );
});
