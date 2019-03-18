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

        private string _dataType;
        [RequiredParameter]
		public string DataType
        {
            get
            {
                if (this.TypeCode.HasValue)
                    return this.TypeCode.Value.ToString();
                else
                    return this._dataType;
            }
            set
            {
                if (Enum.TryParse<TypeCode>(value, out var typeCode))
                    this.TypeCode = typeCode;
                else
                    this._dataType = value;
            }
		}

		internal TypeCode? TypeCode { get; private set; }


	}
}
