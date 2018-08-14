using NLog.Config;

namespace NLog.Cassandra.Configuration
{
	[NLogConfigurationItem]
    public sealed class CassandraNodeInfo
    {

		[RequiredParameter]
		public string Address { get; set; }

	}
}
