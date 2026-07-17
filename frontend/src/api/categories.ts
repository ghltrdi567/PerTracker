import api from './client';
import type { Category, CreateCategoryRequest, PagedResult } from '../types/api';

export async function getCategories(page = 1, pageSize = 100) {
  const res = await api.get<PagedResult<Category>>('/api/categories', {
    params: { page, pageSize },
  });
  return res.data;
}

export async function createCategory(data: CreateCategoryRequest) {
  const res = await api.post<Category>('/api/categories', data);
  return res.data;
}
