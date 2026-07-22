import { Navigate, Route, Routes } from 'react-router-dom';
import { ContentLayout } from './features/content/ContentLayout';
import { PackageOverviewPage } from './features/content/PackageOverviewPage';
import { CategoryManager } from './features/content/CategoryManager';
import { QuestionListPage } from './features/content/QuestionListPage';
import { QuestionFormPage } from './features/content/QuestionFormPage';
import { ContentPackages } from './components/ContentPackages';
import { AdminPage } from './pages/AdminPage';

export default function App() {
  return (
    <Routes>
      <Route path="/admin" element={<AdminPage />} />
      <Route path="/admin/content" element={<ContentLayout />}>
        <Route index element={<ContentPackages />} />
        <Route path="packages/:packageVersionId" element={<PackageOverviewPage />} />
        <Route path="packages/:packageVersionId/categories" element={<CategoryManager />} />
        <Route path="packages/:packageVersionId/questions" element={<QuestionListPage />} />
        <Route path="packages/:packageVersionId/questions/new" element={<QuestionFormPage />} />
        <Route path="packages/:packageVersionId/questions/:questionId" element={<QuestionFormPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/admin" replace />} />
    </Routes>
  );
}
