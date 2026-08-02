import { useEffect, useState } from 'react';
import { Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { ContentLayout } from './features/content/ContentLayout';
import { PackageOverviewPage } from './features/content/PackageOverviewPage';
import { CategoryManager } from './features/content/CategoryManager';
import { QuestionListPage } from './features/content/QuestionListPage';
import { QuestionFormPage } from './features/content/QuestionFormPage';
import { ContentPackages } from './components/ContentPackages';
import { AdminPage } from './pages/AdminPage';
import { OperatorSignIn } from './components/OperatorSignIn';
import { getOperatorToken, subscribeOperatorToken } from './api/operatorSession';

function OperatorGate() {
  const [token, setToken] = useState(getOperatorToken());
  useEffect(() => subscribeOperatorToken(setToken), []);
  return token ? <Outlet /> : <OperatorSignIn />;
}

export default function App() {
  return (
    <Routes>
      <Route element={<OperatorGate />}>
        <Route path="/admin" element={<AdminPage />} />
        <Route path="/admin/content" element={<ContentLayout />}>
          <Route index element={<ContentPackages />} />
          <Route path="packages/:packageVersionId" element={<PackageOverviewPage />} />
          <Route path="packages/:packageVersionId/categories" element={<CategoryManager />} />
          <Route path="packages/:packageVersionId/questions" element={<QuestionListPage />} />
          <Route path="packages/:packageVersionId/questions/new" element={<QuestionFormPage />} />
          <Route path="packages/:packageVersionId/questions/:questionId" element={<QuestionFormPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/admin" replace />} />
    </Routes>
  );
}
