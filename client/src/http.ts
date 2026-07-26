/** Parse JSON from a fetch Response; empty/non-JSON bodies become {}. */
export async function readJson<T extends Record<string, unknown> = Record<string, unknown>>(
  res: Response,
): Promise<T> {
  const text = await res.text()
  if (!text.trim()) {
    return {} as T
  }
  try {
    return JSON.parse(text) as T
  } catch {
    return {} as T
  }
}
