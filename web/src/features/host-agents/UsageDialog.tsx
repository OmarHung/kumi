import type { HostSource, HostSourceUsage } from '../../api/hostAgentTypes';
import { useHostSourceUsage } from '../../api/hostAgents';
import { Button } from '../../components/Button/Button';
import { Dialog, DialogActions } from '../../components/Dialog/Dialog';
import { List, ListRow } from '../../components/List/List';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { formatCount, outcomeSummary, usageSummary } from './hostAgentText';
import styles from './HostAgents.module.css';

const FOOTNOTE = 'Kumi 記錄的用量，不是供應商的帳單或剩餘額度。';

function tokensText(inputTokens: number, outputTokens: number): string {
  return `輸入 ${formatCount(inputTokens)} · 輸出 ${formatCount(outputTokens)} token`;
}

function UsageGroups({ data }: { readonly data: HostSourceUsage }) {
  return (
    <>
      <div className={styles.usageGroup}>
        <h3 className={styles.subheading}>依成員</h3>
        {data.people.length === 0 ? (
          <p className={styles.note}>這個月還沒有人用。</p>
        ) : (
          <List label="依成員的用量">
            {data.people.map((entry) => (
              <ListRow
                key={entry.person.personId}
                title={entry.person.displayName}
                meta={[
                  { key: 'outcome', label: '則數', content: outcomeSummary(entry.totals) },
                  { key: 'tokens', label: 'token', content: tokensText(entry.totals.inputTokens, entry.totals.outputTokens) },
                ]}
              />
            ))}
          </List>
        )}
      </div>
      <div className={styles.usageGroup}>
        <h3 className={styles.subheading}>依專案</h3>
        {data.projects.length === 0 ? (
          <p className={styles.note}>這個月還沒有專案用。</p>
        ) : (
          <List label="依專案的用量">
            {data.projects.map((entry) => (
              <ListRow
                key={entry.projectKey}
                code={entry.projectKey}
                title={outcomeSummary(entry.totals)}
                meta={[{ key: 'tokens', label: 'token', content: tokensText(entry.totals.inputTokens, entry.totals.outputTokens) }]}
              />
            ))}
          </List>
        )}
      </div>
    </>
  );
}

/** 這個月依成員、依專案的用量：窄螢幕也讀得下的清單（每筆兩行），不用表格。 */
export function UsageDialog({ source, onClose }: { readonly source: HostSource; readonly onClose: () => void }) {
  const usage = useHostSourceUsage(source.id, null);
  const data = usage.data;
  return (
    <Dialog title={`「${source.name}」的用量`} description={data === undefined ? undefined : `${data.month} · ${usageSummary(data.totals)}`} size="lg" onClose={onClose}>
      <div className={styles.dialogForm}>
        {usage.isPending && <Skeleton label="正在載入用量…" rows={2} />}
        {usage.isError && (
          <ErrorState
            error={usage.error}
            title="用量載入失敗"
            onRetry={() => {
              void usage.refetch();
            }}
          />
        )}
        {data !== undefined && <UsageGroups data={data} />}
        <p className={styles.note}>{FOOTNOTE}</p>
        <DialogActions>
          <Button onClick={onClose}>關閉</Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
