import type { ReactNode } from 'react';

interface ToolbarProps {
  children: ReactNode;
  className?: string;
}

export function Toolbar({ children, className }: ToolbarProps) {
  return <div className={`toolbar ${className ?? ''}`.trim()}>{children}</div>;
}
