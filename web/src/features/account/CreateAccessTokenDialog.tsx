import { useState } from 'react';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { CreateTokenFields } from './CreateTokenFields';
import { useCreateTokenForm } from './createAccessTokenForm';
import { TokenSecretView } from './TokenSecretView';
import styles from './AccessTokens.module.css';

/**
 * 「建立存取權杖」：填完送出之後，同一個對話框換成「只會顯示這一次」的畫面（不另開一個：關上才算看過）。
 * 明文放在這個元件的 state，關上對話框就卸下。送出中 Esc 與「取消」都不作用，送出鈕只標 aria-disabled，
 * 焦點留在上面；失敗時對話框留著，欄位的問題標在欄位上、其他的寫在表單上方。
 */
export function CreateAccessTokenDialog({ onClose }: { readonly onClose: () => void }) {
  const [secret, setSecret] = useState<string | null>(null);
  const form = useCreateTokenForm(setSecret);
  const close = form.isPending ? () => undefined : onClose;
  if (secret !== null) {
    return (
      <Dialog title="存取權杖已建立" description="複製之後再關上這個視窗。" onClose={onClose}>
        <TokenSecretView secret={secret} onDone={onClose} />
      </Dialog>
    );
  }
  return (
    <Dialog title="建立存取權杖" description="給 CLI 與腳本用，不必用你的密碼登入。" onClose={close}>
      <form className={styles.form} noValidate onSubmit={form.handleSubmit}>
        <CreateTokenFields form={form} />
        <DialogFormActions submitLabel="建立" pendingLabel="建立中…" isPending={form.isPending} keepsFocusWhilePending onCancel={close} />
      </form>
    </Dialog>
  );
}
