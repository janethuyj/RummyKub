import { useEffect } from 'react';
import { useStore } from '../store';

/** Transient error/notice banner; auto-dismisses after a few seconds. */
export function ErrorToast() {
  const error = useStore((s) => s.error);
  const clearError = useStore((s) => s.clearError);

  useEffect(() => {
    if (!error) return;
    const t = setTimeout(clearError, 4000);
    return () => clearTimeout(t);
  }, [error, clearError]);

  if (!error) return null;
  return (
    <div className="toast" role="alert" onClick={clearError}>
      {error}
    </div>
  );
}
