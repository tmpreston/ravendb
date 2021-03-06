using System;
using Raven.Client.Util;
using Raven.Server.Commercial;

namespace Raven.Server.Documents.PeriodicBackup
{
    public class ConcurrentBackupsCounter
    {
        private readonly LicenseManager _licenseManager;
        private int _concurrentBackups;
        private int _maxConcurrentBackups;
        private readonly bool _skipModifications;

        public ConcurrentBackupsCounter(int? maxNumberOfConcurrentBackupsConfiguration, LicenseManager licenseManager)
        {
            _licenseManager = licenseManager;

            int numberOfCoresToUse;
            var skipModifications = maxNumberOfConcurrentBackupsConfiguration != null;
            if (skipModifications)
            {
                numberOfCoresToUse = maxNumberOfConcurrentBackupsConfiguration.Value;
            }
            else
            {
                var utilizedCores = _licenseManager.GetCoresLimitForNode();
                numberOfCoresToUse = GetNumberOfCoresToUseForBackup(utilizedCores);
            }

            _concurrentBackups = numberOfCoresToUse;
            _maxConcurrentBackups = numberOfCoresToUse;
            _skipModifications = skipModifications;
        }

        public void StartBackup(string backupName)
        {
            lock (this)
            {
                if (_concurrentBackups <= 0)
                {
                    throw new BackupDelayException(
                        $"Failed to start Backup Task: '{backupName}'. " +
                        $"The task exceeds the maximum number of concurrent backup tasks configured. " +
                        $"Current maximum number of concurrent backups is: {_maxConcurrentBackups:#,#;;0}")
                    {
                        DelayPeriod = TimeSpan.FromMinutes(1)
                    };
                }

                _concurrentBackups--;
            }
        }

        public void FinishBackup()
        {
            lock (this)
            {
                _concurrentBackups++;
            }
        }

        public void ModifyMaxConcurrentBackups()
        {
            if (_skipModifications)
                return;

            var utilizedCores = _licenseManager.GetCoresLimitForNode();
            var newMaxConcurrentBackups = GetNumberOfCoresToUseForBackup(utilizedCores);

            lock (this)
            {
                var diff = newMaxConcurrentBackups - _maxConcurrentBackups;
                _maxConcurrentBackups = newMaxConcurrentBackups;
                _concurrentBackups += diff;
            }
        }

        public int GetNumberOfCoresToUseForBackup(int utilizedCores)
        {
            return Math.Max(1, utilizedCores / 2);
        }
    }
}
