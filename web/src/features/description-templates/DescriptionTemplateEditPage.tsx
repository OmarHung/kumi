import { useState, type ReactNode } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { useDescriptionTemplates } from '../../api/projectDescription';
import type { DescriptionTemplate, DescriptionTemplateInput } from '../../api/types';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { buttonClassName } from '../../components/Button/Button';
import { PageBar, type Crumb } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { NoPermission } from '../../components/StatusViews/NoPermission';
import { EmptyState, ErrorState, LoadingState } from '../../components/StatusViews/StatusViews';
import { TemplateForm } from './TemplateForm';
import { copyName } from './templateRules';
import { TEMPLATES_PATH } from './templatePaths';

const BLANK: DescriptionTemplateInput = { name: '', body: '', isDefault: false };

/*
 * 位置是「設定 › 說明格式」。「設定」不給連結：設定中心沒有自己的首頁（側欄的「設定」連到的是第一頁「我的帳號」），
 * 麵包屑的每一段都該是名字對得上的一頁，連到「我的帳號」會讓人意外；要回設定中心，從「說明格式」那一段回清單，
 * 清單頁就在設定中心裡。和設定中心各頁頁列上沒有連結的「設定」同一個寫法。
 */
const CRUMBS: readonly Crumb[] = [{ label: '設定' }, { label: '說明格式', to: TEMPLATES_PATH }];

function Frame({ title, children }: { readonly title: string; readonly children: ReactNode }) {
  return (
    <PageLayout variant="reading" bar={<PageBar title={title} crumbs={CRUMBS} />}>
      {children}
    </PageLayout>
  );
}

function NotFound() {
  const back = (
    <Link to={TEMPLATES_PATH} className={buttonClassName('secondary')}>
      回到說明格式
    </Link>
  );
  return (
    <EmptyState title="找不到這個說明格式" action={back}>
      它可能已經被刪掉，或是內建的格式（內建的不能改，可以複製一份來改）。
    </EmptyState>
  );
}

function inputOf(template: DescriptionTemplate): DescriptionTemplateInput {
  return { name: template.name, body: template.body, isDefault: template.isDefault };
}

/** 修改：內建的與不存在的編號都是「找不到」（後端也是 404）。 */
function ExistingTemplate({ id, list }: { readonly id: string; readonly list: readonly DescriptionTemplate[] }) {
  // 表單掛上時找一次：之後清單重抓（例如儲存失敗後），別人刪掉了它也不能把表單換成「找不到」，打到一半的內容要留著。
  const [found] = useState(() => list.find((template) => template.id === id && !template.isBuiltIn));
  return found === undefined ? <NotFound /> : <TemplateForm key={found.id} id={found.id} initial={inputOf(found)} />;
}

/** 新增：有 from 就帶入那一份的名稱與內容（複製內建的，不是預設）；找不到那一份就從空白開始。 */
function NewTemplate({ from, list }: { readonly from: string | null; readonly list: readonly DescriptionTemplate[] }) {
  const source = from === null ? undefined : list.find((template) => template.id === from);
  const initial = source === undefined ? BLANK : { name: copyName(source.name), body: source.body, isDefault: false };
  return <TemplateForm key={source?.id ?? 'blank'} initial={initial} />;
}

/**
 * 新增（/description-templates/new，`?from=<編號>` 複製一份）與修改（/description-templates/:id）。
 * 沒有單一格式的端點，從清單裡找；空白的新增不必等清單。
 */
export function DescriptionTemplateEditPage() {
  const user = useCurrentUser();
  const { id } = useParams();
  const from = useSearchParams()[0].get('from');
  const templates = useDescriptionTemplates();
  const title = id === undefined ? '新增格式' : '編輯格式';
  const needsList = id !== undefined || from !== null;
  if (!isOrganizationAdmin(user)) {
    return <Frame title={title}><NoPermission message="只有組織的擁有者與管理員可以管理說明格式。" /></Frame>;
  }
  if (needsList && templates.isPending) return <Frame title={title}><LoadingState label="正在載入說明格式…" /></Frame>;
  if (needsList && templates.isLoadingError) {
    return (
      <Frame title={title}>
        <ErrorState error={templates.error} title="說明格式載入失敗" onRetry={() => { void templates.refetch(); }} />
      </Frame>
    );
  }
  const list = templates.data ?? [];
  return <Frame title={title}>{id === undefined ? <NewTemplate from={from} list={list} /> : <ExistingTemplate key={id} id={id} list={list} />}</Frame>;
}
