using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;

namespace Pansynchro.PanSQL.Compiler.Test
{
	public class StreamedTests
	{
		private string _inDict = null!;
		private string _outDict = null!;

		[OneTimeSetUp]
		public void SetUp() 
		{
			var inDict = DataDictionary.LoadFromFile("myDataDict.pansync");
			var outDict = DataDictionary.LoadFromFile("outDataDict.pansync");
			_inDict = inDict.ToString().ToCompressedString();
			_outDict = outDict.ToString().ToCompressedString();
		}

		private string FixDicts(string expected) => expected.Replace("$INDICT$", _inDict).Replace("$OUTDICT$", _outDict);

		private const string DUPE_SPEC = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

table types as myDataDict.UserTypes --defines that myDataDict.UserTypes should be loaded into memory
stream users as myDataDict.Users    --defines that myDataDict.Users should be streamed, not loaded into memory
stream users2 as outDataDict.Users   --likewise, the output should be a stream, not buffered

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data from multiple sources.
  All JOINed tables must be declared as table, not stream.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select u.name, u.address, t.name as type
from users u
join types t on u.typeId = t.Id
into users2

-- errors out because users is streamed and can only be used once
select u.name
from users u
into output3

map MyDataDict.Orders to outDataDict.OrderData --renames one stream to another
map MyDataDict.Products to outDataDict.Products with {SKU = name, Vendor = VendorId} --renames fields within a stream

sync myInput to myOutput
";

		[Test]
		public void ParseErrorSpec()
		{
			Assert.Throws<CompilerError>(() => new Compiler().Compile("test", DUPE_SPEC), "The stream 'users' has already been processed in an earlier command.  If it needs to be used multiple times, it should be declared as 'table'.");
		}

		private const string MISSING_FIELD_SPEC = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

table types as myDataDict.UserTypes --defines that myDataDict.UserTypes should be loaded into memory
stream users as myDataDict.Users    --defines that myDataDict.Users should be streamed, not loaded into memory
stream users2 as outDataDict.Users   --likewise, the output should be a stream, not buffered

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data from multiple sources.
  All JOINed tables must be declared as table, not stream.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select u.name, u.address, t.name as type
from users u
join types t on u.typeId = t.Id
into users2

map myDataDict.Orders to outDataDict.OrderData --renames one stream to another
map myDataDict.Products to outDataDict.Products with {SKU = name, Vendor = VendorId} --renames fields within a stream

sync myInput to myOutput
/*At EOF:
  Any streams not mentioned in input or output are auto-mapped to one another by equal names.
  All inputs and outputs are type-checked.  Errors are emitted as needed.
  Warnings are emitted for unused streams
  input streams declared as Table are not auto-mapped.  If manually mapped, transfer will work as expected
  Run codegen */";

		[Test]
		public void ParseMissingFieldSpec()
		{
			Assert.Throws<CompilerError>(() => new Compiler().Compile("test", MISSING_FIELD_SPEC), "The following field(s) on users2 are not nullable, but are not assigned a value: Id");
		}

		private const string CLEAN_SPEC = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

table types as myDataDict.UserTypes --defines that myDataDict.UserTypes should be loaded into memory
stream users as myDataDict.Users    --defines that myDataDict.Users should be streamed, not loaded into memory
stream users2 as outDataDict.Users   --likewise, the output should be a stream, not buffered

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data from multiple sources.
  All JOINed tables must be declared as table, not stream.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select u.id, u.name, u.address, t.name as type
from users u
join types t on u.typeId = t.Id
into users2

map myDataDict.Orders to outDataDict.OrderData --renames one stream to another
map myDataDict.Products to outDataDict.Products with {SKU = name, Vendor = VendorId} --renames fields within a stream

sync myInput to myOutput
/*At EOF:
  Any streams not mentioned in input or output are auto-mapped to one another by equal names.
  All inputs and outputs are type-checked.  Errors are emitted as needed.
  Warnings are emitted for unused streams
  input streams declared as Table are not auto-mapped.  If manually mapped, transfer will work as expected
  Run codegen */";

		private const string CLEAN_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

using NMemory;
using NMemory.Indexes;
using NMemory.Tables;

class Sync : StreamTransformerBase {
	private class DB : Database {
		public class UserTypes_ {
			public int Id { get; }
			public string Name { get; }

			public UserTypes_(IDataReader r) {
				this.Id = r.GetInt32(0);
				this.Name = r.GetString(1);
			}
		}

		public ITable<UserTypes_> UserTypes { get; }
		public IUniqueIndex<UserTypes_, int> UserTypes__Id { get; }

		public DB() {
			NMemory.NMemoryManager.DisableObjectCloning = true;
			UserTypes = Tables.Create<UserTypes_, int>(t => t.Id);
			UserTypes__Id = (IUniqueIndex<UserTypes_, int>)UserTypes.PrimaryKeyIndex;
		}
	}

	private readonly DB __db = new();

	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		while (r.Read()) {
			__db.UserTypes.Insert(new DB.UserTypes_(r));
		}
		yield break;
	}

	private IEnumerable<object?[]> Transformer__2(IDataReader r) {
		var result = new object[6];
		result[4] = DBNull.Value;
		result[5] = DBNull.Value;
		while (r.Read()) {
			var types = __db.UserTypes__Id.GetByUniqueIndex(r.GetInt32(3));
			if (!((types != null))) continue;
			result[0] = r.GetInt32(0);
			result[1] = r.GetString(1);
			result[2] = (r.IsDBNull(2) ? System.DBNull.Value : r.GetString(2));
			result[3] = types.Name;
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""UserTypes"", Transformer__1);
		_streamDict.Add(""Users"", Transformer__2);
		_nameMap.Add(StreamDescription.Parse(""Orders""), StreamDescription.Parse(""OrderData""));
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__3 = myInput.ReadFrom(myDataDict);
		reader__3 = new Sync(outDataDict).Transform(reader__3);
		await myOutput.Sync(reader__3, outDataDict);
	}
}
";

		private const string CLEAN_CSPROJ = @"<Project Sdk=""Microsoft.NET.Sdk"">

	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net8.0</TargetFramework>
		<Nullable>enable</Nullable>
		<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
		<NoWarn>$(NoWarn);CS8621</NoWarn>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include=""NMemory"" Version=""*"" />
		<PackageReference Include=""Pansynchro.Core"" Version=""*"" />
		<PackageReference Include=""Pansynchro.PanSQL.Core"" Version=""*"" />
		<PackageReference Include=""Pansynchro.Connectors.MSSQL"" Version=""*"" />
		<PackageReference Include=""Pansynchro.Connectors.Postgres"" Version=""*"" />
	</ItemGroup>

	<ItemGroup>
		<None Update=""connectors.pansync"">
			<CopyToOutputDirectory>Always</CopyToOutputDirectory>
		</None>
	</ItemGroup>
	
</Project>
";

		private const string CLEAN_CONNECTORS = @"Connectors:
	Connector MSSQL:
		supports Analyzer, Reader, Writer, Configurator, Queryable
		assembly Pansynchro.Connectors.MSSQL
	Connector Postgres:
		supports Analyzer, Reader, Writer, Configurator, Queryable
		assembly Pansynchro.Connectors.Postgres
";

		[Test]
		public void ParseCleanSpec()
		{
			var result = new Compiler().Compile("test", CLEAN_SPEC);
			Assert.Multiple(() => {
				Assert.That(result.Code, Is.EqualTo(FixDicts(CLEAN_OUTPUT)));
				Assert.That(result.ProjectFile, Is.EqualTo(CLEAN_CSPROJ));
				Assert.That(result.Connectors, Is.EqualTo(CLEAN_CONNECTORS));
			});
		}

		private const string NET_SERVER = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Network, associated with outDataDict
open myOutput as Network for write with outDataDict, '127.0.0.1'

map myDataDict.Orders to outDataDict.OrderData --renames one stream to another
map myDataDict.Products to outDataDict.Products with {SKU = name, Vendor = VendorId} --renames fields within a stream

sync myInput to myOutput";

		private const string NET_CLIENT = @"
load myDataDict from '.\outDataDict.pansync'

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as Network for read with myDataDict, '127.0.0.1'

--opens a writer of type Network, associated with outDataDict
open myOutput as Postgres for write with myDataDict, 'Connection string here'

sync myInput to myOutput";

		private const string NETWORK_RESULT_0 = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	public Sync(DataDictionary destDict) : base(destDict) {
		_nameMap.Add(StreamDescription.Parse(""Orders""), StreamDescription.Parse(""OrderData""));
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var filename__1 = System.IO.Path.GetTempFileName();
		outDataDict.SaveToFile(filename__1);
		var myOutput = ConnectorRegistry.GetWriter(""Network"", ""127.0.0.1;"" + filename__1);
		var reader__2 = myInput.ReadFrom(myDataDict);
		reader__2 = new Sync(outDataDict).Transform(reader__2);
		await myOutput.Sync(reader__2, outDataDict);
	}
}
";
		private const string NETWORK_RESULT_1 = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var filename__1 = System.IO.Path.GetTempFileName();
		myDataDict.SaveToFile(filename__1);
		var myInput = ConnectorRegistry.GetReader(""Network"", ""127.0.0.1;"" + filename__1);
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", ""Connection string here"");
		var reader__2 = myInput.ReadFrom(myDataDict);
		await myOutput.Sync(reader__2, myDataDict);
	}
}
";

		[Test]
		public void ParseNetworkSync()
		{
			var t1 = Path.GetTempFileName();
			var t2 = Path.GetTempFileName();
			Script[] result;
			try {
				File.WriteAllText(t1, NET_SERVER);
				File.WriteAllText(t2, NET_CLIENT);
				File.Copy("myDataDict.pansync", Path.Combine(Path.GetDirectoryName(t1)!, "myDataDict.pansync"), true);
				File.Copy("outDataDict.pansync", Path.Combine(Path.GetDirectoryName(t1)!, "outDataDict.pansync"), true);
				result = new Compiler().CompileFiles(Environment.CurrentDirectory, t1, t2);
			} finally {
				File.Delete(t1);
				File.Delete(t2);
			}
			Assert.Multiple(() => {
				Assert.That(result.Length, Is.EqualTo(2));
				Assert.That(result[0].Code, Is.EqualTo(FixDicts(NETWORK_RESULT_0)));
				Assert.That(result[1].Code, Is.EqualTo(FixDicts(NETWORK_RESULT_1)));
			});
		}

		private const string FILTERED_INT = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream products as myDataDict.Products    --defines that myDataDict.Products should be streamed, not loaded into memory
stream products2 as outDataDict.Products

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data, filtering it by vendor.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select p.id, p.SKU as name, p.Vendor VendorID, p.Price
from products p
where p.Vendor = 1
into products2

sync myInput to myOutput";

		private const string FILTERED_INT_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		var result = new object[4];
		while (r.Read()) {
			if (!((r.GetInt32(2) == 1))) continue;
			result[0] = r.GetInt32(0);
			result[1] = r.GetString(1);
			result[2] = r.GetInt32(2);
			result[3] = r.GetDecimal(3);
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""Products"", Transformer__1);
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__2 = myInput.ReadFrom(myDataDict);
		reader__2 = new Sync(outDataDict).Transform(reader__2);
		await myOutput.Sync(reader__2, outDataDict);
	}
}
";

		[Test]
		public void ParseFilteredInt()
		{
			var result = new Compiler().Compile("test", FILTERED_INT);
			Assert.That(result.Code, Is.EqualTo(FixDicts(FILTERED_INT_OUTPUT)));
		}

		private const string FILTERED_STR = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream products as myDataDict.Products    --defines that myDataDict.Products should be streamed, not loaded into memory
stream products2 as outDataDict.Products

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data, filtering it by vendor.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select p.id, p.SKU as name, p.Vendor VendorID, p.Price
from products p
where p.SKU = 'ABC123'
into products2

sync myInput to myOutput";

		private const string FILTERED_STR_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		var result = new object[4];
		while (r.Read()) {
			if (!((r.GetString(1) == ""ABC123""))) continue;
			result[0] = r.GetInt32(0);
			result[1] = r.GetString(1);
			result[2] = r.GetInt32(2);
			result[3] = r.GetDecimal(3);
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""Products"", Transformer__1);
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__2 = myInput.ReadFrom(myDataDict);
		reader__2 = new Sync(outDataDict).Transform(reader__2);
		await myOutput.Sync(reader__2, outDataDict);
	}
}
"; 
		[Test]
		public void ParseFilteredStr()
		{
			var result = new Compiler().Compile("test", FILTERED_STR);
			Assert.That(result.Code, Is.EqualTo(FixDicts(FILTERED_STR_OUTPUT)));
		}

		private const string GROUPED = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream products as myDataDict.Products    --defines that myDataDict.Products should be streamed, not loaded into memory
stream result as outDataDict.ProductMax

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data, filtering it by vendor.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select p.Vendor VendorID, max(p.Price)
from products p
group by Vendor
into result

sync myInput to myOutput";

		private const string GROUPED_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		var result = new object[2];
		var aggregator__2 = Aggregates.Max<int, decimal>();
		while (r.Read()) {
			aggregator__2.Add(r.GetInt32(2), r.GetDecimal(3));
		}
		foreach (var pair in aggregator__2) {
			result[0] = pair.Key;
			result[1] = pair.Value;
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""Products"", Transformer__1);
		_nameMap.Add(StreamDescription.Parse(""Products""), StreamDescription.Parse(""ProductMax""));
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__3 = myInput.ReadFrom(myDataDict);
		reader__3 = new Sync(outDataDict).Transform(reader__3);
		await myOutput.Sync(reader__3, outDataDict);
	}
}
";

		[Test]
		public void ParseGrouped() 
		{
			var result = new Compiler().Compile("test", GROUPED);
			Assert.That(result.Code, Is.EqualTo(FixDicts(GROUPED_OUTPUT)));
		}

		private const string COUNTED = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream products as myDataDict.Products    --defines that myDataDict.Products should be streamed, not loaded into memory
stream result as outDataDict.ProductCount

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data, filtering it by vendor.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select p.Vendor VendorID, count(*) Quantity
from products p
group by Vendor
into result

sync myInput to myOutput";

		private const string COUNTED_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		var result = new object[2];
		var aggregator__2 = Aggregates.Count<int>();
		while (r.Read()) {
			aggregator__2.Add(r.GetInt32(2));
		}
		foreach (var pair in aggregator__2) {
			result[0] = pair.Key;
			result[1] = pair.Value;
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""Products"", Transformer__1);
		_nameMap.Add(StreamDescription.Parse(""Products""), StreamDescription.Parse(""ProductCount""));
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__3 = myInput.ReadFrom(myDataDict);
		reader__3 = new Sync(outDataDict).Transform(reader__3);
		await myOutput.Sync(reader__3, outDataDict);
	}
}
";

		[Test]
		public void ParseCounted()
		{
			var result = new Compiler().Compile("test", COUNTED);
			Assert.That(result.Code, Is.EqualTo(FixDicts(COUNTED_OUTPUT)));
		}

		private const string HAVING = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream products as myDataDict.Products    --defines that myDataDict.Products should be streamed, not loaded into memory
stream result as outDataDict.ProductCount

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data, filtering it by vendor.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select p.Vendor VendorID, count(*) Quantity
from products p
group by Vendor
having count(*) > 5
into result

sync myInput to myOutput";

		private const string HAVING_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		var result = new object[2];
		var aggregator__2 = Aggregates.Count<int>();
		while (r.Read()) {
			aggregator__2.Add(r.GetInt32(2));
		}
		foreach (var pair in aggregator__2) {
			if (!(pair.Value > 5)) continue;
			result[0] = pair.Key;
			result[1] = pair.Value;
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""Products"", Transformer__1);
		_nameMap.Add(StreamDescription.Parse(""Products""), StreamDescription.Parse(""ProductCount""));
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__3 = myInput.ReadFrom(myDataDict);
		reader__3 = new Sync(outDataDict).Transform(reader__3);
		await myOutput.Sync(reader__3, outDataDict);
	}
}
";

		[Test]
		public void ParseHaving()
		{
			var result = new Compiler().Compile("test", HAVING);
			Assert.That(result.Code, Is.EqualTo(FixDicts(HAVING_OUTPUT)));
		}

		private const string GROUPED2 = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream products as myDataDict.Products    --defines that myDataDict.Products should be streamed, not loaded into memory
stream result as outDataDict.ProductMaxAndCount

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data, filtering it by vendor.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select p.Vendor VendorID, max(p.Price), count(p.Price) Quantity
from products p
group by Vendor
into result

sync myInput to myOutput";

		private const string GROUPED2_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		var result = new object[3];
		var aggregator__2 = Aggregates.Max<int, decimal>();
		var aggregator__3 = Aggregates.Count<int>();
		while (r.Read()) {
			aggregator__2.Add(r.GetInt32(2), r.GetDecimal(3));
			aggregator__3.Add(r.GetInt32(2));
		}
		foreach (var pair in Aggregates.Combine(aggregator__2, aggregator__3)) {
			result[0] = pair.Key;
			result[1] = pair.Value.Item2;
			result[2] = pair.Value.Item1;
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""Products"", Transformer__1);
		_nameMap.Add(StreamDescription.Parse(""Products""), StreamDescription.Parse(""ProductMaxAndCount""));
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__4 = myInput.ReadFrom(myDataDict);
		reader__4 = new Sync(outDataDict).Transform(reader__4);
		await myOutput.Sync(reader__4, outDataDict);
	}
}
";

		[Test]
		public void ParseGrouped2()
		{
			var result = new Compiler().Compile("test", GROUPED2);
			Assert.That(result.Code, Is.EqualTo(FixDicts(GROUPED2_OUTPUT)));
		}

		private const string LITERAL1 = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream products as myDataDict.Products    --defines that myDataDict.Products should be streamed, not loaded into memory
stream result as outDataDict.ProductMaxAndCount

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data, filtering it by vendor.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select p.Vendor VendorID, max(p.Price), 10 Quantity
from products p
group by Vendor
into result

sync myInput to myOutput";

		private const string LITERAL1_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		var result = new object[3];
		var aggregator__2 = Aggregates.Max<int, decimal>();
		while (r.Read()) {
			aggregator__2.Add(r.GetInt32(2), r.GetDecimal(3));
		}
		result[1] = 10;
		foreach (var pair in aggregator__2) {
			result[0] = pair.Key;
			result[2] = pair.Value;
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""Products"", Transformer__1);
		_nameMap.Add(StreamDescription.Parse(""Products""), StreamDescription.Parse(""ProductMaxAndCount""));
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__3 = myInput.ReadFrom(myDataDict);
		reader__3 = new Sync(outDataDict).Transform(reader__3);
		await myOutput.Sync(reader__3, outDataDict);
	}
}
"; 

		[Test]
		public void ParseLiteral1()
		{
			var result = new Compiler().Compile("test", LITERAL1);
			Assert.That(result.Code, Is.EqualTo(FixDicts(LITERAL1_OUTPUT)));
		}

		private const string LITERAL2 = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream users as myDataDict.Users    --defines that myDataDict.Users should be streamed, not loaded into memory
stream users2 as outDataDict.Users   --likewise, the output should be a stream, not buffered

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data from multiple sources.
  All JOINed tables must be declared as table, not stream.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select u.id, u.name, u.address, 'NONE' as type, null
from users u
into users2

map myDataDict.Orders to outDataDict.OrderData --renames one stream to another
map myDataDict.Products to outDataDict.Products with {SKU = name, Vendor = VendorId} --renames fields within a stream

sync myInput to myOutput
/*At EOF:
  Any streams not mentioned in input or output are auto-mapped to one another by equal names.
  All inputs and outputs are type-checked.  Errors are emitted as needed.
  Warnings are emitted for unused streams
  input streams declared as Table are not auto-mapped.  If manually mapped, transfer will work as expected
  Run codegen */";

		private const string LITERAL2_OUTPUT = @"using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;
using Pansynchro.Core.DataDict;
using Pansynchro.PanSQL.Core;
using static Pansynchro.PanSQL.Core.Credentials;

class Sync : StreamTransformerBase {
	private IEnumerable<object?[]> Transformer__1(IDataReader r) {
		var result = new object[6];
		result[3] = ""NONE"";
		result[4] = DBNull.Value;
		result[5] = DBNull.Value;
		while (r.Read()) {
			result[0] = r.GetInt32(0);
			result[1] = r.GetString(1);
			result[2] = (r.IsDBNull(2) ? System.DBNull.Value : r.GetString(2));
			yield return result;
		}
	}

	public Sync(DataDictionary destDict) : base(destDict) {
		_streamDict.Add(""Users"", Transformer__1);
		_nameMap.Add(StreamDescription.Parse(""Orders""), StreamDescription.Parse(""OrderData""));
	}
}

static class Program {
	public static async Task Main() {
		var myDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$INDICT$""));
		var outDataDict = DataDictionaryWriter.Parse(CompressionHelper.Decompress(""$OUTDICT$""));
		var myInput = ConnectorRegistry.GetReader(""MSSQL"", ""connection string here"");
		var myOutput = ConnectorRegistry.GetWriter(""Postgres"", CredentialsFromEnv(""PostgresConnectionString""));
		var reader__2 = myInput.ReadFrom(myDataDict);
		reader__2 = new Sync(outDataDict).Transform(reader__2);
		await myOutput.Sync(reader__2, outDataDict);
	}
}
";

		[Test]
		public void ParseLiteral2()
		{
			var result = new Compiler().Compile("test", LITERAL2);
			Assert.That(result.Code, Is.EqualTo(FixDicts(LITERAL2_OUTPUT)));
		}

		private const string ORDERED = @"
load myDataDict from '.\myDataDict.pansync' --loads the file into a variable named myDataDict
load outDataDict from '.\outDataDict.pansync' --loads the file into a variable named outDataDict

stream products as myDataDict.Products    --defines that myDataDict.Products should be streamed, not loaded into memory
stream products2 as outDataDict.Products

--opens a reader of type MsSql with the provided connection string, associated with myDataDict
open myInput as MsSql for read with myDataDict, 'connection string here'

--opens a writer of type Postgres with a stored connection string retrieved from an external source, associated with outDataDict
open myOutput as Postgres for write with outDataDict, CredentialsFromEnv('PostgresConnectionString')

/*Uses a SQL query to process data, filtering it by vendor.
  Must end with an ""into"" clause referencing a previously-defined table or stream.
  Output is type-checked, and will error if it doesn't match the defined table.
*/
select p.id, p.SKU as name, p.Vendor VendorID, p.Price
from products p
order by p.Vendor
into products2

sync myInput to myOutput";

		[Test]
		public void DisallowOrdering()
		{
			Assert.Throws<CompilerError>(() => new Compiler().Compile("test", ORDERED), "ORDER BY is not supported for queries involving a STREAM input.");
		}
	}
}