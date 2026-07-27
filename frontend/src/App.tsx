import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material';
import ErrorBoundary from './components/ErrorBoundary';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './components/ToastProvider';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import ConfirmEmailPage from './pages/ConfirmEmailPage';
import EventsPage from './pages/EventsPage';
import EventDetailPage from './pages/EventDetailPage';
import CategoriesPage from './pages/CategoriesPage';
import SettingsPage from './pages/SettingsPage';
import AdminDashboardPage from './pages/AdminDashboardPage';

const theme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#7c4dff' },
    secondary: { main: '#ff6d00' },
  },
  shape: { borderRadius: 10 },
});

export default function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <ErrorBoundary>
        <ToastProvider>
          <AuthProvider>
            <BrowserRouter>
            <Routes>
              <Route path="/login" element={<LoginPage />} />
              <Route path="/register" element={<RegisterPage />} />
              <Route path="/confirm-email" element={<ConfirmEmailPage />} />
              <Route element={<ProtectedRoute />}>
                <Route element={<Layout />}>
                  <Route path="/events" element={<EventsPage />} />
                  <Route path="/events/:id" element={<EventDetailPage />} />
                  <Route path="/categories" element={<CategoriesPage />} />
                  <Route path="/settings" element={<SettingsPage />} />
                  <Route path="/admin" element={<AdminDashboardPage />} />
                </Route>
              </Route>
              <Route path="*" element={<Navigate to="/events" replace />} />
            </Routes>
          </BrowserRouter>
        </AuthProvider>
      </ToastProvider>
      </ErrorBoundary>
    </ThemeProvider>
  );
}
