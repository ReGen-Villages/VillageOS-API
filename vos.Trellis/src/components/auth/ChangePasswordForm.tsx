import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { RegenLogo } from './RegenLogo';

interface ChangePasswordFormProps {
  onChangePassword: (currentPassword: string, newPassword: string) => Promise<void>;
  error: string | null;
  loading: boolean;
  username: string;
}

export function ChangePasswordForm({ onChangePassword, error, loading, username }: ChangePasswordFormProps) {
  const { t } = useTranslation();
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    if (!currentPassword || !newPassword || !confirmPassword) return;

    if (newPassword !== confirmPassword) {
      setValidationError(t('changePassword.mismatch'));
      return;
    }

    if (newPassword.length < 4) {
      setValidationError(t('changePassword.tooShort'));
      return;
    }

    try {
      await onChangePassword(currentPassword, newPassword);
    } catch {
      // error is surfaced via the error prop
    }
  };

  const displayError = validationError || error;
  const canSubmit = !loading && !!currentPassword && !!newPassword && !!confirmPassword;

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-900">
      <div className="w-full max-w-sm bg-gray-800 rounded-lg shadow-lg p-8">
        <div className="flex justify-center mb-2">
          <RegenLogo className="w-40 h-40" />
        </div>
        <p className="text-sm text-gray-400 text-center mb-1">{t('changePassword.required')}</p>
        <p className="text-xs text-gray-500 text-center mb-6">
          {t('changePassword.loggedInAs')} <span className="text-gray-300 font-medium">{username}</span>
        </p>
        <form onSubmit={handleSubmit} className="space-y-4">
          {displayError && (
            <div className="bg-red-900/50 border border-red-500 text-red-200 px-3 py-2 rounded text-sm">
              {displayError}
            </div>
          )}
          <div>
            <label htmlFor="currentPassword" className="block text-sm font-medium text-gray-300 mb-1">
              {t('changePassword.currentPassword')}
            </label>
            <input
              id="currentPassword"
              type="password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400 focus:outline-none focus:border-blue-500"
              placeholder={t('changePassword.currentPlaceholder')}
              autoComplete="current-password"
              autoFocus
              disabled={loading}
            />
          </div>
          <div>
            <label htmlFor="newPassword" className="block text-sm font-medium text-gray-300 mb-1">
              {t('changePassword.newPassword')}
            </label>
            <input
              id="newPassword"
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400 focus:outline-none focus:border-blue-500"
              placeholder={t('changePassword.newPlaceholder')}
              autoComplete="new-password"
              disabled={loading}
            />
          </div>
          <div>
            <label htmlFor="confirmPassword" className="block text-sm font-medium text-gray-300 mb-1">
              {t('changePassword.confirmPassword')}
            </label>
            <input
              id="confirmPassword"
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400 focus:outline-none focus:border-blue-500"
              placeholder={t('changePassword.confirmPlaceholder')}
              autoComplete="new-password"
              disabled={loading}
            />
          </div>
          <button
            type="submit"
            disabled={!canSubmit}
            className="w-full py-2 px-4 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 disabled:cursor-not-allowed text-white font-medium rounded transition-colors"
          >
            {loading ? t('changePassword.changing') : t('changePassword.submit')}
          </button>
        </form>
      </div>
    </div>
  );
}
