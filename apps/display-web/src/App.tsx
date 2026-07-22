import { Navigate, Route, Routes } from 'react-router-dom';
import { DisplayPage } from './pages/DisplayPage';

export default function App() {
  return (
    <Routes>
      <Route path="/display" element={<DisplayPage />} />
      <Route path="*" element={<Navigate to="/display" replace />} />
    </Routes>
  );
}
