import { useCallback, useState } from 'react';
import type { ToastMessage } from './ToastRegion';

let nextToastId = 0;

export function useToastQueue() {
  const [toasts, setToasts] = useState<ToastMessage[]>([]);

  const pushToast = useCallback((toast: Omit<ToastMessage, 'id'>) => {
    nextToastId += 1;
    const id = `toast-${nextToastId}`;
    setToasts((current) => [...current, { ...toast, id }]);
    return id;
  }, []);

  const dismissToast = useCallback((id: string) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  return { toasts, pushToast, dismissToast };
}
