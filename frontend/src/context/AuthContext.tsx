import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { refresh as refreshApi, logout as logoutApi } from '../api/auth';
import { getAccessToken, setOnAuthFailure } from '../api/client';
import { extractUserInfo, type UserInfo } from '../utils/jwt';

interface AuthState {
  isAuthenticated: boolean;
  loading: boolean;
  user: UserInfo | null;
  setIsAuthenticated: (value: boolean) => void;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | undefined>(undefined);

function getUserFromToken(): UserInfo | null {
  const token = getAccessToken();
  if (!token) return null;
  return extractUserInfo(token);
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [user, setUser] = useState<UserInfo | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setOnAuthFailure(() => {
      setIsAuthenticated(false);
      setUser(null);
    });
    // При загрузке приложения пытаемся получить новый access token через refresh cookie.
    refreshApi()
      .then(() => {
        setIsAuthenticated(true);
        setUser(getUserFromToken());
      })
      .catch(() => setIsAuthenticated(false))
      .finally(() => setLoading(false));
  }, []);

  const logout = async () => {
    await logoutApi();
    setIsAuthenticated(false);
    setUser(null);
  };

  // Вызывается после login — обновляет состояние пользователя из токена
  const handleSetAuthenticated = (value: boolean) => {
    setIsAuthenticated(value);
    if (value) {
      setUser(getUserFromToken());
    } else {
      setUser(null);
    }
  };

  return (
    <AuthContext.Provider value={{ isAuthenticated, loading, user, setIsAuthenticated: handleSetAuthenticated, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
