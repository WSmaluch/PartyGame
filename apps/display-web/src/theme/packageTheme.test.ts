import { describe, expect, it } from 'vitest';
import { neutralPackageTheme, packageThemeFor } from './packageTheme';

describe('packageThemeFor', () => {
  it.each([['classic', 'classic'], ['znajomi', 'friends'], ['studenckie', 'student'], ['18+', 'adult']] as const)('maps %s to %s', (key, theme) => {
    expect(packageThemeFor(key)).toBe(theme);
  });
  it('uses neutral classic styling for unknown and mixed selections', () => {
    expect(packageThemeFor('future-pack')).toBe(neutralPackageTheme);
    expect(packageThemeFor('18+', true)).toBe(neutralPackageTheme);
  });
});
