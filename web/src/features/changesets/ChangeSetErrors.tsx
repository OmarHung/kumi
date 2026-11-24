import { NotFoundPage } from '../errors/NotFoundPage';
import { projectPageLink } from '../projects/projectViewPreference';
import { changeSetsPath } from '../work-items/itemLinks';

/** 沒有 blueprint.read：看不到設定，也看不到變更集。 */
export function NoPermission({ projectKey }: { readonly projectKey: string }) {
  const message = '你在這個專案的角色不能看設定與變更集。需要的話，請專案的管理員調整你的角色。';
  return <NotFoundPage code="403" title="沒有權限看這個專案的變更集" message={message} backTo={projectPageLink(projectKey)} backLabel="回到專案" />;
}

/** 編號寫錯或這個專案沒有這一份。 */
export function ChangeSetMissing({ projectKey, number }: { readonly projectKey: string; readonly number: string }) {
  return (
    <NotFoundPage
      title="找不到這份變更集"
      message={`這個專案沒有編號是 #${number} 的變更集。`}
      backTo={changeSetsPath(projectKey)}
      backLabel="回到變更集清單"
    />
  );
}
