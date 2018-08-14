using NLog.Config;
using System.Collections.Generic;

namespace NLog.Cassandra.Configuration
{
	[NLogConfigurationItem]
    public sealed class CassandraKeyspaceInfo
    {

		[RequiredParameter]
		public string Name { get; set; }

		[RequiredParameter]
		[ArrayParameter(typeof(CassandraTableInfo), "table")]
		public IEnumerable<CassandraTableInfo> Tables { get; set; }

		public CassandraKeyspaceInfo()
		{
			this.Tables = new List<CassandraTableInfo>();
		}

	}
}
