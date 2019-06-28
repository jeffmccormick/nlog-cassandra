using Cassandra;
using NLog.Cassandra.Configuration;
using NLog.Common;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NLog.Cassandra.Targets
{
    [Target("Cassandra")]
    public sealed class CassandraNLogTarget : TargetWithLayout
    {

        private Cluster _cluster;
        private readonly Dictionary<string, ISession> _sessions;
        private bool _isConfigured = false;
        private volatile ConnectionState _connectionState = ConnectionState.Disconnected;
        private readonly object _connectionLock = new object();
        private bool _disposed;

        [RequiredParameter]
        [ArrayParameter(typeof(CassandraNodeInfo), "node")]
        public IEnumerable<CassandraNodeInfo> Nodes { get; set; }

        [RequiredParameter]
        [ArrayParameter(typeof(CassandraKeyspaceInfo), "keyspace")]
        public IEnumerable<CassandraKeyspaceInfo> Keyspaces { get; set; }

        public CassandraNLogTarget()
        {
            this.Nodes = new List<CassandraNodeInfo>();
            this.Keyspaces = new List<CassandraKeyspaceInfo>();
            this._sessions = new Dictionary<string, ISession>();
        }

        protected override void Write(LogEventInfo logEvent)
        {
            if (!this._isConfigured)
                throw new NLogRuntimeException("Invalid Cassandra target configuration");
            if (this._disposed)
                throw new ObjectDisposedException(nameof(CassandraNLogTarget));

            this.EnsureConnectionsInitialized();

            List<Exception> exceptions = null;

            foreach (var keyspace in this.Keyspaces)
            {
                var session = this._sessions[keyspace.Name];
                if (session is null)
                    continue;

                foreach (var table in keyspace.Tables)
                {
                    if (table.PreparedStatement is null)
                        continue;

                    try
                    {
                        var args = table.Columns.Select(c =>
                        {
                            try
                            {
                                if (c.TypeCode.HasValue)
                                    return Convert.ChangeType(c.Layout.Render(logEvent), c.TypeCode.Value);

                                switch (c.DataType)
                                {
                                    case nameof(Guid):
                                        if (Guid.TryParse(c.Layout.Render(logEvent), out var guid))
                                            return guid;
                                        else
                                            return Guid.Empty;
                                    default:
                                        return default;
                                }
                            }
                            catch
                            {
                                return null;
                            }
                        });
                        var executable = table.PreparedStatement.Bind(args.ToArray());

                        session.Execute(executable);
                    }
                    catch (Exception e)
                    {
                        if (exceptions is null)
                            exceptions = new List<Exception>(1);

                        // Delay exception here so that we can try logging to other tables/keyspaces
                        exceptions.Add(e);
                    }
                }
            }

            // Finally, if we had any exceptions, we can now throw them
            if (exceptions is null)
                return;
            else if (exceptions.Count == 1)
                throw exceptions[0];
            else if (exceptions.Count > 1)
                throw new AggregateException(exceptions);
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            this.EnsureValidConfiguration();

            try
            {
                this._cluster = Cluster.Builder().AddContactPoints(this.Nodes.Select(n => n.Address)).Build();

                foreach (var keyspace in this.Keyspaces)
                    this._sessions.Add(keyspace.Name, null);
            }
            catch (Exception e)
            {
                InternalLogger.Error(e, "Failed to build cluster for Cassandra NLog target(Name={0}); Nodes:{1}", this.Name, string.Join(",", this.Nodes));
                throw new FormatException("Failed to build cluster for Cassandra NLog target", e);
            }
        }

        protected override void CloseTarget()
        {
            this._cluster?.Shutdown();

            this._sessions.Clear();

            base.CloseTarget();
        }

        private void EnsureConnectionsInitialized()
        {
            if (this._connectionState == ConnectionState.Connected)
                return;

            lock (this._connectionLock)
            {
                if (this._connectionState == ConnectionState.Connected)
                    return;

                var allSuccess = true;
                foreach (var keyspace in this.Keyspaces)
                {
                    try
                    {
                        if (this._sessions[keyspace.Name] != null && keyspace.Tables.All(t => t.PreparedStatement != null))
                            continue;

                        var session = this._cluster.Connect(keyspace.Name);
                        this._sessions[keyspace.Name] = session;

                        foreach (var table in keyspace.Tables)
                        {
                            var statement = $"INSERT INTO {table.Name} ({string.Join(",", table.Columns.Select(c => c.Name))})" +
                                $" VALUES ({string.Join(",", Enumerable.Repeat('?', table.Columns.Count))})";

                            table.PreparedStatement = session.Prepare(statement);
                        }

                        this._connectionState = ConnectionState.Partial;
                    }
                    catch (Exception e)
                    {
                        InternalLogger.Warn("Failed to connect Cassandra Target(Name={0}) to keyspace {1}: {2}", this.Name, keyspace.Name, e);
                        allSuccess = false;
                    }
                }

                if (allSuccess)
                    this._connectionState = ConnectionState.Connected;
            }

            if (this._connectionState == ConnectionState.Disconnected)
                throw new NLogRuntimeException("Could not connect to any configured Cassandra keyspaces");
        }

        private void EnsureValidConfiguration()
        {
            if (this.Nodes?.Any() != true)
            {
                InternalLogger.Error("NLog Cassandra(Name={0}) configuration does not include a valid node configuration", this.Name);
                throw new NLogConfigurationException($"No nodes configured for NLog Cassandra target(Name={this.Name})");
            }
            else if (this.Keyspaces?.Any() != true)
            {
                InternalLogger.Error("NLog Cassandra(Name={0}) configuration does not include a valid keyspace configuration", this.Name);
                throw new NLogConfigurationException($"No keyspaces configured for NLog Cassandra target(Name={this.Name})");
            }
            foreach (var keyspace in this.Keyspaces)
            {
                if (keyspace.Tables?.Any() != true)
                {
                    InternalLogger.Error("NLog Cassandra(Name={0}) configuration does not include a valid keyspace configuration", this.Name);
                    throw new NLogConfigurationException($"No tables configured for NLog Cassandra target(Name={this.Name}) keyspace(Name={keyspace.Name})");
                }
            }

            this._isConfigured = true;
        }

        #region IDisposable Members

        protected override void Dispose(bool disposing)
        {
            if (this._disposed)
                return;

            if (disposing)
            {
                foreach (var session in this._sessions.Values)
                    session?.Dispose();

                this._cluster?.Dispose();
            }

            this._disposed = true;
            base.Dispose(disposing);
        }

        #endregion

        private enum ConnectionState
        {
            Disconnected = 0,
            Partial = 1,
            Connected = 2
        }

    }
}