import { useMutation, useQueryClient, type UseQueryResult } from '@tanstack/react-query';
import { useRef, useState, type RefObject, type SubmitEvent } from 'react';
import { useNavigate } from 'react-router';
import { createProject } from '../../api/projects';
import { useTemplates } from '../../api/queries';
import { queryKeys } from '../../api/queryKeys';
import type { NewProjectRequest, Template } from '../../api/types';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import {
  firstInvalidField,
  hasErrors,
  mapApiErrorToFields,
  type FieldErrors,
} from '../../lib/formErrors';
import { DescriptionFormatField, useDescriptionFormat } from './DescriptionFormatField';
import { ExamplesField, useExamplesChoice } from './ExamplesField';
import { ProjectIdentityFields } from './ProjectIdentityFields';
import {
  PROJECT_ERROR_MAPPING,
  PROJECT_FIELDS,
  validateProject,
  type ProjectField,
} from './projectForm';
import { TemplateField } from './TemplateField';
import styles from './NewProjectDialog.module.css';

type FieldRefs = Readonly<Record<ProjectField, RefObject<HTMLElement | null>>>;

function focusFirstInvalid(refs: FieldRefs, errors: FieldErrors<ProjectField>): void {
  const field = firstInvalidField(PROJECT_FIELDS, errors);
  if (field !== undefined) {
    refs[field].current?.focus();
  }
}

function useCreateProject() {
  const keyRef = useRef<HTMLInputElement>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const templateRef = useRef<HTMLButtonElement>(null);
  const descriptionRef = useRef<HTMLButtonElement>(null);
  const refs: FieldRefs = { key: keyRef, name: nameRef, template: templateRef, description: descriptionRef };
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<ProjectField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const mutation = useMutation({
    mutationFn: (request: NewProjectRequest) => createProject(request),
    onSuccess: async (project) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
      void queryClient.invalidateQueries({ queryKey: queryKeys.overviews });
      await navigate(`/projects/${encodeURIComponent(project.key)}`);
    },
    onError: (error) => {
      const mapped = mapApiErrorToFields(error, PROJECT_ERROR_MAPPING);
      setFieldErrors(mapped.fieldErrors);
      setFormError(mapped.formError);
      focusFirstInvalid(refs, mapped.fieldErrors);
    },
  });

  function submit(request: NewProjectRequest) {
    const errors = validateProject(request);
    setFormError(null);
    setFieldErrors(errors);
    if (hasErrors(errors)) {
      focusFirstInvalid(refs, errors);
      return;
    }
    mutation.mutate(request);
  }

  function clearError(field: ProjectField) {
    setFieldErrors((errors) => ({ ...errors, [field]: undefined }));
  }

  return { submit, clearError, fieldErrors, formError, isPending: mutation.isPending, keyRef, nameRef, templateRef, descriptionRef };
}

interface NewProjectDialogProps {
  readonly onClose: () => void;
}

/** 對話框裡填的內容：代號、名稱、模板（還沒選的時候預設第一個，從清單推出來，不必等載入完再用 effect 補值）與範例的勾選。 */
function useProjectDraft(templates: UseQueryResult<readonly Template[]>, onEdit: (field: ProjectField) => void) {
  const [draft, setDraft] = useState<NewProjectRequest>({ key: '', name: '', template: '' });
  const template = draft.template || (templates.data?.[0]?.key ?? '');
  const examples = useExamplesChoice(templates.data?.find((candidate) => candidate.key === template));
  function update(field: ProjectField, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    onEdit(field);
  }
  // description 是選好的說明格式的內容；沒有（不套用、讀不到）就不送。
  const toRequest = (description: string | undefined): NewProjectRequest => ({
    key: draft.key,
    name: draft.name.trim(),
    template,
    includeExamples: examples.includeExamples,
    description,
  });
  return { draft, template, examples, update, toRequest };
}

/** 組織的擁有者或管理員開新專案：代號、名稱、模板（附預覽）、範例、專案說明的格式。成功後直接進到那個專案。 */
export function NewProjectDialog({ onClose }: NewProjectDialogProps) {
  const templates = useTemplates();
  const format = useDescriptionFormat();
  const create = useCreateProject();
  const { keyRef, nameRef, templateRef, descriptionRef } = create;
  const { draft, template, examples, update, toRequest } = useProjectDraft(templates, create.clearError);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    create.submit(toRequest(format.description));
  }

  // 送出中不讓 Esc 或「取消」關掉，結果才不會沒人看到。
  const close = create.isPending ? () => undefined : onClose;

  return (
    <Dialog title="新增專案" description="從模板開一個專案，你會成為這個專案的管理員。" onClose={close}>
      <form className={styles.form} noValidate onSubmit={handleSubmit}>
        <FormAlert message={create.formError} />
        <ProjectIdentityFields
          keyRef={keyRef}
          nameRef={nameRef}
          projectKey={draft.key}
          name={draft.name}
          errors={create.fieldErrors}
          onChange={update}
        />
        <TemplateField
          ref={templateRef}
          templates={templates}
          value={template}
          error={create.fieldErrors.template}
          onChange={(value) => { update('template', value); }}
        />
        <ExamplesField exampleCount={examples.exampleCount} isChecked={examples.isChecked} onChange={examples.setIsChecked} />
        <DescriptionFormatField
          ref={descriptionRef}
          format={format}
          error={create.fieldErrors.description}
          onChosen={() => { create.clearError('description'); }}
        />
        <DialogFormActions
          submitLabel="建立"
          pendingLabel="建立中…"
          isPending={create.isPending}
          // 說明格式還在讀的時候先不送：送出去才知道預設是哪一份就來不及了。
          canSubmit={templates.isSuccess && !format.templates.isPending}
          onCancel={close}
        />
      </form>
    </Dialog>
  );
}
