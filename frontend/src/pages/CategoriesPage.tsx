import { useEffect, useState, type FormEvent } from 'react';
import {
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  TextField,
  Typography,
} from '@mui/material';
import { Add as AddIcon } from '@mui/icons-material';
import { getCategories, createCategory } from '../api/categories';
import type { Category } from '../types/api';
import { getApiErrors } from '../utils/helpers';
import { useToast } from '../components/ToastProvider';

const PAGE_SIZE = 50;

export default function CategoriesPage() {
  const toast = useToast();
  const [items, setItems] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);

  const [name, setName] = useState('');
  const [icon, setIcon] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  const load = async (pageNum: number, append: boolean) => {
    if (append) setLoadingMore(true); else setLoading(true);
    try {
      const res = await getCategories(pageNum, PAGE_SIZE);
      const newItems = res?.items ?? [];
      setItems(append ? (prev) => [...prev, ...newItems] : newItems);
      setHasMore((res?.page ?? 1) < (res?.totalPages ?? 1));
      setPage(pageNum);
    } catch (err) {
      console.error('CategoriesPage: failed to load categories', err);
      const msg = getApiErrors(err).join(' ');
      toast.show(msg, 'error');
      if (!append) setItems([]);
    } finally {
      setLoading(false);
      setLoadingMore(false);
    }
  };

  useEffect(() => {
    load(1, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleLoadMore = () => load(page + 1, true);

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault();
    setErrors([]);
    setSaving(true);
    try {
      await createCategory({ name, icon: icon || null });
      toast.show('Категория создана');
      setName('');
      setIcon('');
      await load(1, false);
    } catch (err) {
      setErrors(getApiErrors(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Box>
      <Typography variant="h4" gutterBottom>Категории</Typography>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h6" gutterBottom>Создать категорию</Typography>
          <Box component="form" onSubmit={handleCreate} sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <TextField
              label="Название"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              inputProps={{ maxLength: 100 }}
              size="small"
            />
            <TextField
              label="Иконка (эмодзи)"
              value={icon}
              onChange={(e) => setIcon(e.target.value)}
              inputProps={{ maxLength: 50 }}
              size="small"
              sx={{ width: 120 }}
            />
            <Button type="submit" variant="contained" startIcon={<AddIcon />} disabled={saving}>
              {saving ? '...' : 'Добавить'}
            </Button>
            {errors.length > 0 && (
              <Typography color="error" variant="body2" sx={{ width: '100%' }}>
                {errors.join(' ')}
              </Typography>
            )}
          </Box>
        </CardContent>
      </Card>

      {loading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
          <CircularProgress />
        </Box>
      ) : items.length > 0 ? (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: '1fr 1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 2 }}>
            {items.map((cat) => (
              <Card key={cat.id}>
                <CardContent sx={{ textAlign: 'center' }}>
                  <Typography variant="h4">{cat.icon ?? '📁'}</Typography>
                  <Typography variant="body2">{cat.name}</Typography>
                </CardContent>
              </Card>
            ))}
          </Box>
          {hasMore && (
            <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
              <Button
                variant="outlined"
                onClick={handleLoadMore}
                disabled={loadingMore}
                startIcon={loadingMore ? <CircularProgress size={20} /> : undefined}
              >
                {loadingMore ? 'Загрузка...' : 'Загрузить ещё'}
              </Button>
            </Box>
          )}
        </>
      ) : (
        <Typography color="text.secondary">Категорий пока нет. Создайте первую!</Typography>
      )}
    </Box>
  );
}
