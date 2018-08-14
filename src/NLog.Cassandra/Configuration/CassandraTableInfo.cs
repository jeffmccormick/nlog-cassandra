using Cassandra;
using NLog.Config;
using System.Collections.Generic;

namespace NLog.Cassandra.Configuration
{
	[NLogConfigurationItem]
    public sealed class CassandraTableInfo
    {

		[RequiredParameter]
		public string Name { get; set; }

		[RequiredParameter]
		[ArrayParameter(typeof(CassandraColumnInfo), "column")]
		public ICollection<CassandraColumnInfo> Columns { get; set; }

		internal PreparedStatement PreparedStatement { get; set; }

		public CassandraTableInfo()
		{
			this.Columns = new List<CassandraColumnInfo>();
		}

	}
}
