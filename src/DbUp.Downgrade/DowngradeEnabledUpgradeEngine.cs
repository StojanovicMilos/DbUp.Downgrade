﻿using DbUp.Builder;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Engine.Transactions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DbUp.Downgrade
{
    public class DowngradeEnabledUpgradeEngine
    {
        public UpgradeEngine UpgradeEngine { get; }

        private DowngradeEnabledTableJournal _journal;
        private List<IScriptProvider> _scriptProviders;
        private IConnectionManager _connectionManager;
        private IUpgradeLog _log;
        private readonly bool _autoDowngradeEnabled;

        public DowngradeEnabledUpgradeEngine(UpgradeEngineBuilder builder, bool autoDowngradeEnabled)
        {
            _autoDowngradeEnabled = autoDowngradeEnabled;

            builder.Configure(c =>
            {
                _journal = c.Journal as DowngradeEnabledTableJournal ?? throw new NotSupportedException("Can't build 'DowngradeEnabledUpgradeEngine', journal table not inherits 'DowngradeEnabledTableJournal'");
                _scriptProviders = c.ScriptProviders;
                _connectionManager = c.ConnectionManager;
                _log = c.Log;
            });

            UpgradeEngine = builder.Build();
        }

        public DatabaseUpgradeResult PerformDowngrade()
        {
            List<SqlScript> downgradeScripts = new List<SqlScript>();

            SqlScript downgradeSqlScript = null;
            try
            {
                var configurationTransactionMode = _connectionManager.TransactionMode;
                _connectionManager.TransactionMode = TransactionMode.SingleTransaction;
                using (var _ = _connectionManager.OperationStarting(_log, new List<SqlScript>()))
                {
                    var allScripts = _scriptProviders.SelectMany(scriptProvider => scriptProvider.GetScripts(_connectionManager));
                    var executedScripts = _journal.GetExecutedScriptsInReverseOrder();

                    foreach (var executedScript in executedScripts)
                    {
                        if (!allScripts.Any(s => s.Name.Equals(executedScript)))
                        {
                            string downgradeScript = _journal.GetDowngradeScript(executedScript);
                            
                            downgradeSqlScript = new SqlScript("FailedDowngradeScript", downgradeScript);
                            
                            _journal.RevertScript(executedScript, downgradeScript);
                            
                            downgradeScripts.Add(downgradeSqlScript);
                        }
                    }
                }
                _connectionManager.TransactionMode = configurationTransactionMode;
            }
            catch (Exception ex)
            {
                return new DatabaseUpgradeResult(downgradeScripts, false, ex, downgradeSqlScript);
            }

            return new DatabaseUpgradeResult(downgradeScripts, true, null, null);
        }

        public DatabaseUpgradeResult PerformDowngradeFor(string[] scriptsToBeReverted)
        {
            if (scriptsToBeReverted == null)
            {
                throw new ArgumentNullException(nameof(scriptsToBeReverted));
            }

            List<SqlScript> downgradeScripts = new List<SqlScript>();
            SqlScript downgradeSqlScript = null;
            try
            {
                var configurationTransactionMode = _connectionManager.TransactionMode;
                _connectionManager.TransactionMode = TransactionMode.SingleTransaction;
                using (var _ = _connectionManager.OperationStarting(_log, new List<SqlScript>()))
                {
                    foreach (var executedScript in scriptsToBeReverted)
                    {
                        string downgradeScript = _journal.GetDowngradeScript(executedScript);
                        
                        downgradeSqlScript = new SqlScript("FailedDowngradeScript", downgradeScript);
                        
                        _journal.RevertScript(executedScript, downgradeScript);
                        
                        downgradeScripts.Add(downgradeSqlScript);
                    }
                }
                _connectionManager.TransactionMode = configurationTransactionMode;
            }
            catch (Exception ex)
            {
                return new DatabaseUpgradeResult(downgradeScripts, false, ex, downgradeSqlScript);
            }

            return new DatabaseUpgradeResult(downgradeScripts, true, null, null);
        }

        public DatabaseUpgradeResult PerformUpgrade()
        {
            if (_autoDowngradeEnabled)
            {
                var downgradeResult = PerformDowngrade();

                if (!downgradeResult.Successful)
                {
                    return downgradeResult;
                }
            }

            return UpgradeEngine.PerformUpgrade();
        }
    }
}
