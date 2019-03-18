using Cassandra;
using NLog.Cassandra.Configuration;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NLog.Cassandra.Targets
{
	[Target("Cassandra")]
    public sealed class CassandraNLogTarget : TargetWithLayout
    {

		[RequiredParameter]
		[ArrayParameter(typeof(CassandraNodeInfo), "node")]
		public IEnumerable<CassandraNodeInfo> Nodes { get; set; }

		[RequiredParameter]
		[ArrayParameter(typeof(CassandraKeyspaceInfo), "keyspace")]
		public IEnumerable<CassandraKeyspaceInfo> Keyspaces { get; set; }

		private Cluster _cluster;
		private readonly Dictionary<string, ISession> _sessions;

		public CassandraNLogTarget()
		{
			this.Nodes = new List<CassandraNodeInfo>();
			this.Keyspaces = new List<CassandraKeyspaceInfo>();
			this._sessions = new Dictionary<string, ISession>();
		}

		protected override void Write(LogEventInfo logEvent)
		{
            var exceptions = new List<Exception>();

			foreach (var keyspace in this.Keyspaces)
			{
				if (!this._sessions.TryGetValue(keyspace.Name, out ISession session))
					continue;

				foreach (var table in keyspace.Tables)
				{
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
                        // Delay exception here so that we can try logging to other tables/keyspaces
                        exceptions.Add(e);
					}
				}
			}

            // Finally, if we had any exceptions, we can now throw them
            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);
		}

		protected override void InitializeTarget()
		{
			base.InitializeTarget();

			try
			{
				this._cluster = Cluster.Builder().AddContactPoints(this.Nodes.Select(n => n.Address)).Build();
			}
			catch (Exception e)
			{
				throw new FormatException("Invalid Cassandra node addresses in NLog.config", e);
			}

			try
			{
				foreach (var keyspace in this.Keyspaces)
				{
					var session = this._cluster.Connect(keyspace.Name);
					this._sessions.Add(keyspace.Name, session);

					StringBuilder statementBuilder = new StringBuilder();
					foreach (var table in keyspace.Tables)
					{
						statementBuilder.Append("INSERT INTO ")
							.Append(table.Name)
							.Append(" (")
							.Append(string.Join(",", table.Columns.Select(c => c.Name)))
							.Append(") VALUES (")
							.Append(string.Join(",", Enumerable.Repeat('?', table.Columns.Count)))
							.Append(")");

						table.PreparedStatement = session.Prepare(statementBuilder.ToString());

						statementBuilder.Clear();
					}
				}
			}
			catch (Exception e)
			{
				throw new FormatException("Invalid Cassandra keyspace in NLog.config", e);
			}
		}

		protected override void CloseTarget()
		{
			this._cluster?.Shutdown();

			this._sessions.Clear();

			base.CloseTarget();
		}

		protected override void Dispose(bool disposing)
		{
			Parallel.ForEach(this._sessions.Values, s => s?.Dispose());

			this._cluster?.Dispose();

			base.Dispose(disposing);
		}

	}
}
