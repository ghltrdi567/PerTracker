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
import { register } from '../api/auth';
import { getApiErrors } from '../utils/helpers';

export default function RegisterPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setErrors([]);
    setLoading(true);
    try {
      await register({ email, password, displayName });
      setSuccess(true);
    } catch (err) {
      setErrors(getApiErrors(err));
    } finally {
      setLoading(false);
    }
  };

  if (success) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh' }}>
        <Card sx={{ maxWidth: 420, width: '100%' }}>
          <CardContent sx={{ p: 4, textAlign: 'center' }}>
            <Typography variant="h5" gutterBottom>
              Письмо отправлено!
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
              Мы отправили письмо с подтверждением на {email}. Перейдите по ссылке в письме, чтобы активировать аккаунт.
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
              В режиме разработки письма можно посмотреть в MailHog: http://localhost:8025
            </Typography>
            <Button variant="contained" onClick={() => navigate('/login')}>
              Перейти к входу
            </Button>
          </CardContent>
        </Card>
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh' }}>
      <Card sx={{ maxWidth: 420, width: '100%' }}>
        <CardContent sx={{ p: 4 }}>
          <Typography variant="h5" align="center" gutterBottom>
            Регистрация
          </Typography>
          <Box component="form" onSubmit={handleSubmit} sx={{ mt: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
            <TextField
              label="Отображаемое имя"
              required
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
            />
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
              autoComplete="new-password"
              helperText="Мин. 8 символов, цифра, заглавная и строчная буквы, спецсимвол"
            />
            {errors.length > 0 && (
              <Typography color="error" variant="body2">
                {errors.join(' ')}
              </Typography>
            )}
            <Button type="submit" variant="contained" size="large" disabled={loading}>
              {loading ? 'Регистрация...' : 'Зарегистрироваться'}
            </Button>
          </Box>
          <Typography variant="body2" align="center" sx={{ mt: 2 }}>
            Уже есть аккаунт?{' '}
            <Link component={RouterLink} to="/login">
              Войти
            </Link>
          </Typography>
        </CardContent>
      </Card>
    </Box>
  );
}
