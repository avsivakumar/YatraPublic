export function readSaved<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(`yatra:${key}`);
    if (raw === null) return fallback;
    const value: unknown = JSON.parse(raw);
    if (value === null || typeof value !== typeof fallback || Array.isArray(value) !== Array.isArray(fallback)) return fallback;
    return value as T;
  } catch {
    return fallback;
  }
}

export function saveState(key: string, value: unknown): boolean {
  try {
    localStorage.setItem(`yatra:${key}`, JSON.stringify(value));
    return true;
  } catch {
    console.warn('Yatra could not persist browser state.');
    return false;
  }
}
