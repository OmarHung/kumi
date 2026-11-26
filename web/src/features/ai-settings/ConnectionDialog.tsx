import { useEffect, useRef, useState, type RefObject, type SubmitEvent } from 'react';
import type { AiConnection, AiConnectionScope, AiProviderKind } from '../../api/aiTypes';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { Dialog, DialogActions, DialogFormActions } from '../../components/Dialog/Dialog';
import { TextField, fieldLabelClassName } from '../../components/Field/Field';
import { PasswordField } from '../../components/Field/PasswordField';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import { API_KEY_PAGES, PROVIDER_LABELS } from '../ai/aiLabels';
import { draftFrom, emptyDraft, hasEnteredInput, needsNewApiKey } from './connectionDraft';
import { ModelPicker } from './ModelPicker';
import { useConnectionForm, type ConnectionForm } from './useConnectionForm';
import styles from './AiSettings.module.css';

const KIND_OPTIONS = (Object.keys(PROVIDER_LABELS) as AiProviderKind[]).map((kind) => ({ value: kind, label: PROVIDER_LABELS[kind] }));
const BASE_URL_PLACEHOLDER = 'http://localhost:11434/v1';
const BASE_URL_HINT = '例如自架的 Ollama、vLLM 的網址（到 /v1）。';
const NEW_KEY_NEEDED = '換了供應商或網址，要重新輸入金鑰';

/** 金鑰欄下面的說明：到哪裡拿金鑰；編輯時說沿用或要重新輸入。 */
function apiKeyHint(form: ConnectionForm, stored: AiConnection | null) {
  const { draft } = form;
  const page = API_KEY_PAGES[draft.kind];
  if (needsNewApiKey(draft, stored)) return NEW_KEY_NEEDED;
  const note =
    stored?.hasApiKey === true && stored.apiKeyHint !== null
      ? `已經存了（…${stored.apiKeyHint}），不改就留白`
      : draft.kind === 'openai_compatible'
        ? '沒有就留白'
        : null;
  return (
    <>
      {note !== null && <span>{note}</span>}
      {page !== undefined && (
        <a href={page} target="_blank" rel="noopener noreferrer" className={styles.keyLink}>
          {`到 ${PROVIDER_LABELS[draft.kind]} 取得金鑰`}
        </a>
      )}
    </>
  );
}

function KindPicker({ form }: { readonly form: ConnectionForm }) {
  return (
    <div className={styles.kind}>
      <span className={fieldLabelClassName()} aria-hidden="true">
        供應商
      </span>
      <SegmentedControl label="供應商" options={KIND_OPTIONS} value={form.draft.kind} onChange={form.changeKind} />
    </div>
  );
}

function NameField({ form, nameRef }: { readonly form: ConnectionForm; readonly nameRef: RefObject<HTMLInputElement | null> }) {
  return (
    <>
      <TextField
        ref={nameRef}
        label="名稱"
        autoComplete="off"
        maxLength={100}
        value={form.draft.name}
        error={form.errors.name}
        onChange={(event) => {
          form.changeName(event.target.value);
        }}
      />
    </>
  );
}

/**
 * 新增 OpenRouter 連線時的登入入口：主要的是「用 OpenRouter 登入」（OpenRouter 官方的登入，不必自己去拿金鑰），
 * 下面一行「或貼上 API 金鑰」才展開金鑰欄位與模型。展開之後登入鈕退成次要的，儲存走一般的流程。
 */
function OpenRouterLogin({ form, isExpanded, onExpand, onLogin }: {
  readonly form: ConnectionForm;
  readonly isExpanded: boolean;
  readonly onExpand: () => void;
  readonly onLogin: () => void;
}) {
  return (
    <div className={styles.login}>
      <div className={styles.loginMain}>
        <Button
          variant={isExpanded ? 'secondary' : 'primary'}
          aria-disabled={form.isLoggingIn || undefined}
          onClick={onLogin}
        >
          {form.isLoggingIn ? '前往 OpenRouter…' : '用 OpenRouter 登入'}
        </Button>
        {!isExpanded && (
          <Button variant="ghost" onClick={onExpand}>
            或貼上 API 金鑰
          </Button>
        )}
      </div>
      <FormAlert message={form.loginError} />
    </div>
  );
}

function ProviderFields({
  form,
  stored,
  nameRef,
}: {
  readonly form: ConnectionForm;
  readonly stored: AiConnection | null;
  readonly nameRef: RefObject<HTMLInputElement | null>;
}) {
  const { draft, errors } = form;
  return (
    <>
      <NameField form={form} nameRef={nameRef} />
      {draft.kind === 'openai_compatible' && (
        <TextField
          label="基底網址"
          type="url"
          autoComplete="off"
          spellCheck={false}
          maxLength={500}
          placeholder={BASE_URL_PLACEHOLDER}
          hint={BASE_URL_HINT}
          value={draft.baseUrl}
          error={errors.baseUrl}
          onChange={(event) => {
            form.change({ baseUrl: event.target.value }, 'baseUrl');
          }}
        />
      )}
      <PasswordField
        label="API 金鑰"
        autoComplete="new-password"
        maxLength={512}
        value={draft.apiKey}
        hint={apiKeyHint(form, stored)}
        error={errors.apiKey}
        onChange={(event) => {
          form.change({ apiKey: event.target.value }, 'apiKey');
        }}
      />
    </>
  );
}

interface ConnectionDialogProps {
  /** 打哪一組 API：AI 設定頁是組織的，AI 帳號頁是自己的。 */
  readonly scope: AiConnectionScope;
  /** null 是新增。 */
  readonly connection: AiConnection | null;
  readonly onClose: () => void;
}

/** 對話框裡有沒存的輸入（和打開時的草稿不一樣、或手動加模型的輸入框裡有字）。 */
function useHasUnsavedInput(form: ConnectionForm, stored: AiConnection | null): boolean {
  const [initial] = useState(() => JSON.stringify(stored === null ? emptyDraft() : draftFrom(stored)));
  return JSON.stringify(form.draft) !== initial || form.customId !== '';
}

/** 新增時第一個要填的是名稱（打開時預設落在供應商的第一顆按鈕，那個通常不用動）；送出後有問題，焦點到第一個標了無效的欄位。 */
function useDialogFocus(form: ConnectionForm, nameRef: RefObject<HTMLInputElement | null>, isNew: boolean) {
  const { formRef, focusCount } = form;
  useEffect(() => {
    if (isNew) nameRef.current?.focus();
  }, [isNew, nameRef]);
  useEffect(() => {
    if (focusCount > 0) formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [focusCount, formRef]);
}

function ConfirmLoginDialog({ onConfirm, onCancel }: { readonly onConfirm: () => void; readonly onCancel: () => void }) {
  return (
    <ConfirmDialog
      title="先離開去登入 OpenRouter？"
      description="還沒存的金鑰與勾選的模型會不見；登入回來後會是一個新的連線。"
      confirmLabel="繼續登入"
      cancelLabel="回來編輯"
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}

function ConfirmCloseDialog({ onConfirm, onCancel }: { readonly onConfirm: () => void; readonly onCancel: () => void }) {
  return (
    <ConfirmDialog
      title="放棄還沒存的輸入？"
      description="名稱、金鑰與勾選的模型還沒存，關掉就會不見。"
      confirmLabel="放棄並關閉"
      cancelLabel="繼續編輯"
      tone="danger"
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}

/** 只有登入時沒有「儲存」（登入是上面那顆主要按鈕），只留「取消」。 */
function DialogFooter({ isLoginOnly, form, onCancel }: { readonly isLoginOnly: boolean; readonly form: ConnectionForm; readonly onCancel: () => void }) {
  if (!isLoginOnly) {
    return <DialogFormActions submitLabel="儲存" pendingLabel="儲存中…" isPending={form.isSaving} keepsFocusWhilePending onCancel={onCancel} />;
  }
  return (
    <DialogActions>
      <Button onClick={onCancel} disabled={form.isSaving || form.isLoggingIn}>
        取消
      </Button>
    </DialogActions>
  );
}

type Confirmation = 'close' | 'login' | null;

function PendingConfirmation({ confirming, onLogin, onClose, onDismiss }: {
  readonly confirming: Confirmation;
  readonly onLogin: () => void;
  readonly onClose: () => void;
  readonly onDismiss: () => void;
}) {
  if (confirming === 'login') return <ConfirmLoginDialog onConfirm={onLogin} onCancel={onDismiss} />;
  if (confirming === 'close') return <ConfirmCloseDialog onConfirm={onClose} onCancel={onDismiss} />;
  return null;
}

/** 組織與自己的區塊都叫「…的 API 金鑰」，按鈕與對話框的標題照它（原本組織的叫「連線」，使用者分不出是金鑰還是訂閱）。 */
function dialogTitle(connection: AiConnection | null): string {
  return connection === null ? '新增 API 金鑰' : '編輯 API 金鑰';
}

/** 關閉與登入前的確認：打了還沒存的輸入會不見時先問（登入會整頁離開，回來是一個新的連線）。 */
function useDialogRequests({ form, isLoginOnly, hasUnsavedInput, onClose }: {
  readonly form: ConnectionForm;
  readonly isLoginOnly: boolean;
  readonly hasUnsavedInput: boolean;
  readonly onClose: () => void;
}) {
  const [confirming, setConfirming] = useState<Confirmation>(null);
  return {
    confirming,
    setConfirming,
    requestClose: () => {
      if (hasUnsavedInput) setConfirming('close');
      else onClose();
    },
    requestLogin: () => {
      if (hasEnteredInput(form.draft, form.customId, !isLoginOnly)) setConfirming('login');
      else form.startLogin();
    },
  };
}

/** 新增、編輯連線：供應商、名稱、（相容端點的）基底網址、金鑰、模型。422 的細項在欄位下面，供應商的失敗在按鈕上面。 */
export function ConnectionDialog({ scope, connection, onClose }: ConnectionDialogProps) {
  const form = useConnectionForm(scope, connection, onClose);
  const { formRef } = form;
  const nameRef = useRef<HTMLInputElement>(null);
  // 新增 OpenRouter 連線：預設只有登入；按「或貼上 API 金鑰」才展開名稱、金鑰與模型。
  const [isKeyExpanded, setIsKeyExpanded] = useState(false);
  const canLogin = connection === null && form.draft.kind === 'openrouter';
  const isLoginOnly = canLogin && !isKeyExpanded;
  const isBusy = form.isSaving || form.isLoggingIn;
  const hasEditedDraft = useHasUnsavedInput(form, connection);
  // 只有登入時看不到名稱與金鑰，換了供應商不算有東西會不見。
  const hasUnsavedInput = isLoginOnly ? hasEnteredInput(form.draft, form.customId, false) : hasEditedDraft;
  useDialogFocus(form, nameRef, connection === null);

  const { requestClose, requestLogin, confirming, setConfirming } = useDialogRequests({ form, isLoginOnly, hasUnsavedInput, onClose });

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isLoginOnly) requestLogin();
    else form.save();
  }

  return (
    <>
      <Dialog title={dialogTitle(connection)} size="lg" onClose={isBusy ? () => undefined : requestClose}>
        <form ref={formRef} className={styles.dialogForm} noValidate aria-busy={isBusy} onSubmit={handleSubmit}>
          <KindPicker form={form} />
          {canLogin && <OpenRouterLogin form={form} isExpanded={isKeyExpanded} onExpand={() => { setIsKeyExpanded(true); }} onLogin={requestLogin} />}
          {!isLoginOnly && <ProviderFields form={form} stored={connection} nameRef={nameRef} />}
          {!isLoginOnly && <ModelPicker form={form} />}
          <FormAlert message={form.formError} />
          <DialogFooter isLoginOnly={isLoginOnly} form={form} onCancel={requestClose} />
        </form>
      </Dialog>
      <PendingConfirmation
        confirming={confirming}
        onLogin={() => {
          setConfirming(null);
          form.startLogin();
        }}
        onClose={onClose}
        onDismiss={() => { setConfirming(null); }}
      />
    </>
  );
}
