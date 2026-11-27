import { render } from '@testing-library/react';
import { createPortal } from 'react-dom';
import { describe, expect, it } from 'vitest';
import { QuickAddSlot, useQuickAddHost } from './QuickAddSlot';

function Harness({ place }: { readonly place: 'top' | 'bottom' }) {
  const host = useQuickAddHost(true);
  if (host === null) return null;
  return (
    <>
      {createPortal(<input aria-label="標題" />, host)}
      {place === 'top' ? (
        <section key="top">
          <QuickAddSlot host={host} />
        </section>
      ) : (
        <article key="bottom">
          <QuickAddSlot host={host} />
        </article>
      )}
    </>
  );
}

describe('快速新增的容器換位置', () => {
  it('換到另一個元件樹的位置之後，焦點仍在原本的標題框上', () => {
    // Arrange
    const { rerender, getByLabelText } = render(<Harness place="top" />);
    const input = getByLabelText('標題');
    input.focus();

    // Act
    rerender(<Harness place="bottom" />);

    // Assert
    expect(input.closest('article')).not.toBeNull();
    expect(document.activeElement).toBe(input);
  });
});
