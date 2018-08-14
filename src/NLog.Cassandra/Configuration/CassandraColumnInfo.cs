using NLog.Config;
using NLog.Layouts;
using System;

namespace NLog.Cassandra.Configuration
{
	[NLogConfigurationItem]
    public sealed class CassandraColumnInfo
    {

		[RequiredParameter]
		public string Name { get; set; }

		[RequiredParameter]
		public Layout Layout { get; set; }

		[RequiredParameter]
		public string DataType
		{
			get => this._typeCode.ToString();
			set => Enum.TryParse(value, out this._typeCode);
		}

		private TypeCode _typeCode;
		internal TypeCode TypeCode
		{
			get => this._typeCode;
			set => this._typeCode = value;
		}


	}
}
