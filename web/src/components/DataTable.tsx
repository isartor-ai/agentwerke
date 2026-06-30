import type { KeyboardEvent, ReactNode } from 'react';

export interface DataTableColumn<T> {
  key: string;
  label: string;
  render: (row: T) => ReactNode;
}

interface DataTableProps<T> {
  caption: string;
  columns: DataTableColumn<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  onRowClick?: (row: T) => void;
  rowAriaLabel?: (row: T) => string;
  className?: string;
}

export function DataTable<T>({
  caption,
  columns,
  rows,
  rowKey,
  onRowClick,
  rowAriaLabel,
  className,
}: DataTableProps<T>) {
  const handleKeyDown = (event: KeyboardEvent<HTMLTableRowElement>, row: T) => {
    if (!onRowClick) {
      return;
    }

    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onRowClick(row);
    }
  };

  return (
    <div className={`table-wrap ${className ?? ''}`.trim()}>
      <table>
        <caption>{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.key} scope="col">
                {column.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const key = rowKey(row);
            return (
              <tr
                key={key}
                tabIndex={onRowClick ? 0 : undefined}
                aria-label={rowAriaLabel?.(row)}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                onKeyDown={(event) => handleKeyDown(event, row)}
                className={onRowClick ? 'row-interactive' : undefined}
              >
                {columns.map((column) => (
                  <td key={`${key}-${column.key}`}>{column.render(row)}</td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
