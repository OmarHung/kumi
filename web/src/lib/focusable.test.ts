import { describe, expect, it } from 'vitest';
import { focusableIn, trapStopsIn, TRANSIENT_FOCUS_ATTRIBUTE } from './focusable';

function containerOf(html: string): HTMLElement {
  const container = document.createElement('div');
  container.innerHTML = html;
  return container;
}

describe('focusableIn', () => {
  it('照文件的順序，tabindex 是負的不算', () => {
    // Arrange
    const container = containerOf('<button id="a">a</button><div tabindex="-1" id="b"></div><a href="#" id="c">c</a>');

    // Act
    const ids = focusableIn(container).map((element) => element.id);

    // Assert
    expect(ids).toEqual(['a', 'c']);
  });

  it('hidden 底下的（藏著的選檔案 input）不算，才不會變成「最後一個」', () => {
    // Arrange
    const container = containerOf('<button id="a">a</button><input hidden type="file" id="file"><div hidden><button id="b">b</button></div>');

    // Act
    const ids = focusableIn(container).map((element) => element.id);

    // Assert
    expect(ids).toEqual(['a']);
  });

  it('trapStopsIn 另外不算只是暫時有焦點的元素', () => {
    // Arrange
    const container = containerOf(`<button id="a">a</button><div ${TRANSIENT_FOCUS_ATTRIBUTE}><input id="filter"></div>`);

    // Act
    const ids = trapStopsIn(container).map((element) => element.id);

    // Assert
    expect(ids).toEqual(['a']);
  });
});
