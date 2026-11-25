import { useId, useState } from 'react';
import { useProject } from '../../api/queries';
import { Button } from '../../components/Button/Button';
import { Dialog, DialogActions } from '../../components/Dialog/Dialog';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { AddMemberForm } from './AddMemberForm';
import { PRIVILEGED_PERMISSIONS_NOTE } from './memberRoles';
import { MemberRoster } from './MemberRoster';
import styles from './MembersDialog.module.css';

interface MembersDialogProps {
  readonly projectKey: string;
  readonly onClose: () => void;
}

/** 載入中的骨架：大約是一個小專案的成員數。 */
const SKELETON_ROWS = 3;

/**
 * 專案的成員與角色。有 project.manage 的人（組織管理員也有）可以改角色、加成員、移除成員；改完會重新讀專案內容，
 * 因為我的角色與能做的事（myPermissions）可能跟著變。裡面有表格，用寬的對話框（size="lg"）。
 */
export function MembersDialog({ projectKey, onClose }: MembersDialogProps) {
  const project = useProject(projectKey);
  const closeId = useId();
  const [message, setMessage] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const detail = project.data;
  const canManage = detail?.myPermissions.includes('project.manage') ?? false;

  const feedback = {
    onSaved: (text: string) => {
      setFailure(null);
      setMessage(text);
    },
    onFailed: (text: string) => {
      setMessage(null);
      setFailure(text);
    },
  };

  const description =
    detail === undefined
      ? undefined
      : `${String(detail.members.length)} 位成員。改狀態的角色限制看的是這裡的角色，組織管理員也一樣。`;

  return (
    <Dialog title="成員" description={description} size="lg" onClose={onClose}>
      <div className={styles.body}>
        {detail === undefined ? (
          <Skeleton label="正在載入成員…" rows={SKELETON_ROWS} />
        ) : (
          <>
            <FormAlert message={failure} />
            <MemberRoster detail={detail} canManage={canManage} fallbackFocusId={closeId} {...feedback} />
            {canManage && <Notice tone="info" role="none">{PRIVILEGED_PERMISSIONS_NOTE}</Notice>}
            {canManage && <AddMemberForm detail={detail} onSaved={feedback.onSaved} />}
          </>
        )}
        {/* 一直掛著（沒有訊息時看不見）：之後才填進來的訊息，報讀軟體才唸得到。 */}
        <Notice tone="success" role="status">
          {message}
        </Notice>
      </div>
      <DialogActions>
        <Button id={closeId} onClick={onClose}>
          關閉
        </Button>
      </DialogActions>
    </Dialog>
  );
}
