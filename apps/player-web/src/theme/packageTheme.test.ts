import { describe, expect, it } from 'vitest';
import { neutralPackageTheme, packageThemeFor } from './packageTheme';

describe('packageThemeFor', () => {
  it('keeps the controller in the active pack atmosphere', () => {
    expect(packageThemeFor('znajomi')).toBe('friends');
    expect(packageThemeFor('studenckie')).toBe('student');
    expect(packageThemeFor('18+')).toBe('adult');
  });
  it('uses a safe neutral fallback', () => {
    expect(packageThemeFor(undefined)).toBe(neutralPackageTheme);
    expect(packageThemeFor('18+', true)).toBe(neutralPackageTheme);
  });
});
