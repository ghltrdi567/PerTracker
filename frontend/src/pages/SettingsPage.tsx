import { useEffect, useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  CircularProgress,
  FormControlLabel,
  Switch,
  Typography,
} from '@mui/material';
import { getSettings, updateSettings } from '../api/users';
import { getApiErrors } from '../utils/helpers';
import { useToast } from '../components/ToastProvider';

export default function SettingsPage() {
  const toast = useToast();
  const [notify, setNotify] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const res = await getSettings();
        setNotify(res.notifyByEmailOnCompletion);
      } catch (err) {
        toast.show(getApiErrors(err).join(' '), 'error');
      } finally {
        setLoading(false);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleChange = async (checked: boolean) => {
    setNotify(checked);
    setSaving(true);
    try {
      await updateSettings({ notifyByEmailOnCompletion: checked });
      toast.show('Настройки сохранены');
    } catch (err) {
      setNotify(!checked);
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box>
      <Typography variant="h4" gutterBottom>Настройки</Typography>
      <Card sx={{ maxWidth: 480 }}>
        <CardContent>
          <FormControlLabel
            control={
              <Switch
                checked={notify}
                disabled={saving}
                onChange={(e) => handleChange(e.target.checked)}
              />
            }
            label="Email-уведомления при завершении событий"
          />
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Когда другой участник отмечает завершение события, вы получите письмо на email.
          </Typography>
        </CardContent>
      </Card>
    </Box>
  );
}
