import { useId } from 'react';
import { useAuditEvents, type AuditEvent, type AuditPersonRef } from '../../api/audit';
import { Badge } from '../../components/Badge/Badge';
import { Button } from '../../components/Button/Button';
import { List, ListRow } from '../../components/List/List';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { EmptyState, ErrorState } from '../../components/StatusViews/StatusViews';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { formatInstant } from '../../domain/time';
import { useIsCompact } from '../../lib/useMediaQuery';
import { EmptyValue } from '../work-items/ItemCells';
import { AUDIT_OUTCOME_LABELS, auditActionText } from './auditText';
import styles from './AuditLog.module.css';

const SKELETON_ROWS = 4;
/** 紀錄裡的人已經不在組織（名字是 null）時顯示的字。 */
const PERSON_GONE_LABEL = '（已不在組織）';

/** 各頁接起來；重抓時邊界上可能重複，以編號去重（留先出現的）。 */
function flatten(pages: readonly { readonly items: readonly AuditEvent[] }[]): readonly AuditEvent[] {
  const seen = new Set<string>();
  return pages.flatMap((page) =>
    page.items.filter((event) => {
      if (seen.has(event.eventId)) return false;
      seen.add(event.eventId);
      return true;
    }),
  );
}

function Time({ at }: { readonly at: string }) {
  return (
    <time className={styles.time} dateTime={at}>
      {formatInstant(at)}
    </time>
  );
}

function Person({ person, emptyLabel }: { readonly person: AuditPersonRef | null; readonly emptyLabel: string }) {
  if (person === null) return <EmptyValue label={emptyLabel} />;
  return person.displayName === null ? <span className={styles.gone}>{PERSON_GONE_LABEL}</span> : <>{person.displayName}</>;
}

/** 失敗要看得出來（文字加警示的標籤，不只靠顏色）；成功就是一般的字。 */
function Outcome({ event }: { readonly event: AuditEvent }) {
  const label = AUDIT_OUTCOME_LABELS[event.outcome];
  return event.outcome === 'failed' ? <Badge tone="danger">{label}</Badge> : <>{label}</>;
}

function Address({ value }: { readonly value: string | null }) {
  return value === null ? <EmptyValue label="沒有來源位址" /> : <span className={styles.address}>{value}</span>;
}

function AuditTable({ events }: { readonly events: readonly AuditEvent[] }) {
  return (
    <TableScroll label="稽核紀錄">
      <table className={tableStyles.table}>
        <caption className="visually-hidden">稽核紀錄</caption>
        <thead>
          <tr>
            <th scope="col">時間</th>
            <th scope="col">做的人</th>
            <th scope="col">對象</th>
            <th scope="col">動作</th>
            <th scope="col">結果</th>
            <th scope="col">來源位址</th>
          </tr>
        </thead>
        <tbody>
          {events.map((event) => (
            <tr key={event.eventId}>
              <td className={tableStyles.nowrap}>
                <Time at={event.occurredAt} />
              </td>
              <td>
                <Person person={event.actor} emptyLabel="沒有做的人" />
              </td>
              <td>
                <Person person={event.target} emptyLabel="沒有對象" />
              </td>
              <td className={tableStyles.primaryCell}>{auditActionText(event)}</td>
              <td className={tableStyles.nowrap}>
                <Outcome event={event} />
              </td>
              <td>
                <Address value={event.sourceAddress} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableScroll>
  );
}

/** 窄螢幕：一筆兩行，第一行是動作，第二行是時間、人、結果與來源位址。 */
function AuditList({ events }: { readonly events: readonly AuditEvent[] }) {
  return (
    <List label="稽核紀錄">
      {events.map((event) => (
        <ListRow
          key={event.eventId}
          title={auditActionText(event)}
          meta={[
            { key: 'time', label: '時間', content: <Time at={event.occurredAt} /> },
            { key: 'actor', label: '做的人', content: <Person person={event.actor} emptyLabel="沒有做的人" /> },
            ...(event.target === null ? [] : [{ key: 'target', label: '對象', content: <Person person={event.target} emptyLabel="沒有對象" /> }]),
            { key: 'outcome', label: '結果', content: <Outcome event={event} /> },
            ...(event.sourceAddress === null ? [] : [{ key: 'address', label: '來源位址', content: <Address value={event.sourceAddress} /> }]),
          ]}
        />
      ))}
    </List>
  );
}

/**
 * 人員頁下面的「稽核紀錄」：登入、改密碼、權杖、角色這類帳號安全的事，新的在前，一頁一頁往後載入（資料量大，不一次全載）。
 * 只有組織的擁有者看得到：呼叫端不給別的角色這個區塊，所以其他角色不會發出請求；真正的權限由伺服器檢查。
 */
export function AuditLogSection() {
  const headingId = useId();
  const result = useAuditEvents(true);
  const isCompact = useIsCompact();
  const events = flatten(result.data?.pages ?? []);

  return (
    <section className={styles.section} aria-labelledby={headingId}>
      <h2 id={headingId} className={styles.heading}>
        稽核紀錄
      </h2>
      <p className={styles.note}>只有擁有者看得到，保留最近 400 天。</p>
      {result.isPending && <Skeleton label="正在載入稽核紀錄…" rows={SKELETON_ROWS} />}
      {result.isError && events.length === 0 && (
        <ErrorState
          error={result.error}
          title="無法載入稽核紀錄"
          onRetry={() => {
            void result.refetch();
          }}
        />
      )}
      {result.isSuccess && events.length === 0 && <EmptyState title="還沒有稽核紀錄" />}
      {events.length > 0 && (isCompact ? <AuditList events={events} /> : <AuditTable events={events} />)}
      {events.length > 0 && <Footer result={result} count={events.length} />}
    </section>
  );
}

interface FooterProps {
  readonly result: ReturnType<typeof useAuditEvents>;
  readonly count: number;
}

/** 「載入更多」：載入中用 aria-disabled 而不是 disabled，有焦點的按鈕一停用焦點就掉回頁面本體。 */
function Footer({ result, count }: FooterProps) {
  const isFetching = result.isFetchingNextPage;
  return (
    <div className={styles.footer}>
      <span>已載入 {count} 筆</span>
      {result.hasNextPage && (
        <Button
          size="sm"
          aria-disabled={isFetching || undefined}
          onClick={() => {
            if (!isFetching) void result.fetchNextPage();
          }}
        >
          {isFetching ? '載入中…' : '載入更多'}
        </Button>
      )}
      {result.isFetchNextPageError && <span role="alert">載入更多失敗，請再試一次。</span>}
    </div>
  );
}
