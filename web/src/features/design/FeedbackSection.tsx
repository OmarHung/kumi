import { useState } from 'react';
import { Avatar } from '../../components/Avatar/Avatar';
import { Badge } from '../../components/Badge/Badge';
import { Button } from '../../components/Button/Button';
import { CountBadge } from '../../components/CountBadge/CountBadge';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ArrivalNotice, EmptyState, ErrorState, LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { NOT_A_MEMBER } from '../../domain/fields';
import { StateBadge } from '../work-items/ItemCells';
import { EveryoneMark, NobodyMark, PersonMark } from '../work-items/selectOptionMarks';
import { Demo, DemoSection } from './DemoSection';
import { ToastDemo } from './ToastDemo';
import styles from './Design.module.css';

const STALE_QUERY = { isRefetchError: true, refetch: () => Promise.resolve() };

// 六個名稱剛好各是一種色調（Avatar.tsx 的 avatarToneOf：1～6 依序），淺色與深色一次看完六種。
const TONE_NAMES = ['範例設計師', '範例成員', '範例管理員', '範例助理', '範例使用者', '範例工程師'];

function NoticeDemos() {
  const [isDismissed, setIsDismissed] = useState(false);
  return (
    <Demo name="Notice">
      <Notice tone="info" role="none">
        有限制轉換的流程：只有勾起來的轉換可以走。
      </Notice>
      <Notice tone="success" role="none">
        已套用變更集 #3，藍圖是第 2 版。
      </Notice>
      <Notice tone="warning" role="none" title="別人已經套用了新版本" actions={<Button size="sm">以最新版本重新開始</Button>}>
        設定在你編輯時已經更新為第 5 版。
      </Notice>
      {!isDismissed && (
        <Notice
          tone="danger"
          role="none"
          dismissLabel="知道了（AGY-4）"
          onDismiss={() => {
            setIsDismissed(true);
          }}
        >
          AGY-4「驗收中的需求」沒有移到「結案」：需要的角色是專案經理。
        </Notice>
      )}
    </Demo>
  );
}

function StatusDemos() {
  return (
    <>
      <Demo name="FormAlert／ErrorState">
        <FormAlert message="Email 或密碼不對。" />
        <ErrorState title="專案載入失敗" error={new Error('連不上伺服器，請稍後再試。')} onRetry={() => undefined} />
      </Demo>
      <Demo name="RefetchErrorNotice／ArrivalNotice">
        <RefetchErrorNotice query={STALE_QUERY} message="無法更新專案的最新資料，畫面上可能是舊的。" />
        <ArrivalNotice message="已刪除 AGY-7「首頁」。" />
      </Demo>
      <Demo name="LoadingState／Skeleton／EmptyState">
        <LoadingState label="正在載入設定…" />
        <Skeleton label="正在載入項目…" rows={4} />
        <Skeleton label="正在載入說明…" variant="text" />
        <EmptyState title="還沒有專案" action={<Button variant="primary">新增專案</Button>}>
          專案會列在這裡，每個專案照自己的流程與欄位運作。
        </EmptyState>
      </Demo>
    </>
  );
}

export function FeedbackSection() {
  return (
    <DemoSection title="提示與狀態">
      <NoticeDemos />
      <ToastDemo />
      <StatusDemos />
    </DemoSection>
  );
}

export function LabelsSection() {
  return (
    <DemoSection title="標籤與頭像">
      <Demo name="Badge">
        <Badge>內建</Badge>
        <Badge tone="accent">新增</Badge>
        <Badge tone="success">可以套用</Badge>
        <Badge tone="warning">已修改</Badge>
        <Badge tone="danger">已過期</Badge>
        <Badge tone="warning" icon="warning">
          擋住套用
        </Badge>
      </Demo>
      <Demo name="Badge（藍圖選項的顏色）">
        <Badge tone="blue">藍色的選項</Badge>
        <Badge tone="purple">紫色的選項</Badge>
      </Demo>
      <Demo name="StateBadge">
        <StateBadge category="backlog" label="待規劃" />
        <StateBadge category="not_started" label="待辦" />
        <StateBadge category="in_progress" label="開發" />
        <StateBadge category="done" label="結案" />
        <StateBadge category="canceled" label="取消" />
      </Demo>
      <Demo name="CountBadge">
        <CountBadge value={8} />
        <CountBadge value={2} tone="danger" />
      </Demo>
      <Demo name="Avatar">
        <Avatar name="範例使用者" />
        <Avatar name="Example User" />
        <Avatar name="範例成員" size="sm" />
        <span>
          <Avatar name="範例成員" size="sm" decorative /> 範例成員
        </span>
      </Demo>
      <Demo name="Avatar 的色調">
        {TONE_NAMES.map((name) => (
          <Avatar key={name} name={name} />
        ))}
        <span className={styles.note}>照名稱固定一種（六種）；不是某一個人的佔位維持中性：</span>
        <PersonMark name={NOT_A_MEMBER} />
        <EveryoneMark />
        <NobodyMark />
      </Demo>
    </DemoSection>
  );
}
