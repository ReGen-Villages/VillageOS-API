import { LanguageSwitcher } from '../common/LanguageSwitcher';
import { ThemeToggleButton } from '../common/ThemeToggleButton';

/** The corner every screen before the application shares: the theme switch and the language
 *  switcher, which the sidebar footer carries once a person is signed in. */
export function SignInScreenControls() {
  return (
    <div className="fixed top-3 right-3 z-10 flex items-center gap-1">
      <ThemeToggleButton />
      <LanguageSwitcher openDirection="down" align="right" />
    </div>
  );
}
