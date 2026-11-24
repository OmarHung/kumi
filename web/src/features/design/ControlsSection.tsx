import { Link } from 'react-router';
import { Button, buttonClassName } from '../../components/Button/Button';
import { CheckboxField, CheckboxGroup } from '../../components/CheckboxField/CheckboxField';
import { TextField } from '../../components/Field/Field';
import { PasswordField } from '../../components/Field/PasswordField';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { DateDemos } from './DateDemos';
import { Demo, DemoSection } from './DemoSection';
import { SelectDemos } from './SelectDemos';
import styles from './Design.module.css';

function SmallButtonDemos() {
  return (
    <>
      <Demo name="Button size=sm">
        <Button size="sm" variant="primary">
          新增
        </Button>
        <Button size="sm">
          <Icon name="add" />
          新增項目
        </Button>
        <Button size="sm" variant="ghost">
          載入更多
        </Button>
        <Button size="sm" variant="danger">
          移除
        </Button>
      </Demo>
      <Demo name="buttonClassName（連結）">
        <Link to="/design" className={buttonClassName('secondary')}>
          回到專案清單
        </Link>
        <Link to="/design" className={buttonClassName('ghost', 'sm')}>
          所有變更集
        </Link>
      </Demo>
    </>
  );
}

function ButtonDemos() {
  return (
    <>
      <Demo name="Button">
        <Button variant="primary">預覽變更</Button>
        <Button>取消</Button>
        <Button variant="ghost">清除條件</Button>
        <Button variant="danger">刪除</Button>
        <Button variant="primary" disabled>
          送出中…
        </Button>
        <Button aria-disabled="true" aria-describedby="design-blocked">
          改類型
        </Button>
        <span id="design-blocked" className={styles.note}>
          停用但停得到：原因用 aria-describedby 連過去。
        </span>
      </Demo>
      <SmallButtonDemos />
      <Demo name="IconButton">
        <IconButton icon="close" label="關閉" />
        <IconButton icon="more" label="更多" variant="secondary" />
        <IconButton icon="arrowUp" label="把需求確認往上移" size="sm" />
        <IconButton icon="arrowDown" label="把需求確認往下移" size="sm" disabled />
        <IconButton icon="sidebar" label="收合側欄" />
      </Demo>
    </>
  );
}

/** 勾選框的每一種狀態（docs/DESIGN.md 7.3：自己畫的方框與勾；元素仍然是原生的勾選框）。 */
function CheckboxDemos() {
  return (
    <>
      <Demo name="CheckboxField">
        <CheckboxField label="限制轉換" hint="只有勾起來的轉換可以走。" defaultChecked />
        <CheckboxField label="必填" />
        <CheckboxField label="已封存的欄位" disabled />
        <CheckboxField label="內建的欄位" disabled defaultChecked />
        <CheckboxField label="有問題的轉換" aria-invalid defaultChecked />
        <CheckboxField label="要先填（有問題）" aria-invalid />
      </Demo>
      <Demo name="CheckboxGroup">
        <CheckboxGroup legend="套用到" error="至少選一個類型。">
          <CheckboxField label="需求" defaultChecked />
          <CheckboxField label="任務" />
          <CheckboxField label="需求變更單" />
        </CheckboxGroup>
      </Demo>
    </>
  );
}

function FieldDemos() {
  return (
    <>
      <Demo name="TextField">
        <TextField label="標題" placeholder="例如：首頁設計稿" />
        <TextField label="key" hint="小寫英文開頭，2 到 40 個字。" defaultValue="code_review" />
        <TextField label="名稱" error="名稱必填。" />
        <TextField label="標題" size="sm" isLabelHidden isErrorInside error="請輸入標題。" placeholder="新增項目：輸入標題後按 Enter" />
        <TextField label="專案代號" hint="建立之後不能改。" defaultValue="AGY" disabled />
      </Demo>
      <Demo name="PasswordField">
        <PasswordField label="新密碼" hint="至少 10 個字元" defaultValue="example-pass" autoComplete="off" />
        <PasswordField label="目前的密碼" error="目前的密碼不正確。" defaultValue="wrong-pass" autoComplete="off" />
        <PasswordField label="初始密碼" defaultValue="example-pass" autoComplete="off" disabled />
      </Demo>
      <DateDemos />
      <SelectDemos />
      <CheckboxDemos />
    </>
  );
}

export function ControlsSection() {
  return (
    <DemoSection title="按鈕與欄位">
      <ButtonDemos />
      <FieldDemos />
    </DemoSection>
  );
}
