import { useEffect, useState } from 'react';
import { Box, Button, Card, CardContent, CircularProgress, Link, Typography } from '@mui/material';
import { CheckCircle, Error, Mail } from '@mui/icons-material';
import { useNavigate, useSearchParams, Link as RouterLink } from 'react-router-dom';
import { confirmEmail } from '../api/auth';

export default function ConfirmEmailPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState('');

  const userId = searchParams.get('userId') ?? '';
  const token = searchParams.get('token') ?? '';

  useEffect(() => {
    if (!userId || !token) {
      setStatus('error');
      setMessage('Ссылка некорректна: отсутствуют параметры userId или token.');
      return;
    }

    confirmEmail(userId, token)
      .then(() => {
        setStatus('success');
        setMessage('Email успешно подтверждён! Теперь вы можете войти в систему.');
      })
      .catch((err) => {
        setStatus('error');
        const detail = err.response?.data;
        if (typeof detail === 'string') {
          setMessage(detail);
        } else if (detail?.errors?.length) {
          setMessage(detail.errors.join(' '));
        } else {
          setMessage('Не удалось подтвердить email. Ссылка могла истечь или быть некорректной.');
        }
      });
  }, [userId, token]);

  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh' }}>
      <Card sx={{ maxWidth: 440, width: '100%' }}>
        <CardContent sx={{ p: 4, textAlign: 'center' }}>
          {status === 'loading' && (
            <>
              <CircularProgress sx={{ mb: 2 }} />
              <Typography variant="h5" gutterBottom>
                Подтверждение email...
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Пожалуйста, подождите.
              </Typography>
            </>
          )}

          {status === 'success' && (
            <>
              <CheckCircle color="success" sx={{ fontSize: 64, mb: 2 }} />
              <Typography variant="h5" gutterBottom>
                Email подтверждён!
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                {message}
              </Typography>
              <Button variant="contained" size="large" onClick={() => navigate('/login')}>
                Перейти к входу
              </Button>
            </>
          )}

          {status === 'error' && (
            <>
              <Error color="error" sx={{ fontSize: 64, mb: 2 }} />
              <Typography variant="h5" gutterBottom>
                Ошибка подтверждения
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                {message}
              </Typography>
              <Typography variant="body2" align="center">
                <Mail sx={{ fontSize: 16, verticalAlign: 'middle', mr: 0.5 }} />
                Попробуйте зарегистрироваться снова или свяжитесь с поддержкой.
              </Typography>
              <Box sx={{ mt: 2 }}>
                <Link component={RouterLink} to="/register">
                  К регистрации
                </Link>
              </Box>
            </>
          )}
        </CardContent>
      </Card>
    </Box>
  );
}
