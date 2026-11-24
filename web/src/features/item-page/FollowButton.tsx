import { useId } from 'react';
import { isApiError } from '../../api/http';
import { NOT_MEMBER_CODE, useItemSubscription, useSetSubscription } from '../../api/notificationQueries';
import type { ItemSubscription, SubscriptionReason } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import styles from './FollowButton.module.css';

const REASON_TEXT: Readonly<Record<SubscriptionReason, string>> = {
  manual: '你自己選了關注',
  creator: '你是建立的人，所以自動關注',
  assignee: '你是負責人，所以自動關注',
  commenter: '你留過言，所以自動關注',
  mentioned: '你被提及，所以自動關注',
};

const NOTIFY_TEXT = '這個項目有留言或改了狀態時會通知你';

/** 不是專案成員：只有成員收得到通知（組織的管理員看得到專案，但不是成員）。 */
export const NOT_MEMBER_HINT = '加入專案成員才能關注。';

/** 按鈕的說明：為什麼在關注、關注會怎樣。 */
export function subscriptionDescription(subscription: ItemSubscription): string {
  if (!subscription.canFollow) return NOT_MEMBER_HINT;
  if (!subscription.following) return `關注之後，${NOTIFY_TEXT}。`;
  const reason = subscription.reason === null ? '' : `${REASON_TEXT[subscription.reason]}。`;
  return `${reason}${NOTIFY_TEXT}；再按一下取消關注。`;
}

/** 失敗時說的話：後端說不是成員（409）就照不是成員的說法，其他是一般的失敗。 */
function failureMessage(error: unknown): string {
  return isApiError(error) && error.code === NOT_MEMBER_CODE ? NOT_MEMBER_HINT : '無法更新關注，請再試一次。';
}

interface FollowButtonProps {
  readonly itemKey: string;
  /** 只有圖示（項目面板的標題列很擠，要讓位給項目的編號）；名稱、狀態、說明都一樣。 */
  readonly isCompact?: boolean;
}

/**
 * 項目頁與項目面板標題列的「關注」切換（docs/DESIGN.md 8.3「關注」）：名稱固定是「關注」，有沒有在關注只靠 aria-pressed 說
 * （不是「關注中」：「關注中，已按下」會唸兩次，字變了名稱也跟著變）。按下的樣子是選取的底加實心的鈴鐺，不只靠顏色。
 * 為什麼在關注（負責人、建立的人…）與關注會怎樣放在說明裡（aria-describedby 與 title）。
 * 送出中與不是專案成員（canFollow 為 false）用 aria-disabled（有焦點的按鈕一停用焦點就掉回頁面本體，停用的按鈕也唸不到說明）；
 * 失敗時回到原本的狀態，說一聲。讀不到關注狀態（沒有權限、項目看不到）時不放按鈕：多半是整個項目都看不到，畫面上別的地方已經說了。
 */
export function FollowButton({ itemKey, isCompact = false }: FollowButtonProps) {
  const subscription = useItemSubscription(itemKey);
  const mutation = useSetSubscription(itemKey);
  const descriptionId = useId();
  if (subscription.data === undefined) return null;
  const { following, canFollow } = subscription.data;
  const description = subscriptionDescription(subscription.data);
  const isInert = mutation.isPending || !canFollow;
  const shared = {
    'aria-pressed': following,
    'aria-disabled': isInert || undefined,
    'aria-describedby': descriptionId,
    className: styles.follow,
    onClick: () => {
      if (!isInert) mutation.mutate(!following);
    },
  };
  return (
    <>
      {isCompact ? (
        <IconButton icon="notifications" label="關注" description={description} size="sm" variant="secondary" {...shared} />
      ) : (
        <Button size="sm" variant="secondary" title={description} {...shared}>
          <Icon name="notifications" />
          關注
        </Button>
      )}
      <span id={descriptionId} hidden>
        {description}
      </span>
      {mutation.isError && (
        <span role="alert" className="visually-hidden">
          {failureMessage(mutation.error)}
        </span>
      )}
    </>
  );
}
