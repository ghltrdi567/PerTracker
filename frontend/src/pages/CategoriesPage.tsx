import { useEffect, useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  CircularProgress,
  Typography,
} from '@mui/material';
import { getCategories } from '../api/categories';
import type { Category } from '../types/api';
import { getApiErrors } from '../utils/helpers';
import { useToast } from '../components/ToastProvider';

const PAGE_SIZE = 50;

export default function CategoriesPage() {
  const toast = useToast();
  const [items, setItems] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const res = await getCategories(1, PAGE_SIZE);
        setItems(res?.items ?? []);
      } catch (err) {
        const msg = getApiErrors(err).join(' ');
        toast.show(msg, 'error');
      } finally {
        setLoading(false);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <Box>
      <Typography variant="h4" gutterBottom>Категории</Typography>

      {loading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
          <CircularProgress />
        </Box>
      ) : items.length > 0 ? (
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
      ) : (
        <Typography color="text.secondary">Категорий пока нет.</Typography>
      )}
    </Box>
  );
}
