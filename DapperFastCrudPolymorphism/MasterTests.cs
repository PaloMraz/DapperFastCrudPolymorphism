using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dapper;
using Dapper.FastCrud;
using System.Data;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;

namespace DapperFastCrudPolymorphism
{
  [TestClass]
  public class MasterTests
  {
    private const string LocalDatabaseConnectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;";

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
      OrmConfiguration.DefaultDialect = SqlDialect.MsSql;
      OrmConfiguration.GetDefaultEntityMapping<Base>()
        .SetTableName("TBase")
        .SetProperty(entity => entity.Id, mapping => mapping.SetPrimaryKey());
      OrmConfiguration.GetDefaultEntityMapping<Derived>()
        .SetTableName("TDerived")
        .SetProperty(entity => entity.Id, mapping => mapping.SetPrimaryKey());

      string sql = @"
        if object_id('TBase') is not null
          drop table TBase;
        if object_id('TDerived') is not null
          drop table TDerived;

        create table TBase (Id int not null primary key, Name nvarchar(100) not null);
        create table TDerived (Id int not null primary key, Name nvarchar(100) not null, Name2 nvarchar(100) not null);
        ";

      using (var connection = OpenConnectionAsync().Result)
      {
        using (var command = connection.CreateCommand())
        {
          command.CommandText = sql;
          command.ExecuteNonQuery();
        }
      }
    }


    [TestMethod]
    public async Task CheckPolymorphicInserts()
    {
      Base realBase = new Base() { Id = 1, Name = "Base" };
      Base derivedAsBase = new Derived() { Id = 1, Name = "Derived", Name2 = "Additional derived data" };

      using (var connection = await OpenConnectionAsync())
      {
        await connection.InsertAsync(realBase);
        await connection.InsertAsync(derivedAsBase);
      }

      using (var connection = await OpenConnectionAsync())
      {
        IEnumerable<dynamic> results = await connection.QueryAsync(
          @"select Id, Name, '-' as Name2 from TBase 
            union all 
            select Id, Name, Name2 from TDerived order by Name;");

        Assert.AreEqual(2, results.Count());
        Assert.AreEqual(1, results.ElementAt(0).Id);
        Assert.AreEqual("Base", results.ElementAt(0).Name);
        Assert.AreEqual("-", results.ElementAt(0).Name2);
        Assert.AreEqual(1, results.ElementAt(1).Id);
        Assert.AreEqual("Derived", results.ElementAt(1).Name);
        Assert.AreEqual("Additional derived data", results.ElementAt(1).Name2);
      }
    }


    private static async Task<IDbConnection> OpenConnectionAsync()
    {
      var connection = new System.Data.SqlClient.SqlConnection(LocalDatabaseConnectionString);
      await connection.OpenAsync();
      return connection;
    }
  }

  public static class DapperFastCrudPolymorphicExtensions
  {
    public static async Task InsertAsync(this IDbConnection connection, object entity)
    {
      MethodInfo insertAsyncGenericMethod = typeof(Dapper.FastCrud.DapperExtensions).GetMethod("InsertAsync");
      MethodInfo insertAsyncConstructedMethod = insertAsyncGenericMethod.MakeGenericMethod(entity.GetType());
      Task resultTask = (Task)insertAsyncConstructedMethod.Invoke(obj: null, parameters: new object[] { connection, entity, null });
      await resultTask;
    }
  }

  public class Base
  {
    public int Id { get; set; }
    public string Name { get; set; }
  }


  public class Derived : Base
  {
    public string Name2 { get; set; }
  }
}
