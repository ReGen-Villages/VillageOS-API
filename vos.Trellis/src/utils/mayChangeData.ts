/** The roles the platform's policy for changing data grants, other than its own services. */
export function mayChangeData(role: string | null): boolean {
  return role === 'admin' || role === 'editor';
}
