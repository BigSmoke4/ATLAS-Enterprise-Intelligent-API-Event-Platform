export async function request(url, options = {}) {
  const response = await fetch(url, { headers: { Accept: 'application/json', ...(options.headers ?? {}) }, ...options });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.title || `Request failed (${response.status})`);
  }
  return response.status === 204 ? undefined : response.json();
}
