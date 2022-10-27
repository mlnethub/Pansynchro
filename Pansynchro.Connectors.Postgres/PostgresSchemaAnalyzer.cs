﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

using Npgsql;

using Pansynchro.Core;
using Pansynchro.SQL;
using Pansynchro.Core.DataDict;

namespace Pansynchro.Connectors.Postgres
{
    public class PostgresSchemaAnalyzer : SqlSchemaAnalyzer
    {
        public PostgresSchemaAnalyzer(string connectionString) : base(new NpgsqlConnection(connectionString))
        { }

        protected override string ColumnsQuery =>
@"SELECT
	n.nspname as schema_name,
	t.relname as table_name,
	a.attname AS colname,
	pg_catalog.format_type(a.atttypid, a.atttypmod) AS coltype,
	a.attnotnull
FROM pg_catalog.pg_attribute a
join pg_catalog.pg_class t on t.oid = a.attrelid
LEFT JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
WHERE a.attnum > 0
	AND NOT a.attisdropped
	and a.attgenerated = ''
	and t.relkind = 'r'
	and nspname not in ('information_schema', 'pg_catalog', 'pansynchro')
    and nspname not like 'pg_toast%'
    and nspname not like 'pg_temp_%'
ORDER BY a.attnum";

        protected override string PkQuery =>
@"SELECT n.nspname, c.relname, a.attname
FROM   pg_index i
JOIN   pg_attribute a ON a.attrelid = i.indrelid
                     AND a.attnum = ANY(i.indkey)
JOIN   pg_class c on c.oid = i.indrelid
join   pg_namespace n on n.oid = c.relnamespace
WHERE  i.indisprimary
	and nspname not in ('information_schema', 'pg_catalog', 'pansynchro')
    and nspname not like 'pg_toast%'
    and nspname not like 'pg_temp_%'";

        const string CUSTOM_TYPE_QUERY =
@"SELECT n.nspname AS schema
     , t.typname AS name
	 , t.typtype
     , pg_catalog.format_type(t.typbasetype, t.typtypmod) AS type
     , not t.typnotnull AS nullable
     --, t.typdefault AS default
FROM   pg_catalog.pg_type t
LEFT   JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace
WHERE  t.typtype = 'd'  -- domains
AND    n.nspname <> 'pg_catalog'
AND    n.nspname <> 'information_schema'
AND    pg_catalog.pg_type_is_visible(t.oid)";


        protected override async Task<Dictionary<string, FieldType>> LoadCustomTypes()
        {
            await foreach (var type in SqlHelper.ReadValuesAsync(_conn, CUSTOM_TYPE_QUERY, ReadCustomType))
            {
                _customTypes.Add(type.Name, type.Type);
            }
            return _customTypes;
        }

        private FieldDefinition ReadCustomType(IDataReader input)
        {
            var name = $"{input.GetString(1)}";
            var type = GetColumnType(input);
            return new FieldDefinition(name, type);
        }

        protected override (StreamDescription table, FieldDefinition column) BuildFieldDefinition(IDataReader reader)
        {
            var table = new StreamDescription(reader.GetString(0), reader.GetString(1));
            var name = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var type = GetColumnType(reader);
            var column = new FieldDefinition(name, type);

            return (table, column);
        }

        protected override (StreamDescription table, string column) BuildPkDefintion(IDataReader reader)
        {
            return (new StreamDescription(reader.GetString(0), reader.GetString(1)), reader.GetString(2));
        }

        private readonly Dictionary<string, FieldType> _customTypes = new();

        private FieldType GetColumnType(IDataReader reader)
        {
            var typeName = reader.GetString(3);
            var formalType = typeName.StartsWith('"');
            if (formalType) {
                if (_customTypes.TryGetValue(typeName[1..^1], out var result)) {
                    return result;
                }
            }
            string? info = null;
            if (typeName.EndsWith(')')) {
                var startPos = typeName.LastIndexOf('(');
                info = typeName[(startPos+1) .. ^1];
                typeName = typeName.Substring(0, startPos);
            }
            var type = GetTagType(typeName);
            var nullable = !reader.GetBoolean(4);
            return new FieldType(type, nullable, CollectionType.None, info);
        }

        private static readonly Dictionary<string, TypeTag> TYPE_MAP = new()
        {
            { "binary", TypeTag.Binary },
            { "bigint", TypeTag.Long },
            { "boolean", TypeTag.Boolean },
            { "bytea", TypeTag.Varbinary },
            { "character", TypeTag.Nchar },
            { "character varying", TypeTag.Nvarchar },
            { "date", TypeTag.Date },
            { "datetime2", TypeTag.VarDateTime },
            { "datetimeoffset", TypeTag.DateTimeTZ },
            { "decimal", TypeTag.Decimal },
            { "float", TypeTag.Float },
            { "image", TypeTag.Blob },
            { "integer", TypeTag.Int },
            { "json", TypeTag.Json},
            { "jsonb", TypeTag.Json },
            { "money", TypeTag.Money },
            { "nchar", TypeTag.Nchar },
            { "ntext", TypeTag.Ntext },
            { "numeric", TypeTag.Numeric },
            { "nvarchar", TypeTag.Nvarchar },
            { "real", TypeTag.Single },
            { "smalldatetime", TypeTag.SmallDateTime },
            { "smallint", TypeTag.Short },
            { "smallmoney", TypeTag.SmallMoney },
            { "text", TypeTag.Ntext },
            { "time without time zone", TypeTag.Time },
            { "timestamp without time zone", TypeTag.DateTime },
            { "tinyint", TypeTag.Byte },
            { "uuid", TypeTag.Guid },
            { "xml", TypeTag.Xml }
        };

        private static TypeTag GetTagType(string v)
        {
            if (TYPE_MAP.TryGetValue(v, out var result)) {
                return result;
            }
            throw new ArgumentException($"Unknown SQL data type '{v}'.");
        }

        const string READ_DEPS =
@"SELECT distinct
  (SELECT nspname FROM pg_namespace WHERE oid=f.relnamespace) AS dependency_schema,
  f.relname AS dependency_table,
  (SELECT nspname FROM pg_namespace WHERE oid=m.relnamespace) AS dependent_schema,
  m.relname AS dependent_table
FROM
  pg_constraint o
LEFT JOIN pg_class f ON f.oid = o.confrelid
LEFT JOIN pg_class m ON m.oid = o.conrelid
WHERE
  o.contype = 'f' AND o.conrelid IN (SELECT oid FROM pg_class c WHERE c.relkind = 'r') and o.conrelid <> o.confrelid
order by f.relname";

        const string TABLE_QUERY =
@"select table_schema, table_name
from information_schema.tables
where table_type = 'BASE TABLE' and table_schema !~ 'pg_' and table_schema != 'information_schema' and table_schema != 'pansynchro'";

        protected override async Task<StreamDescription[][]> BuildStreamDependencies()
        {
            var names = new List<StreamDescription>();
            var deps = new List<KeyValuePair<StreamDescription, StreamDescription>>();
            await foreach (var sd in SqlHelper.ReadValuesAsync(_conn, TABLE_QUERY, r => new StreamDescription(r.GetString(0), r.GetString(1))))
            {
                names.Add(sd);
            }
            await foreach (var pair in SqlHelper.ReadValuesAsync(_conn, READ_DEPS, r => KeyValuePair.Create(new StreamDescription(r.GetString(0), r.GetString(1)), new StreamDescription(r.GetString(2), r.GetString(3)))))
            {
                deps.Add(pair);
            }
            return OrderDeps(names, deps).Reverse().ToArray();
        }
        protected override string GetDistinctCountQuery(string fieldList, string tableName, long threshold)
            => $"select {fieldList} from (select * from {tableName} limit {threshold}) a;";

        protected override FieldDefinition[] AnalyzeCustomTableFields(IDataReader reader)
        {
            var columnschema = ((NpgsqlDataReader)reader).GetColumnSchemaAsync().GetAwaiter().GetResult();
            var table = reader.GetSchemaTable()!;
            var columns = table.Select();
            var fields = columns.Select(BuildFieldDef).ToArray();
            return fields;
        }

        private FieldDefinition BuildFieldDef(DataRow row)
        {
            var name = (string)row["ColumnName"];
            return new FieldDefinition(name, BuildFieldType(row));
        }

        private static FieldType BuildFieldType(DataRow row)
        {
            var typeName = (string)row["DataTypeName"];
            string? info = null;
            if (typeName.EndsWith(')')) {
                var startPos = typeName.LastIndexOf('(');
                info = typeName[(startPos + 1)..^1];
                typeName = typeName.Substring(0, startPos);
            }
            var type = GetTagType(typeName);
            // BUG: https://github.com/npgsql/npgsql/issues/4639
            var nullable = row["AllowDBNull"] is bool b ? b : true;
            return new FieldType(type, nullable, CollectionType.None, info);
        }
    }
}