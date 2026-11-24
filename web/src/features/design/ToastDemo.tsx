import { useState } from 'react';
import type { Notification } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import { NotificationToasts } from '../notification-toasts/NotificationToasts';
import type { ToastMode } from '../notification-toasts/toastMode';
import { Demo } from './DemoSection';

const MODES = [
  { value: 'full', label: '完整' },
  { value: 'compact', label: '精簡' },
] as const;

function sample(sequence: number): Notification {
  return {
    notificationId: `demo-toast-${String(sequence)}`,
    kind: sequence % 2 === 0 ? 'commented' : 'mentioned',
    projectKey: 'EXA',
    itemKey: `EXA-${String(sequence)}`,
    itemTitle: '修正登入頁',
    actor: { personId: 'demo-person', displayName: '範例成員' },
    fromStateName: null,
    toStateName: null,
    excerpt: '設計稿更新了，請看一下第二頁的按鈕位置。',
    commentId: null,
    createdAt: '2026-11-22T08:00:00Z',
    readAt: null,
  };
}

/** 新通知的提示的範例：按了才跳（真正的提示由即時推送觸發）；在右下角、窄螢幕在下方。 */
export function ToastDemo() {
  const [entries, setEntries] = useState<readonly Notification[]>([]);
  const [count, setCount] = useState(0);
  const [mode, setMode] = useState<Exclude<ToastMode, 'off'>>('full');
  const add = (times: number) => {
    const added = Array.from({ length: times }, (_unused, index) => sample(count + index + 1));
    setCount(count + times);
    setEntries([...entries, ...added]);
  };
  return (
    <Demo name="NotificationToasts">
      <SegmentedControl label="提示的內容" options={MODES} value={mode} onChange={setMode} size="sm" />
      <Button
        size="sm"
        onClick={() => {
          add(1);
        }}
      >
        跳一則提示
      </Button>
      <Button
        size="sm"
        onClick={() => {
          add(5);
        }}
      >
        一次跳五則
      </Button>
      <NotificationToasts
        marksRead={false}
        view={{
          entries,
          mode,
          announcement: { text: '', seq: 0 },
          dismiss: (ids) => {
            setEntries((current) => current.filter((entry) => !ids.includes(entry.notificationId)));
          },
        }}
      />
    </Demo>
  );
}
