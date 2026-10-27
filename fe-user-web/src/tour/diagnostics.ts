/** Development console only; no content, credentials, or URL query strings. */
export function tourDiagnostic(event: string, data: Record<string, unknown> = {}) {
  if (import.meta.env?.DEV) console.debug('[tour]', JSON.stringify({ event, at: performance.now(), ...data }));
}
