import { useState, type FormEvent } from 'react';
import {
  Box,
  Button,
  Card,
  CardContent,
  Link,
  TextField,
  Typography,
} from '@mui/material';
import { useNavigate, Link as RouterLink } from 'react-router-dom';
import { login } from '../api/auth';
import { useAuth } from '../context/AuthContext';
import { getApiErrors } from '../utils/helpers';
import { useToast } from '../components/ToastProvider';

export default function LoginPage() {
  const navigate = useNavigate();
  const { setIsAuthenticated } = useAuth();
  const toast = useToast();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setErrors([]);
    setLoading(true);
    try {
      await login({ email, password });
      setIsAuthenticated(true);
      toast.show('Вход выполнен');
      navigate('/events');
    } catch (err) {
      setErrors(getApiErrors(err));
    } finally {
      setLoading(false);
    }
  };

  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Card sx={{ maxWidth: 420, width: '100%' }}>
        <CardContent sx={{ p: 4 }}>
          <Typography variant="h5" align="center" gutterBottom>
            Вход в PerFunTracker
          </Typography>
          <Box component="form" onSubmit={handleSubmit} sx={{ mt: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
            <TextField
              label="Email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
            />
            <TextField
              label="Пароль"
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
            />
            {errors.length > 0 && (
              <Typography color="error" variant="body2">
                {errors.join(' ')}
              </Typography>
            )}
            <Button type="submit" variant="contained" size="large" disabled={loading}>
              {loading ? 'Вход...' : 'Войти'}
            </Button>
          </Box>
          <Typography variant="body2" align="center" sx={{ mt: 2 }}>
            Нет аккаунта?{' '}
            <Link component={RouterLink} to="/register">
              Зарегистрироваться
            </Link>
          </Typography>
        </CardContent>
      </Card>
    </Box>
  );
}
