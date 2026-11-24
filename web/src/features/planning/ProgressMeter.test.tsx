import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { NO_PROGRESS } from '../../test/planning';
import { ProgressMeter } from './ProgressMeter';

const NAME = '衝刺 2';

describe('進度', () => {
  it('取消的項目不算進分母：進度條的 max 是全部減取消，value 是完成', () => {
    // Arrange / Act
    render(<ProgressMeter name={NAME} progress={{ ...NO_PROGRESS, total: 10, done: 9, canceled: 1 }} />);

    // Assert
    const bar = screen.getByRole('progressbar', { name: '衝刺 2 的完成進度' });
    expect(bar).toHaveAttribute('max', '9');
    expect(bar).toHaveAttribute('value', '9');
    expect(screen.getByText('完成 9／9・取消 1')).toBeInTheDocument();
  });

  it('沒有取消的項目時文字只有完成數', () => {
    // Arrange / Act
    render(<ProgressMeter name={NAME} progress={{ ...NO_PROGRESS, total: 5, done: 2 }} />);

    // Assert
    expect(screen.getByText('完成 2／5')).toBeInTheDocument();
    expect(screen.queryByText(/取消/)).not.toBeInTheDocument();
  });

  it('估計照舊：完成的估計對全部的估計', () => {
    // Arrange / Act
    render(<ProgressMeter name={NAME} progress={{ total: 4, done: 1, canceled: 1, estimateTotal: 21, estimateDone: 8 }} />);

    // Assert
    expect(screen.getByText('估計 8／21')).toBeInTheDocument();
  });

  it('沒有項目：說還沒有項目，不畫條', () => {
    // Arrange / Act
    render(<ProgressMeter name={NAME} progress={NO_PROGRESS} />);

    // Assert
    expect(screen.getByText('還沒有項目')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('全部都取消了：沒有要做的，說全部取消，不畫條', () => {
    // Arrange / Act
    render(<ProgressMeter name={NAME} progress={{ ...NO_PROGRESS, total: 3, canceled: 3 }} />);

    // Assert
    expect(screen.getByText('全部取消')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });
});
