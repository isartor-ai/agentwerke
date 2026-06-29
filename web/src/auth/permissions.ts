import type { AuthRole, AuthState, AuthUser } from '../types';

export const AUTH_ROLES: AuthRole[] = ['Viewer', 'Operator', 'Approver', 'Admin'];

const ROLE_PRIORITY: AuthRole[] = ['Admin', 'Approver', 'Operator', 'Viewer'];

export function normalizeRoles(values: readonly string[] | undefined): AuthRole[] {
  if (!values) {
    return [];
  }

  const seen = new Set<AuthRole>();
  for (const value of values) {
    const role = AUTH_ROLES.find((candidate) => candidate.toLowerCase() === value.toLowerCase());
    if (role) {
      seen.add(role);
    }
  }

  return AUTH_ROLES.filter((role) => seen.has(role));
}

export function primaryRole(roles: readonly AuthRole[]): AuthRole {
  return ROLE_PRIORITY.find((role) => roles.includes(role)) ?? 'Viewer';
}

export function roleLabel(roles: readonly AuthRole[]): string {
  return roles.length > 0 ? roles.join(', ') : 'No role';
}

export function avatarInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) {
    return 'AU';
  }

  return parts
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
}

export function hasAnyRole(user: AuthUser | undefined, roles: readonly AuthRole[]): boolean {
  if (!user) {
    return false;
  }

  return roles.some((role) => user.roles.includes(role));
}

export function canOperate(auth: AuthState): boolean {
  return hasAnyRole(auth.user, ['Operator', 'Admin']);
}

export function canApprove(auth: AuthState): boolean {
  return hasAnyRole(auth.user, ['Approver', 'Admin']);
}

export function canAdmin(auth: AuthState): boolean {
  return hasAnyRole(auth.user, ['Admin']);
}
