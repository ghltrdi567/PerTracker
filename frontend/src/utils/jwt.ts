// Декодирование JWT для извлечения информации о пользователе.
// JWT содержит claims: sub (userId), email, display_name

interface JwtPayload {
  sub: string;
  email: string;
  display_name: string;
  [key: string]: string;
}

export function decodeJwt(token: string): JwtPayload | null {
  try {
    const payload = token.split('.')[1];
    // Base64Url → Base64
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const json = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join(''),
    );
    return JSON.parse(json) as JwtPayload;
  } catch {
    return null;
  }
}

export interface UserInfo {
  userId: string;
  email: string;
  displayName: string;
}

export function extractUserInfo(token: string): UserInfo | null {
  const payload = decodeJwt(token);
  if (!payload) return null;
  return {
    userId: payload.sub,
    email: payload.email,
    displayName: payload.display_name,
  };
}
