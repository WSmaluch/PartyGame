import { describe, expect, it } from 'vitest';
import { translations } from './translations';

describe('Web Player localization', () => {
  it('has non-empty Polish and English values for every key', () => {
    for (const key of Object.keys(translations.pl) as (keyof typeof translations.pl)[]) {
      expect(translations.en[key]).toBeTruthy(); expect(translations.pl[key]).toBeTruthy(); expect(translations.pl[key]).not.toBe(key); expect(translations.en[key]).not.toBe(key);
    }
  });
});
