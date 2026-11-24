import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type SubmitEvent } from 'react';
import { setSpaceArchived, updateSpace, KNOWLEDGE_SPACE_DESCRIPTION_MAX, KNOWLEDGE_SPACE_NAME_MAX } from '../../../api/knowledge';
import type { KnowledgeSpace } from '../../../api/knowledgeTypes';
import { queryKeys } from '../../../api/queryKeys';
import { Button } from '../../../components/Button/Button';
import { ConfirmDialog } from '../../../components/Dialog/ConfirmDialog';
import { FieldShell, TextField, fieldControlClassName } from '../../../components/Field/Field';
import { FormAlert } from '../../../components/FormAlert/FormAlert';
import { PageBar } from '../../../components/PageBar/PageBar';
import { PageLayout } from '../../../components/PageLayout/PageLayout';
import { errorMessage } from '../../../api/http';
import { mapApiErrorToFields, type FieldErrors } from '../../../lib/formErrors';
import { useUnsavedChangesGuard } from '../../../lib/useUnsavedChangesGuard';
import { NotFoundPage } from '../../errors/NotFoundPage';
import { KNOWLEDGE_HOME, knowledgeSpaceUrl } from '../knowledgeLinks';
import { SpaceGate } from '../SpaceGate';
import { validateSpaceDescription, validateSpaceName } from '../spaceForm';
import styles from '../Knowledge.module.css';

type SettingsField = 'name' | 'description';
const SETTINGS_MAPPING = { fields: ['name', 'description'] as const };

function useSpaceMutations(space: KnowledgeSpace) {
  const queryClient = useQueryClient();
  const refresh = (saved: KnowledgeSpace) => {
    queryClient.setQueryData(queryKeys.knowledgeSpace(space.key), saved);
    // 空間底下的樹、頁面（頁面資料裡的 myPermissions 會跟著封存變）都帶著空間的狀態，整個空間一起重抓；空間清單也列著它。
    void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeSpace(space.key) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeSpaces });
  };
  const save = useMutation({
    mutationFn: (settings: { readonly name: string; readonly description: string | null }) => updateSpace(space.key, settings),
    onSuccess: refresh,
  });
  const archive = useMutation({
    mutationFn: (archived: boolean) => setSpaceArchived(space.key, archived),
    onSuccess: refresh,
  });
  return { save, archive };
}

interface GeneralDraft {
  readonly name: string;
  readonly description: string;
}

function useGeneralForm(space: KnowledgeSpace) {
  const { save } = useSpaceMutations(space);
  const [draft, setDraft] = useState<GeneralDraft>({ name: space.name, description: space.description ?? '' });
  const [errors, setErrors] = useState<FieldErrors<SettingsField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [isSaved, setIsSaved] = useState(false);
  const hasChanges = draft.name.trim() !== space.name || draft.description.trim() !== (space.description ?? '');

  function edit(field: SettingsField, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    setIsSaved(false);
    setErrors((current) => ({ ...current, [field]: undefined }));
  }

  function submit() {
    const found = { name: validateSpaceName(draft.name), description: validateSpaceDescription(draft.description) };
    setErrors(found);
    setFormError(null);
    if (found.name !== undefined || found.description !== undefined) return;
    const trimmed = draft.description.trim();
    save.mutate(
      { name: draft.name.trim(), description: trimmed === '' ? null : trimmed },
      {
        onSuccess: (saved) => {
          setDraft({ name: saved.name, description: saved.description ?? '' });
          setIsSaved(true);
        },
        onError: (error) => {
          const mapped = mapApiErrorToFields(error, SETTINGS_MAPPING);
          setErrors(mapped.fieldErrors);
          setFormError(mapped.formError);
        },
      },
    );
  }
  return { draft, errors, formError, isSaved: isSaved && !hasChanges, hasChanges, isPending: save.isPending, edit, submit };
}

function GeneralFields({ form }: { readonly form: ReturnType<typeof useGeneralForm> }) {
  const { draft, errors, edit } = form;
  return (
    <>
      <TextField
        label="名稱"
        value={draft.name}
        maxLength={KNOWLEDGE_SPACE_NAME_MAX}
        autoComplete="off"
        error={errors.name}
        onChange={(event) => { edit('name', event.target.value); }}
      />
      <FieldShell label="簡介" hint="可以不填：這個空間放什麼、給誰看。" error={errors.description}>
        {(control) => (
          <textarea
            id={control.id}
            className={fieldControlClassName('textarea', errors.description !== undefined)}
            rows={3}
            maxLength={KNOWLEDGE_SPACE_DESCRIPTION_MAX}
            value={draft.description}
            aria-invalid={errors.description === undefined ? undefined : true}
            aria-describedby={control.describedBy}
            onChange={(event) => { edit('description', event.target.value); }}
          />
        )}
      </FieldShell>
    </>
  );
}

function SettingsForm({ space }: { readonly space: KnowledgeSpace }) {
  const form = useGeneralForm(space);
  const guard = useUnsavedChangesGuard(form.hasChanges, '離開這一頁的話，名稱與簡介的修改會不見。');
  return (
    <form
      className={styles.section}
      noValidate
      aria-labelledby="space-general-heading"
      onSubmit={(event: SubmitEvent<HTMLFormElement>) => {
        event.preventDefault();
        form.submit();
      }}
    >
      {guard.dialog}
      <h2 id="space-general-heading" className={styles.sectionHeading}>
        名稱與簡介
      </h2>
      <FormAlert message={form.formError} />
      <GeneralFields form={form} />
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={form.isPending || !form.hasChanges}>
          {form.isPending ? '儲存中…' : '儲存'}
        </Button>
        <span role="status" className={styles.saved}>
          {form.isSaved ? '已儲存。' : ''}
        </span>
      </div>
    </form>
  );
}

interface ArchiveConfirmProps {
  readonly space: KnowledgeSpace;
  readonly archive: ReturnType<typeof useSpaceMutations>['archive'];
  readonly onClose: () => void;
}

function ArchiveConfirm({ space, archive, onClose }: ArchiveConfirmProps) {
  return (
    <ConfirmDialog
      title={`封存「${space.name}」`}
      description="封存後只能看，不能新增或修改頁面。之後可以取消封存。"
      confirmLabel="封存空間"
      pendingLabel="封存中…"
      tone="danger"
      isPending={archive.isPending}
      error={archive.isError ? errorMessage(archive.error) : null}
      onConfirm={() => {
        archive.mutate(true, { onSuccess: onClose });
      }}
      onCancel={onClose}
    />
  );
}

function ArchiveSection({ space }: { readonly space: KnowledgeSpace }) {
  const { archive } = useSpaceMutations(space);
  const [isConfirming, setIsConfirming] = useState(false);
  const isArchived = space.archivedAt !== null;
  return (
    <section className={styles.section} aria-labelledby="space-archive-heading">
      <h2 id="space-archive-heading" className={styles.sectionHeading}>
        封存
      </h2>
      <p className={styles.sectionText}>
        {isArchived
          ? '這個空間已經封存，只能看，不能新增或修改頁面。取消封存後可以照常編輯。'
          : '封存後只能看，不能新增或修改頁面；隨時可以取消封存。'}
      </p>
      {archive.isError && !isConfirming && <FormAlert message={errorMessage(archive.error)} />}
      <div className={styles.actions}>
        {isArchived ? (
          <Button disabled={archive.isPending} onClick={() => { archive.mutate(false); }}>
            {archive.isPending ? '取消封存中…' : '取消封存'}
          </Button>
        ) : (
          <Button
            variant="danger"
            onClick={() => {
              archive.reset();
              setIsConfirming(true);
            }}
          >
            封存空間
          </Button>
        )}
      </div>
      {isConfirming && (
        <ArchiveConfirm
          space={space}
          archive={archive}
          onClose={() => {
            setIsConfirming(false);
          }}
        />
      )}
    </section>
  );
}

function Facts({ space }: { readonly space: KnowledgeSpace }) {
  return (
    <dl className={styles.facts}>
      <dt>代號</dt>
      <dd className={styles.mono}>{space.key}</dd>
      <dt>種類</dt>
      <dd>{space.project === null ? '組織空間' : `專案空間：${space.project.key} ${space.project.name}`}</dd>
    </dl>
  );
}

/** 空間設定（/knowledge/:spaceKey/settings）：只有 canManage（組織管理員、專案的 project.manage）。代號與綁的專案建立後不能改。 */
export function KnowledgeSpaceSettingsPage() {
  return (
    <SpaceGate title="空間設定" loadingLabel="正在載入空間設定…">
      {(space, notice) =>
        space.myPermissions.canManage ? (
          <PageLayout
            variant="reading"
            bar={
              <PageBar
                title="空間設定"
                documentTitle={`空間設定 · ${space.name}`}
                crumbs={[
                  { label: '知識庫', to: KNOWLEDGE_HOME },
                  { label: space.name, to: knowledgeSpaceUrl(space.key) },
                ]}
              />
            }
          >
            {notice}
            <div className={styles.settings}>
              <Facts space={space} />
              <SettingsForm key={space.key} space={space} />
              <ArchiveSection space={space} />
            </div>
          </PageLayout>
        ) : (
          <NotFoundPage
            code="403"
            title="沒有權限管理這個空間"
            message="只有組織管理員（專案空間是專案的管理員）能改空間的設定。"
            backTo={knowledgeSpaceUrl(space.key)}
            backLabel="回到空間"
          />
        )
      }
    </SpaceGate>
  );
}
