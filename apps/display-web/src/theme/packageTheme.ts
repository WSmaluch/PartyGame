export const packageThemeKeys = ['classic', 'friends', 'student', 'adult'] as const;
export type PackageThemeKey = (typeof packageThemeKeys)[number];
export const neutralPackageTheme: PackageThemeKey = 'classic';

/** Converts stable content metadata to presentation tokens. */
export function packageThemeFor(packageKey?: string | null, isMixed = false): PackageThemeKey {
  if (isMixed || !packageKey) return neutralPackageTheme;
  const key = packageKey.trim().toLowerCase();
  if (['znajomi', 'friends'].includes(key)) return 'friends';
  if (['studenckie', 'domowkowe', 'student', 'house-party'].includes(key)) return 'student';
  if (['18+', 'adult', 'adult-18'].includes(key)) return 'adult';
  return 'classic';
}
