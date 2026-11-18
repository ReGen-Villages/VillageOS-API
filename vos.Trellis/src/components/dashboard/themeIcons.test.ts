import { describe, it, expect } from 'vitest';
import { iconNames } from 'lucide-react/dynamic';

/** The icons the platform's templates give their themes. Only this repository can see which icons Trellis
 *  ships, and only the platform's can read its templates, so a theme added there adds its icon here. */
const TEMPLATE_THEME_ICONS = [
  'thermometer', 'droplet', 'wind', 'sun', 'triangle-alert', 'sprout', 'mountain', 'leaf', 'haze', 'drafting-compass',
];

describe('the theme icons', () => {
  it('names only icons the icon set Trellis ships can draw', () => {
    const shipped = new Set<string>(iconNames);

    expect(TEMPLATE_THEME_ICONS.filter((icon) => !shipped.has(icon))).toEqual([]);
  });
});
