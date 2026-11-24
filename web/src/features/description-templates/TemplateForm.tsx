import type { SubmitEvent } from 'react';
import type { DescriptionTemplateInput } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { cx } from '../../lib/cx';
import fill from '../project-info/editorFill.module.css';
import { TemplateInputs } from './TemplateInputs';
import { useTemplateForm } from './useTemplateForm';
import styles from './TemplateForm.module.css';

interface TemplateFormProps {
  /** 修改時是那一份的編號；新增時沒有。 */
  readonly id?: string;
  readonly initial: DescriptionTemplateInput;
}

/** 新增或修改一份說明格式：名稱、設成預設、內容（填滿畫面高度）、固定的提醒、儲存與取消。 */
export function TemplateForm({ id, initial }: TemplateFormProps) {
  const form = useTemplateForm(initial, id);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    form.save();
  }

  return (
    <form className={cx(styles.form, fill.fill)} noValidate aria-busy={form.isSaving} onSubmit={handleSubmit}>
      <TemplateInputs form={form} />
      <p className={styles.reminder}>不要貼密碼或金鑰，寫密碼管理器裡的項目名稱就好。</p>
      <div className={styles.actions}>
        {/* 送出後才出現的訊息放在按鈕旁邊，不把按鈕推出畫面。 */}
        <div className={styles.alert}>
          <FormAlert message={form.formError} />
        </div>
        <Button disabled={form.isSaving} onClick={form.cancel}>
          取消
        </Button>
        {/* 送出中不停用、改標 aria-disabled：焦點常常就在這顆按鈕上，停用會讓焦點掉到頁面本體（和 DialogFormActions 一樣）。 */}
        <Button variant="primary" type="submit" aria-disabled={form.isSaving || undefined}>
          {form.isSaving ? '儲存中…' : '儲存'}
        </Button>
      </div>
      {form.guardDialog}
    </form>
  );
}
