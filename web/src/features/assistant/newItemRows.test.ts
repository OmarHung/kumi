import { describe, expect, it } from 'vitest';
import { makeNewItemDraft } from '../../test/assistant';
import { newItemRows } from './newItemRows';

const OUTSIDER = '019b2c3d-0000-7000-8000-000000000001';

describe('newItemRows 的負責人', () => {
  it('負責人是這份建議要先加入的人：寫他的名字並標明會先加入，不說成「不是成員的人」', () => {
    // Arrange
    const draft = makeNewItemDraft({ assigneeId: OUTSIDER });
    const additions = [{ personId: OUTSIDER, role: 'contributor', displayName: 'Ivan', roleName: '執行者' }];

    // Act
    const [row] = newItemRows([draft], undefined, 'ready', {}, additions);

    // Assert
    expect(row?.details).toContain('負責人：Ivan（先加入專案）');
  });

  it('專案還在載入時，要先加入的人照樣寫得出名字', () => {
    // Arrange
    const draft = makeNewItemDraft({ assigneeId: OUTSIDER });
    const additions = [{ personId: OUTSIDER, role: 'contributor', displayName: 'Ivan', roleName: '執行者' }];

    // Act
    const [row] = newItemRows([draft], undefined, 'loading', {}, additions);

    // Assert
    expect(row?.details).toContain('負責人：Ivan（先加入專案）');
  });
});
